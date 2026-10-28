using DynamicIsland.Windows.Infrastructure.CommandPalette;

namespace DynamicIsland.Windows.ViewModels;

public sealed partial class CommandPaletteViewModel
{
    private string _conversionCategory = "Length", _conversionValue = "1", _conversionFrom = "m", _conversionTo = "ft", _conversionResult = "", _conversionError = "";
    private bool _rebuildingUnits;
    public IReadOnlyList<string> ConversionCategories => UnitConversionParser.Categories;
    public IReadOnlyList<string> ConversionUnits => UnitConversionParser.SymbolsFor(ConversionCategory);
    public string ConversionCategory
    {
        get => _conversionCategory;
        set { if (value is not null && SetProperty(ref _conversionCategory, value)) RebuildConversionUnits(); }
    }
    public string ConversionValue { get => _conversionValue; set { if (SetProperty(ref _conversionValue, value)) UpdateConversion(); } }
    public string ConversionFrom { get => _conversionFrom; set { if (!_rebuildingUnits && value is not null && ConversionUnits.Contains(value) && SetProperty(ref _conversionFrom, value)) UpdateConversion(); } }
    public string ConversionTo { get => _conversionTo; set { if (!_rebuildingUnits && value is not null && ConversionUnits.Contains(value) && SetProperty(ref _conversionTo, value)) UpdateConversion(); } }
    public string ConversionResult => _conversionResult;
    public string ConversionError => _conversionError;
    public bool CanCopyConversion => string.IsNullOrEmpty(_conversionError) && !string.IsNullOrEmpty(_conversionResult);
    private void RebuildConversionUnits()
    {
        _rebuildingUnits = true;
        try
        {
            var units = ConversionUnits;
            if (!units.Contains(_conversionFrom)) _conversionFrom = units.FirstOrDefault() ?? "";
            if (!units.Contains(_conversionTo)) _conversionTo = units.Skip(1).FirstOrDefault() ?? _conversionFrom;
            RaisePropertyChanged(nameof(ConversionUnits)); RaisePropertyChanged(nameof(ConversionFrom)); RaisePropertyChanged(nameof(ConversionTo));
        }
        finally { _rebuildingUnits = false; }
        UpdateConversion();
    }
    private void UpdateConversion()
    {
        var valid = UnitConversionParser.TryConvert($"{ConversionValue} {ConversionFrom} to {ConversionTo}", out var result, out var error);
        _conversionResult = valid ? result : "—"; _conversionError = valid ? "" : error ?? "Enter a valid value.";
        RaisePropertyChanged(nameof(ConversionResult)); RaisePropertyChanged(nameof(ConversionError)); RaisePropertyChanged(nameof(CanCopyConversion));
    }
    public void SwapConversion()
    {
        (_conversionFrom, _conversionTo) = (_conversionTo, _conversionFrom);
        RaisePropertyChanged(nameof(ConversionFrom)); RaisePropertyChanged(nameof(ConversionTo)); UpdateConversion();
    }
    public void CopyConversion()
    {
        if (!CanCopyConversion) return;
        try { System.Windows.Clipboard.SetText(ConversionResult); ShowStatus("Result copied to clipboard.", false); }
        catch { ShowStatus("Couldn’t copy the result. Try again.", true); }
    }
}
