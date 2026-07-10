using ScreenTrans.Present;
using Xunit;

namespace ScreenTrans.Tests;

/// <summary>筆記卡外框加深之暗色計算（Issue #118→#123：0.80「識別性太淺」改 0.62）：各通道 ×0.62、截斷取整——
/// 純函式定本測試，供 intTest#47 位圖精確色計數鏡像同一公式。</summary>
public class NoteCardBrushTests
{
    [Theory]
    [InlineData(255, 255, 255, 158, 158, 158)] // 白卡 → 灰框 #9E9E9E（×0.62，精確、無特例）
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(0xFB, 0xE4, 0xEC, 155, 141, 146)] // Pink #FBE4EC → 框 #9B8D92
    [InlineData(0xB4, 0xEB, 0xFF, 111, 145, 158)] // Sky（#109 最深底）→ 框 #6F919E
    public void Darken_MultiplicativeTruncation(byte r, byte g, byte b, byte er, byte eg, byte eb)
    {
        Assert.Equal((er, eg, eb), NoteCardBrush.Darken(r, g, b));
    }

    [Fact]
    public void DarkenFactor_Pinned()
    {
        Assert.Equal(0.62, NoteCardBrush.DarkenFactor, 3); // #123 定本（design ＜III.C.(C)＞）
    }

    [Theory]
    [InlineData(true, true)]   // 通過＋透明開 → 透明底
    [InlineData(true, false)]  // 通過＋透明關 → 素底（#123 可選）
    [InlineData(false, true)]  // 未過 → 素底
    public void For_PassedTransparentToggle(bool passed, bool passedTransparent)
    {
        var brush = NoteCardBrush.For("#FBE4EC", passed, passedTransparent);
        bool isTransparent = ReferenceEquals(brush, System.Windows.Media.Brushes.Transparent);
        Assert.Equal(passed && passedTransparent, isTransparent);
    }
}
