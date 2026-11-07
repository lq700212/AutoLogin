using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AutoLogin.Models;
using AutoLogin.Services;
using Microsoft.Win32;

namespace AutoLogin.ViewModels
{
    /// <summary>
    /// 配置界面 ViewModel：负责配置装载/保存、开机自启开关、手动运行与日志面板。
    /// </summary>
    public sealed class MainViewModel : ObservableObject
    {
        private readonly IniConfigService _configService;
        private readonly StartupService _startupService;
        private readonly ILogService _log;
        private readonly AppPaths _paths;
        private readonly Dispatcher _dispatcher;
        private readonly AsyncRelayCommand _runCommand;

        private bool _loading;

        private string _exePath = "";
        private string _userName = "";
        private string _password = "";
        private string _titleKeyword = "";
        private string _userEditOrdText = "1";
        private string _waitWindowSecText = "120";
        private string _readyDelayMsText = "3000";
        private string _typeDelayMsText = "30";
        private bool _msgBoxOnFail = true;
        private bool _retryOnStillOpen;
        private bool _autoStart;
        private bool _showPassword;
        private bool _isRunning;
        private string _statusText = "";
        private StatusLevel _statusLevel = StatusLevel.Info;

        public MainViewModel(
            IniConfigService configService,
            StartupService startupService,
            ILogService log,
            AppPaths paths)
        {
            _configService = configService;
            _startupService = startupService;
            _log = log;
            _paths = paths;
            _dispatcher = Dispatcher.CurrentDispatcher;

            Logs = new ObservableCollection<LogEntry>();

            BrowseCommand = new RelayCommand(Browse);
            SaveCommand = new RelayCommand(SaveAndReport);
            _runCommand = new AsyncRelayCommand(RunAsync, delegate { return !IsRunning; });
            OpenLogCommand = new RelayCommand(OpenLog);
            OpenFolderCommand = new RelayCommand(OpenFolder);
            ClearLogCommand = new RelayCommand(ClearLogs);
            ResetDefaultsCommand = new RelayCommand(ResetDefaults);
        }

        // ---------------- 绑定属性 ----------------
        public string ExePath
        {
            get { return _exePath; }
            set { Set(ref _exePath, value); }
        }

        public string UserName
        {
            get { return _userName; }
            set { Set(ref _userName, value); }
        }

        public string Password
        {
            get { return _password; }
            set { Set(ref _password, value); }
        }

        public string TitleKeyword
        {
            get { return _titleKeyword; }
            set { Set(ref _titleKeyword, value); }
        }

        public string UserEditOrdText
        {
            get { return _userEditOrdText; }
            set { Set(ref _userEditOrdText, value); }
        }

        public string WaitWindowSecText
        {
            get { return _waitWindowSecText; }
            set { Set(ref _waitWindowSecText, value); }
        }

        public string ReadyDelayMsText
        {
            get { return _readyDelayMsText; }
            set { Set(ref _readyDelayMsText, value); }
        }

        public string TypeDelayMsText
        {
            get { return _typeDelayMsText; }
            set { Set(ref _typeDelayMsText, value); }
        }

        public bool MsgBoxOnFail
        {
            get { return _msgBoxOnFail; }
            set { Set(ref _msgBoxOnFail, value); }
        }

        public bool RetryOnStillOpen
        {
            get { return _retryOnStillOpen; }
            set { Set(ref _retryOnStillOpen, value); }
        }

        /// <summary>
        /// 开机自动运行开关：切换即创建/删除启动项快捷方式（所见即所得）。
        /// </summary>
        public bool AutoStart
        {
            get { return _autoStart; }
            set
            {
                if (!Set(ref _autoStart, value)) return;
                OnPropertyChanged("AutoStartHint");

                if (_loading) return;

                string error;
                if (!_startupService.TrySetEnabled(value, out error))
                {
                    _autoStart = !value;
                    OnPropertyChanged("AutoStart");
                    OnPropertyChanged("AutoStartHint");
                    SetStatus("设置开机自启动失败:" + error, StatusLevel.Error);
                    return;
                }

                SetStatus(
                    value
                        ? "已开启开机自动运行:启动项快捷方式已创建(目标含 /auto 与图标)"
                        : "已关闭开机自动运行:启动项快捷方式已移除",
                    StatusLevel.Success);
            }
        }

