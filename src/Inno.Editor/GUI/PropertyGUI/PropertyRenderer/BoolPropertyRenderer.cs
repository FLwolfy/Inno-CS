using System;

namespace Inno.Editor.GUI.PropertyGUI.PropertyRenderer;

public class BoolPropertyRenderer : PropertyRenderer<bool>
{
    protected override void Bind(string name, Func<bool> getter, Action<bool> setter, bool enabled)
    {
        bool value = getter.Invoke();
        if (EditorGUILayout.Checkbox(name, ref value, enabled))
        {
            setter.Invoke(value);
        }
    }
}