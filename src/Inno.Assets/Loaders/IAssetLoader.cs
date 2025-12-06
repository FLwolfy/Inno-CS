using Inno.Assets.AssetTypes;
using Inno.Assets.Serializers;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Inno.Assets.Loaders;

/// <summary>
/// Interface for asset loaders
/// </summary>
internal interface IAssetLoader
{
    protected static readonly IDeserializer DESERIALIZER = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IncludeNonPublicProperties()
        .WithTypeInspector(i => new AssetPropertyTypeInspector())
        .WithObjectFactory(new AssetObjectFactory())
        .IgnoreUnmatchedProperties()
        .Build();

    protected static readonly ISerializer SERIALIZER = new SerializerBuilder()
        .IncludeNonPublicProperties()
        .WithTypeInspector(i => new AssetPropertyTypeInspector())
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();
    
    /// <summary>
    /// Load the asset from disk / raw file
    /// </summary>
    InnoAsset? Load(string path);
}