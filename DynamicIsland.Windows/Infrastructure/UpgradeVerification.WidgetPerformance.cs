using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using DynamicIsland.Windows.Services;
using System.Windows;
using Application = System.Windows.Application;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static readonly TimeSpan WidgetVerificationTimeout = TimeSpan.FromSeconds(5);

    internal static async Task VerifyWidgetPerformanceAsync(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        var dispatcher = Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
        if (!dispatcher.CheckAccess())
            throw new InvalidOperationException("Widget performance fixtures must run on the WPF dispatcher.");

        void Check(bool condition, string description)
        {
            check(condition, description);
            if (!condition) throw new InvalidOperationException(description);
        }

        var log = new LoggingService();
        await VerifyStocksAsync(log, Check);
        await VerifyWeatherAsync(log, Check);
    }

    private static async Task VerifyStocksAsync(LoggingService log, Action<bool, string> check)
    {
        var disabledHandler = new WidgetHttpHandler((_, _) => Task.FromResult(StockResponse()));
        using (var disabled = new StocksService(log, disabledHandler))
        {
            disabled.Configure("AAPL");
            disabled.Start();
            disabled.OnTimerTick(null, EventArgs.Empty);
            await disabled.RefreshAsync().WaitAsync(WidgetVerificationTimeout);
            check(disabledHandler.RequestCount == 0, "Disabled stocks performs no HTTP work on start, timer tick, or explicit refresh");
        }

        var concurrentGate = new AsyncGate();
        var oldConfigGate = new AsyncGate();
        var disableGate = new AsyncGate();
        var disposeGate = new AsyncGate();
        var handler = new WidgetHttpHandler(async (request, token) =>
        {
            var symbol = request.RequestUri!.AbsolutePath.Split('/').Last();
            var gate = symbol switch
            {
                "BLOCKED" => concurrentGate,
                "OLD" => oldConfigGate,
                "DISABLE" => disableGate,
                "DISPOSE" => disposeGate,
                _ => null
            };
            if (gate is not null) await gate.WaitAsync(token).ConfigureAwait(false);
            return StockResponse();
        });
        using var stocks = new StocksService(log, handler);
        var published = new List<string>();
        stocks.Changed += (_, quotes) => published.Add(string.Join(',', quotes.Select(q => q.Symbol)));
        stocks.Configure("BASE");
        stocks.Start();
        stocks.OnTimerTick(null, EventArgs.Empty);
        await stocks.RefreshAsync().WaitAsync(WidgetVerificationTimeout);
        check(handler.RequestCount == 0, "Stocks remains idle until enabled");

        stocks.SetEnabled(true);
        await stocks.RefreshAsync().WaitAsync(WidgetVerificationTimeout);
        check(handler.RequestCount == 1 && stocks.Current.Single().Symbol == "BASE", "Enabling stocks immediately fetches the configured symbol");

        stocks.Configure("BLOCKED");
        var blockedRequest = await handler.NextRequestAsync(uri => uri.Contains("/BLOCKED?", StringComparison.Ordinal));
        var inFlight = stocks.RefreshAsync();
        var overlapping = Enumerable.Range(0, 32).Select(_ => stocks.RefreshAsync()).ToArray();
        check(handler.MaxInFlight == 1, "Concurrent stocks refresh calls share one active HTTP request");
        concurrentGate.Release();
        await Task.WhenAll(overlapping.Append(inFlight)).WaitAsync(WidgetVerificationTimeout);
        check(handler.RequestCount == 2 && !blockedRequest.CancellationToken.IsCancellationRequested,
            "Thirty-two overlapping stocks refresh calls do not start duplicate requests");

        published.Clear();
        stocks.Configure("OLD");
        var oldRequest = await handler.NextRequestAsync(uri => uri.Contains("/OLD?", StringComparison.Ordinal));
        stocks.Configure("MIDDLE");
        stocks.Configure("LATEST");
        await stocks.RefreshAsync().WaitAsync(WidgetVerificationTimeout);
        check(oldRequest.CancellationToken.IsCancellationRequested, "A stock configuration burst cancels the obsolete request");
        check(stocks.Current.Single().Symbol == "LATEST"
              && published.SequenceEqual(["LATEST"])
              && !handler.RequestUris.Any(uri => uri.Contains("/MIDDLE?", StringComparison.Ordinal)),
            "A stock configuration burst fetches and publishes only its latest symbol set");

        published.Clear();
        stocks.Configure("DISABLE");
        var disableRequest = await handler.NextRequestAsync(uri => uri.Contains("/DISABLE?", StringComparison.Ordinal));
        var disableRefresh = stocks.RefreshAsync();
        var cachedQuotes = stocks.Current;
        stocks.SetEnabled(false);
        await disableRefresh.WaitAsync(WidgetVerificationTimeout);
        var requestsAtDisable = handler.RequestCount;
        stocks.OnTimerTick(null, EventArgs.Empty);
        await stocks.RefreshAsync().WaitAsync(WidgetVerificationTimeout);
        check(disableRequest.CancellationToken.IsCancellationRequested && ReferenceEquals(stocks.Current, cachedQuotes)
              && published.Count == 0 && handler.RequestCount == requestsAtDisable,
            "Disabling stocks cancels pending work, preserves cached quotes, and prevents late or timer refreshes");

        stocks.Configure("REENTRY");
        var requestsBeforeReenable = handler.RequestCount;
        stocks.SetEnabled(true);
        await stocks.RefreshAsync().WaitAsync(WidgetVerificationTimeout);
        check(handler.RequestCount == requestsBeforeReenable + 1 && stocks.Current.Single().Symbol == "REENTRY",
            "Re-enabling stocks immediately fetches the latest disabled-period configuration");

        published.Clear();
        stocks.Configure("DISPOSE");
        var disposeRequest = await handler.NextRequestAsync(uri => uri.Contains("/DISPOSE?", StringComparison.Ordinal));
        var disposeRefresh = stocks.RefreshAsync();
        var cachedBeforeDispose = stocks.Current;
        stocks.Dispose();
        await disposeRefresh.WaitAsync(WidgetVerificationTimeout);
        var requestsAtDispose = handler.RequestCount;
        stocks.OnTimerTick(null, EventArgs.Empty);
        await stocks.RefreshAsync().WaitAsync(WidgetVerificationTimeout);
        check(disposeRequest.CancellationToken.IsCancellationRequested && ReferenceEquals(stocks.Current, cachedBeforeDispose)
              && published.Count == 0 && handler.RequestCount == requestsAtDispose,
            "Disposing stocks cancels pending work and prevents subsequent requests or late publication");
    }

    private static async Task VerifyWeatherAsync(LoggingService log, Action<bool, string> check)
    {
        var disabledHandler = new WidgetHttpHandler((_, _) => Task.FromResult(WeatherResponse()));
        using (var disabled = new WeatherService(log, disabledHandler))
        {
            disabled.Configure("DisabledCity", fahrenheit: false);
            disabled.Start();
            disabled.OnTimerTick(null, EventArgs.Empty);
            await disabled.RefreshAsync().WaitAsync(WidgetVerificationTimeout);
            check(disabledHandler.RequestCount == 0, "Disabled weather performs no HTTP work on start, timer tick, or explicit refresh");
        }

        var concurrentGate = new AsyncGate();
        var oldConfigGate = new AsyncGate();
        var disableForecastGate = new AsyncGate();
        var disposeForecastGate = new AsyncGate();
        var handler = new WidgetHttpHandler(async (request, token) =>
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.Contains("/v1/search", StringComparison.Ordinal))
            {
                var city = QueryValue(uri, "name");
                if (city == "ConcurrentCity") await concurrentGate.WaitAsync(token).ConfigureAwait(false);
                if (city == "OldCity") await oldConfigGate.WaitAsync(token).ConfigureAwait(false);
                var latitude = LatitudeFor(city);
                return JsonResponse($"{{\"results\":[{{\"latitude\":{latitude.ToString(CultureInfo.InvariantCulture)},\"longitude\":-86.1,\"name\":\"{city}\"}}]}}");
            }

            if (uri.AbsolutePath.Contains("/v1/forecast", StringComparison.Ordinal))
            {
                var latitude = double.Parse(QueryValue(uri, "latitude"), CultureInfo.InvariantCulture);
                if (latitude == LatitudeFor("DisableCity")) await disableForecastGate.WaitAsync(token).ConfigureAwait(false);
                if (latitude == LatitudeFor("DisposeCity")) await disposeForecastGate.WaitAsync(token).ConfigureAwait(false);
                return WeatherResponse();
            }

            throw new InvalidOperationException("Unexpected weather fixture URI: " + uri);
        });
        using var weather = new WeatherService(log, handler);
        var published = new List<string>();
        weather.Changed += (_, info) => { if (info is not null) published.Add(info.City); };
        weather.Configure("BaseCity", fahrenheit: false);
        weather.Start();
        weather.OnTimerTick(null, EventArgs.Empty);
        await weather.RefreshAsync().WaitAsync(WidgetVerificationTimeout);
        check(handler.RequestCount == 0, "Weather remains idle until enabled");

        weather.SetEnabled(true);
        await weather.RefreshAsync().WaitAsync(WidgetVerificationTimeout);
        check(handler.RequestCount == 2 && weather.Current?.City == "BaseCity",
            "Enabling weather immediately geocodes and fetches the configured location");

        weather.Configure("ConcurrentCity", fahrenheit: false);
        var concurrentRequest = await handler.NextRequestAsync(uri => uri.Contains("name=ConcurrentCity", StringComparison.Ordinal));
        var inFlight = weather.RefreshAsync();
        var overlapping = Enumerable.Range(0, 32).Select(_ => weather.RefreshAsync()).ToArray();
        check(handler.MaxInFlight == 1, "Concurrent weather refresh calls share one active HTTP request");
        concurrentGate.Release();
        await Task.WhenAll(overlapping.Append(inFlight)).WaitAsync(WidgetVerificationTimeout);
        check(handler.MaxInFlight == 1 && weather.Current?.City == "ConcurrentCity"
              && !concurrentRequest.CancellationToken.IsCancellationRequested
              && handler.RequestUris.Count(uri => uri.Contains("name=ConcurrentCity", StringComparison.Ordinal)) == 1,
            "Thirty-two overlapping weather refresh calls finish one latest-location request");

        published.Clear();
        weather.Configure("OldCity", fahrenheit: false);
        var oldRequest = await handler.NextRequestAsync(uri => uri.Contains("name=OldCity", StringComparison.Ordinal));
        weather.Configure("MiddleCity", fahrenheit: false);
        weather.Configure("LatestCity", fahrenheit: false);
        await weather.RefreshAsync().WaitAsync(WidgetVerificationTimeout);
        check(oldRequest.CancellationToken.IsCancellationRequested, "A weather configuration burst cancels obsolete geocoding");
        check(weather.Current?.City == "LatestCity"
              && published.SequenceEqual(["LatestCity"])
              && !handler.RequestUris.Any(uri => uri.Contains("name=MiddleCity", StringComparison.Ordinal)),
            "A weather configuration burst fetches and publishes only its latest location");

        published.Clear();
        weather.Configure("DisableCity", fahrenheit: false);
        var disableGeocode = await handler.NextRequestAsync(uri => uri.Contains("name=DisableCity", StringComparison.Ordinal));
        var disableForecast = await handler.NextRequestAsync(uri => uri.Contains("/v1/forecast", StringComparison.Ordinal)
            && QueryValue(new Uri(uri), "latitude") == LatitudeFor("DisableCity").ToString(CultureInfo.InvariantCulture));
        var disableRefresh = weather.RefreshAsync();
        var cachedWeather = weather.Current;
        weather.SetEnabled(false);
        await disableRefresh.WaitAsync(WidgetVerificationTimeout);
        var requestsAtDisable = handler.RequestCount;
        weather.OnTimerTick(null, EventArgs.Empty);
        await weather.RefreshAsync().WaitAsync(WidgetVerificationTimeout);
        check(disableGeocode.Uri.Contains("name=DisableCity", StringComparison.Ordinal)
              && disableForecast.CancellationToken.IsCancellationRequested
              && ReferenceEquals(weather.Current, cachedWeather) && published.Count == 0
              && handler.RequestCount == requestsAtDisable,
            "Disabling weather cancels pending forecast work, preserves cached conditions, and prevents late or timer refreshes");

        weather.Configure("ReentryCity", fahrenheit: true);
        var requestsBeforeReenable = handler.RequestCount;
        weather.SetEnabled(true);
        await weather.RefreshAsync().WaitAsync(WidgetVerificationTimeout);
        check(handler.RequestCount == requestsBeforeReenable + 2 && weather.Current?.City == "ReentryCity"
              && handler.RequestUris.Last().Contains("temperature_unit=fahrenheit", StringComparison.Ordinal),
            "Re-enabling weather immediately fetches the latest disabled-period location and unit");

        published.Clear();
        weather.Configure("DisposeCity", fahrenheit: false);
        var disposeGeocode = await handler.NextRequestAsync(uri => uri.Contains("name=DisposeCity", StringComparison.Ordinal));
        var disposeForecast = await handler.NextRequestAsync(uri => uri.Contains("/v1/forecast", StringComparison.Ordinal)
            && QueryValue(new Uri(uri), "latitude") == LatitudeFor("DisposeCity").ToString(CultureInfo.InvariantCulture));
        var disposeRefresh = weather.RefreshAsync();
        var cachedBeforeDispose = weather.Current;
        weather.Dispose();
        await disposeRefresh.WaitAsync(WidgetVerificationTimeout);
        var requestsAtDispose = handler.RequestCount;
        weather.OnTimerTick(null, EventArgs.Empty);
        await weather.RefreshAsync().WaitAsync(WidgetVerificationTimeout);
        check(disposeGeocode.Uri.Contains("name=DisposeCity", StringComparison.Ordinal)
              && disposeForecast.CancellationToken.IsCancellationRequested
              && ReferenceEquals(weather.Current, cachedBeforeDispose)
              && published.Count == 0 && handler.RequestCount == requestsAtDispose,
            "Disposing weather cancels pending forecast work and prevents subsequent requests or late publication");
    }

    private static HttpResponseMessage StockResponse() => JsonResponse(
        "{\"chart\":{\"result\":[{\"meta\":{\"regularMarketPrice\":101.5,\"chartPreviousClose\":100},\"indicators\":{\"quote\":[{\"close\":[100,101,101.5]}]}}]}}");

    private static HttpResponseMessage WeatherResponse() => JsonResponse(
        "{\"current\":{\"temperature_2m\":21,\"weather_code\":0}}");

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string QueryValue(Uri uri, string key)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = pair.Split('=', 2);
            if (pieces.Length == 2 && string.Equals(pieces[0], key, StringComparison.Ordinal))
                return Uri.UnescapeDataString(pieces[1].Replace('+', ' '));
        }
        throw new InvalidOperationException($"Fixture request is missing query key '{key}': {uri}");
    }

    private static double LatitudeFor(string city) => city switch
    {
        "BaseCity" => 10,
        "ConcurrentCity" => 20,
        "OldCity" => 30,
        "MiddleCity" => 40,
        "LatestCity" => 50,
        "DisableCity" => 60,
        "ReentryCity" => 70,
        "DisposeCity" => 80,
        _ => 90
    };

    private sealed class AsyncGate
    {
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task WaitAsync(CancellationToken cancellationToken) => _release.Task.WaitAsync(cancellationToken);
        public void Release() => _release.TrySetResult(true);
    }

    private sealed record ObservedRequest(string Uri, CancellationToken CancellationToken);

    private sealed class WidgetHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        private readonly ConcurrentQueue<ObservedRequest> _observed = new();
        private readonly ConcurrentQueue<string> _requestUris = new();
        private readonly SemaphoreSlim _requestSignal = new(0);
        private int _requestCount;
        private int _activeRequests;
        private int _maxInFlight;

        public int RequestCount => Volatile.Read(ref _requestCount);
        public int MaxInFlight => Volatile.Read(ref _maxInFlight);
        public string[] RequestUris => _requestUris.ToArray();

        public async Task<ObservedRequest> NextRequestAsync(Func<string, bool> matches)
        {
            using var timeout = new CancellationTokenSource(WidgetVerificationTimeout);
            while (true)
            {
                await _requestSignal.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (!_observed.TryDequeue(out var request))
                    throw new InvalidOperationException("A fixture request signal had no matching request.");
                if (matches(request.Uri)) return request;
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _activeRequests);
            UpdateMaximum(active);
            Interlocked.Increment(ref _requestCount);
            var uri = request.RequestUri?.ToString() ?? "";
            _requestUris.Enqueue(uri);
            _observed.Enqueue(new ObservedRequest(uri, cancellationToken));
            _requestSignal.Release();
            try { return await respond(request, cancellationToken).ConfigureAwait(false); }
            finally { Interlocked.Decrement(ref _activeRequests); }
        }

        private void UpdateMaximum(int active)
        {
            var current = Volatile.Read(ref _maxInFlight);
            while (active > current)
            {
                var previous = Interlocked.CompareExchange(ref _maxInFlight, active, current);
                if (previous == current) return;
                current = previous;
            }
        }
    }
}
