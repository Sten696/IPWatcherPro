namespace IPWatcherPro.Core;

public abstract record NetworkEventArgs
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record IpChangedEventArgs(
    string? PreviousIp,
    string NewIp,
    string? PreviousCountryCode,
    string? NewCountryCode,
    string? NewCountryName,
    string? Gateway,
    DateTimeOffset ChangedAt) : NetworkEventArgs;

public sealed record AdapterChangedEventArgs(
    string AdapterName,
    string ChangeKind) : NetworkEventArgs;

public sealed record LeakDetectedEventArgs(
    string LeakKind,
    string RemoteIp,
    string? RemoteCountryCode,
    string? RemoteCountryName,
    uint? OwnerPid,
    string? OwnerProcessName,
    string? Hostname,
    DateTimeOffset DetectedAt) : NetworkEventArgs;

public sealed record DnsQueryEventArgs(
    string QueryName,
    string QueryType,
    uint? OwnerPid) : NetworkEventArgs;

public sealed record SniObservedEventArgs(
    string Hostname,
    string RemoteIp,
    ushort RemotePort,
    uint? OwnerPid) : NetworkEventArgs;