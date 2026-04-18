using System.Runtime.Versioning;
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
