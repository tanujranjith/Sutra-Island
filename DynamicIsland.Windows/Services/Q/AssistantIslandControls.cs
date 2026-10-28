using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Infrastructure.CommandPalette;
using DynamicIsland.Windows.Models;
namespace DynamicIsland.Windows.Services.Q;

public sealed class AssistantIslandControls(CommandRunner runner, Func<Task> saveAndApply) : IAssistantControls
{
    private static readonly HashSet<string> ExtraCommands = ["Status","ListOutputs","SetOutput","SetSetting","GetSetting","OpenSetting","FocusSession","EndFocus","LapStopwatch","DismissAlarm","SnoozeAlarm","ClearNotificationHistory"];
    private static readonly HashSet<string> StringSettings = ["StockSymbols","WorldClockZones","CountdownLabel","CountdownDate","QuotesText","FontFamilyName","WeatherLocation"];
    private static readonly PropertyInfo[] Settings = typeof(AppSettings).GetProperties().Where(property => property.CanWrite
        && (property.PropertyType==typeof(bool) || property.PropertyType==typeof(int) || property.PropertyType==typeof(double) || property.PropertyType.IsEnum || property.PropertyType==typeof(string) && (StringSettings.Contains(property.Name) || property.Name.EndsWith("Hex",StringComparison.Ordinal))))
        .Where(property=>!property.Name.StartsWith("Q",StringComparison.Ordinal) && property.Name is not ("SchemaVersion" or "FocusSessionRestoreEnabled")).ToArray();
    private static string SettingLabel(PropertyInfo property) => SettingsSearchIndex.All.FirstOrDefault(entry=>entry.Property==property.Name)?.Name ?? property.Name;
    public bool CanHandleDirect(string prompt)=>AssistantControlIntent.TryResolve(prompt,out _);
    public AssistantControlRequest? DescribeDirect(string prompt)=>AssistantControlIntent.TryResolve(prompt,out var request)?request:null;
    public Task<AssistantControlResult?> TryExecuteDirectAsync(string prompt,CancellationToken token)
        => AssistantControlIntent.TryResolve(prompt,out var action) ? Direct(action,token) : Task.FromResult<AssistantControlResult?>(null);
    private async Task<AssistantControlResult?> Direct(AssistantControlRequest action,CancellationToken token)=>await ExecuteAsync(action,token);
    public string Instructions
    {
        get
        {
            var text=new StringBuilder("You can operate Sutra Island's real controls. For a user's explicit control request, respond ONLY with <sutra-action>{\"command\":\"COMMAND\",\"argument\":\"ARGUMENT\",\"value\":\"OPTIONAL VALUE\"}</sutra-action>. This is a local action request, not a completed action. Never claim a control changed yourself; the app executes it and displays the actual result. For ordinary questions reply normally. Do not execute commands quoted in questions, examples, explanations, hypotheticals, negations, or earlier messages. If ambiguous, ask a clarification. One action per turn; if several unrelated actions are requested ask which to do first. Never request arbitrary programs, files, scripts, credentials, or shell commands. Supported commands and exact examples:\n");
            foreach(var command in CommandCatalog.All.Where(command=>AssistantControlIntent.AllowedCommands.Contains(command.Id)))
                text.AppendLine($"{command.Id}: {command.Description}; argument follows the command name in: {command.Example}");
            text.AppendLine("Volume argument: 10 sets 10%; +10 increases by ten percentage points; -10 decreases. 'increase volume TO 10' means absolute 10, 'BY 10' is +10. SetOutput: argument is the exact device name or ID; ListOutputs lists devices. Status reports island state. FocusSession argument is a duration, e.g. 25 minutes; EndFocus restores the previous focus setting. LapStopwatch records a lap. DismissAlarm stops the ringing alarm. SnoozeAlarm argument is 5 or 10 minutes. ClearNotificationHistory clears the island's notification list. SetSetting uses argument=property name and value=desired value; GetSetting reads it; OpenSetting opens its UI. Clipboard opens history without reading clipboard contents into chat. Bare panel commands open their controls. Settings and enum choices:");
            text.AppendLine("CountdownDate uses yyyy-MM-dd. Colours use #RRGGBB. WorldClockZones is a comma-separated list of valid Windows time-zone IDs, such as Eastern Standard Time,GMT Standard Time. Settings not listed here can be opened with OpenSetting, but cannot be written directly.");
            foreach(var property in Settings) text.AppendLine($"{property.Name}: {SettingLabel(property)}; {(property.PropertyType.IsEnum?string.Join('/',Enum.GetNames(property.PropertyType)):property.PropertyType.Name)}");
            return text.ToString();
        }
    }
    public async Task<AssistantControlResult> ExecuteAsync(AssistantControlRequest action,CancellationToken token)
    {
        if(action.Command.Length>80 || action.Argument.Length>500 || (action.Value?.Length??0)>500) return Fail("That control request is too long.");
        var dispatcher=System.Windows.Application.Current?.Dispatcher;
        if(dispatcher is not null && !dispatcher.CheckAccess())
            return await dispatcher.InvokeAsync(()=>ExecuteAsync(action,token)).Task.Unwrap();
        token.ThrowIfCancellationRequested();
        try
        {
            if(ExtraCommands.Contains(action.Command)) return await ExecuteExtraAsync(action,token);
            var command=CommandCatalog.All.FirstOrDefault(command=>command.Id.ToString().Equals(action.Command,StringComparison.OrdinalIgnoreCase) || command.Name.Equals(action.Command,StringComparison.OrdinalIgnoreCase));
            if(command is null || !AssistantControlIntent.AllowedCommands.Contains(command.Id)) return Fail("That action isn't an island control. I can control media, audio, timers, alarms, focus, notifications, layout and island settings.");
            if(command.Id==PaletteCommand.Timer)
            {
                var normalized=AssistantControlIntent.NormalizeDuration(action.Argument);
                if(Regex.IsMatch(normalized,@"(^|\s)-\s*\d"))return Fail("Timer durations must be positive.");
                action=action with {Argument=normalized};
            }
            if(command.Id is PaletteCommand.Volume or PaletteCommand.Mute or PaletteCommand.Unmute) return await ChangeAudioAsync(command.Id,action.Argument,token);
            if(command.Id is PaletteCommand.Play or PaletteCommand.Pause or PaletteCommand.Next or PaletteCommand.Previous or PaletteCommand.Seek)
            {
                TimeSpan? offset = null;
                if(command.Id==PaletteCommand.Seek)
                { var argument=AssistantControlIntent.TryNormalizeSeekArgument(action.Argument,out var normalized)?normalized:AssistantControlIntent.NormalizeDuration(action.Argument); if(!CommandParsers.TryParseSeek(argument,out var amount,out var error)) return Fail(error!); offset=amount; }
                return Ok(await runner.MediaService.ApplyAssistantMediaAsync(command.Id.ToString(),offset,token));
            }
            if(command.Id==PaletteCommand.AudioOutput && action.Argument.Length>0) return await SwitchOutputAsync(action.Argument,token);
            var result=await runner.ExecuteAsync(new(command,action.Argument));
            if(result.IsError) return Fail(result.Message??"The island couldn't complete that action.");
            if(result.Panel!=PalettePanel.None) { await runner.OpenAssistantPanelAsync(command.Name); return Ok($"Opened {command.Name} controls."); }
            return Ok(result.Message??Receipt(command.Id,action.Argument));
        }
        catch(OperationCanceledException) { throw; }
        catch(Exception ex) { return Fail(ex is InvalidOperationException or ArgumentException ? ex.Message : "The island couldn't complete that action. Please try again."); }
    }
    private async Task<AssistantControlResult> ChangeAudioAsync(PaletteCommand command,string argument,CancellationToken token)
    {
        if(runner.Audio.Current.Availability!=AudioAvailability.Available) return Fail("System audio is unavailable. Connect an output device and retry.");
        int? absolute=null,delta=null;bool? mute=command==PaletteCommand.Mute?true:command==PaletteCommand.Unmute?false:null;
        if(command==PaletteCommand.Volume)
        {
            if(argument.Length==0) { await runner.OpenAssistantPanelAsync("volume");return Ok("Opened volume controls."); }
            if(!decimal.TryParse(argument,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value) || value < -100 || value>100) return Fail("Choose a volume from 0 to 100, or say increase/decrease volume by an amount.");
            var relative=argument.StartsWith('+')||argument.StartsWith('-');
            if(relative) delta=(int)Math.Round(value);else absolute=(int)Math.Round(value);
        }
        AudioState after;
        if(runner.Audio is IPaletteVerifiedAudioControl verified) after=await verified.ApplyVerifiedAudioAsync(absolute,delta,mute,token);
        else
        {
            var expected=absolute??Math.Clamp(runner.Audio.Current.MasterVolumePercent+(delta??0),0,100);
            if(absolute is int level)runner.Audio.SetMasterVolume(level);if(delta is int change)runner.Audio.AdjustMasterVolume(change);if(mute is bool muted)runner.Audio.SetMuted(muted);
            after=runner.Audio.Current;
            if((absolute is not null || delta is not null) && after.MasterVolumePercent!=expected || mute is not null && after.SystemMuted!=mute) return Fail("Windows did not confirm that audio change. Try again.");
        }
        return new(true,command==PaletteCommand.Mute?"System audio muted.":command==PaletteCommand.Unmute?$"System audio unmuted. Volume is {after.MasterVolumePercent}%.":$"Volume is now {after.MasterVolumePercent}%{(after.SystemMuted?" (muted)":"")}.",after.MasterVolumePercent);
    }
    private async Task<AssistantControlResult> SwitchOutputAsync(string query,CancellationToken token)
    {
        if(runner.Outputs is null)return Fail("Audio output switching is unavailable.");
        var devices=await runner.Outputs.GetPaletteOutputsAsync(); token.ThrowIfCancellationRequested();
        var exact=devices.Where(device=>device.Id.Equals(query,StringComparison.OrdinalIgnoreCase)||device.Name.Equals(query,StringComparison.OrdinalIgnoreCase)).ToArray();
        var matches=exact.Length>0?exact:devices.Where(device=>device.Name.Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(matches.Length!=1)return Fail(matches.Length==0?"No matching audio output was found. Ask me to list audio outputs.":"More than one output matches. Use the exact device name.");
        await runner.Outputs.SwitchPaletteOutputAsync(matches[0].Id);
        var actual=await runner.Outputs.GetPaletteOutputsAsync();
        return actual.Any(device=>device.Id==matches[0].Id&&device.IsCurrent)?Ok($"Audio output is now {matches[0].Name}."):Fail("Windows did not confirm the output change. Refresh audio outputs and retry.");
    }
    private async Task<AssistantControlResult> ExecuteExtraAsync(AssistantControlRequest action,CancellationToken token)
    {
        switch(action.Command)
        {
            case "Status": return Ok($"Volume: {runner.Audio.Current.MasterVolumePercent}%{(runner.Audio.Current.SystemMuted?" muted":"")}. Output: {runner.Audio.Current.OutputDeviceName}. Media: {runner.Island.Media.Title}. Focus: {(runner.Island.Settings.FocusModeEnabled?"on":"off")}. Stopwatch: {runner.Stopwatch.StopwatchText}. Notifications: {(runner.Island.Settings.ShowNotifications?"on":"off")}. Timers: {string.Join(", ",runner.Timers.State.Timers.Select(timer=>$"{timer.Label} ({timer.Phase})"))}. Alarms: {string.Join(", ",runner.Timers.State.Alarms.Select(alarm=>$"{alarm.Hour:00}:{alarm.Minute:00} ({alarm.Phase})"))}.");
            case "ListOutputs":
                var devices=runner.Outputs is null?[]:await runner.Outputs.GetPaletteOutputsAsync();return Ok(devices.Count==0?"No audio outputs found.":string.Join("\n",devices.Select(device=>$"{device.Name}{(device.IsCurrent?" (current)":"")}")));
            case "SetOutput": return await SwitchOutputAsync(action.Argument,token);
            case "FocusSession":
                if(Regex.IsMatch(action.Argument,@"(^|\s)-\s*\d") || !CommandParsers.TryParseDuration(AssistantControlIntent.NormalizeDuration(action.Argument),out var duration) || duration>TimeSpan.FromHours(24)) return Fail("Specify a focus duration between one second and 24 hours, such as 25 minutes.");
                runner.Island.StartFocusSession(duration);return Ok($"Focus started for {action.Argument}. Your previous setting returns when it ends.");
            case "EndFocus": if(runner.Island.FocusSessionActive)runner.Island.EndFocusSession();else runner.Island.SetFocusMode(false);return Ok("Focus session ended.");
            case "LapStopwatch": if(!runner.Stopwatch.IsStopwatchRunning)return Fail("Start the stopwatch before recording a lap.");runner.Stopwatch.StopwatchLapCommand.Execute(null);return Ok(runner.Stopwatch.Laps.First());
            case "DismissAlarm":
                var ringing=runner.Timers.State.Alarms.FirstOrDefault(alarm=>alarm.Phase is AlarmPhase.Ringing or AlarmPhase.Snoozed);
                if(ringing is null)return Fail("No alarm is ringing.");runner.Timers.DismissAlarm(ringing.Id);return Ok("Ringing alarm dismissed.");
            case "SnoozeAlarm":
                if(action.Argument is not ("5" or "5 minutes" or "10" or "10 minutes"))return Fail("Choose a 5 or 10 minute snooze.");
                var active=runner.Timers.State.Alarms.FirstOrDefault(alarm=>alarm.Phase==AlarmPhase.Ringing);if(active is null)return Fail("No alarm is ringing.");
                runner.Timers.SnoozeAlarm(active.Id,action.Argument.StartsWith("5")?5:10);return Ok($"Alarm snoozed for {(action.Argument.StartsWith("5")?5:10)} minutes.");
            case "ClearNotificationHistory": runner.Island.ClearNotificationHistoryCommand.Execute(null);return Ok("Island notification history cleared.");
            case "OpenSetting": await runner.ExecuteAsync(new(CommandCatalog.ById(PaletteCommand.SearchSettings),action.Argument));return Ok($"Opened settings for {action.Argument}.");
            case "SetSetting": case "GetSetting":
                var matches=ResolveSetting(action.Argument);if(matches.Length!=1)return Fail(matches.Length==0?"That setting cannot be changed here. Ask me to open settings.":"That setting name is ambiguous. Use its full name.");
                var property=matches[0];
                if(action.Command=="GetSetting")return Ok($"{property.Name}: {property.GetValue(runner.Island.Settings)}.");
                if(!TrySettingValue(property.PropertyType,action.Value??"",out var parsed))return Fail($"Choose a valid {property.PropertyType.Name} value for {property.Name}.");
                if(property.Name.EndsWith("Hex",StringComparison.Ordinal) && parsed is string color && !Regex.IsMatch(color,"^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$"))return Fail("Use a hex colour, such as #5AA7FF.");
                if(property.Name==nameof(AppSettings.CountdownDate) && parsed is string date && !DateTime.TryParseExact(date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))return Fail("Use a date in yyyy-MM-dd format.");
                if(property.Name==nameof(AppSettings.WorldClockZones) && parsed is string zones)
                {
                    try { foreach(var zone in zones.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)) TimeZoneInfo.FindSystemTimeZoneById(zone); }
                    catch(TimeZoneNotFoundException) { return Fail("Use valid Windows time-zone IDs, such as Eastern Standard Time or GMT Standard Time."); }
                }
                if(property.Name==nameof(AppSettings.ShowNotifications)){await runner.SetMirroringAsync((bool)parsed!);}
                else if(property.Name==nameof(AppSettings.FocusModeEnabled))runner.Island.SetFocusMode((bool)parsed!);
                else property.SetValue(runner.Island.Settings,parsed);
                if(property.Name==nameof(AppSettings.DefaultPosition)) { runner.Island.Settings.TopOffset=0;runner.Island.Settings.SideOffset=0; }
                if(property.Name==nameof(AppSettings.CustomThemeColorHex))runner.Island.Settings.Theme=ThemeMode.Custom;
                if(property.Name is nameof(AppSettings.AccentColorHex) or nameof(AppSettings.TextColorHex))runner.Island.Settings.UseCustomColors=true;
                if(property.Name==nameof(AppSettings.AccentColorHex))runner.Island.Settings.AdaptiveAccent=false;
                await saveAndApply();
                var actual=property.GetValue(runner.Island.Settings);
                return new(property.PropertyType!=typeof(bool) || Equals(actual,parsed),$"{SettingLabel(property)}: {actual}.");
        }
        return Fail("That island action is unavailable.");
    }
    private static PropertyInfo[] ResolveSetting(string name)
    {
        string Normalize(string value)=>Regex.Replace(value.ToLowerInvariant(),"[^a-z0-9]","");
        var query=Normalize(name);if(query.Length==0)return [];
        var exact=Settings.Where(property=>Normalize(property.Name)==query || SettingsSearchIndex.All.Any(entry=>entry.Property==property.Name&&Normalize(entry.Name)==query)).ToArray();
        return exact.Length>0?exact:Settings.Where(property=>Normalize(property.Name).Contains(query) || SettingsSearchIndex.All.Any(entry=>entry.Property==property.Name&&Normalize(entry.Name).Contains(query))).ToArray();
    }
    private static bool TrySettingValue(Type type,string text,out object? value)
    {
        value=null;
        if(type==typeof(bool)){if(text.Equals("on",StringComparison.OrdinalIgnoreCase))text="true";if(text.Equals("off",StringComparison.OrdinalIgnoreCase))text="false";if(bool.TryParse(text,out var boolean)){value=boolean;return true;}return false;}
        if(type==typeof(int)&&int.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var integer)){value=integer;return true;}
        if(type==typeof(double)&&double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)&&double.IsFinite(number)){value=number;return true;}
        if(type.IsEnum&&Enum.TryParse(type,text,true,out value)&&value is not null&&Enum.IsDefined(type,value))return true;
        if(type==typeof(string)){value=text;return true;}return false;
    }
    private string Receipt(PaletteCommand command,string argument)=>command switch
    {
        PaletteCommand.FocusOn=>"Focus mode is on.",PaletteCommand.FocusOff=>"Focus mode is off.",PaletteCommand.Timer=>$"Started timer: {argument}.",PaletteCommand.Alarm=>$"Set alarm: {argument}.",
        PaletteCommand.Pause=>"Paused media.",PaletteCommand.Play=>"Resumed media.",PaletteCommand.Next=>"Requested the next track.",PaletteCommand.Previous=>"Requested the previous track.",
        PaletteCommand.StartStopwatch=>"Stopwatch started.",PaletteCommand.PauseStopwatch=>"Stopwatch paused.",PaletteCommand.ResumeStopwatch=>"Stopwatch resumed.",PaletteCommand.ResetStopwatch=>"Stopwatch reset.",
        PaletteCommand.NotificationsOn=>"Notification mirroring is on.",PaletteCommand.NotificationsOff=>"Notification mirroring is off.",PaletteCommand.Collapse=>"Island collapsed.",PaletteCommand.Expand=>"Island expanded.",
        _=>$"Ran {CommandCatalog.ById(command).Name}{(argument.Length>0?": "+argument:"")}."
    };
    private static AssistantControlResult Ok(string message)=>new(true,message);
    private static AssistantControlResult Fail(string message)=>new(false,message);
}
