# 自動按鍵工具 (AutoKey)

這是一個適用於 Windows 7 ~ Windows 11 的免安裝定時自動按鍵工具。
特色是**完全不依賴視窗標題比對**，專為標題動態變更（或亂碼）的遊戲/應用程式設計。

## 功能特色
- 透過「程序名稱 (.exe / .bin)」與「視窗 ClassName」精準鎖定目標。
- 支援背景發送按鍵 (`PostMessage`)，視窗在背景也能運作，不干擾使用者操作。
- 免安裝設計，單一 `.exe` 綠色軟體。
- 支援縮小至系統匣。

## 開發環境與建置方法

本專案使用 `.NET Framework 4.0 Client Profile` 開發。

**透過 GitHub Actions 自動建置（推薦）：**
1. 將本程式碼推送到您的 GitHub 儲存庫。
2. 切換到 GitHub 網頁的 `Actions` 頁籤。
3. 等待 `Build AutoKey for Windows` 流程完成。
4. 下載產生的 Artifacts (AutoKey-Windows.zip)。
5. 解壓縮後即可取得 `AutoKey.exe`。

**本機手動建置 (需要 Windows 環境)：**
開啟開發人員命令提示字元並輸入：
```cmd
msbuild AutoKey/AutoKey.csproj /p:Configuration=Release
```

## 使用說明

1. 開啟欲控制的遊戲或應用程式 (例如：`TWClient.bin`)。
2. 執行 `AutoKey.exe`。
3. 在上方第一步選擇目標程序名稱 (若不在清單中可點擊重整)。
4. 點選【🔍 偵測選取程序的視窗】，下方會列出相關視窗。
5. 點擊清單中正確的視窗 (ClassName 會自動填入)。
6. 設定您想要送出的快速鍵 (例如 `F1`) 與間隔時間 (毫秒)。
7. 點擊【▶ 開始發送】。
8. 程式可最小化至右下角系統匣。
