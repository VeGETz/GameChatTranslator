using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenTranslator.Services.Hotkey;
using ScreenTranslator.Services.Ocr;
using ScreenTranslator.Services.Pipeline;
using ScreenTranslator.Services.Settings;
using ScreenTranslator.Services.Translation;

namespace ScreenTranslator.ViewModels;

[SupportedOSPlatform("windows10.0.19041.0")]
public sealed partial class ControlPanelViewModel : ObservableObject
{
    private readonly SettingsStore _settings;
    private readonly TranslationLoop _loop;
    private readonly IHotkeyService _hotkey;
    private readonly GoogleCloudTranslator _googleCloud;
    private readonly LlmTranslator _llmTranslator;
    private readonly OverlayViewModel _overlayVm;

    public ControlPanelViewModel(
        SettingsStore settings,
        TranslationLoop loop,
        IHotkeyService hotkey,
        GoogleCloudTranslator googleCloud,
        LlmTranslator llmTranslator,
        OverlayViewModel overlayVm)
    {
        _settings = settings;
        _loop = loop;
        _hotkey = hotkey;
        _googleCloud = googleCloud;
        _llmTranslator = llmTranslator;
        _overlayVm = overlayVm;

        // Seed from persisted settings
        var s = _settings.Current;
        translationEnabled = s.TranslationEnabled;
        intervalMs = s.IntervalMilliseconds;
        targetLanguage = s.TargetLanguage;
        translator = s.Translator;
        overlayLocked = s.OverlayLocked;
        showOverlay = s.ShowOverlay;
        googleCloudApiKey = _settings.GetGoogleCloudApiKey() ?? string.Empty;
        llmProvider = s.LlmProvider;
        llmBaseUrl = s.LlmBaseUrl;
        llmModel = s.LlmModel;
        llmApiKey = _settings.GetLlmApiKey() ?? string.Empty;
        llmAzureApiVersion = s.LlmAzureApiVersion;
        llmTemperature = s.LlmTemperature;
        llmSystemPrompt = s.LlmSystemPrompt;
        messageTtlSeconds = s.MessageTtlSeconds;
        chatLineRegex = s.ChatLineRegex;
        skipPlayerNames = s.SkipPlayerNames;
        blockedPhrasesText = string.Join(Environment.NewLine, s.BlockedPhrases);
        maxVisibleMessages = s.MaxVisibleMessages;

        HotkeyDisplay = FormatHotkey(s.HotkeyModifiers, s.HotkeyVirtualKey);

        RefreshOcrLanguages();

        // Stash the loop tick callback on a marshaled handler
        _loop.Ticked += OnTick;

        _hotkey.Pressed += () => Application.Current.Dispatcher.BeginInvoke(() => TranslationEnabled = !TranslationEnabled);

        _overlayVm.TranslationEnabled = translationEnabled;
        _overlayVm.Locked = overlayLocked;
        _overlayVm.Visible = showOverlay;
    }

    // ---------- Observable state ----------

    [ObservableProperty]
    private bool translationEnabled;

    partial void OnTranslationEnabledChanged(bool value)
    {
        _settings.Current.TranslationEnabled = value;
        _settings.Save();
        _overlayVm.TranslationEnabled = value;
        if (value) _loop.Start(); else _loop.Stop();
    }

    [ObservableProperty]
    private int intervalMs;

    partial void OnIntervalMsChanged(int value)
    {
        _settings.Current.IntervalMilliseconds = Math.Clamp(value, 200, 5000);
        _settings.Save();
    }

    public ObservableCollection<OcrLanguage> AvailableOcrLanguages { get; } = new();

    [ObservableProperty]
    private OcrLanguage? selectedOcrLanguage;

    partial void OnSelectedOcrLanguageChanged(OcrLanguage? value)
    {
        _settings.Current.OcrLanguageTag = value?.Tag;
        _settings.Save();
    }

    [ObservableProperty]
    private string targetLanguage;

    partial void OnTargetLanguageChanged(string value)
    {
        _settings.Current.TargetLanguage = value;
        _settings.Save();
    }

