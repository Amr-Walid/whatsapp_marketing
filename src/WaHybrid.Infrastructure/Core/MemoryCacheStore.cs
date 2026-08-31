using System.Collections.Concurrent;
using WaHybrid.Domain.Contracts;

namespace WaHybrid.Infrastructure.Core;

/// <summary>
/// تنفيذ <see cref="ICacheStore"/> في الذاكرة — للتطوير والاختبار.
///
/// في الإنتاج بتستبدله بـ Redis (StackExchange.Redis) بنفس الواجهة بالظبط.
/// كل العمليات المستخدمة (GET / SET EX / SET NX EX / INCR / EXISTS / DEL)
/// ليها مقابل مباشر في Redis — فالاستبدال سطر واحد في DI.
/// </summary>
public sealed class MemoryCacheStore : ICacheStore
{
    private sealed record Entry(string Value, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _store = new();
    private readonly object _lock = new();

    private void Sweep()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _store)
            if (kv.Value.ExpiresAt <= now)
                _store.TryRemove(kv.Key, out _);
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        if (_store.TryGetValue(key, out var e))
        {
            if (e.ExpiresAt > DateTimeOffset.UtcNow) return Task.FromResult<string?>(e.Value);
            _store.TryRemove(key, out _);
        }
        return Task.FromResult<string?>(null);
    }

    public Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default)
    {
        _store[key] = new Entry(value, DateTimeOffset.UtcNow.Add(ttl));
        return Task.CompletedTask;
    }

    /// <summary>مقابل <c>SET key val NX EX ttl</c> في Redis — أساس الـ idempotency</summary>
    public Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_store.TryGetValue(key, out var e) && e.ExpiresAt > DateTimeOffset.UtcNow)
                return Task.FromResult(false);

            _store[key] = new Entry(value, DateTimeOffset.UtcNow.Add(ttl));
            return Task.FromResult(true);
        }
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        if (_store.TryGetValue(key, out var e) && e.ExpiresAt > DateTimeOffset.UtcNow)
            return Task.FromResult(true);
        return Task.FromResult(false);
    }

    /// <summary>مقابل <c>INCR</c> + <c>EXPIRE</c> على أول زيادة</summary>
    public Task<long> IncrementAsync(string key, TimeSpan ttlOnFirst, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            if (_store.TryGetValue(key, out var e) && e.ExpiresAt > now)
            {
                var n = long.Parse(e.Value) + 1;
                _store[key] = new Entry(n.ToString(), e.ExpiresAt);   // الـ TTL مش بيتجدّد
                return Task.FromResult(n);
            }

            _store[key] = new Entry("1", now.Add(ttlOnFirst));
            return Task.FromResult(1L);
        }
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    /// <summary>للاختبار: تفريغ كل حاجة</summary>
    public void Clear() => _store.Clear();

    /// <summary>للداشبورد: عدد المفاتيح النشطة</summary>
    public int ActiveKeys()
    {
        Sweep();
        return _store.Count;
    }
}
