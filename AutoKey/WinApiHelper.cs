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
        public string Title { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; }
        public DateTime ProcessStartTime { get; set; }
        public bool ProcessStartTimeKnown { get; set; }

        public override string ToString()
        {
            return string.Format("[{0}] PID={1} ClassName={2} Title={3}", ProcessName, ProcessId, ClassName, Title);
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
        public const uint WM_LBUTTONDOWN = 0x0201;
        public const uint WM_LBUTTONUP   = 0x0202;
        public const int MK_LBUTTON = 0x0001;
        public const uint WM_ACTIVATE = 0x0006;
        public const int WA_ACTIVE = 1;
        public const uint WM_CHAR = 0x0102;
        public const uint WM_SETFOCUS = 0x0007;


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

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern IntPtr GetMessageExtraInfo();

        public delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // SendInput 相關結構
        public const int INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_SCANCODE = 0x0008;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public int type;
            public INPUTUNION u;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct INPUTUNION
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // ──────────────────────────────────────
        // 取得視窗 ClassName
        // ──────────────────────────────────────

        public static string GetWindowClassName(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetWindowTitle(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            if (length == 0) return string.Empty;
            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
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
        ///   限定 PID 集合；若為 null 則依 processName 自動取得。
        /// </param>
        public static List<WindowEntry> FindWindowsByProcess(
            string processName,
            string classNamePattern,
            HashSet<int> targetPids)
        {
            var result = new List<WindowEntry>();

            if (targetPids == null)
                targetPids = ProcessHelper.GetPidsByName(processName);

            if (targetPids == null || targetPids.Count == 0)
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

                bool pidMatch = targetPids.Contains((int)pid);
                if (!pidMatch)
                    return true;

                if (!string.IsNullOrEmpty(processName) &&
                    !ProcessHelper.IsProcessIdMatchingName((int)pid, processName))
                {
                    return true;
                }

                string className = GetWindowClassName(hWnd);

                // 若有 ClassName 篩選，進行比對
                if (filterByClass && classRegex != null)
                {
                    if (!classRegex.IsMatch(className))
                        return true;
                }

                string title = GetWindowTitle(hWnd);

                result.Add(new WindowEntry
                {
                    Handle      = hWnd,
                    ClassName   = className,
                    Title       = title,
                    ProcessId   = (int)pid,
                    ProcessName = processName
                });

                return true;
            }, IntPtr.Zero);

            return result;
        }

        // ──────────────────────────────────────
        // 發送按鍵（使用 SendInput 硬體層級模擬）
        // ──────────────────────────────────────
        /// <summary>
        /// 使用 SendMessage / PostMessage 對指定視窗及其所有子視窗發送鍵盤按鍵 (WM_KEYDOWN / WM_KEYUP / WM_CHAR)。
        /// 可在目標視窗處於背景時正常運作，不需要目標視窗為前景。
        /// </summary>
        public static bool SendKey(IntPtr hWnd, Keys vkCode)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
                return false;

            ushort vk = (ushort)vkCode;
            uint scan = MapVirtualKey((uint)vkCode, 0);
            bool isExtended = IsExtendedKey(vkCode);

            // 組裝 WM_KEYDOWN 的 lParam
            uint lParamDown = 1u;
            lParamDown |= (scan & 0xFFu) << 16;
            if (isExtended)
                lParamDown |= (1u << 24);

            // 組裝 WM_KEYUP 的 lParam
            uint lParamUp = 1u;
            lParamUp |= (scan & 0xFFu) << 16;
            if (isExtended)
                lParamUp |= (1u << 24);
            lParamUp |= (1u << 30);
            lParamUp |= (1u << 31);

            // 1. 僅對最頂層主視窗發送 WM_ACTIVATE 狀態偽裝 (不重複發送給子視窗以免狀態錯亂)
            PostMessage(hWnd, WM_ACTIVATE, (IntPtr)WA_ACTIVE, IntPtr.Zero);

            // 收集主視窗與其下所有子視窗 Handle
            var targets = new List<IntPtr> { hWnd };
            EnumChildWindows(hWnd, (childHWnd, lp) =>
            {
                targets.Add(childHWnd);
                return true;
            }, IntPtr.Zero);

            // 2. 對所有相關視窗發送焦點狀態與按壓訊息
            foreach (var target in targets)
            {
                if (IsWindow(target))
                {
                    // 送出 WM_SETFOCUS 使其獲取邏輯焦點
                    PostMessage(target, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);

                    // 使用 SendMessage 同步發送，穿透某些過濾 PostMessage 的保護機制
                    SendMessage(target, WM_KEYDOWN, (IntPtr)vk, (IntPtr)lParamDown);

                    // 如果是字母或數字鍵，額外加送 WM_CHAR 訊息，因為有些遊戲僅由 WM_CHAR 讀取按鍵輸入
                    if ((vkCode >= Keys.D0 && vkCode <= Keys.D9) ||
                        (vkCode >= Keys.NumPad0 && vkCode <= Keys.NumPad9) ||
                        (vkCode >= Keys.A && vkCode <= Keys.Z))
                    {
                        char charVal = (char)MapVirtualKey((uint)vkCode, 2); // MAPVK_VK_TO_CHAR = 2
                        if (charVal != '\0')
                        {
                            SendMessage(target, WM_CHAR, (IntPtr)charVal, (IntPtr)lParamDown);
                        }
                    }
                }
            }

            // 按壓持續 100 毫秒，模擬真實人類按鍵
            System.Threading.Thread.Sleep(100);

            // 3. 對所有相關視窗發送釋放訊息
            foreach (var target in targets)
            {
                if (IsWindow(target))
                {
                    SendMessage(target, WM_KEYUP, (IntPtr)vk, (IntPtr)lParamUp);
                }
            }

            return true;
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

        // ──────────────────────────────────────
        // 發送滑鼠點擊（背景，不需聚焦）
        // ──────────────────────────────────────
        
        /// <summary>
        /// 對指定視窗以 PostMessage 發送滑鼠左鍵點擊。
        /// 座標必須是相對於該視窗的 Client Coordinates。
        /// </summary>
        public static bool SendMouseClick(IntPtr hWnd, int x, int y)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
                return false;

            // lParam 是 Y 座標在最高 16 位元，X 座標在最低 16 位元
            IntPtr lParam = (IntPtr)((y << 16) | (x & 0xFFFF));

            // 先發送 WM_MOUSEMOVE 讓遊戲內部更新游標狀態，避免狀態突變導致閃退
            PostMessage(hWnd, 0x0200, IntPtr.Zero, lParam);
            System.Threading.Thread.Sleep(30);

            if (!PostMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lParam))
                return false;

            System.Threading.Thread.Sleep(50);

            if (!IsWindow(hWnd))
                return false;

            return PostMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
        }
    }
}
