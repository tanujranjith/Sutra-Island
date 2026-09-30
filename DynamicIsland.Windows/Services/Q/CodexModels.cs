namespace DynamicIsland.Windows.Services.Q;

public sealed record CodexAccount(string? Email, string? PlanType, string AuthMode);

public static class CodexProviderLabel
{
    public static string For(CodexAccount? account) => account?.AuthMode switch
    {
        string mode when string.Equals(mode, "apiKey", StringComparison.OrdinalIgnoreCase) => "Codex · API key",
        string mode when string.Equals(mode, "chatgpt", StringComparison.OrdinalIgnoreCase) => "Codex · Subscription",
        null => "Codex · Sign in",
        _ => "Codex · Account"
    };
}
public sealed record CodexDeviceLogin(string LoginId, string VerificationUrl, string UserCode);
public sealed record CodexModel(
    string Id,
    string DisplayName,
    bool SupportsImages,
    bool IsDefault,
    IReadOnlyList<string> SupportedReasoningEfforts,
    string? DefaultReasoningEffort);
public sealed record CodexRateLimit(string? LimitId, int? UsedPercent, DateTimeOffset? ResetsAt);

public enum CodexAccountState { Checking, RuntimeUnavailable, SignedOut, LoginPending, Connected, LimitReached, Error }

public sealed record CodexAccountSnapshot(
    CodexAccountState State,
    CodexAccount? Account = null,
    CodexRateLimit? RateLimit = null,
    CodexRuntimeInfo? Runtime = null,
    IReadOnlyList<CodexModel>? Models = null,
    CodexDeviceLogin? PendingLogin = null,
    CodexFailureKind? FailureKind = null,
    string? Error = null)
{
    public bool IsConnected => State is CodexAccountState.Connected or CodexAccountState.LimitReached;
    public int? UsedPercent => RateLimit?.UsedPercent;
}
