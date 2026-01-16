using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Inno.Core.Serialization;

public abstract class Serializable
{
    /// <summary>
    /// Gets all serialized properties of this Serializable instance.
    /// </summary>
    public IReadOnlyList<SerializedProperty> GetSerializedProperties()
    {
        var result = new List<SerializedProperty>();
        var properties = GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.IsDefined(typeof(SerializablePropertyAttribute), true))
            .OrderBy(p => p.MetadataToken);

        foreach (var property in properties)
        {
            result.Add(new SerializedProperty
            (
                property.Name, 
                property.PropertyType, 
                () => property.GetValue(this),
                (val) => property.SetValue(this, val),
                property.GetCustomAttribute<SerializablePropertyAttribute>(true)!.propertyVisibility
            ));
        }

        return result;
    }

}