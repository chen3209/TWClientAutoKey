using System.Collections.Generic;
using System.Windows.Forms;

namespace AutoKey
{
    /// <summary>
    /// 快速鍵項目（顯示名稱 + 對應的 Virtual Key）
    /// </summary>
    public class HotkeyInfo
    {
        public string DisplayName { get; set; }
        public Keys   Key         { get; set; }

        public override string ToString()
        {
            return DisplayName;
        }

        /// <summary>
        /// 回傳預設快速鍵清單（F1~F12 + 常用鍵）
        /// </summary>
        public static List<HotkeyInfo> GetDefaultList()
        {
            return new List<HotkeyInfo>
            {
                new HotkeyInfo { DisplayName = "F1",    Key = Keys.F1 },
                new HotkeyInfo { DisplayName = "F2",    Key = Keys.F2 },
                new HotkeyInfo { DisplayName = "F3",    Key = Keys.F3 },
                new HotkeyInfo { DisplayName = "F4",    Key = Keys.F4 },
                new HotkeyInfo { DisplayName = "F5",    Key = Keys.F5 },
                new HotkeyInfo { DisplayName = "F6",    Key = Keys.F6 },
                new HotkeyInfo { DisplayName = "F7",    Key = Keys.F7 },
                new HotkeyInfo { DisplayName = "F8",    Key = Keys.F8 },
                new HotkeyInfo { DisplayName = "F9",    Key = Keys.F9 },
                new HotkeyInfo { DisplayName = "F10",   Key = Keys.F10 },
                new HotkeyInfo { DisplayName = "F11",   Key = Keys.F11 },
                new HotkeyInfo { DisplayName = "F12",   Key = Keys.F12 },
                new HotkeyInfo { DisplayName = "ESC",   Key = Keys.Escape },
                new HotkeyInfo { DisplayName = "Enter", Key = Keys.Return },
                new HotkeyInfo { DisplayName = "Space", Key = Keys.Space },
                new HotkeyInfo { DisplayName = "Tab",   Key = Keys.Tab },
                new HotkeyInfo { DisplayName = "1",     Key = Keys.D1 },
                new HotkeyInfo { DisplayName = "2",     Key = Keys.D2 },
                new HotkeyInfo { DisplayName = "3",     Key = Keys.D3 },
                new HotkeyInfo { DisplayName = "4",     Key = Keys.D4 },
                new HotkeyInfo { DisplayName = "5",     Key = Keys.D5 },
                new HotkeyInfo { DisplayName = "6",     Key = Keys.D6 },
                new HotkeyInfo { DisplayName = "7",     Key = Keys.D7 },
                new HotkeyInfo { DisplayName = "8",     Key = Keys.D8 },
                new HotkeyInfo { DisplayName = "9",     Key = Keys.D9 },
                new HotkeyInfo { DisplayName = "0",     Key = Keys.D0 },
            };
        }
    }
}
