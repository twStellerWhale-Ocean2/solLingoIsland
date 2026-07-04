using System.IO;
using System.Text.Json;

namespace ScreenTrans.Query;

/// <summary>一個自訂類別資料夾：Id＋名稱＋有序條目清單（新在前）。</summary>
public sealed class NoteFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<NoteEntry> Entries { get; set; } = new();
}

/// <summary>我的筆記根結構：資料夾清單。</summary>
public sealed class NotesData
{
    public List<NoteFolder> Folders { get; set; } = new();
}

/// <summary>加入我的筆記之結果（供 toast 回饋）。</summary>
public enum NoteAddResult { Added, AlreadyExists, Empty }

/// <summary>
/// 我的筆記本機儲存（[modQuery模組] 我的筆記儲存契約，spec#7）。存
/// <c>%APPDATA%\ScreenTrans\notes.json</c>（與 <c>history.json</c>／<c>ui-state.json</c> 同資料夾、各自檔名）。
/// 加入以英文原文正規化去重（跨全部資料夾）；資料夾 CRUD、同夾排序、跨夾移動皆為不依賴 UI 之純函式、可單元測試。
/// 讀取失敗退空結構、寫入失敗靜默降級——皆不致命；金鑰不入筆記；不受 <c>paramHistoryMax</c>／歷史清除影響。
/// </summary>
public sealed class NotesStore
{
    /// <summary>預設資料夾名稱（首次或清空後自動具備一個容器）。</summary>
    public const string DefaultFolderName = "我的筆記";

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    private readonly string _path;

    public NotesStore(string? path = null) => _path = path ?? DefaultPath;

    private static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenTrans");

    public static string DefaultPath => Path.Combine(DefaultDir, "notes.json");

    /// <summary>讀出筆記；缺檔或格式毀損 → 空結構、不致命。</summary>
    public NotesData Load()
    {
        try
        {
            return JsonSerializer.Deserialize<NotesData>(File.ReadAllText(_path)) ?? new NotesData();
        }
        catch
        {
            return new NotesData();
        }
    }

    /// <summary>讀出並確保至少一個（預設）資料夾。</summary>
    public NotesData LoadEnsured()
    {
        var d = Load();
        Ensure(d);
        return d;
    }

    /// <summary>確保至少一個資料夾（供加入與刪夾後不致無容器）。</summary>
    public static void Ensure(NotesData d)
    {
        if (d.Folders.Count == 0)
        {
            d.Folders.Add(new NoteFolder { Name = DefaultFolderName });
        }
    }

    /// <summary>寫回 notes.json；寫入失敗靜默降級、不致命。</summary>
    public void Save(NotesData d)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(d, Opts));
        }
        catch { /* 寫入失敗不影響主流程 */ }
    }

    /// <summary>便利方法：讀出→加入（去重）→若確有加入則存回；回傳結果供 toast 回饋。</summary>
    public NoteAddResult AddAndSave(QueryResult r, DateTimeOffset now)
    {
        var d = LoadEnsured();
        var res = AddTo(d, NoteEntry.From(r, now));
        if (res == NoteAddResult.Added)
        {
            Save(d);
        }
        return res;
    }

    // ---- 純函式（可單元測試，不觸檔案） ----

    /// <summary>某去重鍵是否已存在於任一資料夾。</summary>
    public static bool Contains(NotesData d, string key) =>
        !string.IsNullOrEmpty(key) && d.Folders.Any(f => f.Entries.Any(e => e.Key == key));

    /// <summary>加入至第一個資料夾頂端；空原文回 Empty、已存在（跨夾去重）回 AlreadyExists。</summary>
    public static NoteAddResult AddTo(NotesData d, NoteEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Key))
        {
            return NoteAddResult.Empty;
        }
        if (Contains(d, entry.Key))
        {
            return NoteAddResult.AlreadyExists;
        }
        Ensure(d);
        d.Folders[0].Entries.Insert(0, entry);
        return NoteAddResult.Added;
    }

    public static NoteFolder AddFolder(NotesData d, string name)
    {
        var f = new NoteFolder { Name = string.IsNullOrWhiteSpace(name) ? "新資料夾" : name.Trim() };
        d.Folders.Add(f);
        return f;
    }

    public static void RenameFolder(NotesData d, string id, string name)
    {
        var f = d.Folders.FirstOrDefault(x => x.Id == id);
        if (f is not null && !string.IsNullOrWhiteSpace(name))
        {
            f.Name = name.Trim();
        }
    }

    /// <summary>刪除資料夾（連同其條目）；刪後確保仍有預設資料夾。</summary>
    public static void RemoveFolder(NotesData d, string id)
    {
        d.Folders.RemoveAll(f => f.Id == id);
        Ensure(d);
    }

    /// <summary>自任一資料夾刪除指定條目。</summary>
    public static void RemoveEntry(NotesData d, string entryId)
    {
        foreach (var f in d.Folders)
        {
            f.Entries.RemoveAll(e => e.Id == entryId);
        }
    }

    /// <summary>將條目移動到目標資料夾頂端（跨夾歸類）。</summary>
    public static void MoveEntry(NotesData d, string entryId, string toFolderId)
    {
        var to = d.Folders.FirstOrDefault(f => f.Id == toFolderId);
        if (to is null)
        {
            return;
        }
        foreach (var f in d.Folders)
        {
            var idx = f.Entries.FindIndex(e => e.Id == entryId);
            if (idx >= 0)
            {
                if (ReferenceEquals(f, to))
                {
                    return; // 已在目標夾
                }
                var e = f.Entries[idx];
                f.Entries.RemoveAt(idx);
                to.Entries.Insert(0, e);
                return;
            }
        }
    }

    /// <summary>同一資料夾內把條目自 from 位置移到 to 位置（拖曳排序）。</summary>
    public static void Reorder(NoteFolder f, int from, int to)
    {
        if (from < 0 || from >= f.Entries.Count)
        {
            return;
        }
        to = Math.Max(0, Math.Min(to, f.Entries.Count - 1));
        if (from == to)
        {
            return;
        }
        var e = f.Entries[from];
        f.Entries.RemoveAt(from);
        f.Entries.Insert(to, e);
    }
}
