using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using LingoIsland.Video;

namespace LingoIsland.Present;

/// <summary>
/// 影片擷取分頁（[modVideoCapture模組]／[techApp桌面查詢工具] 擷取來源頁，spec#2）：貼 YouTube 影片→
/// yt-dlp 取字幕（<see cref="ISubtitleFetcher"/>）→ WebView2 內嵌 YouTube IFrame Player API 導引播放、
/// <see cref="PauseDecider"/> 到句暫停顯字幕→暫停句逐字可點（<see cref="WordLookupRequested"/>，沿用既有查詢）→
/// 加入既有筆記（<see cref="AddToNotesRequested"/>）。與螢幕擷取並列之可插拔擷取來源、下游完全共用。
/// </summary>
public partial class VideoCapturePage : System.Windows.Controls.UserControl
{
    private readonly ISubtitleFetcher _fetcher;
    private readonly DispatcherTimer _poll;
    private IReadOnlyList<SubtitleCue> _cues = new List<SubtitleCue>();
    private int _lastPausedIndex = -1; // 上次已暫停之 cue（PauseDecider 用）
    private int _shownCue = -1;        // 目前字幕帶顯示之 cue
    private bool _webReady;
    private bool _guiding;             // 導引播放中（輪詢到句暫停生效）

    /// <summary>暫停句點選單字＝查該字（App 導向獨立字典視窗，沿用 spec#1 查詢）。</summary>
    public event Action<string>? WordLookupRequested;

    /// <summary>加入我的筆記（目前句原文；App 重譯後入既有 NotesStore）。</summary>
    public event Action<string>? AddToNotesRequested;

