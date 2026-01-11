using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Inno.Platform.ImGui.Bridge;

internal static class ImGuiDataStore
{
	// ini-friendly: we store as string payload
	public static readonly Dictionary<string, string> DATA = new();
}

internal static class ImGuiDataCodec
{
	// payload format: "<type>:<value>"
	// type: f,i,b,s
	public static string Encode(object? value)
	{
		if (value is null) return "s:";

		return value switch
		{
			float f   => "f:" + f.ToString(CultureInfo.InvariantCulture),
			double d  => "f:" + ((float)d).ToString(CultureInfo.InvariantCulture), // optional
			int i     => "i:" + i.ToString(CultureInfo.InvariantCulture),
			bool b    => "b:" + (b ? "1" : "0"),
			string s  => "s:" + s,
			_ => throw new NotSupportedException(
				$"IImGui.RegisterData only supports float/int/bool/string. Got: {value.GetType().FullName}")
		};
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
			_ => v
		};
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

internal static class ImGuiIniDataFile
{
    private const string C_SECTION = "InnoData";

    public static void Load(string iniPath)
    {
        if (string.IsNullOrWhiteSpace(iniPath)) return;
        if (!File.Exists(iniPath)) return;

        string text = File.ReadAllText(iniPath);
        ParseSection(text);
    }

    public static void Save(string iniPath)
    {
        if (string.IsNullOrWhiteSpace(iniPath)) return;
        ImGuiNET.ImGui.SaveIniSettingsToDisk(iniPath);

        string baseText = File.Exists(iniPath) ? File.ReadAllText(iniPath) : string.Empty;
        string merged = UpsertSection(baseText);
        File.WriteAllText(iniPath, merged);
    }

    private static void ParseSection(string text)
    {
        ImGuiDataStore.DATA.Clear();

        using var sr = new StringReader(text);
        string? line;
        bool inSection = false;

        while ((line = sr.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                inSection = string.Equals(line, $"[{C_SECTION}]", StringComparison.Ordinal);
                continue;
            }

            if (!inSection) continue;

            int idx = line.IndexOf('=');
            if (idx <= 0) continue;

            string key = line.Substring(0, idx).Trim();
            string val = line.Substring(idx + 1).Trim();
            if (key.Length == 0) continue;

            ImGuiDataStore.DATA[key] = val;
        }
    }

    private static string UpsertSection(string baseText)
    {
        // Remove existing [InnoData] section, then append a fresh one at the end.
        var sb = new StringBuilder(baseText.Length + 256);

        using (var sr = new StringReader(baseText))
        {
            string? line;
            bool skipping = false;

            while ((line = sr.ReadLine()) != null)
            {
                string trimmed = line.Trim();

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    // entering a section
                    if (string.Equals(trimmed, $"[{C_SECTION}]", StringComparison.Ordinal))
                    {
                        skipping = true;
                        continue; // drop the header
                    }

                    // leaving our section
                    if (skipping)
                    {
                        skipping = false;
                    }
                }

                if (!skipping) sb.AppendLine(line);
            }
        }

        // Ensure there is a blank line before appending
        if (sb.Length > 0 && sb[^1] != '\n')
            sb.AppendLine();

        sb.AppendLine($"[{C_SECTION}]");
        foreach (var kv in ImGuiDataStore.DATA)
            sb.AppendLine($"{kv.Key}={kv.Value}");
        sb.AppendLine();

        return sb.ToString();
    }
}

