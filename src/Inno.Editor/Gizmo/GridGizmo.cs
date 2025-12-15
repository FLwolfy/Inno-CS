using Inno.Core.Math;
using Inno.Editor.Core;
using Inno.Platform.ImGui;

namespace Inno.Editor.Gizmo;

public class GridGizmo : EditorGizmo
{
    public Vector2 startPos = Vector2.ZERO;
    public Vector2 size = Vector2.ZERO;
    public Vector2 offset = Vector2.ZERO;
    public Color color = Color.GRAY;
    public float spacing = 100;
    public float lineThickness = 1f;
    
    public bool showCoords = false;
    public Vector2 startCoords = Vector2.ZERO;
    public Vector2 coordsIncrement = Vector2.ONE;
    
    internal override void Draw()
    {
        if (!isVisible || spacing <= 0f) return;

        Vector2 bottomRightBounds = startPos + size; // Bottom-right bounds
        Vector2 axisTopLeft = startPos + offset;     // Top-left
    
        int xIndex = 0;
        for (float i = axisTopLeft.x; i < bottomRightBounds.x; i += spacing, xIndex++)
        {
            EditorImGuiEx.DrawLine(
                new Vector2(i, startPos.y),
                new Vector2(i, bottomRightBounds.y),
                color,
                lineThickness);
        
            if (showCoords)
            {
                string label = $"{startCoords.x + xIndex * coordsIncrement.x:0.##}";
                EditorImGuiEx.DrawText(new Vector2(i + 2, startPos.y + 2), label, color);
            }
        }

        int yIndex = 0;
        for (float j = axisTopLeft.y; j < bottomRightBounds.y; j += spacing, yIndex++)
        {
            EditorImGuiEx.DrawLine(
                new Vector2(startPos.x, j),
                new Vector2(bottomRightBounds.x, j),
                color,
                lineThickness);
        
            if (showCoords)
            {
                string label = $"{startCoords.y - yIndex * coordsIncrement.y:0.##}"; // Here use MINUS because ImGui Y increases downward
                EditorImGuiEx.DrawText(new Vector2(startPos.x + 2, j + 2), label, color);
            }
        }
    }

}