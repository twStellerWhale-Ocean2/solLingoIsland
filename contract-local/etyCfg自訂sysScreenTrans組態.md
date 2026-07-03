---
name: etyCfg自訂sysScreenTrans組態
date: 2026/7/3
description: sysScreenTrans 系統自身持有的專屬本地組態。
---

# I. 主旨目的

定義 [sysScreenTrans系統]（畫面選區英文發音與中譯查詢工具）的系統層本地組態定位。

# II. 說明介紹

本系統為 Windows 桌面常駐單一 exe，無 K8s／Helm 部署面；組態分為環境變數（機密）與 `appsettings.json`（非機密偏好）兩類。

# III. 組態參數

## A. 程式硬編碼參數

### paramHotkey

* **說明描述**：全域熱鍵組合，MVP 固定為 `Alt+L`（左右 Alt 皆可，`MOD_ALT`+`VK_L`）。
* **是否必要**：是。
* **設定來源**：MVP 硬編碼；後續增量可改列 appsettings。
* **影響對象**：[modCapture模組] 熱鍵註冊與喚起流程。

## B. Env轉K8sSec參數

### OPENAI_API_KEY

* **說明描述**：呼叫 OpenAI vision API 之金鑰；一律取自使用者環境變數，程式檔、設定檔與 repo 不落地。
* **是否必要**：是。
* **設定來源**：[USR] 於 OpenAI 平台自備，並依 [setWi自訂Usr安裝設定金鑰] 設入使用者環境變數。
* **影響對象**：[modQuery模組] API 呼叫授權；缺失時查詢功能降級為明確錯誤提示。

## C. HelmChart參數-chart.yaml

暫無（桌面應用，不適用 Helm）。

## D. HelmChart參數-values.yaml

暫無（桌面應用；非機密偏好改以 `appsettings.json` 承載，如下）。

### paramModel

* **說明描述**：查詢所用之 OpenAI 模型名稱，預設 `gpt-4o-mini`。
* **是否必要**：否（有預設值）。
* **設定來源**：`appsettings.json`。
* **影響對象**：[modQuery模組] 查詢成本與品質。

### paramQueryTimeoutSec

* **說明描述**：單次查詢逾時秒數，預設 `15`。
* **是否必要**：否（有預設值）。
* **設定來源**：`appsettings.json`。
* **影響對象**：[modQuery模組] 逾時中止與錯誤提示。

### paramTtsVoice

* **說明描述**：朗讀語音名稱，預設空值＝系統預設英文語音。
* **是否必要**：否（有預設值）。
* **設定來源**：`appsettings.json`。
* **影響對象**：[modPresent模組] TTS 朗讀。

# IV. 備註紀錄

* 2026/7/3：依 solScreenEnglishTrans1 增量 #1 方案設計補建。
