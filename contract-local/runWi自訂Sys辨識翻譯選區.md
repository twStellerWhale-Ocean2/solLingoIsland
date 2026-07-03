---
name: runWi自訂Sys辨識翻譯選區
date: 2026/7/3
description: 系統將選區影像送交 OpenAI vision API 完成文字辨識與翻譯並回傳結構化結果的行為介面。
---

# I. 主旨目的

定義系統於使用者完成框選後，自動以單次 vision API 呼叫取得「英文原文／KK 音標／繁體中文翻譯」結構化結果的單一行為。

# II. 參考準備

* 前置：`OPENAI_API_KEY` 已依 [etyCfg自訂sysScreenTrans組態] 設妥；網路可達 OpenAI API（[comIntf通用HTTPS連線]）。
* 介面依據：[apiIntf標準OPENAI的API協定]；回傳格式依 [datIntf自訂查詢結果格式]。

# III. 內容程序

1. 系統將選區影像編碼後，依 [apiIntf標準OPENAI的API協定] 送出單次 vision 查詢（模型、逾時依 [etyCfg自訂sysScreenTrans組態]）。
2. 查詢期間結果視窗顯示進行中狀態（spinner＋文字）。
3. 回應解析為 [datIntf自訂查詢結果格式] 三欄位；解析成功即交付 [modPresent模組] 顯示。
4. 異常降級：金鑰缺失、網路失敗、逾時、回應不合格式時，顯示明確可讀錯誤與下一步指引（如「請確認 OPENAI_API_KEY 已設定」），不當機、不無聲失敗。

**可驗證結果**：合法選區影像可得三欄位齊備之結果；金鑰缺失或 API 失敗時出現對應錯誤訊息且程式續存活。

# IV. 備註紀錄

* 2026/7/3：依 solScreenEnglishTrans1 增量 #1 系統設計補建。
