using System.Collections.Concurrent;
using System.Threading;

namespace Umbraco.RedirectManager.Services;

public interface IRedirectHitTracker
{
    void RecordHit(int redirectId);
    IReadOnlyDictionary<int, (int Count, DateTime LastHitUtc)> DrainAll();
    void MergeBack(IReadOnlyDictionary<int, (int Count, DateTime LastHitUtc)> data);
}

public class RedirectHitTracker : IRedirectHitTracker
{
    private ConcurrentDictionary<int, HitAccumulator> _accumulators = new();

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

    public void MergeBack(IReadOnlyDictionary<int, (int Count, DateTime LastHitUtc)> data)
    {
        foreach (var (redirectId, hit) in data)
        {
            _accumulators.AddOrUpdate(
                redirectId,
                _ => new HitAccumulator(hit.Count, hit.LastHitUtc),
                (_, existing) =>
                {
                    existing.Add(hit.Count, hit.LastHitUtc);
                    return existing;
                });
        }
    }

    public IReadOnlyDictionary<int, (int Count, DateTime LastHitUtc)> DrainAll()
    {
        // Swap the whole dictionary reference atomically rather than removing
        // keys one at a time: a concurrent RecordHit landing mid-drain lands in
        // the fresh dictionary instead of racing AddOrUpdate against TryRemove
        // on the same key (which could otherwise lose or double-count a hit).
        var drainedDictionary = Interlocked.Exchange(ref _accumulators, new ConcurrentDictionary<int, HitAccumulator>());

        var drained = new Dictionary<int, (int Count, DateTime LastHitUtc)>();
        foreach (var (key, accumulator) in drainedDictionary)
        {
            drained[key] = (accumulator.Count, accumulator.LastHitUtc);
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

        public void Add(int count, DateTime lastHitUtc)
        {
            Interlocked.Add(ref _count, count);

            // Only advance LastHitUtc, never regress it — a merge-back of an
            // older failed batch shouldn't overwrite a newer hit that was
            // recorded in the meantime.
            var newTicks = lastHitUtc.Ticks;
            var current = Interlocked.Read(ref _lastHitUtcTicks);
            while (newTicks > current)
            {
                var original = Interlocked.CompareExchange(ref _lastHitUtcTicks, newTicks, current);
                if (original == current)
                {
                    break;
                }

                current = original;
            }
        }
    }
}
