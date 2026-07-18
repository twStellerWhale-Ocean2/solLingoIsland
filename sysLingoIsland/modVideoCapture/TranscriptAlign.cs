using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LingoIsland.Video;

/// <summary>
/// 字幕主線 pivot 之純函式（[modVideoCapture模組]，epic #178 增量5′）：字幕檔整理（去 HTML、組解析提示、解析逐句序列）與
/// 逐句對齊（組聲音時間軸、組對齊提示、解析每句時間、組裝帶說話人＋時間之 cue）。不依賴網路／UI，可單元測試；
/// HTTP 由 <see cref="OpenAiTranscriptAligner"/> 負責。回應解析沿用 <see cref="SpeakerInference.ExtractOutputText"/>（Responses 信封取模型文字）。
/// </summary>
public static class TranscriptAlign
{
    /// <summary>對齊分塊大小（每塊台詞句數）：沿用說話人對齊之量級（<see cref="OpenAiWebSpeakerEnricher"/> ChunkSize），逐塊小而準、輸出長度可控。</summary>
    public const int ChunkSize = 40;

    private static readonly Regex ScriptStyle = new(@"<(script|style)\b[^>]*>.*?</\1>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex BlockTag = new(@"</?(p|div|br|li|tr|h[1-6]|ul|ol|table|blockquote|section|article)\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AnyTag = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex SpacesTabs = new(@"[ \t\f\v]+", RegexOptions.Compiled);
    private static readonly Regex BlankLines = new(@"\n{3,}", RegexOptions.Compiled);

    /// <summary>
    /// 把抓回之字幕檔／逐字稿頁（HTML 或純文字）化為**純文字**供 AI 解析（純函式）：去 <c>script/style</c> 區塊、
    /// 區塊級標籤（<c>p/div/br/li/tr/h1-6…</c>）轉換行以保留逐句結構、去其餘標籤、解 HTML 實體、收合行內空白與過多空行。
    /// null／空回空字串。<b>不判斷對白</b>（雜訊留待 AI 解析階段濾除）。
    /// </summary>
    public static string StripToPlainText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return ""; }
        var s = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        s = ScriptStyle.Replace(s, "\n");         // 去 script/style 全區塊（含內容）
        s = BlockTag.Replace(s, "\n");            // 區塊級標籤→換行（保留逐句/逐段界線）
        s = AnyTag.Replace(s, "");                // 去其餘行內標籤
        s = WebUtility.HtmlDecode(s);             // 解 &amp; &lt; &#39; &nbsp; … 等實體
        // 逐行收合行內空白、去空行；再限制連續空行至多兩行。
        var lines = s.Split('\n').Select(l => SpacesTabs.Replace(l, " ").Trim());
        s = string.Join("\n", lines);
        s = BlankLines.Replace(s, "\n\n");
        return s.Trim();
    }

    // ── 第1段：字幕檔整理（原文→逐句「說話人＋台詞」序列） ─────────────────────────

    /// <summary>組「整理字幕檔為逐句序列」提示（不上網）：擷取依序對白、每句標說話人（角色名或空）、略過雜訊；回 <c>{lines:[{speaker,text}]}</c>。</summary>
    public static string BuildParsePrompt(string transcriptText)
    {
        var sb = new StringBuilder();
        sb.Append("下面是一支英文影片的字幕檔／逐字稿原文（可能夾雜網頁導覽、標題、廣告等雜訊）。請擷取其中**依序的對白**，整理成逐句序列。\n");
        sb.Append("每句判斷其**說話者（角色名）**：若原文以「角色：台詞」「角色 - 台詞」等標明，取該角色名；無明確角色則說話者留空字串。\n");
        sb.Append("略過：導覽列／頁尾／廣告／章節標題／集數資訊／純舞台指示（場景描述、[music]、(applause) 等）等非對白。保持台詞原文與出現順序，不要翻譯、不要改寫。\n");
        sb.Append("只回傳 JSON：{\"lines\":[{\"speaker\":\"角色名或空字串\",\"text\":\"台詞\"}, ...]}。不要輸出任何說明文字。\n\n");
        sb.Append("原文：\n---\n").Append(transcriptText.Trim()).Append("\n---");
        return sb.ToString();
    }

    /// <summary>解析「整理字幕檔」之 Responses 回應為逐句 <see cref="TranscriptLine"/>（取 output_text 內之 <c>{lines:[{speaker,text}]}</c>；容忍圍籬與前後贅字）；空／缺欄／解析失敗回空清單。台詞空白之句略過；說話人空白＝未標示（null）。</summary>
    public static IReadOnlyList<TranscriptLine> ParseLines(string responsesApiJson)
    {
        var lines = new List<TranscriptLine>();
        using var doc = JsonDocument.Parse(responsesApiJson);
        var text = SpeakerInference.ExtractOutputText(doc.RootElement);
        var json = ExtractJsonObject(text);
        if (json is null) { return lines; }
        try
        {
            using var inner = JsonDocument.Parse(json);
            if (inner.RootElement.ValueKind != JsonValueKind.Object
                || !inner.RootElement.TryGetProperty("lines", out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                return lines;
            }
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) { continue; }
                var lineText = (el.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null)?.Trim();
                if (string.IsNullOrEmpty(lineText)) { continue; }
                var speaker = (el.TryGetProperty("speaker", out var sp) && sp.ValueKind == JsonValueKind.String ? sp.GetString() : null)?.Trim();
                lines.Add(new TranscriptLine(string.IsNullOrEmpty(speaker) ? null : speaker, lineText));
            }
        }
        catch (JsonException) { /* malformed → 回已解析部分（多半空） */ }
        return lines;
    }

    // ── 第2段：對齊（字幕檔句 ↔ Whisper 聲音時間軸） ────────────────────────────

    /// <summary>把 Whisper 逐句 cue 渲染為對齊參考「時間軸」文字（每行 <c>[秒] 聽寫文字</c>，依時間遞增）；未定時句（null）略過。純函式。</summary>
    public static string RenderAudioTimeline(IReadOnlyList<SubtitleCue> audioCues)
    {
        var sb = new StringBuilder();
        foreach (var c in audioCues)
        {
            if (c.StartSec is not double s) { continue; }
            var text = (c.Text ?? "").Trim();
            if (text.Length == 0) { continue; }
            sb.Append('[').Append(s.ToString("0.0", CultureInfo.InvariantCulture)).Append("] ").Append(text).Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>組「對照聲音時間軸標每句台詞開始秒」提示（不上網）：給聲音時間軸與該塊台詞，回每句開始秒（恰該塊句數、對不到回 -1、單調不遞減）。</summary>
    public static string BuildAlignPrompt(IReadOnlyList<TranscriptLine> chunk, string audioTimeline)
    {
        var sb = new StringBuilder();
        sb.Append("以下是一支影片**聲音轉錄的逐句時間軸**（每行『[秒] 聽寫文字』，秒＝該句在影片中的開始秒，依時間遞增）：\n---\n");
        sb.Append(audioTimeline.Trim()).Append("\n---\n");
        sb.Append("下面是該影片**字幕檔的其中 ").Append(chunk.Count).Append(" 句台詞**（已編號、依敘事順序）。請**對照上面的聲音時間軸**，判斷每一句台詞在影片中的**開始秒**（聲音中念出該句的時間）。\n");
        sb.Append("規則：台詞文字與聽寫文字可能不完全一致（用詞／標點／大小寫），以語意最相近者對齊；時間須隨編號**單調不遞減**；實在對不到聲音者回 -1（寧可留 -1 勿硬填）。\n");
        sb.Append("只回傳 JSON：{\"times\":[...]}，times 長度恰好 ").Append(chunk.Count).Append(" 個、依序對應、數值為開始秒（小數）或 -1。不要輸出任何說明文字。\n\n台詞：");
        for (var i = 0; i < chunk.Count; i++) { sb.Append('\n').Append(i + 1).Append(". ").Append(chunk[i].Text); }
        return sb.ToString();
    }

    /// <summary>解析「對齊」之 Responses 回應為每句開始秒（取 output_text 內之 <c>{times:[...]}</c>）：長度校正為 <paramref name="expectedCount"/>（短補 null、長截斷）；負值／非數字／缺→null（時間未知）；有值四捨五入至 3 位。</summary>
    public static IReadOnlyList<double?> ParseTimes(string responsesApiJson, int expectedCount)
    {
        var result = new double?[Math.Max(0, expectedCount)];
        using var doc = JsonDocument.Parse(responsesApiJson);
        var text = SpeakerInference.ExtractOutputText(doc.RootElement);
        var json = ExtractJsonObject(text);
        if (json is null) { return result; }
        try
        {
            using var inner = JsonDocument.Parse(json);
            if (inner.RootElement.ValueKind != JsonValueKind.Object
                || !inner.RootElement.TryGetProperty("times", out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                return result;
            }
            var i = 0;
            foreach (var el in arr.EnumerateArray())
            {
                if (i >= result.Length) { break; }           // 長於預期→截斷
                result[i] = ReadTime(el);
                i++;
            }
        }
        catch (JsonException) { /* malformed → 回已解析部分（其餘 null） */ }
        return result;
    }

    /// <summary>單一時間值容錯讀取：數字（≥0）→該值（四捨五入 3 位）；負值／字串化數字（≥0）→值；其餘（-1、非數字、null）→null。</summary>
    private static double? ReadTime(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var v))
        {
            return v >= 0 ? Math.Round(v, 3) : (double?)null;
        }
        if (el.ValueKind == JsonValueKind.String
            && double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var sv))
        {
            return sv >= 0 ? Math.Round(sv, 3) : (double?)null;
        }
        return null;
    }

    /// <summary>
    /// 判斷 Responses 回應是否因輸出上限被截斷（純函式，增量5′ 審查修）：頂層 <c>status=="incomplete"</c>
    /// 或 <c>incomplete_details.reason=="max_output_tokens"</c>＝輸出未完成、內容不可靠（截斷之 JSON 解析後多為空清單）。
    /// 呼叫端據此給明確「內容過長」錯誤而非靜默回空、誤指 URL、反覆付費重試。信封非 JSON／無 status → false。
    /// </summary>
    public static bool IsTruncated(string? responsesApiJson)
    {
        if (string.IsNullOrWhiteSpace(responsesApiJson)) { return false; }
        try
        {
            using var doc = JsonDocument.Parse(responsesApiJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return false; }
            if (root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
                && string.Equals(st.GetString(), "incomplete", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return root.TryGetProperty("incomplete_details", out var id) && id.ValueKind == JsonValueKind.Object
                && id.TryGetProperty("reason", out var rs) && rs.ValueKind == JsonValueKind.String
                && string.Equals(rs.GetString(), "max_output_tokens", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException) { return false; }
    }

    // ── 組裝 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 把整理後之逐句（說話人＋台詞）與對齊所得每句開始秒組裝為 <see cref="SubtitleCue"/>（純函式）：
    /// 依 <paramref name="lines"/> **敘事順序**（逐字稿為主、閱讀序），時間取 <paramref name="startSecs"/> 對應值（缺／null＝時間未知，#184）。
    /// <b>不重排序</b>——維持字幕檔敘事序（含說話人序列完整性）；未定時句留原位、由 <see cref="PauseDecider"/> 之 null 容忍略過（增量4）。
    /// </summary>
    public static IReadOnlyList<SubtitleCue> Assemble(IReadOnlyList<TranscriptLine> lines, IReadOnlyList<double?> startSecs)
    {
        var cues = new List<SubtitleCue>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var time = i < startSecs.Count ? startSecs[i] : null;
            var speaker = string.IsNullOrWhiteSpace(lines[i].Speaker) ? null : lines[i].Speaker!.Trim();
            cues.Add(new SubtitleCue(lines[i].Text, time, speaker));
        }
        return cues;
    }

    /// <summary>自模型輸出文字取 JSON 物件：去 <c>```</c> 圍籬、取首個 '{' 到末個 '}'；空／無物件回 null。</summary>
    private static string? ExtractJsonObject(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) { return null; }
        var s = content;
        var fence = s.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var nl = s.IndexOf('\n', fence);
            var close = nl >= 0 ? s.IndexOf("```", nl + 1, StringComparison.Ordinal) : -1;
            if (nl >= 0 && close > nl) { s = s.Substring(nl + 1, close - nl - 1); }
        }
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        return start >= 0 && end > start ? s.Substring(start, end - start + 1) : null;
    }
}
