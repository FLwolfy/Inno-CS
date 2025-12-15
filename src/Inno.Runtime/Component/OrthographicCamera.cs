using Inno.Core.ECS;
using Inno.Core.Math;
using Inno.Core.Serialization;

namespace Inno.Runtime.Component;

/// <summary>
/// Orthographic (2D) camera component.
/// </summary>
public class OrthographicCamera : GameCamera
{
    private const float C_NEAR = 0f;
    private const float C_FAR = 1f;

    private float m_size = 1080f;

    /// <summary>
    /// The size of the camera's view in world units.
    /// </summary>
    [SerializableProperty]
    public float size
    {
        get => m_size;
        set
        {
            if (!MathHelper.AlmostEquals(m_size, value))
            {
                m_size = value;
                MarkDirty();
            }
        }
    }
    
    protected override void RebuildMatrix(out Matrix view, out Matrix projection, out Rect visibleRect)
    {
        Vector2 cameraPos = new Vector2(
            transform.worldPosition.x, 
            transform.worldPosition.y
        );
        float rotationZ = transform.worldRotation.ToEulerAnglesZYX().z;

        projection = CalculateProjectionMatrix();
        view = CalculateViewMatrix(cameraPos, rotationZ);
        visibleRect = CalculateViewRect(cameraPos, rotationZ);
    }

    private Matrix CalculateProjectionMatrix()
    {
        float halfHeight = m_size * 0.5f;
        float halfWidth = halfHeight * aspectRatio;

        return Matrix.CreateOrthographic(
            width: halfWidth * 2,
            height: halfHeight * 2,
            C_NEAR,
            C_FAR
        );
    }

    private Matrix CalculateViewMatrix(Vector2 cameraPos, float rotationZ)
    {
        Matrix rotation = Matrix.CreateRotationZ(-rotationZ);
        Matrix translation = Matrix.CreateTranslation(-cameraPos.x, -cameraPos.y, 0f);
        return translation * rotation;
    }

    private Rect CalculateViewRect(Vector2 cameraPos, float rotationZ)
    {
        float halfHeight = m_size * 0.5f;
        float halfWidth = halfHeight * aspectRatio;

        Vector2[] corners =
        [
            new(-halfWidth, -halfHeight),
            new(halfWidth, -halfHeight),
            new(halfWidth, halfHeight),
            new(-halfWidth, halfHeight)
        ];

        float cos = MathF.Cos(rotationZ);
        float sin = MathF.Sin(rotationZ);

        for (int i = 0; i < corners.Length; i++)
        {
            float x = corners[i].x;
            float y = corners[i].y;

            corners[i] = new Vector2(
                x * cos - y * sin,
                x * sin + y * cos
            ) + cameraPos;
        }

        float minX = corners.Min(c => c.x);
        float maxX = corners.Max(c => c.x);
        float minY = corners.Min(c => c.y);
        float maxY = corners.Max(c => c.y);

        return new Rect(
            x: (int)minX,
            y: (int)minY,
            width: (int)(maxX - minX),
            height: (int)(maxY - minY)
        );
    }
}