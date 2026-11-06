namespace DynamicIsland.Windows.Infrastructure.CommandPalette;

public static class GoogleSearch
{
    public static Task LaunchAsync(Uri destination) => LaunchAsync(destination, uri =>
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });
    });

    internal static Task LaunchAsync(Uri destination, Action<Uri> launch)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                launch(destination);
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "Sutra browser handoff" };
        // URL handlers can use COM and DDE. Keep their apartment requirements and any slow
        // shell/browser startup away from WPF's dispatcher and its dismissal animation.
        if (OperatingSystem.IsWindows()) worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return completion.Task;
    }

    public static IReadOnlyList<GoogleSearchLanguageOption> LanguageOptions { get; } =
    [
        new("auto", "Automatic"), new("en", "English"), new("hi", "Hindi"),
        new("es", "Spanish"), new("fr", "French"), new("de", "German"),
        new("it", "Italian"), new("pt", "Portuguese"), new("ja", "Japanese"),
        new("ko", "Korean"), new("zh-CN", "Chinese (Simplified)"), new("ar", "Arabic")
    ];

    public static string NormalizeLanguage(string? language) =>
        LanguageOptions.FirstOrDefault(option => option.Code.Equals(language?.Trim(), StringComparison.OrdinalIgnoreCase))?.Code ?? "auto";

    public static Uri Destination(string? query, string? language = "auto", bool safeSearch = false)
    {
        var text = query?.Trim();
        var parameters = new List<string>();
        if (!string.IsNullOrEmpty(text)) parameters.Add("q=" + Uri.EscapeDataString(text));
        var normalizedLanguage = NormalizeLanguage(language);
        if (normalizedLanguage != "auto")
        {
            parameters.Add("hl=" + Uri.EscapeDataString(normalizedLanguage));
            parameters.Add("lr=lang_" + Uri.EscapeDataString(normalizedLanguage));
        }
        if (safeSearch) parameters.Add("safe=active");
        var destination = string.IsNullOrEmpty(text) ? "https://www.google.com/" : "https://www.google.com/search";
        return new Uri(destination + (parameters.Count == 0 ? "" : "?" + string.Join("&", parameters)));
    }
}

public sealed record GoogleSearchLanguageOption(string Code, string Name);
