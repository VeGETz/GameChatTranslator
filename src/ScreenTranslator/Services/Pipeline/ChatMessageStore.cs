using System.Text;

namespace ScreenTranslator.Services.Pipeline;

/// <summary>
/// One parsed chat line after OCR + regex + translation.
/// </summary>
public sealed class ChatEntry
{
    public string? Name { get; init; }
    public required string SourceText { get; init; }
    public required string TranslatedText { get; set; }
    public DateTime FirstSeenUtc { get; init; }
    public DateTime LastSeenUtc { get; set; }

    public string Key => (Name ?? string.Empty) + "\u0001" + SourceText;
}

/// <summary>
/// A small LRU-ish list of recent chat entries with TTL. Thread-safe.
/// Insertion order is preserved so chat reads top-to-bottom chronologically.
/// </summary>
public sealed class ChatMessageStore
{
    private readonly List<ChatEntry> _entries = new();
    private readonly object _gate = new();

    /// <summary>
    /// Merge this tick's parsed lines into the store. Existing entries have their LastSeen refreshed;
    /// new entries are appended. Returns true if the visible set changed (callers can use this to
    /// decide whether to republish).
    /// </summary>
    public void Upsert(IEnumerable<ChatEntry> parsedLines, DateTime nowUtc)
    {
        lock (_gate)
        {
            foreach (var parsed in parsedLines)
            {
                var existing = _entries.FirstOrDefault(e => e.Key == parsed.Key);
                if (existing is not null)
                {
                    existing.LastSeenUtc = nowUtc;
                    // Keep translation fresh if it changed (e.g. user switched translator/target).
                    if (!string.Equals(existing.TranslatedText, parsed.TranslatedText, StringComparison.Ordinal))
                        existing.TranslatedText = parsed.TranslatedText;
                }
                else
                {
                    _entries.Add(parsed);
                }
            }
        }
    }

    /// <summary>Remove entries older than TTL. Call this on every tick so the UI ages out correctly.</summary>
    public void Prune(TimeSpan ttl, int maxVisible, DateTime nowUtc)
    {
        lock (_gate)
        {
            _entries.RemoveAll(e => (nowUtc - e.LastSeenUtc) > ttl);

            // Trim to max count, dropping the oldest first.
            while (_entries.Count > maxVisible)
                _entries.RemoveAt(0);
        }
    }

    /// <summary>Render the current visible set as a newline-separated string for the overlay.</summary>
    public string Render()
    {
        lock (_gate)
        {
            if (_entries.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (i > 0) sb.Append('\n');
                if (!string.IsNullOrEmpty(e.Name))
                {
                    sb.Append('[').Append(e.Name).Append("]: ");
                }
                sb.Append(e.TranslatedText);
            }
            return sb.ToString();
        }
    }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }
}
