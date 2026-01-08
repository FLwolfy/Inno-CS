using System;
using System.Collections.Generic;
using System.Threading;

using GpuCacheKey = (System.Guid, System.Type, int);

namespace Inno.Graphics.Resources.GpuResources.Cache;

public sealed class GpuCache
{
    private readonly Lock m_sync = new();
    private readonly Dictionary<GpuCacheKey, Entry> m_cache = new();

    internal GpuCache() { }

    private sealed class Entry
    {
        public required IDisposable resource;
        public int refCount;
    }

    public readonly struct Handle<T> : IDisposable where T : class, IDisposable
    {
        private readonly GpuCache? m_resourceCache;
        private readonly GpuCacheKey m_key;
        public T value { get; }

        internal Handle(GpuCache resourceCache, GpuCacheKey key, T value)
        {
            m_resourceCache = resourceCache;
            m_key = key;
            this.value = value;
        }

        public void Dispose() => m_resourceCache?.Release(m_key);
    }

    public Handle<T> Acquire<T>(Func<T> factory, Guid guid, int variantKey = 0)
        where T : class, IDisposable
    {
        var key = new GpuCacheKey(guid, typeof(T), variantKey);

        lock (m_sync)
        {
            if (m_cache.TryGetValue(key, out var e))
            {
                e.refCount++;
                return new Handle<T>(this, key, (T)e.resource);
            }

            var created = factory();
            m_cache[key] = new Entry { resource = created, refCount = 1 };
            return new Handle<T>(this, key, created);
        }
    }

    private void Release(GpuCacheKey key)
    {
        lock (m_sync)
        {
            if (!m_cache.TryGetValue(key, out var e))
                return;

            e.refCount--;
            if (e.refCount > 0) return;

            m_cache.Remove(key);
            e.resource.Dispose();
        }
    }
    
    public void Clear()
    {
        lock (m_sync)
        {
            foreach (var entry in m_cache.Values) entry.resource.Dispose();
            m_cache.Clear();
        }
    }
}