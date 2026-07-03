namespace ScreenTrans.Capture;

/// <summary>
/// 選區擷取結果：PNG 影像位元組＋像素尺寸。
/// 對應 design ＜III.B.(A)＞ [modCapture模組]→[modQuery模組] 之 ICaptureResult。
/// </summary>
public sealed record CaptureResult(byte[] PngBytes, int Width, int Height);
