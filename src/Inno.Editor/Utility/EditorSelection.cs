using System.Runtime.Serialization;
using Inno.Core.Serialization;

namespace Inno.Editor.Utility;

public class EditorSelection
{
    public Serializable? selectedObject { get; private set; }
    public delegate void SelectionChangedHandler(Serializable? oldObj, Serializable? newObj);
    public event SelectionChangedHandler? OnSelectionChanged;

    public void Select(Serializable obj)
    {
        if (selectedObject != obj)
        {
            var old = selectedObject;
            selectedObject = obj;
            OnSelectionChanged?.Invoke(old, obj);
        }
    }

    public void Deselect()
    {
        if (selectedObject != null)
        {
            var old = selectedObject;
            selectedObject = null;
            OnSelectionChanged?.Invoke(old, null);
        }
    }

    public bool IsSelected(object obj)
    {
        return selectedObject == obj;
    }
}

