using ScreenTrans.Present;
using Xunit;

namespace ScreenTrans.Tests;

/// <summary>
/// 維運狀態顯示文字單一來源（Issue #25）：常駐主控頁與系統匣共用同一組字串，
/// 於此鎖定金鑰狀態／快捷鍵／tray 提示之呈現，確保兩入口鏡像一致、不各寫一份。
/// </summary>
public class AppStatusTextTests
{
    [Fact]
    public void KeyStatus_Ready_ShowsFilledMark()
    {
        Assert.Equal("● 金鑰已備妥（OPENAI_API_KEY）", AppStatusText.KeyStatus(true));
    }

    [Fact]
    public void KeyStatus_NotReady_ShowsHollowMark()
    {
        Assert.Equal("○ 金鑰未設定（OPENAI_API_KEY）", AppStatusText.KeyStatus(false));
    }

    [Fact]
    public void HotkeyLine_EmbedsDisplayName()
    {
        Assert.Equal("喚起快捷鍵：Alt + L", AppStatusText.HotkeyLine("Alt + L"));
    }

    [Fact]
    public void TrayTip_EmbedsHotkey()
    {
        Assert.Equal("ScreenTrans — 遊戲畫面英文查詢（Ctrl + Shift + F）",
            AppStatusText.TrayTip("Ctrl + Shift + F"));
    }
}
