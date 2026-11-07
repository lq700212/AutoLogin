using System;
using System.Windows;
using System.Windows.Threading;
using AutoLogin.Interop;
using AutoLogin.Models;
using AutoLogin.Services;
using AutoLogin.ViewModels;

namespace AutoLogin
{
    /// <summary>
    /// 程序入口。双形态：
    ///   双击（无参数）      -> 打开可视化配置界面
    ///   /auto /run /silent -> 静默执行自动登录（开机自启动形态）
    /// </summary>
    public partial class App : Application
    {
        private LogService _logService;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            try
            {
                if (IsSilentMode(e.Args))
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    int exitCode = RunSilent();
                    Shutdown(exitCode);
                    return;
                }

                RunGui();
            }
            catch (Exception ex)
            {
                if (_logService != null)
                {
                    _logService.Error("程序启动失败:" + ex);
                }

                try
                {
                    MessageBox.Show(
                        "程序启动失败:" + ex.Message + "\r\n\r\n" + ex,
                        "AutoLogin 启动失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch
                {
                }

                Shutdown(1);
            }
        }

        private static bool IsSilentMode(string[] args)
        {
            if (args == null) return false;

            foreach (string arg in args)
            {
                string text = (arg ?? "").Trim().ToLowerInvariant();
                if (text == "/auto" || text == "-auto" ||
                    text == "/run" || text == "-run" ||
                    text == "/silent" || text == "-silent")
                {
                    return true;
                }
            }
            return false;
        }

        private int RunSilent()
        {
            AppPaths paths = AppPaths.ForCurrentProcess();
            _logService = new LogService(paths.LogPath);
            _logService.RotateIfNeeded();

            AppConfig config = null;
            try
            {
                IniConfigService configService = new IniConfigService(paths, _logService);
                config = configService.Load();

                AutoLoginRunner runner = new AutoLoginRunner(config, _logService, paths);
                RunResult result = runner.Run();

                if (!result.Success)
                {
                    _logService.Error("自动登录失败:" + result.Message.Replace("\r\n", " | "));
                    if (config.MsgBoxOnFail) Popup(result.Message);
                    return 1;
                }

                if (result.Skipped)
                {
                    _logService.Warning(result.Message);
                }
                return 0;
            }
            catch (Exception ex)
            {
                _logService.Error("自动登录发生异常:" + ex);
                bool showBox = config == null || config.MsgBoxOnFail;
                if (showBox) Popup("自动登录发生异常:" + ex.Message + "\r\n详情见 AutoLogin.log");
                return 1;
            }
        }

        private void RunGui()
        {
            AppPaths paths = AppPaths.ForCurrentProcess();
            _logService = new LogService(paths.LogPath);
            _logService.RotateIfNeeded();

            IniConfigService configService = new IniConfigService(paths, _logService);
            StartupService startupService = new StartupService(paths, _logService);
            MainViewModel viewModel = new MainViewModel(configService, startupService, _logService, paths);

            AutoLogin.Views.MainWindow window = new AutoLogin.Views.MainWindow();
            window.DataContext = viewModel;
            this.MainWindow = window;

            viewModel.Initialize();
            window.Show();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            if (_logService != null)
            {
                _logService.Error("界面线程异常:" + e.Exception);
            }

            try
            {
                MessageBox.Show(
                    "程序发生异常:" + e.Exception.Message + "\r\n详情见 AutoLogin.log",
                    "AutoLogin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
            }

            e.Handled = true;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (_logService != null)
            {
                Exception ex = e.ExceptionObject as Exception;
                _logService.Error("未处理异常:" + (ex == null ? e.ExceptionObject.ToString() : ex.ToString()));
            }
        }

        private static void Popup(string message)
        {
            try
            {
                NativeMethods.MessageBoxW(IntPtr.Zero, message, "AutoLogin 提示", 0x40);
            }
            catch
            {
            }
        }
    }
}
