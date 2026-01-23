using System;
using System.Collections.Generic;

namespace Inno.Core.Serialization;

[AttributeUsage(AttributeTargets.Method, Inherited = true)]
public sealed class OnSerializableRestored : Attribute;

public sealed record SerializedState
{
    public IReadOnlyDictionary<string, object?> values { get; }

    internal SerializedState(IReadOnlyDictionary<string, object?> values)
    {
        this.values = values ?? throw new ArgumentNullException(nameof(values));
    }

    public bool Contains(string key) => values.ContainsKey(key);

    public bool TryGetValue(string key, out object? value) => values.TryGetValue(key, out value);

    public T GetValue<T>(string key)
    {
        if (!values.TryGetValue(key, out var v))
            throw new KeyNotFoundException(key);

        if (v is T t) return t;

        throw new InvalidCastException(
            $"State value '{key}' is {v?.GetType().FullName}, expected {typeof(T).FullName}");
    }
}
