using System.Diagnostics;
using System.Runtime.InteropServices;
using DynamicIsland.Windows.Infrastructure.CommandPalette;

namespace DynamicIsland.Windows.Services;

/// <summary>Discovers desktop and Store apps through the same shell folder Windows uses.
/// Shell COM work stays on a background STA, away from the WPF dispatcher.</summary>
public sealed class WindowsLauncher : IWindowsLauncher
{
    private const string AppsFolder = "shell:AppsFolder";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WindowsAppEntry[] _apps = [];
    private DateTime _refreshed;

    public async Task<WindowsLaunchResult> OpenAppAsync(string query)
    {
        query = query.Trim();
        if (!WindowsLaunchCatalog.IsAppName(query))
            return new(false, "Use an installed app name, for example: open Notepad. Paths, arguments and shell commands are not supported.");
        try
        {
            var apps = await GetAppsAsync();
            var matches = WindowsLaunchCatalog.FindApps(apps, query);
            // Refresh on a miss so a newly installed app is immediately available.
            if (matches.Length == 0) matches = WindowsLaunchCatalog.FindApps(await GetAppsAsync(refresh: true), query);
            if (matches.Length == 0) return new(false, $"No installed app matches '{query}'. Type apps to see available names.");
            if (matches.Length > 1) return new(false, $"More than one app matches: {string.Join(", ", matches.Take(8).Select(app => app.Name))}. Use the full app name.");
            var app = matches[0];
            await RunStaAsync(() =>
            {
                if (app.Id == "@file-explorer")
                {
                    Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true });
                    return true;
                }
                object? shell = null, folder = null, item = null;
                try
                {
                    shell = CreateShell();
                    folder = ((dynamic)shell).NameSpace(AppsFolder);
                    item = ((dynamic)folder).ParseName(app.Id);
                    if (item is null) throw new InvalidOperationException("That app is no longer installed. Refresh the app list and try again.");
                    ((dynamic)item).InvokeVerb("open");
                    return true;
                }
                finally { Release(item); Release(folder); Release(shell); }
            });
            return new(true, $"Asked Windows to open {app.Name}.");
        }
        catch (Exception) { return new(false, "Windows couldn't launch that app. Check that it is installed and available in the Start menu, then try again."); }
    }

    public async Task<WindowsLaunchResult> OpenSettingsAsync(string query)
    {
        var page = WindowsLaunchCatalog.FindSettings(query);
        if (page is null) return new(false, "Unknown Windows Settings page. Try windows settings display, Bluetooth, Wi-Fi, sound, apps, privacy, or Windows Update.");
        try
        {
            await RunStaAsync(() =>
            {
                Process.Start(new ProcessStartInfo(page.Uri) { UseShellExecute = true });
                return true;
            });
            return new(true, $"Asked Windows to open {page.Name}{(page.Uri == "ms-settings:" ? "" : " settings")}.");
        }
        catch (Exception) { return new(false, "Windows couldn't open Settings. This page may be unavailable on this device or managed by your organization."); }
    }

    public async Task<WindowsLaunchResult> ListAppsAsync(string query)
    {
        if (query.Length > 0 && !WindowsLaunchCatalog.IsAppName(query)) return new(false, "Filter apps by name, for example: apps Microsoft.");
        try
        {
            var apps = await GetAppsAsync(refresh: true);
            var matches = string.IsNullOrWhiteSpace(query) ? apps : WindowsLaunchCatalog.FindApps(apps, query);
            if (matches.Length == 0) return new(false, "No matching installed apps were found.");
            return new(true, $"Installed apps: {string.Join(", ", matches.Take(35).Select(app => app.Name))}."
                + (matches.Length > 35 ? $" Showing 35 of {matches.Length}; filter with apps <name>." : " Use open <app name> to launch one."));
        }
        catch (Exception) { return new(false, "Windows couldn't read the installed app list. Please try again."); }
    }

    public async Task<WindowsAppEntry[]> GetAppsAsync(bool refresh = false)
    {
        await _gate.WaitAsync();
        try
        {
            if (!refresh && _apps.Length > 0 && DateTime.UtcNow - _refreshed < TimeSpan.FromMinutes(5)) return _apps;
            _apps = await RunStaAsync(() =>
            {
                object? shell = null, folder = null, items = null;
                try
                {
                    shell = CreateShell();
                    folder = ((dynamic)shell).NameSpace(AppsFolder);
                    items = ((dynamic)folder).Items();
                    var apps = new List<WindowsAppEntry>();
                    for (var index = 0; index < (int)((dynamic)items).Count; index++)
                    {
                        object? item = null;
                        try
                        {
                            item = ((dynamic)items).Item(index);
                            string name = ((dynamic)item).Name;
                            string id = ((dynamic)item).Path;
                            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id)) apps.Add(new(name, id));
                        }
                        catch (COMException) { /* A removed or inaccessible entry must not hide the other apps. */ }
                        finally { Release(item); }
                    }
                    if (!apps.Any(app => WindowsLaunchCatalog.Normalize(app.Name) == "fileexplorer")) apps.Add(new("File Explorer", "@file-explorer"));
                    return apps.DistinctBy(app => app.Id, StringComparer.OrdinalIgnoreCase).OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
                }
                finally { Release(items); Release(folder); Release(shell); }
            });
            _refreshed = DateTime.UtcNow;
            return _apps;
        }
        finally { _gate.Release(); }
    }

    private static object CreateShell() => Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    private static Task<T> RunStaAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { completion.SetResult(action()); } catch (Exception ex) { completion.SetException(ex); } })
            { IsBackground = true, Name = "Island Windows launcher" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
