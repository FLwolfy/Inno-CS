using Inno.Assets.AssetType;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Inno.Assets.Core;

internal static class AssetYamlSerializer
{
    private static readonly ISerializer SERIALIZER = new SerializerBuilder()
        .IncludeNonPublicProperties()
        .WithTypeInspector(_ => new AssetPropertyTypeInspector())
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();
    
    private static readonly IDeserializer DESERIALIZER = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IncludeNonPublicProperties()
        .WithTypeInspector(_ => new AssetPropertyTypeInspector())
        .WithObjectFactory(new AssetObjectFactory())
        .IgnoreUnmatchedProperties()
        .Build();

    public static T DeserializeFromYaml<T>(string yamlString) where T : InnoAsset
    {
        return DESERIALIZER.Deserialize<T>(yamlString);
    }

    public static string SerializeToYaml(InnoAsset innoAsset)
    {
        return SERIALIZER.Serialize(innoAsset);
    }
}