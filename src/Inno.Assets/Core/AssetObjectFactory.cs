using System;
using System.Runtime.CompilerServices;

namespace Inno.Assets.Core;

internal static class AssetObjectFactory
{
    public static object Create(Type runtimeType)
    {
        if (runtimeType == null) throw new ArgumentNullException(nameof(runtimeType));

        try
        {
            return Activator.CreateInstance(runtimeType, nonPublic: true)
                   ?? throw new InvalidOperationException($"Activator returned null for {runtimeType.FullName}");
        }
        catch
        {
            // Deterministic fallback (no ctor)
            return RuntimeHelpers.GetUninitializedObject(runtimeType);
        }
    }
}