using DynamicIsland.Windows.Infrastructure.CommandPalette;
using DynamicIsland.Windows.Services;

var launcher = new WindowsLauncher();
var apps = await launcher.GetAppsAsync();
Check(apps.Length > 0, $"Discovered {apps.Length} installed Windows apps");
foreach (var name in new[] { "Notepad", "Calculator", "File Explorer" })
{
    var matches = WindowsLaunchCatalog.FindApps(apps, name);
    Check(matches.Length == 1, $"Resolved {name} to one installed app");
    Console.WriteLine($"  {matches[0].Name}: {matches[0].Id}");
}
var missing = await launcher.OpenAppAsync("IslandNonexistentAppSmokeCheck019283");
Check(!missing.Success, "Missing apps return a failure receipt");
var invalid = await launcher.OpenAppAsync(@"C:\Windows\System32\cmd.exe /c anything");
Check(!invalid.Success, "Executable paths and arguments cannot be launched");
Check(!(await launcher.OpenSettingsAsync("unknown-page")).Success, "Unknown Settings pages return a failure receipt");
Check((await launcher.ListAppsAsync("Notepad")).Success, "Installed app listing can be filtered");

// Opt in to opening real desktop windows. No Windows settings are modified.
if (args.Contains("--launch"))
{
    foreach (var name in new[] { "Notepad", "Calculator" })
    {
        var result = await launcher.OpenAppAsync(name);
        Check(result.Success, result.Message);
    }
    var settings = await launcher.OpenSettingsAsync("display");
    Check(settings.Success, settings.Message);
}
if (args.Contains("--launch-desktop"))
{
    var desktop = await launcher.OpenAppAsync("File Explorer");
    Check(desktop.Success, desktop.Message);
}

static void Check(bool success, string message)
{
    Console.WriteLine($"{(success ? "PASS" : "FAIL")} {message}");
    if (!success) throw new InvalidOperationException(message);
}
