using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoKey
{
    public partial class MainForm : Form
    {
        private bool isRunning = false;
        private string targetProcessName = "";
        private string targetClassName = "";
        private Keys targetKey = Keys.None;

        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // 初始化圖示
            try
            {
                this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                notifyIcon1.Icon = this.Icon;
            }
            catch { }

            // 載入快速鍵清單
            var hotkeys = HotkeyInfo.GetDefaultList();
            cmbHotkey.DataSource = hotkeys;
            cmbHotkey.DisplayMember = "DisplayName";
            cmbHotkey.ValueMember = "Key";
            cmbHotkey.SelectedIndex = 0;

            RefreshProcessList();
        }

        private void btnRefreshProcess_Click(object sender, EventArgs e)
        {
            RefreshProcessList();
        }

        private void RefreshProcessList()
        {
            cmbProcess.DataSource = null;
            var list = ProcessHelper.GetRunningProcessNames();
            cmbProcess.DataSource = list;
            
            // 嘗試預選包含 TWClient 的程序
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].IndexOf("TWClient", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    cmbProcess.SelectedIndex = i;
                    break;
                }
            }
        }

        private void btnDetectWindows_Click(object sender, EventArgs e)
        {
            if (cmbProcess.SelectedItem == null)
            {
                MessageBox.Show("請先選擇程序！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string processName = cmbProcess.SelectedItem.ToString();
            var pids = ProcessHelper.GetPidsByName(processName);
            var windows = WinApiHelper.FindWindowsByProcess(processName, "", pids);

            lvWindows.Items.Clear();
            foreach (var w in windows)
            {
                var item = new ListViewItem(w.ClassName);
                item.SubItems.Add("0x" + w.Handle.ToString("X"));
                item.SubItems.Add(w.Title);
                lvWindows.Items.Add(item);
            }

            if (windows.Count == 0)
            {
                MessageBox.Show("找不到該程序的任何可見視窗。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void lvWindows_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lvWindows.SelectedItems.Count > 0)
            {
                txtClassName.Text = lvWindows.SelectedItems[0].Text;
            }
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (cmbProcess.SelectedItem == null)
            {
                MessageBox.Show("請先選擇程序！", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            targetProcessName = cmbProcess.SelectedItem.ToString();
            targetClassName = txtClassName.Text.Trim();
            targetKey = (Keys)cmbHotkey.SelectedValue;

            int interval = (int)numInterval.Value;
            timerKey.Interval = interval;
            timerKey.Start();

            isRunning = true;
            UpdateUIState();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            timerKey.Stop();
            isRunning = false;
            UpdateUIState();
        }

        private void timerKey_Tick(object sender, EventArgs e)
        {
            if (!isRunning) return;

            // 每次 Tick 都重新尋找目標，因為視窗控制代碼可能改變
            var pids = ProcessHelper.GetPidsByName(targetProcessName);
            var windows = WinApiHelper.FindWindowsByProcess(targetProcessName, targetClassName, pids);

            foreach (var w in windows)
            {
                WinApiHelper.SendKey(w.Handle, targetKey);
            }

            if (windows.Count > 0)
            {
                lblTargetInfo.Text = $"目標: {targetProcessName} (找到 {windows.Count} 個視窗)";
            }
            else
            {
                lblTargetInfo.Text = $"目標: {targetProcessName} (未找到視窗)";
            }
        }

        private void UpdateUIState()
        {
            groupBox1.Enabled = !isRunning;
            groupBox2.Enabled = !isRunning;
            cmbHotkey.Enabled = !isRunning;
            numInterval.Enabled = !isRunning;

            btnStart.Enabled = !isRunning;
            btnStop.Enabled = isRunning;

            lblStatus.Text = isRunning ? "狀態: ● 執行中" : "狀態: ⏸ 停止";
            
            if (!isRunning)
            {
                lblTargetInfo.Text = "準備就緒";
            }
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
                notifyIcon1.Visible = true;
            }
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
        }
    }
}