        public string AutoStartHint
        {
            get
            {
                if (!_autoStart) return "启动项未创建";

                string target = _startupService.GetShortcutTarget();
                string self = StartupService.SelfExePath;
                if (!string.IsNullOrEmpty(target) && !SamePath(target, self))
                {
                    return "启动项指向其他路径:" + target;
                }
                return "启动项已创建(目标含 /auto,图标取本程序)";
            }
        }

        public bool ShowPassword
        {
            get { return _showPassword; }
            set { Set(ref _showPassword, value); }
        }

        public bool IsRunning
        {
            get { return _isRunning; }
            private set
            {
                if (!Set(ref _isRunning, value)) return;
                OnPropertyChanged("RunButtonText");
                if (_runCommand != null) _runCommand.RaiseCanExecuteChanged();
            }
        }

        public string RunButtonText
        {
            get { return _isRunning ? "正在运行…" : "保存并立即运行"; }
        }

        public string StatusText
        {
            get { return _statusText; }
            private set { Set(ref _statusText, value); }
        }

        public StatusLevel StatusLevel
        {
            get { return _statusLevel; }
            private set { Set(ref _statusLevel, value); }
        }

        public ObservableCollection<LogEntry> Logs { get; private set; }

        public string VersionText
        {
            get
            {
                Version version = Assembly.GetExecutingAssembly().GetName().Version;
                return version == null ? "v9.0.0" : "v" + version.ToString(3);
            }
        }

        public string ConfigPathText
        {
            get { return "AutoLogin.ini"; }
        }

        public string ConfigFullPath
        {
            get { return _paths.IniPath; }
        }

        // ---------------- 命令 ----------------
        public ICommand BrowseCommand { get; private set; }
        public ICommand SaveCommand { get; private set; }
        public ICommand RunCommand { get { return _runCommand; } }
        public ICommand OpenLogCommand { get; private set; }
        public ICommand OpenFolderCommand { get; private set; }
        public ICommand ClearLogCommand { get; private set; }
        public ICommand ResetDefaultsCommand { get; private set; }

        /// <summary>日志列表追加后触发，供视图自动滚动到最新一行。</summary>
        public event EventHandler LogEntryAdded;

        // ---------------- 初始化 ----------------
        public void Initialize()
        {
            _log.EntryWritten += OnLogEntryWritten;

            string warning = LoadConfigToView();

            foreach (LogEntry entry in _log.LoadRecent(200))
            {
                Logs.Add(entry);
            }

            if (!string.IsNullOrEmpty(warning))
            {
                SetStatus(warning, StatusLevel.Warning);
            }
            else
            {
                SetStatus("就绪。修改配置后点击“保存并立即运行”即可试跑。", StatusLevel.Info);
            }
        }

        private string LoadConfigToView()
        {
            AppConfig config = _configService.Load();

            _loading = true;
            ExePath = config.ExePath;
            UserName = config.UserName;
            Password = config.Password;
            TitleKeyword = config.TitleKeyword;
            UserEditOrdText = config.UserEditOrd.ToString();
            WaitWindowSecText = config.WaitWindowSec.ToString();
            ReadyDelayMsText = config.ReadyDelayMs.ToString();
            TypeDelayMsText = config.TypeDelayMs.ToString();
            MsgBoxOnFail = config.MsgBoxOnFail;
            RetryOnStillOpen = config.RetryOnStillOpen;

            _autoStart = _startupService.IsEnabled;
            OnPropertyChanged("AutoStart");
            OnPropertyChanged("AutoStartHint");
            _loading = false;

            if (_autoStart)
            {
                string target = _startupService.GetShortcutTarget();
                if (!string.IsNullOrEmpty(target) && !SamePath(target, StartupService.SelfExePath))
                {
                    return "注意:开机启动项指向其他路径,建议关闭后重新开启一次:" + target;
                }
            }

            return null;
        }

