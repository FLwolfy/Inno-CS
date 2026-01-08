using System;

namespace Inno.Graphics.Resources.GpuResources.Cache;

public class GpuVariant
{
    private HashCode m_hash;
    private GpuVariant() {}

    public delegate void Fill(GpuVariant v);

    public void Add<T>(T value) => m_hash.Add(value);
    public void AddId(Guid guid) => m_hash.Add(guid);
    public void AddType(Type type) => m_hash.Add(type.TypeHandle);

    public static int Build(Fill fill)
    {
        var v = new GpuVariant();
        fill(v);
        return v.m_hash.ToHashCode();;
    }
}