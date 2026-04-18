namespace ScreenTranslator.Services.Pipeline;

public sealed record TickResult(
    string OcrText,
    string TranslatedText,
    long CaptureMs,
    long OcrMs,
    long TranslateMs,
    bool Skipped,
    string? Error);
