using System.Collections.Concurrent;

namespace NginxSslManager.Api.Services;

public sealed class DnsTxtRecord
{
    public required string Domain { get; init; }
    public bool Wildcard { get; init; }
    public required string RecordName { get; init; }
    public required string RecordValue { get; init; }
}

public sealed class PendingDnsOrder
{
    public required string OrderUrl { get; init; }
    public required IReadOnlyList<DnsTxtRecord> Records { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; init; } = DateTime.UtcNow.AddHours(2);
}

/// <summary>
/// Keeps the pending DNS-01 order between the "prepare" and "complete" calls.
/// This state is intentionally in memory: if the application restarts the user
/// can prepare a fresh order, and stale entries are discarded automatically.
/// </summary>
public sealed class DnsChallengeStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);
    private readonly ConcurrentDictionary<int, PendingDnsOrder> _orders = new();

    public PendingDnsOrder Set(int certificateId, string orderUrl, IReadOnlyList<DnsTxtRecord> records)
    {
        Cleanup();
        var order = new PendingDnsOrder
        {
            OrderUrl = orderUrl,
            Records = records,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(Lifetime)
        };
        _orders[certificateId] = order;
        return order;
    }

    public PendingDnsOrder? Get(int certificateId)
    {
        if (!_orders.TryGetValue(certificateId, out var order))
        {
            return null;
        }

        if (order.ExpiresAt <= DateTime.UtcNow)
        {
            _orders.TryRemove(certificateId, out _);
            return null;
        }

        return order;
    }

    public void Remove(int certificateId) => _orders.TryRemove(certificateId, out _);

    private void Cleanup()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in _orders)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                _orders.TryRemove(pair.Key, out _);
            }
        }
    }
}
