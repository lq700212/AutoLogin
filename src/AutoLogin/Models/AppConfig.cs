using System;

namespace AutoLogin.Models
{
    /// <summary>
    /// 自动登录的全部可配置项。键名与旧版 AutoLogin.ini 完全兼容。
    /// </summary>
    public sealed class AppConfig
    {
        public string ExePath { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string TitleKeyword { get; set; }
        public int WaitWindowSec { get; set; }
        public int ReadyDelayMs { get; set; }
        public int TypeDelayMs { get; set; }
        public bool MsgBoxOnFail { get; set; }
        public bool RetryOnStillOpen { get; set; }
        public int UserEditOrd { get; set; }

        public AppConfig()
        {
            ExePath = "";
            UserName = "操作员";
            Password = "123456";
            TitleKeyword = "";
            WaitWindowSec = 120;
            ReadyDelayMs = 3000;
            TypeDelayMs = 30;
            MsgBoxOnFail = true;
            RetryOnStillOpen = false;
            UserEditOrd = 1;
        }

        public static AppConfig CreateDefault()
        {
            return new AppConfig();
        }

        public AppConfig Clone()
        {
            return (AppConfig)MemberwiseClone();
        }

        /// <summary>
        /// 把数值收敛到安全范围，避免现场误填导致流程异常。
        /// </summary>
        public void Normalize()
        {
            if (ExePath == null) ExePath = "";
            if (UserName == null) UserName = "";
            if (Password == null) Password = "";
            if (TitleKeyword == null) TitleKeyword = "";

            ExePath = ExePath.Trim();
            TitleKeyword = TitleKeyword.Trim();

            if (WaitWindowSec < 5) WaitWindowSec = 5;
            if (WaitWindowSec > 3600) WaitWindowSec = 3600;
            if (ReadyDelayMs < 0) ReadyDelayMs = 0;
            if (ReadyDelayMs > 600000) ReadyDelayMs = 600000;
            if (TypeDelayMs < 1) TypeDelayMs = 1;
            if (TypeDelayMs > 2000) TypeDelayMs = 2000;
            if (UserEditOrd < 1) UserEditOrd = 1;
            if (UserEditOrd > 99) UserEditOrd = 99;
        }
    }
}