    public IReadOnlyList<TranslatorKind> AvailableTranslators { get; } =
        new[] { TranslatorKind.GoogleFree, TranslatorKind.GoogleCloud, TranslatorKind.Llm };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGoogleCloudSelected))]
    [NotifyPropertyChangedFor(nameof(IsLlmSelected))]
    [NotifyPropertyChangedFor(nameof(IsAzureLlmSelected))]
    private TranslatorKind translator;

    partial void OnTranslatorChanged(TranslatorKind value)
    {
        _settings.Current.Translator = value;
        _settings.Save();
    }

    public bool IsGoogleCloudSelected => Translator == TranslatorKind.GoogleCloud;
    public bool IsLlmSelected         => Translator == TranslatorKind.Llm;
    public bool IsAzureLlmSelected    => Translator == TranslatorKind.Llm &&
                                         (LlmProvider == LlmProviderType.AzureChatCompletions ||
                                          LlmProvider == LlmProviderType.AzureResponses);

    [ObservableProperty]
    private string googleCloudApiKey;

    partial void OnGoogleCloudApiKeyChanged(string value)
    {
        _settings.SetGoogleCloudApiKey(string.IsNullOrWhiteSpace(value) ? null : value);
        _settings.Save();
    }

    [ObservableProperty]
    private bool overlayLocked;

    partial void OnOverlayLockedChanged(bool value)
    {
        _settings.Current.OverlayLocked = value;
        _settings.Save();
        _overlayVm.Locked = value;
    }

    [ObservableProperty]
    private bool showOverlay;

    partial void OnShowOverlayChanged(bool value)
    {
        _settings.Current.ShowOverlay = value;
        _settings.Save();
        _overlayVm.Visible = value;
    }

    // ---------- LLM translator settings ----------

    public IReadOnlyList<LlmProviderType> AvailableLlmProviders { get; } = new[]
    {
        LlmProviderType.OpenAICompatible,     // OpenAI / Ollama / LM Studio / OpenRouter / Groq / etc.
        LlmProviderType.OpenAIResponses,      // OpenAI's new /v1/responses endpoint
        LlmProviderType.AzureChatCompletions, // Legacy Azure (api-key header)
        LlmProviderType.AzureResponses,       // Modern Azure /openai/responses (GPT-5 etc.)
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAzureLlmSelected))]
    private LlmProviderType llmProvider;

    partial void OnLlmProviderChanged(LlmProviderType value)
    {
        _settings.Current.LlmProvider = value;
        _settings.Save();
    }

    [ObservableProperty]
    private string llmBaseUrl;

    partial void OnLlmBaseUrlChanged(string value)
    {
        _settings.Current.LlmBaseUrl = value ?? string.Empty;
        _settings.Save();
    }

    [ObservableProperty]
    private string llmModel;

    partial void OnLlmModelChanged(string value)
    {
        _settings.Current.LlmModel = value ?? string.Empty;
        _settings.Save();
    }

    [ObservableProperty]
    private string llmApiKey;

    partial void OnLlmApiKeyChanged(string value)
    {
        _settings.SetLlmApiKey(string.IsNullOrWhiteSpace(value) ? null : value);
        _settings.Save();
    }

    [ObservableProperty]
    private string llmAzureApiVersion;

    partial void OnLlmAzureApiVersionChanged(string value)
    {
        _settings.Current.LlmAzureApiVersion = value ?? string.Empty;
        _settings.Save();
    }

    [ObservableProperty]
    private double llmTemperature;

    partial void OnLlmTemperatureChanged(double value)
    {
        _settings.Current.LlmTemperature = Math.Clamp(value, 0.0, 2.0);
        _settings.Save();
    }

    [ObservableProperty]
    private string llmSystemPrompt;

    partial void OnLlmSystemPromptChanged(string value)
    {
        _settings.Current.LlmSystemPrompt = value ?? string.Empty;
        _settings.Save();
    }

    [RelayCommand]
    private void ApplyLlmPreset(string preset)
    {
        switch (preset)
        {
            case "openai":
                LlmProvider = LlmProviderType.OpenAICompatible;
                LlmBaseUrl = "https://api.openai.com/v1";
                LlmModel = "gpt-4o-mini";
                break;
            case "azure":
                // Modern Azure: Responses API (required for GPT-5 family, recommended for new deployments).
                LlmProvider = LlmProviderType.AzureResponses;
                LlmBaseUrl = "https://YOUR-RESOURCE.cognitiveservices.azure.com";
                LlmModel = "your-deployment-name";
                LlmAzureApiVersion = "2025-04-01-preview";
                break;
            case "azure-legacy":
                // Legacy Azure: Chat Completions with api-key header.
                LlmProvider = LlmProviderType.AzureChatCompletions;
                LlmBaseUrl = "https://YOUR-RESOURCE.openai.azure.com";
                LlmModel = "your-deployment-name";
                LlmAzureApiVersion = "2024-08-01-preview";
                break;
            case "openai-responses":
                LlmProvider = LlmProviderType.OpenAIResponses;
                LlmBaseUrl = "https://api.openai.com/v1";
                LlmModel = "gpt-4o-mini";
                break;
            case "ollama":
                LlmProvider = LlmProviderType.OpenAICompatible;
                LlmBaseUrl = "http://localhost:11434/v1";
                LlmModel = "llama3.2";
                LlmApiKey = string.Empty;
                break;
            case "lmstudio":
                LlmProvider = LlmProviderType.OpenAICompatible;
                LlmBaseUrl = "http://localhost:1234/v1";
                LlmApiKey = string.Empty;
                break;
            case "openrouter":
                LlmProvider = LlmProviderType.OpenAICompatible;
                LlmBaseUrl = "https://openrouter.ai/api/v1";
                LlmModel = "anthropic/claude-3.5-sonnet";
                break;
            case "groq":
                LlmProvider = LlmProviderType.OpenAICompatible;
                LlmBaseUrl = "https://api.groq.com/openai/v1";
                LlmModel = "llama-3.3-70b-versatile";
                break;
        }
    }

    [RelayCommand]
    private async Task TestLlmAsync()
    {
        try
        {
            StatusText = "Testing LLM…";
            var result = await _llmTranslator.TranslateAsync(
                "สวัสดี เพื่อน วันนี้เล่นดีมาก",
                "auto", TargetLanguage, CancellationToken.None);
            StatusText = $"LLM OK: {result}";
        }
        catch (Exception ex)
        {
            StatusText = $"LLM FAILED: {ex.Message}";
        }
    }

    [ObservableProperty]
    private int messageTtlSeconds;

    partial void OnMessageTtlSecondsChanged(int value)
    {
        _settings.Current.MessageTtlSeconds = Math.Clamp(value, 1, 60);
        _settings.Save();
    }

    [ObservableProperty]
    private string chatLineRegex;

    partial void OnChatLineRegexChanged(string value)
    {
        _settings.Current.ChatLineRegex = value ?? string.Empty;
        _settings.Save();
    }

    [ObservableProperty]
    private bool skipPlayerNames;

    partial void OnSkipPlayerNamesChanged(bool value)
    {
        _settings.Current.SkipPlayerNames = value;
        _settings.Save();
    }

    [ObservableProperty]
    private string blockedPhrasesText;

    partial void OnBlockedPhrasesTextChanged(string value)
    {
        _settings.Current.BlockedPhrases = (value ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _settings.Save();
    }

    [ObservableProperty]
    private int maxVisibleMessages;

    partial void OnMaxVisibleMessagesChanged(int value)
    {
        _settings.Current.MaxVisibleMessages = Math.Clamp(value, 1, 50);
        _settings.Save();
    }

    [ObservableProperty]
    private string hotkeyDisplay = string.Empty;

    [ObservableProperty]
    private string statusText = "Idle";

    [ObservableProperty]
    private string lastOcrText = string.Empty;

    // ---------- Commands ----------

    [RelayCommand]
    private void RefreshOcrLanguages()
    {
        AvailableOcrLanguages.Clear();
        foreach (var lang in OcrLanguageHelper.GetInstalled())
            AvailableOcrLanguages.Add(lang);

        var desiredTag = _settings.Current.OcrLanguageTag;
        SelectedOcrLanguage = AvailableOcrLanguages.FirstOrDefault(l =>
            string.Equals(l.Tag, desiredTag, StringComparison.OrdinalIgnoreCase))
            ?? AvailableOcrLanguages.FirstOrDefault();
    }

    [RelayCommand]
    private async Task TestTranslatorAsync()
    {
        try
        {
            var result = await _googleCloud.TranslateAsync("Hello world", "en", TargetLanguage, CancellationToken.None);
            StatusText = $"Google Cloud OK: {result}";
        }
        catch (Exception ex)
        {
            StatusText = $"Google Cloud FAILED: {ex.Message}";
        }
    }

    public void CaptureHotkey(uint modifiers, uint virtualKey)
    {
        _settings.Current.HotkeyModifiers = modifiers;
        _settings.Current.HotkeyVirtualKey = virtualKey;
        _settings.Save();
        HotkeyDisplay = FormatHotkey(modifiers, virtualKey);
        _hotkey.Register(modifiers, virtualKey);
    }

    public static string FormatHotkey(uint mods, uint vk)
    {
        var parts = new List<string>();
        if ((mods & WinModifiers.Control) != 0) parts.Add("Ctrl");
        if ((mods & WinModifiers.Alt) != 0) parts.Add("Alt");
        if ((mods & WinModifiers.Shift) != 0) parts.Add("Shift");
        if ((mods & WinModifiers.Win) != 0) parts.Add("Win");
        parts.Add(VirtualKeyToString(vk));
        return string.Join(" + ", parts);
    }

    private static string VirtualKeyToString(uint vk)
    {
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
        if (vk >= 0x70 && vk <= 0x7B) return $"F{vk - 0x70 + 1}";
        return $"VK_{vk:X2}";
    }

    private void OnTick(TickResult r)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (r.Error is not null)
            {
                StatusText = $"Error: {r.Error}";
                return;
            }
            LastOcrText = r.OcrText;
            _overlayVm.TranslatedText = r.TranslatedText;
            StatusText = r.Skipped
                ? $"skipped (cap {r.CaptureMs}ms, ocr {r.OcrMs}ms)"
                : $"cap {r.CaptureMs}ms · ocr {r.OcrMs}ms · trans {r.TranslateMs}ms";
        });
    }
}
