namespace DynamicIsland.Q.Core;

public sealed class QSessionController(IQProviderRegistry providers) : IQSessionController
{
    private readonly object _gate = new();
    private CancellationTokenSource? _activeCts;
    private long _generation;
    private long _actionSequence;
    private readonly List<QMessage> _history = [];
    private IAssistantControls? _controls;
    public void AttachControls(IAssistantControls controls) => _controls = controls;
    private QSessionSnapshot _snapshot = new(QRunState.Idle, QMode.Ask, string.Empty, string.Empty, "Ready", null, null, "", "");

    public QSessionSnapshot Snapshot { get { lock (_gate) return _snapshot; } }
    public event Action<QSessionSnapshot>? Changed;

    public Task BeginAsync(QMode mode, string providerId, string model, QScreenContext? context, CancellationToken cancellationToken = default)
    {
        Cancel();
        Publish(_snapshot with
        {
            State = QRunState.Ready,
            Mode = mode,
            Prompt = string.Empty,
            Response = string.Empty,
            Status = context is null ? "Ready for a question" : $"Reading {context.WindowTitle}",
            Error = null,
            Context = context,
            ProviderId = providerId,
            Model = model,
            Action = null
        });
        return Task.CompletedTask;
    }

    public async Task SubmitAsync(string prompt, QMode mode, string providerId, string model, string? credential, string? baseUrl,
        bool includeImage, Func<CancellationToken, Task<QScreenContext?>>? recapture = null,
        CancellationToken cancellationToken = default, int maxResponseTokens = 8192, string? customSystemPrompt = null,
        string reasoningEffort = "auto", QContextPolicy contextPolicy = QContextPolicy.Existing)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return;
        Cancel();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        long generation;
        lock (_gate) { _activeCts = linked; generation = ++_generation; }
        void Emit(QSessionSnapshot value)
        {
            lock (_gate) { if (generation != _generation) return; _snapshot = value; }
            Changed?.Invoke(value);
        }
        var token = linked.Token;
        AssistantActionFeedback StartAction(AssistantControlRequest? action)
        {
            var feedback = new AssistantActionFeedback(Interlocked.Increment(ref _actionSequence), action?.Command ?? "Island", action?.Argument ?? "", AssistantActionPhase.Working);
            Emit(Snapshot with { State = QRunState.Thinking, Prompt = prompt, Response = "", Error = null, Context = null, Status = "Using island controls…", Action = feedback });
            return feedback;
        }
        void FinishAction(AssistantActionFeedback feedback, AssistantControlResult result)
            => Emit(Snapshot with { Action = feedback with { Phase = result.Success ? AssistantActionPhase.Succeeded : AssistantActionPhase.Failed, Message = result.Message, Level = result.Level } });
        void Complete(string answer, QScreenContext? context)
        {
            lock (_gate)
            {
                if (generation != _generation) return;
                _history.Add(new QMessage("user", prompt)); _history.Add(new QMessage("assistant", answer));
                while (_history.Count > 8) _history.RemoveAt(0);
            }
            Emit(Snapshot with { State = QRunState.Complete, Prompt = prompt, Mode = mode, Context = context,
                ProviderId = providerId, Model = model, Response = answer, Status = "Complete", Error = null });
        }
        try
        {
            Emit(Snapshot with { Action = null });
            var controls = mode == QMode.Ask && contextPolicy == QContextPolicy.None ? _controls : null;
            if (controls is not null && controls.CanHandleDirect(prompt))
            {
                var feedback = StartAction(controls.DescribeDirect(prompt));
                token.ThrowIfCancellationRequested();
                var local = await controls.TryExecuteDirectAsync(prompt, token).ConfigureAwait(false);
                if (local is not null) { FinishAction(feedback, local); Complete(local.Message, null); return; }
            }
            var provider = providers.Find(providerId);
            if (provider is null) throw new InvalidOperationException($"Provider '{providerId}' is not available.");

            var context = contextPolicy == QContextPolicy.None ? null : Snapshot.Context;
            if (contextPolicy != QContextPolicy.None && recapture is not null)
            {
                Emit(Snapshot with { State = QRunState.Capturing, Prompt = prompt, Mode = mode, Error = null, Response = string.Empty, ProviderId = providerId, Model = model, Status = "Reading active window…", Action = null });
                context = await recapture(token).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
            Emit(Snapshot with { State = QRunState.Thinking, Prompt = prompt, Mode = mode, Context = context, Response = string.Empty, Error = null, ProviderId = providerId, Model = model, Status = "Thinking…", Action = null });
            QMessage[] history;
            lock (_gate) history = _history.ToArray();
            var request = new QRequest(mode, prompt, context, history, model,
                includeImage && context?.HasImage == true && provider.Info.Capabilities.HasFlag(QProviderCapabilities.Images),
                Math.Clamp(maxResponseTokens, 2048, 32768), customSystemPrompt, reasoningEffort, controls?.Instructions);
            var response = new System.Text.StringBuilder();
            await foreach (var item in provider.StreamAsync(request, credential, baseUrl, token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                switch (item)
                {
                    case QStreamEvent.Started:
                        Emit(Snapshot with { State = QRunState.Streaming, Status = "Sutra Assistant is responding…" });
                        break;
                    case QStreamEvent.Text text:
                        response.Append(text.Value);
                        if (controls is null || !AssistantControlProtocol.MustBuffer(response.ToString()))
                            Emit(Snapshot with { State = QRunState.Streaming, Response = response.ToString(), Status = "Sutra Assistant is responding…" });
                        break;
                    case QStreamEvent.Failed failed:
                        throw failed.Exception ?? new InvalidOperationException(failed.Message);
                    case QStreamEvent.Completed:
                        break;
                }
            }

            var answer = response.ToString().Trim();
            if (answer.Length == 0)
                throw new InvalidOperationException("The provider returned an empty response. Retry the question or switch models.");
            if (controls is not null && AssistantControlProtocol.MustBuffer(answer))
            {
                token.ThrowIfCancellationRequested();
                if (!AssistantControlProtocol.TryParse(answer, out var action))
                    throw new InvalidOperationException("The requested island action was not understood. Please restate it.");
                if (action.Command is not ("Status" or "GetSetting" or "ListOutputs") && !AssistantControlProtocol.AllowsMutation(prompt))
                { Complete("No island control was changed. Ask for the action directly when you want me to run it.", context); return; }
                var feedback = StartAction(action);
                var outcome = await controls.ExecuteAsync(action, token).ConfigureAwait(false);
                FinishAction(feedback, outcome);
                answer = outcome.Message;
            }
            Complete(answer, context);
        }
        catch (OperationCanceledException)
        {
            Emit(Snapshot with { State = QRunState.Cancelled, Status = "Cancelled", Action = Snapshot.Action is { Phase: AssistantActionPhase.Working } action ? action with { Phase = AssistantActionPhase.Cancelled, Message = "Action stopped." } : Snapshot.Action });
        }
        catch (Exception ex)
        {
            Emit(Snapshot with { State = QRunState.Error, Status = "Sutra Assistant could not respond", Error = ex.Message, Action = Snapshot.Action is { Phase: AssistantActionPhase.Working } action ? action with { Phase = AssistantActionPhase.Failed, Message = "Action could not be completed." } : Snapshot.Action });
        }
        finally
        {
            lock (_gate) if (ReferenceEquals(_activeCts, linked)) _activeCts = null;
            linked.Dispose();
        }
    }

    public void Cancel()
    {
        QSessionSnapshot? cancelled = null;
        lock (_gate)
        {
            ++_generation;
            if (_activeCts is { } active)
            {
                _activeCts = null;
                active.Cancel();
                _snapshot = cancelled = _snapshot with { State = QRunState.Cancelled, Status = "Cancelled", Action = _snapshot.Action is { Phase: AssistantActionPhase.Working } action ? action with { Phase = AssistantActionPhase.Cancelled, Message = "Action stopped." } : _snapshot.Action };
            }
        }
        if (cancelled is not null) Changed?.Invoke(cancelled);
    }

    public void Clear()
    {
        Cancel();
        lock (_gate) _history.Clear();
        Publish(new QSessionSnapshot(QRunState.Idle, QMode.Ask, string.Empty, string.Empty, "Ready", null, null, "", ""));
    }

    private void Publish(QSessionSnapshot snapshot)
    {
        lock (_gate) _snapshot = snapshot;
        Changed?.Invoke(snapshot);
    }

    public void Dispose() => Clear();
}
