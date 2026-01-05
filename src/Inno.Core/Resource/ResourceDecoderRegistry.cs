using System;
using System.Collections.Generic;
using Inno.Core.Utility;

namespace Inno.Core.Resource;

internal static class ResourceDecoderRegistry
{
    private static readonly Dictionary<Type, IResourceDecoder> DECODERS = new();
    
    [TypeCacheInitialize]
    private static void RegisterAll()
    {
        foreach (var type in TypeCacheManager.GetTypesImplementing<IResourceDecoder>())
        {
            Register(type);
        }
    }

    private static void Register(Type type)
    {
        if (type.IsAbstract || type.IsInterface)
            return;

        var baseType = type.BaseType;
        if (baseType!.IsGenericType &&
            baseType.GetGenericTypeDefinition() == typeof(ResourceDecoder<>))
        {
            if (Activator.CreateInstance(type) is IResourceDecoder instance)
            {
                var genericArg = baseType.GetGenericArguments()[0];
                DECODERS[genericArg] = instance;
            }
        }
    }

    public static T Decode<T>(ResourceBin bin)
    {
        if (DECODERS.TryGetValue(typeof(T), out var decoder))
        {
            return (T)decoder.Decode(bin);
        }
        
        throw new ArgumentException($"Decoding {typeof(T).Name} failed.");
    }
}
