namespace ScreenTranslator.Services.Settings;

public enum TranslatorKind
{
    GoogleFree = 0,
    GoogleCloud = 1,
    Llm = 2,
}

public enum LlmProviderType
{
    /// <summary>
    /// Chat Completions on any OpenAI-shaped endpoint: OpenAI, Ollama, LM Studio,
    /// OpenRouter, Groq, Together, xAI, Mistral, LocalAI, vLLM, etc.
    /// POST {baseUrl}/chat/completions with Authorization: Bearer {key}.
    /// </summary>
    OpenAICompatible = 0,

    /// <summary>
    /// Legacy Azure OpenAI Chat Completions:
    /// POST {baseUrl}/openai/deployments/{deployment}/chat/completions?api-version=... with api-key header.
    /// Still works for most existing Azure deployments.
    /// </summary>
    AzureChatCompletions = 1,

    /// <summary>
    /// Modern Azure OpenAI Responses API (2025+, required for GPT-5 family):
    /// POST {baseUrl}/openai/responses?api-version=... with Authorization: Bearer {key}.
    /// Model/deployment goes in the request body.
    /// </summary>
    AzureResponses = 2,

    /// <summary>
    /// OpenAI Responses API: POST {baseUrl}/responses with Authorization: Bearer {key}.
    /// </summary>
    OpenAIResponses = 3,
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

    // LLM translator (OpenAI-compatible; covers OpenAI, Azure, Ollama, LM Studio, OpenRouter, Groq, etc.)
    public LlmProviderType LlmProvider { get; set; } = LlmProviderType.OpenAICompatible;
    /// <summary>e.g. "https://api.openai.com/v1", "http://localhost:11434/v1", "https://myresource.openai.azure.com"</summary>
    public string LlmBaseUrl { get; set; } = "https://api.openai.com/v1";
    /// <summary>OpenAI model id (e.g. "gpt-4o-mini"), or for Azure the deployment name.</summary>
    public string LlmModel { get; set; } = "gpt-4o-mini";
    /// <summary>DPAPI-protected (CurrentUser) base64 of the LLM API key. Optional for local servers.</summary>
    public string? LlmApiKeyProtected { get; set; }
    /// <summary>
    /// Only used when <see cref="LlmProvider"/> is one of the Azure modes
    /// (<see cref="LlmProviderType.AzureChatCompletions"/> or <see cref="LlmProviderType.AzureResponses"/>).
    /// </summary>
    public string LlmAzureApiVersion { get; set; } = "2025-04-01-preview";
    public double LlmTemperature { get; set; } = 0.2;
    /// <summary>
    /// System prompt. "{source}" / "{target}" placeholders are replaced at call-time with
    /// the configured OCR source language and target language.
    /// </summary>
    public string LlmSystemPrompt { get; set; } =
        "You are a translation engine for live in-game chat. " +
        "Translate the user's message from {source} to {target}. " +
        "Output ONLY the translated text — no explanations, no quotes, no prefixes, no language labels. " +
        "Preserve proper nouns, player names, game/hero/champion/item names, and common gaming slang. " +
        "Keep it concise and natural. If the text is already in {target}, output it unchanged.";

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
    /// Substrings that cause a line to be skipped. Case-insensitive `Contains` match against
    /// the raw OCR line. Useful for filtering voice-line captions, hero-change notifications,
    /// killfeed, etc. that share the chat area. More tolerant than a regex filter because OCR
    /// noise can break strict pattern matching.
    /// </summary>
    public List<string> BlockedPhrases { get; set; } = new();
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
