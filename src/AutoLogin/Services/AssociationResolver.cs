using System;
using System.IO;
using System.Text;
using AutoLogin.Interop;

namespace AutoLogin.Services
{
    /// <summary>
    /// 解析 .lnk / 工程文件（如 .ezd）实际对应的可执行程序，用于启动后认根。
    /// </summary>
    internal static class AssociationResolver
    {
        /// <summary>
        /// 返回目标 exe 的文件名（小写，含扩展名）；无法解析时返回 null。
        /// </summary>
        public static string ResolveTargetExeName(string targetPath, ILogService log)
        {
            try
            {
                string ext = Path.GetExtension(targetPath);
                if (string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFileName(targetPath).ToLowerInvariant();
                }

                if (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    string target = ShellLink.ResolveTarget(targetPath);
                    if (!string.IsNullOrEmpty(target))
                    {
                        log.Info("快捷方式目标(仅记录):" + target);
                        return Path.GetFileName(target).ToLowerInvariant();
                    }
                    log.Warning("解析快捷方式目标失败,稍后按候选进程探测。");
                    return null;
                }

                string assoc = GetExeFromAssociation(ext);
                if (!string.IsNullOrEmpty(assoc))
                {
                    log.Info("工程文件关联的可执行程序:" + assoc);
                    return Path.GetFileName(assoc).ToLowerInvariant();
                }

                log.Warning("未能解析工程文件的关联程序,稍后采用候选进程探测/兜底模式。");
                return null;
            }
            catch (Exception ex)
            {
                log.Warning("解析目标程序失败:" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 通过系统文件关联解析出可执行文件完整路径（如 .ezd -> "...\Runtime.exe" "%1"）。
        /// </summary>
        private static string GetExeFromAssociation(string extension)
        {
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                uint len = (uint)sb.Capacity;
                uint hr = NativeMethods.AssocQueryStringW(
                    NativeMethods.ASSOCF_NONE,
                    NativeMethods.ASSOCSTR_COMMAND,
                    extension,
                    "open",
                    sb,
                    ref len);

                if (hr != 0 || len == 0) return null;

                string cmd = sb.ToString();
                if (string.IsNullOrEmpty(cmd)) return null;

                string exe = cmd;
                if (exe.StartsWith("\""))
                {
                    int end = exe.IndexOf('"', 1);
                    if (end > 0) exe = exe.Substring(1, end - 1);
                }
                else
                {
                    int sp = exe.IndexOf(' ');
                    if (sp > 0) exe = exe.Substring(0, sp);
                }

                exe = exe.Trim();
                if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(exe))
                {
                    return exe;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
