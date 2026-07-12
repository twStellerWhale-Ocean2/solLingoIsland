using ScreenTrans.Query;
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace ScreenTrans.Present;

/// <summary>
/// Dictionary 分頁（#135）：查詢結果併入主視窗成為一個分頁，取代原浮動 <c>ResultWindow</c>。頂部文字框可
/// **手動輸入英文查詢**（觸發 <see cref="ManualQueryRequested"/>，App 依單字/整句走查字義或翻譯），下方宿共用
/// <see cref="ResultView"/> 呈現三欄結果、逐字查字、編輯重譯、加入筆記。整頁底色初為白、出結果後轉 70% 透明白
/// （<c>#B3FFFFFF</c>）透出主視窗浮水印。擷取（快捷鍵/手動）結果由 App 導向本頁並將主視窗帶前景。
/// </summary>
public partial class DictionaryPage : UserControl
{
    private static readonly Brush SolidWhite = System.Windows.Media.Brushes.White;
    private static readonly Brush TranslucentWhite = MakeTranslucent();

    /// <summary>底部「加入我的筆記」或「自動加入筆記」時觸發（轉發自 <see cref="ResultView"/>）。</summary>
    public event Action<NoteAddRequest>? AddToNotesRequested;

    /// <summary>結果內點單字查該字時觸發（轉發自 <see cref="ResultView"/>）。</summary>
    public event Action<string>? WordQueryRequested;

    /// <summary>編輯原文重譯時觸發（轉發自 <see cref="ResultView"/>）。</summary>
    public event Action<string>? TextReQueryRequested;

    /// <summary>頂部文字框按「Look up」或 Enter 時觸發（帶輸入之英文原文，App 依單字/整句決定查字義或翻譯）。</summary>
    public event Action<string>? ManualQueryRequested;

    public DictionaryPage()
    {
        InitializeComponent();
        // 轉發內層 ResultView 事件給 App（單一接線點）
        Result.AddToNotesRequested += r => AddToNotesRequested?.Invoke(r);
        Result.WordQueryRequested += w => WordQueryRequested?.Invoke(w);
        Result.TextReQueryRequested += t => TextReQueryRequested?.Invoke(t);

        LookupBtn.Click += (_, _) => DoLookup();
        InputBox.KeyDown += OnInputKeyDown;
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DoLookup();
            e.Handled = true;
        }
    }

    private void DoLookup()
    {
        var t = (InputBox.Text ?? "").Trim();
        if (t.Length == 0)
        {
            return;
        }
        ManualQueryRequested?.Invoke(t);
    }

    /// <summary>是否已顯示過非空結果（供 App「喚回」判斷分頁是否已有內容）。</summary>
    public bool HasResult => Result.HasResult;

    /// <summary>設定變更後由 App 注入新語音服務（避免播放鈕用到已釋放的舊服務）。</summary>
    public void UpdateSpeech(ISpeechService speech) => Result.UpdateSpeech(speech);

    /// <summary>設定「加入至」下拉來源（顯示結果前呼叫）。</summary>
    public void SetNoteTargets(IEnumerable<string> topFolderNames, string activeContextName)
        => Result.SetNoteTargets(topFolderNames, activeContextName);

    public void ShowLoading()
    {
        PageBg.Background = SolidWhite; // 查詢中回白底
        Result.ShowLoading();
    }

    public void ShowResult(QueryResult r, ISpeechService speech)
    {
        Result.ShowResult(r, speech);
        PageBg.Background = TranslucentWhite; // 出結果 → 70% 透明白、透出浮水印
    }

    public void ShowError(string message)
    {
        Result.ShowError(message);
        PageBg.Background = SolidWhite; // 錯誤維持白底
    }

    public void PushWordResult(QueryResult r)
    {
        Result.PushWordResult(r);
        PageBg.Background = TranslucentWhite;
    }

    public void ReplaceCurrentResult(QueryResult r)
    {
        Result.ReplaceCurrentResult(r);
        PageBg.Background = TranslucentWhite;
    }

    public void WordLookupFailed() => Result.WordLookupFailed();

    private static Brush MakeTranslucent()
    {
        var b = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)); // 白色 70% 不透明
        b.Freeze();
        return b;
    }
}
