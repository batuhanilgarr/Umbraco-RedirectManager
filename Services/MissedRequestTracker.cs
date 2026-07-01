using System.Collections.Concurrent;
using System.Threading;

namespace Umbraco.RedirectManager.Services;

public interface IMissedRequestTracker
{
    void RecordMiss(string path);
    IReadOnlyDictionary<string, (int Count, DateTime FirstSeenUtc, DateTime LastSeenUtc)> DrainAll();
}

public class MissedRequestTracker : IMissedRequestTracker
{
    // Soft cap, not a security control: bounds this package's own memory and
    // database growth under a URL-scanning bot generating unlimited distinct
    // garbage paths. Real abuse mitigation belongs at the infra/WAF layer.
    private const int MaxDistinctPathsPerWindow = 5000;

    private ConcurrentDictionary<string, MissAccumulator> _accumulators = new();

    public void RecordMiss(string path)
    {
        if (_accumulators.TryGetValue(path, out var existing))
        {
            existing.Increment();
            return;
        }

        if (_accumulators.Count >= MaxDistinctPathsPerWindow)
        {
            // Cap reached: silently drop newly-seen paths for this window.
            // Already-tracked paths above keep incrementing normally. A few
            // concurrent callers can race past this check at once (soft cap,
            // not exact), which is an accepted approximation for analytics data.
            return;
        }

        _accumulators.GetOrAdd(path, _ => new MissAccumulator(DateTime.UtcNow));
    }

    public IReadOnlyDictionary<string, (int Count, DateTime FirstSeenUtc, DateTime LastSeenUtc)> DrainAll()
    {
        var drainedDictionary = Interlocked.Exchange(ref _accumulators, new ConcurrentDictionary<string, MissAccumulator>());

        var drained = new Dictionary<string, (int Count, DateTime FirstSeenUtc, DateTime LastSeenUtc)>();
        foreach (var (key, accumulator) in drainedDictionary)
        {
            drained[key] = (accumulator.Count, accumulator.FirstSeenUtc, accumulator.LastSeenUtc);
        }

        return drained;
    }

    private sealed class MissAccumulator
    {
        private int _count = 1;
        private readonly long _firstSeenUtcTicks;
        private long _lastSeenUtcTicks;

        public MissAccumulator(DateTime firstSeenUtc)
        {
            _firstSeenUtcTicks = firstSeenUtc.Ticks;
            _lastSeenUtcTicks = firstSeenUtc.Ticks;
        }

        public int Count => _count;
        public DateTime FirstSeenUtc => new(_firstSeenUtcTicks, DateTimeKind.Utc);
        public DateTime LastSeenUtc => new(Interlocked.Read(ref _lastSeenUtcTicks), DateTimeKind.Utc);

        public void Increment()
        {
            Interlocked.Increment(ref _count);
            Interlocked.Exchange(ref _lastSeenUtcTicks, DateTime.UtcNow.Ticks);
        }
    }
}
