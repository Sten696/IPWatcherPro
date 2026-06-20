using System.Collections.Concurrent;
using System.Diagnostics;

namespace IPWatcherPro.Infrastructure;

public sealed class ProcessResolver
{
    private readonly ConcurrentDictionary<uint, (string Name, long Expiry)> _cache = new();

    private const int CacheTtlMs = 30_000;

    public ProcessInfo? TryResolve(uint pid)
    {
        if (pid == 0)
            return null;

        var now = Environment.TickCount64;

        if (_cache.TryGetValue(pid, out var cached) && cached.Expiry > now)
            return new ProcessInfo(pid, cached.Name);

        try
        {
            using var process = Process.GetProcessById(unchecked((int)pid));

            var name = SafeProcessName(process, pid);

            _cache[pid] = (name, now + CacheTtlMs);

            return new ProcessInfo(pid, name);
        }
        catch
        {
            var name = $"PID {pid}";
            _cache[pid] = (name, now + CacheTtlMs);
            return new ProcessInfo(pid, name);
        }
    }

    private static string SafeProcessName(Process process, uint pid)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(process.ProcessName))
                return process.ProcessName;
        }
        catch
        {
        }

        return $"PID {pid}";
    }
}

public sealed record ProcessInfo(uint ProcessId, string ProcessName);