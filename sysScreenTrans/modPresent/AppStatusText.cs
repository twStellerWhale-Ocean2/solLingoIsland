namespace ScreenTrans.Present;

/// <summary>
/// 維運狀態顯示文字之單一來源（Issue #25）：常駐主控頁與系統匣選單為同一組維運資訊之兩個入口鏡像，
/// 顯示字串一律取自本類、不各寫一份（design ＜II/III.C.(C)＞「動作來源單一」）。純函式、可單元測試。
/// </summary>
public static class AppStatusText
{
    /// <summary>金鑰狀態列（tray 選單與主控頁共用）。</summary>
    public static string KeyStatus(bool keyReady) =>
        keyReady ? "● 金鑰已備妥（OPENAI_API_KEY）" : "○ 金鑰未設定（OPENAI_API_KEY）";

    /// <summary>主控頁的喚起快捷鍵列。</summary>
    public static string HotkeyLine(string hotkeyDisplay) => $"喚起快捷鍵：{hotkeyDisplay}";

    /// <summary>系統匣停留提示（滑鼠移到圖示上顯示）。</summary>
    public static string TrayTip(string hotkeyDisplay) =>
        $"ScreenTrans — 遊戲畫面英文查詢（{hotkeyDisplay}）";
}
