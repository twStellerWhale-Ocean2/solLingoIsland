---
name: datIntf自訂查詢結果格式
date: 2026/7/3
description: 單次選區查詢回傳之結構化結果資料格式（英文原文／KK 音標／繁體中文翻譯）。
---

# I. 主旨目的

定義 [modQuery模組] 對 vision API 要求並解析之查詢結果資料格式；此格式為 [modQuery模組] 與 [modPresent模組] 間的資料契約。

# II. 參考準備

* 由 vision API 以 JSON 結構化輸出回傳（structured output／JSON mode，依 [apiIntf標準OPENAI的API協定]）。

# III. 內容程序

JSON 物件，三欄位皆為必要：

| 欄位 | 型別 | 說明 |
|---|---|---|
| `original` | string | 選區內辨識出的英文原文（保留原斷行語意、修正明顯辨識雜訊） |
| `phonetic` | string | 原文之 KK 音標（逐句或逐詞，與原文順序對應） |
| `translation` | string | 原文之繁體中文翻譯（依上下文語意翻譯，非逐字直譯） |

* 範例：`{"original":"You've got mail.","phonetic":"[ juv gɑt mel ]","translation":"你收到郵件了。"}`
* 驗證規則：三欄位缺一即判定回應不合格式，走 [runWi自訂Sys辨識翻譯選區] 之異常降級。
* 選區內無可辨識英文時：`original` 回空字串、其餘欄位回空字串，呈現層顯示「未偵測到英文文字」。

# IV. 備註紀錄

* 2026/7/3：依 solScreenEnglishTrans1 增量 #1 系統設計補建。
