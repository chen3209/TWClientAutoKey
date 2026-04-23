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
        private int targetProcessId = 0;
        private DateTime targetProcessStartTime = DateTime.MinValue;
        private bool autoStoppedByTargetLoss = false;
        private string autoStopMessage = "";
        private readonly object sendStateLock = new object();
        private System.Threading.Timer sendTimer = null;
        private int sendTimerGeneration = 0;
        private int sendIntervalMs = 1000;
        private DateTime targetMissingSinceUtc = DateTime.MinValue;
        private int sendFailureCount = 0;
        private const int MaxConsecutiveSendFailures = 3;

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
            ResetDetectedWindowState();
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

        private void cmbProcess_SelectedIndexChanged(object sender, EventArgs e)
        {
            ResetDetectedWindowState();
        }

        private void btnDetectWindows_Click(object sender, EventArgs e)
        {
            if (cmbProcess.SelectedItem == null)
            {
                MessageBox.Show("請先選擇程序！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ResetDetectedWindowState();

            string processName = cmbProcess.SelectedItem.ToString();
            var pids = ProcessHelper.GetPidsByName(processName);
            var windows = WinApiHelper.FindWindowsByProcess(processName, "", pids);

            foreach (var w in windows)
            {
                var item = new ListViewItem(w.ClassName);
                item.SubItems.Add(w.ProcessId.ToString());
                item.SubItems.Add(w.Title);
                item.Tag = w;
                item.ToolTipText = w.Title;
                lvWindows.Items.Add(item);
            }

            if (windows.Count == 0)
            {
                lblTargetInfo.Text = "找不到可鎖定的目標視窗";
                MessageBox.Show("找不到該程序的任何可見視窗。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                lblTargetInfo.Text = string.Format("已找到 {0} 個視窗，請選擇其中一個", windows.Count);
            }
        }

        private void ResetDetectedWindowState()
        {
            autoStoppedByTargetLoss = false;
            autoStopMessage = "";
            targetProcessId = 0;
            targetProcessStartTime = DateTime.MinValue;
            sendFailureCount = 0;

            lvWindows.Items.Clear();
            txtClassName.Text = "";

            if (!isRunning)
            {
                lblStatus.Text = "狀態: ⏸ 停止";
                lblTargetInfo.Text = "請重新偵測目標視窗";
            }
        }

        private void lvWindows_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lvWindows.SelectedItems.Count > 0)
            {
                var selectedWindow = lvWindows.SelectedItems[0].Tag as WindowEntry;
                if (selectedWindow != null)
                {
                    txtClassName.Text = selectedWindow.ClassName;
                    lblTargetInfo.Text = string.Format(
                        "已鎖定條件: PID {0}, ClassName {1}",
                        selectedWindow.ProcessId,
                        selectedWindow.ClassName);
                }
            }
            else
            {
                txtClassName.Text = "";
                lblTargetInfo.Text = "請選擇單一目標視窗";
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
            CaptureSelectedWindow();

            if (targetProcessId <= 0 || targetProcessStartTime == DateTime.MinValue)
            {
                MessageBox.Show(
                    "請先偵測並選擇一個明確的目標視窗。\r\n目前已停用自動群發模式，以避免影響其他視窗。",
                    "需要指定單一視窗",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (ResolveSelectedWindow(targetProcessName, targetClassName, targetProcessId, targetProcessStartTime) == null)
            {
                MessageBox.Show(
                    "目前無法用所選的 PID + ClassName 找到唯一視窗。\r\n請重新偵測並確認同一個 PID 底下只有一個符合的目標視窗。",
                    "無法安全鎖定視窗",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            int interval = (int)numInterval.Value;
            lock (sendStateLock)
            {
                isRunning = true;
                autoStoppedByTargetLoss = false;
                autoStopMessage = "";
                sendIntervalMs = interval;
                targetMissingSinceUtc = DateTime.MinValue;
                sendFailureCount = 0;
            }

            StartSendTimer(interval);
            UpdateUIState();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            StopSendTimer();
            lock (sendStateLock)
            {
                isRunning = false;
                autoStoppedByTargetLoss = false;
                autoStopMessage = "";
                targetMissingSinceUtc = DateTime.MinValue;
                sendFailureCount = 0;
            }
            UpdateUIState();
        }

        private void SendTimerCallback(object state)
        {
            var expectedGeneration = (int)state;
            string processName;
            string className;
            Keys hotkey;
            int processId;
            DateTime processStartTime;
            int interval;

            lock (sendStateLock)
            {
                if (!isRunning || expectedGeneration != sendTimerGeneration)
                    return;

                processName = targetProcessName;
                className = targetClassName;
                hotkey = targetKey;
                processId = targetProcessId;
                processStartTime = targetProcessStartTime;
                interval = sendIntervalMs;
            }

            try
            {
                // 安全模式：只允許對明確鎖定的單一視窗發送。
                if (processId <= 0 || processStartTime == DateTime.MinValue)
                {
                    if (IsTimerGenerationActive(expectedGeneration))
                    {
                        SetTargetInfoText(string.Format("目標: {0} (未鎖定程序實例，未發送)", processName));
                    }
                    return;
                }

                var selectedTarget = ResolveSelectedWindow(processName, className, processId, processStartTime);
                if (selectedTarget != null)
                {
                    lock (sendStateLock)
                    {
                        if (!isRunning || expectedGeneration != sendTimerGeneration)
                            return;

                        targetMissingSinceUtc = DateTime.MinValue;
                    }

                    if (!SendKeyIfTimerActive(selectedTarget.Handle, hotkey, expectedGeneration))
                    {
                        if (ShouldAutoStopForSendFailure(expectedGeneration))
                        {
                            HandleAutoStop(
                                processName,
                                "發送按鍵失敗，已自動停止，請確認權限或重新偵測",
                                "發送按鍵失敗，請確認 AutoKey 與目標程式的權限層級一致，並重新偵測後再開始發送。",
                                expectedGeneration);
                        }
                        else if (IsTimerGenerationActive(expectedGeneration))
                        {
                            SetTargetInfoText(string.Format(
                                "目標: {0} (發送失敗，重試中...)",
                                processName));
                        }
                        return;
                    }

                    if (IsTimerGenerationActive(expectedGeneration))
                    {
                        SetTargetInfoText(string.Format(
                            "目標: {0} (PID {1}, ClassName {2})",
                            processName,
                            selectedTarget.ProcessId,
                            selectedTarget.ClassName));
                    }
                }
                else if (ShouldAutoStopForTargetLoss(interval, expectedGeneration))
                {
                    HandleTargetLoss(processName, expectedGeneration);
                }
                else
                {
                    if (IsTimerGenerationActive(expectedGeneration))
                    {
                        SetTargetInfoText(string.Format(
                            "目標: {0} (暫時找不到視窗，重試中...)",
                            processName));
                    }
                }
            }
            finally
            {
                RearmSendTimer(expectedGeneration);
            }
        }

        private void HandleTargetLoss(string processName, int expectedGeneration)
        {
            HandleAutoStop(
                processName,
                "目標視窗失聯，已自動停止，請重新偵測",
                "目標視窗已失聯，請重新偵測後再開始發送。",
                expectedGeneration);
        }

        private void HandleAutoStop(string processName, string reason, string notificationText, int expectedGeneration)
        {
            System.Threading.Timer timerToDispose = null;

            lock (sendStateLock)
            {
                if (expectedGeneration != sendTimerGeneration)
                    return;

                sendTimerGeneration++;
                timerToDispose = sendTimer;
                sendTimer = null;

                isRunning = false;
                autoStoppedByTargetLoss = true;
                autoStopMessage = string.Format(
                    "目標: {0} ({1})",
                    processName,
                    reason);
                targetProcessId = 0;
                targetProcessStartTime = DateTime.MinValue;
                targetMissingSinceUtc = DateTime.MinValue;
                sendFailureCount = 0;
            }

            if (timerToDispose != null)
                timerToDispose.Dispose();

            RunOnUiThread(delegate
            {
                lvWindows.Items.Clear();
                txtClassName.Text = "";
                UpdateUIState();
                ShowAutoStopNotification(notificationText);
            });
        }

        private void ShowAutoStopNotification(string text)
        {
            try
            {
                notifyIcon1.BalloonTipTitle = "AutoKey 已自動停止";
                notifyIcon1.BalloonTipText = text;
                notifyIcon1.ShowBalloonTip(5000, "AutoKey 已自動停止", text, ToolTipIcon.Warning);
            }
            catch { }
        }

        private void StartSendTimer(int interval)
        {
            System.Threading.Timer timerToDispose = null;
            int generation;

            lock (sendStateLock)
            {
                sendTimerGeneration++;
                generation = sendTimerGeneration;
                timerToDispose = sendTimer;
                sendTimer = new System.Threading.Timer(
                    SendTimerCallback,
                    generation,
                    interval,
                    System.Threading.Timeout.Infinite);
            }

            if (timerToDispose != null)
                timerToDispose.Dispose();
        }

        private void StopSendTimer()
        {
            System.Threading.Timer timerToDispose = null;

            lock (sendStateLock)
            {
                sendTimerGeneration++;
                timerToDispose = sendTimer;
                sendTimer = null;
            }

            if (timerToDispose != null)
                timerToDispose.Dispose();
        }

        private void RearmSendTimer(int expectedGeneration)
        {
            lock (sendStateLock)
            {
                if (!isRunning || sendTimer == null || expectedGeneration != sendTimerGeneration)
                    return;

                sendTimer.Change(sendIntervalMs, System.Threading.Timeout.Infinite);
            }
        }

        private bool ShouldAutoStopForTargetLoss(int interval, int expectedGeneration)
        {
            var now = DateTime.UtcNow;

            lock (sendStateLock)
            {
                if (!isRunning || expectedGeneration != sendTimerGeneration)
                    return false;

                if (targetMissingSinceUtc == DateTime.MinValue)
                {
                    targetMissingSinceUtc = now;
                    return false;
                }

                return now - targetMissingSinceUtc >= GetTargetLossGraceDuration(interval);
            }
        }

        private static TimeSpan GetTargetLossGraceDuration(int interval)
        {
            int graceMs = Math.Min(Math.Max(interval * 3, 1000), 5000);
            return TimeSpan.FromMilliseconds(graceMs);
        }

        private bool IsTimerGenerationActive(int expectedGeneration)
        {
            lock (sendStateLock)
            {
                return isRunning && expectedGeneration == sendTimerGeneration;
            }
        }

        private bool SendKeyIfTimerActive(IntPtr hWnd, Keys hotkey, int expectedGeneration)
        {
            lock (sendStateLock)
            {
                if (!isRunning || expectedGeneration != sendTimerGeneration)
                    return false;

                bool sent = WinApiHelper.SendKey(hWnd, hotkey);
                if (sent)
                    sendFailureCount = 0;

                return sent;
            }
        }

        private bool ShouldAutoStopForSendFailure(int expectedGeneration)
        {
            lock (sendStateLock)
            {
                if (!isRunning || expectedGeneration != sendTimerGeneration)
                    return false;

                sendFailureCount++;
                return sendFailureCount >= MaxConsecutiveSendFailures;
            }
        }

        private void SetTargetInfoText(string text)
        {
            RunOnUiThread(delegate
            {
                lblTargetInfo.Text = text;
            });
        }

        private void RunOnUiThread(Action action)
        {
            if (IsDisposed)
                return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (!IsDisposed)
                            action();
                    });
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
            else
            {
                action();
            }
        }

        private void CaptureSelectedWindow()
        {
            targetProcessId = 0;
            targetProcessStartTime = DateTime.MinValue;

            var currentSelection = lvWindows.SelectedItems.Count > 0
                ? lvWindows.SelectedItems[0].Tag as WindowEntry
                : null;

            if (currentSelection == null)
                return;

            if (!string.Equals(currentSelection.ProcessName, targetProcessName, StringComparison.OrdinalIgnoreCase))
                return;

            targetProcessId = currentSelection.ProcessId;
            targetClassName = currentSelection.ClassName;
            txtClassName.Text = targetClassName;
            ProcessHelper.TryGetProcessStartTime(targetProcessId, out targetProcessStartTime);
        }

        private WindowEntry ResolveSelectedWindow(string processName, string className, int processId, DateTime processStartTime)
        {
            if (processId <= 0)
                return null;

            if (!ProcessHelper.IsProcessInstanceMatching(processId, processName, processStartTime))
                return null;

            var windows = WinApiHelper.FindWindowsByProcess(
                processName,
                className,
                new HashSet<int> { processId });

            if (windows.Count == 0)
                return null;

            if (windows.Count == 1)
                return windows[0];

            return null;
        }

        private void UpdateUIState()
        {
            groupBox1.Enabled = !isRunning;
            groupBox2.Enabled = !isRunning;
            cmbHotkey.Enabled = !isRunning;
            numInterval.Enabled = !isRunning;

            btnStart.Enabled = !isRunning;
            btnStop.Enabled = isRunning;

            if (isRunning)
            {
                lblStatus.Text = "狀態: ● 執行中";
            }
            else if (autoStoppedByTargetLoss)
            {
                lblStatus.Text = "狀態: ⚠ 已自動停止";
                lblTargetInfo.Text = autoStopMessage;
            }
            else
            {
                lblStatus.Text = "狀態: ⏸ 停止";
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

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopSendTimer();
            base.OnFormClosed(e);
        }
    }
}
