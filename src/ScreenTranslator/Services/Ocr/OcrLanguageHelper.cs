using System.Runtime.Versioning;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace ScreenTranslator.Services.Ocr;

public sealed record OcrLanguage(string Tag, string DisplayName);

[SupportedOSPlatform("windows10.0.19041.0")]
public static class OcrLanguageHelper
{
    /// <summary>Languages currently installed on this machine that the Windows OCR engine can use.</summary>
    public static IReadOnlyList<OcrLanguage> GetInstalled()
    {
        var list = new List<OcrLanguage>();
        foreach (var lang in OcrEngine.AvailableRecognizerLanguages)
        {
            list.Add(new OcrLanguage(lang.LanguageTag, $"{lang.NativeName} ({lang.LanguageTag})"));
        }
        return list;
    }

    /// <summary>
    /// Authoritative check: can the OCR engine actually handle this tag right now?
    /// Handles the case where AvailableRecognizerLanguages returns a short tag like "ko"
    /// while the caller asks about "ko-KR" (or vice versa). Also checks primary subtag
    /// (language part before "-") as a fallback.
    /// </summary>
    public static bool IsOcrAvailable(string bcp47Tag)
    {
        if (string.IsNullOrWhiteSpace(bcp47Tag)) return false;

        // Direct attempt: if TryCreateFromLanguage succeeds, we can OCR this tag.
        try
        {
            var language = new Language(bcp47Tag);
            if (OcrEngine.TryCreateFromLanguage(language) is not null) return true;
        }
        catch { /* invalid tag string — fall through */ }

        // Fallback: match on primary subtag against the available recognizer list
        // (e.g. "ko-KR" requested, only "ko" is reported — treat as installed).
        var primary = PrimarySubtag(bcp47Tag);
        foreach (var avail in OcrEngine.AvailableRecognizerLanguages)
        {
            if (string.Equals(PrimarySubtag(avail.LanguageTag), primary, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string PrimarySubtag(string tag)
    {
        var idx = tag.IndexOf('-');
        return idx > 0 ? tag.Substring(0, idx) : tag;
    }

    /// <summary>
    /// Common OCR language capability names for Add-WindowsCapability.
    /// Shown to the user in the "Add OCR language" dialog.
    /// </summary>
    public static IReadOnlyList<(string DisplayName, string Tag, string CapabilityName)> CommonOcrLanguages { get; } = new[]
    {
        ("English (US)",                  "en-US", "Language.OCR~~~en-US~0.0.1.0"),
        ("Thai",                          "th-TH", "Language.OCR~~~th-TH~0.0.1.0"),
        ("Japanese",                      "ja-JP", "Language.OCR~~~ja-JP~0.0.1.0"),
        ("Korean",                        "ko-KR", "Language.OCR~~~ko-KR~0.0.1.0"),
        ("Chinese (Simplified)",          "zh-CN", "Language.OCR~~~zh-CN~0.0.1.0"),
        ("Chinese (Traditional)",         "zh-TW", "Language.OCR~~~zh-TW~0.0.1.0"),
        ("Russian",                       "ru-RU", "Language.OCR~~~ru-RU~0.0.1.0"),
        ("Spanish",                       "es-ES", "Language.OCR~~~es-ES~0.0.1.0"),
        ("French",                        "fr-FR", "Language.OCR~~~fr-FR~0.0.1.0"),
        ("German",                        "de-DE", "Language.OCR~~~de-DE~0.0.1.0"),
        ("Italian",                       "it-IT", "Language.OCR~~~it-IT~0.0.1.0"),
        ("Portuguese (Brazil)",           "pt-BR", "Language.OCR~~~pt-BR~0.0.1.0"),
        ("Vietnamese",                    "vi-VN", "Language.OCR~~~vi-VN~0.0.1.0"),
        ("Arabic (Saudi Arabia)",         "ar-SA", "Language.OCR~~~ar-SA~0.0.1.0"),
    };

    public static string BuildInstallCommand(string capabilityName) =>
        $"Add-WindowsCapability -Online -Name \"{capabilityName}\"";
}
