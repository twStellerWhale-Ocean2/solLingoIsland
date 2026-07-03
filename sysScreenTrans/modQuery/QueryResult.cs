namespace ScreenTrans.Query;

/// <summary>
/// 查詢結果三欄（[datIntf自訂查詢結果格式]）。三欄皆必要（string）；
/// 三欄皆空字串＝選區無可辨識英文，呈現層顯示「未偵測到英文文字」。
/// </summary>
public sealed record QueryResult(string Original, string Phonetic, string Translation)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Original)
                        && string.IsNullOrWhiteSpace(Phonetic)
                        && string.IsNullOrWhiteSpace(Translation);
}
