using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using AutoLogin.Interop;
using AutoLogin.Models;

namespace AutoLogin.Services
{
    /// <summary>
    /// 精确控件模式的全部实现：枚举登录框控件、静默写入、读回验证、点名提交。
    ///
    /// 安全铁律：账密读回验证不通过时绝不发送任何按键/点击，绝不触碰“取消”类按钮。
    /// </summary>
    internal sealed class LoginAutomation
    {
        private struct EditFound
        {
            public IntPtr Handle;
            public int Ord;
        }

        private struct ButtonFound
        {
            public IntPtr Handle;
            public string Title;
        }

        private readonly AppConfig _config;
        private readonly ILogService _log;

        private List<EditFound> _foundEdits = new List<EditFound>();
        private IntPtr _foundPasswordEdit = IntPtr.Zero;
        private List<ButtonFound> _foundOkButtons = new List<ButtonFound>();
        private List<ButtonFound> _foundCancelButtons = new List<ButtonFound>();

        public LoginAutomation(AppConfig config, ILogService log)
        {
            _config = config;
            _log = log;
        }

        /// <summary>
        /// 对一个候选窗口执行：激活（焦点铁律）→ 自动填写账密 → 提交。
        /// 返回 true 表示已完成输入；summary=="locked" 表示被另一实例占用。
        /// </summary>
        public bool TryTypeOn(WindowInfo candidate, out string summary)
        {
            summary = null;
            _log.Info("尝试激活候选:0x" + candidate.Handle.ToString("X") +
                      " 标题=~" + candidate.Title + "~(PID=" + candidate.Pid + ")");

            Thread.Sleep(Math.Max(0, _config.ReadyDelayMs));

            if (!WindowLocator.FocusWindow(candidate.Handle, _log))
            {
                _log.Warning("焦点确认失败,放弃该候选换下一个。");
                summary = "焦点确认失败";
                return false;
            }

            Thread.Sleep(600);

            bool createdNew;
            Mutex mutex = new Mutex(true, "Local\\AutoLogin_Typing", out createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                summary = "locked";
                return false;
            }

            try
            {
                string body;
                bool ok = TryTypeOnBody(candidate, out body);
                summary = body;
                return ok;
            }
            finally
            {
                try { mutex.ReleaseMutex(); } catch { }
                mutex.Dispose();
            }
        }

        private bool TryTypeOnBody(WindowInfo candidate, out string summary)
        {
            summary = null;

            string detail;
            if (FillByControls(candidate, out detail))
            {
                summary = detail;
                return true;
            }

            if (detail == "controls-not-found")
            {
                _log.Info("该窗口未识别到标准输入框控件,走回退键盘模式(激活窗口→打字→TAB→密码→回车)。");
                _log.Info("若现场仍出现打不进去/点错按钮,请把 AutoLogin.log 全文发给维护者(日志已记录控件清单)。");
                FallbackTypeOn(candidate);
                summary = "输入完成(回退键盘模式:账号+TAB+密码+回车)。窗口=0x" +
                          candidate.Handle.ToString("X") + " 标题=~" + candidate.Title + "~";
                return true;
            }

            summary = detail;
            return false;
        }

