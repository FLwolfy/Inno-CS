using System;
using System.Globalization;
using System.Text;

namespace Inno.Editor.ImGui;

internal static class ImGuiDataCodec
{
	// payload format: "<type>:<value>"
	// type:
	// - f: float
	// - i: int
	// - b: bool
	// - s: string (inline, single-line)
	// - S: string (base64-utf8, supports newlines and any characters)
	public static string Encode(object? value)
	{
		if (value is null) return "s:";

		return value switch
		{
			float f  => "f:" + f.ToString(CultureInfo.InvariantCulture),
			double d => "f:" + ((float)d).ToString(CultureInfo.InvariantCulture), // optional convenience
			int i    => "i:" + i.ToString(CultureInfo.InvariantCulture),
			bool b   => "b:" + (b ? "1" : "0"),
			string s => EncodeString(s),
			_ => throw new NotSupportedException(
				$"IImGui.RegisterData only supports float/int/bool/string. Got: {value.GetType().FullName}")
		};
	}

	private static string EncodeString(string s)
	{
		// INI is line-based; newlines would corrupt the file.
		// If a string contains CR/LF, store it as base64 to keep the ini single-line and lossless.
		if (s.IndexOfAny(new[] { '\r', '\n' }) >= 0)
		{
			var bytes = Encoding.UTF8.GetBytes(s);
			return "S:" + Convert.ToBase64String(bytes);
		}

		return "s:" + s;
	}

	private static object? Decode(string? payload)
	{
		if (payload == null) return null;
		if (payload.Length < 2 || payload[1] != ':') return payload; // backward-compatible fallback

		char type = payload[0];
		string v = payload.Substring(2);

		return type switch
		{
			'f' => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f,
			'i' => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0,
			'b' => v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase),
			's' => v,
			'S' => DecodeBase64Utf8(v),
			_ => v
		};
	}

	private static string DecodeBase64Utf8(string base64)
	{
		try
		{
			var bytes = Convert.FromBase64String(base64);
			return Encoding.UTF8.GetString(bytes);
		}
		catch
		{
			// Fallback: treat it as plain string to avoid throwing during ini load.
			return base64;
		}
	}

	public static T Decode<T>(string? payload, T defaultValue)
	{
		object? o = Decode(payload);
		if (o is T t) return t;

		// allow int -> float convenience etc. (optional)
		if (typeof(T) == typeof(float) && o is int i)
			return (T)(object)(float)i;

		return defaultValue;
	}
}