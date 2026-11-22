namespace AutomationPortal.Core;

/// <summary>
/// A token bucket per key (burst <c>capacity</c>, refilled at <c>perSecond</c>), for at most <c>maxKeys</c> keys. When full, the key
/// with the most tokens left goes, least recently used first among equals: forgetting it loses the least. So a flood of new keys
/// can't wipe the limit of a key that is being held back.
/// </summary>
internal sealed class RateBuckets
{
    private sealed class Entry
    {
        public required string Key;
        public double Tokens;
        public DateTimeOffset At;
    }

    private readonly int _capacity;
    private readonly double _perSecond;
    private readonly int _maxKeys;
    private readonly Dictionary<string, LinkedListNode<Entry>> _byKey = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _recent = new();   // most recently used first

    public RateBuckets(int capacity, double perSecond, int maxKeys)
    {
        _capacity = capacity;
        _perSecond = perSecond;
        _maxKeys = maxKeys;
    }

    public int Count { get { lock (_byKey) return _byKey.Count; } }

    public bool Contains(string key) { lock (_byKey) return _byKey.ContainsKey(key); }

    /// <summary>Takes one token from <paramref name="key"/>'s bucket; false when it is empty.</summary>
    public bool TryTake(string key, DateTimeOffset now)
    {
        lock (_byKey)
        {
            if (_byKey.TryGetValue(key, out var node)) _recent.Remove(node);
            else
            {
                if (_byKey.Count >= _maxKeys) Evict(now);
                node = new LinkedListNode<Entry>(new Entry { Key = key, Tokens = _capacity, At = now });
                _byKey[key] = node;
            }
            _recent.AddFirst(node);
            var e = node.Value;
            Refill(e, now);
            if (e.Tokens < 1) return false;
            e.Tokens -= 1;
            return true;
        }
    }

    private void Refill(Entry e, DateTimeOffset now)
    {
        var seconds = Math.Max(0, (now - e.At).TotalSeconds);
        e.Tokens = Math.Min(_capacity, e.Tokens + seconds * _perSecond);
        e.At = now;
    }

    // Goes through the keys from least to most recently used and drops the one with the most tokens left (the first such, so the least
    // recently used among equals). A key that is being held back has the fewest tokens and goes last.
    private void Evict(DateTimeOffset now)
    {
        LinkedListNode<Entry>? pick = null;
        var most = double.MinValue;
        for (var n = _recent.Last; n is not null; n = n.Previous)
        {
            var e = n.Value;
            var tokens = Math.Min(_capacity, e.Tokens + Math.Max(0, (now - e.At).TotalSeconds) * _perSecond);
            if (tokens > most) { most = tokens; pick = n; }
        }
        if (pick is not null) Remove(pick);
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        _recent.Remove(node);
        _byKey.Remove(node.Value.Key);
    }
}
