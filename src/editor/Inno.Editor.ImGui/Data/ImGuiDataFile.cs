using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Inno.Editor.ImGui;

internal static class ImGuiIniDataFile
{
	private const string C_SECTION = "InnoData";

	// NOTE: Keys live on the left side of '=' in ini files. That side cannot safely contain arbitrary characters.
	// We percent-encode keys to keep ini parsing stable and to avoid collisions after trimming.
	// Values are stored in ImGuiDataCodec payloads and can contain '=' safely; newlines are base64-encoded.
	private static string EscapeKey(string key)
		=> Uri.EscapeDataString(key);

	private static string UnescapeKey(string key)
	{
		try { return Uri.UnescapeDataString(key); }
		catch { return key; }
	}

	public static void Load(string iniPath)
	{
		if (string.IsNullOrWhiteSpace(iniPath)) return;
		if (!File.Exists(iniPath)) return;

		string text = File.ReadAllText(iniPath);
		ParseSection(text);
	}

	/// <summary>
	/// Ensures the ini file contains an up-to-date [InnoData] section.
	///
	/// IMPORTANT: This method deliberately does NOT call ImGui.SaveIniSettingsToDisk.
	/// It is intended to be invoked AFTER ImGui has written its own ini content,
	/// to avoid competing saves and to prevent ImGui from "washing out" custom sections.
	/// </summary>
	public static void EnsureSectionPresent(string iniPath)
	{
		if (string.IsNullOrWhiteSpace(iniPath)) return;
		if (!File.Exists(iniPath)) return;

		string baseText = File.ReadAllText(iniPath);
		string merged = UpsertSection(baseText);
		if (!string.Equals(baseText, merged, StringComparison.Ordinal))
			File.WriteAllText(iniPath, merged);
	}

	/// <summary>
	/// Load [InnoData] and immediately inject it back into the ini file if the section is missing.
	/// This is a "self-heal" step that prevents the next ImGui save from wiping our custom data.
	/// </summary>
	public static void LoadAndEnsure(string iniPath)
	{
		if (string.IsNullOrWhiteSpace(iniPath)) return;
		if (!File.Exists(iniPath)) return;

		string text = File.ReadAllText(iniPath);
		ParseSection(text);

		// If the section does not exist yet, append it once so future ImGui saves keep a stable file shape.
		if (!text.Contains($"[{C_SECTION}]", StringComparison.Ordinal))
			File.WriteAllText(iniPath, UpsertSection(text));
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

			// IMPORTANT:
			// - We Trim() because ini writers commonly add whitespace.
			// - Then we unescape to recover original keys.
			string key = UnescapeKey(line.Substring(0, idx).Trim());
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
		{
			var safeKey = EscapeKey(kv.Key);
			sb.AppendLine($"{safeKey}={kv.Value}");
		}
		sb.AppendLine();

		return sb.ToString();
	}
}
