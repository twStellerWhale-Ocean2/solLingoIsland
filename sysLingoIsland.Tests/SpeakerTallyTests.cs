using System.Collections.Generic;
using LingoIsland.Video;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>
/// [modPresent模組] 說話人語句數統計（SpeakerTally，#196）：與勾選面板同一「拆原子」口徑——
/// 合唸句對每位參與者各計一次；（全部）＝總句數、（無說話人）＝未標句數；純函式、僅供顯示、不影響比對邏輯。
/// </summary>
public class SpeakerTallyTests
{
    private static readonly List<SubtitleCue> Cues = new()
    {
        new SubtitleCue("l1", 1.0, "Peppa"),
        new SubtitleCue("l2", 2.0, "Suzy"),
        new SubtitleCue("l3", 3.0, "Peppa/Suzy"),   // 合唸句：Peppa 與 Suzy 各 +1
        new SubtitleCue("l4", 4.0, "Peppa"),
        new SubtitleCue("l5", 5.0, null),            // 無說話人
        new SubtitleCue("l6", 6.0, ""),              // 無說話人
    };

    [Fact]
    public void CountBySpeaker_CountsAtoms_CoSpokenEachParticipant()
    {
        var counts = SpeakerTally.CountBySpeaker(Cues);
        Assert.Equal(3, counts["Peppa"]);   // l1 + l3(合唸) + l4
        Assert.Equal(2, counts["Suzy"]);    // l2 + l3(合唸)
        Assert.Equal(2, counts.Count);       // 只 Peppa／Suzy 兩位；無說話人不入字典
    }

    [Fact]
    public void CountBySpeaker_IsCaseInsensitive_ByDefault()
    {
        var cues = new List<SubtitleCue>
        {
            new SubtitleCue("a", 1.0, "Rocky"),
            new SubtitleCue("b", 2.0, "rocky"),
        };
        var counts = SpeakerTally.CountBySpeaker(cues);
        Assert.Single(counts);               // Rocky／rocky 視為同一人（與面板去重比較器一致）
        Assert.Equal(2, counts["Rocky"]);
    }

    [Fact]
    public void CountBySpeaker_SameNameTwiceInOneCue_CountsOnce()
    {
        var cues = new List<SubtitleCue> { new SubtitleCue("x", 1.0, "Peppa/Peppa") };
        var counts = SpeakerTally.CountBySpeaker(cues);
        Assert.Equal(1, counts["Peppa"]);    // 同句內同名不重複計
    }

    [Fact]
    public void TotalCount_IsAllCues()
    {
        Assert.Equal(6, SpeakerTally.TotalCount(Cues));
    }

    [Fact]
    public void NoSpeakerCount_CountsNullAndEmpty()
    {
        Assert.Equal(2, SpeakerTally.NoSpeakerCount(Cues));   // l5(null) + l6("")
    }

    [Fact]
    public void CountBySpeaker_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(SpeakerTally.CountBySpeaker(new List<SubtitleCue>()));
        Assert.Equal(0, SpeakerTally.TotalCount(new List<SubtitleCue>()));
        Assert.Equal(0, SpeakerTally.NoSpeakerCount(new List<SubtitleCue>()));
    }
}
