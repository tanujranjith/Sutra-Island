using System.Globalization;
using System.Text.RegularExpressions;
using DynamicIsland.Q.Core;
namespace DynamicIsland.Windows.Infrastructure.CommandPalette;

public static class AssistantControlIntent
{
    private static Match Match(string text,string pattern)=>Regex.Match(text,pattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    public static bool TryResolve(string prompt,out AssistantControlRequest request)
    {
        request=null!; if(prompt.Length>2000) return false;
        var text=prompt.Trim().TrimEnd('?', '!', '.').Trim();
        text=Regex.Replace(text,@"^(?:(?:hey\s+sutra|sutra)[,\s]+|please\s+|(?:can|could|would|will)\s+(?:you|u)\s+|i\s+want\s+you\s+to\s+)+","",RegexOptions.IgnoreCase);
        text=Regex.Replace(text,@"\s+please$","",RegexOptions.IgnoreCase).Trim();
        var volume=Match(text,@"^(?:(?<verb>set|change|adjust|increase|decrease|raise|lower|turn up|turn down|turn)\s+)?(?:(?:the|my|system)\s+)*volume(?:\s+(?<direction>up|down))?\s*(?:(?<prep>to|at|by)\s+)?(?<amount>[+-]?\d+(?:\.\d+)?|[a-z]+(?:[ -][a-z]+)?)(?:\s*(?:%|percent))?$");
        if(volume.Success && TryNumber(Regex.Replace(volume.Groups["amount"].Value,@"\s+percent$","",RegexOptions.IgnoreCase),out var amount))
        {
            var verb=volume.Groups["verb"].Value.ToLowerInvariant(); var prep=volume.Groups["prep"].Value.ToLowerInvariant(); var direction=volume.Groups["direction"].Value.ToLowerInvariant();
            var relative=prep=="by" || prep is not ("to" or "at") && (verb is "increase" or "decrease" or "raise" or "lower" or "turn up" or "turn down" || direction.Length>0 || volume.Groups["amount"].Value.StartsWith('+') || volume.Groups["amount"].Value.StartsWith('-'));
            if(relative && (verb is "decrease" or "lower" or "turn down" || direction=="down")) amount=-Math.Abs(amount);
            var argument=amount.ToString(CultureInfo.InvariantCulture); if(relative && amount>=0) argument="+"+argument;
            if(!relative && amount<0) argument="invalid absolute volume";
            request=new("Volume",argument); return true;
        }
        if(Match(text,@"^(?:(?:turn|put)\s+(?:the\s+)?)?volume\s+(?:up|down)$").Success)
        {request=new("Volume",text.EndsWith("down",StringComparison.OrdinalIgnoreCase)?"-10":"+10");return true;}
        if(Match(text,@"^(?:mute|unmute)(?:\s+(?:(?:the|my|system)\s+)?(?:volume|sound|audio))?$").Success)
        {request=new(text.StartsWith("unmute",StringComparison.OrdinalIgnoreCase)?"Unmute":"Mute");return true;}
        var timer=Match(text,@"^(?:set|start|create)\s+(?:a\s+)?timer(?:\s+for)?\s+(.+)$");
        if(timer.Success){request=new("Timer",NormalizeDuration(Regex.Replace(timer.Groups[1].Value,@"\s+(?:called|named|labelled)\s+"," for ",RegexOptions.IgnoreCase)));return true;}
        var alarm=Match(text,@"^(?:set|create)\s+(?:an?\s+)?alarm(?:\s+(?:for|at))?\s+(.+)$");
        if(alarm.Success && Match(alarm.Groups[1].Value,@"^\d{1,2}:\d{2}").Success){request=new("Alarm",alarm.Groups[1].Value);return true;}
        var media=Match(text,@"^(pause|play|resume|unpause)\s+(?:the\s+)?(?:music|media|song|playback)$");
        if(media.Success){request=new(media.Groups[1].Value.Equals("pause",StringComparison.OrdinalIgnoreCase)?"Pause":"Play");return true;}
        if(TryNormalizeSeekArgument(text,out var seek)){request=new("Seek",seek);return true;}
        var stopwatch=Match(text,@"^(start|pause|stop|resume|reset|lap)\s+(?:the\s+)?stopwatch$");
        if(stopwatch.Success){request=new(stopwatch.Groups[1].Value.ToLowerInvariant() switch {"start"=>"StartStopwatch","resume"=>"ResumeStopwatch","reset"=>"ResetStopwatch","lap"=>"LapStopwatch",_=>"PauseStopwatch"});return true;}
        var output=Match(text,@"^(?:switch|change|set)\s+(?:the\s+)?(?:audio\s+)?(?:output|output device|sound output)\s+to\s+(.+)$");
        if(output.Success){request=new("SetOutput",output.Groups[1].Value);return true;}
        var focus=Match(text,@"^(?:start\s+(?:a\s+)?)?focus(?:\s+(?:session|mode))?\s+for\s+(.+)$");
        if(focus.Success){request=new("FocusSession",NormalizeDuration(focus.Groups[1].Value));return true;}
        if(Match(text,@"^(?:end|stop|cancel)\s+(?:the\s+)?focus(?:\s+(?:session|mode))?$").Success){request=new("EndFocus");return true;}
        var open=Match(text,@"^(?:open|show)\s+(?:(?:the|my)\s+)?(clipboard(?: history)?|notifications?(?: history)?|audio outputs?|output devices?|stopwatch|timers?|alarms?|settings)$");
        if(open.Success)
        {
            var name=open.Groups[1].Value.ToLowerInvariant();
            request=new(name.StartsWith("clipboard")?"Clipboard":name.StartsWith("notification")?"NotificationHistory":name is "audio output" or "audio outputs" or "output devices"?"AudioOutput":name=="stopwatch"?"Stopwatch":name.StartsWith("timer")?"ShowTimers":name.StartsWith("alarm")?"ShowAlarms":"OpenSettings");return true;
        }
        if(Match(text,@"^(?:enable|disable|turn on|turn off)\s+(?:the\s+)?(?:windows\s+)?(?:notifications?|notification mirroring)$").Success)
        { request=new(Match(text,@"^(?:disable|turn off)").Success?"NotificationsOff":"NotificationsOn");return true; }
        if(Match(text,@"^(?:enable|disable|turn on|turn off)\s+(?:the\s+)?focus(?: mode)?$").Success)
        { request=new(Match(text,@"^(?:disable|turn off)").Success?"FocusOff":"FocusOn");return true; }
        var setting=Match(text,@"^(?<verb>enable|disable|show|hide|turn on|turn off)\s+(?:the\s+)?(?<name>.+)$");
        if(setting.Success)
        {
            var name=setting.Groups["name"].Value; var query=Regex.Replace(name.ToLowerInvariant(),"[^a-z0-9]","");
            if(query.Length>1 && SettingsSearchIndex.All.Any(entry=>Regex.Replace((entry.Name+entry.Property).ToLowerInvariant(),"[^a-z0-9]","").Contains(query)))
            { request=new("SetSetting",name,setting.Groups["verb"].Value.ToLowerInvariant() is "disable" or "hide" or "turn off"?"false":"true");return true; }
        }
        var match=CommandCatalog.Match(text);
        if(match is null || !AllowedCommands.Contains(match.Command.Id)) return false;
        if(match.Argument.Length>0 && !ArgumentCommands.Contains(match.Command.Id)) return false;
        request=new(match.Command.Id.ToString(),match.Command.Id==PaletteCommand.Timer?NormalizeDuration(match.Argument):match.Argument);return true;
    }
    public static string NormalizeDuration(string argument)
    {
        var labelIndex=argument.IndexOf(" for ",StringComparison.OrdinalIgnoreCase);
        var duration=labelIndex>=0?argument[..labelIndex]:argument;var label=labelIndex>=0?argument[labelIndex..]:"";
        duration=Regex.Replace(duration,@"\bhalf\s+(?:an?\s+)?hour\b","30 minutes",RegexOptions.IgnoreCase);
        duration=Regex.Replace(duration,@"\bhalf\s+(?:an?\s+)?minute\b","30 seconds",RegexOptions.IgnoreCase);
        duration=Regex.Replace(duration,@"\s+and\s+"," ",RegexOptions.IgnoreCase);
        duration=Regex.Replace(duration,@"\b(?<number>[a-z]+(?:[- ][a-z]+)?)\s+(?<unit>hours?|minutes?|seconds?)\b",match=>
        {
            var number=match.Groups["number"].Value;
            if(number.Equals("a",StringComparison.OrdinalIgnoreCase)||number.Equals("an",StringComparison.OrdinalIgnoreCase))return "1 "+match.Groups["unit"].Value;
            return TryNumber(number,out var value)?value.ToString(CultureInfo.InvariantCulture)+" "+match.Groups["unit"].Value:match.Value;
        },RegexOptions.IgnoreCase);
        duration=Regex.Replace(duration,@"(?<![\d.])(?<number>\d+\.\d+)\s*(?<unit>hours?|hrs?|hr|h|minutes?|mins?|min|m|seconds?|secs?|sec|s)\b",match=>
        {
            if(!decimal.TryParse(match.Groups["number"].Value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value))return "0 seconds";
            try
            {
                var seconds=value*(char.ToLowerInvariant(match.Groups["unit"].Value[0]) switch {'h'=>3600m,'m'=>60m,_=>1m});
                return seconds>=1 && seconds%1==0?seconds.ToString("0",CultureInfo.InvariantCulture)+" seconds":"0 seconds";
            }
            catch(OverflowException){return "0 seconds";}
        },RegexOptions.IgnoreCase);
        return duration+label;
    }
    public static bool TryNormalizeSeekArgument(string argument,out string normalized)
    {
        normalized=argument;
        var text=argument.Trim().TrimEnd('.', '!', '?').Trim();
        text=Regex.Replace(text,@"\s+(?:in|of)\s+(?:(?:the|this|current)\s+)?(?:song|track|music|video|playback)$","",RegexOptions.IgnoreCase);
        var seek=Match(text,@"^(?:(?:seek|skip|go|jump|move)\s+)?(?<direction>back(?:ward|wards)?|forward(?:s)?|ahead)\s+(?:by\s+)?(?<duration>.+)$");
        if(!seek.Success) seek=Match(text,@"^(?<direction>rewind|fast forward)\s+(?:(?:the|this|current)\s+)?(?:(?:song|track|music|video|playback)\s+)?(?:by\s+)?(?<duration>.+)$");
        if(!seek.Success) seek=Match(text,@"^(?:(?:seek|skip|go|jump|move)\s+)?(?<duration>.+?)\s+(?<direction>back(?:ward|wards)?|forward(?:s)?|ahead)$");
        if(!seek.Success) return false;
        var duration=NormalizeDuration(seek.Groups["duration"].Value);
        // Only explicit durations qualify as direct actions; conversational uses of "go back" do not.
        if(!Match(duration,@"^(?:\d+\s*(?:hours?|hrs?|hr|h|minutes?|mins?|min|m|seconds?|secs?|sec|s)\s*)+$").Success) return false;
        var direction=seek.Groups["direction"].Value;
        normalized=(direction.StartsWith("back",StringComparison.OrdinalIgnoreCase)||direction.Equals("rewind",StringComparison.OrdinalIgnoreCase)?"back ":"forward ")+duration.Trim();
        return true;
    }
    public static readonly HashSet<PaletteCommand> AllowedCommands = [PaletteCommand.Pause,PaletteCommand.Play,PaletteCommand.Next,PaletteCommand.Previous,PaletteCommand.Seek,PaletteCommand.Mute,PaletteCommand.Unmute,PaletteCommand.Volume,PaletteCommand.OpenMedia,PaletteCommand.Timer,PaletteCommand.PauseTimer,PaletteCommand.ResumeTimer,PaletteCommand.CancelTimer,PaletteCommand.ShowTimers,PaletteCommand.Alarm,PaletteCommand.CancelAlarm,PaletteCommand.ShowAlarms,PaletteCommand.Stopwatch,PaletteCommand.StartStopwatch,PaletteCommand.PauseStopwatch,PaletteCommand.ResumeStopwatch,PaletteCommand.ResetStopwatch,PaletteCommand.Focus,PaletteCommand.FocusOn,PaletteCommand.FocusOff,PaletteCommand.Expand,PaletteCommand.Collapse,PaletteCommand.OpenSettings,PaletteCommand.SearchSettings,PaletteCommand.Recenter,PaletteCommand.Clipboard,PaletteCommand.NotificationHistory,PaletteCommand.NotificationsOn,PaletteCommand.NotificationsOff,PaletteCommand.RetryNotifications,PaletteCommand.DismissBanner,PaletteCommand.AudioOutput,PaletteCommand.Convert,PaletteCommand.WakeOn,PaletteCommand.WakeOff];
    private static readonly HashSet<PaletteCommand> ArgumentCommands=[PaletteCommand.Seek,PaletteCommand.Volume,PaletteCommand.Timer,PaletteCommand.PauseTimer,PaletteCommand.ResumeTimer,PaletteCommand.CancelTimer,PaletteCommand.Alarm,PaletteCommand.CancelAlarm,PaletteCommand.OpenSettings,PaletteCommand.SearchSettings,PaletteCommand.Convert];
    private static bool TryNumber(string text,out decimal value)
    {
        if(decimal.TryParse(text,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out value)) return true;
        var words=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase){{"zero",0},{"one",1},{"two",2},{"three",3},{"four",4},{"five",5},{"six",6},{"seven",7},{"eight",8},{"nine",9},{"ten",10},{"eleven",11},{"twelve",12},{"thirteen",13},{"fourteen",14},{"fifteen",15},{"sixteen",16},{"seventeen",17},{"eighteen",18},{"nineteen",19},{"twenty",20},{"thirty",30},{"forty",40},{"fifty",50},{"sixty",60},{"seventy",70},{"eighty",80},{"ninety",90},{"hundred",100}};
        if(text.Equals("one hundred",StringComparison.OrdinalIgnoreCase)){value=100;return true;}
        var pieces=text.Split([' ','-'],StringSplitOptions.RemoveEmptyEntries);value=0;
        if(pieces.Length==1 && words.TryGetValue(pieces[0],out var number)){value=number;return true;}
        if(pieces.Length==2 && words.TryGetValue(pieces[0],out var tens) && tens>=20 && tens<100 && tens%10==0 && words.TryGetValue(pieces[1],out var units) && units>0 && units<10){value=tens+units;return true;}
        return false;
    }
}