        /// <summary>
        /// 枚举登录窗口的控件：输入框（EDIT 类）按样式区分密码框；按钮按标题分“确定/取消”。
        /// </summary>
        private void EnumLoginFormControls(IntPtr parent)
        {
            _foundEdits = new List<EditFound>();
            _foundPasswordEdit = IntPtr.Zero;
            _foundOkButtons = new List<ButtonFound>();
            _foundCancelButtons = new List<ButtonFound>();

            NativeMethods.EnumWindowsProc callback = delegate(IntPtr h, IntPtr lp)
            {
                if (!NativeMethods.IsWindowVisible(h) || !NativeMethods.IsWindowEnabled(h)) return true;

                string cls = NativeMethods.GetClassNameOf(h);
                if (string.IsNullOrEmpty(cls)) return true;

                string lowerClass = cls.ToLowerInvariant();

                if (lowerClass.Contains("edit"))
                {
                    int style = NativeMethods.GetWindowStyle(h);
                    if ((style & NativeMethods.ES_PASSWORD) != 0)
                    {
                        if (_foundPasswordEdit == IntPtr.Zero) _foundPasswordEdit = h;
                    }
                    else
                    {
                        EditFound edit = new EditFound();
                        edit.Handle = h;
                        edit.Ord = _foundEdits.Count + 1;
                        _foundEdits.Add(edit);
                    }
                    return true;
                }

                if (lowerClass.Contains("button"))
                {
                    string caption = NativeMethods.GetControlText(h);
                    if (string.IsNullOrEmpty(caption)) return true;

                    // 标题去空格标准化：“登 录”/“确 定”这类带空格的按钮也能命中
                    string normalized = caption.Replace(" ", "").Replace("　", "").ToLowerInvariant();

                    if (normalized.Contains("取消") || normalized.Contains("cancel") || normalized.Contains("关闭"))
                    {
                        ButtonFound cancel = new ButtonFound();
                        cancel.Handle = h;
                        cancel.Title = caption;
                        _foundCancelButtons.Add(cancel);
                        return true;
                    }

                    if (normalized.Contains("确定") || normalized.Contains("ok") ||
                        normalized.Contains("登录") || normalized.Contains("登陆") ||
                        normalized.Contains("log in") || normalized.Contains("login"))
                    {
                        ButtonFound ok = new ButtonFound();
                        ok.Handle = h;
                        ok.Title = caption;
                        _foundOkButtons.Add(ok);
                    }
                }

                return true;
            };

            NativeMethods.EnumChildWindows(parent, callback, IntPtr.Zero);
            GC.KeepAlive(callback);
        }

        /// <summary>
        /// 精确控件模式主流程。返回 true=已提交；summary=描述/失败原因。
        /// </summary>
        private bool FillByControls(WindowInfo candidate, out string summary)
        {
            summary = null;
            EnumLoginFormControls(candidate.Handle);

            if (_foundEdits.Count == 0 && _foundPasswordEdit == IntPtr.Zero)
            {
                _log.Info("精确模式放弃:EDIT 控件数=0(窗口可能为自绘控件)。");
                summary = "controls-not-found";
                return false;
            }

            if (_foundEdits.Count == 0 || _foundPasswordEdit == IntPtr.Zero)
            {
                return FillMixed(candidate, out summary);
            }

            int wantedOrd = Math.Max(1, _config.UserEditOrd);
            IntPtr userEdit = IntPtr.Zero;
            int userOrd = _foundEdits[0].Ord;
            foreach (EditFound edit in _foundEdits)
            {
                if (edit.Ord == wantedOrd)
                {
                    userEdit = edit.Handle;
                    userOrd = edit.Ord;
                    break;
                }
            }
            if (userEdit == IntPtr.Zero) userEdit = _foundEdits[0].Handle;

            _log.Info("精确控件模式:用户名框=第 " + userOrd + " 个 0x" + userEdit.ToString("X") +
                      " / 密码框 0x" + _foundPasswordEdit.ToString("X") +
                      " / 确定按钮 " + _foundOkButtons.Count + " 个 / 取消按钮 " + _foundCancelButtons.Count + " 个");
            LogEditList();

            bool userOk = WriteToEdit(userEdit, _config.UserName, false);
            bool passOk = WriteToEdit(_foundPasswordEdit, _config.Password, true);

            if (!userOk || !passOk)
            {
                _log.Error("铁律:账密读回验证未通过 => 不发送回车/点击,中止!用户名OK=" + userOk + " 密码OK=" + passOk);
                summary = "账密验证未通过(已中止提交)";
                return false;
            }

            return SubmitLogin(out summary);
        }

