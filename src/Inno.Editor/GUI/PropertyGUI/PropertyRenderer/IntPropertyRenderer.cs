using System;

namespace Inno.Editor.GUI.PropertyGUI.PropertyRenderer;

public class IntPropertyRenderer : PropertyRenderer<int>
{
    protected override void Bind(string name, Func<int> getter, Action<int> setter, bool enabled)
    {
        int value = getter.Invoke();
        if (EditorGuiLayout.IntField(name, ref value, enabled))
        {
            setter.Invoke(value);
        }
    }
}