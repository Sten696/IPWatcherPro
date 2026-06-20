using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using IPWatcherPro.Core;
using IPWatcherPro.Infrastructure;

namespace IPWatcherPro.Services;

public sealed class GeoLeakMonitor : IDisposable
{
    private readonly AppConfiguration _config;
    private readonly NetworkEventHub _hub;
    private readonly JsonLinesLogger _logger;
    private readonly ProcessResolver _processResolver;
    private readonly HunterActivityStore? _activity;

    private static readonly HttpClient s_http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(8),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private CancellationTokenSource? _cts;
    private Task? _scanTask;
    private readonly SemaphoreSlim _startLock = new(1, 1);

    private readonly SemaphoreSlim _geoLimiter = new(1, 1);
    private DateTime _lastGeoRequestUtc = DateTime.MinValue;

    private bool _disposed;

    private const int GeoLruMax = 1024;
    private const int GeoTtlMs = 3_600_000;
    private const int ScanIntervalMs = 5_000;

    private static readonly TimeSpan GeoRequestInterval = TimeSpan.FromSeconds(3);

    private readonly ConcurrentDictionary<string, (string? Code, string? Name, long Expiry)> _geoCache = new();
    private readonly LinkedList<string> _geoLru = new();
    private readonly object _geoLock = new();

    private readonly ConcurrentDictionary<string, bool> _reported = new(StringComparer.Ordinal);

    private static readonly string[] VpnNameKeywords =
    [
        "vpn", "wireguard", "tap", "openvpn", "nordvpn", "expressvpn",
        "tunnel", "tun", "proton", "mullvad"
    ];

    private static readonly HashSet<string> BaseMarkerPrefixes = new(StringComparer.Ordinal)
    {
        "104.16.", "104.17.", "104.18.", "104.19.", "104.20.", "104.21.",
        "172.64.", "172.65.", "172.66.", "172.67.", "172.68.", "172.69.",
        "172.70.", "172.71.", "23.235.", "23.236.", "23.237.", "23.238.", "23.239.",
        "104.131.", "104.236.", "138.197.", "139.59.", "172.104.", "172.105.",
        "45.63.", "45.76.", "45.77."
    };

    private static readonly HashSet<string> ExtendedMarkerPrefixes = new(StringComparer.Ordinal)
    {
        "54.80.", "54.81.", "54.82.", "54.83.", "54.84.", "54.85.",
        "52.0.", "52.1.", "52.2.", "52.3.", "52.4.", "52.5.",
        "40.76.", "40.77.", "40.78.", "40.79.", "40.80.",
        "34.64.", "34.65.", "34.80.", "34.81.", "34.82.",
        "91.121.", "94.23.", "158.69.", "95.217.", "116.202.", "116.203.", "116.204.",
        "199.249.", "185.220."
    };

    public GeoLeakMonitor(
        AppConfiguration config,
        NetworkEventHub hub,
        JsonLinesLogger logger,
        ProcessResolver processResolver,
        HunterActivityStore? activity = null)
    {
        _config = config;
        _hub = hub;
        _logger = logger;
        _processResolver = processResolver;
        _activity = activity;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(GeoLeakMonitor));

        await _startLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (_scanTask is { IsCompleted: false })
                return;

            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            _logger.Write("GeoLeakMonitorStart", new
            {
                ScanIntervalMs,
                GeoTtlMs,
                GeoRequestIntervalSeconds = GeoRequestInterval.TotalSeconds,
                _config.TargetCountryCode,
                _config.HunterMode_ExtendedScan
            });

            _activity?.Info($"Hunter Mode started. Target country: {_config.TargetCountryCode}");

