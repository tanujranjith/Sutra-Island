namespace DynamicIsland.Q.Core;

public static class QPromptComposer
{
    public const string DefaultAskSystemPrompt = "You are Sutra Assistant in Ask mode. Answer, explain, solve, or analyze the user's request using the visible context when relevant. Be direct, distinguish visible facts from uncertainty, and do not invent details that are not present.";
    public const string DefaultSaySystemPrompt = "You are Sutra Assistant in Say mode. Use the visible context to suggest concise, natural first-person wording the user can say next. Do not claim actions were taken. Return only the suggested wording unless a brief clarification is essential.";

    public static string SystemPrompt(QMode mode) => mode switch
    {
        QMode.Say => DefaultSaySystemPrompt,
        _ => DefaultAskSystemPrompt,
    };

    public static string SystemPrompt(QRequest request) => SystemPrompt(request.Mode, request.CustomSystemPrompt)
        + (request.Mode == QMode.Ask && !string.IsNullOrEmpty(request.ControlInstructions) ? "\n\n" + request.ControlInstructions : "");

    public static string SystemPrompt(QMode mode, string? customSystemPrompt)
    {
        var baseline = SystemPrompt(mode);
        return string.IsNullOrWhiteSpace(customSystemPrompt)
            ? baseline
            : $"{baseline}\n\nAdditional instructions from the user:\n{customSystemPrompt.Trim()}";
    }

    public static string ContextText(QScreenContext? context)
    {
        if (context is null)
            return "No active-window context was captured.";

        var ocr = string.IsNullOrWhiteSpace(context.OcrText)
            ? "(No OCR text was detected.)"
            : context.OcrText.Trim();

        return $"Context from the active window ({context.ProcessName}, {context.WindowTitle}, {context.Width}x{context.Height}):\n{ocr}";
    }

    public static IReadOnlyList<QMessage> BuildMessages(QRequest request)
    {
        var messages = new List<QMessage>
        {
            new("system", SystemPrompt(request)),
        };

        messages.AddRange(request.History);
        messages.Add(new("user", $"{ContextText(request.ScreenContext)}\n\nUser request:\n{request.Prompt}"));
        return messages;
    }
}
