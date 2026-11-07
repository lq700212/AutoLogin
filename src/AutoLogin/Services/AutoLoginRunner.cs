using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using AutoLogin.Models;

namespace AutoLogin.Services
{
    /// <summary>
    /// 自动登录主流程编排。
    ///
    /// 设计要点：
    /// 1) 进程树绑定优先，同机其它软件弹窗不误伤；
    /// 2) 窗口候选集合连续稳定若干轮才动手，防 splash 抢跑；
    /// 3) 焦点确认成功后才输入；
    /// 4) 账密读回验证不通过绝不提交。
    /// </summary>
    public sealed class AutoLoginRunner
    {
        private const int StableHoldRounds = 3;     // 候选集合连续稳定多少轮才选定（每轮 400ms）
        private const int RootLostSec = 5;          // 根进程消失多少秒后从关联 exe 重新认根

        private readonly AppConfig _config;
        private readonly ILogService _log;
        private readonly AppPaths _paths;
        private readonly WindowLocator _locator;
        private readonly LoginAutomation _automation;

        private string _assocBaseName;
        private bool _globalFallback;

        public AutoLoginRunner(AppConfig config, ILogService log, AppPaths paths)
        {
            _config = config.Clone();
            _config.Normalize();
            _log = log;
            _paths = paths;

            int selfPid = Process.GetCurrentProcess().Id;
            _locator = new WindowLocator(_config, log, selfPid);
            _automation = new LoginAutomation(_config, log);
        }

        /// <summary>阶段变化通知，供界面显示当前进度。</summary>
        public event EventHandler<string> StageChanged;

        public RunResult Run()
        {
            _log.RotateIfNeeded();
            _log.Info("========== AutoLogin 启动 ==========");
            LogConfig();

            if (string.IsNullOrEmpty(_config.ExePath))
            {
                _log.Error("失败:AutoLogin.ini 中 ExePath 为空,请填写软件 exe/快捷方式/工程文件的完整路径。");
                return RunResult.Fail("配置错误:ExePath 为空,请编辑 AutoLogin.ini");
            }

            if (!File.Exists(_config.ExePath))
            {
                _log.Error("失败:ExePath 指向的文件不存在:" + _config.ExePath);
                _log.Error("请检查盘符/文件夹路径是否与工控机一致。");
                return RunResult.Fail("找不到软件:\r\n" + _config.ExePath + "\r\n请核对 AutoLogin.ini 的 ExePath");
            }

            _assocBaseName = AssociationResolver.ResolveTargetExeName(_config.ExePath, _log);

            // 启动前快照：进程表 + 桌面可见窗口（兜底/新进程差分用）
            Dictionary<int, ProcessEntry> oldProcs = ProcessTree.Snapshot();
            Dictionary<string, WindowInfo> oldWins = _locator.SnapshotWindows();

            Stage("正在启动目标软件…");
            int rootPid = LaunchTarget(oldProcs);

            Stage("正在等待登录窗口…");
            int finalRootPid;
            bool globalFallback;
            List<WindowInfo> candidates = WaitForLoginWindow(
                rootPid, oldProcs, oldWins, out finalRootPid, out globalFallback);

            if (candidates.Count == 0 && !string.IsNullOrEmpty(_config.TitleKeyword))
            {
                _log.Info("未发现新窗口,按关键词 ~" + _config.TitleKeyword + "~ 在现有窗口兜底查找(软件可能早开着)...");
                candidates = _locator.FindExistingWindowsByTitle();
            }

            if (candidates.Count == 0)
            {
                _log.Error("失败:" + _config.WaitWindowSec + " 秒内没有发现像登录框的窗口。");
                _log.Error("对策:确认软件能弹出登录框;或在 AutoLogin.ini 填 TitleKeyword=登录窗口标题。");
                return RunResult.Fail(_config.WaitWindowSec + " 秒内未检测到登录窗口。\r\n详情见 AutoLogin.log");
            }

            _log.Info("候选登录窗口 " + candidates.Count + " 个,按优先级:" + WindowLocator.Describe(candidates));

            string typedResult = null;
            bool loginWindowStillOpen = false;
            WindowInfo usedCandidate = default(WindowInfo);

            foreach (WindowInfo candidate in candidates)
            {
                string summary;
                if (!_automation.TryTypeOn(candidate, out summary))
                {
                    if (summary == "locked")
                    {
                        _log.Warning("另一个实例正在输入账密,本次退出(它会把登录做完)。");
                        return RunResult.Skip("另一个实例正在输入账密,本次退出。");
                    }

                    _log.Warning("该候选输入失败,换下一个。原因:" + summary);
                    continue;
                }

                typedResult = summary;
                usedCandidate = candidate;
                _log.Info(typedResult);

                Stage("已提交,正在确认登录结果…");
                Thread.Sleep(5000);
                if (IsWindowOpen(candidate.Handle))
                {
                    _log.Warning("注意:5 秒后登录框仍在,请到屏幕确认是否登录成功(可能弹出了提示框)。");
                    loginWindowStillOpen = true;
                }
                else
                {
                    _log.Info("登录框已关闭 => 登录动作大概率成功。");
                    loginWindowStillOpen = false;
                }
                break;
            }

            if (typedResult == null)
            {
                _log.Error("失败:所有候选窗口都没能成功激活到前台,没有执行输入。");
                return RunResult.Fail("登录窗口无法激活到前台(可能被系统遮挡),未执行自动输入。\r\n详情见 AutoLogin.log");
            }

            // 可选重试：提交后窗口仍未关闭 + ini 开了 RetryOnStillOpen=1 才进行（默认关）
            if (loginWindowStillOpen && _config.RetryOnStillOpen)
            {
                Stage("登录框未关闭,正在重试…");
                _log.Info("RetryOnStillOpen=1,对同一窗口进行第二轮重试...");
                if (IsWindowOpen(usedCandidate.Handle))
                {
                    string summary2;
                    bool ok = _automation.TryTypeOn(usedCandidate, out summary2);
                    if (ok && summary2 != null)
                    {
                        _log.Info("重试完成:" + summary2);
                        Thread.Sleep(5000);
                        if (IsWindowOpen(usedCandidate.Handle))
                        {
                            _log.Warning("注意:重试后登录框仍在,请现场确认登录是否成功。");
                        }
                        else
                        {
                            _log.Info("重试后登录框已关闭 => 登录成功。");
                        }
                    }
                    else
                    {
                        _log.Warning("重试未能开始:" + summary2);
                    }
                }
            }

            _log.Info("========== AutoLogin 结束 ==========");
            Stage("完成");

            if (loginWindowStillOpen)
            {
                return RunResult.Ok("登录动作已提交,但登录框仍在,请现场确认是否登录成功。");
            }
            return RunResult.Ok("登录动作已提交,登录框已关闭。");
        }

