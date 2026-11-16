using DynamicIsland.Windows.Services;
using var monitor = new WindowsCameraActivityMonitor(apps => Console.WriteLine("ACTIVE " + string.Join(", ", apps)), Console.WriteLine);
if (!monitor.Start()) return 1;
await Task.Delay(4000);
Console.WriteLine("Current streaming apps: " + string.Join(", ", monitor.CurrentApps));
return 0;
