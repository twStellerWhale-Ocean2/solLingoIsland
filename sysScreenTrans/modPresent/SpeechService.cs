using System.Speech.Synthesis;

namespace ScreenTrans.Present;

/// <summary>朗讀抽象（[techItem語音合成]）——介面化使單元測試可攔截、不實際發聲。</summary>
public interface ISpeechService
{
    /// <summary>朗讀文字；重複呼叫時先停止前次再播新內容。</summary>
    void Speak(string text);
}

/// <summary>Windows 內建語音合成（SAPI，離線）之 ISpeechService 實作。</summary>
public sealed class SpeechService : ISpeechService, IDisposable
{
    private readonly SpeechSynthesizer _synth = new();

    public SpeechService(string? voice)
    {
        _synth.SetOutputToDefaultAudioDevice();
        if (!string.IsNullOrWhiteSpace(voice))
        {
            try { _synth.SelectVoice(voice); }
            catch { /* 指定語音缺失，退回系統預設英文語音 */ }
        }
    }

    public void Speak(string text)
    {
        _synth.SpeakAsyncCancelAll(); // 重複觸發先停前次
        if (!string.IsNullOrWhiteSpace(text))
        {
            _synth.SpeakAsync(text);
        }
    }

    public void Dispose() => _synth.Dispose();
}
