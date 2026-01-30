using System;
using System.Linq;
using Inno.Core.ECS;
using Inno.Core.Math;
using Inno.Core.Serialization;

namespace Inno.Runtime.Component;

/// <summary>
/// Perspective (2.5D/3D) camera component.
///
/// IMPORTANT:
/// - This camera is LEFT-HANDED (+Z is forward) to match current sprite Z usage (0..1).
/// - Projection depth range is 0..1 to match Matrix.CreateOrthographic.
/// </summary>
public sealed class PerspectiveCamera : GameCamera
{
    // Must be > 0 for perspective.
    private const float C_MIN_NEAR = 0.01f;
    private const float C_MIN_FOV  = 1f;
    private const float C_MAX_FOV  = 179f;

    private float m_fovDegrees = 60f;
    private float m_near = 0.1f;
    private float m_far  = 500f;

    // Used only to compute a reasonable 2D viewRect (for culling tools, debug, etc.)
    // This is the distance from camera to the "reference plane" along +Z.
    private float m_focusDistance = 10f;

    [SerializableProperty]
    public float fovDegrees
    {
        get => m_fovDegrees;
        set
        {
            float v = Math.Clamp(value, C_MIN_FOV, C_MAX_FOV);
            if (!MathHelper.AlmostEquals(m_fovDegrees, v))
            {
                m_fovDegrees = v;
                MarkDirty();
            }
        }
    }

    [SerializableProperty]
    public float nearClip
    {
        get => m_near;
        set
        {
            float v = Math.Max(C_MIN_NEAR, value);
            if (!MathHelper.AlmostEquals(m_near, v))
            {
                m_near = v;
                // keep far valid
                if (m_far <= m_near + 0.001f) m_far = m_near + 0.001f;
                MarkDirty();
            }
        }
    }

    [SerializableProperty]
    public float farClip
    {
        get => m_far;
        set
        {
            float v = Math.Max(m_near + 0.001f, value);
            if (!MathHelper.AlmostEquals(m_far, v))
            {
                m_far = v;
                MarkDirty();
            }
        }
    }

    /// <summary>
    /// Distance to the reference plane used to approximate viewRect.
    /// This does NOT affect projection; it is only for 2D rectangle estimation.
    /// </summary>
    [SerializableProperty]
    public float focusDistance
    {
        get => m_focusDistance;
        set
        {
            float v = Math.Max(0.01f, value);
            if (!MathHelper.AlmostEquals(m_focusDistance, v))
            {
                m_focusDistance = v;
                MarkDirty();
            }
        }
    }

    protected override void RebuildMatrix(out Matrix view, out Matrix projection, out Rect visibleRect)
    {
        Vector3 cameraPos = transform.worldPosition;

        // NOTE: Keep the same "style" as OrthographicCamera:
        // view = translation * rotation (using inverse rotation).
        var invRot = Quaternion.Inverse(transform.worldRotation);
        projection = CalculateProjectionMatrixLH();
        view       = CalculateViewMatrix(cameraPos, invRot);

        // Approximate a 2D view rect on a plane at focusDistance along +Z.
        // (This is mainly useful for tools/culling; true 3D frustum culling is different.)
        visibleRect = CalculateViewRect2D(cameraPos, transform.worldRotation);
    }

    private Matrix CalculateViewMatrix(Vector3 cameraPos, Quaternion invRot)
    {
        Matrix rotation    = Matrix.CreateFromQuaternion(invRot);
        Matrix translation = Matrix.CreateTranslation(-cameraPos.x, -cameraPos.y, -cameraPos.z);
        return translation * rotation;
    }

    /// <summary>
    /// Left-handed perspective projection, depth range 0..1.
    /// This matches Matrix.CreateOrthographic's depth convention and current sprite Z usage.
    /// </summary>
    private Matrix CalculateProjectionMatrixLH()
    {
        float aspect = aspectRatio;
        if (aspect <= 0.0001f || float.IsNaN(aspect) || float.IsInfinity(aspect))
            aspect = 16f / 9f;

        float fovRad = m_fovDegrees * (MathF.PI / 180f);
        float f      = 1f / MathF.Tan(fovRad * 0.5f);
        float nf     = 1f / (m_far - m_near);

        // LH (+Z forward), NDC depth 0..1
        // Row-vector layout (translation in last row) consistent with Matrix.CreateTranslation. :contentReference[oaicite:2]{index=2}
        return new Matrix(
            f / aspect, 0, 0, 0,
            0,          f, 0, 0,
            0,          0, m_far * nf,  1,
            0,          0, -m_near * m_far * nf, 0
        );
    }

    /// <summary>
    /// Approximates a 2D world-rect by projecting the frustum onto a plane at focusDistance.
    /// Similar in spirit to OrthographicCamera.CalculateViewRect. :contentReference[oaicite:3]{index=3}
    /// </summary>
    private Rect CalculateViewRect2D(Vector3 cameraPos3D, Quaternion cameraRot)
    {
        Vector2 cameraPos = new Vector2(cameraPos3D.x, cameraPos3D.y);

        float fovRad = m_fovDegrees * (MathF.PI / 180f);
        float halfHeight = MathF.Tan(fovRad * 0.5f) * m_focusDistance;
        float halfWidth  = halfHeight * aspectRatio;

        Vector2[] corners =
        [
            new(-halfWidth, -halfHeight),
            new( halfWidth, -halfHeight),
            new( halfWidth,  halfHeight),
            new(-halfWidth,  halfHeight)
        ];

        // If you only rotate in Z for 2D cameras, this matches your OrthographicCamera approach.
        float rotationZ = cameraRot.ToEulerAnglesZYX().z;
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

        int x0 = (int)MathF.Floor(minX);
        int y0 = (int)MathF.Floor(minY);
        int x1 = (int)MathF.Ceiling(maxX);
        int y1 = (int)MathF.Ceiling(maxY);

        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }
}
