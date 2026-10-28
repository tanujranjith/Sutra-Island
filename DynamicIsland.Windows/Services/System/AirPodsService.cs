using System.Runtime.InteropServices.WindowsRuntime;
using System.Globalization;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using DynamicIsland.Windows.Interop;
using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.Services;

/// <summary>
/// Native AirPods BLE service. Owns watcher lifecycle, paired-device discovery,
/// protocol parsing, state aggregation and freshness handling.
/// </summary>
public sealed class AirPodsService : IDisposable
{
    private readonly LoggingService _log;
    private readonly object _gate = new();
    private AirPodsState _current = AirPodsState.Unavailable;
    private BluetoothLEAdvertisementWatcher? _watcher;
    private bool _running;
    private bool _isBluetoothAvailable = true;
    private string? _deviceName;
    private AirPodsModel _boundModel = AirPodsModel.Unknown;
    private bool _hasPairedConnectedAirPods;
    private int _connectedAirPodsCount;
    private int _pairedDisconnectPolls;
    private short _rssiMin = -75;
    private int _lifecycleGeneration;
    private readonly SemaphoreSlim _pairedPollGate = new(1, 1);

    private sealed class CachedAdv
    {
        public AirPodsParser.ParsedAdvertisement Parsed = null!;
        public DateTimeOffset Timestamp;
        public short Rssi;
        public ulong Address;
    }

    private CachedAdv? _left;
    private CachedAdv? _right;

    private System.Threading.Timer? _lostTimer;
    private System.Threading.Timer? _leftTimer;
    private System.Threading.Timer? _rightTimer;
    private System.Threading.Timer? _pairedPollTimer;
    private System.Threading.Timer? _retryTimer;
    private TimeSpan _pairedPollInterval = TimeSpan.FromSeconds(15);

    public AirPodsState Current
    {
        get { lock (_gate) return _current; }
    }

    public event EventHandler<AirPodsState>? Changed;

    public void ConfigurePolling(TimeSpan interval)
    {
        var normalized = TimeSpan.FromSeconds(Math.Clamp(interval.TotalSeconds, 1, 120));
        lock (_gate)
        {
            if (_pairedPollInterval == normalized) return;
            _pairedPollInterval = normalized;
        }
        try { _pairedPollTimer?.Change(TimeSpan.Zero, normalized); } catch (ObjectDisposedException) { }
    }

