using System.Text.Json;
using LingoIsland.Video;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>
/// [modVideoCapture模組] 字幕主線 pivot 純函式（<see cref="TranscriptAlign"/>，epic #178 增量5′〔字幕檔＋Whisper 對齊〕）：
/// 去 HTML（StripToPlainText）、組整理提示（BuildParsePrompt）＋解析逐句序列（ParseLines）、渲染聲音時間軸（RenderAudioTimeline）＋
/// 組對齊提示（BuildAlignPrompt）＋解析每句時間（ParseTimes，-1→null、長度校正、四捨五入）、組裝帶說話人＋時間 cue（Assemble，敘事序、null 時間）。
/// 皆以假 Responses JSON／字串餵測、不打真網路。
/// </summary>
public class TranscriptAlignTests
{
    /// <summary>把模型輸出文字包成 OpenAI Responses API 回應形狀（message.output_text）。</summary>
    private static string Api(string outputText) =>
        JsonSerializer.Serialize(new
        {
            output = new object[]
            {
                new { type = "message", role = "assistant", content = new object[]
                    { new { type = "output_text", text = outputText } } },
            },
        });

    private static string LinesJson(params object[] lines) => JsonSerializer.Serialize(new { lines });
    private static object Line(string speaker, string text) => new { speaker, text };
    private static string TimesJson(params object[] times) => JsonSerializer.Serialize(new { times });

    // ── StripToPlainText ─────────────────────────────────────────────────────

    [Fact]
    public void StripToPlainText_RemovesTags_KeepsTextOnSeparateLines()
    {
        var html = "<html><body><p>Ryder: To the Lookout!</p><p>Chase: Chase is on the case!</p></body></html>";
        var text = TranscriptAlign.StripToPlainText(html);
        Assert.Contains("Ryder: To the Lookout!", text);
        Assert.Contains("Chase: Chase is on the case!", text);
        Assert.DoesNotContain("<p>", text);
        Assert.DoesNotContain("</body>", text);
        // 區塊級標籤→換行：兩句應分行
        Assert.Contains("\n", text);
    }

    [Fact]
    public void StripToPlainText_DecodesHtmlEntities()
    {
        var text = TranscriptAlign.StripToPlainText("<p>Tom &amp; Jerry &lt;3 &quot;hi&quot; &#39;yo&#39;</p>");
        Assert.Contains("Tom & Jerry <3 \"hi\" 'yo'", text);
    }

    [Fact]
    public void StripToPlainText_DropsScriptAndStyleBlocks()
    {
        var html = "<style>.nav{color:red}</style><script>var x=1;alert(x)</script><p>Real line</p>";
        var text = TranscriptAlign.StripToPlainText(html);
        Assert.Contains("Real line", text);
        Assert.DoesNotContain("color:red", text);
        Assert.DoesNotContain("alert", text);
    }

    [Fact]
    public void StripToPlainText_BrBecomesNewline_AndCollapsesInlineWhitespace()
    {
        var text = TranscriptAlign.StripToPlainText("A<br>B    C\t\tD");
        Assert.Contains("A\nB", text);
        Assert.Contains("B C D", text); // 行內多空白/tab 收合為單一空白
    }

    [Fact]
    public void StripToPlainText_NullOrBlank_ReturnsEmpty()
    {
        Assert.Equal("", TranscriptAlign.StripToPlainText(null));
        Assert.Equal("", TranscriptAlign.StripToPlainText("   \n\t "));
    }

    [Fact]
    public void StripToPlainText_PlainTextPassesThrough()
    {
        var text = TranscriptAlign.StripToPlainText("Ryder: Ready for action?\nRubble: On the double!");
        Assert.Contains("Ryder: Ready for action?", text);
        Assert.Contains("Rubble: On the double!", text);
    }

    // ── BuildParsePrompt ─────────────────────────────────────────────────────

    [Fact]
    public void BuildParsePrompt_IncludesTranscript_KeysAndInstructions()
    {
        var p = TranscriptAlign.BuildParsePrompt("Ryder: To the Lookout!");
        Assert.Contains("Ryder: To the Lookout!", p); // 內文帶入
        Assert.Contains("lines", p);                  // 要求 JSON 鍵
        Assert.Contains("speaker", p);
        Assert.Contains("text", p);
        Assert.Contains("說話者", p);                 // 每句判斷說話者
        Assert.Contains("略過", p);                   // 略過雜訊
    }

    // ── ParseLines ───────────────────────────────────────────────────────────

    [Fact]
    public void ParseLines_ParsesSpeakerAndText_EmptySpeakerBecomesNull()
    {
        var json = Api(LinesJson(Line("Ryder", "To the Lookout!"), Line("", "No speaker here")));
        var lines = TranscriptAlign.ParseLines(json);
        Assert.Equal(2, lines.Count);
        Assert.Equal("Ryder", lines[0].Speaker);
        Assert.Equal("To the Lookout!", lines[0].Text);
        Assert.Null(lines[1].Speaker);                // 空說話人→null
        Assert.Equal("No speaker here", lines[1].Text);
    }

