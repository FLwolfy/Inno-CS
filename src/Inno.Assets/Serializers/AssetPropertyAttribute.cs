namespace Inno.Assets.Serializers;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class AssetPropertyAttribute(bool readOnly = false) : Attribute
{
    public bool @readonly { get; } = readOnly;
}
