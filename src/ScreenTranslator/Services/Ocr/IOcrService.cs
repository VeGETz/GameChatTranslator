using System.Drawing;

namespace ScreenTranslator.Services.Ocr;

public interface IOcrService
{
    /// <summary>
    /// Runs OCR on the given bitmap and returns the recognized text, or empty string if nothing recognized.
    /// The bitmap is read-only from OCR's point of view and may be reused by the caller afterward.
    /// </summary>
    Task<string> RecognizeAsync(Bitmap bitmap, string languageTag, CancellationToken ct);
}