        private int LaunchTarget(Dictionary<int, ProcessEntry> oldProcs)
        {
            int rootPid = 0;
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                Process proc = Process.Start(new ProcessStartInfo(_config.ExePath) { UseShellExecute = true });
                if (proc != null)
                {
                    rootPid = proc.Id;

                    ProcessEntry entry;
                    bool knownProc = oldProcs.TryGetValue(rootPid, out entry);
                    if (knownProc)
                    {
                        string realName = "";
                        try { realName = Process.GetProcessById(rootPid).ProcessName; } catch { }
                        _log.Info("已获得要绑定的启动进程:PID=" + rootPid + " 名称=" + realName);
                    }
                    else
                    {
                        _log.Info("已获得要绑定的启动进程:PID=" + rootPid);
                    }

                    if (string.IsNullOrEmpty(_assocBaseName) && knownProc)
                    {
                        _assocBaseName = entry.Name.ToLowerInvariant();
                    }
                }
                else
                {
                    _log.Info("Shell 启动未返回进程句柄,稍后按关联/兜底认根。");
                }
            }
            catch (Exception ex)
            {
                _log.Warning("启动软件异常(接下来尝试认根/兜底):" + ex.Message);
            }

            _log.Info("启动软件调用已返回(耗时 " + sw.ElapsedMilliseconds + "ms)");

            if (rootPid == 0)
            {
                _log.Info("未获得启动进程句柄,将按关联程序/新窗口兜底定位登录框;与其他软件同时启动时请填写 TitleKeyword 以免认错。");
            }

            return rootPid;
        }

