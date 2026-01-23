using System;

namespace Inno.Core.Serialization;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class SerializablePropertyAttribute(
    SerializedProperty.PropertyVisibility visibility = SerializedProperty.PropertyVisibility.Show)
    : Attribute
{
    /// <summary>
    /// The visibility of the property.
    /// </summary>
    public SerializedProperty.PropertyVisibility propertyVisibility { get; } = visibility;
}