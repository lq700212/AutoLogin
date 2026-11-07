using System;
using System.Collections.Generic;
using AutoLogin.Models;

namespace AutoLogin.Services
{
    /// <summary>
    /// 日志输出抽象：同时服务文件日志与界面实时日志面板。
    /// </summary>
    public interface ILogService
    {
        event EventHandler<LogEntry> EntryWritten;

        string LogPath { get; }

        void Info(string message);
        void Success(string message);
        void Warning(string message);
        void Error(string message);

        /// <summary>启动时调用：日志超过阈值则删除，避免无限膨胀。</summary>
        void RotateIfNeeded();

        /// <summary>读取最近若干条历史日志，用于界面初始化。</summary>
        IList<LogEntry> LoadRecent(int maxLines);
    }
}
