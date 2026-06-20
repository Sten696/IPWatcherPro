using System.Collections.Concurrent;

namespace IPWatcherPro.Infrastructure;

public enum HunterActivityKind
{
    Scan,
    Connection,
    GeoLookup,
    Leak,
    Error,
    Info
}

public sealed record HunterActivityItem(
    DateTimeOffset Timestamp,
    HunterActivityKind Kind,
    string Message,
    string? ProcessName = null,
    string? RemoteIp = null,
    string? CountryCode = null,
    string? CountryName = null
);

public sealed record HunterActivitySnapshot(
    DateTimeOffset? LastScanTime,
    long TotalScans,
    long ConnectionsChecked,
    long GeoLookups,
    long LeaksFound,
    IReadOnlyList<HunterActivityItem> Items
);

public sealed class HunterActivityStore
{
    private readonly ConcurrentQueue<HunterActivityItem> _items = new();
    private readonly object _trimLock = new();

    private readonly int _maxItems;

    private long _totalScans;
    private long _connectionsChecked;
    private long _geoLookups;
    private long _leaksFound;

    private DateTimeOffset? _lastScanTime;

    public HunterActivityStore(int maxItems = 300)
    {
        _maxItems = Math.Max(50, maxItems);
    }

    public void ScanStarted(int connectionCount)
    {
        _lastScanTime = DateTimeOffset.Now;
        Interlocked.Increment(ref _totalScans);

        Add(
            HunterActivityKind.Scan,
            $"Scan started — connections: {connectionCount}");
    }

    public void ConnectionChecked(string processName, string remoteIp, ushort remotePort)
    {
        Interlocked.Increment(ref _connectionsChecked);

        Add(
            HunterActivityKind.Connection,
            $"{processName} → {remoteIp}:{remotePort}",
            processName,
            remoteIp,
            null,
            null);
    }

    public void GeoLookup(string remoteIp, string? countryCode, string? countryName)
    {
        Interlocked.Increment(ref _geoLookups);

        Add(
            HunterActivityKind.GeoLookup,
            $"{remoteIp} → {countryName ?? "Unknown"} ({countryCode ?? "??"})",
            null,
            remoteIp,
            countryCode,
            countryName);
    }

    public void Leak(
        string leakKind,
        string remoteIp,
        string? countryCode,
        string? countryName,
        string? processName)
    {
        Interlocked.Increment(ref _leaksFound);

        Add(
            HunterActivityKind.Leak,
            $"{leakKind}: {processName ?? "unknown"} → {remoteIp} ({countryCode ?? "??"})",
            processName,
            remoteIp,
            countryCode,
            countryName);
    }

    public void Error(string message)
    {
        Add(HunterActivityKind.Error, message);
    }

    public void Info(string message)
    {
        Add(HunterActivityKind.Info, message);
    }

    public HunterActivitySnapshot GetSnapshot()
    {
        return new HunterActivitySnapshot(
            _lastScanTime,
            Interlocked.Read(ref _totalScans),
            Interlocked.Read(ref _connectionsChecked),
            Interlocked.Read(ref _geoLookups),
            Interlocked.Read(ref _leaksFound),
            _items.Reverse().ToList());
    }

    public void Clear()
    {
        while (_items.TryDequeue(out _))
        {
        }

        Interlocked.Exchange(ref _totalScans, 0);
        Interlocked.Exchange(ref _connectionsChecked, 0);
        Interlocked.Exchange(ref _geoLookups, 0);
        Interlocked.Exchange(ref _leaksFound, 0);

        _lastScanTime = null;
    }

    private void Add(
        HunterActivityKind kind,
        string message,
        string? processName = null,
        string? remoteIp = null,
        string? countryCode = null,
        string? countryName = null)
    {
        _items.Enqueue(new HunterActivityItem(
            DateTimeOffset.Now,
            kind,
            message,
            processName,
            remoteIp,
            countryCode,
            countryName));

        Trim();
    }

    private void Trim()
    {
        if (_items.Count <= _maxItems)
            return;

        lock (_trimLock)
        {
            while (_items.Count > _maxItems && _items.TryDequeue(out _))
            {
            }
        }
    }
}