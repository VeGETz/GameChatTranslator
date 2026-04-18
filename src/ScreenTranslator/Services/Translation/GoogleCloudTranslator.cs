using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using ScreenTranslator.Services.Settings;

namespace ScreenTranslator.Services.Translation;

/// <summary>
/// Official Google Cloud Translation v2 REST API. Requires an API key.
/// https://translation.googleapis.com/language/translate/v2
/// </summary>
public sealed class GoogleCloudTranslator : ITranslator
{
    private readonly HttpClient _http;
    private readonly SettingsStore _settings;
    private readonly TranslationCache _cache = new();

    public GoogleCloudTranslator(HttpClient http, SettingsStore settings)
    {
        _http = http;
        _settings = settings;
    }

    public string DisplayName => "Google Cloud Translation (API key)";

    public async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var apiKey = _settings.GetGoogleCloudApiKey();
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("Google Cloud API key is not set.");

        if (_cache.TryGet(text, sourceLanguage, targetLanguage, out var cached))
            return cached;

        var url = $"https://translation.googleapis.com/language/translate/v2?key={Uri.EscapeDataString(apiKey)}";

        var payload = new Dictionary<string, object?>
        {
            ["q"] = text,
            ["target"] = targetLanguage,
            ["format"] = "text",
        };
        if (!string.IsNullOrEmpty(sourceLanguage) && sourceLanguage != "auto")
            payload["source"] = sourceLanguage;

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload),
        };

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var translations = doc.RootElement
            .GetProperty("data")
            .GetProperty("translations");
        string result = string.Empty;
        if (translations.GetArrayLength() > 0)
            result = translations[0].GetProperty("translatedText").GetString() ?? string.Empty;

        _cache.Set(text, sourceLanguage, targetLanguage, result);
        return result;
    }
}