        // ---------------- 动作 ----------------
        private void Browse()
        {
            try
            {
                OpenFileDialog dialog = new OpenFileDialog();
                dialog.Title = "选择软件的 exe / 快捷方式 / 工程文件";
                dialog.Filter = "所有文件 (*.*)|*.*|可执行文件 (*.exe)|*.exe|快捷方式 (*.lnk)|*.lnk";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog() == true)
                {
                    ExePath = dialog.FileName;
                }
            }
            catch (Exception ex)
            {
                SetStatus("选择文件失败:" + ex.Message, StatusLevel.Error);
            }
        }

        private void SaveAndReport()
        {
            AppConfig config;
            string error;
            if (!TrySaveConfig(out config, out error))
            {
                SetStatus(error, StatusLevel.Error);
                return;
            }

            SetStatus("配置已保存:" + _paths.IniPath, StatusLevel.Success);
        }

        private bool TrySaveConfig(out AppConfig config, out string error)
        {
            config = null;
            error = null;

            string exe = (ExePath ?? "").Trim();
            if (exe.Length == 0)
            {
                error = "ExePath 不能为空,请先在“目标软件”里选择或填写路径。";
                return false;
            }

            int userEditOrd;
            int waitWindowSec;
            int readyDelayMs;
            int typeDelayMs;

            if (!TryParseInt(UserEditOrdText, "用户名框序号", 1, 99, out userEditOrd, out error)) return false;
            if (!TryParseInt(WaitWindowSecText, "等待窗口(秒)", 5, 3600, out waitWindowSec, out error)) return false;
            if (!TryParseInt(ReadyDelayMsText, "就绪延时(毫秒)", 0, 600000, out readyDelayMs, out error)) return false;
            if (!TryParseInt(TypeDelayMsText, "打字间隔(毫秒)", 1, 2000, out typeDelayMs, out error)) return false;

            AppConfig cfg = new AppConfig();
            cfg.ExePath = exe;
            cfg.UserName = (UserName ?? "").Trim();
            cfg.Password = Password ?? "";
            cfg.TitleKeyword = (TitleKeyword ?? "").Trim();
            cfg.UserEditOrd = userEditOrd;
            cfg.WaitWindowSec = waitWindowSec;
            cfg.ReadyDelayMs = readyDelayMs;
            cfg.TypeDelayMs = typeDelayMs;
            cfg.MsgBoxOnFail = MsgBoxOnFail;
            cfg.RetryOnStillOpen = RetryOnStillOpen;
            cfg.Normalize();

            try
            {
                _configService.Save(cfg);
            }
            catch (Exception ex)
            {
                error = "保存失败:" + ex.Message;
                return false;
            }

            // 把规范化后的值回写到输入框，避免界面显示与实际配置不一致
            _loading = true;
            UserEditOrdText = cfg.UserEditOrd.ToString();
            WaitWindowSecText = cfg.WaitWindowSec.ToString();
            ReadyDelayMsText = cfg.ReadyDelayMs.ToString();
            TypeDelayMsText = cfg.TypeDelayMs.ToString();
            _loading = false;

            config = cfg;
            return true;
        }

