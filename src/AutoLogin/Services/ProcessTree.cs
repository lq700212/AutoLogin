using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AutoLogin.Interop;

namespace AutoLogin.Services
{
    public struct ProcessEntry
    {
        public int Pid;
        public int ParentPid;
        public string Name;
    }

    /// <summary>
    /// 轻量进程表快照与进程树计算（ToolHelp）。
    /// </summary>
    internal static class ProcessTree
    {
        public static Dictionary<int, ProcessEntry> Snapshot()
        {
            Dictionary<int, ProcessEntry> table = new Dictionary<int, ProcessEntry>();
            IntPtr snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return table;

            try
            {
                NativeMethods.PROCESSENTRY32W entry = new NativeMethods.PROCESSENTRY32W();
                entry.dwSize = (uint)Marshal.SizeOf(typeof(NativeMethods.PROCESSENTRY32W));

                if (NativeMethods.Process32FirstW(snap, ref entry))
                {
                    while (true)
                    {
                        ProcessEntry pe = new ProcessEntry();
                        pe.Pid = (int)entry.th32ProcessID;
                        pe.ParentPid = (int)entry.th32ParentProcessID;
                        pe.Name = entry.szExeFile;
                        table[pe.Pid] = pe;

                        if (!NativeMethods.Process32NextW(snap, ref entry)) break;
                        entry.dwSize = (uint)Marshal.SizeOf(typeof(NativeMethods.PROCESSENTRY32W));
                    }
                }
            }
            finally
            {
                NativeMethods.CloseHandle(snap);
            }

            return table;
        }

        /// <summary>
        /// 收集根进程及其全部子孙 PID。rootPid <= 0 返回空集合。
        /// </summary>
        public static HashSet<int> CollectTree(Dictionary<int, ProcessEntry> table, int rootPid)
        {
            HashSet<int> result = new HashSet<int>();
            if (rootPid <= 0 || table == null) return result;

            Dictionary<int, List<int>> children = new Dictionary<int, List<int>>();
            foreach (KeyValuePair<int, ProcessEntry> kv in table)
            {
                List<int> list;
                if (!children.TryGetValue(kv.Value.ParentPid, out list))
                {
                    list = new List<int>();
                    children[kv.Value.ParentPid] = list;
                }
                list.Add(kv.Key);
            }

            Stack<int> stack = new Stack<int>();
            stack.Push(rootPid);
            while (stack.Count > 0)
            {
                int pid = stack.Pop();
                if (!result.Add(pid)) continue;

                List<int> list;
                if (children.TryGetValue(pid, out list))
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (!result.Contains(list[i])) stack.Push(list[i]);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 在进程表里按 exe 文件名查找 PID；找不到返回 0。
        /// </summary>
        public static int FindByExeName(Dictionary<int, ProcessEntry> table, string exeName)
        {
            if (table == null || string.IsNullOrEmpty(exeName)) return 0;

            foreach (KeyValuePair<int, ProcessEntry> kv in table)
            {
                if (string.Equals(kv.Value.Name, exeName, StringComparison.OrdinalIgnoreCase))
                {
                    return kv.Key;
                }
            }
            return 0;
        }
    }
}
