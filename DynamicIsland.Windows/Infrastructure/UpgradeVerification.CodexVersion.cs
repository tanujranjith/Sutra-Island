using System.Windows.Controls;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.Services.Q;
using DynamicIsland.Windows.Views;
namespace DynamicIsland.Windows.Infrastructure;
internal static partial class UpgradeVerification
{
    private static async Task VerifyInstalledCodexVersionAsync(IslandWindow island, LoggingService log, Action<bool,string> check)
    {
        // Version-only probe: no login, account reads, model calls, or app-server startup.
        await using var client=new CodexAppServerClient(log:log);
        var runtime=await client.ValidateRuntimeAsync(new CodexRuntimeResolver().Resolve(),CancellationToken.None);
        check(runtime.IsValidated && CodexRuntimeVersion.TryParse(runtime.Version,out var version) && version.IsAtLeast(CodexRuntimeResolver.MinimumSupportedVersion),$"Installed Codex runtime passes version verification: {runtime.Version}");
        check(((TextBlock)island.FindName("AssistantBrandTitle")).Text=="Sutra Assistant","Assistant conversation header uses Sutra Assistant branding");
    }
}
