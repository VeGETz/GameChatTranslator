using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using ScreenTranslator.Infrastructure;

namespace ScreenTranslator.Services.Capture;

/// <summary>
/// Screen capture using GDI BitBlt. Small region, low overhead, no anti-cheat concerns.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GdiScreenCapturer : IScreenCapturer
{
    private Bitmap? _buffer;
    private int _bufferWidth;
    private int _bufferHeight;
    private readonly object _gate = new();

    public Bitmap Capture(Rectangle screenRect)
    {
        if (screenRect.Width <= 0 || screenRect.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(screenRect), "Capture rect must be positive.");

        lock (_gate)
        {
            EnsureBuffer(screenRect.Width, screenRect.Height);
            var buffer = _buffer!;

            using var g = Graphics.FromImage(buffer);
            var srcDc = g.GetHdc();
            var desktopDc = WinInterop.GetDC(WinInterop.GetDesktopWindow());
            try
            {
                WinInterop.BitBlt(
                    srcDc, 0, 0, screenRect.Width, screenRect.Height,
                    desktopDc, screenRect.X, screenRect.Y,
                    WinInterop.SRCCOPY | WinInterop.CAPTUREBLT);
            }
            finally
            {
                g.ReleaseHdc(srcDc);
                WinInterop.ReleaseDC(WinInterop.GetDesktopWindow(), desktopDc);
            }
            return buffer;
        }
    }

    private void EnsureBuffer(int width, int height)
    {
        if (_buffer is not null && _bufferWidth == width && _bufferHeight == height)
            return;

        _buffer?.Dispose();
        _buffer = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        _bufferWidth = width;
        _bufferHeight = height;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _buffer?.Dispose();
            _buffer = null;
        }
    }
}
