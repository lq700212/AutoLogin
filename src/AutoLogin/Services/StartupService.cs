using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using AutoLogin.Interop;

namespace AutoLogin.Services
{
    /// <summary>
    /// 开机自启动管理：在“启动”文件夹创建/删除指向本程序的快捷方式（带 /auto 参数）。
    /// </summary>
    public sealed class StartupService
    {
        private readonly AppPaths _paths;
        private readonly ILogService _log;

        public StartupService(AppPaths paths, ILogService log)
        {
            _paths = paths;
            _log = log;
        }

        public bool IsEnabled
        {
            get { return File.Exists(_paths.StartupShortcutPath); }
        }

        /// <summary>
        /// 当前启动项指向的目标路径；不存在或读取失败返回 null。
        /// </summary>
        public string GetShortcutTarget()
        {
            if (!File.Exists(_paths.StartupShortcutPath)) return null;
            return ShellLink.ResolveTarget(_paths.StartupShortcutPath);
        }

        public bool TrySetEnabled(bool enabled, out string error)
        {
            error = null;
            try
            {
                if (!enabled)
                {
                    if (File.Exists(_paths.StartupShortcutPath))
                    {
                        File.Delete(_paths.StartupShortcutPath);
                        _log.Info("已删除开机自启动快捷方式:" + _paths.StartupShortcutPath);
                    }
                    return true;
                }

                string exe = SelfExePath;
                ShellLink.CreateShortcut(
                    _paths.StartupShortcutPath,
                    exe,
                    " /auto",
                    Path.GetDirectoryName(exe),
                    exe,
                    "AutoLogin 开机自启动(静默模式,含 /auto 参数)");

                _log.Info("已创建开机自启动快捷方式:" + _paths.StartupShortcutPath +
                          " -> \"" + exe + "\" /auto(图标取 exe)");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _log.Error("部署开机自启动快捷方式失败:" + error);
                return false;
            }
        }

        public static string SelfExePath
        {
            get
            {
                try
                {
                    string path = Assembly.GetEntryAssembly().Location;
                    if (!string.IsNullOrEmpty(path)) return path;
                }
                catch
                {
                }

                try
                {
                    return Process.GetCurrentProcess().MainModule.FileName;
                }
                catch
                {
                    return Environment.GetCommandLineArgs()[0];
                }
            }
        }
    }
}
