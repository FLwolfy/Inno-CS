using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Logging;
using Inno.Core.Math;
using Inno.Editor.Core;

using ImGuiNET;
using Inno.ImGui;
using ImGuiNet = ImGuiNET.ImGui;

namespace Inno.Editor.Panel;

public class LogPanel : EditorPanel, ILogSink
{
    private const int C_MAX_LOG_ENTRIES = 1000;

    public override string title => "Log";

    private readonly Queue<LogEntry> m_pendingEntries = new();
    private readonly List<LogEntry> m_entries = new();
    private readonly HashSet<LogLevel> m_filterLevels = Enum.GetValues<LogLevel>().ToHashSet();
    private readonly LogLevel[] m_levels = Enum.GetValues<LogLevel>();

    private bool m_collapse = true;

    public LogPanel()
    {
        LogManager.RegisterSink(this);
    }
    
    public void Receive(LogEntry entry)
    {
        lock (m_pendingEntries)
        {
            m_pendingEntries.Enqueue(entry);
        }
    }

    internal override void OnGUI()
    {
        // -------------------
        // Add entries
        // -------------------
        lock(m_pendingEntries)
        {
            while (m_pendingEntries.Count > 0)
            {
                m_entries.Add(m_pendingEntries.Dequeue());
            }

            while (m_entries.Count > C_MAX_LOG_ENTRIES)
            {
                m_entries.RemoveAt(0);
            }
        }
        

        // -------------------
        // Top Options
        // -------------------
        ImGuiNet.BeginChild("LogChild", new Vector2(0, 0));
        
        bool oldCollapse = m_collapse;
        ImGuiNet.Checkbox("Collapse", ref m_collapse);
        ImGuiNet.SameLine();

        if (ImGuiNet.BeginCombo("##LogFilter", "Filter", ImGuiComboFlags.WidthFitPreview))
        {
            foreach (var level in m_levels)
            {
                bool selected = m_filterLevels.Contains(level);
                if (ImGuiNet.Checkbox(level.ToString(), ref selected))
                {
                    if (selected)
                        m_filterLevels.Add(level);
                    else
                        m_filterLevels.Remove(level);
                }
            }
            ImGuiNet.EndCombo();
        }

        ImGuiNet.SameLine();
        if (ImGuiNet.Button("Clear"))
        {
            m_entries.Clear();
        }

        ImGuiNet.Separator();

        // -------------------
        // Scrollable log region
        // -------------------
        ImGuiNet.BeginChild("LogRegion", new Vector2(0, 0));
        bool scrollAtBottom = ImGuiNet.GetScrollY() >= ImGuiNet.GetScrollMaxY() - 1.0f;

        LogEntry? collapsedEntry = null;
        int repeatCount = 0;

        foreach (var entry in m_entries)
        {
            if (!m_filterLevels.Contains(entry.level))
                continue;

            if (m_collapse &&
                collapsedEntry != null &&
                entry.level == collapsedEntry.Value.level &&
                entry.message == collapsedEntry.Value.message)
            {
                collapsedEntry = entry;
                repeatCount++;
                continue;
            }

            if (collapsedEntry != null)
            {
                if (oldCollapse && !m_collapse) ImGuiNet.SetNextItemOpen(false);
                DrawLogEntry(collapsedEntry.Value, repeatCount);
            }

            collapsedEntry = entry;
            repeatCount = 1;
        }

        // Draw last collapsed entry
        if (collapsedEntry != null)
        {
            DrawLogEntry(collapsedEntry.Value, repeatCount);
        }

        if (scrollAtBottom)
        {
            ImGuiNet.SetScrollHereY(1.0f);
        }

        ImGuiNet.EndChild();
        ImGuiNet.EndChild();
    }

    private void DrawLogEntry(LogEntry entry, int repeatCount)
    {
        Vector4 color = entry.level switch
        {
            LogLevel.Debug => new Vector4(0.5f, 0.5f, 0.5f, 1f),
            LogLevel.Info  => new Vector4(0.2f, 1f, 0.2f, 1f),
            LogLevel.Warn  => new Vector4(1f, 1f, 0.2f, 1f),
            LogLevel.Error => new Vector4(1f, 0.2f, 0.2f, 1f),
            LogLevel.Fatal => new Vector4(1f, 0.2f, 1f, 1f),
            _              => Vector4.ONE
        };

        ImGuiNet.PushID(entry.time.GetHashCode());

        bool open = ImGuiNet.CollapsingHeader("");

        ImGuiHost.UseFont(ImGuiFontStyle.Bold);
        ImGuiNet.PushStyleColor(ImGuiCol.Text, color);
        ImGuiNet.SameLine();
        ImGuiNet.Text($"[{entry.level}]");
        ImGuiNet.PopStyleColor();
        ImGuiHost.UseFont(ImGuiFontStyle.Regular);

        ImGuiNet.SameLine();
        ImGuiNet.Text(entry.message);

        if (repeatCount > 1)
        {
            ImGuiNet.SameLine();
            ImGuiNet.TextDisabled($"(x{repeatCount})");
        }

        if (open)
        {
            ImGuiNet.TextWrapped($"Time: {entry.time:HH:mm:ss}");
            ImGuiNet.TextWrapped($"Source: {entry.source}");
            ImGuiNet.TextWrapped($"File: {entry.file}");
            ImGuiNet.TextWrapped($"Line: {entry.line}");
            ImGuiNet.Dummy(new Vector2(0, 8));
        }

        ImGuiNet.PopID();
    }
}
