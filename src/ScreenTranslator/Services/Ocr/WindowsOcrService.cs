using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace ScreenTranslator.Services.Ocr;

/// <summary>
/// OCR using Windows.Media.Ocr. Fast, built-in, and language support is controlled by installed OS OCR language packs.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsOcrService : IOcrService
{
    // Cache engines per language — creation is not free.
    private readonly Dictionary<string, OcrEngine?> _engineCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public async Task<string> RecognizeAsync(Bitmap bitmap, string languageTag, CancellationToken ct)
    {
        var engine = GetEngine(languageTag);
        if (engine is null)
            return string.Empty;

        ct.ThrowIfCancellationRequested();

        using var softwareBitmap = BitmapToSoftwareBitmap(bitmap);
        var result = await engine.RecognizeAsync(softwareBitmap).AsTask(ct).ConfigureAwait(false);
        return result?.Text ?? string.Empty;
    }

    private OcrEngine? GetEngine(string languageTag)
    {
        lock (_gate)
        {
            if (_engineCache.TryGetValue(languageTag, out var cached))
                return cached;

            OcrEngine? engine = null;
            try
            {
                var language = new Language(languageTag);
                engine = OcrEngine.TryCreateFromLanguage(language);
            }
            catch
            {
                engine = null;
            }
            _engineCache[languageTag] = engine;
            return engine;
        }
    }

    private static SoftwareBitmap BitmapToSoftwareBitmap(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int bytes = Math.Abs(data.Stride) * data.Height;
            var pixelData = new byte[bytes];
            Marshal.Copy(data.Scan0, pixelData, 0, bytes);

            var buffer = CryptographicBuffer.CreateFromByteArray(pixelData);
            return SoftwareBitmap.CreateCopyFromBuffer(
                buffer,
                BitmapPixelFormat.Bgra8,
                bitmap.Width,
                bitmap.Height,
                BitmapAlphaMode.Premultiplied);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
