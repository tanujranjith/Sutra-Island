using Microsoft.Win32;

namespace DynamicIsland.Windows.Services;

public sealed class StartupService(LoggingService log)
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Sutra Island";
    private const string LegacyValueName = "DynamicIsland.Windows";

    public bool IsEnabled()
    {
        if (Infrastructure.AppDataPaths.IsPreview) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return (key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value))
                || (key?.GetValue(LegacyValueName) is string legacyValue && !string.IsNullOrWhiteSpace(legacyValue));
        }
        catch (Exception ex)
        {
            log.Error("Unable to read startup registration", ex);
            return false;
        }
    }

    public bool SetEnabled(bool enabled)
    {
        if (Infrastructure.AppDataPaths.IsPreview) return false;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (enabled)
            {
                var executable = Environment.ProcessPath
                    ?? throw new InvalidOperationException("Executable path is unavailable.");
                key.SetValue(ValueName, $"\"{executable}\" --startup", RegistryValueKind.String);
                key.DeleteValue(LegacyValueName, false);
            }
            else
            {
                key.DeleteValue(ValueName, false);
                key.DeleteValue(LegacyValueName, false);
            }
            return true;
        }
        catch (Exception ex)
        {
            log.Error("Unable to update startup registration", ex);
            return false;
        }
    }
}
