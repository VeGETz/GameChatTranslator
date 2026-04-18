using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using ScreenTranslator.Services.Capture;
using ScreenTranslator.Services.Ocr;
using ScreenTranslator.Services.Settings;
using ScreenTranslator.Services.Translation;

namespace ScreenTranslator.Services.Pipeline;

/// <summary>
/// The capture → OCR → per-line parse → translate → sticky-store → publish pipeline.
/// Runs on a dedicated background task. Skips unchanged frames via a pixel hash,
/// skips translation on unchanged OCR lines, and keeps messages visible for a TTL after they
/// stop being seen so fast-scrolling chats are readable.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class TranslationLoop : IDisposable
{
    private readonly IScreenCapturer _capturer;
    private readonly IOcrService _ocr;
    private readonly TranslatorFactory _translatorFactory;
    private readonly SettingsStore _settings;
    private readonly ChatMessageStore _store = new();

    private CancellationTokenSource? _cts;
    private Task? _runner;

    private ulong _lastFrameHash;
    private string _lastOcrText = string.Empty;

    // Compiled regex cache (recompile only when the pattern changes).
    private string? _cachedPattern;
    private Regex? _cachedRegex;

    /// <summary>Raised on every tick (on a background thread). Subscribers must marshal to UI thread themselves.</summary>
    public event Action<TickResult>? Ticked;

    /// <summary>Function that returns the current target screen rectangle (in desktop pixels).</summary>
    public Func<Rectangle?>? GetCaptureRect { get; set; }

    public TranslationLoop(
        IScreenCapturer capturer,
        IOcrService ocr,
        TranslatorFactory translatorFactory,
        SettingsStore settings)
    {
        _capturer = capturer;
        _ocr = ocr;
        _translatorFactory = translatorFactory;
        _settings = settings;
    }

    public bool IsRunning => _runner is not null && !_runner.IsCompleted;

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _runner = Task.Run(() => RunAsync(token), token);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); }
        catch { /* already disposed */ }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var interval = Math.Max(200, _settings.Current.IntervalMilliseconds);
            var tickStarted = Stopwatch.GetTimestamp();
            try
            {
                await TickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Ticked?.Invoke(new TickResult(_lastOcrText, _store.Render(), 0, 0, 0, false, ex.Message));
            }

            var elapsedMs = (int)Stopwatch.GetElapsedTime(tickStarted).TotalMilliseconds;
            var delay = interval - elapsedMs;
            if (delay > 0)
            {
                try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var rect = GetCaptureRect?.Invoke();
        if (rect is null || rect.Value.Width <= 0 || rect.Value.Height <= 0)
            return;

        var captureSw = Stopwatch.StartNew();
        var bitmap = _capturer.Capture(rect.Value);
        captureSw.Stop();

        var settings = _settings.Current;
        var ttl = TimeSpan.FromSeconds(Math.Max(1, settings.MessageTtlSeconds));
        var nowUtc = DateTime.UtcNow;

        // Prune expired entries on every tick so the overlay ages out even when the frame is static.
        _store.Prune(ttl, Math.Max(1, settings.MaxVisibleMessages), nowUtc);

        // Fast hash of the pixel buffer — skip OCR+translate if frame unchanged.
        var hash = HashBitmap(bitmap);
        if (hash == _lastFrameHash)
        {
            Publish(captureSw.ElapsedMilliseconds, 0, 0, skipped: true, null);
            return;
        }
        _lastFrameHash = hash;

        var ocrSw = Stopwatch.StartNew();
        var ocrLang = settings.OcrLanguageTag ?? "en-US";
        var ocrText = (await _ocr.RecognizeAsync(bitmap, ocrLang, ct).ConfigureAwait(false)).Trim();
        ocrSw.Stop();
        _lastOcrText = ocrText;

        if (string.IsNullOrWhiteSpace(ocrText))
        {
            // Nothing recognized this tick, but existing sticky entries stay until TTL.
            Publish(captureSw.ElapsedMilliseconds, ocrSw.ElapsedMilliseconds, 0, skipped: false, null);
            return;
        }

        var lines = SplitLines(ocrText);
        var regex = GetRegex(settings);
        var translator = _translatorFactory.Current;
        var source = string.IsNullOrWhiteSpace(settings.OcrLanguageTag) ? "auto" : TwoLetter(settings.OcrLanguageTag!);
        var target = settings.TargetLanguage;

        var translateSw = Stopwatch.StartNew();
        var parsed = new List<ChatEntry>(lines.Count);
        foreach (var line in lines)
        {
            string? name = null;
            string payload = line;
            bool matched = false;

            if (regex is not null)
            {
                var m = regex.Match(line);
                if (m.Success)
                {
                    matched = true;
                    if (settings.SkipPlayerNames)
                    {
                        var nameGroup = m.Groups["name"];
                        var textGroup = m.Groups["text"];
                        if (nameGroup.Success) name = nameGroup.Value.Trim();
                        if (textGroup.Success) payload = textGroup.Value.Trim();
                    }
                }
            }

            // Line-filter mode: drop anything that didn't match the regex.
            // If the pattern was invalid or empty, GetRegex returns null and we treat every line as a match.
            if (settings.OnlyTranslateMatchingLines && regex is not null && !matched)
                continue;

            if (string.IsNullOrWhiteSpace(payload))
                continue;

            string translated;
            try
            {
                translated = await translator.TranslateAsync(payload, source, target, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Surface the error but keep showing whatever else is already in the store.
                Publish(captureSw.ElapsedMilliseconds, ocrSw.ElapsedMilliseconds, translateSw.ElapsedMilliseconds, skipped: false, ex.Message);
                return;
            }

            parsed.Add(new ChatEntry
            {
                Name = name,
                SourceText = payload,
                TranslatedText = translated,
                FirstSeenUtc = nowUtc,
                LastSeenUtc = nowUtc,
            });
        }
        translateSw.Stop();

        _store.Upsert(parsed, nowUtc);
        _store.Prune(ttl, Math.Max(1, settings.MaxVisibleMessages), nowUtc);

        Publish(captureSw.ElapsedMilliseconds, ocrSw.ElapsedMilliseconds, translateSw.ElapsedMilliseconds, skipped: false, null);
    }

    private void Publish(long captureMs, long ocrMs, long translateMs, bool skipped, string? error)
    {
        Ticked?.Invoke(new TickResult(
            _lastOcrText,
            _store.Render(),
            captureMs,
            ocrMs,
            translateMs,
            skipped,
            error));
    }

    private static List<string> SplitLines(string ocrText)
    {
        var list = new List<string>();
        foreach (var raw in ocrText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length > 0)
                list.Add(trimmed);
        }
        return list;
    }

    private Regex? GetRegex(AppSettings settings)
    {
        var pattern = settings.ChatLineRegex;
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        if (_cachedPattern == pattern && _cachedRegex is not null) return _cachedRegex;
        try
        {
            _cachedRegex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
            _cachedPattern = pattern;
            return _cachedRegex;
        }
        catch
        {
            _cachedRegex = null;
            _cachedPattern = pattern; // don't retry the broken pattern every tick
            return null;
        }
    }

    private static string TwoLetter(string bcp47)
    {
        var idx = bcp47.IndexOf('-');
        return idx > 0 ? bcp47.Substring(0, idx) : bcp47;
    }

    private static ulong HashBitmap(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var length = Math.Abs(data.Stride) * data.Height;
            unsafe
            {
                var span = new ReadOnlySpan<byte>((void*)data.Scan0, length);
                return XxHash64.HashToUInt64(span);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
