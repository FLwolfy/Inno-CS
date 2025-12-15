using System.Runtime.Serialization;

namespace Inno.Core.Math;

[DataContract]
public struct Matrix : IEquatable<Matrix>
{
    [DataMember] public float m11, m12, m13, m14;
    [DataMember] public float m21, m22, m23, m24;
    [DataMember] public float m31, m32, m33, m34;
    [DataMember] public float m41, m42, m43, m44;

    public Matrix(
        float m11, float m12, float m13, float m14,
        float m21, float m22, float m23, float m24,
        float m31, float m32, float m33, float m34,
        float m41, float m42, float m43, float m44)
    {
        this.m11 = m11; this.m12 = m12; this.m13 = m13; this.m14 = m14;
        this.m21 = m21; this.m22 = m22; this.m23 = m23; this.m24 = m24;
        this.m31 = m31; this.m32 = m32; this.m33 = m33; this.m34 = m34;
        this.m41 = m41; this.m42 = m42; this.m43 = m43; this.m44 = m44;
    }

    public static Matrix identity => new Matrix(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1);

    public static Matrix CreateTranslation(float x, float y, float z)
    {
        return new Matrix(
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            x, y, z, 1);
    }

    public static Matrix CreateTranslation(Vector3 v)
        => CreateTranslation(v.x, v.y, v.z);

    public static Matrix CreateScale(float scale)
        => CreateScale(scale, scale, scale);

    public static Matrix CreateScale(float x, float y, float z)
    {
        return new Matrix(
            x, 0, 0, 0,
            0, y, 0, 0,
            0, 0, z, 0,
            0, 0, 0, 1);
    }

    public static Matrix CreateScale(Vector3 v)
        => CreateScale(v.x, v.y, v.z);

    public static Matrix CreateRotationX(float radians)
    {
        float c = MathF.Cos(radians);
        float s = MathF.Sin(radians);
        return new Matrix(
            1, 0, 0, 0,
            0, c, s, 0,
            0, -s, c, 0,
            0, 0, 0, 1);
    }

    public static Matrix CreateRotationY(float radians)
    {
        float c = MathF.Cos(radians);
        float s = MathF.Sin(radians);
        return new Matrix(
            c, 0, -s, 0,
            0, 1, 0, 0,
            s, 0, c, 0,
            0, 0, 0, 1);
    }

    public static Matrix CreateRotationZ(float radians)
    {
        float c = MathF.Cos(radians);
        float s = MathF.Sin(radians);
        return new Matrix(
            c, s, 0, 0,
            -s, c, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);
    }

    public static Matrix CreateFromQuaternion(Quaternion q)
    {
        float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z;
        float xy = q.x * q.y, xz = q.x * q.z, yz = q.y * q.z;
        float wx = q.w * q.x, wy = q.w * q.y, wz = q.w * q.z;

        return new Matrix(
            1 - 2 * (yy + zz), 2 * (xy + wz),     2 * (xz - wy),     0,
            2 * (xy - wz),     1 - 2 * (xx + zz), 2 * (yz + wx),     0,
            2 * (xz + wy),     2 * (yz - wx),     1 - 2 * (xx + yy), 0,
            0,                 0,                 0,                 1);
    }

    public static Matrix CreatePerspectiveFieldOfView(float fov, float aspect, float near, float far)
    {
        float f = 1f / MathF.Tan(fov / 2);
        float nf = 1f / (near - far);

        return new Matrix(
            f / aspect, 0, 0,                         0,
            0,          f, 0,                         0,
            0,          0, (far + near) * nf,        -1,
            0,          0, (2 * far * near) * nf,     0);
    }

    /// <summary>
    /// Creates an orthographic projection matrix with the given width, height, near, and far planes.
    /// This matrix maps the X and Y coordinates linearly to [-1, 1] (NDC),
    /// and maps the Z coordinate linearly from [near, far] to [0, 1] (depth buffer range).
    ///
    /// <p>
    /// Y-axis is oriented upwards, i.e., +Y is the top of the view.
    /// </p>
    /// 
    /// This is suitable for APIs like Veldrid/Direct3D/Vulkan where the depth buffer expects 0-1.
    /// </summary>
    /// <param name="width">The width of the orthographic view.</param>
    /// <param name="height">The height of the orthographic view.</param>
    /// <param name="near">The near plane in camera space.</param>
    /// <param name="far">The far plane in camera space.</param>
    /// <returns>A Matrix representing the orthographic projection.</returns>
    public static Matrix CreateOrthographic(float width, float height, float near, float far)
    {
        float m00 = 2f / width;
        float m11 = 2f / height;
        float m22 = 1f / (far - near);
        float m32 = -near / (far - near);

        return new Matrix(
            m00, 0,   0, 0,
            0,   m11, 0, 0,
            0,   0,   m22, 0,
            0,   0,   m32, 1
        );
    }