        /// <summary>
        /// 两类输入框只识别到其一时的混合模式：能精确的精确，其余用键盘，但仍验证守门。
        /// </summary>
        private bool FillMixed(WindowInfo candidate, out string summary)
        {
            summary = null;

            if (_foundEdits.Count > 0)
            {
                IntPtr userEdit = _foundEdits[0].Handle;
                int wantedOrd = Math.Max(1, _config.UserEditOrd);
                foreach (EditFound edit in _foundEdits)
                {
                    if (edit.Ord == wantedOrd)
                    {
                        userEdit = edit.Handle;
                        break;
                    }
                }

                _log.Info("混合模式:用户名框 0x" + userEdit.ToString("X") +
                          " 精确写入;密码框未按样式识别(将其 TAB 后键盘输入)。");
                LogEditList();

                if (!WriteToEdit(userEdit, _config.UserName, false))
                {
                    _log.Error("铁律:用户名读回验证未通过 => 不发送任何后续按键。");
                    summary = "用户名验证未通过(已中止提交)";
                    return false;
                }

                KeyboardInput.SendKey(NativeMethods.VK_TAB);
                Thread.Sleep(250);
                bool typed = KeyboardInput.SendText(_config.Password, _config.TypeDelayMs, _log);
                _log.Info("混合模式:密码键盘输入=" + (typed ? "OK" : "失败"));
                if (!typed)
                {
                    summary = "密码键盘输入失败(已中止提交)";
                    return false;
                }

                return SubmitLogin(out summary);
            }

            if (_foundPasswordEdit != IntPtr.Zero)
            {
                _log.Info("混合模式:仅识别到密码框 0x" + _foundPasswordEdit.ToString("X") +
                          ",先写密码,用户名靠 TAB 移焦点键盘输入。");

                bool passOk = WriteToEdit(_foundPasswordEdit, _config.Password, true);
                if (!passOk)
                {
                    _log.Error("铁律:密码读回验证未通过 => 不发送任何后续按键。");
                    summary = "密码验证未通过(已中止提交)";
                    return false;
                }

                KeyboardInput.SendKey(NativeMethods.VK_TAB);
                Thread.Sleep(250);
                bool typed = KeyboardInput.SendText(_config.UserName, _config.TypeDelayMs, _log);
                _log.Info("混合模式:用户名键盘输入=" + (typed ? "OK" : "失败"));
                if (!typed)
                {
                    summary = "用户名键盘输入失败(已中止提交)";
                    return false;
                }

                return SubmitLogin(out summary);
            }

            summary = "controls-not-found";
            return false;
        }

        /// <summary>
        /// 单个输入框写入：先 WM_SETTEXT 静默写入 + 读回验证；不通过则焦点钉住该控件后键盘输入 + 再验证。
        /// </summary>
        private bool WriteToEdit(IntPtr hWnd, string text, bool isPassword)
        {
            // 输入框若属于下拉框（ComboBox），直接对下拉框本体写文本，保证下拉框托管状态同步
            IntPtr target = hWnd;
            IntPtr parent = NativeMethods.GetParent(hWnd);
            if (parent != IntPtr.Zero &&
                NativeMethods.GetClassNameOf(parent).ToLowerInvariant().Contains("combobox"))
            {
                target = parent;
                _log.Info("用户名输入框属于下拉框(ComboBox),改为对下拉框本体写文本。");
            }

            string name = isPassword ? "密码框" : "用户名框";
            bool setRet = NativeMethods.SetControlText(target, text);
            Thread.Sleep(150);
            bool ok = VerifyEdit(target, text, isPassword);
            _log.Info(name + " WM_SETTEXT 调用=" + (setRet ? "成功" : "失败") +
                      ",读回验证 " + (ok ? "通过" : "未通过"));
            if (ok) return true;

            _log.Info(name + " 转键盘输入:先把焦点钉到该输入框...");
            if (!FocusControlDirect(hWnd))
            {
                _log.Warning(name + " 焦点钉入失败。");
                return false;
            }

            Thread.Sleep(150);
            NativeMethods.SetControlText(hWnd, ""); // 键盘输入前清掉任何预填/半写内容
            Thread.Sleep(80);

            // 焐热键盘/输入法链路（防中文首字被吞）
            KeyboardInput.SendText("q", _config.TypeDelayMs, _log);
            Thread.Sleep(150);
            KeyboardInput.SendKey(NativeMethods.VK_BACK);
            Thread.Sleep(100);
            KeyboardInput.SendKey(NativeMethods.VK_BACK);
            Thread.Sleep(150);

            bool typed = KeyboardInput.SendText(text, _config.TypeDelayMs, _log);
            Thread.Sleep(150);
            bool ok2 = VerifyEdit(hWnd, text, isPassword);
            _log.Info(name + " 键盘输入=" + (typed ? "OK" : "失败") +
                      ",读回验证 " + (ok2 ? "通过" : "未通过"));
            return ok2;
        }

