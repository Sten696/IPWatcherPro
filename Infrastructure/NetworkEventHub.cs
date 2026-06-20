using System.Net.NetworkInformation;
using IPWatcherPro.Core;

namespace IPWatcherPro.Infrastructure;

/// <summary>
/// Thread-safe pub/sub event bus. All events are dispatched on the thread-pool.
/// Also subscribes to NetworkChange.NetworkAddressChanged and translates 
/// OS adapter events into AdapterChangedEventArgs.
/// </summary>
public sealed class NetworkEventHub : IDisposable
{
    // Strongly-typed surface events
    public event Action<NetworkEventArgs>? OnEvent;
    public event Action<IpChangedEventArgs>? OnIpChanged;
    public event Action<LeakDetectedEventArgs>? OnLeakDetected;
    public event Action<DnsQueryEventArgs>? OnDnsQuery;
    public event Action<SniObservedEventArgs>? OnSniObserved;
    public event Action<AdapterChangedEventArgs>? OnAdapterChanged;

    private volatile bool _disposed;

    // Snapshot of adapters at the last NetworkAddressChanged notification,
    // used to compute which adapter actually changed.
    private NetworkInterface[] _lastAdapters;
    private readonly object _adapterLock = new();

    public NetworkEventHub()
    {
        _lastAdapters = NetworkInterface.GetAllNetworkInterfaces();
        NetworkChange.NetworkAddressChanged += HandleNetworkAddressChanged;
    }

    // ── OS → hub adapter bridge ───────────────────────────────────────────

    private void HandleNetworkAddressChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;

        try
        {
            NetworkInterface[] current;
            NetworkInterface[] previous;

            lock (_adapterLock)
            {
                previous = _lastAdapters;
                current = NetworkInterface.GetAllNetworkInterfaces();
                _lastAdapters = current;
            }

            // Detect added adapters
            var prevNames = new HashSet<string>(previous.Select(a => a.Id));
            var currentNames = new HashSet<string>(current.Select(a => a.Id));

            foreach (var adapter in current.Where(a => !prevNames.Contains(a.Id)))
                Publish(new AdapterChangedEventArgs(adapter.Name, "Added"));

            foreach (var adapter in previous.Where(a => !currentNames.Contains(a.Id)))
                Publish(new AdapterChangedEventArgs(adapter.Name, "Removed"));

            // Detect operational state changes on existing adapters
            var prevDict = previous.ToDictionary(a => a.Id, a => a.OperationalStatus);
            foreach (var adapter in current)
            {
                if (prevDict.TryGetValue(adapter.Id, out var prevStatus) &&
                    prevStatus != adapter.OperationalStatus)
                {
                    string kind = adapter.OperationalStatus == OperationalStatus.Up ? "Up" : "Down";
                    Publish(new AdapterChangedEventArgs(adapter.Name, kind));
                }
            }
        }
        catch
        {
            // Never let OS callback exceptions propagate to the runtime.
        }
    }

    // ── Publishing ───────────────────────────────────────────────────────

    /// <summary>
    /// Fire-and-forget publish: dispatches args on the thread-pool.
    /// Exceptions in individual handlers are isolated and swallowed.
    /// </summary>
    public void Publish(NetworkEventArgs args)
    {
        if (_disposed) return;

        // Dispatch on thread-pool so callers are never blocked by slow subscribers.
        ThreadPool.QueueUserWorkItem(_ => DispatchEvent(args));
    }

    private void DispatchEvent(NetworkEventArgs args)
    {
        if (_disposed) return;

        SafeInvoke(OnEvent, args);

        switch (args)
        {
            case IpChangedEventArgs ip:
                SafeInvoke(OnIpChanged, ip);
                break;
            case LeakDetectedEventArgs leak:
                SafeInvoke(OnLeakDetected, leak);
                break;
            case DnsQueryEventArgs dns:
                SafeInvoke(OnDnsQuery, dns);
                break;
            case SniObservedEventArgs sni:
                SafeInvoke(OnSniObserved, sni);
                break;
            case AdapterChangedEventArgs adapter:
                SafeInvoke(OnAdapterChanged, adapter);
                break;
        }
    }

    private static void SafeInvoke<T>(Action<T>? handler, T args)
    {
        if (handler is null) return;
        foreach (Action<T> d in handler.GetInvocationList().Cast<Action<T>>())
        {
            try { d(args); }
            catch { /* individual handler fault; continue to next */ }
        }
    }

    // ── Subscription helpers ──────────────────────────────────────────────

    public void Subscribe(Action<NetworkEventArgs> handler) => OnEvent += handler;
    public void Unsubscribe(Action<NetworkEventArgs> handler) => OnEvent -= handler;

    // ── IDisposable ───────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NetworkChange.NetworkAddressChanged -= HandleNetworkAddressChanged;
    }
}