using System;
using System.Collections.Generic;

using Inno.Assets.AssetType;
using Inno.Core.Serialization;

using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.TypeInspectors;

namespace Inno.Assets.Core;

internal sealed class AssetPropertyTypeInspector(ITypeInspector inner) : TypeInspectorSkeleton
{
    private readonly ITypeInspector m_inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public override string GetEnumName(Type enumType, string name)
    {
        // Delegate to the inner inspector for consistent enum handling.
        return m_inner.GetEnumName(enumType, name);
    }

    public override string GetEnumValue(object enumValue)
    {
        // Delegate to the inner inspector for consistent enum handling.
        return m_inner.GetEnumValue(enumValue);
    }

    public override IEnumerable<IPropertyDescriptor> GetProperties(Type type, object? container)
    {
        // Non-assets: default behavior
        if (!typeof(InnoAsset).IsAssignableFrom(type))
            return m_inner.GetProperties(type, container);

        // Assets: expose only envelope fields, do NOT reflect-expand the object graph.
        return
        [
            new LambdaPropertyDescriptor(
                name: "$type",
                propertyType: typeof(string),
                order: 0,
                getter: o =>
                {
                    var t = o.GetType();
                    return t.AssemblyQualifiedName ?? t.FullName ?? t.Name;
                }
            ),
            new LambdaPropertyDescriptor(
                name: "$state",
                propertyType: typeof(object),
                order: 1,
                getter: o =>
                {
                    if (o is not ISerializable ser)
                        throw new InvalidOperationException($"{o.GetType().FullName} must implement ISerializable.");

                    // This should be a YAML-friendly tagged node tree (Dictionary<string, object?> etc.)
                    return SerializingStateYamlCodec.EncodeState(ser.CaptureState());
                }
            )
        ];
    }

    private sealed class LambdaPropertyDescriptor(
        string name,
        Type propertyType,
        int order,
        Func<object, object?> getter)
        : IPropertyDescriptor
    {
        private readonly Func<object, object?> m_getter = getter ?? throw new ArgumentNullException(nameof(getter));

        public string Name { get; set; } = name;

        // For our virtual props, allow nulls (safe for $state when empty).
        public bool AllowNulls => true;

        public Type Type { get; set; } = propertyType;
        public Type? TypeOverride { get; set; }
        public int Order { get; set; } = order;
        public ScalarStyle ScalarStyle { get; set; } = ScalarStyle.Any;

        // We are not mapping these from attributes; defaults are fine.
        public bool Required => false;
        public Type? ConverterType => null;

        // We intentionally prevent writing via YamlDotNet reflection route.
        public bool CanWrite => false;

        IObjectDescriptor IPropertyDescriptor.Read(object target)
        {
            var value = m_getter(target);

            // Use ObjectDescriptor so YamlDotNet knows the runtime/static type.
            // - value can be null
            // - property Type is the declared type for this virtual property
            var staticType = TypeOverride ?? Type;
            var actualType = value?.GetType() ?? staticType;

            return new ObjectDescriptor(value, actualType, staticType);
        }

        public void Write(object target, object? value)
        {
            // no-op (CanWrite=false)
        }

        public T? GetCustomAttribute<T>() where T : Attribute => null;
    }
}
