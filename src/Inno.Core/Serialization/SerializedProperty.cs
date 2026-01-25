using System;

namespace Inno.Core.Serialization;

/// <summary>
/// Represents a discoverable property in the editor and in the serialization pipeline.
/// </summary>
public sealed class SerializedProperty
{
    #region Public Types

    /// <summary>
    /// Defines member participation rules for serialization and editor exposure.
    /// </summary>
    public enum PropertyVisibility
    {
        Show,
        Hide,
        ReadOnly
    }

    #endregion

    #region Backing Delegates

    private readonly Func<object?> m_getter;
    private readonly Action<object?> m_setter;

    #endregion

    #region Public State

    /// <summary>
    /// Gets the display and serialization key name.
    /// </summary>
    public string name { get; }

    /// <summary>
    /// Gets the declared CLR type of this property.
    /// </summary>
    public Type propertyType { get; }

    /// <summary>
    /// Gets the visibility of this property.
    /// </summary>
    public PropertyVisibility visibility { get; }

    #endregion

    #region Construction

    internal SerializedProperty(
        string name,
        Type propertyType,
        Func<object?> getter,
        Action<object?> setter,
        PropertyVisibility visibility)
    {
        this.name = name;
        this.propertyType = propertyType;
        this.visibility = visibility;
        m_getter = getter;
        m_setter = setter;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Gets the current value.
    /// </summary>
    /// <returns>The value produced by the getter delegate.</returns>
    /// <example>
    /// <code>
    /// var v = prop.GetValue();
    /// </code>
    /// </example>
    public object? GetValue() => m_getter();

    /// <summary>
    /// Sets the current value.
    /// </summary>
    /// <param name="value">The value to assign.</param>
    /// <remarks>
    /// For <see cref="PropertyVisibility.ReadOnly"/> members, the underlying setter may be a no-op.
    /// </remarks>
    /// <example>
    /// <code>
    /// prop.SetValue(123);
    /// </code>
    /// </example>
    public void SetValue(object? value) => m_setter(value);

    #endregion
}