    public VideoCapturePage(ISubtitleFetcher fetcher)
    {
        InitializeComponent();
        _fetcher = fetcher;
        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _poll.Tick += OnPoll;

        LoadBtn.Click += (_, _) => _ = LoadAsync();
        UrlBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) _ = LoadAsync(); };
        ReplayBtn.Click += (_, _) => _ = ReplayCurrentAsync();
        ResumeBtn.Click += (_, _) => _ = ResumeAsync();
        NextBtn.Click += (_, _) => _ = SkipNextAsync();
        AddNoteBtn.Click += (_, _) => AddCurrent();
        CueList.SelectionChanged += (_, _) => _ = JumpToSelectedAsync();
        Loaded += async (_, _) => await EnsureWebAsync();
    }

    private async Task EnsureWebAsync()
    {
        if (_webReady) return;
        try
        {
            await Web.EnsureCoreWebView2Async();
            _webReady = true;
        }
        catch (Exception ex)
        {
            SetStatus("WebView2 runtime unavailable — install the Microsoft Edge WebView2 Runtime. (" + ex.Message + ")");
        }
    }

    /// <summary>由 YouTube 連結或 11 碼影片 ID 取出影片 ID；無法辨識回 null。internal 供單元測試。</summary>
    internal static string? ExtractVideoId(string? input)
    {
        var s = (input ?? "").Trim();
        if (Regex.IsMatch(s, @"^[A-Za-z0-9_-]{11}$")) return s;
        var m = Regex.Match(s, @"(?:v=|youtu\.be/|/embed/|/shorts/|/live/)([A-Za-z0-9_-]{11})");
        return m.Success ? m.Groups[1].Value : null;
    }

    private async Task LoadAsync()
    {
        var id = ExtractVideoId(UrlBox.Text);
        if (id is null) { SetStatus("Enter a valid YouTube link or 11-character video ID."); return; }

        SetBusy(true);
        SetStatus("Fetching subtitles…");
        _guiding = false; _poll.Stop();
        _lastPausedIndex = -1; _shownCue = -1;
        _cues = new List<SubtitleCue>();
        CueList.ItemsSource = null;
        SubtitleBand.Inlines.Clear();
        SetControls(false);

        try
        {
            _cues = await _fetcher.FetchAsync(UrlBox.Text);
            CueList.ItemsSource = _cues;
            await EnsureWebAsync();
            if (_webReady)
            {
                Web.NavigateToString(PlayerHtml(id));
                _guiding = true;
                _poll.Start();
                SetControls(true);
                SetStatus($"{_cues.Count} subtitle lines loaded — playback pauses at each line; tap a word to look it up.");
            }
            else
            {
                SetStatus($"{_cues.Count} subtitle lines loaded, but the player is unavailable (WebView2 runtime missing).");
            }
        }
        catch (SubtitleException ex) { SetStatus(ex.Message); }
        catch (Exception ex) { SetStatus("Failed to load: " + ex.Message); }
        finally { SetBusy(false); }
    }

    /// <summary>承載 YouTube IFrame Player API 之最小 HTML；宿主以 li_time/li_pause/li_play/li_seek 控制。</summary>
    private static string PlayerHtml(string videoId) => """
<!doctype html><html><head><meta charset="utf-8">
<style>html,body{margin:0;height:100%;background:#000;overflow:hidden}#p{width:100%;height:100%}</style></head>
<body><div id="p"></div>
<script>
var player,ready=false;
var tag=document.createElement('script');tag.src="https://www.youtube.com/iframe_api";
document.head.appendChild(tag);
function onYouTubeIframeAPIReady(){player=new YT.Player('p',{height:'100%',width:'100%',videoId:'__VID__',
 playerVars:{'playsinline':1,'rel':0,'modestbranding':1},
 events:{'onReady':function(){ready=true;player.playVideo();}}});}
window.li_time=function(){return (ready&&player&&player.getCurrentTime)?player.getCurrentTime():-1;};
window.li_pause=function(){if(ready&&player)player.pauseVideo();};
window.li_play=function(){if(ready&&player)player.playVideo();};
window.li_seek=function(t){if(ready&&player){player.seekTo(t,true);player.playVideo();}};
</script></body></html>
""".Replace("__VID__", videoId);

    private async void OnPoll(object? sender, EventArgs e)
    {
        if (!_webReady || !_guiding || _cues.Count == 0) return;
        double t;
        try
        {
            var raw = await Web.ExecuteScriptAsync("window.li_time?window.li_time():-1");
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out t)) return;
        }
        catch { return; }
        if (t < 0) return;

        var pause = PauseDecider.NextPause(t, _cues, _lastPausedIndex);
        if (pause >= 0)
        {
            _lastPausedIndex = pause;
            try { await Web.ExecuteScriptAsync("window.li_pause&&window.li_pause()"); } catch { }
            ShowCue(pause);
        }
    }

    private void ShowCue(int i)
    {
        if (i < 0 || i >= _cues.Count) return;
        _shownCue = i;
        if (CueList.SelectedIndex != i) CueList.SelectedIndex = i; // 觸發 SelectionChanged→JumpToSelected，靠 _shownCue 早退
        CueList.ScrollIntoView(_cues[i]);
        RenderClickable(_cues[i].Text);
    }

    /// <summary>把字幕句以逐字可點呈現（單字＝Hyperlink→WordLookupRequested；分隔＝純文字），沿用 EnglishWordTokenizer。</summary>
    private void RenderClickable(string text)
    {
        SubtitleBand.Inlines.Clear();
        foreach (var tok in EnglishWordTokenizer.Tokenize(text))
        {
            if (tok.IsWord)
            {
                var word = tok.Text;
                var link = new Hyperlink(new Run(word))
                {
                    Foreground = System.Windows.Media.Brushes.MediumVioletRed,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    TextDecorations = null,
                };
                link.Click += (_, _) => WordLookupRequested?.Invoke(word);
                SubtitleBand.Inlines.Add(link);
            }
            else
            {
                SubtitleBand.Inlines.Add(new Run(tok.Text));
            }
        }
    }

    private void AddCurrent()
    {
        if (_shownCue >= 0 && _shownCue < _cues.Count)
        {
            var t = _cues[_shownCue].Text;
            if (t.Length > 0) AddToNotesRequested?.Invoke(t);
        }
    }

    private async Task ReplayCurrentAsync()
    {
        if (_shownCue < 0 || _shownCue >= _cues.Count || !_webReady) return;
        _lastPausedIndex = _shownCue - 1; // 允許重播後於本句結束再暫停
        await SeekAsync(_cues[_shownCue].StartSec);
    }

    private async Task ResumeAsync()
    {
        if (!_webReady) return;
        try { await Web.ExecuteScriptAsync("window.li_play&&window.li_play()"); } catch { }
    }

    private async Task SkipNextAsync()
    {
        if (_cues.Count == 0 || !_webReady) return;
        var next = _shownCue + 1;
        if (next >= _cues.Count) return;
        _lastPausedIndex = next - 1;
        ShowCue(next);
        await SeekAsync(_cues[next].StartSec);
    }

    private async Task JumpToSelectedAsync()
    {
        var i = CueList.SelectedIndex;
        if (i < 0 || i >= _cues.Count || !_webReady) return;
        if (i == _shownCue) return; // 由 ShowCue 程式化選取觸發者早退，僅使用者手動點清單才跳播
        _lastPausedIndex = i - 1;
        ShowCue(i);
        await SeekAsync(_cues[i].StartSec);
    }

    private async Task SeekAsync(double sec)
    {
        try { await Web.ExecuteScriptAsync($"window.li_seek&&window.li_seek({sec.ToString(CultureInfo.InvariantCulture)})"); }
        catch { }
    }

    private void SetStatus(string msg) => StatusText.Text = msg;

    private void SetBusy(bool busy) { LoadBtn.IsEnabled = !busy; UrlBox.IsEnabled = !busy; }

    private void SetControls(bool enabled)
    {
        ReplayBtn.IsEnabled = enabled;
        ResumeBtn.IsEnabled = enabled;
        NextBtn.IsEnabled = enabled;
        AddNoteBtn.IsEnabled = enabled;
    }
}
