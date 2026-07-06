namespace ScreenTrans.Present;

/// <summary>
/// 發音練習通知文案組裝（[modPresent模組] 發音回饋通知契約，spec#10／#101）：純函式、可單元測試——
/// **標題含目標英文句**（明載在練哪一句）、**內文含分數／門檻／過不過**＋可選 AI 建議，或各失敗態明訊。
/// </summary>
public static class PronNotify
{
    /// <summary>評分結果通知：內文＝「score / threshold ✓ passed」或「score / threshold — try again」＋可選建議（次行）。</summary>
    public static (string Title, string Body) Result(string target, int score, int threshold, string note)
    {
        var head = score >= threshold
            ? $"{score} / {threshold}  ✓ passed"
            : $"{score} / {threshold} — try again";
        var body = string.IsNullOrWhiteSpace(note) ? head : head + "\n" + note.Trim();
        return (Title(target), body);
    }

    /// <summary>失敗態通知：內文＝明確訊息（錄音太短／找不到麥克風／未授權／評分失敗／無網／未偵測到朗讀等）。</summary>
    public static (string Title, string Body) Failure(string target, string message) => (Title(target), message);

    /// <summary>通知標題：含目標句（空目標退回泛用標題）。</summary>
    private static string Title(string target)
        => string.IsNullOrWhiteSpace(target) ? "Pronunciation practice" : $"Pronunciation: {target.Trim()}";
}
