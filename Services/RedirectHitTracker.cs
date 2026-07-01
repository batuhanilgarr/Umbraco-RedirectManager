using System.Collections.Concurrent;
using System.Threading;

namespace Umbraco.RedirectManager.Services;

public interface IRedirectHitTracker
{
    void RecordHit(int redirectId);
    IReadOnlyDictionary<int, (int Count, DateTime LastHitUtc)> DrainAll();
}

public class RedirectHitTracker : IRedirectHitTracker
{
    private readonly ConcurrentDictionary<int, HitAccumulator> _accumulators = new();

    public void RecordHit(int redirectId)
    {
        _accumulators.AddOrUpdate(
            redirectId,
            _ => new HitAccumulator(1, DateTime.UtcNow),
            (_, existing) =>
            {
                existing.Increment();
                return existing;
            });
    }

    public IReadOnlyDictionary<int, (int Count, DateTime LastHitUtc)> DrainAll()
    {
        var drained = new Dictionary<int, (int Count, DateTime LastHitUtc)>();

        foreach (var key in _accumulators.Keys.ToArray())
        {
            if (_accumulators.TryRemove(key, out var accumulator))
            {
                drained[key] = (accumulator.Count, accumulator.LastHitUtc);
            }
        }

        return drained;
    }

    private sealed class HitAccumulator
    {
        private int _count;
        private long _lastHitUtcTicks;

        public HitAccumulator(int initialCount, DateTime lastHitUtc)
        {
            _count = initialCount;
            _lastHitUtcTicks = lastHitUtc.Ticks;
        }

        public int Count => _count;
        public DateTime LastHitUtc => new(Interlocked.Read(ref _lastHitUtcTicks), DateTimeKind.Utc);

        public void Increment()
        {
            Interlocked.Increment(ref _count);
            Interlocked.Exchange(ref _lastHitUtcTicks, DateTime.UtcNow.Ticks);
        }
    }
}
