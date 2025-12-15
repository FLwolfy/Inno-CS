using ImGuiNET;
using Inno.Core.Logging;
using Inno.Core.Math;
using Inno.Editor.Core;
using Inno.Platform.ImGui;

namespace Inno.Editor.Panel;

public class LogPanel : EditorPanel, ILogSink
{
    private const int C_MAX_LOG_ENTRIES = 1000;

    public override string title => "Log";

    private readonly Queue<LogEntry> m_pendingEntries = new();
    private readonly List<LogEntry> m_entries = new();
    private readonly HashSet<LogLevel> m_filterLevels = Enum.GetValues(typeof(LogLevel)).Cast<LogLevel>().ToHashSet();
    private readonly LogLevel[] m_levels = (LogLevel[])Enum.GetValues(typeof(LogLevel));

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
        ImGui.BeginChild("LogChild", new Vector2(0, 0));
        
        bool oldCollapse = m_collapse;
        ImGui.Checkbox("Collapse", ref m_collapse);
        ImGui.SameLine();

        if (ImGui.BeginCombo("##LogFilter", "Filter", ImGuiComboFlags.WidthFitPreview))
        {
            foreach (var level in m_levels)
            {
                bool selected = m_filterLevels.Contains(level);
                if (ImGui.Checkbox(level.ToString(), ref selected))
                {
                    if (selected)
                        m_filterLevels.Add(level);
                    else
                        m_filterLevels.Remove(level);
                }
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            m_entries.Clear();
        }

        ImGui.Separator();

        // -------------------
        // Scrollable log region
        // -------------------
        ImGui.BeginChild("LogRegion", new Vector2(0, 0));
        bool scrollAtBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 1.0f;

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
                if (oldCollapse && !m_collapse) ImGui.SetNextItemOpen(false);
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
            ImGui.SetScrollHereY(1.0f);
        }

        ImGui.EndChild();
        ImGui.EndChild();
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

        ImGui.PushID(entry.time.GetHashCode());

        bool open = ImGui.CollapsingHeader("");

        IImGui.UseFont(ImGuiFontStyle.Bold);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.SameLine();
        ImGui.Text($"[{entry.level}]");
        ImGui.PopStyleColor();
        IImGui.UseFont(ImGuiFontStyle.Regular);

        ImGui.SameLine();
        ImGui.Text(entry.message);

        if (repeatCount > 1)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"(x{repeatCount})");
        }

        if (open)
        {
            ImGui.TextWrapped($"Time: {entry.time:HH:mm:ss}");
            ImGui.TextWrapped($"File: {entry.file}");
            ImGui.TextWrapped($"Line: {entry.line}");
            ImGui.Dummy(new Vector2(0, 8));
        }

        ImGui.PopID();
    }
}
