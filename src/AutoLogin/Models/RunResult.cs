namespace AutoLogin.Models
{
    /// <summary>
    /// 一次自动登录执行的结果。
    /// </summary>
    public sealed class RunResult
    {
        private RunResult(bool success, bool skipped, string message)
        {
            Success = success;
            Skipped = skipped;
            Message = message;
        }

        public bool Success { get; private set; }
        public bool Skipped { get; private set; }
        public string Message { get; private set; }

        public static RunResult Ok(string message)
        {
            return new RunResult(true, false, message);
        }

        public static RunResult Fail(string message)
        {
            return new RunResult(false, false, message);
        }

        public static RunResult Skip(string message)
        {
            return new RunResult(true, true, message);
        }
    }

    public enum StatusLevel
    {
        Info,
        Success,
        Warning,
        Error
    }
}