        /// <summary>
        /// 等待登录窗口（核心状态机）：认根 → 收集候选 → 连续稳定若干轮 → 返回候选列表。
        /// </summary>
        private List<WindowInfo> WaitForLoginWindow(
            int rootPid,
            Dictionary<int, ProcessEntry> oldProcs,
            Dictionary<string, WindowInfo> oldWins,
            out int finalRootPid,
            out bool globalFallback)
        {
            DateTime deadline = DateTime.Now.AddSeconds(_config.WaitWindowSec);
            DateTime lastLog = DateTime.Now;
            string lastStableSignature = "";
            int stableRounds = 0;
            int rootGoneAtSec = -1;

            globalFallback = false;

            while (DateTime.Now < deadline)
            {
                Dictionary<int, ProcessEntry> table = ProcessTree.Snapshot();

                // --- 认根 / 进程树 ---
                if (rootPid <= 0 && !string.IsNullOrEmpty(_assocBaseName))
                {
                    int found = ProcessTree.FindByExeName(table, _assocBaseName);
                    if (found > 0 && !oldProcs.ContainsKey(found))
                    {
                        rootPid = found;
                        _globalFallback = false;
                        _log.Info("按关联程序认根成功:PID=" + rootPid + " 名称=" + table[found].Name);
                    }
                }

                if (rootPid > 0)
                {
                    if (table.ContainsKey(rootPid))
                    {
                        rootGoneAtSec = -1;
                    }
                    else if (rootGoneAtSec < 0)
                    {
                        rootGoneAtSec = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    }
                    else if ((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds() - rootGoneAtSec > RootLostSec)
                    {
                        int retry = ProcessTree.FindByExeName(table, _assocBaseName);
                        if (retry > 0)
                        {
                            rootPid = retry;
                            rootGoneAtSec = -1;
                            _log.Info("根进程已消失,按关联程序重新认根:PID=" + retry);
                        }
                        else
                        {
                            _log.Info("根进程已消失,转入全局新窗口兜底模式。");
                            rootPid = 0;
                            _globalFallback = true;
                        }
                    }
                }

                // --- 收集候选窗口 ---
                List<WindowInfo> newWins;
                if (rootPid > 0)
                {
                    HashSet<int> treeSet = ProcessTree.CollectTree(table, rootPid);
                    newWins = _locator.EnumerateNewWindows(treeSet, oldWins, false);
                }
                else
                {
                    newWins = _locator.EnumerateNewWindows(null, oldWins, true);
                }

                // --- 稳定 N 轮后才选定，防止 splash 抢跑 ---
                string signature = BuildSignature(newWins);
                if (newWins.Count > 0 && signature == lastStableSignature)
                {
                    stableRounds++;
                }
                else
                {
                    stableRounds = 1;
                    lastStableSignature = signature;
                }

                if (newWins.Count > 0 && stableRounds >= StableHoldRounds)
                {
                    _log.Info("候选窗口签名稳定(" + stableRounds + " 轮):共 " + newWins.Count + " 个" +
                              (rootPid > 0 ? "(进程树绑定模式,根 PID=" + rootPid + ")" : "(全局兜底模式)"));
                    List<WindowInfo> chosen = _locator.ChooseCandidates(newWins);
                    if (chosen.Count > 0)
                    {
                        finalRootPid = rootPid;
                        globalFallback = _globalFallback;
                        return chosen;
                    }
                }

                if ((DateTime.Now - lastLog).TotalSeconds > 15)
                {
                    lastLog = DateTime.Now;
                    int remaining = Math.Max(0, (int)(deadline - DateTime.Now).TotalSeconds);
                    _log.Info("等待登录窗口中...(剩余约 " + remaining + " 秒,模式=" +
                              (rootPid > 0 ? ("进程树绑定,根 PID=" + rootPid) : "全局兜底") + ")");
                }

                Thread.Sleep(400);
            }

            finalRootPid = rootPid;
            globalFallback = _globalFallback;
            return new List<WindowInfo>();
        }

        private static string BuildSignature(List<WindowInfo> windows)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (WindowInfo info in windows)
            {
                sb.Append(info.Handle.ToString("X") + ";");
            }
            return sb.ToString();
        }

        private static bool IsWindowOpen(IntPtr hWnd)
        {
            return hWnd != IntPtr.Zero &&
                   Interop.NativeMethods.IsWindow(hWnd) &&
                   Interop.NativeMethods.IsWindowVisible(hWnd);
        }

        private void LogConfig()
        {
            _log.Info("配置文件 => " + _paths.IniPath);
            _log.Info("配置 => ExePath=" + _config.ExePath + " | 用户名=" + _config.UserName + " | 密码=***" +
                      " | 标题关键词=" + (string.IsNullOrEmpty(_config.TitleKeyword) ? "(空)" : _config.TitleKeyword) +
                      " | WaitWindowSec=" + _config.WaitWindowSec + " | ReadyDelayMs=" + _config.ReadyDelayMs +
                      " | TypeDelayMs=" + _config.TypeDelayMs + " | 失败弹窗=" + (_config.MsgBoxOnFail ? "开" : "关") +
                      " | 用户名框序号=" + _config.UserEditOrd);
        }

        private void Stage(string text)
        {
            EventHandler<string> handler = StageChanged;
            if (handler != null)
            {
                try { handler(this, text); }
                catch { }
            }
        }
    }
}
