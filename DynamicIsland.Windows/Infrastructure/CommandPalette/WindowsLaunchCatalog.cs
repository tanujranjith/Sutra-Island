using System.Text.RegularExpressions;

namespace DynamicIsland.Windows.Infrastructure.CommandPalette;

public sealed record WindowsAppEntry(string Name, string Id);
public sealed record WindowsSettingsPage(string Name, string Uri, params string[] Aliases);
public sealed record WindowsLaunchResult(bool Success, string Message);

public interface IWindowsLauncher
{
    Task<WindowsLaunchResult> OpenAppAsync(string query);
    Task<WindowsLaunchResult> OpenSettingsAsync(string query);
    Task<WindowsLaunchResult> ListAppsAsync(string query);
}

/// <summary>Only catalogued apps and Settings destinations can be launched. User text is never executable code.</summary>
public static class WindowsLaunchCatalog
{
    public static readonly WindowsSettingsPage[] SettingsPages =
    [
        new("Windows Settings", "ms-settings:", "windows", "settings", "windows settings", "home"),
        new("Display", "ms-settings:display", "screen", "monitor", "resolution", "brightness"),
        new("Sound", "ms-settings:sound", "audio", "volume", "speaker", "speakers"),
        new("Volume mixer", "ms-settings:apps-volume", "app volume"),
        new("Bluetooth", "ms-settings:bluetooth", "bluetooth devices"),
        new("Wi-Fi", "ms-settings:network-wifi", "wifi", "wireless"),
        new("Network & internet", "ms-settings:network", "network", "internet"),
        new("Ethernet", "ms-settings:network-ethernet"),
        new("VPN", "ms-settings:network-vpn"),
        new("Proxy", "ms-settings:network-proxy"),
        new("Mobile hotspot", "ms-settings:network-mobilehotspot", "hotspot"),
        new("Airplane mode", "ms-settings:network-airplanemode"),
        new("Windows Update", "ms-settings:windowsupdate", "update", "updates", "windows updates"),
        new("Notifications", "ms-settings:notifications", "notification"),
        new("Power & battery", "ms-settings:powersleep", "power", "battery", "sleep", "power and battery"),
        new("Storage", "ms-settings:storagesense", "disk space"),
        new("About", "ms-settings:about", "system information", "system about", "device specifications"),
        new("System", "ms-settings:system"),
        new("Multitasking", "ms-settings:multitasking", "snap windows"),
        new("Remote Desktop", "ms-settings:remotedesktop"),
        new("Clipboard", "ms-settings:clipboard"),
        new("Personalization", "ms-settings:personalization", "personalisation"),
        new("Background", "ms-settings:personalization-background", "wallpaper", "desktop background"),
        new("Colors", "ms-settings:colors", "colours", "accent color", "dark mode"),
        new("Themes", "ms-settings:themes", "theme"),
        new("Lock screen", "ms-settings:lockscreen"),
        new("Taskbar", "ms-settings:taskbar"),
        new("Start", "ms-settings:personalization-start", "start menu"),
        new("Fonts", "ms-settings:fonts", "font"),
        new("Installed apps", "ms-settings:appsfeatures", "apps", "apps and features", "applications"),
        new("Default apps", "ms-settings:defaultapps", "defaults", "default applications"),
        new("Startup apps", "ms-settings:startupapps", "startup"),
        new("Accounts", "ms-settings:accounts", "account"),
        new("Your info", "ms-settings:yourinfo", "profile"),
        new("Sign-in options", "ms-settings:signinoptions", "sign in", "password", "windows hello"),
        new("Date & time", "ms-settings:dateandtime", "date", "time", "clock", "date and time"),
        new("Language & region", "ms-settings:regionlanguage", "language", "region", "language and region"),
        new("Mouse", "ms-settings:mousetouchpad"),
        new("Touchpad", "ms-settings:devices-touchpad"),
        new("Printers & scanners", "ms-settings:printers", "printer", "printers", "scanner", "printers and scanners"),
        new("Typing", "ms-settings:typing", "keyboard"),
        new("Accessibility", "ms-settings:easeofaccess", "ease of access"),
        new("Text size", "ms-settings:easeofaccess-display"),
        new("Magnifier", "ms-settings:easeofaccess-magnifier"),
        new("Narrator", "ms-settings:easeofaccess-narrator"),
        new("Privacy", "ms-settings:privacy", "privacy and security", "privacy & security"),
        new("Microphone privacy", "ms-settings:privacy-microphone", "microphone", "mic", "microphone permissions"),
        new("Camera privacy", "ms-settings:privacy-webcam", "camera", "webcam", "camera permissions"),
        new("Location privacy", "ms-settings:privacy-location", "location", "location permissions"),
        new("Windows Security", "ms-settings:windowsdefender", "security", "defender"),
        new("Recovery", "ms-settings:recovery"),
        new("Troubleshoot", "ms-settings:troubleshoot", "troubleshooting"),
        new("Activation", "ms-settings:activation"),
        new("Gaming", "ms-settings:gaming-gamebar", "game bar", "xbox game bar"),
        new("Game Mode", "ms-settings:gaming-gamemode")
    ];

