using System;
using System.IO;

namespace AutoLogin.Services
{
    /// <summary>
    /// 程序使用的固定路径。全部以 exe 所在目录为基准，保持“拷走即用”。
    /// </summary>
    public sealed class AppPaths
    {
        public AppPaths(string baseDirectory)
        {
            if (string.IsNullOrEmpty(baseDirectory))
                baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

            BaseDirectory = baseDirectory;
            IniPath = Path.Combine(baseDirectory, "AutoLogin.ini");
            LogPath = Path.Combine(baseDirectory, "AutoLogin.log");
            StartupShortcutPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                "AutoLogin.lnk");
        }

        public string BaseDirectory { get; private set; }
        public string IniPath { get; private set; }
        public string LogPath { get; private set; }
        public string StartupShortcutPath { get; private set; }

        public static AppPaths ForCurrentProcess()
        {
            return new AppPaths(AppDomain.CurrentDomain.BaseDirectory);
        }
    }
}
