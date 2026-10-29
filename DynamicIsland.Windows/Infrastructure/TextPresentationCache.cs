namespace DynamicIsland.Windows.Infrastructure;

// Per-view-model, bounded reuse of pure text formatting. Streaming replaces old
// entries instead of retaining every intermediate response or a conversation log.
internal sealed class TextPresentationCache(Func<string, string> format, int capacity = 3, int maximumLength = 128 * 1024)
{
    private readonly (string? Input, string? Output)[] _entries = new (string?, string?)[Math.Max(1, capacity)];
    private int _next;

    public string Format(string input)
    {
        foreach (var entry in _entries)
            if (string.Equals(entry.Input, input, StringComparison.Ordinal)) return entry.Output!;
        var output = format(input);
        if (input.Length <= maximumLength && output.Length <= maximumLength)
        {
            _entries[_next] = (input, output);
            _next = (_next + 1) % _entries.Length;
        }
        return output;
    }

    public void Clear() { Array.Clear(_entries); _next = 0; }
}
