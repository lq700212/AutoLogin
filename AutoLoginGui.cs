// ============================================================================
// AutoLoginGui —— AutoLogin.exe 的可视化配置界面(WPF 纯代码版,不用 XAML)
// 视觉素材复刻自 uiverse.io 组件库(E:\Project\galaxy)提取的 CSS:
//   输入框  : alexruix(圆角描边、聚焦变蓝)+ adamgiebl(聚焦三色渐变下划线)
//   开  关  : csemszepp(关=红 #ff7a7a,开=绿 #6ee07a,白色圆点手柄)
//   按  钮  : vinodjangid07(悬停抬升)
// 不用 XAML 的原因:保持旧版 csc 一条命令直接编译 => 单文件 exe、零外部依赖;
// WPF 本体是 Windows 自带运行库(PresentationFramework 等),拷走即跑性质不变。
//   双击 exe             => 打开本配置界面(可视化编辑 AutoLogin.ini 后保存)
//   AutoLogin.exe /auto  => 开机自启动静默执行主流程(不弹窗口)
// ============================================================== inex===========
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace AutoLogin
{
    internal static class AutoLoginGui
    {
        public static void RunGui()
        {
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                Environment.ExitCode = 1; // UI 线程异常兜底:不让现场闪退,只记退出码
                e.Handled = true;
            };
            app.Run(new MainForm());
        }
    }

    // -------------------------------- 主题色 --------------------------------
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromRgb(0x14, 0x16, 0x2b);          // 窗体底 #14162b
        public static readonly Color Bg2 = Color.FromRgb(0x1b, 0x1b, 0x33);         // 渐变底部 #1b1b33
        public static readonly Color Card = Color.FromRgb(0x1c, 0x20, 0x38);        // 卡片底 #1c2038
        public static readonly Color CardBorder = Color.FromRgb(0x2e, 0x35, 0x57);  // 卡片描边 #2e3557
        public static readonly Color Text = Color.FromRgb(0xe8, 0xec, 0xf4);        // 主文字
        public static readonly Color SubText = Color.FromRgb(0x8f, 0xa3, 0xc8);     // 次文字 #8fa3c8
        public static readonly Color BoxBg = Color.FromRgb(0x10, 0x13, 0x1f);       // 输入框底 #10131f
        public static readonly Color BoxBorder = Color.FromRgb(0x39, 0x45, 0x64);   // 输入框描边 #394564
        public static readonly Color Accent = Color.FromRgb(0x21, 0x96, 0xf3);      // 主强调 #2196f3
        public static readonly Color AccentDark = Color.FromRgb(0x1a, 0x73, 0xe8);  // 主按钮渐变上
        public static readonly Color AccentDark2 = Color.FromRgb(0x0f, 0x4c, 0x81); // 主按钮渐变下
        public static readonly Color GradA = Color.FromRgb(0xff, 0x64, 0x64);       // 聚焦下划线 #FF6464
        public static readonly Color GradB = Color.FromRgb(0xff, 0xbf, 0x59);       // #FFBF59
        public static readonly Color GradC = Color.FromRgb(0x47, 0xc9, 0xff);       // #47C9FF
        public static readonly Color SecFill = Color.FromRgb(0x2a, 0x32, 0x55);     // 次按钮渐变上
        public static readonly Color SecFill2 = Color.FromRgb(0x23, 0x2a, 0x48);    // 次按钮渐变下
        public static readonly Color ToggleOff = Color.FromRgb(0xff, 0x7a, 0x7a);   // 开关关 #ff7a7a
        public static readonly Color ToggleOn = Color.FromRgb(0x6e, 0xe0, 0x7a);    // 开关开 #6ee07a
        public static readonly Color Warn = Color.FromRgb(0xff, 0x96, 0x6e);        // 警示文字

        public static readonly FontFamily YaHei = new FontFamily("Microsoft YaHei UI");

        // 聚焦下划线三色渐变(横向,adamgiebl 素材)
        public static readonly Brush GradLine = CreateGradLine();

        private static Brush CreateGradLine()
        {
            LinearGradientBrush lb = new LinearGradientBrush();
            lb.StartPoint = new Point(0, 0);
            lb.EndPoint = new Point(1, 0);
            lb.GradientStops.Add(new GradientStop(GradA, 0.0));
            lb.GradientStops.Add(new GradientStop(GradB, 0.5));
            lb.GradientStops.Add(new GradientStop(GradC, 1.0));
            return lb;
        }

        public static Brush Br(Color c)
        {
            return new SolidColorBrush(c);
        }

        public static Color Lighten(Color c, int amt)
        {
            return Color.FromRgb(
                (byte)Math.Min(255, c.R + amt),
                (byte)Math.Min(255, c.G + amt),
                (byte)Math.Min(255, c.B + amt));
        }
    }

    // -------------------------- 主窗体 --------------------------
    // ---------------- Embedded asset: titlebar logo (64x64 PNG, from assets/app.png) ----------------
    internal static class Assets
    {
        public const string Logo64 =
            "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAAXNSR0IArs4c6QAAAARnQU1BAACx" +
            "jwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAzjSURBVHhe1Zt7jFTVHccn9VUoKlRta2qjTTRp" +
            "bKvVphHatNqkMa2B2kbR/tMmNWgFLSUUHwjyVCuiiIoKSgBFAakFxMfCooUoKhZYcVcR5LkrrCz7" +
            "ZGdnHzP3nl+/33Pv2Z69c2b23mWXuge+mdn7OHM/3/M7v3POnbmp7opS6oycUsPxOtv3/fV4Lcfr" +
            "Ps/398dRLqayheR1VbtDHTgO2ucpvwIqxTU+zGsWkTNDjOSlqqpqgPK8WwG7zUONqKzPCiv3w9di" +
            "4jEueeErjzEFBoiv/O249NHQgBArXlHZ7BU46d9hXZKFWpWvlYFa4sj3JV1UqlPNLnn5OhZDTaEy" +
            "waXrApaNWaWGhnjFCw6+Hqrjie2QAbcN6FYApAGFpbRsE7oIAFG5DImq04hcoCaoOQwLMnmed0OI" +
            "6S4Im+twYDNP6BE45Aa2lQzeBepSFN4WjdDdRflpmDAyxO1asll1ha/UURd8bAOcwLZOTMu7RBOY" +
            "K5DPavO6A7YP8n31tgveCeqSE9jWiW95WzSgEWLB6LS5VqnTQ3zd70dzR1/3+YIGAKAnBiSBN2pm" +
            "X0BBJIzR8NIogxH6ZdyYGJxyAtv6/7d8VIwDzD12NDXJkFQulxsOAzx7qIttgBPY1pen5W21wAEP" +
            "GbE9p0akMNF5xG59J6hLTmBbyVr+RMFTJhfkfDUnpXxVmtgAJ7Ctvml56njhqcZsYAAifwMjoIJ/" +
            "OEFdcgLb+vK2vBbgaUAOzMgDH9OAfTSA01cnsC0nsK1k8C5Ql3oTnmqAOtjoWESluGKLZYAT2NaX" +
            "v+VN61sGHIhngBPYVv9oecInN8AJbCuAL2gAAFwGpDEv1VNTiO9PFHwyA5zAtnoe9kxCe+sbtWhC" +
            "HHgfUxgqFjgF0Ch8fAOcwLZ61vIUZ6Prd+2V86fN0irZ9ZneVgie21ugdXuVlO7jxQfHOKGNAOmC" +
            "j2eAE9hWz1s+Hc7F/7JyjaT+erfWzStX622cp7tavg3Ata1Krl2qZNh8kf8c4nQ2AmwLgIXgqaIG" +
            "uIFtJYM34LYBnIbc+s9X5JS/36t1C8zgNm0AAKJiPVnU/exWJZfNE7lljUhju8L1RsApwBWDN6IB" +
            "4IkY4AS21fOWjxow5uW1nQbQDG0AkoHLAKoVsM248NFrRS5/UmSFnr4BBvuSwlP5BjiBbR1fyxs5" +
            "DXi5ewOC+buSTQeV/HSByB9fFt0taExSeHYBLv3TtgG8UekGpwL4ggYQLKIodAsU1wA9LGKYMPCm" +
            "hfn5GbyOfU3kJ0+LlOwJcwGA4sJT9VCnAbwnX9yAnrc8E1qGZGHRw10RA47hAC7LTaFpdoibKFi1" +
            "U8nlyAVT3sLx7BrcF4EsJLZ+lwgobkDPW57wbbhe3p9/6t0PZCwgy4/UaECXAWDV+8q/qJGxyAnz" +
            "Nm+RDpxLA40JfOUQuKdByTXPidywQqQGH8JthHIB2zLwMQ1I1vI2PMXWY5lW8pakxt0jqVvHy68X" +
            "LJaWLOMg3wCWDPZds2CJPjY1bqJMKXlTb2fXMFHAz2b9t78qcuVCkXcqsZTH3zTcBW1kw8cwoOct" +
            "b8SycsfHcioAh9wzU5vwq6cWSWsuMGDUS6vl5PGT5WTsv2nFKr2N+66ev1gfy3NozoodOt1r+Aw+" +
            "O4gfJbPeVvKLZ0WuXy4yaYOSOhDx2uLAN3QUNeD4Wp7iEFPTkpEfPTxPvnrHVBl093S58P6HZWvV" +
            "YZ0HWGau3yip2yZIaswEmb4OnRmF+7Z/flguun+ODLpruj730tlPyJF0Bt0BU+dGJUt3KKk6pmRZ" +
            "uZKrEAE/hwl/wohAA3jdceCLGJAMPgpOMRRZntmyTbcwW/J0GLD2k116O1uyDXV9AYMmv7FBJr1e" +
            "KtUtLXpbkORE1u7crc8ZjHNPQh3z39+qty8tF7loTjAETixV8hvkAXaDF7Gda4Si4FQIX8CA4295" +
            "ikmrA2Zet2Q5QniynDJhiox8boUO3AzyghnWMIR3FiZLk+iYO7jrxudf0ueyG/x+8TLevZHDaeQU" +
            "BMtQTIcZ/lcvERmJLlDVDCAkzKIGWPAFDSgogHUHbsRMfqipWb4/6zEZcOdUHcbPbfuQH6EBzdhO" +
            "MbnZCY4yUfD89g/1uazj4gfnSmVjszamA9ezdpfSff/Sx0XmvkfDwnNd4FQEvh5qQ13NsQwAVBID" +
            "eJHlX9TKeVjlDbxzmpwz+X5590AVPyLPAMqG1xAQ63j/YJWcc+8Duo5vT3tQdlQf1ebyM4i8u17J" +
            "Evh68BgjKwCN0/JOA1glv5I+XniKdZVV18i5U/6hL56v25DYuD1qQBSeogEcQcs+r5ZzpwZ1fAt1" +
            "bD10RN9DMJBtCHn+Y7fl30ng66BOA/grDH5gngGASQpPMZPvrq2XC2bMxsVPlTMnzsDafw+2djUg" +
            "Cm5kukDp7r36XNbBuj49Wq8jgKAuxYGnaECXCKABeREAkJ7A69kf6mpobZdhjy1AH56ix/mZGzZi" +
            "a/7cvpBY7tuwSSdA1jH0sflS19qmE2dP4Q14ICRefIbbAEAkBacIT/E9y7g1b8hXxk+SgXdNkx8+" +
            "9ISeF3CPyfYucR8jiMdeMnuePvck1DF29eudXagoOBUCF4MvbAAuvicGGHjTuqzv7f2VCOGZMmTS" +
            "TETBZD315XZOklwmcBv3sdy+6lU9hPLcM+6eIZv2HdR5gYBFDQiBu4N3GwCApOCUDW/EJS8veBSm" +
            "uJzTcyQ4DWP6+FfekHR7gMn+rL+dCd+ztHR0yIRXSuQ0hP3ZOIdT4j+jDlSPz/hf+OeBUyFwHPhe" +
            "M8AFT7FFCVbZ0CiXPPS4ns2dPek+3aevenKhLCv7SPbWNcieunrZg4TJ98vLyuWX2MdjzgI8Z5E/" +
            "mPW4HEAdNIiQvdHy+QZ4XQ2IQhZSIXgjmsBSdqhaLp41V7fmWTCBWZ2J7aIH5siFFOb9fM8JzwBM" +
            "fHgMj/3eg4/KtkOHdR29DU9pA7weGtAdPGX6Ncvuo7Xyu0Uv6NY9FV2B8/yvIcHx1X7PfRw1rl24" +
            "VD6t0T9XSgxPdQdfB7Wi7jwD4igOPGUnN3aHNix1V3xYISMAd960h+TraOlvYKZH8f13sG049r2I" +
            "7pHBsTynL+CpPAM49BDKBRxVEnjbBHODhP35kyNH9QInWCtMkeuxcGJftwtDNA86FAGbUVErXOJr" +
            "EniqFiuhTgM6YhqQtOVdso0Yt+Z1PTwOvGuq/AErv1UVO2XRB2V6Gb1wy3bZjcTIiY8Lvg3g6Zwv" +
            "lZk0DPC1EXHhed+giwH8sTGvqZgBvQGvBYBjuFh+3ph/rdUGcJ7PIY8JkEMlh8DU3ybKsLnzpTqd" +
            "0UvoKPyudKPcUrFOrtyyTEaVr5NPmhulFa3oAjZi2BM+sQG9Ca8VMeCbMIDzBK0wJwzBGmAwJlHv" +
            "VR7SuYC3sRniDPd01tPw5298Si7bvFgu2Pi03FyxHtfgof58cCMDn8iA3oZnMnMZYJKhEe8iMTK2" +
            "VIUGAJ4GMNQrMy1yFVqe8D977wW5fPMSRMJy2Y/tGexnS3cBj8DHNqAv4G0DRq1crdcKLgM4KnA+" +
            "8G7l53ptYAwIIiCHFl8nF2x6Wn4M+O8iAtgNmhAZrgiIwscyoK/gAwW3vGaU8qboHXISooDzA1v8" +
            "xvh8LIE/q2/Ut8vsLM++vhM54ObyEkTCchlVUSI7HTnA1fJGXQzgkxa2AX0LH4gjQTVWfNPXb5Sb" +
            "lq/SX44a8Zti3jJ/bedn0orjGmCYMYDizQzCHkM9BxD2fOXfeaEfgY5KG4BlQOfP5DrB+xieYkZn" +
            "dmdhNPCtEUOe29j3bXAqGOaCoY7hzj4fDftiLW9kIqBN/0xOBT+UbDpB8F1EgKgARBWDt2Gjf7uA" +
            "ozoK4aN49/rNVE4FP5XVBgCgmJzAtgAVGx4y43unLGBbLniXXLBRsfVpAAsi4NFUu1LDc+gHvJ/v" +
            "gjZyAtsCQFz4PHDKArYVBz5uyxOeakQ/6/AVmNUIPiwxOOt5O+iIC5xyAtsCQH9oeSP0MH4T9VGT" +
            "yBD9zAAMGEMDeCenP8EnbXmqjp0fpc3zbtfwLPh7kKfUO9xBqP4AT7lgo7LhayCOMB1KdX1khkWp" +
            "4KEpDkVctTmBbQGgP7U8Ex+X2HxoCqzuZwhzuRyfGeTXdcVNAEB/annC4z+fD0h3qG6eHcRxI2GC" +
            "fnCSDx3mGQGA/gJP8DpkPIZ9VvkNnufdGGIWL9lsdihMCL7OQWmBaERBqIjMMXoZa8sCtqE5vaVc" +
            "wJQOechu1agMOKGpBoQxsz27NMqmrFLDQrx4BQYMgEbzAWSwdxY9g4ohXEO3Yp+MI05b44jHhkle" +
            "2nHNmOSVgeG2wyIDQ6zkBXWd2Z7LjUBFj3i+vyGL+XMGiwj+ztalllD88UF34n35boUVW7fyFY/d" +
            "j7H9Y1zfmwCf065yv8W1Dw4xCpRU6r9kBZws+mycIgAAAABJRU5ErkJggg==";
    }

    internal class MainForm : Window
    {
        private FancyBox tbExe, tbUser, tbPass, tbTitle, tbOrd, tbWait, tbReady, tbType;
        private ToggleBox tgMsgBox, tgRetry, tgAuto;
        private FancyButton btnRun, btnSave, btnBrowse;
        private TextBlock lblStatus;
        private bool busy;

        public MainForm()
        {
            Width = 800;
            SizeToContent = SizeToContent.Height; // 宽固定 800,高随内容自适应
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;            // 圆角外透出桌面
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Title = "AutoLogin 自动登录配置";
            ShowInTaskbar = true;
            FontFamily = Theme.YaHei;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            // 根:圆角 + 垂直渐变底 + 描边
            Border root = new Border();
            root.CornerRadius = new CornerRadius(14);
            root.BorderBrush = Theme.Br(Theme.CardBorder);
            root.BorderThickness = new Thickness(1);
            LinearGradientBrush bg = new LinearGradientBrush();
            bg.StartPoint = new Point(0, 0);
            bg.EndPoint = new Point(0, 1);
            bg.GradientStops.Add(new GradientStop(Theme.Bg, 0.0));
            bg.GradientStops.Add(new GradientStop(Theme.Bg2, 1.0));
            root.Background = bg;

            Grid rootGrid = new Grid();
            root.Child = rootGrid;
            RowDefinition rd0 = new RowDefinition();
            rd0.Height = new GridLength(44);
            RowDefinition rd1 = new RowDefinition();
            rd1.Height = new GridLength(1, GridUnitType.Star);
            rootGrid.RowDefinitions.Add(rd0);
            rootGrid.RowDefinitions.Add(rd1);

            rootGrid.Children.Add(BuildTitleBar());

            Grid content = BuildContent();
            Grid.SetRow(content, 1);
            rootGrid.Children.Add(content);

            Content = root; // Window.Content
            LoadConfig();
            SetStatus("提示:开机自启动在下方\"行为\"卡片一键开启(自动生成带 /auto 与图标的启动项快捷方式)", false);
        }

        // ------------------------- 标题栏 -------------------------
        private Grid BuildTitleBar()
        {
            Grid title = new Grid();
            title.Height = 44;
            title.Background = Brushes.Transparent; // 命中拖动
            title.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    try { DragMove(); }
                    catch { }
                }
            };

            // 左侧软件图标(与 exe/任务栏/快捷方式图标同源)
            Image logo = new Image();
            logo.Width = 22; logo.Height = 22;
            logo.Source = MakeLogo();
            logo.HorizontalAlignment = HorizontalAlignment.Left;
            logo.VerticalAlignment = VerticalAlignment.Center;
            logo.Margin = new Thickness(16, 0, 0, 0);
            title.Children.Add(logo);

            TextBlock mainTitle = new TextBlock();
            mainTitle.Text = "AutoLogin 自动登录配置";
            mainTitle.FontSize = 13;
            mainTitle.FontWeight = FontWeights.Bold;
            mainTitle.Foreground = Brushes.White;
            mainTitle.VerticalAlignment = VerticalAlignment.Center;
            mainTitle.HorizontalAlignment = HorizontalAlignment.Left;
            mainTitle.Margin = new Thickness(48, 0, 0, 0);
            title.Children.Add(mainTitle);

            TextBlock sub = new TextBlock();
            sub.Text = "可视化配置  ·  保存即写入 AutoLogin.ini  ·  开机自启加参数 /auto";
            sub.FontSize = 11;
            sub.Foreground = Theme.Br(Theme.SubText);
            sub.VerticalAlignment = VerticalAlignment.Center;
            sub.HorizontalAlignment = HorizontalAlignment.Left;
            sub.Margin = new Thickness(330, 0, 0, 0);
            title.Children.Add(sub);

            CloseButton btnClose = new CloseButton();
            btnClose.Width = 36; btnClose.Height = 30;
            btnClose.HorizontalAlignment = HorizontalAlignment.Right;
            btnClose.VerticalAlignment = VerticalAlignment.Center;
            btnClose.Margin = new Thickness(0, 0, 8, 0);
            title.Children.Add(btnClose);

            return title;
        }

        // ------------------------- 内容区 -------------------------
        private Grid BuildContent()
        {
            Grid g = new Grid();
            g.Margin = new Thickness(16, 3, 16, 12);

            // 行:卡1 / 8 / 卡2 / 8 / 卡3 / 8 / 卡4 / 12 / 按钮 / 10 / 状态
            foreach (int h in new int[] { 0, 8, 0, 8, 0, 8, 0, 12, 46, 10, 0 })
            {
                RowDefinition r = new RowDefinition();
                if (h == 46) r.Height = new GridLength(46);
                else if (h == 0) r.Height = new GridLength(1, GridUnitType.Auto);
                else r.Height = new GridLength(h);
                g.RowDefinitions.Add(r);
            }

            g.Children.Add(BuildCard1());
            g.Children.Add(BuildCard2());
            g.Children.Add(BuildCard3());
            g.Children.Add(BuildCard4());

            // 按钮行
            Grid btns = new Grid();
            ColumnDefinition[] bc = MakeCols(btns, 286, 16, 130, 16, 120, 16, 184, 0);
            Grid.SetRow(btns, 8);

            btnRun = new FancyButton("保存并立即运行一次", Theme.AccentDark, Theme.AccentDark2, Colors.White, 13);
            Grid.SetColumn(btnRun, 0);
            btnRun.Clicked += delegate { RunNow(); };
            btns.Children.Add(btnRun);

            btnSave = new FancyButton("仅保存", Theme.SecFill, Theme.SecFill2, Theme.Text, 12.5);
            Grid.SetColumn(btnSave, 2);
            btnSave.Clicked += delegate
            {
                if (SaveConfig()) SetStatus("配置已保存:" + Program._iniPath, false);
            };
            btns.Children.Add(btnSave);

            FancyButton btnLog = new FancyButton("打开日志", Theme.SecFill, Theme.SecFill2, Theme.Text, 12.5);
            Grid.SetColumn(btnLog, 4);
            btnLog.Clicked += delegate { OpenLog(); };
            btns.Children.Add(btnLog);

            FancyButton btnFolder = new FancyButton("打开文件夹", Theme.SecFill, Theme.SecFill2, Theme.Text, 12.5);
            Grid.SetColumn(btnFolder, 6);
            btnFolder.Clicked += delegate
            {
                try { System.Diagnostics.Process.Start("explorer.exe", "\"" + Program._baseDir + "\""); }
                catch { }
            };
            btns.Children.Add(btnFolder);

            g.Children.Add(btns);

            // 状态行
            lblStatus = new TextBlock();
            lblStatus.FontSize = 11;
            lblStatus.Foreground = Theme.Br(Theme.SubText);
            Grid.SetRow(lblStatus, 10);
            g.Children.Add(lblStatus);
            return g;
        }

        // 固定列宽表(0 => 填充 *)
        private ColumnDefinition[] MakeCols(Grid g, params int[] widths)
        {
            ColumnDefinition[] cols = new ColumnDefinition[widths.Length];
            for (int i = 0; i < widths.Length; i++)
            {
                ColumnDefinition c = new ColumnDefinition();
                if (widths[i] <= 0) c.Width = new GridLength(1, GridUnitType.Star);
                else c.Width = new GridLength(widths[i]);
                g.ColumnDefinitions.Add(c);
                cols[i] = c;
            }
            return cols;
        }

        // 卡片 1:目标软件
        private CardPanel BuildCard1()
        {
            CardPanel card = new CardPanel("目标软件");
            Grid.SetRow(card, 0);

            TextBlock lbl = new TextBlock();
            lbl.Text = "执行文件路径(.exe / .lnk / .ezd / 其它可打开文件)";
            lbl.FontSize = 11;
            lbl.Foreground = Theme.Br(Theme.SubText);
            card.Body.Children.Add(lbl);

            Grid line = new Grid();
            line.Height = 36;
            line.Margin = new Thickness(0, 6, 0, 0);
            MakeCols(line, 0, 12, 88);

            tbExe = new FancyBox("exepath", false);
            Grid.SetColumn(tbExe, 0);
            line.Children.Add(tbExe);

            btnBrowse = new FancyButton("浏 览", Theme.SecFill, Theme.SecFill2, Theme.Text, 12.5);
            Grid.SetColumn(btnBrowse, 2);
            btnBrowse.Clicked += delegate { PickFile(); };
            line.Children.Add(btnBrowse);

            card.Body.Children.Add(line);
            return card;
        }

        // 卡片 2:登录账号(两列:用户名 / 密码)
        private CardPanel BuildCard2()
        {
            CardPanel card = new CardPanel("登录账号");
            Grid.SetRow(card, 2);

            Grid two = new Grid();
            two.Margin = new Thickness(0, 4, 0, 0);
            MakeCols(two, 0, 28, 0);

            StackPanel left = new StackPanel();
            TextBlock lu = new TextBlock();
            lu.Text = "用户名";
            lu.FontSize = 12;
            lu.Foreground = Theme.Br(Theme.Text);
            left.Children.Add(lu);
            tbUser = new FancyBox("username", false);
            tbUser.Margin = new Thickness(0, 6, 0, 0);
            left.Children.Add(tbUser);
            Grid.SetColumn(left, 0);
            two.Children.Add(left);

            StackPanel right = new StackPanel();
            TextBlock lp = new TextBlock();
            lp.Text = "密码";
            lp.FontSize = 12;
            lp.Foreground = Theme.Br(Theme.Text);
            right.Children.Add(lp);
            tbPass = new FancyBox("password", true); // 掩码
            tbPass.Margin = new Thickness(0, 6, 0, 0);
            right.Children.Add(tbPass);
            Grid.SetColumn(right, 2);
            two.Children.Add(right);

            card.Body.Children.Add(two);
            return card;
        }

        // 卡片 3:窗口识别与时序(两行)
        private CardPanel BuildCard3()
        {
            CardPanel card = new CardPanel("窗口识别与时序");
            Grid.SetRow(card, 4);

            // 行 1:标题关键词 / 用户名框序号 / 提示
            Grid row1 = new Grid();
            row1.Height = 38;
            row1.Margin = new Thickness(0, 5, 0, 0);
            MakeCols(row1, 100, 260, 24, 112, 70, 12, 178, 0);

            row1.Children.Add(PlainLabel("标题关键词", 0, TextAlignment.Left));
            tbTitle = new FancyBox("titlekeyword", false);
            Grid.SetColumn(tbTitle, 1);
            row1.Children.Add(tbTitle);
            row1.Children.Add(PlainLabel("用户名框序号", 3, TextAlignment.Left));
            tbOrd = new FancyBox("usereditord", false);
            Grid.SetColumn(tbOrd, 4);
            row1.Children.Add(tbOrd);

            TextBlock hint = new TextBlock();
            hint.Text = "用户名=第 N 个非密码输入框;认错框时改 2";
            hint.FontSize = 11;
            hint.Foreground = Theme.Br(Theme.SubText);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(hint, 6);
            row1.Children.Add(hint);

            // 行 2:等待窗口 / 就绪延时 / 打字间隔
            Grid row2 = new Grid();
            row2.Height = 38;
            row2.Margin = new Thickness(0, 8, 0, 0);
            MakeCols(row2, 100, 80, 32, 112, 80, 32, 112, 80, 0);

            row2.Children.Add(PlainLabel("等待窗口(秒)", 0, TextAlignment.Left));
            tbWait = new FancyBox("waitwindowsec", false);
            Grid.SetColumn(tbWait, 1);
            row2.Children.Add(tbWait);
            row2.Children.Add(PlainLabel("就绪延时(毫秒)", 3, TextAlignment.Left));
            tbReady = new FancyBox("readydelayms", false);
            Grid.SetColumn(tbReady, 4);
            row2.Children.Add(tbReady);
            row2.Children.Add(PlainLabel("打字间隔(毫秒)", 6, TextAlignment.Left));
            tbType = new FancyBox("typedelayms", false);
            Grid.SetColumn(tbType, 7);
            row2.Children.Add(tbType);

            card.Body.Children.Add(row1);
            card.Body.Children.Add(row2);
            return card;
        }

        // 卡片 4:行为(三个开关)
        private CardPanel BuildCard4()
        {
            CardPanel card = new CardPanel("行为");
            Grid.SetRow(card, 6);

            Grid line = new Grid();
            line.Margin = new Thickness(0, 4, 0, 2);
            MakeCols(line, 46, 10, 0, 0, 46, 10, 0);

            tgMsgBox = new ToggleBox(true);
            Grid.SetColumn(tgMsgBox, 0);
            line.Children.Add(tgMsgBox);

            TextBlock l1 = new TextBlock();
            l1.Text = "失败时弹窗提示";
            l1.FontSize = 12;
            l1.Foreground = Theme.Br(Theme.Text);
            l1.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(l1, 2);
            line.Children.Add(l1);

            tgRetry = new ToggleBox(false);
            Grid.SetColumn(tgRetry, 4);
            line.Children.Add(tgRetry);

            TextBlock l2 = new TextBlock();
            l2.Text = "提交后登录框未关闭时重试(默认关)";
            l2.FontSize = 12;
            l2.Foreground = Theme.Br(Theme.Text);
            l2.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(l2, 6);
            line.Children.Add(l2);

            card.Body.Children.Add(line);

            // 第二行:开机自动运行(一键部署启动项快捷方式,目标含 /auto、图标取本 exe)
            Grid line2 = new Grid();
            line2.Margin = new Thickness(0, 12, 0, 2);
            MakeCols(line2, 46, 10, 0);

            tgAuto = new ToggleBox(false);
            Grid.SetColumn(tgAuto, 0);
            line2.Children.Add(tgAuto);

            TextBlock l3 = new TextBlock();
            l3.Text = "开机自动运行(自动创建启动项快捷方式,目标含 /auto 参数)";
            l3.FontSize = 12;
            l3.Foreground = Theme.Br(Theme.Text);
            l3.VerticalAlignment = VerticalAlignment.Center;
            l3.HorizontalAlignment = HorizontalAlignment.Stretch;
            l3.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(l3, 2);
            line2.Children.Add(l3);

            card.Body.Children.Add(line2);
            tgAuto.Toggled += delegate(object s, EventArgs e) { ToggleAuto(); };
            return card;
        }

        private TextBlock PlainLabel(string text, int col, TextAlignment align)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 12;
            t.Foreground = Theme.Br(Theme.Text);
            t.VerticalAlignment = VerticalAlignment.Center;
            t.HorizontalAlignment = HorizontalAlignment.Stretch;
            t.TextAlignment = align;
            Grid.SetColumn(t, col);
            return t;
        }

        // ---------------------- 配置载入/保存 ----------------------
        private void LoadConfig()
        {
            Program.InitPaths();
            try { Program.LoadIni(); }
            catch (Exception ex) { SetStatus("读取 AutoLogin.ini 失败:" + ex.Message, true); }

            tbExe.Value = Program._exePath;
            tbUser.Value = Program._userName;
            tbPass.Value = Program._password;
            tbTitle.Value = Program._titleKeyword;
            tbOrd.Value = Program._userEditOrd.ToString();
            tbWait.Value = Program._waitWindowSec.ToString();
            tbReady.Value = Program._readyDelayMs.ToString();
            tbType.Value = Program._typeDelayMs.ToString();
            tgMsgBox.Checked = Program._msgBoxOnFail;
            tgRetry.Checked = Program._retryOnStillOpen;
            tgAuto.Checked = File.Exists(Program.StartupLnkPath); // 以启动项快捷方式实体为准(所见即所得)
        }

        private void ToggleAuto()
        {
            bool want = tgAuto.Checked;
            string err;
            if (!Program.InstallStartup(want, out err))
            {
                tgAuto.Checked = !want; // 失败回滚
                SetStatus((want ? "开启" : "关闭") + "开机自启动失败:" + err, true);
                return;
            }
            SetStatus(want ? "已开启开机自动运行:启动项快捷方式已创建(目标含 /auto 与图标)"
                           : "已关闭开机自动运行:启动项快捷方式已移除", false);
        }

        private static int ParseOrDefault(string s, int def)
        {
            int v;
            return int.TryParse(s, out v) ? v : def;
        }

        private static BitmapImage MakeLogo()
        {
            BitmapImage bi = new BitmapImage();
            bi.BeginInit();
            bi.StreamSource = new MemoryStream(Convert.FromBase64String(Assets.Logo64));
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }

        private void SetStatus(string msg, bool warn)
        {
            if (lblStatus == null) return;
            lblStatus.Text = msg;
            lblStatus.Foreground = Theme.Br(warn ? Theme.Warn : Theme.SubText);
        }

        private bool SaveConfig()
        {
            string exe = tbExe.Value == null ? "" : tbExe.Value.Trim();
            if (exe.Length == 0)
            {
                SetStatus("ExePath 不能为空,请先在\"目标软件\"里选择或填写路径", true);
                return false;
            }
            string u = tbUser.Value.Trim();
            string p = tbPass.Value == null ? "" : tbPass.Value;
            string t = tbTitle.Value.Trim();
            int ord = Math.Max(1, ParseOrDefault(tbOrd.Value.Trim(), Program._userEditOrd));
            int wait = Math.Max(5, ParseOrDefault(tbWait.Value.Trim(), Program._waitWindowSec));
            int ready = Math.Max(0, ParseOrDefault(tbReady.Value.Trim(), Program._readyDelayMs));
            int type = Math.Max(1, ParseOrDefault(tbType.Value.Trim(), Program._typeDelayMs));

            Program._exePath = exe;
            Program._userName = u;
            Program._password = p;
            Program._titleKeyword = t;
            Program._userEditOrd = ord;
            Program._waitWindowSec = wait;
            Program._readyDelayMs = ready;
            Program._typeDelayMs = type;
            Program._msgBoxOnFail = tgMsgBox.Checked;
            Program._retryOnStillOpen = tgRetry.Checked;

            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (string line in Program.IniTemplate.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith(";")) sb.AppendLine(line);
                }
                sb.AppendLine("ExePath=" + exe);
                sb.AppendLine("UserName=" + u);
                sb.AppendLine("Password=" + p);
                sb.AppendLine("TitleKeyword=" + t);
                sb.AppendLine("WaitWindowSec=" + wait);
                sb.AppendLine("ReadyDelayMs=" + ready);
                sb.AppendLine("TypeDelayMs=" + type);
                sb.AppendLine("MsgBoxOnFail=" + (tgMsgBox.Checked ? "1" : "0"));
                sb.AppendLine("RetryOnStillOpen=" + (tgRetry.Checked ? "1" : "0"));
                sb.AppendLine("UserEditOrd=" + ord);
                File.WriteAllText(Program._iniPath, sb.ToString(), new UTF8Encoding(true));
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("保存失败:" + ex.Message, true);
                return false;
            }
        }

        // ---------------------- 动作 ----------------------
        private void PickFile()
        {
            try
            {
                OpenFileDialog ofd = new OpenFileDialog(); // WPF 自带(Microsoft.Win32)
                ofd.Title = "选择软件的 exe / 快捷方式 / 工程文件";
                ofd.Filter = "所有文件 (*.*)|*.*|可执行文件 (*.exe)|*.exe|快捷方式 (*.lnk)|*.lnk";
                ofd.CheckFileExists = true;
                if (ofd.ShowDialog(this) == true) tbExe.Value = ofd.FileName;
            }
            catch { }
        }

        private void OpenLog()
        {
            try
            {
                if (File.Exists(Program._logPath))
                    System.Diagnostics.Process.Start("notepad.exe", "\"" + Program._logPath + "\"");
                else
                    SetStatus("还没有日志(先运行一次)", true);
            }
            catch (Exception ex) { SetStatus("打开日志失败:" + ex.Message, true); }
        }

        private void RunNow()
        {
            if (busy) return;
            if (!SaveConfig()) return;
            if (!File.Exists(Program._exePath))
            {
                SetStatus("文件不存在:" + Program._exePath + "(请检查盘符/路径)", true);
                return;
            }
            busy = true;
            SetStatus("正在运行自动登录…(详情见 AutoLogin.log;若弹出软件属正常)", false);
            Thread t = new Thread(delegate()
            {
                string err = null;
                try { Program.Run(); }
                catch (Exception ex) { err = ex.Message; }

                try
                {
                    Dispatcher.BeginInvoke(new Action(delegate()
                    {
                        busy = false;
                        SetStatus(err == null ? "运行结束,结果以 AutoLogin.log 为准" : "运行出现异常:" + err, err != null);
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }
    }

    // ---------------------- 自绘控件:卡片 ----------------------
    internal class CardPanel : Border
    {
        public readonly StackPanel Body;

        public CardPanel(string title)
        {
            CornerRadius = new CornerRadius(12);
            Background = Theme.Br(Theme.Card);
            BorderBrush = Theme.Br(Theme.CardBorder);
            BorderThickness = new Thickness(1.5);
            Padding = new Thickness(14, 10, 14, 12);

            Grid g = new Grid();
            RowDefinition rt = new RowDefinition();
            rt.Height = new GridLength(1, GridUnitType.Auto);
            g.RowDefinitions.Add(rt);
            g.RowDefinitions.Add(new RowDefinition());

            TextBlock t = new TextBlock();
            t.Text = title;
            t.FontSize = 13;
            t.FontWeight = FontWeights.Bold;
            t.Foreground = Theme.Br(Theme.Accent);
            g.Children.Add(t);
            Grid.SetRow(t, 0);

            Body = new StackPanel();
            Grid.SetRow(Body, 1);
            g.Children.Add(Body);
            Child = g;
        }
    }

    // ----------------- 自绘控件:圆角输入框(含聚焦三色渐变下划线) -----------------
    internal class FancyBox : Border
    {
        private readonly TextBox _tb;
        private readonly PasswordBox _pb;
        private readonly TextBlock _cue;
        private readonly Rectangle _line;
        private readonly bool _pass;

        public FancyBox(string cue, bool pass)
        {
            _pass = pass;
            CornerRadius = new CornerRadius(10);
            Background = Theme.Br(Theme.BoxBg);
            BorderBrush = Theme.Br(Theme.BoxBorder);
            BorderThickness = new Thickness(1.5);

            Grid g = new Grid();
            Child = g;

            // 聚焦三色渐变下划线(adamgiebl 素材):默认隐藏,聚焦渐现
            _line = new Rectangle();
            _line.Height = 3;
            _line.RadiusX = 1.5;
            _line.RadiusY = 1.5;
            _line.Fill = Theme.GradLine;
            _line.Opacity = 0;
            _line.VerticalAlignment = VerticalAlignment.Bottom;
            _line.Margin = new Thickness(12, 0, 12, 2);
            g.Children.Add(_line);

            // 占位提示(灰色;文本非空自动隐藏 —— 对应 WinForms 版 EM_SETCUEBANNER)
            _cue = new TextBlock();
            _cue.Text = cue;
            _cue.FontSize = 12;
            _cue.Foreground = Theme.Br(Theme.SubText);
            _cue.VerticalAlignment = VerticalAlignment.Center;
            _cue.Margin = new Thickness(14, 0, 14, 8);
            _cue.IsHitTestVisible = false;
            g.Children.Add(_cue);

            if (pass)
            {
                _pb = new PasswordBox();
                Makeup(_pb);
                _pb.PasswordChanged += delegate { UpdateCue(); };
                g.Children.Add(_pb);
            }
            else
            {
                _tb = new TextBox();
                Makeup(_tb);
                _tb.TextChanged += delegate { UpdateCue(); };
                g.Children.Add(_tb);
            }

            GotKeyboardFocus += delegate
            {
                BorderBrush = Theme.Br(Theme.Accent);
                DoubleAnimation a = new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(160)));
                _line.BeginAnimation(OpacityProperty, a);
            };
            LostKeyboardFocus += delegate
            {
                BorderBrush = Theme.Br(Theme.BoxBorder);
                DoubleAnimation a = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(160)));
                _line.BeginAnimation(OpacityProperty, a);
            };
        }

        private void Makeup(System.Windows.Controls.Primitives.TextBoxBase tb)
        {
            tb.Background = Brushes.Transparent;
            tb.BorderThickness = new Thickness(0);
            tb.Foreground = Brushes.White;
            tb.CaretBrush = Brushes.White;   // 深底上光标必须发白,否则看不见
            tb.FontSize = 13;
            tb.VerticalAlignment = VerticalAlignment.Center;
            tb.Margin = new Thickness(14, 2, 14, 8);
        }

        private void Makeup(PasswordBox pb)
        {
            pb.Background = Brushes.Transparent;
            pb.BorderThickness = new Thickness(0);
            pb.Foreground = Brushes.White;
            pb.CaretBrush = Brushes.White;
            pb.FontSize = 14; // 掩码圆点稍大更醒目
            pb.VerticalAlignment = VerticalAlignment.Center;
            pb.Margin = new Thickness(14, 2, 14, 8);
        }

        private void UpdateCue()
        {
            int len = _pass ? (_pb == null ? 0 : _pb.Password.Length)
                            : (_tb == null ? 0 : _tb.Text.Length);
            _cue.Visibility = len == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public string Value
        {
            get { return _pass ? _pb.Password : _tb.Text; }
            set
            {
                string v = value == null ? "" : value;
                if (_pass) _pb.Password = v;
                else _tb.Text = v;
                UpdateCue();
            }
        }
    }

    // ---------------------- 自绘控件:开关(csemszepp 素材) ----------------------
    internal class ToggleBox : Grid
    {
        private readonly Border _track;
        private readonly Ellipse _dot;
        private readonly TranslateTransform _tx;
        private bool _chk;

        public ToggleBox(bool chk)
        {
            Width = 46;
            Height = 22;
            Cursor = Cursors.Hand;
            Background = Brushes.Transparent; // 整体可点

            _tx = new TranslateTransform();
            _track = new Border();
            _track.CornerRadius = new CornerRadius(11);
            _track.Background = new SolidColorBrush(Theme.ToggleOff); // 保持未冻结:供颜色动画
            Children.Add(_track);

            _dot = new Ellipse();
            _dot.Width = 16; _dot.Height = 16;
            _dot.Fill = Brushes.White;
            _dot.HorizontalAlignment = HorizontalAlignment.Left;
            _dot.VerticalAlignment = VerticalAlignment.Center;
            _dot.Margin = new Thickness(3, 0, 0, 0);
            _dot.RenderTransform = _tx;
            Children.Add(_dot);

            SetChecked(chk, false);
            MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                Checked = !Checked;
                if (Toggled != null) Toggled(this, EventArgs.Empty);
            };
        }

        public bool Checked
        {
            get { return _chk; }
            set { SetChecked(value, true); }
        }

        public event EventHandler Toggled;

        private void SetChecked(bool v, bool animate)
        {
            _chk = v;
            SolidColorBrush b = _track.Background as SolidColorBrush;
            SolidColorBrush target = new SolidColorBrush(v ? Theme.ToggleOn : Theme.ToggleOff);
            if (animate && b != null)
            {
                ColorAnimation ca = new ColorAnimation(target.Color, new Duration(TimeSpan.FromMilliseconds(180)));
                b.BeginAnimation(SolidColorBrush.ColorProperty, ca);
            }
            else if (b != null)
            {
                b.Color = target.Color;
            }

            double x = v ? 24 : 0; // 手柄滑动(46 宽 - 16 点 - 3*2 边距)
            if (animate)
            {
                DoubleAnimation da = new DoubleAnimation(x, new Duration(TimeSpan.FromMilliseconds(180)));
                da.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                _tx.BeginAnimation(TranslateTransform.XProperty, da);
            }
            else
            {
                _tx.X = x;
            }
        }
    }

    // ---------------------- 自绘控件:按钮(vinodjangid07 悬停抬升) ----------------------
    internal class FancyButton : Border
    {
        public event EventHandler Clicked;

        private readonly TextBlock _label;
        private readonly TranslateTransform _ty;
        private readonly Brush _n, _h;

        public FancyButton(string text, Color c1, Color c2, Color fore, double fontSize)
        {
            CornerRadius = new CornerRadius(10);
            BorderThickness = new Thickness(1.2);
            _n = Grad(c1, c2);
            _h = Grad(Theme.Lighten(c1, 24), Theme.Lighten(c2, 24));
            Background = _n;
            BorderBrush = Theme.Br(Theme.BoxBorder);

            _label = new TextBlock();
            _label.Text = text;
            _label.FontSize = fontSize;
            _label.FontWeight = FontWeights.Bold;
            _label.Foreground = Theme.Br(fore);
            _label.HorizontalAlignment = HorizontalAlignment.Center;
            _label.VerticalAlignment = VerticalAlignment.Center;
            _label.Margin = new Thickness(0, 0, 0, 1);
            Child = _label;

            _ty = new TranslateTransform();
            RenderTransform = _ty;
            Cursor = Cursors.Hand;
            Background = _n;

            MouseEnter += delegate
            {
                Background = _h;
                BorderBrush = Theme.Br(Theme.Accent);
                SlideTo(-2);
            };
            MouseLeave += delegate
            {
                Background = _n;
                BorderBrush = Theme.Br(Theme.BoxBorder);
                SlideTo(0);
            };
            MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    Mouse.Capture(this);
                    _ty.Y = -1; // 按压微沉
                    e.Handled = true;
                }
            };
            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                bool had = Mouse.Captured == this;
                Mouse.Capture(null);
                _ty.Y = IsMouseOver ? -2 : 0;
                if (had && IsMouseOver && Clicked != null) Clicked(this, EventArgs.Empty);
            };
        }

        private static Brush Grad(Color c1, Color c2)
        {
            LinearGradientBrush lb = new LinearGradientBrush();
            lb.StartPoint = new Point(0, 0);
            lb.EndPoint = new Point(0, 1);
            lb.GradientStops.Add(new GradientStop(c1, 0.0));
            lb.GradientStops.Add(new GradientStop(c2, 1.0));
            return lb;
        }

        private void SlideTo(double y)
        {
            DoubleAnimation a = new DoubleAnimation(y, new Duration(TimeSpan.FromMilliseconds(140)));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            _ty.BeginAnimation(TranslateTransform.YProperty, a);
        }
    }

    // ---------------------- 自绘控件:标题关闭 ----------------------
    internal class CloseButton : Border
    {
        private bool _hover;
        private readonly TextBlock _x;

        public CloseButton()
        {
            CornerRadius = new CornerRadius(15, 15, 15, 15);
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            _x = new TextBlock();
            _x.Text = "✕";
            _x.FontSize = 14;
            _x.Foreground = Theme.Br(Theme.SubText);
            _x.HorizontalAlignment = HorizontalAlignment.Center;
            _x.VerticalAlignment = VerticalAlignment.Center;
            Child = _x;

            MouseEnter += delegate { _hover = true; Refresh(); };
            MouseLeave += delegate { _hover = false; Refresh(); };
            MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                Window w = Window.GetWindow(this);
                if (w != null) w.Close();
            };
        }

        private void Refresh()
        {
            Background = _hover ? Theme.Br(Color.FromRgb(0xe5, 0x39, 0x35)) : Brushes.Transparent;
            _x.Foreground = Theme.Br(_hover ? Colors.White : Theme.SubText);
        }
    }
}
