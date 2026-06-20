using System.Net.Http.Json;
using IPWatcherPro.Core;
using IPWatcherPro.Infrastructure;

namespace IPWatcherPro.Services;

public sealed class IpWatcherService : IDisposable
{
    private readonly AppConfiguration _config;
    private readonly NetworkEventHub _hub;
    private readonly JsonLinesLogger _logger;

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    private string? _lastIp;
    private string? _lastCountry;

    private bool _disposed;

    public IpWatcherService(
        AppConfiguration config,
        NetworkEventHub hub,
        JsonLinesLogger logger)
    {
        _config = config;
        _hub = hub;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(IpWatcherService));

        if (_loopTask is { IsCompleted: false })
            return Task.CompletedTask;

        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _logger.Write("IpWatcherStart", new
        {
            _config.RefreshIntervalMinutes,
            HunterMode = _config.HunterMode_AggressivePolling
        });

        _loopTask = Task.Run(
            () => RunLoopAsync(_cts.Token),
            CancellationToken.None);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is null && _loopTask is null)
            return;

        _logger.Write("IpWatcherStopRequested", new { });

        try
        {
            _cts?.Cancel();

            if (_loopTask != null)
            {
                try
                {
                    await _loopTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _loopTask = null;

            _logger.Write("IpWatcherStopped", new { });
        }
    }

    public async Task TriggerPollAsync()
    {
        await DoPollAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        _logger.Write("IpWatcherLoopStarted", new { });

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await DoPollAsync(ct).ConfigureAwait(false);

                var delayMs = _config.HunterMode_AggressivePolling
                    ? 30_000
                    : Math.Clamp(_config.RefreshIntervalMinutes, 1, 1440) * 60_000;

                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Write("IpWatcherLoopError", new
            {
                Error = ex.ToString()
            });
        }
        finally
        {
            _logger.Write("IpWatcherLoopExited", new { });
        }
    }

    private async Task DoPollAsync(CancellationToken ct)
    {
        try
        {
            _logger.Write("IpPollStarted", new
            {
                Time = DateTimeOffset.UtcNow
            });

            var ip = await GetPublicIpAsync(ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(ip))
            {
                _logger.Write("IpPollNoIp", new { });
                return;
            }

            var geo = await ResolveGeoAsync(ip, ct).ConfigureAwait(false);

            var gateway = GetLocalGateway() ?? "Unknown";

            var changed =
                ip != _lastIp ||
                !string.Equals(
                    geo.CountryCode,
                    _lastCountry,
                    StringComparison.OrdinalIgnoreCase);

            _logger.Write("IpPollCompleted", new
            {
                Ip = ip,
                CountryCode = geo.CountryCode,
                CountryName = geo.CountryName,
                Gateway = gateway,
                Changed = changed
            });

            if (changed || _config.HunterMode_LogAllChanges)
            {
                var args = new IpChangedEventArgs(
                    _lastIp,
                    ip,
                    _lastCountry,
                    geo.CountryCode,
                    geo.CountryName,
                    gateway,
                    DateTimeOffset.UtcNow);

                _hub.Publish(args);

                _lastIp = ip;
                _lastCountry = geo.CountryCode;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Write("PollError", new
            {
                Error = ex.ToString()
            });
        }
    }

    private async Task<string?> GetPublicIpAsync(CancellationToken ct)
    {
        try
        {
            var ipify = await _http.GetFromJsonAsync<IpifyResponse>(
                "https://api.ipify.org?format=json",
                ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(ipify?.ip))
                return ipify.ip.Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Write("IpifyError", new
            {
                Error = ex.ToString()
            });
        }

        try
        {
            var text = await _http.GetStringAsync(
                "https://checkip.amazonaws.com",
                ct).ConfigureAwait(false);

            text = text.Trim();

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Write("CheckIpAmazonError", new
            {
                Error = ex.ToString()
            });
        }

        return null;
    }

    private async Task<GeoResult> ResolveGeoAsync(
        string ip,
        CancellationToken ct)
    {
        try
        {
            var resp = await _http.GetFromJsonAsync<IpWhoIsResponse>(
                $"https://ipwho.is/{ip}",
                ct).ConfigureAwait(false);

            if (resp?.success == true)
            {
                return new GeoResult(
                    resp.country_code,
                    resp.country);
            }

            _logger.Write("IpWhoIsLookupFailed", new
            {
                Ip = ip,
                Success = resp?.success,
                Message = resp?.message
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Write("IpWhoIsLookupError", new
            {
                Ip = ip,
                Error = ex.ToString()
            });
        }

        return new GeoResult(null, null);
    }

    private static string? GetLocalGateway()
    {
        try
        {
            var dest = NativeMethods.IpStringToNetworkUInt32("8.8.8.8");
            var route = NativeMethods.GetBestRouteTo(dest);

            return route.HasValue
                ? NativeMethods.NetworkUInt32ToIpString(route.Value.dwForwardNextHop)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        _http.Dispose();
    }

    private sealed record IpifyResponse(string? ip);

    private sealed class IpWhoIsResponse
    {
        public bool success { get; init; }
        public string? country_code { get; init; }
        public string? country { get; init; }
        public string? message { get; init; }
    }

    private sealed record GeoResult(
        string? CountryCode,
        string? CountryName);
}