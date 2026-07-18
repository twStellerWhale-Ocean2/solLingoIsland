using System.Net.Http;

namespace LingoIsland.Video;

/// <summary>
/// 取字幕檔網址之原始內容（[modVideoCapture模組]，epic #178 增量5′）：HTTP GET 使用者／finder 提供之字幕檔／逐字稿 URL，
/// 回原始文字（HTML 或純文字，交 <see cref="TranscriptAlign.StripToPlainText"/> 去雜訊、再交 <see cref="ITranscriptAligner.ParseTranscriptAsync"/> 整理）。
/// 帶瀏覽器 User-Agent（部分 wiki／字幕站擋非瀏覽器）、跟隨轉址。純 IO、不列單元測試；失敗擲 <see cref="SpeakerEnrichException"/>（人類可讀）、取消傳遞。
/// </summary>
public static class TranscriptFetch
{
    private static readonly HttpClient Http = new();
    private const int TimeoutSec = 60;

    public static async Task<string> FetchAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new SpeakerEnrichException("This video has no subtitle-file URL to read.");
        }
        using var req = new HttpRequestMessage(HttpMethod.Get, url.Trim());
        req.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,text/plain,*/*");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSec));
        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req, cts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new SpeakerEnrichException($"Reading the subtitle-file URL timed out ({TimeoutSec}s)."); }
        catch (HttpRequestException ex) { throw new SpeakerEnrichException("Could not read the subtitle-file URL: " + ex.Message); }
        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                throw new SpeakerEnrichException($"The subtitle-file URL returned HTTP {(int)resp.StatusCode} — check the URL is reachable.");
            }
            return await resp.Content.ReadAsStringAsync(ct);
        }
    }
}
