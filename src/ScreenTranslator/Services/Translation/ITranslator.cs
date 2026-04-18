namespace ScreenTranslator.Services.Translation;

public interface ITranslator
{
    /// <summary>Name shown in UI / logs.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Translate <paramref name="text"/> from <paramref name="sourceLanguage"/> to <paramref name="targetLanguage"/>.
    /// Use "auto" for source to request auto-detection.
    /// </summary>
    Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct);
}