    public static Matrix CreateLookAt(Vector3 eye, Vector3 target, Vector3 up)
    {
        Vector3 z = (eye - target).normalized;
        Vector3 x = Vector3.Cross(up, z).normalized;
        Vector3 y = Vector3.Cross(z, x);

        return new Matrix(
            x.x, y.x, z.x, 0,
            x.y, y.y, z.y, 0,
            x.z, y.z, z.z, 0,
            -Vector3.Dot(x, eye), -Vector3.Dot(y, eye), -Vector3.Dot(z, eye), 1);
    }

    public static Matrix Multiply(Matrix a, Matrix b)
    {
        return new Matrix(
            a.m11 * b.m11 + a.m12 * b.m21 + a.m13 * b.m31 + a.m14 * b.m41,
            a.m11 * b.m12 + a.m12 * b.m22 + a.m13 * b.m32 + a.m14 * b.m42,
            a.m11 * b.m13 + a.m12 * b.m23 + a.m13 * b.m33 + a.m14 * b.m43,
            a.m11 * b.m14 + a.m12 * b.m24 + a.m13 * b.m34 + a.m14 * b.m44,

            a.m21 * b.m11 + a.m22 * b.m21 + a.m23 * b.m31 + a.m24 * b.m41,
            a.m21 * b.m12 + a.m22 * b.m22 + a.m23 * b.m32 + a.m24 * b.m42,
            a.m21 * b.m13 + a.m22 * b.m23 + a.m23 * b.m33 + a.m24 * b.m43,
            a.m21 * b.m14 + a.m22 * b.m24 + a.m23 * b.m34 + a.m24 * b.m44,

            a.m31 * b.m11 + a.m32 * b.m21 + a.m33 * b.m31 + a.m34 * b.m41,
            a.m31 * b.m12 + a.m32 * b.m22 + a.m33 * b.m32 + a.m34 * b.m42,
            a.m31 * b.m13 + a.m32 * b.m23 + a.m33 * b.m33 + a.m34 * b.m43,
            a.m31 * b.m14 + a.m32 * b.m24 + a.m33 * b.m34 + a.m34 * b.m44,

            a.m41 * b.m11 + a.m42 * b.m21 + a.m43 * b.m31 + a.m44 * b.m41,
            a.m41 * b.m12 + a.m42 * b.m22 + a.m43 * b.m32 + a.m44 * b.m42,
            a.m41 * b.m13 + a.m42 * b.m23 + a.m43 * b.m33 + a.m44 * b.m43,
            a.m41 * b.m14 + a.m42 * b.m24 + a.m43 * b.m34 + a.m44 * b.m44
        );
    }
    
    public static Matrix Extract2DTransform(Matrix m)
    {
        return new Matrix(
            m.m11, m.m12, 0, 0,
            m.m21, m.m22, 0, 0,
            0,     0,     1, 0,
            m.m41, m.m42, 0, 1
        );
    }
    
    public static Matrix Transpose(Matrix a)
    {
        return new Matrix(
            a.m11, a.m21, a.m31, a.m41,
            a.m12, a.m22, a.m32, a.m42,
            a.m13, a.m23, a.m33, a.m43,
            a.m14, a.m24, a.m34, a.m44
        );
    }
    
    public static Matrix Invert(Matrix m)
    {
        float a00 = m.m11, a01 = m.m12, a02 = m.m13, a03 = m.m14;
        float a10 = m.m21, a11 = m.m22, a12 = m.m23, a13 = m.m24;
        float a20 = m.m31, a21 = m.m32, a22 = m.m33, a23 = m.m34;
        float a30 = m.m41, a31 = m.m42, a32 = m.m43, a33 = m.m44;

        float b00 = a00 * a11 - a01 * a10;
        float b01 = a00 * a12 - a02 * a10;
        float b02 = a00 * a13 - a03 * a10;
        float b03 = a01 * a12 - a02 * a11;
        float b04 = a01 * a13 - a03 * a11;
        float b05 = a02 * a13 - a03 * a12;
        float b06 = a20 * a31 - a21 * a30;
        float b07 = a20 * a32 - a22 * a30;
        float b08 = a20 * a33 - a23 * a30;
        float b09 = a21 * a32 - a22 * a31;
        float b10 = a21 * a33 - a23 * a31;
        float b11 = a22 * a33 - a23 * a32;

        float det = b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06;
        if (MathF.Abs(det) < MathHelper.C_TOLERANCE)
            return identity; // No Inverse, return identity matrix

        float invDet = 1f / det;

        return new Matrix(
            (a11 * b11 - a12 * b10 + a13 * b09) * invDet,
            (-a01 * b11 + a02 * b10 - a03 * b09) * invDet,
            (a31 * b05 - a32 * b04 + a33 * b03) * invDet,
            (-a21 * b05 + a22 * b04 - a23 * b03) * invDet,

            (-a10 * b11 + a12 * b08 - a13 * b07) * invDet,
            (a00 * b11 - a02 * b08 + a03 * b07) * invDet,
            (-a30 * b05 + a32 * b02 - a33 * b01) * invDet,
            (a20 * b05 - a22 * b02 + a23 * b01) * invDet,

            (a10 * b10 - a11 * b08 + a13 * b06) * invDet,
            (-a00 * b10 + a01 * b08 - a03 * b06) * invDet,
            (a30 * b04 - a31 * b02 + a33 * b00) * invDet,
            (-a20 * b04 + a21 * b02 - a23 * b00) * invDet,

            (-a10 * b09 + a11 * b07 - a12 * b06) * invDet,
            (a00 * b09 - a01 * b07 + a02 * b06) * invDet,
            (-a30 * b03 + a31 * b01 - a32 * b00) * invDet,
            (a20 * b03 - a21 * b01 + a22 * b00) * invDet
        );
    }



