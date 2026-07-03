---
name: setWi自訂Usr安裝設定金鑰
date: 2026/7/3
description: 使用者放置程式檔並設定 OpenAI API 金鑰環境變數的配置作業介面。
---

# I. 主旨目的

定義使用者取得 [sysScreenTrans系統] 單一 exe 後，完成放置與金鑰設定的配置作業。

# II. 參考準備

* 使用者已於 OpenAI 平台自備 API 金鑰與額度。
* 環境：Windows 11（或 Windows 10 1903+），無需安裝 .NET runtime（self-contained exe）。

# III. 內容程序

1. 將 `ScreenTrans.exe` 複製至任意資料夾（免安裝）。
2. 設定使用者環境變數 `OPENAI_API_KEY`：`設定 → 系統 → 進階系統設定 → 環境變數`，或 PowerShell `[Environment]::SetEnvironmentVariable('OPENAI_API_KEY','sk-…','User')`。
3. （選配）調整 `appsettings.json` 之 `paramModel`／`paramQueryTimeoutSec`／`paramTtsVoice`。

**可驗證結果**：環境變數存在且非空；啟動程式後不出現金鑰缺失錯誤。

# IV. 備註紀錄

* 2026/7/3：依 solScreenEnglishTrans1 增量 #1 系統設計補建。
