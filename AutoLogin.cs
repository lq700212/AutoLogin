// ============================================================================
//  AutoLogin.exe —— 开机自启动并自动登录上位机软件 (v5)
//  v5 核心架构:进程树绑定优先
//   1) ExePath 若为 .exe/.lnk,Process.Start 会返回启动进程 => 以该 PID 为根,
//      连同全部子孙进程绑定在一起,只有"属于这棵进程树"的可见窗口才算登录框。
//      同机其他软件弹的窗口统统不会误伤。
//   2) ExePath 若为 .ezd 等工程文件:Process.Start 可能拿不到进程 => 先尝试
//      读取系统文件关联解开真实 exe 再找;仍不行才降级"全局新窗口兜底"并写日志。
//   3) 焦点铁律:任何窗口只有 SetForegroundWindow 确认成功才打字。
//   4) 窗口选定前要求"候选集合连续稳定"若干轮,防止 splash 抢跑。
//  配置见同目录 AutoLogin.ini,部署说明见 README.md
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AutoLogin
{
    internal static class Program
    {
        // ---------------- 配置默认值(可被 AutoLogin.ini 覆盖) ----------------
        internal static string _exePath = "";
        internal static string _userName = "操作员";
        internal static string _password = "123456";
        internal static string _titleKeyword = "";     // 登录窗口标题关键词,留空=自动识别
        internal static int _waitWindowSec = 120;      // 等待登录框出现的最长时间
        internal static int _readyDelayMs = 3000;      // 窗口出现后再等多少毫秒才输入
        internal static int _typeDelayMs = 30;         // 逐字符间隔毫秒
        internal static bool _msgBoxOnFail = true;     // 失败时是否弹窗提示
        internal static bool _retryOnStillOpen = false;// 提交后登录框未关闭是否重试(默认关,防误打主界面)
        internal static int _userEditOrd = 1;          // 用户名框序号:第 N 个非密码输入框(默认 1,现场认错框时改 ini 调整)

        // 精确控件模式:枚举到的登录框子控件结果
        private struct EditFound { public IntPtr H; public int Ord; }
        private struct BtnFound { public IntPtr H; public string Title; }
        private static List<EditFound> _foundEdits;
        private static IntPtr _foundPwEdit;
        private static List<BtnFound> _foundOkBtns;
        private static List<BtnFound> _foundCancelBtns;

        // 内部常量(不在 ini 暴露)
        private const int STABLE_HOLD_ROUNDS = 3;     // 候选集合连续稳定多少轮才选定(每轮 400ms)
        private const int ROOT_LOST_SEC = 5;          // 根进程消失多少秒后从关联 exe 重新认根

        internal static string _baseDir;
        internal static string _logPath;
        internal static string _iniPath;
        internal static int _curPid;
        private static string _assocBaseName;         // 工程文件关联的真 exe 文件名(小写,含扩展名)
        private static bool _globalFallback;          // 全局兜底模式标识

        [STAThread]
        private static void Main(string[] args)
        {
            // 命令行含 /auto|/run|/silent(不区分 - 与大小写) => 开机自启动静默模式
            // 无参数 => 打开可视化配置界面
            bool silent = false;
            if (args != null)
            {
                foreach (string a in args)
                {
                    string t = (a == null ? "" : a.ToLowerInvariant());
                    if (t == "/auto" || t == "-auto" || t == "/run" || t == "-run" || t == "/silent" || t == "-silent")
                    {
                        silent = true;
                        break;
                    }
                }
            }

            if (!silent)
            {
                try { AutoLoginGui.RunGui(); }
                catch (Exception ex)
                {
                    Log("配置界面发生异常:" + ex.Message);
                    Environment.ExitCode = 1;
                }
                return;
            }

            try { Run(); }
            catch (Exception ex)
            {
                Log("发生异常,请把本日志发给维护人员:" + ex.Message);
                PopMsg("自动登录发生异常:" + ex.Message);
                Environment.ExitCode = 1;
            }
        }

        // 初始化路径/进程常量(主流程与配置界面共用)
        internal static void InitPaths()
        {
            _baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _logPath = System.IO.Path.Combine(_baseDir, "AutoLogin.log");
            _iniPath = System.IO.Path.Combine(_baseDir, "AutoLogin.ini");
            _curPid = Process.GetCurrentProcess().Id;
        }

        // ---------------- 主流程 ----------------
        internal static void Run()
        {
            InitPaths();

            try
            {
                var fi = new System.IO.FileInfo(_logPath);
                if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
            }
            catch { }

            Log("========== AutoLogin 启动 ==========");

            LoadIni();
            LogConfig();

            if (string.IsNullOrEmpty(_exePath))
            {
                Log("失败:AutoLogin.ini 中 ExePath 为空,请填写软件 exe/快捷方式/工程文件的完整路径。");
                PopMsg("配置错误:ExePath 为空,请编辑 AutoLogin.ini");
                Environment.ExitCode = 1;
                return;
            }
            if (!System.IO.File.Exists(_exePath))
            {
                Log("失败:ExePath 指向的文件不存在:" + _exePath);
                Log("请检查盘符/文件夹路径是否与工控机一致。");
                PopMsg("找不到软件:\r\n" + _exePath + "\r\n请核对 AutoLogin.ini 的 ExePath");
                Environment.ExitCode = 1;
                return;
            }

            // 快速方式解析目标(仅记录;工程文件用系统关联猜出真 exe,用于进程认根)
            if (string.Equals(System.IO.Path.GetExtension(_exePath), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string target = ResolveLnk(_exePath);
                if (!string.IsNullOrEmpty(target))
                {
                    Log("快捷方式目标(仅记录):" + target);
                    _assocBaseName = System.IO.Path.GetFileName(target).ToLowerInvariant();
                }
            }
            else if (!string.Equals(System.IO.Path.GetExtension(_exePath), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                string assoc = GetExeFromAssociation(System.IO.Path.GetExtension(_exePath));
                if (!string.IsNullOrEmpty(assoc))
                {
                    Log("工程文件关联的可执行程序:" + assoc);
                    _assocBaseName = System.IO.Path.GetFileName(assoc).ToLowerInvariant();
                }
                else
                {
                    Log("未能解析工程文件的关联程序,稍后采用候选进程探测/兜底模式。");
                }
            }

            // 启动前快照:进程表 + 桌面可见窗口(兜底/新进程差分用)
            Dictionary<int, ProcEntry> oldProcs = GetProcessTable();
            Dictionary<string, WinSnapshot> oldWins = SnapshotWindows();

            // 启动软件
            int rootPid = 0;
            var sw = Stopwatch.StartNew();
            try
            {
                Process proc = Process.Start(new ProcessStartInfo(_exePath) { UseShellExecute = true });
                if (proc != null)
                {
                    rootPid = proc.Id;
                    ProcEntry pe;
                    bool knownProc = oldProcs.TryGetValue(rootPid, out pe);
                    if (knownProc)
                    {
                        string realName = "";
                        try { realName = Process.GetProcessById(rootPid).ProcessName; } catch { }
                        Log("已获得要绑定的启动进程:PID=" + rootPid + " 名称=" + realName);
                    }
                    else
                    {
                        Log("已获得要绑定的启动进程:PID=" + rootPid);
                    }
                    if (string.IsNullOrEmpty(_assocBaseName) && knownProc)
                    {
                        _assocBaseName = pe.Name.ToLowerInvariant();
                    }
                }
                else
                {
                    Log("Shell 启动未返回进程句柄,稍后按关联/兜底认根。");
                }
            }
            catch (Exception ex)
            {
                Log("启动软件异常(接下来尝试认根/兜底):" + ex.Message);
            }
            Log("启动软件调用已返回(耗时 " + sw.ElapsedMilliseconds + "ms)");
            if (rootPid == 0 && _globalFallback)
            {
                Log("兜底模式已开启:将按\"启动后新出现的窗口\"定位登录框,与其他软件同时启动时请填写 TitleKeyword 以免认错。");
            }


            // 等待窗口并自动登录
            List<WinInfo> candidates = WaitForLoginWindow(rootPid, oldProcs, oldWins);
            if (candidates.Count == 0 && !string.IsNullOrEmpty(_titleKeyword))
            {
                Log("未发现新窗口,按关键词 ~" + _titleKeyword + "~ 在现有窗口兜底查找(软件可能早开着)...");
                candidates = FindExistingWindowsByTitle();
            }
            if (candidates.Count == 0)
            {
                Log("失败:" + _waitWindowSec + " 秒内没有发现像登录框的窗口。");
                Log("对策:确认软件能弹出登录框;或在 AutoLogin.ini 填 TitleKeyword=登录窗口标题。");
                PopMsg(_waitWindowSec + " 秒内未检测到登录窗口。\r\n详情见 AutoLogin.log");
                Environment.ExitCode = 1;
                return;
            }
            Log("候选登录窗口 " + candidates.Count + " 个,按优先级:" + Describe(candidates));

            // 逐个候选试焦点:焦点确认成功才输入(核心铁律!)
            string typedResult = null;
            bool doubleWindowOpen = false;
            WinInfo usedCand = default(WinInfo);

            foreach (WinInfo cand in candidates)
            {
                string summary;
                if (!TryTypeOn(cand, out summary))
                {
                    if (summary == "locked")
                    {
                        Log("另一个实例正在输入账密,本次退出(它会把登录做完)。");
                        return;
                    }
                    Log("该候选输入失败,换下一个。原因:" + summary);
                    continue;
                }
                typedResult = summary;
                usedCand = cand;
                Log(typedResult);

                // 5 秒后看窗口是否已关闭(多数软件登录成功后关闭登录框)
                Thread.Sleep(5000);
                if (IsWindow(cand.H) && IsWindowVisible(cand.H))
                {
                    Log("注意:5 秒后登录框仍在,请到屏幕确认是否登录成功(可能弹出了提示框)。");
                    doubleWindowOpen = true;
                }
                else
                {
                    Log("登录框已关闭 => 登录动作大概率成功。");
                    doubleWindowOpen = false;
                }
                break;
            }
            if (typedResult == null)
            {
                Log("失败:所有候选窗口都没能成功激活到前台,没有执行输入。");
                PopMsg("登录窗口无法激活到前台(可能被系统遮挡),未执行自动输入。\r\n详情见 AutoLogin.log");
                Environment.ExitCode = 1;
                return;
            }

            // 可选重试:提交后窗口仍未关闭 + ini 开了 RetryOnStillOpen=1 才进行(默认关)
            if (doubleWindowOpen && _retryOnStillOpen)
            {
                WinInfo cand = usedCand;
                Log("RetryOnStillOpen=1,对同一窗口进行第二轮重试...");
                if (IsWindow(cand.H) && IsWindowVisible(cand.H))
                {
                    string summary2;
                    bool ok = TryTypeOn(cand, out summary2);
                    if (ok && summary2 != null)
                    {
                        Log("重试完成:" + summary2);
                        Thread.Sleep(5000);
                        if (IsWindow(cand.H) && IsWindowVisible(cand.H))
                        {
                            Log("注意:重试后登录框仍在,请现场确认登录是否成功。");
                        }
                        else
                        {
                            Log("重试后登录框已关闭 => 登录成功。");
                        }
                    }
                    else
                    {
                        Log("重试未能开始:" + summary2);
                    }
                }
            }
            Log("========== AutoLogin 结束 ==========");
        }

        // 对一个候选窗口执行:激活(焦点铁律)→ 自动填写账密 → 提交
        // 返回 true 且 summary 非空 = 已完成输入;summary=="locked" 表示锁被另一实例占用;否则摘要为失败原因
        private static bool TryTypeOn(WinInfo cand, out string summary)
        {
            summary = null;
            Log("尝试激活候选:0x" + cand.H.ToString("X") + " 标题=~" + cand.Title + "~(PID=" + cand.Pid + ")");
            Thread.Sleep(Math.Max(0, _readyDelayMs));
            if (!FocusWindow(cand.H))
            {
                Log("焦点确认失败,放弃该候选换下一个。");
                summary = "焦点确认失败";
                return false;
            }
            Thread.Sleep(600);
            // 输入瞬间持有防双开锁,避免两实例同时打字
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
                TryTypeOnBody(cand, out body);
                summary = body;
                return summary != null && summary != "焦点确认失败" && summary != "locked";
            }
            finally
            {
                try { mutex.ReleaseMutex(); } catch { }
                mutex.Dispose();
            }
        }

        // TryTypeOn 的实施体(调用时已持防双开锁)
        private static void TryTypeOnBody(WinInfo cand, out string summary)
        {
            summary = null;
            // ===== 精确控件模式:枚举登录窗子的输入框/按钮,点名填入并读回验证 =====
            string sub;
            if (FillByControls(cand, out sub)) { summary = sub; return; }

            if (sub == "controls-not-found")
            {
                Log("该窗口未识别到标准输入框控件,走回退键盘模式(激活窗口→打字→TAB→密码→回车)。");
                Log("若现场仍出现打不进去/点错按钮,请把 AutoLogin.log 全文发给维护者(日志已记录控件清单)。");
                FallbackTypeOn(cand);
                summary = "输入完成(回退键盘模式:账号+TAB+密码+回车)。窗口=0x" + cand.H.ToString("X") + " 标题=~" + cand.Title + "~";
                return;
            }
            summary = sub; // 验证未通过等失败原因(未提交任何键)
        }

        // ---------------- 精确控件模式 ----------------

        // 枚举登录窗子的控件:输入框(EDIT 类)按样式区分密码框;按钮按标题分"确定/取消"
        private static void EnumLoginFormControls(IntPtr parent)
        {
            _foundEdits = new List<EditFound>();
            _foundPwEdit = IntPtr.Zero;
            _foundOkBtns = new List<BtnFound>();
            _foundCancelBtns = new List<BtnFound>();
            EnumChildWindows(parent, delegate(IntPtr h, IntPtr lp)
            {
                if (!IsWindowVisible(h) || !IsWindowEnabled(h)) return true;
                string cls = GetClassNameOf(h);
                if (string.IsNullOrEmpty(cls)) return true;
                string cl = cls.ToLowerInvariant();
                if (cl.Contains("edit"))
                {
                    int style = GetWindowLongPtr(h, GWL_STYLE);
                    if ((style & ES_PASSWORD) != 0)
                    {
                        if (_foundPwEdit == IntPtr.Zero) _foundPwEdit = h;
                    }
                    else
                    {
                        EditFound e;
                        e.H = h;
                        e.Ord = _foundEdits.Count + 1;
                        _foundEdits.Add(e);
                    }
                    return true;
                }
                if (cl.Contains("button"))
                {
                    string cap = GetCtlText(h);
                    if (string.IsNullOrEmpty(cap)) return true;
                    // 标题去空格标准化:"登 录"/"确 定" 这类带空格的按钮也能命中
                    string c = cap.Replace(" ", "").Replace("　", "").ToLowerInvariant();
                    if (c.Contains("取消") || c.Contains("cancel") || c.Contains("关闭"))
                    {
                        BtnFound b;
                        b.H = h; b.Title = cap;
                        _foundCancelBtns.Add(b);
                        return true;
                    }
                    if (c.Contains("确定") || c.Contains("ok") || c.Contains("登录")
                        || c.Contains("登陆") || c.Contains("log in") || c.Contains("login"))
                    {
                        BtnFound b2;
                        b2.H = h; b2.Title = cap;
                        _foundOkBtns.Add(b2);
                    }
                    return true;
                }
                return true;
            }, IntPtr.Zero);
        }

        // 精确控件模式主流程:返回 true=已提交;summary=描述/失败原因
        private static bool FillByControls(WinInfo cand, out string summary)
        {
            summary = null;
            EnumLoginFormControls(cand.H);

            if (_foundEdits.Count == 0 && _foundPwEdit == IntPtr.Zero)
            {
                Log("精确模式放弃:EDIT 控件数=0(窗口可能为自绘控件)。");
                summary = "controls-not-found";
                return false;
            }
            if (_foundEdits.Count == 0 || _foundPwEdit == IntPtr.Zero)
            {
                return FillMixed(cand, out summary);
            }

            int ord = Math.Max(1, _userEditOrd);
            IntPtr userEdit = IntPtr.Zero;
            foreach (EditFound e in _foundEdits) { if (e.Ord == ord) { userEdit = e.H; break; } }
            if (userEdit == IntPtr.Zero) userEdit = _foundEdits[0].H;

            Log("精确控件模式:用户名框=第 " + (userEdit == _foundEdits[0].H ? _foundEdits[0].Ord.ToString() : ord.ToString()) +
                " 个 0x" + userEdit.ToString("X") + " / 密码框 0x" + _foundPwEdit.ToString("X") +
                " / 确定按钮 " + _foundOkBtns.Count + " 个 / 取消按钮 " + _foundCancelBtns.Count + " 个");
            foreach (EditFound e in _foundEdits)
                Log("  EDIT#" + e.Ord + " 0x" + e.H.ToString("X") + " 类名=" + GetClassNameOf(e.H) + " 当前文本=~" + GetCtlText(e.H) + "~");

            bool userOk = WriteToEdit(userEdit, _userName, false);
            bool passOk = WriteToEdit(_foundPwEdit, _password, true);
            if (!userOk || !passOk)
            {
                Log("铁律:账密读回验证未通过 => 不发送回车/点击,中止!用户名OK=" + userOk + " 密码OK=" + passOk);
                PopMsg("自动登录未能把账号密码正确写入登录框(已中止,未提交任何按键)。\r\n详情见 AutoLogin.log;若反复出现,请把日志发给维护者。");
                summary = "账密验证未通过(已中止提交)";
                return false;
            }
            return SubmitLogin(out summary);
        }

        // 两类输入框只识别到其一时的混合模式:能精确的精确,其余用键盘,但仍有验证守门
        private static bool FillMixed(WinInfo cand, out string summary)
        {
            summary = null;
            if (_foundEdits.Count > 0)
            {
                IntPtr userEdit = _foundEdits[0].H;
                int wantOrd = Math.Max(1, _userEditOrd);
                foreach (EditFound e in _foundEdits) { if (e.Ord == wantOrd) { userEdit = e.H; break; } }
                Log("混合模式:用户名框 0x" + userEdit.ToString("X") + " 精确写入;密码框未按样式识别(将其 TAB 后键盘输入)。");
                foreach (EditFound e in _foundEdits)
                    Log("  EDIT#" + e.Ord + " 0x" + e.H.ToString("X") + " 类名=" + GetClassNameOf(e.H) + " 当前文本=~" + GetCtlText(e.H) + "~");
                if (!WriteToEdit(userEdit, _userName, false))
                {
                    Log("铁律:用户名读回验证未通过 => 不发送任何后续按键。");
                    summary = "用户名验证未通过(已中止提交)";
                    return false;
                }
                SendKey(VK_TAB);
                Thread.Sleep(250);
                bool typed = TypeText(_password);
                Log("混合模式:密码键盘输入=" + (typed ? "OK" : "失败"));
                if (!typed) { summary = "密码键盘输入失败(已中止提交)"; return false; }
                return SubmitLogin(out summary);
            }
            if (_foundPwEdit != IntPtr.Zero)
            {
                Log("混合模式:仅识别到密码框 0x" + _foundPwEdit.ToString("X") + ",先写密码,用户名靠 TAB 移焦点键盘输入。");
                bool px = WriteToEdit(_foundPwEdit, _password, true);
                if (!px)
                {
                    Log("铁律:密码读回验证未通过 => 不发送任何后续按键。");
                    summary = "密码验证未通过(已中止提交)";
                    return false;
                }
                SendKey(VK_TAB);
                Thread.Sleep(250);
                bool tx = TypeText(_userName);
                Log("混合模式:用户名键盘输入=" + (tx ? "OK" : "失败"));
                if (!tx) { summary = "用户名键盘输入失败(已中止提交)"; return false; }
                return SubmitLogin(out summary);
            }
            summary = "controls-not-found";
            return false;
        }

        // 单个输入框写入:先 WM_SETTEXT 静默写入+读回验证;不通过则焦点钉住该控件后键盘输入+再验证
        private static bool WriteToEdit(IntPtr h, string text, bool isPass)
        {
            // 输入框若属于下拉框(ComboBox),直接对下拉框本体写文本,保证下拉框托管状态同步
            IntPtr target = h;
            IntPtr parent = GetParent(h);
            if (parent != IntPtr.Zero && GetClassNameOf(parent).ToLowerInvariant().Contains("combobox"))
            {
                target = parent;
                Log("用户名输入框属于下拉框(ComboBox),改为对下拉框本体写文本。");
            }

            string name = isPass ? "密码框" : "用户名框";
            bool setRet = EditSetText(target, text);
            Thread.Sleep(150);
            bool ok = VerifyEdit(target, text, isPass);
            Log(name + " WM_SETTEXT 调用=" + (setRet ? "成功" : "失败") + ",读回验证 " + (ok ? "通过" : "未通过"));
            if (ok) return true;

            Log(name + " 转键盘输入:先把焦点钉到该输入框...");
            if (!FocusControlDirect(h)) { Log(name + " 焦点钉入失败。"); return false; }
            Thread.Sleep(150);
            EditSetText(h, ""); // 键盘输入前清掉任何预填/半写内容
            Thread.Sleep(80);
            TypeText("q");      // 焐热键盘/IME 链路(防中文首字被吞)
            Thread.Sleep(150);
            SendKey(VK_BACK);
            Thread.Sleep(100);
            SendKey(VK_BACK);
            Thread.Sleep(150);
            bool typed = TypeText(text);
            Thread.Sleep(150);
            bool ok2 = VerifyEdit(h, text, isPass);
            Log(name + " 键盘输入=" + (typed ? "OK" : "失败") + ",读回验证 " + (ok2 ? "通过" : "未通过"));
            return ok2;
        }

        // 读回验证:普通框必须完全一致;密码框按"掩码长度=明文长度"判定
        private static bool VerifyEdit(IntPtr h, string text, bool isPass)
        {
            if (isPass)
            {
                int len = GetCtlTextLength(h);
                if (len == text.Length) return true;
                Log("密码框读回长度不符:实际=" + len + " 期望=" + text.Length);
                return false;
            }
            string got = GetCtlText(h);
            if (got == text) return true;
            Log("用户名框读回不符:实际=~" + got + "~ 期望=~" + text + "~");
            return false;
        }

        // 提交:有"确定/登录/OK"按钮就点它(从机制上不许点取消);找不到按钮才回车兜底
        private static bool SubmitLogin(out string summary)
        {
            summary = null;
            if (_foundOkBtns.Count > 0)
            {
                IntPtr btn = _foundOkBtns[0].H;
                string cap = _foundOkBtns[0].Title;
                Log("提交:向按钮 ~" + cap + "~(0x" + btn.ToString("X") + ")发送点击消息。");
                PostMessage(btn, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                summary = "输入完成(精确控件模式,已点击按钮~" + cap + "~)。";
                return true;
            }
            Log("提交:窗口内未见确定/登录按钮,发送回车。");
            SendKey(VK_RETURN);
            summary = "输入完成(精确控件模式,回车提交)。";
            return true;
        }

        // 回退键盘模式(老姿势):所有键交给窗体,适用于自绘控件等非标准登录框
        private static void FallbackTypeOn(WinInfo cand)
        {
            // 中文账号首字常被 IME 热身吞掉:先打占位字符焐热,再连退两格清干净
            TypeText("q");
            Thread.Sleep(200);
            SendKey(VK_BACK);
            Thread.Sleep(150);
            SendKey(VK_BACK);
            Thread.Sleep(200);
            Log("开始输入账号密码(回退模式,无法逐框验证)...");
            TypeText(_userName);
            Thread.Sleep(150);
            SendKey(VK_TAB);
            Thread.Sleep(150);
            TypeText(_password);
            Thread.Sleep(200);
            SendKey(VK_RETURN);
        }

        // ---------------- 控件读写小工具 ----------------
        private static string GetClassNameOf(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetCtlText(IntPtr h)
        {
            var sb = new StringBuilder(1024);
            SendMessageB(h, WM_GETTEXT, (IntPtr)sb.Capacity, sb);
            return sb.ToString();
        }

        private static int GetCtlTextLength(IntPtr h)
        {
            return (int)SendMessageI(h, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        }

        private static bool EditSetText(IntPtr h, string text)
        {
            try { return SendMessageS(h, WM_SETTEXT, IntPtr.Zero, text) != IntPtr.Zero; }
            catch { return false; }
        }

        // 把系统键盘焦点钉到指定控件(跨进程:AttachThreadInput + SetFocus + GetFocus 确认)
        private static bool FocusControlDirect(IntPtr h)
        {
            uint pidDummy;
            uint tidTarget = GetWindowThreadProcessId(h, out pidDummy);
            uint tidSelf = GetCurrentThreadId();
            if (tidTarget == tidSelf)
            {
                SetFocus(h);
                return GetFocus() == h;
            }
            if (!AttachThreadInput(tidSelf, tidTarget, true))
            {
                Log("AttachThreadInput 失败:0x" + h.ToString("X"));
                return false;
            }
            SetFocus(h);
            bool ok = GetFocus() == h;
            AttachThreadInput(tidSelf, tidTarget, false);
            return ok;
        }

        private static string Describe(List<WinInfo> list)
        {
            var sb = new StringBuilder();
            foreach (WinInfo w in list) sb.Append("0x" + w.H.ToString("X") + "(~" + w.Title + "~) ");
            return sb.ToString();
        }

        // ---------------- 窗口信息 ----------------
        private struct WinInfo
        {
            public IntPtr H;
            public uint Pid;
            public string Title;
            public int Score;
        }

        private struct WinSnapshot
        {
            public IntPtr H;
            public uint Pid;
            public string Title;
            public int Score;
        }

        private static string GetWindowTitle(IntPtr h)
        {
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        // 只按窗口自身属性判断(不查进程,保证轮询快):可见+非工具窗+可激活+尺寸合适
        private static bool IsSuitableWindow(IntPtr h, out string title, out uint pid)
        {
            title = null;
            pid = 0;
            if (!IsWindowVisible(h)) return false;
            GetWindowThreadProcessId(h, out pid);
            if ((int)pid == _curPid) return false; // 排除自己

            int ex = GetWindowLongPtr(h, GWL_EXSTYLE);
            if ((ex & WS_EX_TOOLWINDOW) != 0) return false;
            if ((ex & WS_EX_NOACTIVATE) != 0) return false;

            RECT r;
            if (!GetWindowRect(h, out r)) return false;
            if (r.R - r.L < 120 || r.B - r.T < 80) return false; // 过滤气泡/隐形窗

            title = GetWindowTitle(h);
            return true;
        }

        // 系统进程黑名单:仅对"将要尝试输入的最终候选"查询,次数极少
        private static bool IsSystemOwner(uint pid)
        {
            try
            {
                string name = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant();
                switch (name)
                {
                    case "explorer":
                    case "dwm":
                    case "textinputhost":
                    case "windowsinputexperience":
                    case "shellexperiencehost":
                    case "searchhost":
                    case "searchapp":
                    case "runtimebroker":
                    case "applicationframehost":
                    case "svchost":
                    case "fontdrvhost":
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static int ScoreOf(string title)
        {
            if (title == null) return 0;
            int s = 1; // 标题非空加 1 分
            string lower = title.ToLowerInvariant();
            if (lower.Contains("登录") || lower.Contains("登陆") || lower.Contains("用户") ||
                lower.Contains("密码") || lower.Contains("login") || lower.Contains("sign")
                || lower.Contains("password"))
                s += 10; // 登录相关特征词,优先级大幅提升
            return s;
        }

        // 桌面当前可见窗口快照
        private static Dictionary<string, WinSnapshot> SnapshotWindows()
        {
            Dictionary<string, WinSnapshot> result = new Dictionary<string, WinSnapshot>();
            EnumWindowsProc cb = delegate(IntPtr h, IntPtr lp)
            {
                string t;
                uint p;
                if (IsSuitableWindow(h, out t, out p))
                {
                    var w = new WinSnapshot();
                    w.H = h;
                    w.Pid = p;
                    w.Title = t;
                    w.Score = ScoreOf(t);
                    result[h.ToString()] = w;
                }
                return true;
            };
            EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return result;
        }

        // ---------------- 进程表(ToolHelp 快照,轻量) ----------------
        private struct ProcEntry
        {
            public int Pid;
            public int ParentPid;
            public string Name;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32W
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint TH32CS_SNAPPROCESS = 0x00000002;

        private static Dictionary<int, ProcEntry> GetProcessTable()
        {
            Dictionary<int, ProcEntry> table = new Dictionary<int, ProcEntry>();
            IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return table;
            try
            {
                var e = new PROCESSENTRY32W();
                e.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32W));
                if (Process32FirstW(snap, ref e))
                {
                    while (true)
                    {
                        var pe = new ProcEntry();
                        pe.Pid = (int)e.th32ProcessID;
                        pe.ParentPid = (int)e.th32ParentProcessID;
                        pe.Name = e.szExeFile;
                        table.Add(pe.Pid, pe);
                        if (!Process32NextW(snap, ref e)) break;
                        e.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32W));
                    }
                }
            }
            finally { CloseHandle(snap); }
            return table;
        }

        // 从根 PID 收集全部子孙(含根)。rootPid<=0 返回空
        private static HashSet<int> TreeOf(Dictionary<int, ProcEntry> table, int rootPid)
        {
            HashSet<int> set = new HashSet<int>();
            if (rootPid <= 0) return set;
            Stack<int> st = new Stack<int>();
            st.Push(rootPid);
            while (st.Count > 0)
            {
                int p = st.Pop();
                if (!set.Add(p)) continue;
                foreach (KeyValuePair<int, ProcEntry> kv in table)
                {
                    if (kv.Value.ParentPid == p && !set.Contains(kv.Key)) st.Push(kv.Key);
                }
            }
            return set;
        }

        // ---------------- 工程文件关联解析 ----------------
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint AssocQueryStringW(uint flags, uint str, string pszAssoc, string pszExtra,
            [Out] StringBuilder pszOut, ref uint pcchOut);

        private const uint ASSOCF_NONE = 0;
        private const uint ASSOCSTR_COMMAND = 0;

        // 通过系统文件关联解析出可执行文件完整路径(如 .ezd -> "...\Runtime.exe" "%1")
        private static string GetExeFromAssociation(string ext)
        {
            try
            {
                var sb = new StringBuilder(260);
                uint len = (uint)sb.Capacity;
                uint hr = AssocQueryStringW(ASSOCF_NONE, ASSOCSTR_COMMAND, ext, "open", sb, ref len);
                if (hr != 0 || len == 0) return null;
                string cmd = sb.ToString();
                // 形如 "D:\xx\Runtime.exe" "%1" 或 D:\xx\Runtime.exe "%1"
                if (string.IsNullOrEmpty(cmd)) return null;
                string exe = cmd;
                if (exe.StartsWith("\""))
                {
                    int end = exe.IndexOf('"', 1);
                    if (end > 0) exe = exe.Substring(1, end - 1);
                }
                else
                {
                    int sp = exe.IndexOf(' ');
                    if (sp > 0) exe = exe.Substring(0, sp);
                }
                exe = exe.Trim();
                if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(exe))
                    return exe;
                return null;
            }
            catch { return null; }
        }

        private static string ResolveLnk(string lnkPath)
        {
            try
            {
                Type t = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"));
                object shellLink = Activator.CreateInstance(t);
                IPersistFile pf = (IPersistFile)shellLink;
                pf.Load(lnkPath, 2);
                IShellLinkW sl = (IShellLinkW)shellLink;
                var sb = new StringBuilder(1024);
                sl.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                Log("解析 lnk 失败:" + ex.Message);
                return null;
            }
        }

        // ---------------- 开机自启动快捷方式(GUI 一键部署) ----------------
        internal static string SelfExePath
        {
            get
            {
                try
                {
                    string p = System.Reflection.Assembly.GetEntryAssembly().Location;
                    if (!string.IsNullOrEmpty(p)) return p;
                }
                catch { }
                return Environment.GetCommandLineArgs()[0];
            }
        }

        internal static string StartupLnkPath
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\AutoLogin.lnk"; }
        }

        // 在启动文件夹创建/删除 AutoLogin.lnk:目标=本 exe、参数 " /auto"、图标=本 exe(任务栏同款)
        internal static bool InstallStartup(bool on, out string err)
        {
            err = null;
            try
            {
                string lnk = StartupLnkPath;
                if (!on)
                {
                    if (System.IO.File.Exists(lnk))
                    {
                        System.IO.File.Delete(lnk);
                        Log("已删除开机自启动快捷方式:" + lnk);
                    }
                    return true;
                }

                string exe = SelfExePath;
                Type t = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"));
                object shellLink = Activator.CreateInstance(t);
                IPersistFile pf = (IPersistFile)shellLink;
                IShellLinkW sl = (IShellLinkW)shellLink;
                sl.SetPath(exe);
                sl.SetArguments(" /auto");
                sl.SetWorkingDirectory(System.IO.Path.GetDirectoryName(exe));
                sl.SetIconLocation(exe, 0);
                sl.SetDescription("AutoLogin 开机自启动(静默模式,含 /auto 参数)");
                if (System.IO.File.Exists(lnk)) System.IO.File.Delete(lnk);
                pf.Save(lnk, true);
                Log("已创建开机自启动快捷方式:" + lnk + " -> \"" + exe + "\" /auto(图标取 exe)");
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                Log("部署开机自启动快捷方式失败:" + err);
                return false;
            }
        }

        // ---------------- 等待登录窗口(核心状态机) ----------------
        private static List<WinInfo> WaitForLoginWindow(int rootPid, Dictionary<int, ProcEntry> oldProcs, Dictionary<string, WinSnapshot> oldWins)
        {
            DateTime deadline = DateTime.Now.AddSeconds(_waitWindowSec);
            DateTime lastLog = DateTime.Now;
            string lastStableSignature = "";
            int stableRounds = 0;
            Dictionary<string, WinSnapshot> hiddenCandidates = new Dictionary<string, WinSnapshot>();
            int rootGoneAtSec = -1;

            while (DateTime.Now < deadline)
            {
                // --- 认根/进程树 ---
                Dictionary<int, ProcEntry> table = GetProcessTable();
                if (rootPid <= 0 && !string.IsNullOrEmpty(_assocBaseName))
                {
                    // 无 root:优先从"新出现的进程"里按关联 exe 文件名认根
                    foreach (KeyValuePair<int, ProcEntry> kv in table)
                    {
                        if (oldProcs.ContainsKey(kv.Key)) continue;
                        if (string.Equals(kv.Value.Name, _assocBaseName, StringComparison.OrdinalIgnoreCase))
                        {
                            rootPid = kv.Key;
                            _globalFallback = false;
                            Log("按关联程序认根成功:PID=" + rootPid + " 名称=" + kv.Value.Name);
                            break;
                        }
                    }
                }
                if (rootPid > 0)
                {
                    HashSet<int> treeSet = TreeOf(table, rootPid);
                    if (table.ContainsKey(rootPid))
                    {
                        rootGoneAtSec = -1;
                    }
                    else if (rootGoneAtSec < 0)
                    {
                        rootGoneAtSec = (int)DateTimeOffset.Now.ToUnixTimeSeconds();
                    }
                    else if ((int)DateTimeOffset.Now.ToUnixTimeSeconds() - rootGoneAtSec > ROOT_LOST_SEC)
                    {
                        // 根死了且没有窗口出现,尝试按关联 exe 重新认根
                        int retry = RetryFindRootByKey(recordTable: table, oldProcs: oldProcs);
                        if (retry > 0)
                        {
                            rootPid = retry;
                            rootGoneAtSec = -1;
                            Log("根进程已消失,按关联程序重新认根:PID=" + retry);
                        }
                        else
                        {
                            Log("根进程已消失,转入全局新窗口兜底模式。");
                            rootPid = 0;
                            _globalFallback = true;
                        }
                    }
                }

                // --- 收集候选窗口 ---
                List<WinInfo> newWins = new List<WinInfo>();
                if (rootPid > 0)
                {
                    HashSet<int> treeSet = TreeOf(table, rootPid);
                    EnumWindowsProc cb = delegate(IntPtr h, IntPtr lp)
                    {
                        string t;
                        uint p0;
                        if (!IsSuitableWindow(h, out t, out p0)) return true;
                        if (!treeSet.Contains((int)p0)) return true;
                        if (!string.IsNullOrEmpty(_titleKeyword) &&
                            t.IndexOf(_titleKeyword, StringComparison.OrdinalIgnoreCase) < 0) return true;
                        var w = new WinInfo();
                        w.H = h;
                        w.Pid = p0;
                        w.Title = t;
                        w.Score = ScoreOf(t);
                        newWins.Add(w);
                        return true;
                    };
                    EnumWindows(cb, IntPtr.Zero);
                    GC.KeepAlive(cb);
                }
                else
                {
                    // 全局兜底:不在启动前快照里的、新出现的可见窗口
                    EnumWindowsProc cb2 = delegate(IntPtr h, IntPtr lp)
                    {
                        if (oldWins.ContainsKey(h.ToString())) return true;
                        string t;
                        uint p0;
                        if (!IsSuitableWindow(h, out t, out p0)) return true;
                        if (!string.IsNullOrEmpty(_titleKeyword) &&
                            t.IndexOf(_titleKeyword, StringComparison.OrdinalIgnoreCase) < 0) return true;
                        var w = new WinInfo();
                        w.H = h;
                        w.Pid = p0;
                        w.Title = t;
                        w.Score = ScoreOf(t);
                        newWins.Add(w);
                        return true;
                    };
                    EnumWindows(cb2, IntPtr.Zero);
                    GC.KeepAlive(cb2);
                }

                // --- 稳定 N 轮后才选定,防止 splash 抢跑 ---
                var sig = new StringBuilder();
                foreach (WinInfo w in newWins) sig.Append(w.H.ToString("X") + ";");
                string signature = sig.ToString();
                if (newWins.Count > 0 && signature == lastStableSignature)
                {
                    stableRounds++;
                }
                else
                {
                    stableRounds = 1;
                    lastStableSignature = signature;
                }
                if (newWins.Count > 0 && stableRounds >= STABLE_HOLD_ROUNDS)
                {
                    Log("候选窗口签名稳定(" + stableRounds + " 轮):共 " + newWins.Count + " 个" +
                        (rootPid > 0 ? "(进程树绑定模式" + (rootPid > 0 ? ",根 PID=" + rootPid + ")" : "") : "(全局兜底模式)"));
                    List<WinInfo> chosen = ChooseCandidates(newWins);
                    if (chosen.Count > 0) return chosen;
                }
                if ((DateTime.Now - lastLog).TotalSeconds > 15)
                {
                    lastLog = DateTime.Now;
                    Log("等待登录窗口中...(剩余约 " + (int)DateTime.Now.Subtract(deadline).TotalSeconds * -1 + " 秒,模式=" +
                        (rootPid > 0 ? ("进程树绑定,根 PID=" + rootPid) : "全局兜底") + ")");
                }
                Thread.Sleep(400);
            }
            return new List<WinInfo>();
        }

        private static int RetryFindRootByKey(Dictionary<int, ProcEntry> recordTable, Dictionary<int, ProcEntry> oldProcs)
        {
            if (string.IsNullOrEmpty(_assocBaseName)) return 0;
            foreach (KeyValuePair<int, ProcEntry> kv in recordTable)
            {
                if (string.Equals(kv.Value.Name, _assocBaseName, StringComparison.OrdinalIgnoreCase))
                {
                    return kv.Key;
                }
            }
            return 0;
        }

        // 从候选里选出真正要尝试的(过滤系统黑名单进程+排序)——查询进程,次数少
        private static List<WinInfo> ChooseCandidates(List<WinInfo> raw)
        {
            List<WinInfo> result = new List<WinInfo>();
            foreach (WinInfo w in raw)
            {
                if (!IsSystemOwner(w.Pid)) result.Add(w);
                else Log("跳过系统窗口:0x" + w.H.ToString("X") + " ~" + w.Title + "~");
            }
            result.Sort(delegate(WinInfo a, WinInfo b)
            {
                int cmp = b.Score.CompareTo(a.Score);
                return cmp;
            });
            return result;
        }

        // 兜底:在现有窗口里按标题关键词找(软件早开着的手动场景)
        private static List<WinInfo> FindExistingWindowsByTitle()
        {
            List<WinInfo> raw = new List<WinInfo>();
            EnumWindowsProc cb = delegate(IntPtr h, IntPtr lp)
            {
                string t;
                uint p;
                if (!IsSuitableWindow(h, out t, out p)) return true;
                if (t.IndexOf(_titleKeyword, StringComparison.OrdinalIgnoreCase) < 0) return true;
                var w = new WinInfo();
                w.H = h;
                w.Pid = p;
                w.Title = t;
                w.Score = ScoreOf(t);
                raw.Add(w);
                return true;
            };
            EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return ChooseCandidates(raw);
        }

        // 激活窗口(带 ALT 技巧解除前台锁定),返回是否确认已到 foreground
        private static bool FocusWindow(IntPtr hWnd)
        {
            for (int i = 0; i < 10; i++)
            {
                if (IsIconic(hWnd)) ShowWindow(hWnd, 9); // SW_RESTORE
                keybd_event((byte)VK_MENU, 0, 0, IntPtr.Zero);
                keybd_event((byte)VK_MENU, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
                SetForegroundWindow(hWnd);
                Thread.Sleep(300);
                if (GetForegroundWindow() == hWnd) return true;
            }
            return GetForegroundWindow() == hWnd;
        }

        // ---------------- 输入实现 ----------------
        private static bool TypeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            bool ok = true;
            Thread.Sleep(150);
            foreach (char c in text)
            {
                var down = new INPUT();
                down.type = INPUT_KEYBOARD;
                down.ki.wScan = c;
                down.ki.dwFlags = KEYEVENTF_UNICODE;

                var up = new INPUT();
                up.type = INPUT_KEYBOARD;
                up.ki.wScan = c;
                up.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;

                INPUT[] arr = new INPUT[2];
                arr[0] = down;
                arr[1] = up;
                uint sent = SendInput(2, arr, Marshal.SizeOf(typeof(INPUT)));
                if (sent != 2)
                {
                    Log("SendInput 发送字符失败(chr=" + (int)c + "),Win32Error=" + Marshal.GetLastWin32Error());
                    ok = false;
                }
                Thread.Sleep(Math.Max(1, _typeDelayMs));
            }
            return ok;
        }

        private static void SendKey(ushort vk)
        {
            var down = new INPUT();
            down.type = INPUT_KEYBOARD;
            down.ki.wVk = vk;
            var up = new INPUT();
            up.type = INPUT_KEYBOARD;
            up.ki.wVk = vk;
            up.ki.dwFlags = KEYEVENTF_KEYUP;
            INPUT[] arr = new INPUT[2];
            arr[0] = down;
            arr[1] = up;
            SendInput(2, arr, Marshal.SizeOf(typeof(INPUT)));
        }

        // ---------------- ShellLink(lr) ----------------
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, Guid("0000010B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            void IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        // ---------------- INI 配置 ----------------
        internal const string IniTemplate =
"; ============================================================\r\n" +
"; AutoLogin 配置文件(保存后重新运行 exe 生效)\r\n" +
"; ExePath  : 软件的 exe/快捷方式/工程文件的完整路径(.exe/.lnk/.ezd 均可)\r\n" +
"; UserName : 登录账号\r\n" +
"; Password : 登录密码(改动密码后只需改这里)\r\n" +
"; TitleKeyword : 登录窗口标题关键词。留空=自动识别;多软件并发时建议填(如\"登录\")\r\n" +
"; WaitWindowSec : 最多等多少秒出现登录框\r\n" +
"; ReadyDelayMs  : 登录框出现后再等多少毫秒才输入(控件没准备好就调大)\r\n" +
"; TypeDelayMs   : 每个字符间隔毫秒,输入不稳就调大\r\n" +
"; MsgBoxOnFail  : 失败时是否弹窗提示(1=弹窗 0=只写日志)\r\n" +
"; RetryOnStillOpen : 提交后登录框未关闭时是否重试(1=重试 0=不重试,默认关)\r\n" +
"; UserEditOrd   : 用户名框=窗口里第 N 个非密码输入框(默认 1;日志会列出各框内容,认错框时改 2 试试)\r\n" +
"; 开机自启动 : 无需手改;在配置界面\"行为\"卡片一键开关(自动生成启动项快捷方式,目标含 /auto)\r\n" +
"; ============================================================\r\n" +
"ExePath=C:\\YourApp\\YourApp.exe\r\n" +
"UserName=操作员\r\n" +
"Password=123456\r\n" +
"TitleKeyword=\r\n" +
"WaitWindowSec=120\r\n" +
"ReadyDelayMs=3000\r\n" +
"TypeDelayMs=30\r\n" +
"MsgBoxOnFail=1\r\n" +
"RetryOnStillOpen=0\r\n" +
"UserEditOrd=1\r\n";

        private static string SmartReadFile(string path)
        {
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch
            {
                return System.Text.Encoding.GetEncoding(0).GetString(bytes);
            }
        }

        internal static void LoadIni()
        {
            if (!System.IO.File.Exists(_iniPath))
            {
                System.IO.File.WriteAllText(_iniPath, IniTemplate, new UTF8Encoding(true));
                Log("未找到配置文件,已自动生成默认 AutoLogin.ini(本次按默认值继续,若失败请看日志)。");
            }
            string[] lines = SmartReadFile(_iniPath).Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                int idx = line.IndexOf('=');
                if (idx <= 0) continue;
                string key = line.Substring(0, idx).Trim().ToLowerInvariant();
                string val = line.Substring(idx + 1).Trim();
                switch (key)
                {
                    case "exepath": _exePath = val; break;
                    case "username": _userName = val; break;
                    case "password": _password = val; break;
                    case "titlekeyword": _titleKeyword = val; break;
                    case "waitwindowsec": int.TryParse(val, out _waitWindowSec); break;
                    case "readydelayms": int.TryParse(val, out _readyDelayMs); break;
                    case "typedelayms": int.TryParse(val, out _typeDelayMs); break;
                    case "msgboxonfail": _msgBoxOnFail = val != "0"; break;
                    case "retryonstillopen": _retryOnStillOpen = val == "1"; break;
                    case "usereditord": int.TryParse(val, out _userEditOrd); break;
                }
            }
        }

        private static void LogConfig()
        {
            Log("配置 => ExePath=" + _exePath + " | 用户名=" + _userName + " | 密码=***" +
                " | 标题关键词=" + (string.IsNullOrEmpty(_titleKeyword) ? "(空)" : _titleKeyword) +
                " | WaitWindowSec=" + _waitWindowSec + " | ReadyDelayMs=" + _readyDelayMs +
                " | TypeDelayMs=" + _typeDelayMs + " | 失败弹窗=" + (_msgBoxOnFail ? "开" : "关") +
                " | 用户名框序号=" + _userEditOrd);
        }

        private static void PopMsg(string text)
        {
            if (!_msgBoxOnFail) return;
            try { MessageBoxW(IntPtr.Zero, text, "AutoLogin 提示", 0x40); }
            catch { }
        }

        private static void Log(string msg)
        {
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + msg;
                System.IO.File.AppendAllText(_logPath, line + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }

        // ---------------- Win32 声明 ----------------
        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk; public ushort wScan;
            public uint dwFlags; public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx; public int dy;
            public uint mouseData; public uint dwFlags; public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public MOUSEINPUT mi;
            [FieldOffset(8)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int L, T, R, B; }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const ushort VK_TAB = 0x09;
        private const ushort VK_RETURN = 0x0D;
        private const ushort VK_MENU = 0x12;
        private const ushort VK_BACK = 0x08;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        // ---------------- 精确控件模式 Win32 ----------------
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageS(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageB(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam);

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessageI(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        private const int GWL_STYLE = -16;
        private const int ES_PASSWORD = 0x0020;
        private const uint WM_SETTEXT = 0x000C;
        private const uint WM_GETTEXT = 0x000D;
        private const uint WM_GETTEXTLENGTH = 0x000E;
        private const uint BM_CLICK = 0x00F5;
    }
}
