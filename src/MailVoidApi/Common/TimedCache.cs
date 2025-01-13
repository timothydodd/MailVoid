using Microsoft.Extensions.Caching.Memory;

namespace MailVoidWeb;
public class TimedCache
{
    private readonly IMemoryCache _cache;

    public TimedCache(IMemoryCache memoryCache)
    {
        _cache = memoryCache;
    }

    public void SetCacheItem<T>(string key, T value, TimeSpan expirationTime)
    {
        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(expirationTime);

        _cache.Set<T>(key, value, cacheEntryOptions);
    }

    public T? GetCacheItem<T>(string key)
    {
        return _cache.TryGetValue<T>(key, out var value) ? value : default(T);
    }
}