    public static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]", "");

    public static bool IsAppName(string value) => value.Length is > 0 and <= 200
        && value.Any(char.IsLetterOrDigit)
        && !value.Any(c => char.IsControl(c) || "\\/:;|<>`$\"".Contains(c));

    public static bool TrySettingsRequest(string input, out string query)
    {
        query = "";
        var match = Regex.Match(input, @"^(?:open|show|launch|start|go to)\s+(?:(?:the|my)\s+)?(?<target>.+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        var target = match.Groups["target"].Value.Trim();
        if (Regex.IsMatch(target, @"\b(?:island|sutra)\b", RegexOptions.IgnoreCase)) return false;
        if (target.Equals("settings", StringComparison.OrdinalIgnoreCase)) return true;
        if (target.StartsWith("settings ", StringComparison.OrdinalIgnoreCase))
        { query = Regex.Replace(target[9..], @"^(?:for|to)\s+", "", RegexOptions.IgnoreCase); return true; }
        var prefix = Regex.Match(target, @"^(?:windows|system)\s+settings(?:\s+(?:for\s+)?(.*))?$", RegexOptions.IgnoreCase);
        if (prefix.Success) { query = prefix.Groups[1].Value; return true; }
        var suffix = Regex.Match(target, @"^(.+?)\s+(?:(?:windows|system)\s+)?settings$", RegexOptions.IgnoreCase);
        if (suffix.Success) { query = suffix.Groups[1].Value; return true; }
        // Keep the existing Island panel shortcuts; explicit "... settings" still selects Windows.
        if (Normalize(target) is "clipboard" or "notifications" or "notification" or "audiooutput" or "soundoutput") return false;
        if (FindSettings(target) is not null) { query = target; return true; }
        return false;
    }

    public static string AppQuery(string argument) => Regex.Replace(
        Regex.Replace(argument.Trim(), @"^(?:the|my)\s+", "", RegexOptions.IgnoreCase),
        @"\s+(?:app|application)$", "", RegexOptions.IgnoreCase).Trim();

    public static bool IsSingleAppRequest(string query) => IsAppName(query)
        && !Regex.IsMatch(query, @"\b(?:and|then)\s+(?:open|launch|start|show|mute|play|set|delete|close|turn)\b", RegexOptions.IgnoreCase);

    public static WindowsSettingsPage? FindSettings(string query)
    {
        var value = query.Trim();
        if (value.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
            return SettingsPages.FirstOrDefault(page => page.Uri.Equals(value, StringComparison.OrdinalIgnoreCase));
        value = Regex.Replace(value, @"^(?:windows\s+)?settings(?:\s+(?:for|to))?\s+|\s+(?:windows\s+)?settings$", "", RegexOptions.IgnoreCase);
        var key = Normalize(value);
        if (key.Length == 0) return SettingsPages[0];
        return SettingsPages.FirstOrDefault(page => Normalize(page.Name) == key || page.Aliases.Any(alias => Normalize(alias) == key));
    }

    public static WindowsAppEntry[] FindApps(IEnumerable<WindowsAppEntry> apps, string query)
    {
        if (!IsAppName(query)) return [];
        var key = Normalize(query);
        var all = apps.DistinctBy(app => app.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        var exact = all.Where(app => Normalize(app.Name) == key).ToArray();
        if (exact.Length > 0) return exact;
        var alias = key switch
        {
            "chrome" => "googlechrome", "edge" => "microsoftedge",
            "vscode" or "vsc" => "visualstudiocode", "word" => "microsoftword",
            "excel" => "microsoftexcel", "powerpoint" => "microsoftpowerpoint",
            "store" or "windowsstore" => "microsoftstore", "explorer" or "windowsexplorer" => "fileexplorer",
            "calc" => "calculator", "windowscalculator" => "calculator", _ => key
        };
        exact = all.Where(app => Normalize(app.Name) == alias).ToArray();
        if (exact.Length > 0) return exact;
        return key.Length < 2 ? [] : all.Where(app => Normalize(app.Name).Contains(alias, StringComparison.Ordinal)).OrderBy(app => app.Name).ToArray();
    }
}
