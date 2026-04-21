using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AutoKey
{
    /// <summary>
    /// 協助枚舉執行中的程序，並取得目標程序的 PID 集合
    /// </summary>
    public static class ProcessHelper
    {
        /// <summary>
        /// 取得目前所有執行中的程序名稱清單（唯一值，已排序）
        /// </summary>
        public static List<string> GetRunningProcessNames()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            Process[] procs = null;
            try
            {
                procs = Process.GetProcesses();
                foreach (var p in procs)
                {
                    try
                    {
                        // 過濾系統空閒程序
                        if (p.Id > 0 && !string.IsNullOrEmpty(p.ProcessName))
                            names.Add(p.ProcessName);
                    }
                    catch { /* 存取部分系統程序可能拋出例外，略過 */ }
                }
            }
            finally
            {
                if (procs != null)
                    foreach (var p in procs) p.Dispose();
            }

            return new List<string>(names);
        }

        /// <summary>
        /// 根據程序名稱取得所有符合的 PID 集合
        /// </summary>
        /// <param name="processName">exe 檔名（不含副檔名），大小寫不敏感</param>
        public static HashSet<int> GetPidsByName(string processName)
        {
            var pids = new HashSet<int>();
            if (string.IsNullOrEmpty(processName))
                return pids;

            Process[] procs = null;
            try
            {
                procs = Process.GetProcessesByName(processName);
                foreach (var p in procs)
                {
                    try { pids.Add(p.Id); }
                    catch { }
                }
            }
            catch { }
            finally
            {
                if (procs != null)
                    foreach (var p in procs) p.Dispose();
            }

            return pids;
        }
    }
}
