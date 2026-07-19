<#
.SYNOPSIS
  建置並打包 LingoIsland 發佈成品（Velopack）：dotnet publish -> vpk pack（帶 --icon）。

.DESCRIPTION
  依 VERSION 檔（或 -Version 參數）發佈 self-contained win-x64，再以 vpk 打包為
  Setup.exe / Portable.zip / <id>-<ver>-full.nupkg（有前版基準時另產 delta）/ releases.win.json。

  Issue #177：安裝檔須按業界常規帶「應用圖示」。vpk pack 以 --icon 指定 assets\app.ico，
  使 Setup.exe 及其建立之開始功能表／桌面捷徑、解除安裝項皆帶本應用圖示，而非 Velopack 預設圖示。
  安裝後之主程式 LingoIsland.exe 圖示另由 csproj <ApplicationIcon> 提供、兩者一致。

.PARAMETER Version
  發佈版號；預設讀 repo 根之 VERSION 檔。

.PARAMETER Configuration
  建置組態；預設 Release。

.NOTES
  需求：dotnet SDK、vpk（dotnet tool install -g vpk，Velopack CLI）。
  成品輸出至 repo 根之 Releases\。建置/測試 gate 由呼叫端（發佈列車）負責，本腳本專責 publish+pack。
  檔案編碼：UTF-8 with BOM（供 Windows PowerShell 5.1 正確讀取中文，不致亂碼）。
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'

#region I.主旨目的 ================================
Write-Host "# I.主旨目的 ================================" -ForegroundColor Blue
Write-Host "* 建置並打包 LingoIsland 發佈成品（Velopack）：dotnet publish -> vpk pack。"
Write-Host "* Issue #177：安裝檔 Setup.exe 依 --icon 帶應用圖示（assets\app.ico），非 Velopack 預設圖示。"
Write-Host "* 產物：Setup.exe / Portable.zip / *-full.nupkg / releases.win.json（輸出至 Releases\）。"
#endregion

#region II.參考準備 ================================
Write-Host "# II.參考準備 ================================" -ForegroundColor Blue

  #region A.參數準備 --------------------------------
  Write-Host "## A.參數準備 --------------------------------" -ForegroundColor Cyan

  $repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
  Set-Location $repo
  if ([string]::IsNullOrWhiteSpace($Version)) {
      $Version = (Get-Content (Join-Path $repo 'VERSION') -Raw).Trim()
  }
  $publishDir = Join-Path $repo 'publish'
  $outputDir  = Join-Path $repo 'Releases'
  $icon       = Join-Path $repo 'sysLingoIsland\assets\app.ico'

  Write-Host "* repo：$repo"
  Write-Host "* 版號：$Version（Configuration=$Configuration）"
  Write-Host "* 圖示：$icon"
  Write-Host "* 輸出：$outputDir"

  if (-not (Test-Path $icon)) {
      Write-Host "* 找不到應用圖示：$icon（Issue #177 安裝檔圖示所需）" -ForegroundColor Red
      exit 1
  }
  #endregion

#endregion

#region III.內容程序 ================================
Write-Host "# III.內容程序 ================================" -ForegroundColor Blue

  #region A.發佈（dotnet publish） --------------------------------
  Write-Host "## A.發佈（dotnet publish self-contained win-x64） --------------------------------" -ForegroundColor Cyan

  if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
  dotnet publish sysLingoIsland -c $Configuration -r win-x64 --self-contained -p:Version=$Version -o $publishDir
  if ($LASTEXITCODE -ne 0) {
      Write-Host "* dotnet publish 失敗（exit $LASTEXITCODE）" -ForegroundColor Red
      exit 1
  }
  Write-Host "* 發佈完成：$publishDir" -ForegroundColor Green
  #endregion

  #region B.打包（vpk pack --icon） --------------------------------
  Write-Host "## B.打包（vpk pack，帶 --icon 安裝檔圖示） --------------------------------" -ForegroundColor Cyan

  vpk pack --packId LingoIsland --packVersion $Version --packDir $publishDir --mainExe LingoIsland.exe --icon $icon --outputDir $outputDir
  if ($LASTEXITCODE -ne 0) {
      Write-Host "* vpk pack 失敗（exit $LASTEXITCODE）" -ForegroundColor Red
      exit 1
  }
  Write-Host "* 打包完成。" -ForegroundColor Green
  #endregion

#endregion

#region IV.備註紀錄 ================================
Write-Host "# IV.備註紀錄 ================================" -ForegroundColor Blue
Write-Host "* 成品（$outputDir）：" -ForegroundColor Green
Get-ChildItem $outputDir -File | Sort-Object Name | ForEach-Object { Write-Host "    - $($_.Name)" }
Write-Host "* Setup.exe 已帶應用圖示（--icon，Issue #177）；安裝後 LingoIsland.exe 圖示由 csproj <ApplicationIcon> 提供。" -ForegroundColor Green
#endregion
