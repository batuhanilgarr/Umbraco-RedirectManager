using System.Collections.Concurrent;
using System.Threading;

namespace Umbraco.RedirectManager.Services;

// Deliberately separate from IRedirectHitTracker (not a generalization of it)
// so the A/B test variant-B hit counter never touches the existing,
// carefully-hardened hit-tracking code path — same accumulate-in-memory,
// flush-periodically shape, but fully independent.
public interface IVariantBHitTracker
{
    void RecordHit(int redirectId);
    IReadOnlyDictionary<int, (int Count, DateTime LastHitUtc)> DrainAll();
    void MergeBack(IReadOnlyDictionary<int, (int Count, DateTime LastHitUtc)> data);
}

public class VariantBHitTracker : IVariantBHitTracker
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

    public IReadOnlyDictionary<int, (int Count, DateTime LastHitUtc)> DrainAll()
    {
        var drainedDictionary = Interlocked.Exchange(ref _accumulators, new ConcurrentDictionary<int, HitAccumulator>());

        var drained = new Dictionary<int, (int Count, DateTime LastHitUtc)>();
        foreach (var (key, accumulator) in drainedDictionary)
        {
            drained[key] = (accumulator.Count, accumulator.LastHitUtc);
        }

        return drained;
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