            _scanTask = Task.Run(
                () => RunScanLoopSafeAsync(_cts.Token),
                CancellationToken.None);
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _startLock.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_cts is null && _scanTask is null)
                return;

            _logger.Write("GeoLeakMonitorStopRequested", new { });
            _activity?.Info("Hunter Mode stopping...");

            _cts?.Cancel();

            if (_scanTask != null)
            {
                try
                {
                    await _scanTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.Write("GeoLeakMonitorStopTaskError", new { Error = ex.ToString() });
                    _activity?.Error(ex.Message);
                }
            }

            _logger.Write("GeoLeakMonitorStopped", new { });
            _activity?.Info("Hunter Mode stopped.");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _scanTask = null;

            _startLock.Release();
        }
    }

    private async Task RunScanLoopSafeAsync(CancellationToken ct)
    {
        _logger.Write("GeoLeakMonitorLoopStarted", new { });

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ScanOnceAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Write("GeoLeakMonitorScanError", new { Error = ex.ToString() });
                    _activity?.Error(ex.Message);
                }

                try
                {
                    await Task.Delay(ScanIntervalMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            _logger.Write("GeoLeakMonitorLoopExited", new { });
        }
    }

    private async Task ScanOnceAsync(CancellationToken ct)
    {
        var vpnIfIndexes = DetectVpnInterfaceIndexes();

        var tcpConns = SnapshotTcpConnections();
        var udpConns = SnapshotUdpEndpoints();
        var all = tcpConns.Concat(udpConns).ToList();

        _activity?.ScanStarted(all.Count);

        foreach (var (remoteIpRaw, remotePort, pid) in all)
        {
            ct.ThrowIfCancellationRequested();

            var remoteIp = NativeMethods.NetworkUInt32ToIpString(remoteIpRaw);

            if (IsLoopbackOrPrivate(remoteIp))
                continue;

            var key = $"{remoteIp}:{remotePort}:{pid}";

            if (_reported.ContainsKey(key))
                continue;

            var processName = ResolveProcessName(pid);

            _activity?.ConnectionChecked(
                processName,
                remoteIp,
                remotePort);

            if (!IsMarkedIp(remoteIp))
                continue;

            var isBypass = IsVpnBypass(remoteIpRaw, vpnIfIndexes);

            var geo = await ResolveCountryAsync(remoteIp, ct).ConfigureAwait(false);

            if (geo is null)
                continue;

            _activity?.GeoLookup(remoteIp, geo.Value.Code, geo.Value.Name);

            if (isBypass)
            {
                EmitLeak(
                    "TCP_BYPASS",
                    remoteIp,
                    geo.Value.Code,
                    geo.Value.Name,
                    pid,
                    processName,
                    null);
            }
            else
            {
                EvaluateAndEmit(
                    "TCP_GEO",
                    remoteIp,
                    geo.Value.Code,
                    geo.Value.Name,
                    pid,
                    null);
            }

            _reported.TryAdd(key, true);
        }

        if (_reported.Count > 8192)
            _reported.Clear();
    }

    private string ResolveProcessName(uint pid)
    {
        try
        {
            return _processResolver.TryResolve(pid)?.ProcessName ?? $"PID {pid}";
        }
        catch
        {
            return $"PID {pid}";
        }
    }

    private async Task<(string? Code, string? Name)?> ResolveCountryAsync(
        string ipAddress,
        CancellationToken ct)
    {
        var now = Environment.TickCount64;

        if (_geoCache.TryGetValue(ipAddress, out var cached) && cached.Expiry > now)
            return (cached.Code, cached.Name);

        await WaitGeoRateLimitAsync(ct).ConfigureAwait(false);

        var result = await TryIpWhoIsAsync(ipAddress, ct).ConfigureAwait(false);

        if (result != null)
        {
            UpsertGeoCache(ipAddress, result.Value.Code, result.Value.Name, now);
            return result;
        }

        _logger.Write("GeoLookupFailed", new { Ip = ipAddress });
        _activity?.Error($"Geo lookup failed: {ipAddress}");

        return null;
    }

    private async Task WaitGeoRateLimitAsync(CancellationToken ct)
    {
        await _geoLimiter.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var elapsed = DateTime.UtcNow - _lastGeoRequestUtc;

            if (elapsed < GeoRequestInterval)
            {
                await Task.Delay(
                    GeoRequestInterval - elapsed,
                    ct).ConfigureAwait(false);
            }

            _lastGeoRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            _geoLimiter.Release();
        }
    }

    private async Task<(string? Code, string? Name)?> TryIpWhoIsAsync(
        string ipAddress,
        CancellationToken ct)
    {
        try
        {
            var resp = await s_http.GetFromJsonAsync<IpWhoIsResponse>(
                $"https://ipwho.is/{ipAddress}",
                ct).ConfigureAwait(false);

            if (resp?.success != true)
                return null;

            return (resp.country_code, resp.country);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Write("IpWhoIsGeoError", new
            {
                Ip = ipAddress,
                Error = ex.ToString()
            });

            return null;
        }
    }

    private IReadOnlyList<(uint RemoteIp, ushort RemotePort, uint Pid)> SnapshotTcpConnections()
    {
        try
        {
            var rows = NativeMethods.GetTcpTableOwnerPid();
            var result = new List<(uint, ushort, uint)>(rows.Count);

            foreach (var row in rows)
            {
                if (row.dwRemoteAddr == 0)
                    continue;

                var port = NativeMethods.NetworkPortToHost(row.dwRemotePort);
                result.Add((row.dwRemoteAddr, port, row.dwOwningPid));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.Write("SnapshotTcpError", new { Error = ex.ToString() });
            _activity?.Error($"TCP snapshot error: {ex.Message}");

            return Array.Empty<(uint, ushort, uint)>();
        }
    }

    private IReadOnlyList<(uint RemoteIp, ushort RemotePort, uint Pid)> SnapshotUdpEndpoints()
    {
        try
        {
            var rows = NativeMethods.GetUdpTableOwnerPid();
            var result = new List<(uint, ushort, uint)>(rows.Count);

            foreach (var row in rows)
            {
                if (row.dwLocalAddr == 0)
                    continue;

                var port = NativeMethods.NetworkPortToHost(row.dwLocalPort);
                result.Add((row.dwLocalAddr, port, row.dwOwningPid));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.Write("SnapshotUdpError", new { Error = ex.ToString() });
            _activity?.Error($"UDP snapshot error: {ex.Message}");

            return Array.Empty<(uint, ushort, uint)>();
        }
    }

    private static HashSet<uint> DetectVpnInterfaceIndexes()
    {
        var indexes = new HashSet<uint>();

        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var adapter in adapters)
            {
                var isTunnel =
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Ppp;

                var combined = $"{adapter.Name} {adapter.Description}".ToLowerInvariant();

                var hasKeyword = VpnNameKeywords.Any(keyword =>
                    combined.Contains(keyword));

                if (!isTunnel && !hasKeyword)
                    continue;

                var index = adapter.GetIPProperties()
                    .GetIPv4Properties()
                    ?.Index ?? 0;

                if (index > 0)
                    indexes.Add((uint)index);
            }
        }
        catch
        {
        }

        return indexes;
    }

    private static bool IsVpnBypass(uint remoteIpRaw, HashSet<uint> vpnIfIndexes)
    {
        if (vpnIfIndexes.Count == 0)
            return false;

        try
        {
            var route = NativeMethods.GetBestRouteTo(remoteIpRaw);

            if (route is null)
                return false;

            return !vpnIfIndexes.Contains(route.Value.dwForwardIfIndex);
        }
        catch
        {
            return false;
        }
    }

    private void EvaluateAndEmit(
        string leakKind,
        string remoteIp,
        string? countryCode,
        string? countryName,
        uint? pid,
        string? hostname)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
            return;

        var isForeign = !countryCode.Equals(
            _config.TargetCountryCode,
            StringComparison.OrdinalIgnoreCase);

        if (!isForeign)
            return;

        var processName = pid.HasValue
            ? ResolveProcessName(pid.Value)
            : null;

        EmitLeak(
            leakKind,
            remoteIp,
            countryCode,
            countryName,
            pid,
            processName,
            hostname);
    }

    private void EmitLeak(
        string leakKind,
        string remoteIp,
        string? countryCode,
        string? countryName,
        uint? pid,
        string? processName,
        string? hostname)
    {
        var args = new LeakDetectedEventArgs(
            leakKind,
            remoteIp,
            countryCode,
            countryName,
            pid,
            processName,
            hostname,
            DateTimeOffset.UtcNow);

        _activity?.Leak(
            leakKind,
            remoteIp,
            countryCode,
            countryName,
            processName);

        _logger.Write("LeakDetected", args);
        _hub.Publish(args);
    }

    private bool IsMarkedIp(string ip)
    {
        foreach (var prefix in BaseMarkerPrefixes)
        {
            if (ip.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        if (_config.HunterMode_ExtendedScan)
        {
            foreach (var prefix in ExtendedMarkerPrefixes)
            {
                if (ip.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    private static bool IsLoopbackOrPrivate(string ip)
    {
        if (!IPAddress.TryParse(ip, out var addr))
            return true;

        var bytes = addr.GetAddressBytes();

        if (bytes.Length != 4)
            return true;

        return bytes[0] is 127 or 0
            || bytes[0] == 10
            || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
            || bytes[0] == 192 && bytes[1] == 168
            || bytes[0] == 169 && bytes[1] == 254;
    }

    private void UpsertGeoCache(string ip, string? code, string? name, long now)
    {
        var expiry = now + GeoTtlMs;

        _geoCache.AddOrUpdate(
            ip,
            _ =>
            {
                AddGeoLru(ip);
                return (code, name, expiry);
            },
            (_, _) =>
            {
                TouchGeoLru(ip);
                return (code, name, expiry);
            });

        while (_geoCache.Count > GeoLruMax)
        {
            string? evict = null;

            lock (_geoLock)
            {
                if (_geoLru.First is not null)
                {
                    evict = _geoLru.First.Value;
                    _geoLru.RemoveFirst();
                }
            }

            if (evict is not null)
                _geoCache.TryRemove(evict, out _);
            else
                break;
        }
    }

    private void AddGeoLru(string ip)
    {
        lock (_geoLock)
        {
            if (!_geoLru.Contains(ip))
                _geoLru.AddLast(ip);
        }
    }

    private void TouchGeoLru(string ip)
    {
        lock (_geoLock)
        {
            var node = _geoLru.Find(ip);

            if (node is null)
            {
                _geoLru.AddLast(ip);
                return;
            }

            _geoLru.Remove(node);
            _geoLru.AddLast(node);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            if (_cts is not null || _scanTask is not null)
                StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        try
        {
            _geoLimiter.Dispose();
            _startLock.Dispose();
        }
        catch
        {
        }
    }

    private sealed class IpWhoIsResponse
    {
        public bool success { get; init; }
        public string? country_code { get; init; }
        public string? country { get; init; }
    }
}