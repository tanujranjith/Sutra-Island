using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.Services;

/// <summary>
/// Fetches current conditions from Open-Meteo (no API key required). Geocodes a free-text location once,
/// then polls the forecast every half hour. Raises Changed on the UI thread.
/// </summary>
public sealed class WeatherService : IDisposable
{
    private readonly LoggingService _log;
    private readonly HttpClient _http;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(30) };
    private string _location = "";
    private bool _fahrenheit;
    private (double Lat, double Lon, string City)? _geo;
    private string _geoKey = "";
    private Task? _refreshTask;
    private CancellationTokenSource? _refreshCancellation;
    private int _configurationGeneration;
    private bool _refreshRequested;
    private bool _started;
    private bool _enabled;
    private bool _disposed;

    public WeatherService(LoggingService log) : this(log, CreateClient()) { }

    internal WeatherService(LoggingService log, HttpMessageHandler handler) : this(log, CreateClient(handler)) { }

    private WeatherService(LoggingService log, HttpClient http)
    {
        _log = log;
        _http = http;
    }

    private static HttpClient CreateClient(HttpMessageHandler? handler = null)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    // Glyphs built from code points so no literal icon characters live in the source.
    private static readonly string Sun = char.ConvertFromUtf32(0xE706);   // Brightness
    private static readonly string Cloud = char.ConvertFromUtf32(0xE753); // Cloud
    private static readonly string Degree = "°";

    public event EventHandler<WeatherInfo?>? Changed;
    public WeatherInfo? Current { get; private set; }

    public void ConfigurePolling(TimeSpan interval)
    {
        var normalized = TimeSpan.FromMinutes(Math.Clamp(interval.TotalMinutes, 1, 180));
        if (_timer.Interval != normalized) _timer.Interval = normalized;
    }

    public void Start()
    {
        if (_disposed || _started) return;
        _started = true;
        _timer.Tick += OnTimerTick;
        if (!_enabled) return;
        _timer.Start();
        _ = RequestRefresh(replaceInFlight: true);
    }

    /// <summary>Enables or suspends weather polling while retaining the last published conditions.</summary>
    public void SetEnabled(bool enabled)
    {
        if (_disposed || _enabled == enabled) return;
        _enabled = enabled;
        _configurationGeneration++;
        if (!enabled)
        {
            _timer.Stop();
            _refreshRequested = false;
            _refreshCancellation?.Cancel();
            return;
        }

        if (!_started) return;
        _timer.Start();
        _ = RequestRefresh(replaceInFlight: true);
    }

    internal void OnTimerTick(object? sender, EventArgs e) => _ = RefreshAsync();

    public void Configure(string location, bool fahrenheit)
    {
        var changed = !string.Equals(location, _location, StringComparison.OrdinalIgnoreCase) || fahrenheit != _fahrenheit;
        _location = location ?? "";
        _fahrenheit = fahrenheit;
        if (!changed) return;
        _configurationGeneration++;
        _ = RequestRefresh(replaceInFlight: true);
    }

    /// <summary>Refreshes conditions when started and enabled; overlapping timer ticks are coalesced.</summary>
    public Task RefreshAsync() => RequestRefresh(replaceInFlight: false);

    private Task RequestRefresh(bool replaceInFlight)
    {
        if (_disposed || !_started || !_enabled) return Task.CompletedTask;
        if (_refreshTask is { IsCompleted: false } active)
        {
            if (replaceInFlight)
            {
                _refreshRequested = true;
                _refreshCancellation?.Cancel();
            }
            return active;
        }

        _refreshRequested = false;
        // Yield before fetching so even a blank location cannot complete before _refreshTask is assigned.
        _refreshTask = RefreshLoopAsync();
        return _refreshTask;
    }

    private async Task RefreshLoopAsync()
    {
        await Task.Yield();
        try
        {
            do
            {
                _refreshRequested = false;
                if (_disposed || !_started || !_enabled) break;

                var generation = _configurationGeneration;
                var location = _location;
                var fahrenheit = _fahrenheit;
                using var cancellation = new CancellationTokenSource();
                _refreshCancellation = cancellation;
                try
                {
                    var info = await FetchAsync(location, fahrenheit, cancellation.Token);
                    if (!_disposed && _started && _enabled && generation == _configurationGeneration
                        && !cancellation.IsCancellationRequested)
                        Publish(info);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch (Exception ex) { _log.Error("Weather refresh failed", ex); }
                finally
                {
                    if (ReferenceEquals(_refreshCancellation, cancellation)) _refreshCancellation = null;
                }
            }
            while (_refreshRequested && !_disposed && _started && _enabled);
        }
        finally
        {
            _refreshCancellation = null;
            _refreshTask = null;
            _refreshRequested = false;
        }
    }

    private async Task<WeatherInfo?> FetchAsync(string location, bool fahrenheit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        var geo = _geo;
        if (geo is null || !string.Equals(_geoKey, location, StringComparison.OrdinalIgnoreCase))
        {
            geo = await GeocodeAsync(location, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(_location, location, StringComparison.OrdinalIgnoreCase))
            {
                _geo = geo;
                _geoKey = location;
            }
        }
        if (geo is not { } g) return null;

        var unit = fahrenheit ? "fahrenheit" : "celsius";
        var url = $"https://api.open-meteo.com/v1/forecast?latitude={g.Lat:0.####}&longitude={g.Lon:0.####}"
                  + $"&current=temperature_2m,weather_code&temperature_unit={unit}";
        using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, cancellationToken));
        var current = doc.RootElement.GetProperty("current");
        var temp = current.GetProperty("temperature_2m").GetDouble();
        var code = current.GetProperty("weather_code").GetInt32();
        var (glyph, desc) = Describe(code);
        return new WeatherInfo($"{Math.Round(temp)}{Degree}", glyph, desc, g.City);
    }

    private async Task<(double, double, string)?> GeocodeAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name)}&count=1";
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, cancellationToken));
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return null;
            var first = results[0];
            return (first.GetProperty("latitude").GetDouble(), first.GetProperty("longitude").GetDouble(),
                first.GetProperty("name").GetString() ?? name);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _log.Error("Geocoding failed", ex); return null; }
    }

    private static (string Glyph, string Desc) Describe(int code) => code switch
    {
        0 => (Sun, "Clear"),
        1 or 2 => (Sun, "Partly cloudy"),
        3 => (Cloud, "Cloudy"),
        45 or 48 => (Cloud, "Fog"),
        51 or 53 or 55 or 56 or 57 => (Cloud, "Drizzle"),
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => (Cloud, "Rain"),
        71 or 73 or 75 or 77 or 85 or 86 => (Cloud, "Snow"),
        95 or 96 or 99 => (Cloud, "Storm"),
        _ => (Cloud, "Weather")
    };

    private void Publish(WeatherInfo? info)
    {
        if (info == Current) return;
        Current = info;
        Changed?.Invoke(this, info);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _enabled = false;
        _refreshRequested = false;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _refreshCancellation?.Cancel();
        _http.Dispose();
    }
}
