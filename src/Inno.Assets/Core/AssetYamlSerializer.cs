using System;

using Inno.Core.Serialization;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Inno.Assets.Core;

internal static class AssetYamlSerializer
{
    private static readonly ISerializer YAML_WRITER = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer YAML_READER = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    // ---------------------------------------------------------------------
    // Raw SerializingState <-> YAML
    // ---------------------------------------------------------------------

    public static string SerializeStateToYaml(SerializingState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        return YAML_WRITER.Serialize(SerializingStateYamlCodec.EncodeState(state));
    }

    public static SerializingState DeserializeStateFromYaml(string yamlString)
    {
        if (yamlString == null) throw new ArgumentNullException(nameof(yamlString));

        var parsed = YAML_READER.Deserialize<object>(yamlString);
        parsed = SerializingStateYamlCodec.NormalizeYamlObject(parsed)
                 ?? throw new InvalidOperationException("YAML is empty.");

        return SerializingStateYamlCodec.DecodeState(parsed);
    }
}
