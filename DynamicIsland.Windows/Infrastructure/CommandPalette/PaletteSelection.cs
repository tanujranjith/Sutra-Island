namespace DynamicIsland.Windows.Infrastructure.CommandPalette;

public static class PaletteSelection
{
    public static CommandMatch? Resolve(string query, CommandDefinition? selected, string? selectedArgument = null)
    {
        if (selected is null) return CommandCatalog.Match(query);
        if (selectedArgument is not null) return new(selected, selectedArgument);
        var parsed = CommandCatalog.Match(query);
        if (parsed?.Command.Id == selected.Id) return parsed;
        var parts = query.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parsed is null && parts.Length == 2 &&
            (selected.Name.Split(' ')[0].StartsWith(parts[0], StringComparison.OrdinalIgnoreCase) ||
             selected.Aliases.Any(x => x.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase))))
            return new(selected, parts[1]);
        return new(selected, "");
    }
}
