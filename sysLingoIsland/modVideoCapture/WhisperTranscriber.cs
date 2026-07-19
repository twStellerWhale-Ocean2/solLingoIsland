using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LingoIsland.Video;

/// <summary>
/// 以 OpenAI Whisper 直接轉錄影片音訊為逐句字幕（[modVideoCapture模組]／[techItem字幕擷取]，#187）：
/// <c>yt-dlp -x</c> 下載音訊（降頻為 16kHz 單聲道 mp3、體積小）→ 以 <c>ffprobe</c> 取時長 →（超過單塊上限則以 <c>ffmpeg</c> 切塊）→
/// 逐塊 multipart 上傳 <c>/v1/audio/transcriptions</c>（<c>whisper-1</c>、<c>verbose_json</c>、含 segment 時間軸）→ 各塊 segment 加上塊起始偏移、合併為逐句 cue。
/// 因**直接聽真實發音**，時間軸與實際說話對齊，修正 YouTube 自動字幕逐字滾動/時間漂移造成的到句暫停不準。
/// **會用到金鑰＋下載音訊、按鈕觸發、跑前確認費用**（呼叫端負責 <see cref="AiCost.EstimateWhisperUsd"/> 估算與確認）。
/// 純函式（切塊規劃／segment 合併／verbose_json 解析）拆為 internal static 供單元測試；行程與 HTTP IO 不列入單元測試。
/// </summary>
public sealed class WhisperTranscriber : IAudioTranscriber
{
    private readonly string _ytDlpPath;
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;
    private readonly string _model;
    private readonly int _timeoutSec;
    private static readonly HttpClient Http = new();

    /// <summary>單塊音訊上限秒數（保守避開 OpenAI 25MB 檔案上限）：16kHz 單聲道 mp3 約 0.25MB/分，1400s≈23min 遠低於上限、亦低於 25 分informal 建議。</summary>
    private const double MaxChunkSeconds = 1400;

    /// <summary>下載＋逐塊轉錄較久，逾時下限放寬（沿用長片作業慣例）。</summary>
    private const int MinTimeoutSec = 300;

    public WhisperTranscriber(string model = "whisper-1", int timeoutSec = 600,
        string ytDlpPath = "yt-dlp", string ffmpegPath = "ffmpeg", string ffprobePath = "ffprobe")
    {
        _model = string.IsNullOrWhiteSpace(model) ? "whisper-1" : model;
        _timeoutSec = Math.Max(timeoutSec, MinTimeoutSec);
        _ytDlpPath = ytDlpPath;
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath;
    }

