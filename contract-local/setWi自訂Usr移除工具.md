---
name: setWi自訂Usr移除工具
date: 2026/7/3
description: 使用者移除程式檔與金鑰環境變數的配置作業介面。
---

# I. 主旨目的

定義使用者完整移除 [sysScreenTrans系統] 的配置作業。

# II. 參考準備

* 前置：程式已依 [setWi自訂Usr啟動結束常駐] 結束、不在執行中。

# III. 內容程序

1. 刪除 `ScreenTrans.exe` 及其同資料夾之 `appsettings.json`。
2. 刪除使用者環境變數 `OPENAI_API_KEY`（若不再供他用）。
3. （若曾設定）自「啟動」資料夾移除捷徑。

**可驗證結果**：檔案與環境變數不存在；系統無殘留常駐程序、排程或註冊表開機項。

# IV. 備註紀錄

* 2026/7/3：依 solScreenEnglishTrans1 增量 #1 系統設計補建。
