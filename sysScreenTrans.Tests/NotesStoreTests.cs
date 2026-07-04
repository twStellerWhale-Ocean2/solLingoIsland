using System;
using System.IO;
using System.Linq;
using ScreenTrans.Query;
using Xunit;

namespace ScreenTrans.Tests;

/// <summary>
/// [modQuery模組] 我的筆記儲存契約（Issue #28，spec#7）：加入去重、資料夾 CRUD、
/// 同夾排序、跨夾移動之純函式，及檔案往返與缺檔/毀損退空之容錯。
/// </summary>
public class NotesStoreTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"screentrans-notes-{Guid.NewGuid():N}.json");

    private static QueryResult R(string original) => new(original, "ph", "tr");

    private static NoteEntry E(string original) => NoteEntry.From(R(original), DateTimeOffset.Now);

    // ---- 去重與加入 ----

    [Fact]
    public void AddTo_AddsToFirstFolderTop()
    {
        var d = new NotesData();
        Assert.Equal(NoteAddResult.Added, NotesStore.AddTo(d, E("hello")));
        Assert.Equal(NoteAddResult.Added, NotesStore.AddTo(d, E("world")));
        Assert.Equal("world", d.Folders[0].Entries[0].Original); // 新在前
        Assert.Equal("hello", d.Folders[0].Entries[1].Original);
    }

    [Theory]
    [InlineData("Hello")]
    [InlineData("  hello  ")]
    [InlineData("HELLO")]
    public void AddTo_DuplicateKey_NormalizedCaseAndSpace(string dup)
    {
        var d = new NotesData();
        NotesStore.AddTo(d, E("hello"));
        Assert.Equal(NoteAddResult.AlreadyExists, NotesStore.AddTo(d, E(dup)));
        Assert.Single(d.Folders[0].Entries);
    }

    [Fact]
    public void AddTo_EmptyOriginal_ReturnsEmpty()
    {
        var d = new NotesData();
        Assert.Equal(NoteAddResult.Empty, NotesStore.AddTo(d, E("   ")));
    }

    [Fact]
    public void Contains_ChecksAcrossFolders()
    {
        var d = new NotesData();
        var f2 = NotesStore.AddFolder(d, "F2");
        NotesStore.AddTo(d, E("alpha"));           // → 第一夾
        f2.Entries.Add(E("beta"));                 // 另一夾
        Assert.True(NotesStore.Contains(d, NoteEntry.KeyOf("BETA")));
        Assert.Equal(NoteAddResult.AlreadyExists, NotesStore.AddTo(d, E("beta")));
    }

    // ---- 資料夾 CRUD ----

    [Fact]
    public void Folder_AddRenameRemove()
    {
        var d = new NotesData();
        var f = NotesStore.AddFolder(d, "RPG");
        Assert.Contains(d.Folders, x => x.Name == "RPG");
        NotesStore.RenameFolder(d, f.Id, "RPG 用語");
        Assert.Equal("RPG 用語", d.Folders.Single(x => x.Id == f.Id).Name);
        NotesStore.RemoveFolder(d, f.Id);
        Assert.DoesNotContain(d.Folders, x => x.Id == f.Id);
    }

    [Fact]
    public void RemoveFolder_LastOne_EnsuresDefaultRemains()
    {
        var d = new NotesData();
        var f = NotesStore.AddFolder(d, "only");
        NotesStore.RemoveFolder(d, f.Id);
        Assert.Single(d.Folders); // Ensure 補回預設夾
        Assert.Equal(NotesStore.DefaultFolderName, d.Folders[0].Name);
    }

    // ---- 條目移動與排序 ----

    [Fact]
    public void MoveEntry_MovesAcrossFolders()
    {
        var d = new NotesData();
        NotesStore.AddTo(d, E("x"));
        var src = d.Folders[0];
        var dst = NotesStore.AddFolder(d, "dst");
        var id = src.Entries[0].Id;

        NotesStore.MoveEntry(d, id, dst.Id);

        Assert.Empty(src.Entries);
        Assert.Single(dst.Entries);
        Assert.Equal("x", dst.Entries[0].Original);
    }

    [Fact]
    public void Reorder_MovesWithinFolder()
    {
        var f = new NoteFolder();
        f.Entries.AddRange(new[] { E("a"), E("b"), E("c") }); // a,b,c
        NotesStore.Reorder(f, 0, 2); // a → 末
        Assert.Equal(new[] { "b", "c", "a" }, f.Entries.Select(x => x.Original).ToArray());
    }

    [Fact]
    public void RemoveEntry_RemovesById()
    {
        var d = new NotesData();
        NotesStore.AddTo(d, E("keep"));
        NotesStore.AddTo(d, E("drop"));
        var dropId = d.Folders[0].Entries.Single(e => e.Original == "drop").Id;
        NotesStore.RemoveEntry(d, dropId);
        Assert.Single(d.Folders[0].Entries);
        Assert.Equal("keep", d.Folders[0].Entries[0].Original);
    }

    // ---- 檔案往返與容錯 ----

    [Fact]
    public void AddAndSave_Then_Load_Roundtrips_WithDedup()
    {
        var path = TempPath();
        try
        {
            var store = new NotesStore(path);
            Assert.Equal(NoteAddResult.Added, store.AddAndSave(R("hello world"), DateTimeOffset.Now));
            Assert.Equal(NoteAddResult.AlreadyExists, store.AddAndSave(R("Hello World"), DateTimeOffset.Now));
            var d = store.Load();
            Assert.Single(d.Folders);
            Assert.Single(d.Folders[0].Entries);
            Assert.Equal("hello world", d.Folders[0].Entries[0].Original);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Reorder_Persisted_AcrossSaveLoad()
    {
        var path = TempPath();
        try
        {
            var store = new NotesStore(path);
            var d = store.LoadEnsured();
            NotesStore.AddTo(d, E("a"));
            NotesStore.AddTo(d, E("b")); // b,a
            NotesStore.Reorder(d.Folders[0], 0, 1); // → a,b
            store.Save(d);

            var reloaded = store.Load();
            Assert.Equal(new[] { "a", "b" }, reloaded.Folders[0].Entries.Select(x => x.Original).ToArray());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(new NotesStore(TempPath()).Load().Folders);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmpty()
    {
        var path = TempPath();
        File.WriteAllText(path, "{ not json ]");
        try { Assert.Empty(new NotesStore(path).Load().Folders); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ToResult_MapsThreeColumns()
    {
        var r = new NoteEntry("id", DateTimeOffset.Now, "o", "p", "t").ToResult();
        Assert.Equal("o", r.Original);
        Assert.Equal("p", r.Phonetic);
        Assert.Equal("t", r.Translation);
    }
}
