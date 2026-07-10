using ScreenTrans.Present;
using Xunit;

namespace ScreenTrans.Tests;

/// <summary>筆記卡外框加深之暗色計算（Issue #118；#111 星色 0.72 廢止改 0.80）：各通道 ×0.80、截斷取整——
/// 純函式定本測試，供 intTest#47 位圖精確色計數鏡像同一公式。</summary>
public class NoteCardBrushTests
{
    [Theory]
    [InlineData(255, 255, 255, 204, 204, 204)] // 白卡 → 淺灰框 #CCCCCC（精確、無特例）
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(0xFB, 0xE4, 0xEC, 200, 182, 188)] // Pink #FBE4EC → 框 #C8B6BC
    [InlineData(0xB4, 0xEB, 0xFF, 144, 188, 204)] // Sky（#109 最深底）→ 框 #90BCCC
    public void Darken_MultiplicativeTruncation(byte r, byte g, byte b, byte er, byte eg, byte eb)
    {
        Assert.Equal((er, eg, eb), NoteCardBrush.Darken(r, g, b));
    }

    [Fact]
    public void DarkenFactor_Pinned()
    {
        Assert.Equal(0.80, NoteCardBrush.DarkenFactor, 3); // #118 定本（design ＜III.C.(C)＞）
    }
}
