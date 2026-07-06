using ScreenTrans.Present;
using Xunit;

namespace ScreenTrans.Tests;

/// <summary>
/// 發音練習通知文案組裝（[modPresent模組] 發音回饋通知契約，spec#10／#101）：純函式——標題含目標句、
/// 內文含分數/門檻/過不過＋建議或失敗訊息、門檻邊界。不實際彈通知。
/// </summary>
public class PronNotifyTests
{
    [Fact]
    public void Result_Pass_TitleHasTarget_BodyHasScoreThresholdCheckAndNote()
    {
        var (title, body) = PronNotify.Result("Choose your weapon wisely", 88, 80, "great job");
        Assert.Contains("Choose your weapon wisely", title); // 標題明載在練哪一句
        Assert.Contains("88 / 80", body);
        Assert.Contains("passed", body);
        Assert.Contains("great job", body);                  // AI 建議在內文
    }

    [Fact]
    public void Result_BelowThreshold_BodyHasTryAgain_NoPassed()
    {
        var (_, body) = PronNotify.Result("hello", 62, 80, "");
        Assert.Contains("62 / 80", body);
        Assert.Contains("try again", body);
        Assert.DoesNotContain("passed", body);
    }

    [Fact]
    public void Result_ScoreEqualsThreshold_Passes()
    {
        var (_, body) = PronNotify.Result("hi", 80, 80, "");
        Assert.Contains("passed", body); // 邊界：== 門檻即通過
    }

    [Fact]
    public void Result_ZeroScoreNoSpeechNote_ShownAsTryAgainWithNote()
    {
        var (_, body) = PronNotify.Result("say this", 0, 80, "未偵測到朗讀");
        Assert.Contains("0 / 80", body);
        Assert.Contains("try again", body);
        Assert.Contains("未偵測到朗讀", body);
    }

    [Fact]
    public void Failure_TitleHasTarget_BodyIsMessage()
    {
        var (title, body) = PronNotify.Failure("Loading the next area", "Recording too short");
        Assert.Contains("Loading the next area", title);
        Assert.Equal("Recording too short", body);
    }

    [Fact]
    public void EmptyTarget_FallsBackToGenericTitle()
    {
        var (title, _) = PronNotify.Result("", 90, 80, "");
        Assert.Equal("Pronunciation practice", title);
        var (title2, _) = PronNotify.Failure("   ", "No microphone found");
        Assert.Equal("Pronunciation practice", title2);
    }
}
