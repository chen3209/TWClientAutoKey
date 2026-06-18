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

        /// <summary>
        /// 驗證 PID 目前是否仍屬於指定的程序名稱
        /// </summary>
        public static bool IsProcessIdMatchingName(int processId, string processName)
        {
            if (processId <= 0 || string.IsNullOrEmpty(processName))
                return false;

            Process proc = null;
            try
            {
                proc = Process.GetProcessById(processId);
                return string.Equals(proc.ProcessName, processName, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (proc != null)
                    proc.Dispose();
            }
        }

        /// <summary>
        /// 取得指定 PID 目前程序的啟動時間。
        /// </summary>
        public static bool TryGetProcessStartTime(int processId, out DateTime startTime)
        {
            startTime = DateTime.MinValue;
            if (processId <= 0)
                return false;

            Process proc = null;
            try
            {
                proc = Process.GetProcessById(processId);
                startTime = proc.StartTime;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (proc != null)
                    proc.Dispose();
            }
        }

        /// <summary>
        /// 驗證 PID、程序名稱與啟動時間是否仍為同一個程序實例。
        /// </summary>
        public static bool IsProcessInstanceMatching(int processId, string processName, DateTime expectedStartTime)
        {
            if (!IsProcessIdMatchingName(processId, processName))
                return false;

            DateTime currentStartTime;
            if (!TryGetProcessStartTime(processId, out currentStartTime))
                return false;

            return currentStartTime == expectedStartTime;
        }
    }
}
