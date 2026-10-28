using System.Text.Json;
using System.Text.RegularExpressions;
namespace DynamicIsland.Q.Core;

public sealed record AssistantControlRequest(string Command, string Argument = "", string? Value = null);
public sealed record AssistantControlResult(bool Success, string Message, double? Level = null);
public enum AssistantActionPhase { Working, Succeeded, Failed, Cancelled }
public sealed record AssistantActionFeedback(long Sequence, string Command, string Argument, AssistantActionPhase Phase, string? Message = null, double? Level = null);
public interface IAssistantControls
{
    string Instructions { get; }
    bool CanHandleDirect(string prompt);
    AssistantControlRequest? DescribeDirect(string prompt) => null;
    Task<AssistantControlResult?> TryExecuteDirectAsync(string prompt, CancellationToken token);
    Task<AssistantControlResult> ExecuteAsync(AssistantControlRequest request, CancellationToken token);
}
public static class AssistantControlProtocol
{
    public const string Open = "<sutra-action>";
    public const string Close = "</sutra-action>";
    public static bool AllowsMutation(string prompt)
    {
        var text=prompt.TrimStart().Replace('\u2019','\'');
        return !Regex.IsMatch(text,@"^(?:[""']|how\b|why\b|what\b|explain\b|describe\b|tell me\b|don't\b|do not\b|never\b|if\b|imagine\b|suppose\b|repeat\b|print\b|quote\b|write\b|say\b|(?:can|could|would)\s+(?:you|u)\s+(?:explain|describe|tell|repeat|print|write)\b)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    }
    public static bool MustBuffer(string text)
    { var start=text.TrimStart(); return Open.StartsWith(start,StringComparison.Ordinal) || start.StartsWith(Open,StringComparison.Ordinal); }
    public static bool TryParse(string text,out AssistantControlRequest request)
    {
        request=null!; var value=text.Trim();
        if(value.Length>4096 || !value.StartsWith(Open,StringComparison.Ordinal) || !value.EndsWith(Close,StringComparison.Ordinal)) return false;
        try
        {
            using var json=JsonDocument.Parse(value[Open.Length..^Close.Length]); var root=json.RootElement;
            if(root.ValueKind!=JsonValueKind.Object || !root.TryGetProperty("command",out var command) || command.ValueKind!=JsonValueKind.String) return false;
            var name=command.GetString(); if(string.IsNullOrWhiteSpace(name)) return false;
            foreach(var key in new[]{"argument","value"})
                if(root.TryGetProperty(key,out var item) && item.ValueKind is JsonValueKind.Object or JsonValueKind.Array) return false;
            string Read(string key) => !root.TryGetProperty(key,out var item) || item.ValueKind==JsonValueKind.Null ? "" : item.ValueKind==JsonValueKind.String ? item.GetString()! : item.GetRawText();
            request=new(name,Read("argument"),Read("value")); return true;
        }
        catch(JsonException) { return false; }
    }
}