    [Fact]
    public void ParseLines_SkipsEmptyText()
    {
        var json = Api(LinesJson(Line("X", "   "), Line("Y", "kept")));
        var lines = TranscriptAlign.ParseLines(json);
        Assert.Single(lines);
        Assert.Equal("kept", lines[0].Text);
    }

    [Fact]
    public void ParseLines_ToleratesCodeFence()
    {
        var fenced = "```json\n" + LinesJson(Line("Ryder", "Hi")) + "\n```";
        var lines = TranscriptAlign.ParseLines(Api(fenced));
        Assert.Single(lines);
        Assert.Equal("Ryder", lines[0].Speaker);
    }

    [Fact]
    public void ParseLines_TrimsSpeakerAndText()
    {
        var json = Api(LinesJson(Line("  Ryder  ", "  Hi there  ")));
        var lines = TranscriptAlign.ParseLines(json);
        Assert.Equal("Ryder", lines[0].Speaker);
        Assert.Equal("Hi there", lines[0].Text);
    }

    [Fact]
    public void ParseLines_MissingLinesKey_ReturnsEmpty()
    {
        var json = Api(JsonSerializer.Serialize(new { other = 1 }));
        Assert.Empty(TranscriptAlign.ParseLines(json));
    }

    [Fact]
    public void ParseLines_NoOutputText_ReturnsEmpty()
    {
        var json = JsonSerializer.Serialize(new { output = Array.Empty<object>() });
        Assert.Empty(TranscriptAlign.ParseLines(json));
    }

    [Fact]
    public void ParseLines_MalformedInnerJson_ReturnsEmpty()
    {
        Assert.Empty(TranscriptAlign.ParseLines(Api("{lines: not-json")));
    }

    // ── RenderAudioTimeline ──────────────────────────────────────────────────

    [Fact]
    public void RenderAudioTimeline_FormatsSecondsAndText()
    {
        var cues = new List<SubtitleCue>
        {
            new("Hello there", 1.2),
            new("General Kenobi", 3.4),
        };
        var timeline = TranscriptAlign.RenderAudioTimeline(cues);
        Assert.Equal("[1.2] Hello there\n[3.4] General Kenobi", timeline);
    }

    [Fact]
    public void RenderAudioTimeline_SkipsNullTimeAndEmptyText()
    {
        var cues = new List<SubtitleCue>
        {
            new("timed", 2.0),
            new("no time", null),   // 未定時→略過
            new("   ", 5.0),        // 空文字→略過
        };
        var timeline = TranscriptAlign.RenderAudioTimeline(cues);
        Assert.Equal("[2.0] timed", timeline);
    }

    // ── BuildAlignPrompt ─────────────────────────────────────────────────────

    [Fact]
    public void BuildAlignPrompt_IncludesTimeline_CountLinesAndRules()
    {
        var chunk = new List<TranscriptLine> { new("Ryder", "To the Lookout"), new("Chase", "On the case") };
        var p = TranscriptAlign.BuildAlignPrompt(chunk, "[1.0] to the lookout\n[3.0] on the case");
        Assert.Contains("[1.0] to the lookout", p);   // 時間軸帶入
        Assert.Contains("2", p);                       // 恰 N 句
        Assert.Contains("-1", p);                      // 對不到回 -1
        Assert.Contains("單調", p);                    // 單調不遞減
        Assert.Contains("times", p);                   // 要求的 JSON 鍵
        Assert.Contains("1. To the Lookout", p);       // 編號台詞（用 text）
        Assert.Contains("2. On the case", p);
    }

    // ── ParseTimes ───────────────────────────────────────────────────────────

    [Fact]
    public void ParseTimes_ParsesValues_NegativeBecomesNull()
    {
        var times = TranscriptAlign.ParseTimes(Api(TimesJson(1.5, -1, 3.25)), 3);
        Assert.Equal(3, times.Count);
        Assert.Equal(1.5, times[0]);
        Assert.Null(times[1]);       // -1＝對不到→null
        Assert.Equal(3.25, times[2]);
    }

    [Fact]
    public void ParseTimes_ShorterThanExpected_PadsNull()
    {
        var times = TranscriptAlign.ParseTimes(Api(TimesJson(1.5)), 3);
        Assert.Equal(3, times.Count);
        Assert.Equal(1.5, times[0]);
        Assert.Null(times[1]);
        Assert.Null(times[2]);
    }