        /// <summary>
        /// 读回验证：普通框必须完全一致；密码框按“掩码长度=明文长度”判定。
        /// </summary>
        private bool VerifyEdit(IntPtr hWnd, string text, bool isPassword)
        {
            if (isPassword)
            {
                int len = NativeMethods.GetControlTextLength(hWnd);
                if (len == text.Length) return true;
                _log.Warning("密码框读回长度不符:实际=" + len + " 期望=" + text.Length);
                return false;
            }

            string got = NativeMethods.GetControlText(hWnd);
            if (got == text) return true;
            _log.Warning("用户名框读回不符:实际=~" + got + "~ 期望=~" + text + "~");
            return false;
        }

        /// <summary>
        /// 提交：有“确定/登录/OK”按钮就点它（从机制上不许点取消）；找不到按钮才回车兜底。
        /// </summary>
        private bool SubmitLogin(out string summary)
        {
            summary = null;

            if (_foundOkButtons.Count > 0)
            {
                IntPtr button = _foundOkButtons[0].Handle;
                string caption = _foundOkButtons[0].Title;
                _log.Info("提交:向按钮 ~" + caption + "~(0x" + button.ToString("X") + ")发送点击消息。");
                NativeMethods.PostMessage(button, NativeMethods.BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                summary = "输入完成(精确控件模式,已点击按钮~" + caption + "~)。";
                return true;
            }

            _log.Info("提交:窗口内未见确定/登录按钮,发送回车。");
            KeyboardInput.SendKey(NativeMethods.VK_RETURN);
            summary = "输入完成(精确控件模式,回车提交)。";
            return true;
        }

        /// <summary>
        /// 回退键盘模式（老姿势）：所有键交给窗体，适用于自绘控件等非标准登录框。
        /// </summary>
        private void FallbackTypeOn(WindowInfo candidate)
        {
            // 中文账号首字常被 IME 热身吞掉：先打占位字符焐热，再连退两格清干净
            KeyboardInput.SendText("q", _config.TypeDelayMs, _log);
            Thread.Sleep(200);
            KeyboardInput.SendKey(NativeMethods.VK_BACK);
            Thread.Sleep(150);
            KeyboardInput.SendKey(NativeMethods.VK_BACK);
            Thread.Sleep(200);

            _log.Info("开始输入账号密码(回退模式,无法逐框验证)...");
            KeyboardInput.SendText(_config.UserName, _config.TypeDelayMs, _log);
            Thread.Sleep(150);
            KeyboardInput.SendKey(NativeMethods.VK_TAB);
            Thread.Sleep(150);
            KeyboardInput.SendText(_config.Password, _config.TypeDelayMs, _log);
            Thread.Sleep(200);
            KeyboardInput.SendKey(NativeMethods.VK_RETURN);
        }

        /// <summary>
        /// 把系统键盘焦点钉到指定控件（跨进程：AttachThreadInput + SetFocus + GetFocus 确认）。
        /// </summary>
        private bool FocusControlDirect(IntPtr hWnd)
        {
            uint pidDummy;
            uint targetThread = NativeMethods.GetWindowThreadProcessId(hWnd, out pidDummy);
            uint selfThread = NativeMethods.GetCurrentThreadId();

            if (targetThread == selfThread)
            {
                NativeMethods.SetFocus(hWnd);
                return NativeMethods.GetFocus() == hWnd;
            }

            if (!NativeMethods.AttachThreadInput(selfThread, targetThread, true))
            {
                _log.Warning("AttachThreadInput 失败:0x" + hWnd.ToString("X"));
                return false;
            }

            NativeMethods.SetFocus(hWnd);
            bool ok = NativeMethods.GetFocus() == hWnd;
            NativeMethods.AttachThreadInput(selfThread, targetThread, false);
            return ok;
        }

        private void LogEditList()
        {
            foreach (EditFound edit in _foundEdits)
            {
                _log.Info("  EDIT#" + edit.Ord + " 0x" + edit.Handle.ToString("X") +
                          " 类名=" + NativeMethods.GetClassNameOf(edit.Handle) +
                          " 当前文本=~" + NativeMethods.GetControlText(edit.Handle) + "~");
            }
        }
    }
}
