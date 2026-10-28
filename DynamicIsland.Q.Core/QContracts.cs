namespace DynamicIsland.Q.Core;

public enum QMode { Ask, Say }

public enum QRunState
{
    Idle,
    Capturing,
    Ready,
    Listening,
    Thinking,
    Streaming,
    Complete,
    Cancelled,
    Error
}

[Flags]
public enum QProviderCapabilities
{
    None = 0,
    Text = 1,
    Images = 2,
    Streaming = 4,
    ModelDiscovery = 8
}

public enum QContextPolicy { Existing, None, Capture }

public enum QCaptureMode { ActiveWindow, ActiveMonitor }

public sealed record QModelInfo(
    string Id,
    string DisplayName,
    QProviderCapabilities Capabilities,
    bool IsDefault = false);

public sealed record QProviderInfo(
    string Id,
    string DisplayName,
    QProviderCapabilities Capabilities,
    string DefaultModel,
    string? DefaultBaseUrl = null);

public sealed record QScreenContext(
    string WindowTitle,
    string ProcessName,
    int Width,
    int Height,
    string OcrText,
    byte[]? PngBytes,
    DateTimeOffset CapturedAt)
{
    public bool HasImage => PngBytes is { Length: > 0 };
}

public sealed record QMessage(string Role, string Content);

public sealed record QRequest(
    QMode Mode,
    string Prompt,
    QScreenContext? ScreenContext,
    IReadOnlyList<QMessage> History,
    string Model,
    bool IncludeImage,
    int MaxResponseTokens = 8192,
    string? CustomSystemPrompt = null,
    string ReasoningEffort = "auto",
    string? ControlInstructions = null);

public abstract record QStreamEvent
{
    public sealed record Started : QStreamEvent;
    public sealed record Text(string Value) : QStreamEvent;
    public sealed record Completed : QStreamEvent;
    public sealed record Failed(string Message, Exception? Exception = null) : QStreamEvent;
}

public interface IQProvider
{
    QProviderInfo Info { get; }
    Task<IReadOnlyList<QModelInfo>> GetModelsAsync(string? credential, CancellationToken cancellationToken, string? baseUrl = null);
    IAsyncEnumerable<QStreamEvent> StreamAsync(
        QRequest request,
        string? credential,
        string? baseUrl,
        CancellationToken cancellationToken);
}

public interface IQProviderRegistry
{
    IReadOnlyList<IQProvider> Providers { get; }
    IQProvider? Find(string id);
}

public interface IQScreenContextService
{
    Task<QScreenContext?> CaptureAsync(nint targetWindow, QCaptureMode mode, CancellationToken cancellationToken);
}

public interface IQSpeechInputService
{
    bool IsAvailable { get; }
    Task<string?> DictateAsync(CancellationToken cancellationToken);
}

public sealed record QSessionSnapshot(
    QRunState State,
    QMode Mode,
    string Prompt,
    string Response,
    string Status,
    string? Error,
    QScreenContext? Context,
    string ProviderId,
    string Model,
    AssistantActionFeedback? Action = null);

public interface IQSessionController : IDisposable
{
    void AttachControls(IAssistantControls controls) { }
    QSessionSnapshot Snapshot { get; }
    event Action<QSessionSnapshot>? Changed;
    Task BeginAsync(QMode mode, string providerId, string model, QScreenContext? context, CancellationToken cancellationToken = default);
    Task SubmitAsync(string prompt, QMode mode, string providerId, string model, string? credential, string? baseUrl,
        bool includeImage, Func<CancellationToken, Task<QScreenContext?>>? recapture = null,
        CancellationToken cancellationToken = default, int maxResponseTokens = 8192, string? customSystemPrompt = null,
        string reasoningEffort = "auto", QContextPolicy contextPolicy = QContextPolicy.Existing);
    void Cancel();
    void Clear();
}