    [Fact]
    public void ParseTimes_LongerThanExpected_Truncates()
    {
        var times = TranscriptAlign.ParseTimes(Api(TimesJson(1.0, 2.0, 3.0, 4.0)), 2);
        Assert.Equal(2, times.Count);
        Assert.Equal(1.0, times[0]);
        Assert.Equal(2.0, times[1]);
    }

    [Fact]
    public void ParseTimes_RoundsToThreeDecimals()
    {
        var times = TranscriptAlign.ParseTimes(Api(TimesJson(1.23456)), 1);
        Assert.Equal(1.235, times[0]);
    }

    [Fact]
    public void ParseTimes_MalformedOrMissing_ReturnsAllNullOfExpectedLength()
    {
        var missing = TranscriptAlign.ParseTimes(Api(JsonSerializer.Serialize(new { other = 1 })), 2);
        Assert.Equal(2, missing.Count);
        Assert.All(missing, t => Assert.Null(t));

        var malformed = TranscriptAlign.ParseTimes(Api("{times: oops"), 2);
        Assert.Equal(2, malformed.Count);
        Assert.All(malformed, t => Assert.Null(t));
    }

    [Fact]
    public void ParseTimes_ZeroExpected_ReturnsEmpty()
    {
        Assert.Empty(TranscriptAlign.ParseTimes(Api(TimesJson(1.0)), 0));
    }

    // ── IsTruncated（審查修：偵測 Responses 輸出被上限截斷） ──────────────────

    [Fact]
    public void IsTruncated_StatusIncomplete_True()
    {
        var json = JsonSerializer.Serialize(new { status = "incomplete", output = Array.Empty<object>() });
        Assert.True(TranscriptAlign.IsTruncated(json));
    }

    [Fact]
    public void IsTruncated_IncompleteDetailsMaxOutputTokens_True()
    {
        var json = JsonSerializer.Serialize(new { status = "incomplete", incomplete_details = new { reason = "max_output_tokens" } });
        Assert.True(TranscriptAlign.IsTruncated(json));
    }

    [Fact]
    public void IsTruncated_StatusCompleted_False()
    {
        var json = JsonSerializer.Serialize(new { status = "completed", output = Array.Empty<object>() });
        Assert.False(TranscriptAlign.IsTruncated(json));
    }

    [Fact]
    public void IsTruncated_NoStatus_False()
    {
        Assert.False(TranscriptAlign.IsTruncated(Api(LinesJson(Line("Ryder", "Hi")))));
    }

    [Fact]
    public void IsTruncated_MalformedOrEmpty_False()
    {
        Assert.False(TranscriptAlign.IsTruncated("{not json"));
        Assert.False(TranscriptAlign.IsTruncated(""));
        Assert.False(TranscriptAlign.IsTruncated(null));
    }

    // ── Assemble ─────────────────────────────────────────────────────────────

    [Fact]
    public void Assemble_ZipsSpeakerTextTime_InNarrativeOrder()
    {
        var lines = new List<TranscriptLine> { new("Ryder", "A"), new("Chase", "B"), new(null, "C") };
        var times = new double?[] { 1.0, null, 3.0 };
        var cues = TranscriptAlign.Assemble(lines, times);
        Assert.Equal(3, cues.Count);
        // 敘事序不重排：A, B, C 順序不變
        Assert.Equal("A", cues[0].Text); Assert.Equal(1.0, cues[0].StartSec); Assert.Equal("Ryder", cues[0].Speaker);
        Assert.Equal("B", cues[1].Text); Assert.Null(cues[1].StartSec); Assert.Equal("Chase", cues[1].Speaker); // 未對齊→時間 null、留原位
        Assert.Equal("C", cues[2].Text); Assert.Equal(3.0, cues[2].StartSec); Assert.Null(cues[2].Speaker);
    }

    [Fact]
    public void Assemble_TrimsSpeaker_EmptyBecomesNull()
    {
        var lines = new List<TranscriptLine> { new("  Ryder  ", "A"), new("   ", "B") };
        var cues = TranscriptAlign.Assemble(lines, new double?[] { 1.0, 2.0 });
        Assert.Equal("Ryder", cues[0].Speaker);
        Assert.Null(cues[1].Speaker); // 空白說話人→null
    }

    [Fact]
    public void Assemble_FewerTimesThanLines_ExtraLinesGetNullTime()
    {
        var lines = new List<TranscriptLine> { new("X", "A"), new("Y", "B"), new("Z", "C") };
        var cues = TranscriptAlign.Assemble(lines, new double?[] { 1.0 });
        Assert.Equal(3, cues.Count);
        Assert.Equal(1.0, cues[0].StartSec);
        Assert.Null(cues[1].StartSec);
        Assert.Null(cues[2].StartSec);
    }

    [Fact]
    public void Assemble_Empty_ReturnsEmpty()
    {
        Assert.Empty(TranscriptAlign.Assemble(Array.Empty<TranscriptLine>(), Array.Empty<double?>()));
    }
}
