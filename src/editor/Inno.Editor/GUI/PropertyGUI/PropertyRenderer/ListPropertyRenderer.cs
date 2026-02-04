using System;
using System.Collections.Generic;

namespace Inno.Editor.GUI.PropertyGUI.PropertyRenderer;

public class ListPropertyRenderer<T> : PropertyRenderer<List<T>>
{
    private IPropertyRenderer? m_renderer;
    
    protected override void Bind(string name, Func<List<T>?> getter, Action<List<T>> setter, bool enabled)
    {
        if (m_renderer == null) { PropertyRendererRegistry.TryGetRenderer(typeof(T), out m_renderer); }
        
        // TODO
    }
}