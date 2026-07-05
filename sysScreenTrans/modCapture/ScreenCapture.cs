using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace ScreenTrans.Capture;

/// <summary>
/// 依 physical-pixel 矩形截取螢幕實際像素（[modCapture模組] 選區對位契約，spec#2）。
/// 進程為 PerMonitorV2 DPI aware（app.manifest），故螢幕座標即實際像素、無需縮放換算。
/// </summary>
public static class ScreenCapture
{
    /// <summary>截取指定 physical-pixel 矩形，回 PNG byte[]；矩形無效（寬或高 ≤ 0）回 null。</summary>
    public static CaptureResult? Capture(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return new CaptureResult(ms.ToArray(), width, height);
    }

    /// <summary>
    /// 雙擊自動判斷模式（Issue #54）：截取指定 physical-pixel 矩形（通常整個螢幕），並於
    /// (<paramref name="markerX"/>,<paramref name="markerY"/>)（相對截圖左上）畫紅色圓圈十字標記游標處，
    /// 供查詢層依標記辨識該處那句英文。回 <see cref="CaptureResult"/>（<c>IsPointMode=true</c>）；矩形無效回 null。
    /// </summary>
    public static CaptureResult? CaptureWithMarker(int x, int y, int width, int height, int markerX, int markerY)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            DrawMarker(g, markerX, markerY);
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return new CaptureResult(ms.ToArray(), width, height, IsPointMode: true);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT p);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    /// <summary>
    /// 直接點選擷取（Issue #86）：不經遮罩，截取整個虛擬桌面（physical px）並於**目前游標位置**畫紅色標記，
    /// 交查詢層依標記辨識該處那句英文（等同遮罩內雙擊，但由第二熱鍵直接觸發、免雙擊誤觸遊戲）。
    /// 進程為 PerMonitorV2 DPI aware，故 <c>GetCursorPos</c>／<c>GetSystemMetrics</c> 皆回實際像素、座標一致。
    /// 虛擬桌面尺寸異常回 null。
    /// </summary>
    public static CaptureResult? CaptureAtCursor()
    {
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (!GetCursorPos(out var cur))
        {
            return null;
        }
        return CaptureWithMarker(vx, vy, vw, vh, cur.X - vx, cur.Y - vy);
    }

    /// <summary>畫游標處標記：紅色空心圓＋穿過之十字（醒目、不完全遮住底下文字，供 vision 定位）。</summary>
    private static void DrawMarker(Graphics g, int cx, int cy)
    {
        const int r = 16;
        // 白色描邊墊底提升對比（避免落在紅/暗底時看不清）
        using var halo = new Pen(Color.FromArgb(200, 255, 255, 255), 5f);
        using var red = new Pen(Color.FromArgb(235, 255, 30, 30), 2.5f);
        foreach (var pen in new[] { halo, red })
        {
            g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
            g.DrawLine(pen, cx - r - 6, cy, cx - 4, cy);   // 左
            g.DrawLine(pen, cx + 4, cy, cx + r + 6, cy);   // 右
            g.DrawLine(pen, cx, cy - r - 6, cx, cy - 4);   // 上
            g.DrawLine(pen, cx, cy + 4, cx, cy + r + 6);   // 下
        }
    }
}
