using System;
using System.Collections.Generic;
using Inno.Assets.AssetType;
using Inno.Core.Utility;

namespace Inno.Graphics.Decoder;

internal static class DecoderRegistry
{
    private static readonly Dictionary<(Type, Type), IResourceDecoder> DECODERS = new();
    
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
        if (baseType!.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(ResourceDecoder<,>))
        {
            if (Activator.CreateInstance(type) is IResourceDecoder instance)
            {
                var genericArg0 = baseType.GetGenericArguments()[0];
                var genericArg1 = baseType.GetGenericArguments()[1];
                
                DECODERS[(genericArg0, genericArg1)] = instance;
            }
        }
    }

    public static T Decode<T, TAsset>(TAsset bin) 
        where T : notnull
        where TAsset : InnoAsset
    {
        if (DECODERS.TryGetValue((typeof(T), typeof(TAsset)), out var decoder))
        {
            return (T)decoder.Decode(bin);
        }
        
        throw new ArgumentException($"Decoding {typeof(T).Name} from {typeof(TAsset).Name} failed.");
    }
}
