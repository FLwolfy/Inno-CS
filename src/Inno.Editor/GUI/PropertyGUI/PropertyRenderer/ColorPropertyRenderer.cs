using System;
using Inno.Core.Math;

namespace Inno.Editor.GUI.PropertyGUI.PropertyRenderer;

public class ColorPropertyRenderer : PropertyRenderer<Color>
{
    protected override void Bind(string name, Func<Color> getter, Action<Color> setter, bool enabled)
    {
        Color input = getter.Invoke();
        if (EditorGUILayout.ColorField(name, in input, out var output, enabled))
        {
            setter.Invoke(output);
        }
    }
}