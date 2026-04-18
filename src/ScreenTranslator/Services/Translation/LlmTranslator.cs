using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ScreenTranslator.Services.Settings;

namespace ScreenTranslator.Services.Translation;

/// <summary>
/// Unified LLM translator. Supports both legacy Chat Completions and the new Responses API,
/// for both cloud (OpenAI / Azure) and local (Ollama, LM Studio, etc.) endpoints.
/// </summary>
public sealed class LlmTranslator : ITranslator
{
    private readonly HttpClient _http;
    private readonly SettingsStore _settings;
    private readonly TranslationCache _cache = new();

    public LlmTranslator(HttpClient http, SettingsStore settings)
    {
        _http = http;
        _settings = settings;
    }

    public string DisplayName => "LLM (OpenAI / Azure / local)";

    public async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        if (_cache.TryGet(text, sourceLanguage, targetLanguage, out var cached))
            return cached;

        var s = _settings.Current;
        if (string.IsNullOrWhiteSpace(s.LlmBaseUrl) || string.IsNullOrWhiteSpace(s.LlmModel))
            throw new InvalidOperationException("LLM base URL or model is not configured.");

        var url = BuildUrl(s);
        var isResponses = IsResponsesApi(s.LlmProvider);
        var payload = BuildPayload(s, text, sourceLanguage, targetLanguage, isResponses);

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload),
        };

        ApplyAuth(req, s);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException($"LLM HTTP {(int)resp.StatusCode}: {Truncate(errBody, 400)}");
        }

        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var content = ExtractText(doc.RootElement);
        content = CleanResponse(content);
        _cache.Set(text, sourceLanguage, targetLanguage, content);
        return content;
    }

    // ---------- URL ----------

    private static string BuildUrl(AppSettings s)
    {
        var baseUrl = s.LlmBaseUrl.TrimEnd('/');
        return s.LlmProvider switch
        {
            LlmProviderType.AzureChatCompletions =>
                $"{baseUrl}/openai/deployments/{Uri.EscapeDataString(s.LlmModel)}/chat/completions?api-version={Uri.EscapeDataString(s.LlmAzureApiVersion)}",

            LlmProviderType.AzureResponses =>
                $"{baseUrl}/openai/responses?api-version={Uri.EscapeDataString(s.LlmAzureApiVersion)}",

            LlmProviderType.OpenAIResponses =>
                baseUrl.EndsWith("/responses", StringComparison.OrdinalIgnoreCase)
                    ? baseUrl
                    : $"{baseUrl}/responses",

            // OpenAICompatible — Chat Completions on OpenAI / Ollama / LM Studio / OpenRouter / etc.
            _ => baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                ? baseUrl
                : $"{baseUrl}/chat/completions",
        };
    }

    // ---------- Auth ----------

    private void ApplyAuth(HttpRequestMessage req, AppSettings s)
    {
        var apiKey = _settings.GetLlmApiKey();

        // Legacy Azure Chat Completions uses the api-key header.
        if (s.LlmProvider == LlmProviderType.AzureChatCompletions)
        {
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("Azure OpenAI requires an API key.");
            req.Headers.Add("api-key", apiKey);
            return;
        }

        // Everyone else (OpenAI Chat Completions, OpenAI Responses, Azure Responses, local OpenAI-compatible)
        // uses Authorization: Bearer. For local servers (Ollama / LM Studio) the key is usually empty/unused.
        if (!string.IsNullOrEmpty(apiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    // ---------- Payload ----------

    private static object BuildPayload(AppSettings s, string text, string sourceLanguage, string targetLanguage, bool isResponses)
    {
        var systemPrompt = BuildSystemPrompt(s.LlmSystemPrompt, sourceLanguage, targetLanguage);

        // Chat Completions uses "messages" and "temperature".
        // Responses API accepts "input" as the canonical field, but also accepts "messages"
        // for chat-completions-style compatibility (which Azure's samples use). We send
        // "input" because it's the canonical shape and works on both OpenAI and Azure.
        if (!isResponses)
        {
            return new
            {
                model = s.LlmModel,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = text },
                },
                temperature = s.LlmTemperature,
                stream = false,
            };
        }

        // Responses API canonical shape.
        return new
        {
            model = s.LlmModel,
            input = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = text },
            },
            temperature = s.LlmTemperature,
        };
    }

    // ---------- Response parsing ----------

    private static string ExtractText(JsonElement root)
    {
        // Chat Completions: { choices: [ { message: { content: "..." } } ] }
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            if (choices[0].TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                return content.GetString() ?? string.Empty;
            }
        }

        // Responses API convenience: { output_text: "..." }
        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
            return outputText.GetString() ?? string.Empty;

        // Responses API canonical:
        //   { output: [ { type: "message", content: [ { type: "output_text", text: "..." } ] }, ... ] }
        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var itemContent) || itemContent.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var part in itemContent.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        sb.Append(t.GetString());
                }
            }
            if (sb.Length > 0) return sb.ToString();
        }

        return string.Empty;
    }

    private static bool IsResponsesApi(LlmProviderType kind) =>
        kind is LlmProviderType.OpenAIResponses or LlmProviderType.AzureResponses;

    private static string BuildSystemPrompt(string template, string source, string target)
    {
        return (template ?? string.Empty)
            .Replace("{source}", source)
            .Replace("{target}", target);
    }

    private static string CleanResponse(string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0) return text;

        foreach (var prefix in new[] { "Translation:", "Translated:", "Output:" })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(prefix.Length).TrimStart();
                break;
            }
        }

        if (text.Length >= 2)
        {
            char first = text[0];
            char last = text[^1];
            if ((first == '"' && last == '"') ||
                (first == '\'' && last == '\'') ||
                (first == '\u201C' && last == '\u201D') ||
                (first == '\u2018' && last == '\u2019'))
            {
                text = text.Substring(1, text.Length - 2).Trim();
            }
        }
        return text;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max) + "…";
}
