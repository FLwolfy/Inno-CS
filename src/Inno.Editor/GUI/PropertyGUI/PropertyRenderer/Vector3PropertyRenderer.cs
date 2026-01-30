using System;
using Inno.Core.Math;

namespace Inno.Editor.GUI.PropertyGUI.PropertyRenderer;

public class Vector3PropertyRenderer :  PropertyRenderer<Vector3>
{
    protected override void Bind(string name, Func<Vector3> getter, Action<Vector3> setter, bool enabled)
    {
        Vector3 value = getter.Invoke();
        if (EditorGUILayout.Vector3Field(name, ref value, enabled))
        {
            setter.Invoke(value);
        }
    }
}