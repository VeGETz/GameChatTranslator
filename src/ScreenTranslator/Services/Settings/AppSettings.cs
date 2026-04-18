namespace ScreenTranslator.Services.Settings;

public enum TranslatorKind
{
    GoogleFree = 0,
    GoogleCloud = 1,
}

public sealed class AppSettings
{
    public bool TranslationEnabled { get; set; } = false;
    public int IntervalMilliseconds { get; set; } = 800;

    // Hotkey
    public uint HotkeyModifiers { get; set; } = WinModifiers.Control | WinModifiers.Alt; // Ctrl+Alt
    public uint HotkeyVirtualKey { get; set; } = 0x54; // 'T'

    // Languages
    public string? OcrLanguageTag { get; set; } // e.g. "en-US", null = pick first available
    public string TargetLanguage { get; set; } = "en";

    // Translator
    public TranslatorKind Translator { get; set; } = TranslatorKind.GoogleFree;
    /// <summary>Base64 of DPAPI-protected API key bytes (CurrentUser scope).</summary>
    public string? GoogleCloudApiKeyProtected { get; set; }

    // Chat parsing / sticky messages
    /// <summary>How long a message stays on the overlay after it stops being seen by OCR.</summary>
    public int MessageTtlSeconds { get; set; } = 3;
    /// <summary>
    /// Regex applied to each OCR line. If it matches and has groups "name" and "text",
    /// only "text" is translated and "name" is preserved verbatim. If no match, the whole line
    /// is translated. Default matches "[PlayerName]: message".
    /// </summary>
    public string ChatLineRegex { get; set; } = @"^\[(?<name>[^\]]+)\]\s*:\s*(?<text>.*)$";
    public bool SkipPlayerNames { get; set; } = true;
    /// <summary>
    /// When true, OCR lines that do NOT match <see cref="ChatLineRegex"/> are dropped entirely
    /// (no translation, no overlay entry). Useful for filtering out voice-line captions,
    /// hero-change notifications, killfeed, etc. that share the chat area.
    /// </summary>
    public bool OnlyTranslateMatchingLines { get; set; } = false;
    /// <summary>Maximum number of sticky messages kept in memory / shown on overlay.</summary>
    public int MaxVisibleMessages { get; set; } = 12;

    // Overlay
    public double OverlayLeft { get; set; } = 200;
    public double OverlayTop { get; set; } = 200;
    public double OverlayWidth { get; set; } = 600;
    public double OverlayHeight { get; set; } = 160;
    public double TranslationPanelHeight { get; set; } = 120;
    public bool OverlayLocked { get; set; } = false;
    public bool ShowOverlay { get; set; } = true;
}

/// <summary>Duplicate of common WinInterop modifier flags so AppSettings has no interop dependency.</summary>
public static class WinModifiers
{
    public const uint Alt = 0x0001;
    public const uint Control = 0x0002;
    public const uint Shift = 0x0004;
    public const uint Win = 0x0008;
}
