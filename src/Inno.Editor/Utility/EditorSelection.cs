using Inno.Core.ECS;
using Inno.Core.Logging;

namespace Inno.Editor.Utility;

public class EditorSelection
{
    public delegate void SelectionChangedHandler(GameObject? oldObj, GameObject? newObj);
    public event SelectionChangedHandler? OnSelectionChanged;
    
    private GameObject? m_selectedObject;
    public GameObject? selectedObject => m_selectedObject;

    public void Select(GameObject obj)
    {
        Log.Debug("Selecting " + obj.name);
        
        if (m_selectedObject != obj)
        {
            var old = m_selectedObject;
            m_selectedObject = obj;
            OnSelectionChanged?.Invoke(old, obj);
        }
    }

    public void Deselect()
    {
        if (m_selectedObject != null)
        {
            var old = m_selectedObject;
            m_selectedObject = null;
            OnSelectionChanged?.Invoke(old, null);
        }
    }

    public bool IsSelected(GameObject obj)
    {
        return m_selectedObject == obj;
    }
}