    public async Task<TranscribeResult> TranscribeAsync(
        string videoUrlOrId, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(videoUrlOrId))
        {
            throw new TranscribeException("請先載入 YouTube 影片。");
        }
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new TranscribeException(
                "尚未設定 OPENAI_API_KEY 環境變數，無法轉錄音訊。請設定後重新啟動應用程式。");
        }

        var url = videoUrlOrId.Trim();
        var dir = Path.Combine(Path.GetTempPath(), "lingoisland-asr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // 1) 下載音訊：僅抽音、降頻 16kHz 單聲道 mp3（Whisper 內部即以 16kHz 處理，降頻不損辨識、體積大減）。
            progress?.Report("下載音訊中…");
            var audioTemplate = Path.Combine(dir, "audio.%(ext)s");
            var (dlExit, _, dlErr) = await RunAsync(_ytDlpPath,
                $"-x --audio-format mp3 --postprocessor-args \"ffmpeg:-ac 1 -ar 16000\" --no-playlist -o \"{audioTemplate}\" \"{url}\"",
                _timeoutSec, "下載音訊", ct);
            var audio = Directory.EnumerateFiles(dir, "audio.*")
                .FirstOrDefault(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                ?? Directory.EnumerateFiles(dir, "audio.*").FirstOrDefault();
            if (audio is null)
            {
                throw new TranscribeException(dlExit != 0
                    ? "無法下載音訊：" + FirstMeaningfulLine(dlErr)
                    : "已下載音訊，但未產生任何音檔（是否已安裝 ffmpeg？）。");
            }

            // 2) 取時長：ffprobe（供切塊規劃與實際費用計算）；失敗退回 0（單塊整檔送）。
            var totalSec = await ProbeDurationSecAsync(audio, ct);

            // 3) 規劃切塊（純函式）：短片＝單塊整檔；長片＝多塊，各塊時間軸稍後加回其起始偏移。
            var plan = PlanChunks(totalSec, MaxChunkSeconds);
            var allSegments = new List<(double ChunkStartSec, IReadOnlyList<WhisperSegment> Segments)>();
            if (plan.Count <= 1)
            {
                progress?.Report("轉錄音訊中…");
                var segs = await TranscribeFileAsync(audio, key!, ct);
                allSegments.Add((0.0, segs));
            }
            else
            {
                for (var i = 0; i < plan.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var (start, dur) = plan[i];
                    progress?.Report($"轉錄第 {i + 1}/{plan.Count} 部分…");
                    var chunkPath = Path.Combine(dir, $"chunk-{i:D3}.mp3");
                    var (fxExit, _, fxErr) = await RunAsync(_ffmpegPath,
                        $"-hide_banner -loglevel error -i \"{audio}\" -ss {Sec(start)} -t {Sec(dur)} -ac 1 -ar 16000 -y \"{chunkPath}\"",
                        _timeoutSec, "切割音訊", ct);
                    if (!File.Exists(chunkPath))
                    {
                        throw new TranscribeException(fxExit != 0
                            ? "無法切割音訊：" + FirstMeaningfulLine(fxErr)
                            : "ffmpeg 未產生任何音訊分塊（是否已安裝 ffmpeg？）。");
                    }
                    var segs = await TranscribeFileAsync(chunkPath, key!, ct);
                    allSegments.Add((start, segs));
                    try { File.Delete(chunkPath); } catch { /* best-effort */ }
                }
            }

            var cues = MergeSegments(allSegments);
            if (cues.Count == 0)
            {
                throw new TranscribeException("轉錄未辨識到任何語音——音訊可能為靜音或非英語。");
            }
            // 實際音訊秒數：ffprobe 取得優先；取不到退回**最後一個有值**之句時間（供實際費用估算）。
            // #184：容忍未定時句（取最後有值者、皆無則 0）；Whisper 段本即全定時，行為不變。
            var audioSec = totalSec > 0 ? totalSec : (cues.LastOrDefault(c => c.StartSec.HasValue)?.StartSec ?? 0);
            return new TranscribeResult(cues, audioSec);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }

    // ── 純函式（internal 供單元測試）───────────────────────────────────────────

    /// <summary>Whisper verbose_json 之一段（相對於該塊起始的秒）。</summary>
    internal sealed record WhisperSegment(double Start, double End, string Text);

    /// <summary>
    /// 切塊規劃（純函式）：把 <paramref name="totalSeconds"/> 依 <paramref name="maxChunkSeconds"/> 切為 (起始秒, 長度秒) 序列——
    /// 最後一塊補足餘量。<paramref name="totalSeconds"/> ≤ maxChunk → 單塊整檔（回一筆 (0,total)）；≤0 或 maxChunk≤0 → 空（呼叫端視為整檔）。
    /// </summary>
    internal static IReadOnlyList<(double StartSec, double DurationSec)> PlanChunks(double totalSeconds, double maxChunkSeconds)
    {
        var chunks = new List<(double, double)>();
        if (totalSeconds <= 0 || maxChunkSeconds <= 0) { return chunks; }
        for (double s = 0; s < totalSeconds; s += maxChunkSeconds)
        {
            chunks.Add((s, Math.Min(maxChunkSeconds, totalSeconds - s)));
        }
        return chunks;
    }

    /// <summary>
    /// 合併各塊 segment 為逐句 cue（純函式）：每段時間加上所屬塊起始偏移、去空白句，依開始時間穩定排序。
    /// Whisper segment 本即句/子句級，適合到句暫停；不再併句。
    /// </summary>
    internal static IReadOnlyList<SubtitleCue> MergeSegments(
        IReadOnlyList<(double ChunkStartSec, IReadOnlyList<WhisperSegment> Segments)> chunks)
    {
        var cues = new List<SubtitleCue>();
        foreach (var (chunkStart, segs) in chunks)
        {
            foreach (var seg in segs)
            {
                var text = (seg.Text ?? "").Trim();
                if (text.Length == 0) { continue; }
                cues.Add(new SubtitleCue(text, Math.Round(Math.Max(0, chunkStart + seg.Start), 3)));
            }
        }
        // #184：未定時句（null）排最後、已定時句升冪穩定排序（Whisper 段全定時，序不變）。
        return cues.OrderBy(c => c.StartSec ?? double.MaxValue).ToList();
    }

    /// <summary>解析 Whisper <c>verbose_json</c> 之 <c>segments</c> 陣列為 (start,end,text) 序列（純函式）；無 segments／格式毀損 → 空。</summary>
    internal static IReadOnlyList<WhisperSegment> ParseSegments(string verboseJson)
    {
        var list = new List<WhisperSegment>();
        if (string.IsNullOrWhiteSpace(verboseJson)) { return list; }
        try
        {
            using var doc = JsonDocument.Parse(verboseJson);
            if (!doc.RootElement.TryGetProperty("segments", out var segs) || segs.ValueKind != JsonValueKind.Array)
            {
                return list;
            }
            foreach (var s in segs.EnumerateArray())
            {
                var text = s.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                var start = s.TryGetProperty("start", out var st) && st.TryGetDouble(out var stv) ? stv : 0;
                var end = s.TryGetProperty("end", out var en) && en.TryGetDouble(out var env) ? env : start;
                list.Add(new WhisperSegment(start, end, text));
            }
        }
        catch (JsonException) { /* 毀損→回已解析部分（多半空） */ }
        return list;
    }

    // ── IO（不列入單元測試）─────────────────────────────────────────────────

    /// <summary>上傳單一音訊檔至 <c>/v1/audio/transcriptions</c>（multipart）、回其 verbose_json 的 segment 序列。</summary>
    private async Task<IReadOnlyList<WhisperSegment>> TranscribeFileAsync(string audioPath, string key, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_timeoutSec));

        using var form = new MultipartFormDataContent();
        var bytes = await File.ReadAllBytesAsync(audioPath, cts.Token);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        form.Add(fileContent, "file", Path.GetFileName(audioPath));
        form.Add(new StringContent(_model), "model");
        form.Add(new StringContent("verbose_json"), "response_format");
        form.Add(new StringContent("en"), "language"); // 本 app 專注英文學習素材，指定語言提升準度、免誤判

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions")
        {
            Content = form,
        };
        req.Headers.Add("Authorization", "Bearer " + key);

        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req, cts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // 使用者主動取消
        }
        catch (OperationCanceledException)
        {
            throw new TranscribeException($"轉錄逾時（{_timeoutSec} 秒）。");
        }
        catch (HttpRequestException ex)
        {
            throw new TranscribeException("轉錄音訊時發生網路錯誤：" + ex.Message);
        }
        using (resp)
        {
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                throw new TranscribeException($"轉錄音訊時 OpenAI 回應 {(int)resp.StatusCode}。");
            }
            return ParseSegments(json);
        }
    }

    /// <summary>以 ffprobe 取音訊總秒數；任何失敗（缺 ffprobe／解析失敗）回 0（呼叫端退為單塊整檔＋以末句時間估費）。</summary>
    private async Task<double> ProbeDurationSecAsync(string audioPath, CancellationToken ct)
    {
        try
        {
            var (exit, stdout, _) = await RunAsync(_ffprobePath,
                $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{audioPath}\"",
                60, "偵測音訊", ct);
            if (exit == 0 && double.TryParse(stdout.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var sec) && sec > 0)
            {
                return sec;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { /* ffprobe 缺失/失敗：退回 0 */ }
        return 0;
    }

    private static string Sec(double s) => s.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>啟動外部行程、回 (離開碼, stdout, stderr)；逾時或外部取消殺行程（逾時擲 <see cref="TranscribeException"/>、外部取消傳遞 <see cref="OperationCanceledException"/>）。</summary>
    private static async Task<(int exit, string stdout, string stderr)> RunAsync(
        string exe, string args, int timeoutSec, string what, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Process? p;
        try
        {
            p = Process.Start(psi);
        }
        catch (Exception ex)
        {
            throw new TranscribeException($"{Path.GetFileNameWithoutExtension(exe)} 無法啟動（請確認已安裝並加入 PATH）：" + ex.Message);
        }
        if (p is null)
        {
            throw new TranscribeException($"{Path.GetFileNameWithoutExtension(exe)} 無法啟動（請確認已安裝並加入 PATH）。");
        }
        using (p)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
            var stdoutTask = p.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = p.StandardError.ReadToEndAsync(cts.Token);
            try
            {
                await p.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                if (!ct.IsCancellationRequested)
                {
                    throw new TranscribeException($"{what}逾時（{timeoutSec} 秒）。");
                }
                throw;
            }
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return (p.ExitCode, stdout, stderr);
        }
    }

    private static string FirstMeaningfulLine(string s)
    {
        var line = s.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "未知錯誤";
        return line.Length > 200 ? line[..200] + "…" : line;
    }
}
