namespace DynamicIsland.Windows.Services.Q;

internal static class CodexTurnStartRequest
{
    public static Dictionary<string, object?> CreateThread(string model, string workspace, bool webSearch = false) => new()
    {
        ["model"] = model,
        ["cwd"] = workspace,
        ["approvalPolicy"] = "never",
        ["sandbox"] = "read-only",
        ["serviceName"] = "sutra_island_q",
        ["config"] = new Dictionary<string, object?> { ["web_search"] = webSearch ? "live" : "disabled" }
    };

    public static Dictionary<string, object?> Create(string threadId, IReadOnlyList<object> input,
        string model, string? reasoningEffort)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["threadId"] = threadId,
            ["input"] = input,
            ["model"] = model
        };

        var effort = reasoningEffort?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(effort) && effort != "auto")
            parameters["effort"] = effort;

        return parameters;
    }
}
