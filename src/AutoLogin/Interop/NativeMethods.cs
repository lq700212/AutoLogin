using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoLogin.Interop
{
    /// <summary>
    /// 全部 Win32 交互集中在此，业务代码不直接写 DllImport。
    /// </summary>
    internal static class NativeMethods
    {
        // ---------------- 窗口样式 / 消息 ----------------
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int ES_PASSWORD = 0x0020;

        public const uint WM_SETTEXT = 0x000C;
        public const uint WM_GETTEXT = 0x000D;
        public const uint WM_GETTEXTLENGTH = 0x000E;
        public const uint BM_CLICK = 0x00F5;

        // ---------------- 输入 ----------------
        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_UNICODE = 0x0004;

        public const ushort VK_BACK = 0x08;
        public const ushort VK_TAB = 0x09;
        public const ushort VK_RETURN = 0x0D;
        public const ushort VK_MENU = 0x12;

        // ---------------- 进程快照 ----------------
        public const uint TH32CS_SNAPPROCESS = 0x00000002;

        // ---------------- 文件关联 ----------------
        public const uint ASSOCF_NONE = 0;
        public const uint ASSOCSTR_COMMAND = 0;

        // ---------------- 结构体 ----------------
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public MOUSEINPUT mi;
            [FieldOffset(8)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PROCESSENTRY32W
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

        // ---------------- 委托 ----------------
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // ---------------- user32 ----------------
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageText(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageBuffer(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam);

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessageInt(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetFocus();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

        // ---------------- kernel32 ----------------
        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr hObject);

        // ---------------- shlwapi ----------------
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint AssocQueryStringW(
            uint flags,
            uint str,
            string pszAssoc,
            string pszExtra,
            [Out] StringBuilder pszOut,
            ref uint pcchOut);

        // ---------------- 高层辅助 ----------------
        public static int GetWindowStyle(IntPtr hWnd)
        {
            if (IntPtr.Size == 8)
            {
                return (int)GetWindowLongPtr64(hWnd, GWL_STYLE).ToInt64();
            }
            return GetWindowLong32(hWnd, GWL_STYLE);
        }

        public static int GetWindowExStyle(IntPtr hWnd)
        {
            if (IntPtr.Size == 8)
            {
                return (int)GetWindowLongPtr64(hWnd, GWL_EXSTYLE).ToInt64();
            }
            return GetWindowLong32(hWnd, GWL_EXSTYLE);
        }

        public static string GetClassNameOf(IntPtr hWnd)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetWindowTitleOf(IntPtr hWnd)
        {
            StringBuilder sb = new StringBuilder(512);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetControlText(IntPtr hWnd)
        {
            StringBuilder sb = new StringBuilder(1024);
            SendMessageBuffer(hWnd, WM_GETTEXT, (IntPtr)sb.Capacity, sb);
            return sb.ToString();
        }

        public static int GetControlTextLength(IntPtr hWnd)
        {
            return (int)SendMessageInt(hWnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        }

        public static bool SetControlText(IntPtr hWnd, string text)
        {
            try
            {
                return SendMessageText(hWnd, WM_SETTEXT, IntPtr.Zero, text) != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }
    }
}
