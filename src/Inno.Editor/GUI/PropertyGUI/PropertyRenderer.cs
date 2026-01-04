namespace Inno.Editor.GUI.PropertyGUI;

public interface IPropertyRenderer
{
    /// <summary>
    /// Binds the property renderer to a property with the specified name.
    /// This method is used to connect the property renderer to a property in the target object.
    /// </summary>
    void Bind(string name, Func<object?> getter, Action<object?> setter, bool enabled);
}

public abstract class PropertyRenderer<T> : IPropertyRenderer
{
    /// <summary>
    /// Method to bind the property renderer to a property with the specified Type T.
    /// </summary>
    protected abstract void Bind(string name, Func<T?> getter, Action<T?> setter, bool enabled);

    void IPropertyRenderer.Bind(string name, Func<object?> getter, Action<object?> setter, bool enabled)
    {
        Bind(name, () => (T?)getter.Invoke(), val => setter.Invoke(val), enabled);
    }
}