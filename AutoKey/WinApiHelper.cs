using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AutoKey
{
    /// <summary>
    /// 代表一個找到的視窗
    /// </summary>
    public class WindowEntry
    {
        public IntPtr Handle { get; set; }
        public string ClassName { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; }

        public override string ToString()
        {
            return string.Format("[{0}] PID={1} ClassName={2}", ProcessName, ProcessId, ClassName);
        }
    }

    /// <summary>
    /// 封裝所有 Win32 API 呼叫
    /// </summary>
    public static class WinApiHelper
    {
        // ──────────────────────────────────────
        // Windows Message 常數
        // ──────────────────────────────────────
        public const uint WM_KEYDOWN = 0x0100;
        public const uint WM_KEYUP   = 0x0101;

        // ──────────────────────────────────────
        // P/Invoke 宣告
        // ──────────────────────────────────────

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EnumWindows(EnumWindowsProc enumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder className, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        // ──────────────────────────────────────
        // 取得視窗 ClassName
        // ──────────────────────────────────────

        public static string GetWindowClassName(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        // ──────────────────────────────────────
        // 枚舉符合條件的視窗
        // ──────────────────────────────────────

        /// <summary>
        /// 找出屬於指定程序名稱（及可選 ClassName 篩選）的所有可見頂層視窗。
        /// 完全不依賴視窗標題。
        /// </summary>
        /// <param name="processName">exe 檔名（不含副檔名），例如 "notepad"</param>
        /// <param name="classNamePattern">
        ///   視窗 ClassName 篩選，留空表示不篩選。
        ///   支援萬用字元 * 與 ?。
        /// </param>
        /// <param name="targetPids">
        ///   限定 PID 集合；若為 null 則依 processName 自動取得
        /// </param>
        public static List<WindowEntry> FindWindowsByProcess(
            string processName,
            string classNamePattern,
            HashSet<int> targetPids)
        {
            var result = new List<WindowEntry>();

            if (string.IsNullOrEmpty(processName) && (targetPids == null || targetPids.Count == 0))
                return result;

            bool filterByClass = !string.IsNullOrEmpty(classNamePattern);
            Regex classRegex = null;
            if (filterByClass)
            {
                // 把萬用字元轉成 Regex
                string regexPattern = "^" + Regex.Escape(classNamePattern)
                                              .Replace("\\*", ".*")
                                              .Replace("\\?", ".") + "$";
                try { classRegex = new Regex(regexPattern, RegexOptions.IgnoreCase); }
                catch { classRegex = null; }
            }

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true; // 略過隱藏視窗

                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);

                bool pidMatch = (targetPids != null && targetPids.Contains((int)pid));
                if (!pidMatch)
                    return true;

                string className = GetWindowClassName(hWnd);

                // 若有 ClassName 篩選，進行比對
                if (filterByClass && classRegex != null)
                {
                    if (!classRegex.IsMatch(className))
                        return true;
                }

                result.Add(new WindowEntry
                {
                    Handle      = hWnd,
                    ClassName   = className,
                    ProcessId   = (int)pid,
                    ProcessName = processName
                });

                return true;
            }, IntPtr.Zero);

            return result;
        }

        // ──────────────────────────────────────
        // 發送按鍵（背景，不需聚焦）
        // ──────────────────────────────────────

        /// <summary>
        /// 對指定視窗以 PostMessage 發送 KeyDown + KeyUp，
        /// 視窗不需在前景即可接收。
        /// </summary>
        public static void SendKey(IntPtr hWnd, Keys vkCode)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
                return;

            uint scanCode = MapVirtualKey((uint)vkCode, 0);
            bool isExtended = IsExtendedKey(vkCode);

            // lParam for WM_KEYDOWN: repeat=1, scan code, extended flag
            int lParamDown = 1 | ((int)scanCode << 16);
            if (isExtended) lParamDown |= (1 << 24);

            // lParam for WM_KEYUP: prev state=1, transition=1
            int lParamUp = lParamDown | (1 << 30) | (1 << 31);

            PostMessage(hWnd, WM_KEYDOWN, (IntPtr)(int)vkCode, (IntPtr)lParamDown);
            PostMessage(hWnd, WM_KEYUP,   (IntPtr)(int)vkCode, (IntPtr)lParamUp);
        }

        private static bool IsExtendedKey(Keys key)
        {
            switch (key)
            {
                case Keys.Insert:
                case Keys.Delete:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.NumLock:
                case Keys.Divide:
                case Keys.RControlKey:
                case Keys.RMenu:
                    return true;
                default:
                    return false;
            }
        }
    }
}
