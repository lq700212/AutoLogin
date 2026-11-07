using System;

namespace AutoLogin.Models
{
    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// 一条日志记录。同时用于文件日志与界面日志面板。
    /// </summary>
    public sealed class LogEntry
    {
        public LogEntry(DateTime time, LogLevel level, string message)
        {
            Time = time;
            Level = level;
            Message = message ?? "";
        }

        public DateTime Time { get; private set; }
        public LogLevel Level { get; private set; }
        public string Message { get; private set; }

        public string TimeText
        {
            get { return Time.ToString("HH:mm:ss"); }
        }

        public string FullText
        {
            get { return Time.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + Message; }
        }
    }
}
