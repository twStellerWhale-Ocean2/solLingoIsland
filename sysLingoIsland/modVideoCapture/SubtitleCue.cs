namespace LingoIsland.Video;

/// <summary>
/// 一句字幕：文字＋<b>開始秒</b>＋（可選）說話人（[modVideoCapture模組] 影片擷取契約，spec#2；說話人＝增量5；
/// start-only＝#158；時間未知＝#184）。<b>只留開始時間</b>——一句自 <see cref="StartSec"/> 顯示到<b>下一句的開始</b>（無空窗），
/// 到句暫停於「下一句開始，或本句開始＋上限（防超長間隔乾等）」觸發（見 <see cref="PauseDecider"/>）。
/// 結束時間不入對外模型：解析階段（json3 併句／VTT 去滾動）內部以 <see cref="TimedCue"/> 保留、不外露。
/// <paramref name="StartSec"/>＝<c>null</c> 表<b>時間未知／尚未定位</b>（#184，增量4 資料地基）：比 NaN 哨兵安全（NaN 比較恆 false 易埋雷）。
/// 未定時句仍可顯示／存在，但<b>不列入時間判定</b>——不作為到句暫停目標、不作為 <see cref="PauseDecider.CueAt"/> 之時間比較對象、
/// 無法 seek 定位（見各消費端）；排序時排最後。現況所有產生器皆給非空值（未定時句待增量5 才出現）。
/// <paramref name="Speaker"/> null／空＝未標示；有值＝取自 VTT <c>&lt;v Name&gt;</c> 語音標記或使用者 YAML 編修標註。
/// </summary>
public sealed record SubtitleCue(string Text, double? StartSec, string? Speaker = null);

/// <summary>解析階段內部用之含結束時間 cue（json3 併句之間隔判斷、VTT 去滾動需要）；不對外、轉 <see cref="SubtitleCue"/> 時丟棄 <see cref="EndSec"/>。</summary>
internal sealed record TimedCue(string Text, double StartSec, double EndSec, string? Speaker = null);
