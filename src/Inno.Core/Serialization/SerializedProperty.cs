using System;

namespace Inno.Core.Serialization;

public class SerializedProperty
{
    /// <summary>
    /// The visibility Enum of the property in the editor.
    /// </summary>
    public enum PropertyVisibility { Show, Hide, ReadOnly, }
    
    private readonly Func<object?> m_getter;
    private readonly Action<object?> m_setter;
    
    /// <summary>
    /// The name of the property.
    /// </summary>
    public string name { get; }
    
    /// <summary>
    /// The type of the property.
    /// </summary>
    public Type propertyType { get; }
    
    /// <summary>
    /// The visibility of the property.
    /// </summary>
    public PropertyVisibility visibility { get; }

    internal SerializedProperty(string name, Type propertyType, Func<object?> getter, Action<object?> setter,  PropertyVisibility visibility)
    {
        this.name = name;
        this.propertyType = propertyType;
        this.visibility = visibility;
        
        m_getter = getter;
        m_setter = setter;
    }
    
    /// <summary>
    /// Gets the value of the property.
    /// </summary>
    public object? GetValue()
    {
        return m_getter.Invoke();
    }

    /// <summary>
    /// Sets the value of the property.
    /// </summary>
    public void SetValue(object? value)
    {
        m_setter.Invoke(value);
    }
}
