using System.Windows.Threading;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;
namespace DynamicIsland.Windows.Infrastructure;
internal static partial class UpgradeVerification
{
    private static async Task VerifyPercentageCalculatorAsync(CommandPaletteViewModel vm, CommandPaletteWindow window, Action<bool,string> check, string output)
    {
        window.PrepareForShow(); window.Show(); vm.Query="5%1000";
        await window.Dispatcher.InvokeAsync(()=>window.UpdateLayout(),DispatcherPriority.ApplicationIdle);
        check(vm.SelectedSuggestion?.CalculationValue=="50" && vm.SelectedSuggestion.IsCalculation,"Palette displays 50 for the reported 5%1000 expression");
        await CaptureSutraAsync(window,output,"calculator-percent");
        vm.Query="5% of 1000"; check(vm.SelectedSuggestion?.CalculationValue=="50","Percentage-of wording uses calculator results rather than unit conversion");
        vm.Query="1000 + 5%"; check(vm.SelectedSuggestion?.CalculationValue=="1050","Palette supports percentage increases");
        vm.Query="5 mod 2"; check(vm.SelectedSuggestion?.CalculationValue=="1","Explicit mod preserves remainder calculations without conflicting with unit conversion");
        vm.Query="5% of"; check(vm.IsStatusError && vm.SelectedSuggestion?.IsCalculation!=true,"Incomplete percentage input does not display a misleading numeric result");
        vm.Query="12 in to ft"; check(vm.SelectedSuggestion?.CalculationValue=="1 ft","Percentage calculator changes preserve existing unit conversion routing");
        vm.Query="3x+44x=1253"; check(vm.SelectedSuggestion?.CalculationValue=="x = 26.659574468085106382978723404" && !vm.IsStatusError,"Palette displays a copyable solution for the reported algebra equation");
        vm.Query="2(x+3)=14"; check(vm.SelectedSuggestion?.CalculationValue=="x = 4","Palette solves equations with parentheses");
        vm.Query="x*x=9"; check(vm.IsStatusError && vm.SelectedSuggestion?.IsCalculation!=true,"Unsupported nonlinear equations do not show a misleading result");
        window.Hide();
    }
}
