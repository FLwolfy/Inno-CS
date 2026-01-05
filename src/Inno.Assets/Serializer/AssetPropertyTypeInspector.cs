using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.TypeInspectors;

namespace Inno.Assets.Serializer;

public class AssetPropertyTypeInspector : TypeInspectorSkeleton
{
    public override string GetEnumName(Type enumType, string name) => name;
    public override string GetEnumValue(object enumValue) => enumValue.ToString()!;

    public override IEnumerable<IPropertyDescriptor> GetProperties(Type type, object? container)
    {
        foreach (var prop in GetAllProperties(type))
        {
            var attr = prop.GetCustomAttribute<AssetPropertyAttribute>();
            if (attr == null) continue;

            if (attr.@readonly)
            {
                yield return new ReadonlyPropertyDescriptor(prop);
            }
            else
            {
                yield return new ForceSetPropertyDescriptor(prop);
            }
        }
    }

    private static IEnumerable<PropertyInfo> GetAllProperties(Type type)
    {
        var stack = new Stack<Type>();
        var t = type;
        while (t != null && t != typeof(object))
        {
            stack.Push(t);
            t = t.BaseType;
        }

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var p in current.GetProperties(
                             BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .OrderBy(p => p.MetadataToken))
            {
                yield return p;
            }
        }
    }


    private class ReadonlyPropertyDescriptor(PropertyInfo property) : IPropertyDescriptor
    {
        public string Name => property.Name;
        public bool AllowNulls => true;
        public bool CanWrite => false;
        public Type Type => property.PropertyType;
        public Type? TypeOverride { get; set; }
        public int Order { get; set; }
        public ScalarStyle ScalarStyle { get; set; }
        public bool Required => false;
        public Type? ConverterType => null;

        public T? GetCustomAttribute<T>() where T : Attribute => property.GetCustomAttribute<T>();

        public IObjectDescriptor Read(object target)
        {
            var value = property.GetValue(target);
            return new ObjectDescriptor(value, property.PropertyType, property.PropertyType);
        }

        public void Write(object target, object? value)
        {
            // DO NOTHING
        }
    }

    private class ForceSetPropertyDescriptor(PropertyInfo property) : IPropertyDescriptor
    {
        public string Name => property.Name;
        public bool AllowNulls => true;
        public bool CanWrite => true;
        public Type Type => property.PropertyType;
        public Type? TypeOverride { get; set; }
        public int Order { get; set; }
        public ScalarStyle ScalarStyle { get; set; }
        public bool Required => false;
        public Type? ConverterType => null;

        public T? GetCustomAttribute<T>() where T : Attribute => property.GetCustomAttribute<T>();

        public IObjectDescriptor Read(object target)
        {
            var value = property.GetValue(target);
            return new ObjectDescriptor(value, property.PropertyType, property.PropertyType);
        }

        public void Write(object target, object? value)
        {
            property.SetValue(target, value);
        }
    }
}