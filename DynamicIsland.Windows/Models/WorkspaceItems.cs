namespace DynamicIsland.Windows.Models;

public enum IslandGlance { None, Timer, Stopwatch, Cpu, Memory, Battery, Clock }
public enum TrayItemKind { File, Folder, Link, Screenshot }

public sealed record TrayItem(TrayItemKind Kind, string Value, string Label);
public sealed record CapturedNote(string Text, DateTimeOffset CreatedAt);