        private async Task RunAsync()
        {
            if (IsRunning) return;

            AppConfig config;
            string error;
            if (!TrySaveConfig(out config, out error))
            {
                SetStatus(error, StatusLevel.Error);
                return;
            }

            if (!File.Exists(config.ExePath))
            {
                string message = "文件不存在:" + config.ExePath + "(请检查盘符/路径)";
                _log.Error(message);
                SetStatus(message, StatusLevel.Error);
                return;
            }

            IsRunning = true;
            SetStatus("正在运行自动登录…(详情见右侧日志)", StatusLevel.Info);
            _log.Info("===== 手动运行自动登录 =====");

            try
            {
                AutoLoginRunner runner = new AutoLoginRunner(config, _log, _paths);
                runner.StageChanged += delegate(object sender, string stage)
                {
                    if (_dispatcher == null || _dispatcher.HasShutdownStarted) return;
                    try
                    {
                        _dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (IsRunning) SetStatus(stage, StatusLevel.Info);
                        }));
                    }
                    catch
                    {
                    }
                };

                RunResult result = await Task.Run(delegate { return runner.Run(); });

                if (result.Success)
                {
                    SetStatus(result.Message, result.Skipped ? StatusLevel.Warning : StatusLevel.Success);
                }
                else
                {
                    SetStatus(result.Message, StatusLevel.Error);
                    if (config.MsgBoxOnFail) ShowWarning(result.Message);
                }
            }
            catch (Exception ex)
            {
                _log.Error("运行出现异常:" + ex.Message);
                SetStatus("运行出现异常:" + ex.Message, StatusLevel.Error);
                if (config.MsgBoxOnFail) ShowWarning("运行出现异常:" + ex.Message);
            }
            finally
            {
                IsRunning = false;
            }
        }

        private void OpenLog()
        {
            try
            {
                if (File.Exists(_paths.LogPath))
                {
                    Process.Start("notepad.exe", "\"" + _paths.LogPath + "\"");
                }
                else
                {
                    SetStatus("还没有日志(先运行一次)。", StatusLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                SetStatus("打开日志失败:" + ex.Message, StatusLevel.Error);
            }
        }

        private void OpenFolder()
        {
            try
            {
                if (File.Exists(_paths.IniPath))
                {
                    Process.Start("explorer.exe", "/select,\"" + _paths.IniPath + "\"");
                }
                else
                {
                    Process.Start("explorer.exe", "\"" + _paths.BaseDirectory + "\"");
                }
            }
            catch (Exception ex)
            {
                SetStatus("打开文件夹失败:" + ex.Message, StatusLevel.Error);
            }
        }

        private void ClearLogs()
        {
            Logs.Clear();
            SetStatus("已清空界面日志(文件日志保留)。", StatusLevel.Info);
        }

        private void ResetDefaults()
        {
            MessageBoxResult choice = MessageBox.Show(
                "确定要恢复出厂默认配置吗？\r\n目标软件路径、账号密码等将全部重置。",
                "恢复默认配置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (choice != MessageBoxResult.Yes) return;

            AppConfig defaults = AppConfig.CreateDefault();
            _loading = true;
            ExePath = defaults.ExePath;
            UserName = defaults.UserName;
            Password = defaults.Password;
            TitleKeyword = defaults.TitleKeyword;
            UserEditOrdText = defaults.UserEditOrd.ToString();
            WaitWindowSecText = defaults.WaitWindowSec.ToString();
            ReadyDelayMsText = defaults.ReadyDelayMs.ToString();
            TypeDelayMsText = defaults.TypeDelayMs.ToString();
            MsgBoxOnFail = defaults.MsgBoxOnFail;
            RetryOnStillOpen = defaults.RetryOnStillOpen;
            ShowPassword = false;
            _loading = false;

            SetStatus("已恢复默认值,请重新选择目标软件后点击保存。", StatusLevel.Warning);
        }

        // ---------------- 日志 ----------------
        private void OnLogEntryWritten(object sender, LogEntry entry)
        {
            if (_dispatcher == null || _dispatcher.HasShutdownStarted) return;
            try
            {
                _dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action<LogEntry>(AppendLogEntry),
                    entry);
            }
            catch
            {
            }
        }

        private void AppendLogEntry(LogEntry entry)
        {
            Logs.Add(entry);
            while (Logs.Count > 800)
            {
                Logs.RemoveAt(0);
            }

            EventHandler handler = LogEntryAdded;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        // ---------------- 辅助 ----------------
        private void SetStatus(string message, StatusLevel level)
        {
            StatusText = message ?? "";
            StatusLevel = level;
        }

        private static bool TryParseInt(string text, string name, int min, int max, out int value, out string error)
        {
            value = 0;
            error = null;

            if (!int.TryParse((text ?? "").Trim(), out value))
            {
                error = name + " 必须是数字。";
                return false;
            }

            if (value < min || value > max)
            {
                error = name + " 的范围是 " + min + " ~ " + max + "。";
                return false;
            }
            return true;
        }

        private static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(a),
                    Path.GetFullPath(b),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void ShowWarning(string message)
        {
            try
            {
                MessageBox.Show(message, "AutoLogin 提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch
            {
            }
        }
    }
}
