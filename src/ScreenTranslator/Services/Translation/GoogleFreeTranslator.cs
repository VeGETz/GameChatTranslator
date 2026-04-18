using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ScreenTranslator.Services.Translation;

/// <summary>
/// Uses the unofficial translate.googleapis.com/translate_a/single endpoint.
/// No API key, but technically against Google's ToS and may be rate-limited or blocked at any time.
/// </summary>
public sealed class GoogleFreeTranslator : ITranslator
{
    private readonly HttpClient _http;
    private readonly TranslationCache _cache = new();

    public GoogleFreeTranslator(HttpClient http)
    {
        _http = http;
    }

    public string DisplayName => "Google Translate (unofficial, free)";

    public async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        if (_cache.TryGet(text, sourceLanguage, targetLanguage, out var cached))
            return cached;

        var url =
            $"https://translate.googleapis.com/translate_a/single?client=gtx" +
            $"&sl={Uri.EscapeDataString(sourceLanguage)}" +
            $"&tl={Uri.EscapeDataString(targetLanguage)}" +
            $"&dt=t&q={Uri.EscapeDataString(text)}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        // Lightweight UA helps avoid some bot blocks.
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 ScreenTranslator/1.0");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        var result = ParseResponse(body);
        _cache.Set(text, sourceLanguage, targetLanguage, result);
        return result;
    }

    /// <summary>
    /// Response shape is a nested JSON array:
    /// [[["translated","original",null,null,n],[...],...], null, "en", ...]
    /// We concatenate the first element of every sentence tuple.
    /// </summary>
    private static string ParseResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return string.Empty;
            var sentences = root[0];
            if (sentences.ValueKind != JsonValueKind.Array)
                return string.Empty;
            var sb = new StringBuilder();
            foreach (var sentence in sentences.EnumerateArray())
            {
                if (sentence.ValueKind != JsonValueKind.Array || sentence.GetArrayLength() == 0)
                    continue;
                var translated = sentence[0];
                if (translated.ValueKind == JsonValueKind.String)
                    sb.Append(translated.GetString());
            }
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }
}
