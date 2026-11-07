using System;
using System.IO;
using System.Text;
using AutoLogin.Models;

namespace AutoLogin.Services
{
    /// <summary>
    /// AutoLogin.ini 读写。保持旧版文件格式与全部键名，现场旧配置可直接沿用。
    /// </summary>
    public sealed class IniConfigService
    {
        private static readonly Encoding Utf8Bom = new UTF8Encoding(true);
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly AppPaths _paths;
        private readonly ILogService _log;

        public IniConfigService(AppPaths paths, ILogService log)
        {
            _paths = paths;
            _log = log;
        }

        public const string Template =
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

        public AppConfig Load()
        {
            AppConfig config = AppConfig.CreateDefault();

            try
            {
                if (!File.Exists(_paths.IniPath))
                {
                    File.WriteAllText(_paths.IniPath, Template, Utf8Bom);
                    _log.Info("未找到配置文件,已自动生成默认 AutoLogin.ini(本次按默认值继续,若失败请看日志)。");
                }

                string[] lines = SmartReadFile(_paths.IniPath)
                    .Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

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
                        case "exepath": config.ExePath = val; break;
                        case "username": config.UserName = val; break;
                        case "password": config.Password = val; break;
                        case "titlekeyword": config.TitleKeyword = val; break;
                        case "waitwindowsec": config.WaitWindowSec = ParseIntOrDefault(val, config.WaitWindowSec); break;
                        case "readydelayms": config.ReadyDelayMs = ParseIntOrDefault(val, config.ReadyDelayMs); break;
                        case "typedelayms": config.TypeDelayMs = ParseIntOrDefault(val, config.TypeDelayMs); break;
                        case "msgboxonfail": config.MsgBoxOnFail = val != "0"; break;
                        case "retryonstillopen": config.RetryOnStillOpen = val == "1"; break;
                        case "usereditord": config.UserEditOrd = ParseIntOrDefault(val, config.UserEditOrd); break;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error("读取 AutoLogin.ini 失败,已按默认值继续:" + ex.Message);
            }

            config.Normalize();
            return config;
        }

        public void Save(AppConfig config)
        {
            config.Normalize();

            StringBuilder sb = new StringBuilder();
            foreach (string line in Template.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith(";")) sb.AppendLine(line);
            }

            sb.AppendLine("ExePath=" + config.ExePath);
            sb.AppendLine("UserName=" + config.UserName);
            sb.AppendLine("Password=" + config.Password);
            sb.AppendLine("TitleKeyword=" + config.TitleKeyword);
            sb.AppendLine("WaitWindowSec=" + config.WaitWindowSec);
            sb.AppendLine("ReadyDelayMs=" + config.ReadyDelayMs);
            sb.AppendLine("TypeDelayMs=" + config.TypeDelayMs);
            sb.AppendLine("MsgBoxOnFail=" + (config.MsgBoxOnFail ? "1" : "0"));
            sb.AppendLine("RetryOnStillOpen=" + (config.RetryOnStillOpen ? "1" : "0"));
            sb.AppendLine("UserEditOrd=" + config.UserEditOrd);

            WriteAtomic(_paths.IniPath, sb.ToString());
        }

        private static void WriteAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, Utf8Bom);
            try
            {
                if (File.Exists(path))
                {
                    File.Replace(tmp, path, null);
                }
                else
                {
                    File.Move(tmp, path);
                }
            }
            catch
            {
                // 个别文件系统不支持 Replace 时退回普通写入
                File.WriteAllText(path, content, Utf8Bom);
                try { File.Delete(tmp); } catch { }
            }
        }

        private static int ParseIntOrDefault(string value, int fallback)
        {
            int parsed;
            if (int.TryParse(value, out parsed)) return parsed;
            return fallback;
        }

        /// <summary>
        /// 兼容 UTF-8 BOM / UTF-16 / 系统 ANSI 三种现场编码。
        /// </summary>
        private static string SmartReadFile(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Utf8NoBom.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch
            {
                return Encoding.GetEncoding(0).GetString(bytes);
            }
        }
    }
}
