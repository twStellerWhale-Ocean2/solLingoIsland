using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenTrans.Query;

/// <summary>查詢失敗之明確可讀降級（[runWi自訂Sys辨識翻譯選區] 異常降級）。</summary>
public sealed class QueryException : Exception
{
    public QueryException(string message) : base(message) { }
}

/// <summary>
/// 單次 vision 查詢（[modQuery模組] 查詢契約，spec#3／#5）：讀 OPENAI_API_KEY（僅環境變數、
/// 不落地）、附結構化輸出要求呼叫 OpenAI，解析為 [datIntf自訂查詢結果格式]；各類失敗走 QueryException。
/// </summary>
public sealed class QueryService
{
    private readonly string _model;
    private readonly int _timeoutSec;
    private static readonly HttpClient Http = new();

    public QueryService(string model, int timeoutSec)
    {
        _model = model;
        _timeoutSec = timeoutSec;
    }

    public async Task<QueryResult> QueryAsync(byte[] pngBytes, CancellationToken ct = default)
    {
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new QueryException("未設定 OPENAI_API_KEY 環境變數，無法查詢。請設定使用者環境變數後重新啟動。");
        }

        var dataUrl = "data:image/png;base64," + Convert.ToBase64String(pngBytes);
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Add("Authorization", "Bearer " + key);
        req.Content = JsonContent.Create(BuildPayload(dataUrl));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_timeoutSec));

        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req, cts.Token);
        }
        catch (TaskCanceledException)
        {
            throw new QueryException($"查詢逾時（{_timeoutSec} 秒）。請確認網路後重試。");
        }
        catch (HttpRequestException ex)
        {
            throw new QueryException("網路連線失敗：" + ex.Message);
        }

        var json = await resp.Content.ReadAsStringAsync(cts.Token);
        if (!resp.IsSuccessStatusCode)
        {
            throw new QueryException($"API 回應 {(int)resp.StatusCode}：{Truncate(json, 200)}");
        }

        return Parse(json);
    }

    private object BuildPayload(string dataUrl) => new
    {
        model = _model,
        messages = new object[]
        {
            new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "text", text = "辨識圖片中的英文文字並回傳 JSON：original＝英文原文（保留原意、修正明顯辨識雜訊）、phonetic＝原文的 KK 音標、translation＝繁體中文翻譯（依上下文語意，非逐字直譯）。若圖中無可辨識英文，三欄皆回空字串。" },
                    new { type = "image_url", image_url = new { url = dataUrl } },
                },
            },
        },
        response_format = new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "screen_translation",
                strict = true,
                schema = new
                {
                    type = "object",
                    properties = new
                    {
                        original = new { type = "string" },
                        phonetic = new { type = "string" },
                        translation = new { type = "string" },
                    },
                    required = new[] { "original", "phonetic", "translation" },
                    additionalProperties = false,
                },
            },
        },
    };

    /// <summary>解析 OpenAI 回應為三欄結果（internal 供單元測試）。缺欄/非 JSON 走 QueryException。</summary>
    internal static QueryResult Parse(string apiJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(apiJson);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new QueryException("API 回應內容為空。");
            }

            using var inner = JsonDocument.Parse(content);
            var r = inner.RootElement;
            if (!r.TryGetProperty("original", out var o)
                || !r.TryGetProperty("phonetic", out var p)
                || !r.TryGetProperty("translation", out var t))
            {
                throw new QueryException("回應格式不符：三欄位不齊。");
            }
            return new QueryResult(o.GetString() ?? "", p.GetString() ?? "", t.GetString() ?? "");
        }
        catch (QueryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new QueryException("回應解析失敗（格式不符）：" + ex.Message);
        }
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
