using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoLogin.Interop
{
    /// <summary>
    /// Windows Shell Link (.lnk) 读写。用于开机启动项部署与快捷方式目标解析。
    /// </summary>
    internal static class ShellLink
    {
        private const string ShellLinkClsid = "00021401-0000-0000-C000-000000000046";

        [ComImport]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [Guid("0000010B-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            void IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        /// <summary>
        /// 读取 .lnk 指向的目标路径；失败返回 null。
        /// </summary>
        public static string ResolveTarget(string lnkPath)
        {
            object shellLink = null;
            try
            {
                Type type = Type.GetTypeFromCLSID(new Guid(ShellLinkClsid));
                shellLink = Activator.CreateInstance(type);
                IPersistFile persist = (IPersistFile)shellLink;
                persist.Load(lnkPath, 0);

                IShellLinkW link = (IShellLinkW)shellLink;
                StringBuilder sb = new StringBuilder(1024);
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                return sb.ToString();
            }
            catch
            {
                return null;
            }
            finally
            {
                if (shellLink != null)
                {
                    try { Marshal.ReleaseComObject(shellLink); } catch { }
                }
            }
        }

        /// <summary>
        /// 创建（覆盖）快捷方式。
        /// </summary>
        public static void CreateShortcut(
            string lnkPath,
            string targetPath,
            string arguments,
            string workingDirectory,
            string iconPath,
            string description)
        {
            object shellLink = null;
            try
            {
                Type type = Type.GetTypeFromCLSID(new Guid(ShellLinkClsid));
                shellLink = Activator.CreateInstance(type);

                IShellLinkW link = (IShellLinkW)shellLink;
                link.SetPath(targetPath);
                link.SetArguments(arguments ?? "");
                link.SetWorkingDirectory(workingDirectory ?? Path.GetDirectoryName(targetPath));
                if (!string.IsNullOrEmpty(iconPath))
                {
                    link.SetIconLocation(iconPath, 0);
                }
                link.SetDescription(description ?? "");

                if (File.Exists(lnkPath)) File.Delete(lnkPath);

                IPersistFile persist = (IPersistFile)shellLink;
                persist.Save(lnkPath, true);
            }
            finally
            {
                if (shellLink != null)
                {
                    try { Marshal.ReleaseComObject(shellLink); } catch { }
                }
            }
        }
    }
}
