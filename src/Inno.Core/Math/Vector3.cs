using System;
using System.Runtime.Serialization;

namespace Inno.Core.Math;

[DataContract]
public struct Vector3 : IEquatable<Vector3>
{
    [DataMember] public float x;
    [DataMember] public float y;
    [DataMember] public float z;

    public Vector3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    // Common static vectors
    public static readonly Vector3 ZERO = new(0, 0, 0);
    public static readonly Vector3 ONE = new(1, 1, 1);
    public static readonly Vector3 UP = new(0, 1, 0);
    public static readonly Vector3 DOWN = new(0, -1, 0);
    public static readonly Vector3 LEFT = new(-1, 0, 0);
    public static readonly Vector3 RIGHT = new(1, 0, 0);
    public static readonly Vector3 FORWARD = new(0, 0, 1);
    public static readonly Vector3 BACK = new(0, 0, -1);

    // Lengths
    public float Length() => MathF.Sqrt(x * x + y * y + z * z);
    public float LengthSquared() => x * x + y * y + z * z;

    public Vector3 normalized
    {
        get
        {
            float len = Length();
            return len > 0 ? this / len : ZERO;
        }
    }

    // Dot product
    public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

    // Cross product
    public static Vector3 Cross(Vector3 a, Vector3 b) => new(
        a.y * b.z - a.z * b.y,
        a.z * b.x - a.x * b.z,
        a.x * b.y - a.y * b.x
    );

    // Distance
    public static float Distance(Vector3 a, Vector3 b) => (a - b).Length();

    // Lerp
    public static Vector3 Lerp(Vector3 a, Vector3 b, float t) =>
        a + (b - a) * MathHelper.Clamp(t, 0f, 1f);

    // Reflect
    public static Vector3 Reflect(Vector3 dir, Vector3 normal) =>
        dir - 2f * Dot(dir, normal) * normal;
    
    /// <summary>
    /// The row vector times a matrix. It performs as below v * m (not m * v).
    /// </summary>
    /// <returns></returns>
    public static Vector3 Transform(Vector3 position, Matrix matrix)
    {
        float x = position.x * matrix.m11 + position.y * matrix.m21 + position.z * matrix.m31 + matrix.m41;
        float y = position.x * matrix.m12 + position.y * matrix.m22 + position.z * matrix.m32 + matrix.m42;
        float z = position.x * matrix.m13 + position.y * matrix.m23 + position.z * matrix.m33 + matrix.m43;
        return new Vector3(x, y, z);
    }
    
    public static Vector3 Transform(Vector3 value, Quaternion rotation)
    {
        float x = value.x, y = value.y, z = value.z;
        float qx = rotation.x, qy = rotation.y, qz = rotation.z, qw = rotation.w;

        float num1 = 2f * (qy * z - qz * y);
        float num2 = 2f * (qz * x - qx * z);
        float num3 = 2f * (qx * y - qy * x);

        float rx = x + num1 * qw + (qy * num3 - qz * num2);
        float ry = y + num2 * qw + (qz * num1 - qx * num3);
        float rz = z + num3 * qw + (qx * num2 - qy * num1);

        return new Vector3(rx, ry, rz);
    }

    // Operators
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    public static Vector3 operator -(Vector3 v) => new(-v.x, -v.y, -v.z);
    public static Vector3 operator *(Vector3 v, float s) => new(v.x * s, v.y * s, v.z * s);
    public static Vector3 operator *(float s, Vector3 v) => v * s;
    public static Vector3 operator /(Vector3 v, float s) => new(v.x / s, v.y / s, v.z / s);

    public static bool operator ==(Vector3 a, Vector3 b) =>
        MathHelper.AlmostEquals(a.x, b.x) &&
        MathHelper.AlmostEquals(a.y, b.y) &&
        MathHelper.AlmostEquals(a.z, b.z);

    public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
    
    public static implicit operator System.Numerics.Vector3(Vector3 v) => new(v.x, v.y, v.z);
    public static implicit operator Vector3(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    public override bool Equals(object? obj) => obj is Vector3 other && this == other;
    public bool Equals(Vector3 other) => this == other;
    public override int GetHashCode() => HashCode.Combine(x, y, z);
    public override string ToString() => $"({x}, {y}, {z})";
}
