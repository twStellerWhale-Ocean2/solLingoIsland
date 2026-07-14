using LingoIsland.Present;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>
/// [modPresent] 依 theme 篩選之配對純函式（ThemeFilter.Match，多媒體主題管理·B）：
/// null 篩選（All）恆真；指定篩選僅同 ThemeId 相符。
/// </summary>
public class ThemeFilterTests
{
    [Fact]
    public void Match_NullFilter_MatchesEverything()
    {
        Assert.True(ThemeFilter.Match(null, "theme-a"));
        Assert.True(ThemeFilter.Match(null, null)); // All 也含未標主題之項
    }

    [Fact]
    public void Match_SpecificFilter_OnlySameThemeId()
    {
        Assert.True(ThemeFilter.Match("theme-a", "theme-a"));
        Assert.False(ThemeFilter.Match("theme-a", "theme-b"));
        Assert.False(ThemeFilter.Match("theme-a", null)); // 未標主題之項不入特定 theme
    }
}
