using System;

namespace Inno.Assets.Serializer;

/// <summary>
/// This attribute specifies property that will be written and loaded in the yaml file.
/// The attributed property should ALWAYS have getter and setter.
/// </summary>
/// <param name="readOnly">true if this property CANNOT be set by the read yaml file.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class AssetPropertyAttribute(bool readOnly = false) : Attribute
{
    public bool @readonly { get; } = readOnly;
}
