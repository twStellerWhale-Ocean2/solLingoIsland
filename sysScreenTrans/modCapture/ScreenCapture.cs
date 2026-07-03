using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

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
}
