namespace ScreenTranslator.Services.Translation;

/// <summary>Tiny bounded LRU cache keyed by (text, source, target).</summary>
internal sealed class TranslationCache
{
    private readonly int _capacity;
    private readonly LinkedList<(string Key, string Value)> _order = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, string Value)>> _map = new();
    private readonly object _gate = new();

    public TranslationCache(int capacity = 256) => _capacity = capacity;

    private static string MakeKey(string text, string src, string tgt) => $"{src}|{tgt}|{text}";

    public bool TryGet(string text, string src, string tgt, out string value)
    {
        lock (_gate)
        {
            var key = MakeKey(text, src, tgt);
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = string.Empty;
            return false;
        }
    }

    public void Set(string text, string src, string tgt, string value)
    {
        lock (_gate)
        {
            var key = MakeKey(text, src, tgt);
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _map.Remove(key);
            }
            var node = new LinkedListNode<(string, string)>((key, value));
            _order.AddFirst(node);
            _map[key] = node;
            while (_map.Count > _capacity)
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }
    }
}
