using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using AutoLogin.Interop;
using AutoLogin.Models;

namespace AutoLogin.Services
{
    public struct WindowInfo
    {
        public IntPtr Handle;
        public uint Pid;
        public string Title;
        public int Score;
    }

    /// <summary>
    /// 桌面窗口枚举、筛选与激活。只负责“找窗口/点窗口”，不涉及输入逻辑。
    /// </summary>
    internal sealed class WindowLocator
    {
        private readonly AppConfig _config;
        private readonly ILogService _log;
        private readonly int _selfPid;

        public WindowLocator(AppConfig config, ILogService log, int selfPid)
        {
            _config = config;
            _log = log;
            _selfPid = selfPid;
        }

        /// <summary>
        /// 只按窗口自身属性判断（不查进程，保证轮询快）：可见 + 非工具窗 + 可激活 + 尺寸合适。
        /// </summary>
        public bool IsSuitableWindow(IntPtr hWnd, out string title, out uint pid)
        {
            title = null;
            pid = 0;

            if (!NativeMethods.IsWindowVisible(hWnd)) return false;
            NativeMethods.GetWindowThreadProcessId(hWnd, out pid);
            if ((int)pid == _selfPid) return false;

            int exStyle = NativeMethods.GetWindowExStyle(hWnd);
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return false;
            if ((exStyle & NativeMethods.WS_EX_NOACTIVATE) != 0) return false;

            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(hWnd, out rect)) return false;
            if (rect.Right - rect.Left < 120 || rect.Bottom - rect.Top < 80) return false;

            title = NativeMethods.GetWindowTitleOf(hWnd);
            return true;
        }

        public int ScoreOf(string title)
        {
            if (title == null) return 0;

            int score = 1;
            string lower = title.ToLowerInvariant();
            if (lower.Contains("登录") || lower.Contains("登陆") || lower.Contains("用户") ||
                lower.Contains("密码") || lower.Contains("login") || lower.Contains("sign") ||
                lower.Contains("password"))
            {
                score += 10;
            }
            return score;
        }

        /// <summary>
        /// 桌面当前可见窗口快照。
        /// </summary>
        public Dictionary<string, WindowInfo> SnapshotWindows()
        {
            Dictionary<string, WindowInfo> result = new Dictionary<string, WindowInfo>();

            NativeMethods.EnumWindowsProc callback = delegate(IntPtr h, IntPtr lp)
            {
                string title;
                uint pid;
                if (IsSuitableWindow(h, out title, out pid))
                {
                    WindowInfo info = new WindowInfo();
                    info.Handle = h;
                    info.Pid = pid;
                    info.Title = title;
                    info.Score = ScoreOf(title);
                    result[h.ToString()] = info;
                }
                return true;
            };

            NativeMethods.EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return result;
        }

        /// <summary>
        /// 枚举新出现的候选窗口。
        /// treeSet 非空时按进程树筛选；globalFallback=true 时按“启动前快照之外的新窗口”筛选。
        /// </summary>
        public List<WindowInfo> EnumerateNewWindows(
            HashSet<int> treeSet,
            IDictionary<string, WindowInfo> oldWins,
            bool globalFallback)
        {
            List<WindowInfo> result = new List<WindowInfo>();

            NativeMethods.EnumWindowsProc callback = delegate(IntPtr h, IntPtr lp)
            {
                string title;
                uint pid;
                if (!IsSuitableWindow(h, out title, out pid)) return true;

                if (globalFallback)
                {
                    if (oldWins != null && oldWins.ContainsKey(h.ToString())) return true;
                }
                else
                {
                    if (treeSet == null || !treeSet.Contains((int)pid)) return true;
                }

                if (!string.IsNullOrEmpty(_config.TitleKeyword) &&
                    title.IndexOf(_config.TitleKeyword, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return true;
                }

                WindowInfo info = new WindowInfo();
                info.Handle = h;
                info.Pid = pid;
                info.Title = title;
                info.Score = ScoreOf(title);
                result.Add(info);
                return true;
            };

            NativeMethods.EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return result;
        }

        /// <summary>
        /// 兜底：在现有窗口里按标题关键词找（软件早开着的手动场景）。
        /// </summary>
        public List<WindowInfo> FindExistingWindowsByTitle()
        {
            List<WindowInfo> raw = new List<WindowInfo>();

            NativeMethods.EnumWindowsProc callback = delegate(IntPtr h, IntPtr lp)
            {
                string title;
                uint pid;
                if (!IsSuitableWindow(h, out title, out pid)) return true;

                if (string.IsNullOrEmpty(_config.TitleKeyword)) return true;
                if (title.IndexOf(_config.TitleKeyword, StringComparison.OrdinalIgnoreCase) < 0) return true;

                WindowInfo info = new WindowInfo();
                info.Handle = h;
                info.Pid = pid;
                info.Title = title;
                info.Score = ScoreOf(title);
                raw.Add(info);
                return true;
            };

            NativeMethods.EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return ChooseCandidates(raw);
        }

        /// <summary>
        /// 过滤系统进程窗口并按登录特征分排序。
        /// </summary>
        public List<WindowInfo> ChooseCandidates(List<WindowInfo> raw)
        {
            List<WindowInfo> result = new List<WindowInfo>();
            foreach (WindowInfo info in raw)
            {
                if (!IsSystemOwner(info.Pid))
                {
                    result.Add(info);
                }
                else
                {
                    _log.Info("跳过系统窗口:0x" + info.Handle.ToString("X") + " ~" + info.Title + "~");
                }
            }

            result.Sort(delegate(WindowInfo a, WindowInfo b)
            {
                return b.Score.CompareTo(a.Score);
            });
            return result;
        }

        public static string Describe(List<WindowInfo> windows)
        {
            StringBuilder sb = new StringBuilder();
            foreach (WindowInfo info in windows)
            {
                sb.Append("0x" + info.Handle.ToString("X") + "(~" + info.Title + "~) ");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 激活窗口（带 ALT 技巧解除前台锁定），返回是否确认已到 foreground。
        /// </summary>
        public static bool FocusWindow(IntPtr hWnd, ILogService log)
        {
            for (int i = 0; i < 10; i++)
            {
                if (NativeMethods.IsIconic(hWnd))
                {
                    NativeMethods.ShowWindow(hWnd, 9); // SW_RESTORE
                }

                NativeMethods.keybd_event((byte)NativeMethods.VK_MENU, 0, 0, IntPtr.Zero);
                NativeMethods.keybd_event((byte)NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_KEYUP, IntPtr.Zero);
                NativeMethods.SetForegroundWindow(hWnd);
                Thread.Sleep(300);

                if (NativeMethods.GetForegroundWindow() == hWnd) return true;
            }

            if (log != null)
            {
                log.Warning("焦点确认失败(多次尝试后窗口仍未到前台)。");
            }
            return false;
        }

        /// <summary>
        /// 系统进程黑名单：仅对最终候选查询，次数很少。
        /// </summary>
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
            catch
            {
            }
            return false;
        }
    }
}
