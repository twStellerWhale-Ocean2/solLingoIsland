using System.IO;
using System.IO.Compression;

namespace LingoIsland.Query;

/// <summary>
/// 資料備份與搬遷（#206，spec#3 維運面）：把整個 <c>%APPDATA%\LingoIsland</c>（設定／筆記／歷史／主題／截圖／影片清單與字幕）
/// 打包為單一 zip 供另一部電腦匯入。**不含 OPENAI_API_KEY**（金鑰在使用者環境變數、本就不落地——另機須自行設定）。
/// 打包／還原之路徑組裝與備份識別為純函式（可單元測試）；zip 內為資料夾內容之相對路徑（不含頂層資料夾名）。
/// </summary>
public static class BackupService
{
    /// <summary>資料根目錄（各 store 之 DefaultDir 同式）。</summary>
    public static string DefaultDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LingoIsland");

    /// <summary>預設備份檔名（純函式）：<c>LingoIsland-backup-yyyyMMdd-HHmm.zip</c>。</summary>
    public static string DefaultBackupFileName(DateTimeOffset now) =>
        $"LingoIsland-backup-{now:yyyyMMdd-HHmm}.zip";

    /// <summary>
    /// 備份識別（純函式）：zip 根層須含任一核心資料檔才視為 LingoIsland 備份（防誤選任意 zip 匯入把資料夾灌垃圾）。
    /// </summary>
    public static bool IsLingoBackup(IEnumerable<string> entryNames)
    {
        foreach (var n in entryNames)
        {
            var name = n.Replace('\\', '/');
            if (name is "appsettings.json" or "notes.json" or "videos.json" or "themes.json" or "history.json") { return true; }
        }
        return false;
    }

    /// <summary>
    /// 匯出：把 <paramref name="sourceDir"/> 全部內容壓為 <paramref name="zipPath"/>（覆蓋既有檔）。
    /// 先壓至暫存檔再搬置目的地——防使用者把目的地選在資料夾自身內造成「zip 吃到自己」。
    /// </summary>
    public static void CreateBackup(string sourceDir, string zipPath)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"lingo-backup-{Guid.NewGuid():N}.zip");
        try
        {
            ZipFile.CreateFromDirectory(sourceDir, tmp, CompressionLevel.Optimal, includeBaseDirectory: false);
            File.Move(tmp, zipPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) { File.Delete(tmp); } } catch { /* 暫存清理盡力 */ }
        }
    }

    /// <summary>
    /// 匯入：驗 <paramref name="zipPath"/> 為 LingoIsland 備份後，解壓覆蓋至 <paramref name="targetDir"/>（同名檔覆蓋、其餘保留）。
    /// 非備份 zip 擲 <see cref="InvalidDataException"/>（呼叫端明訊）；路徑跳脫（zip slip）由 .NET <c>ExtractToDirectory</c> 內建防護。
    /// </summary>
    public static void RestoreBackup(string zipPath, string targetDir)
    {
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            if (!IsLingoBackup(zip.Entries.Select(e => e.FullName)))
            {
                throw new InvalidDataException("不是 LingoIsland 備份檔（zip 根層缺核心資料檔）。");
            }
        }
        Directory.CreateDirectory(targetDir);
        ZipFile.ExtractToDirectory(zipPath, targetDir, overwriteFiles: true);
    }
}
