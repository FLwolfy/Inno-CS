using System;

namespace Inno.Editor.GUI.PropertyGUI.PropertyRenderer;

public class FloatPropertyRenderer : PropertyRenderer<float>
{
    protected override void Bind(string name, Func<float> getter, Action<float> setter, bool enabled)
    {
        float value = getter.Invoke();
        if (EditorGUILayout.FloatField(name, ref value, enabled))
        {
            setter.Invoke(value);
        }
    }
}