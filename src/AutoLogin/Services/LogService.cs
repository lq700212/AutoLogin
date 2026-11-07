using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AutoLogin.Models;

namespace AutoLogin.Services
{
    /// <summary>
    /// 线程安全的日志服务：写文件 + 实时通知界面。
    /// 文件格式与旧版保持一致（时间戳 + 两个空格 + 正文），便于沿用现有排障经验。
    /// </summary>
    public sealed class LogService : ILogService
    {
        private const long MaxLogBytes = 512 * 1024;
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly object _gate = new object();
        private readonly string _logPath;

        public LogService(string logPath)
        {
            _logPath = logPath;
        }

        public event EventHandler<LogEntry> EntryWritten;

        public string LogPath
        {
            get { return _logPath; }
        }

        public void Info(string message)
        {
            Write(LogLevel.Info, message);
        }

        public void Success(string message)
        {
            Write(LogLevel.Success, message);
        }

        public void Warning(string message)
        {
            Write(LogLevel.Warning, message);
        }

        public void Error(string message)
        {
            Write(LogLevel.Error, message);
        }

        public void RotateIfNeeded()
        {
            try
            {
                FileInfo fi = new FileInfo(_logPath);
                if (fi.Exists && fi.Length > MaxLogBytes)
                {
                    fi.Delete();
                }
            }
            catch
            {
                // 日志清理失败不影响主流程
            }
        }

        public IList<LogEntry> LoadRecent(int maxLines)
        {
            List<LogEntry> result = new List<LogEntry>();
            if (maxLines <= 0) return result;

            try
            {
                if (!File.Exists(_logPath)) return result;

                string all;
                using (FileStream fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader sr = new StreamReader(fs, Encoding.UTF8, true))
                {
                    all = sr.ReadToEnd();
                }

                string[] lines = all.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                int start = Math.Max(0, lines.Length - maxLines);
                for (int i = start; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.Length == 0) continue;

                    DateTime time = DateTime.Now;
                    string message = line;
                    if (line.Length > 25)
                    {
                        DateTime parsed;
                        if (DateTime.TryParseExact(
                                line.Substring(0, 23),
                                "yyyy-MM-dd HH:mm:ss.fff",
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None,
                                out parsed))
                        {
                            time = parsed;
                            message = line.Substring(25);
                        }
                    }
                    result.Add(new LogEntry(time, LogLevel.Info, message));
                }
            }
            catch
            {
                // 读历史日志失败不影响配置界面
            }
            return result;
        }

        private void Write(LogLevel level, string message)
        {
            if (message == null) message = "";
            LogEntry entry = new LogEntry(DateTime.Now, level, message);

            lock (_gate)
            {
                try
                {
                    File.AppendAllText(_logPath, entry.FullText + "\r\n", Utf8NoBom);
                }
                catch
                {
                    // 现场目录只读时不阻断自动登录
                }
            }

            EventHandler<LogEntry> handler = EntryWritten;
            if (handler != null)
            {
                try { handler(this, entry); }
                catch { }
            }
        }
    }
}
