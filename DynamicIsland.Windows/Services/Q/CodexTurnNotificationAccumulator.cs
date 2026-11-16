using System.Text;
using System.Text.Json;

namespace DynamicIsland.Windows.Services.Q;

internal enum CodexTurnUpdateKind { Text, Completed, Failed, Searching, SearchCompleted }

internal sealed record CodexTurnUpdate(CodexTurnUpdateKind Kind, string? Value = null);

internal sealed class CodexTurnNotificationAccumulator(string threadId)
{
    private readonly Dictionary<string, StringBuilder> _streamedByItem = new(StringComparer.Ordinal);
    private string? _lastError;

    public CodexTurnUpdate? Process(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object) return null;
        var method = String(message, "method");
        var parameters = message.TryGetProperty("params", out var value) ? value : default;
        var messageThreadId = String(parameters, "threadId");
        if (messageThreadId is not null && !string.Equals(messageThreadId, threadId, StringComparison.Ordinal)) return null;

        if (method is "item/started" or "item/completed" && parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("item", out var searchItem) && String(searchItem, "type") == "webSearch")
            return new CodexTurnUpdate(method == "item/started" ? CodexTurnUpdateKind.Searching : CodexTurnUpdateKind.SearchCompleted);

        if (method == "item/agentMessage/delta")
        {
            var delta = String(parameters, "delta") ?? String(parameters, "text");
            if (string.IsNullOrEmpty(delta)) return null;
            var itemId = String(parameters, "itemId") ?? "__agent";
            if (!_streamedByItem.TryGetValue(itemId, out var streamed))
                _streamedByItem[itemId] = streamed = new StringBuilder();
            streamed.Append(delta);
            return new CodexTurnUpdate(CodexTurnUpdateKind.Text, delta);
        }

        if (method == "item/completed" && parameters.TryGetProperty("item", out var item) &&
            string.Equals(String(item, "type"), "agentMessage", StringComparison.Ordinal))
        {
            var text = String(item, "text");
            if (string.IsNullOrEmpty(text)) return null;
            var itemId = String(item, "id") ?? "__agent";
            if (!_streamedByItem.TryGetValue(itemId, out var streamed) || streamed.Length == 0)
                return new CodexTurnUpdate(CodexTurnUpdateKind.Text, text);
            var streamedText = streamed.ToString();
            return text.StartsWith(streamedText, StringComparison.Ordinal) && text.Length > streamedText.Length
                ? new CodexTurnUpdate(CodexTurnUpdateKind.Text, text[streamedText.Length..])
                : null;
        }

        if (method == "error")
        {
            var error = parameters.TryGetProperty("error", out var errorValue) ? errorValue : parameters;
            _lastError = String(error, "message") ?? String(parameters, "message") ?? "Codex encountered an unknown error.";
            return new CodexTurnUpdate(CodexTurnUpdateKind.Failed, _lastError);
        }

        if (method != "turn/completed") return null;
        var turn = parameters.TryGetProperty("turn", out var turnValue) ? turnValue : default;
        var status = String(turn, "status") ?? "completed";
        if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            var error = turn.TryGetProperty("error", out var errorValue) ? errorValue : default;
            var messageText = String(error, "message") ?? _lastError ?? "Codex could not complete the request.";
            return new CodexTurnUpdate(CodexTurnUpdateKind.Failed, messageText);
        }
        if (string.Equals(status, "interrupted", StringComparison.OrdinalIgnoreCase))
            return new CodexTurnUpdate(CodexTurnUpdateKind.Failed, "The Codex request was interrupted.");
        return new CodexTurnUpdate(CodexTurnUpdateKind.Completed);
    }

    private static string? String(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : null;
}