    public static Matrix operator *(Matrix a, Matrix b) => Multiply(a, b);

    public static bool operator ==(Matrix matrix1, Matrix matrix2)
    {
        return
            MathHelper.AlmostEquals(matrix1.m11, matrix2.m11) &&
            MathHelper.AlmostEquals(matrix1.m12, matrix2.m12) &&
            MathHelper.AlmostEquals(matrix1.m13, matrix2.m13) &&
            MathHelper.AlmostEquals(matrix1.m14, matrix2.m14) &&
            MathHelper.AlmostEquals(matrix1.m21, matrix2.m21) &&
            MathHelper.AlmostEquals(matrix1.m22, matrix2.m22) &&
            MathHelper.AlmostEquals(matrix1.m23, matrix2.m23) &&
            MathHelper.AlmostEquals(matrix1.m24, matrix2.m24) &&
            MathHelper.AlmostEquals(matrix1.m31, matrix2.m31) &&
            MathHelper.AlmostEquals(matrix1.m32, matrix2.m32) &&
            MathHelper.AlmostEquals(matrix1.m33, matrix2.m33) &&
            MathHelper.AlmostEquals(matrix1.m34, matrix2.m34) &&
            MathHelper.AlmostEquals(matrix1.m41, matrix2.m41) &&
            MathHelper.AlmostEquals(matrix1.m42, matrix2.m42) &&
            MathHelper.AlmostEquals(matrix1.m43, matrix2.m43) &&
            MathHelper.AlmostEquals(matrix1.m44, matrix2.m44);
    }


    public static bool operator !=(Matrix a, Matrix b) => !(a == b);
    
    public static implicit operator System.Numerics.Matrix4x4(Matrix m)
    {
        return new System.Numerics.Matrix4x4(
            m.m11, m.m12, m.m13, m.m14,
            m.m21, m.m22, m.m23, m.m24,
            m.m31, m.m32, m.m33, m.m34,
            m.m41, m.m42, m.m43, m.m44
        );
    }
    public static implicit operator Matrix(System.Numerics.Matrix4x4 m)
    {
        return new Matrix
        {
            m11 = m.M11, m12 = m.M12, m13 = m.M13, m14 = m.M14,
            m21 = m.M21, m22 = m.M22, m23 = m.M23, m24 = m.M24,
            m31 = m.M31, m32 = m.M32, m33 = m.M33, m34 = m.M34,
            m41 = m.M41, m42 = m.M42, m43 = m.M43, m44 = m.M44
        };
    }

    public bool Equals(Matrix other) => this == other;

    public override bool Equals(object? obj) => obj is Matrix other && Equals(other);

    public override int GetHashCode()
    {
        return this.m11.GetHashCode() + this.m12.GetHashCode() + this.m13.GetHashCode() + this.m14.GetHashCode() + this.m21.GetHashCode() + this.m22.GetHashCode() + this.m23.GetHashCode() + this.m24.GetHashCode() + this.m31.GetHashCode() + this.m32.GetHashCode() + this.m33.GetHashCode() + this.m34.GetHashCode() + this.m41.GetHashCode() + this.m42.GetHashCode() + this.m43.GetHashCode() + this.m44.GetHashCode();
    }

    public override string ToString()
    {
        return $"[{m11}, {m12}, {m13}, {m14}]\n" +
               $"[{m21}, {m22}, {m23}, {m24}]\n" +
               $"[{m31}, {m32}, {m33}, {m34}]\n" +
               $"[{m41}, {m42}, {m43}, {m44}]";
    }
}
