using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.Services;

/// <summary>
/// Polls quotes for a few symbols (stocks or crypto like BTC-USD) from Yahoo Finance's public chart
/// endpoint — no API key. Best-effort: on any failure the symbol is skipped. Raises on the UI thread.
/// </summary>
public sealed class StocksService : IDisposable
{
    private readonly LoggingService _log;
    private readonly HttpClient _http;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(3) };
    private string[] _symbols = [];
    private Task? _refreshTask;
    private CancellationTokenSource? _refreshCancellation;
    private int _configurationGeneration;
    private bool _refreshRequested;
    private bool _started;
    private bool _enabled;
    private bool _disposed;

    public StocksService(LoggingService log) : this(log, CreateClient()) { }

    internal StocksService(LoggingService log, HttpMessageHandler handler) : this(log, CreateClient(handler)) { }

    private StocksService(LoggingService log, HttpClient http)
    {
        _log = log;
        _http = http;
    }

    public event EventHandler<IReadOnlyList<StockQuote>>? Changed;
    public IReadOnlyList<StockQuote> Current { get; private set; } = [];

    public void ConfigurePolling(TimeSpan interval)
    {
        var normalized = TimeSpan.FromMinutes(Math.Clamp(interval.TotalMinutes, 1, 60));
        if (_timer.Interval != normalized) _timer.Interval = normalized;
    }

    private static HttpClient CreateClient(HttpMessageHandler? handler = null)
    {
        var c = handler is null ? new HttpClient() : new HttpClient(handler);
        c.Timeout = TimeSpan.FromSeconds(15);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) DynamicIsland");
        return c;
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

    /// <summary>Enables or suspends quote polling while retaining the last published quotes.</summary>
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

    public void Configure(string symbolsCsv)
    {
        var symbols = (symbolsCsv ?? "")
            .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant()).Distinct().Take(6).ToArray();
        if (symbols.SequenceEqual(_symbols)) return;
        _symbols = symbols;
        _configurationGeneration++;
        _ = RequestRefresh(replaceInFlight: true);
    }

    /// <summary>Refreshes quotes when started and enabled; overlapping timer ticks are coalesced.</summary>
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
        // Yield in the loop before fetching so even an empty symbol list cannot complete before
        // _refreshTask is assigned by this caller.
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
                var symbols = _symbols;
                using var cancellation = new CancellationTokenSource();
                _refreshCancellation = cancellation;
                try
                {
                    var quotes = await FetchAsync(symbols, cancellation.Token);
                    if (!_disposed && _started && _enabled && generation == _configurationGeneration
                        && !cancellation.IsCancellationRequested)
                        Publish(quotes);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch (Exception ex) { _log.Debug($"Stock refresh failed: {ex.Message}"); }
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

    private async Task<IReadOnlyList<StockQuote>> FetchAsync(string[] symbols, CancellationToken cancellationToken)
    {
        if (symbols.Length == 0) return [];
        var quotes = new List<StockQuote>();
        foreach (var symbol in symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // 1-minute intraday points give a smooth sparkline; meta still carries the latest price.
                var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=5m&range=1d";
                using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, cancellationToken));
                var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
                var meta = result.GetProperty("meta");
                var price = meta.GetProperty("regularMarketPrice").GetDouble();
                var prev = meta.TryGetProperty("chartPreviousClose", out var cpc) ? cpc.GetDouble()
                    : meta.TryGetProperty("previousClose", out var pc) ? pc.GetDouble() : price;
                var change = prev > 0 ? (price - prev) / prev * 100 : 0;
                quotes.Add(new StockQuote(symbol, price, change, ExtractHistory(result)));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { _log.Debug($"Stock {symbol} fetch failed: {ex.Message}"); }
        }
        return quotes;
    }

    // Pulls the intraday close series (indicators.quote[0].close), dropping the nulls Yahoo returns for gaps.
    private static IReadOnlyList<double> ExtractHistory(JsonElement result)
    {
        try
        {
            var closes = result.GetProperty("indicators").GetProperty("quote")[0].GetProperty("close");
            var points = new List<double>(closes.GetArrayLength());
            foreach (var c in closes.EnumerateArray())
                if (c.ValueKind == JsonValueKind.Number) points.Add(c.GetDouble());
            return points;
        }
        catch { return []; }
    }

    private void Publish(IReadOnlyList<StockQuote> quotes)
    {
        if (quotes.SequenceEqual(Current)) return;
        Current = quotes;
        Changed?.Invoke(this, quotes);
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