    public AirPodsService(LoggingService log)
    {
        _log = log;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_running) return;
            _running = true;
            _lifecycleGeneration++;
        }

        _lostTimer = new System.Threading.Timer(_ => OnLost(), null, Timeout.Infinite, Timeout.Infinite);
        _leftTimer = new System.Threading.Timer(_ => OnSideReset(AirPodsSide.Left), null, Timeout.Infinite, Timeout.Infinite);
        _rightTimer = new System.Threading.Timer(_ => OnSideReset(AirPodsSide.Right), null, Timeout.Infinite, Timeout.Infinite);
        TimeSpan pairedPollInterval;
        lock (_gate) pairedPollInterval = _pairedPollInterval;
        _pairedPollTimer = new System.Threading.Timer(_ => _ = RefreshPairedAsync(), null, TimeSpan.Zero, pairedPollInterval);
        _retryTimer = new System.Threading.Timer(_ => TryRestartWatcher(), null, Timeout.Infinite, Timeout.Infinite);

        TryStartWatcher();

        _log.Info("AirPodsService started");
    }

    public void Stop()
    {
        System.Threading.Timer? lost, left, right, poll, retry;
        BluetoothLEAdvertisementWatcher? watcher;
        AirPodsState? stoppedState = null;
        lock (_gate)
        {
            if (!_running) return;
            _running = false;
            _lifecycleGeneration++;
            watcher = _watcher;
            _watcher = null;
            lost = _lostTimer; _lostTimer = null;
            left = _leftTimer; _leftTimer = null;
            right = _rightTimer; _rightTimer = null;
            poll = _pairedPollTimer; _pairedPollTimer = null;
            retry = _retryTimer; _retryTimer = null;
        }

        try
        {
            if (watcher != null)
            {
                watcher.Received -= OnWatcherReceived;
                watcher.Stopped -= OnWatcherStopped;
                try { watcher.Stop(); } catch { }
            }
        }
        catch { }

        lost?.Dispose();
        left?.Dispose();
        right?.Dispose();
        poll?.Dispose();
        retry?.Dispose();

        lock (_gate)
        {
            _left = null;
            _right = null;
            _hasPairedConnectedAirPods = false;
            _connectedAirPodsCount = 0;
            _pairedDisconnectPolls = 0;
            var next = AirPodsState.Disconnected(_isBluetoothAvailable);
            var changed = _current.HasPresentationChangedFrom(next);
            _current = next;
            if (changed) stoppedState = next;
        }
        if (stoppedState is not null) SafeRaise(stoppedState);
        _log.Info("AirPodsService stopped");
    }

    public void Dispose()
    {
        Stop();
    }

    private void TryStartWatcher()
    {
        lock (_gate)
        {
            if (!_running) return;
            if (_watcher != null) return;
        }

        BluetoothLEAdvertisementWatcher? watcher = null;
        try
        {
            watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Passive
            };

            // Filter to Apple company identifier is not directly supported via watcher filter on older builds,
            // so we filter manually in Received.
            watcher.Received += OnWatcherReceived;
            watcher.Stopped += OnWatcherStopped;

            lock (_gate)
            {
                if (!_running)
                {
                    watcher.Received -= OnWatcherReceived;
                    watcher.Stopped -= OnWatcherStopped;
                    return;
                }
                _watcher = watcher;
            }

            watcher.Start();
            lock (_gate) _isBluetoothAvailable = true;
            _log.Info("AirPods BLE watcher started");
            UpdateAvailabilityLocked(true);
        }
        catch (Exception ex)
        {
            try
            {
                if (watcher != null)
                {
                    watcher.Received -= OnWatcherReceived;
                    watcher.Stopped -= OnWatcherStopped;
                    try { watcher.Stop(); } catch { }
                }
            }
            catch { }
            lock (_gate)
            {
                if (ReferenceEquals(_watcher, watcher)) _watcher = null;
            }
            lock (_gate) _isBluetoothAvailable = false;
            _log.Error("AirPods BLE watcher start failed", ex);
            UpdateAvailabilityLocked(false);
            ScheduleRetry();
        }
    }

    private void TryRestartWatcher()
    {
        lock (_gate)
        {
            if (!_running) return;
            if (_watcher != null) return; // already running
        }
        TryStartWatcher();
    }

    private void ScheduleRetry()
    {
        try { _retryTimer?.Change(TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan); } catch { }
    }

    private void OnWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        string? err = null;
        try { err = args.Error.ToString(); } catch { }
        var status = sender.Status;
        bool shouldRetry = false;
        AirPodsState? unavailableState = null;
        lock (_gate)
        {
            if (!ReferenceEquals(_watcher, sender)) return;
            // If we are still supposed to be running, treat stopped as unavailable.
            if (_running)
            {
                _isBluetoothAvailable = false;
                _watcher = null;
                shouldRetry = true;
            }
        }

        try
        {
            sender.Received -= OnWatcherReceived;
            sender.Stopped -= OnWatcherStopped;
        }
        catch { }

        _log.Info($"AirPods BLE watcher stopped status={status} error={err ?? "none"}");

        // Update state to unavailable
        lock (_gate)
        {
            var next = AirPodsState.Unavailable;
            // Preserve IsAvailable false
            if (_current.HasPresentationChangedFrom(AirPodsState.Unavailable))
            {
                unavailableState = next;
            }
            _current = next;
            // Clear caches
            _left = null;
            _right = null;
            try { _lostTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
            try { _leftTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
            try { _rightTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
        }

        if (unavailableState is not null) SafeRaise(unavailableState);

        if (shouldRetry) ScheduleRetry();
    }

    private void OnWatcherReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        try
        {
            short rssi = args.RawSignalStrengthInDBm;
            ulong address = args.BluetoothAddress;
            var timestamp = args.Timestamp; // DateTimeOffset
            // Manufacturer data iteration
            byte[]? applePayload = null;
            foreach (var md in args.Advertisement.ManufacturerData)
            {
                if (md.CompanyId != AirPodsParser.AppleCompanyId) continue;
                var data = md.Data;
                if (data == null || data.Length != 27) continue;
                // Convert IBuffer to byte[]
                var bytes = new byte[data.Length];
                using (var reader = DataReader.FromBuffer(data))
                {
                    reader.ReadBytes(bytes);
                }
                applePayload = bytes;
                break;
            }
            if (applePayload == null) return;
            if (!AirPodsParser.TryParse(applePayload, out var parsed) || parsed == null) return;

            // Do not log raw payload or address
            // Use hashed address for trace if needed
            // _log.Debug($"AirPods adv hash={HashAddress(address):X} rssi={rssi}");

            ProcessAdvertisement(parsed, rssi, address, timestamp);
        }
        catch (Exception ex)
        {
            try { _log.Debug($"AirPods adv handling error: {ex.Message}"); } catch { }
        }
    }

    // For tests / direct injection without watcher
    public bool TryProcessPayload(byte[] payload, short rssi, ulong address, DateTimeOffset timestamp)
    {
        try
        {
            if (!AirPodsParser.TryParse(payload, out var parsed) || parsed == null) return false;
            return ProcessAdvertisement(parsed, rssi, address, timestamp);
        }
        catch { return false; }
    }

    private bool ProcessAdvertisement(AirPodsParser.ParsedAdvertisement parsed, short rssi, ulong address, DateTimeOffset timestamp)
    {
        // Quick RSSI pre-check outside lock? Do inside for consistency.
        AirPodsState? toRaise = null;
        lock (_gate)
        {
            if (!_running) return false;
            if (_isBluetoothAvailable == false)
            {
                _isBluetoothAvailable = true;
                // will be reflected in next state
            }

            if (!IsPossibleDesiredAdv(parsed, rssi, address))
            {
                // _log.Debug($"AirPods adv rejected hash={HashAddress(address):X} rssi={rssi}");
                return false;
            }

            // Update cache
            var cached = new CachedAdv { Parsed = parsed, Rssi = rssi, Address = address, Timestamp = timestamp };
            if (parsed.BroadcastSide == AirPodsSide.Left)
            {
                _left = cached;
                try { _leftTimer?.Change(TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan); } catch { }
            }
            else
            {
                _right = cached;
                try { _rightTimer?.Change(TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan); } catch { }
            }
            try { _lostTimer?.Change(TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan); } catch { }

            var newState = MergeStateLocked();
            if (!newState.HasPresentationChangedFrom(_current))
            {
                // Keep freshness and RSSI current without waking the UI for every packet.
                _current = newState;
                return false;
            }
            _current = newState;
            toRaise = newState;
        }

        // Raise outside the state lock so UI handlers cannot block BLE processing.
        SafeRaise(toRaise);
        return true;
    }

    private bool IsPossibleDesiredAdv(AirPodsParser.ParsedAdvertisement parsed, short rssi, ulong address)
    {
        if (rssi < _rssiMin) return false;

        // Require at least one paired connected AirPods device to avoid showing neighbor's.
        // If enumeration has not yet succeeded, we treat _hasPairedConnectedAirPods as false,
        // which will block. To avoid permanently blocking when enumeration is permanently failing
        // (e.g., no Bluetooth), we allow processing if watcher is available but poll hasn't completed yet?
        // We do a grace period: if _pairedPollTimer hasn't run yet, allow processing to avoid deadlock in tests.
        // For tests, _hasPairedConnectedAirPods may be false but we still want to accept test packets.
        // We'll allow if _pairedPollTimer is null (tests) — otherwise require paired.
        bool requirePaired = _pairedPollTimer != null;
        if (requirePaired && (!_hasPairedConnectedAirPods || _connectedAirPodsCount != 1))
        {
            return false;
        }

        // The classic paired endpoint and continuity advertisements do not always share
        // a stable BLE address. The uniquely connected paired device and model checks
        // above remain the binding; rejecting the rotating BLE address loses valid battery packets.

        if (_boundModel != AirPodsModel.Unknown && parsed.Model != AirPodsModel.Unknown && parsed.Model != _boundModel)
            return false;

        CachedAdv? left = _left;
        CachedAdv? right = _right;
        CachedAdv? lastSameSide = parsed.BroadcastSide == AirPodsSide.Left ? left : right;
        CachedAdv? otherSide = parsed.BroadcastSide == AirPodsSide.Left ? right : left;

        bool HasDifferentModel(CachedAdv? cached)
        {
            if (cached == null) return false;
            var cm = cached.Parsed.Model;
            var pm = parsed.Model;
            return cm != AirPodsModel.Unknown && pm != AirPodsModel.Unknown && cm != pm;
        }

        if (HasDifferentModel(lastSameSide) || HasDifferentModel(otherSide))
            return false;

        if (lastSameSide != null && lastSameSide.Address != address)
        {
            int leftDiff = 0, rightDiff = 0, caseDiff = 0;
            if (parsed.LeftBattery.HasValue && lastSameSide.Parsed.LeftBattery.HasValue)
                leftDiff = Math.Abs(parsed.LeftBattery.Value - lastSameSide.Parsed.LeftBattery.Value);
            if (parsed.RightBattery.HasValue && lastSameSide.Parsed.RightBattery.HasValue)
                rightDiff = Math.Abs(parsed.RightBattery.Value - lastSameSide.Parsed.RightBattery.Value);
            if (parsed.CaseBattery.HasValue && lastSameSide.Parsed.CaseBattery.HasValue)
                caseDiff = Math.Abs(parsed.CaseBattery.Value - lastSameSide.Parsed.CaseBattery.Value);

            // Battery values are 0-100 step 10, raw diff 1 ==10%. Threshold 10% (allow small drift)
            if (leftDiff > 10 || rightDiff > 10 || caseDiff > 10)
                return false;

            short rssiDiff = (short)Math.Abs(rssi - lastSameSide.Rssi);
            if (rssiDiff > 50) return false;
        }

        if (otherSide != null)
        {
            short rssiDiff = (short)Math.Abs(rssi - otherSide.Rssi);
            if (rssiDiff > 50) return false;
        }

        return true;
    }

    private AirPodsState MergeStateLocked()
    {
        if (_left == null && _right == null)
            return AirPodsConnectionPolicy.AdvertisementExpiredState(
                _current, _isBluetoothAvailable, _hasPairedConnectedAirPods);

        // Helper to pick model
        AirPodsModel PickModel()
        {
            bool leftAvail = _left != null && _left.Parsed.Model != AirPodsModel.Unknown;
            bool rightAvail = _right != null && _right.Parsed.Model != AirPodsModel.Unknown;
            if (leftAvail && rightAvail)
                return _left!.Timestamp > _right!.Timestamp ? _left.Parsed.Model : _right.Parsed.Model;
            if (leftAvail) return _left!.Parsed.Model;
            if (rightAvail) return _right!.Parsed.Model;
            // If both unknown, pick newest
            if (_left != null && _right != null)
                return _left.Timestamp > _right.Timestamp ? _left.Parsed.Model : _right.Parsed.Model;
            if (_left != null) return _left.Parsed.Model;
            if (_right != null) return _right.Parsed.Model;
            return AirPodsModel.Unknown;
        }

        var model = PickModel();
        var modelName = AirPodsState.GetModelName(model);

        // Helpers for picking per-pod
        (int? batt, bool charging, bool inEar) PickLeft()
        {
            bool leftAvail = _left?.Parsed.LeftBattery.HasValue == true;
            bool rightAvail = _right?.Parsed.LeftBattery.HasValue == true;
            CachedAdv? chosen;
            if (leftAvail && rightAvail) chosen = _left!.Timestamp > _right!.Timestamp ? _left : _right;
            else if (leftAvail) chosen = _left;
            else if (rightAvail) chosen = _right;
            else
            {
                if (_left != null && _right != null) chosen = _left.Timestamp > _right.Timestamp ? _left : _right;
                else chosen = _left ?? _right;
            }
            if (chosen == null) return (null, false, false);
            return (chosen.Parsed.LeftBattery, chosen.Parsed.LeftCharging, chosen.Parsed.LeftInEar);
        }

        (int? batt, bool charging, bool inEar) PickRight()
        {
            bool leftAvail = _left?.Parsed.RightBattery.HasValue == true;
            bool rightAvail = _right?.Parsed.RightBattery.HasValue == true;
            CachedAdv? chosen;
            if (leftAvail && rightAvail) chosen = _left!.Timestamp > _right!.Timestamp ? _left : _right;
            else if (leftAvail) chosen = _left;
            else if (rightAvail) chosen = _right;
            else
            {
                if (_left != null && _right != null) chosen = _left.Timestamp > _right.Timestamp ? _left : _right;
                else chosen = _left ?? _right;
            }
            if (chosen == null) return (null, false, false);
            return (chosen.Parsed.RightBattery, chosen.Parsed.RightCharging, chosen.Parsed.RightInEar);
        }

        (int? batt, bool charging, bool bothInCase, bool lidOpen) PickCase()
        {
            bool leftAvail = _left?.Parsed.CaseBattery.HasValue == true;
            bool rightAvail = _right?.Parsed.CaseBattery.HasValue == true;
            CachedAdv? chosen;
            if (leftAvail && rightAvail) chosen = _left!.Timestamp > _right!.Timestamp ? _left : _right;
            else if (leftAvail) chosen = _left;
            else if (rightAvail) chosen = _right;
            else
            {
                if (_left != null && _right != null) chosen = _left.Timestamp > _right.Timestamp ? _left : _right;
                else chosen = _left ?? _right;
            }
            if (chosen == null) return (null, false, false, false);
            return (chosen.Parsed.CaseBattery, chosen.Parsed.CaseCharging, chosen.Parsed.BothInCase, chosen.Parsed.CaseLidOpen);
        }

        var (leftBatt, leftCharging, leftInEar) = PickLeft();
        var (rightBatt, rightCharging, rightInEar) = PickRight();
        var (caseBatt, caseCharging, bothInCase, lidOpen) = PickCase();

        // Rssi and timestamp: latest
        short rssi = 0;
        DateTimeOffset ts = DateTimeOffset.MinValue;
        if (_left != null && _right != null)
        {
            if (_left.Timestamp > _right.Timestamp) { rssi = _left.Rssi; ts = _left.Timestamp; }
            else { rssi = _right.Rssi; ts = _right.Timestamp; }
        }
        else if (_left != null) { rssi = _left.Rssi; ts = _left.Timestamp; }
        else if (_right != null) { rssi = _right.Rssi; ts = _right.Timestamp; }

        return new AirPodsState
        {
            IsAvailable = _isBluetoothAvailable,
            IsConnected = true,
            Model = model,
            ModelName = modelName,
            DeviceName = _deviceName,
            LeftBatteryPercent = leftBatt,
            RightBatteryPercent = rightBatt,
            CaseBatteryPercent = caseBatt,
            LeftCharging = leftCharging,
            RightCharging = rightCharging,
            CaseCharging = caseCharging,
            LeftInEar = leftInEar,
            RightInEar = rightInEar,
            BothInCase = bothInCase,
            CaseLidOpen = lidOpen,
            Rssi = rssi,
            LastUpdated = ts
        };
    }

    private void OnLost()
    {
        AirPodsState? toRaise = null;
        lock (_gate)
        {
            if (!_running) return;
            if (_left == null && _right == null) return;
            var latest = _left is null ? _right!.Timestamp
                : _right is null ? _left.Timestamp
                : (_left.Timestamp > _right.Timestamp ? _left.Timestamp : _right.Timestamp);
            var age = DateTimeOffset.Now - latest;
            if (age < TimeSpan.FromSeconds(10))
            {
                try { _lostTimer?.Change(TimeSpan.FromSeconds(Math.Max(1, 10 - age.TotalSeconds)), Timeout.InfiniteTimeSpan); } catch { }
                return;
            }
            _left = null;
            _right = null;
            try { _leftTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
            try { _rightTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
            var next = AirPodsConnectionPolicy.AdvertisementExpiredState(
                _current, _isBluetoothAvailable, _hasPairedConnectedAirPods);
            if (!next.HasPresentationChangedFrom(_current))
            {
                _current = next;
                return;
            }
            _current = next;
            toRaise = next;
        }
        if (toRaise != null) SafeRaise(toRaise);
        _log.Info("AirPods state lost (stale)");
    }

    private void OnSideReset(AirPodsSide side)
    {
        AirPodsState? toRaise = null;
        lock (_gate)
        {
            if (!_running) return;
            var cached = side == AirPodsSide.Left ? _left : _right;
            if (cached is null) return;
            var age = DateTimeOffset.Now - cached.Timestamp;
            if (age < TimeSpan.FromSeconds(10))
            {
                try
                {
                    (side == AirPodsSide.Left ? _leftTimer : _rightTimer)?.Change(
                        TimeSpan.FromSeconds(Math.Max(1, 10 - age.TotalSeconds)), Timeout.InfiniteTimeSpan);
                }
                catch { }
                return;
            }
            if (side == AirPodsSide.Left) _left = null; else _right = null;
            var next = MergeStateLocked();
            // If both cleared, Update via OnLost path would have already set disconnected,
            // but MergeState will also return disconnected.
            if (!next.HasPresentationChangedFrom(_current))
            {
                _current = next;
                return;
            }
            _current = next;
            toRaise = next;
        }
        if (toRaise != null) SafeRaise(toRaise);
        _log.Info($"AirPods side reset {side}");
    }

    private void UpdateAvailabilityLocked(bool available)
    {
        AirPodsState? toRaise = null;
        lock (_gate)
        {
            _isBluetoothAvailable = available;
            if (!available)
            {
                var next = AirPodsState.Unavailable;
                if (!next.Equals(_current))
                {
                    _current = next;
                    toRaise = next;
                }
                _left = null;
                _right = null;
            }
            else
            {
                // When becoming available, if no cache, set disconnected state
                if (!_current.IsAvailable)
                {
                    var next = AirPodsState.Disconnected(true);
                    if (!next.Equals(_current))
                    {
                        _current = next;
                        toRaise = next;
                    }
                }
            }
        }
        if (toRaise != null) SafeRaise(toRaise);
    }

    private void SafeRaise(AirPodsState state)
    {
        try { Changed?.Invoke(this, state); }
        catch (Exception ex) { try { _log.Debug($"AirPods Changed handler error: {ex.Message}"); } catch { } }
    }

    private async Task RefreshPairedAsync()
    {
        if (!await _pairedPollGate.WaitAsync(0).ConfigureAwait(false)) return;
        int generation;
        try
        {
            lock (_gate)
            {
                if (!_running) return;
                generation = _lifecycleGeneration;
            }
            string selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var infos = await DeviceInformation.FindAllAsync(selector, new[]
            {
                "System.Devices.Aep.IsConnected",
                "System.Devices.Aep.DeviceAddress",
                "System.DeviceInterface.Bluetooth.VendorId",
                "System.DeviceInterface.Bluetooth.ProductId"
            });
            // Windows can leave the WinRT Bluetooth connection flag stale while the
            // classic A2DP render endpoint is actively connected.
            bool activeAirPodsAudioEndpoint = false;
            try
            {
                activeAirPodsAudioEndpoint = CoreAudioFactory.GetRenderEndpoints()
                    .Any(endpoint => IsAirPodsName(endpoint.Name));
            }
            catch { }
            bool foundConnected = false;
            int connectedCount = 0;
            string? foundName = null;
            AirPodsModel foundModel = AirPodsModel.Unknown;
            ulong? foundAddress = null;

            foreach (var info in infos)
            {
                string name = info.Name ?? string.Empty;
                if (!IsAirPodsName(name)) continue;
                bool isConnected = false;
                if (info.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var val) && val is bool b)
                    isConnected = b;
                else
                {
                    // Fallback: try to query BluetoothDevice directly for connection status
                    try
                    {
                        var dev = await BluetoothDevice.FromIdAsync(info.Id);
                        if (dev != null)
                            isConnected = dev.ConnectionStatus == BluetoothConnectionStatus.Connected;
                    }
                    catch { }
                }
                isConnected |= activeAirPodsAudioEndpoint;

                if (isConnected)
                {
                    connectedCount++;
                    if (connectedCount == 1)
                    {
                        foundConnected = true;
                        foundName = name;
                        foundModel = ModelFromProperties(info) ?? AirPodsModel.Unknown;
                        if (info.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var addressValue) &&
                            TryBluetoothAddress(addressValue, out var address))
                        {
                            foundAddress = address;
                        }
                        else if (TryBluetoothAddressFromDeviceId(info.Id, out var idAddress))
                        {
                            // Some Windows builds omit DeviceAddress from the property set,
                            // but retain the same address in the paired endpoint id.
                            foundAddress = idAddress;
                        }
                    }
                }
            }

            AirPodsState? toRaise = null;
            lock (_gate)
            {
                if (!_running || generation != _lifecycleGeneration) return;
                var ambiguous = connectedCount > 1;
                bool prevConnected = _hasPairedConnectedAirPods;
                _connectedAirPodsCount = connectedCount;
                var observedConnected = foundConnected && !ambiguous && foundAddress.HasValue;
                _hasPairedConnectedAirPods = AirPodsConnectionPolicy.ResolvePairedConnection(
                    prevConnected, observedConnected, ref _pairedDisconnectPolls);
                if (_hasPairedConnectedAirPods && !string.IsNullOrWhiteSpace(foundName))
                    _deviceName = StripFindMySuffix(foundName!);
                else if (!_hasPairedConnectedAirPods)
                {
                    _deviceName = null;
                    _boundModel = AirPodsModel.Unknown;
                }

                if (foundModel != AirPodsModel.Unknown)
                    _boundModel = foundModel;

                if (prevConnected && !_hasPairedConnectedAirPods)
                {
                    // Lost the uniquely identified bound device, or could no longer
                    // identify it safely, so do not retain stale advertisements.
                    _left = null;
                    _right = null;
                    try { _lostTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
                    try { _leftTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
                    try { _rightTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
                    var next = AirPodsState.Disconnected(_isBluetoothAvailable);
                    if (!next.Equals(_current))
                    {
                        _current = next;
                        toRaise = next;
                    }
                }
                else if (_hasPairedConnectedAirPods)
                {
                    // A connected audio endpoint can stop sending continuity packets.
                    // Publish connection metadata independently so the UI still reflects
                    // the Windows Bluetooth connection; battery fields remain unavailable.
                    var model = foundModel != AirPodsModel.Unknown ? foundModel : _current.Model;
                    var next = _current with
                    {
                        IsAvailable = _isBluetoothAvailable,
                        IsConnected = true,
                        Model = model,
                        ModelName = AirPodsState.GetModelName(model),
                        DeviceName = _deviceName,
                        LastUpdated = _current.IsConnected ? _current.LastUpdated : DateTimeOffset.Now
                    };
                    if (next.HasPresentationChangedFrom(_current))
                    {
                        _current = next;
                        toRaise = next;
                    }

                    if (!prevConnected)
                        _log.Info($"AirPods paired device connected: {RedactName(foundName)}");
                }
            }
            if (toRaise != null) SafeRaise(toRaise);
        }
        catch (Exception ex)
        {
            try { _log.Debug($"AirPods paired poll failed: {ex.Message}"); } catch { }
        }
        finally
        {
            _pairedPollGate.Release();
        }
    }

    private static AirPodsModel? ModelFromProperties(DeviceInformation info)
    {
        if (!info.Properties.TryGetValue("System.DeviceInterface.Bluetooth.VendorId", out var vendor) ||
            !TryUInt16(vendor, out var vendorId) || vendorId != AirPodsParser.AppleCompanyId)
            return null;
        if (!info.Properties.TryGetValue("System.DeviceInterface.Bluetooth.ProductId", out var product) ||
            !TryUInt16(product, out var productId)) return null;
        return AirPodsParser.GetModel(productId);
    }

    private static bool TryUInt16(object? value, out ushort result)
    {
        try
        {
            result = value switch
            {
                ushort v => v,
                short v when v >= 0 => (ushort)v,
                uint v when v <= ushort.MaxValue => (ushort)v,
                int v when v >= 0 && v <= ushort.MaxValue => (ushort)v,
                string s when ushort.TryParse(s, out var v) => v,
                _ => 0
            };
            return result != 0;
        }
        catch
        {
            result = 0;
            return false;
        }
    }

    private static bool TryBluetoothAddress(object? value, out ulong address)
    {
        address = 0;
        if (value is ulong numeric)
        {
            address = numeric;
            return address != 0;
        }

        if (value is long signed && signed >= 0)
        {
            address = (ulong)signed;
            return address != 0;
        }

        if (value is not string text) return false;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        text = text.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        return ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address) && address != 0;
    }

    private static bool TryBluetoothAddressFromDeviceId(string? deviceId, out ulong address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(deviceId)) return false;

        foreach (var marker in new[] { "DEV_", "BLUETOOTHDEVICE_" })
        {
            int start = deviceId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0) continue;
            start += marker.Length;
            if (start + 12 > deviceId.Length) continue;
            var token = deviceId.Substring(start, 12);
            if (TryBluetoothAddress(token, out address)) return true;
        }

        return false;
    }

    private static bool IsAirPodsName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return name.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Beats", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("AirPod", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripFindMySuffix(string name)
    {
        const string suffix = " - Find My";
        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return name[..^suffix.Length].Trim();
        return name.Trim();
    }

    private static string RedactName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "<unknown>";
        // Hash for privacy
        int hash = name.GetHashCode();
        return $"hash:{hash:X8}";
    }

    private static AirPodsModel InferModelFromName(string name)
    {
        // Very light heuristic for boundModel filtering. Unknown fallback allows any model.
        var n = name.ToLowerInvariant();
        if (n.Contains("max")) return AirPodsModel.AirPodsMax;
        if (n.Contains("pro"))
        {
            // Could be Pro 2 vs Pro 3 etc, but we return generic Pro for filtering only if obvious
            // To avoid false mismatch, return Unknown unless exact version is clear.
            if (n.Contains("pro 3") || n.Contains("pro3")) return AirPodsModel.AirPodsPro3;
            if (n.Contains("pro 2") || n.Contains("pro2")) return AirPodsModel.AirPodsPro2;
            return AirPodsModel.AirPodsPro;
        }
        if (n.Contains("fit pro")) return AirPodsModel.BeatsFitPro;
        if (n.Contains("airpods 4") && n.Contains("anc")) return AirPodsModel.AirPods4Anc;
        if (n.Contains("airpods 4")) return AirPodsModel.AirPods4;
        if (n.Contains("airpods 3")) return AirPodsModel.AirPods3;
        if (n.Contains("airpods 2")) return AirPodsModel.AirPods2;
        if (n.Contains("airpods")) return AirPodsModel.Unknown; // generic, don't filter
        return AirPodsModel.Unknown;
    }

    // For testing without paired requirement: allow injection
    internal void SetTestPairedState(bool hasPaired, string? deviceName = null, AirPodsModel boundModel = AirPodsModel.Unknown)
    {
        lock (_gate)
        {
            _hasPairedConnectedAirPods = hasPaired;
            _deviceName = deviceName;
            _boundModel = boundModel;
        }
    }

    internal static int HashAddress(ulong addr) => (int)(addr ^ (addr >> 32));
}
