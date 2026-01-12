using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

using ImGuiNET;

using Inno.Assets;
using Inno.Editor.Core;

namespace Inno.Editor.Panel;

public sealed class FileBrowserPanel : EditorPanel
{
    public override string title => "File";

    private const float C_LEFT_DEFAULT_WIDTH = 280f;
    private const float C_SPLITTER_WIDTH = 5f;

    private const float C_TOOLBAR_HEIGHT = 34f;

    private const float C_GRID_ICON_SIZE = 54f;
    private const float C_GRID_CELL_PADDING = 10f;

    private const float C_LIST_ROW_HEIGHT = 22f;

    // List columns proportional sizing (so splitter resizing scales columns)
    private const float C_COL_NAME_RATIO = 0.55f;
    private const float C_COL_TYPE_RATIO = 0.18f;
    private const float C_COL_SIZE_RATIO = 0.12f;
    private const float C_COL_MOD_RATIO  = 0.15f;

    // Toolbar + Statusbar
    private const float C_SEARCH_DEFAULT_WIDTH = 220f;
    private const float C_STATUSBAR_HEIGHT = 22f;

    // Splitter limits
    private const float C_LEFT_MIN_WIDTH  = 160f;
    private const float C_RIGHT_MIN_WIDTH = 260f;

    // Normalized for UI / comparisons
    private string m_rootPath = "";
    private string m_currentDir = "";

    // Native for IO / watcher (do NOT normalize separators)
    private string m_rootPathNative = "";
    private string m_currentDirNative = "";

    private string? m_selectedPath;

    private float m_leftWidth = C_LEFT_DEFAULT_WIDTH;
    private float m_leftRatio = -1f; // keep ratio when window resizes

    private readonly List<string> m_history = new();
    private int m_historyIndex = -1;

    private string m_search = "";
    private string m_searchLast = "";

    private ViewMode m_viewMode = ViewMode.Grid;
    private SortField m_sortField = SortField.Name;
    private bool m_sortAscending = true;

    // Cached directory snapshot (avoid enumerating every frame)
    private DirectorySnapshot m_snapshot = new();
    private DateTime m_snapshotTime = DateTime.MinValue;
    private const double C_SNAPSHOT_TTL_SECONDS = 0.5;

    // Search snapshot (recursive)
    private DirectorySnapshot m_searchSnapshot = new();
    private DateTime m_searchSnapshotTime = DateTime.MinValue;

    // File system watcher (auto refresh)
    private FileSystemWatcher? m_watcher;
    private int m_fsChangeVersion = 0;
    private int m_fsAppliedVersion = 0;
    private DateTime m_lastFsEventUtc = DateTime.MinValue;
    private const double C_FS_DEBOUNCE_SECONDS = 0.15;

    // Rename / Delete UI state
    private string? m_renameTargetPath;
    private string m_renameBuffer = "";
    private string? m_deleteTargetPath;
    private string m_lastError = "";

    private enum ViewMode { Grid, List }
    private enum SortField { Name, Type, Size, Modified }

    private readonly struct Entry
    {
        public readonly string fullPath; // normalized
        public readonly string name;     // display (may be relative path under search)
        public readonly bool isDir;
        public readonly string type;
        public readonly long sizeBytes;
        public readonly DateTime modified;

        public Entry(string fullPath, string name, bool isDir, string type, long sizeBytes, DateTime modified)
        {
            this.fullPath = fullPath;
            this.name = name;
            this.isDir = isDir;
            this.type = type;
            this.sizeBytes = sizeBytes;
            this.modified = modified;
        }
    }

    private sealed class DirectorySnapshot
    {
        public readonly List<Entry> entries = new();
        public void Clear() => entries.Clear();
    }

    internal FileBrowserPanel()
    {
        // Native paths
        m_rootPathNative = Path.GetFullPath(AssetManager.assetDirectory);
        m_currentDirNative = m_rootPathNative;

        // Normalized paths
        m_rootPath = NormalizePath(m_rootPathNative);
        m_currentDir = m_rootPath;

        PushHistory(m_currentDir);

        SetupWatcher();
        RefreshSnapshot(force: true);
    }

    internal override void OnGUI()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(10, 10));
        ImGui.BeginChild("##FileBrowserRoot", new System.Numerics.Vector2(0, 0));

        DrawToolbar();

        // Body takes remaining height minus status bar (+ small separator buffer)
        var avail = ImGui.GetContentRegionAvail();
        float bodyH = Math.Max(0f, avail.Y - C_STATUSBAR_HEIGHT - 6f);

        ImGui.BeginChild("##FileBrowserBody", new System.Numerics.Vector2(0, bodyH));

        var region = ImGui.GetContentRegionAvail();

        // Apply ratio-based resizing for left pane when window width changes
        float totalW = Math.Max(0f, region.X);
        if (m_leftRatio < 0f && totalW > 0f)
            m_leftRatio = Math.Clamp(m_leftWidth / totalW, 0f, 1f);

        // Left: tree
        ImGui.BeginChild("##Tree", new System.Numerics.Vector2(m_leftWidth, 0));
        DrawDirectoryTree(m_rootPathNative, m_rootPath);
        ImGui.EndChild();

        // Splitter with limits
        ImGui.SameLine();

        float maxLeft = Math.Max(C_LEFT_MIN_WIDTH, totalW - C_RIGHT_MIN_WIDTH);
        bool draggingSplitter = DrawSplitter(ref m_leftWidth, minLeft: C_LEFT_MIN_WIDTH, maxLeft: maxLeft);

        ImGui.SameLine();

        // Update/apply ratio
        if (totalW > 0f)
        {
            float maxLeft2 = Math.Max(C_LEFT_MIN_WIDTH, totalW - C_RIGHT_MIN_WIDTH);

            if (draggingSplitter)
            {
                m_leftRatio = Math.Clamp(m_leftWidth / totalW, 0f, 1f);
            }
            else
            {
                // keep ratio when window resizes
                float target = m_leftRatio * totalW;
                m_leftWidth = Math.Clamp(target, C_LEFT_MIN_WIDTH, maxLeft2);
            }
        }

        // Right: content
        ImGui.BeginChild("##Content", new System.Numerics.Vector2(0, 0));
        DrawContent();
        ImGui.EndChild();

        ImGui.EndChild(); // body

        ImGui.Separator();
        DrawStatusBarFinderPath();

        ImGui.EndChild(); // root
        ImGui.PopStyleVar();

        DrawRenamePopup();
        DrawDeletePopup();
        DrawErrorPopup();
    }

    // ============================
    // Toolbar (Finder-like: Back/Forward + FolderName + View Toggle + Search filling rest)
    // ============================
    private void DrawToolbar()
    {
        ImGui.BeginChild("##Toolbar", new System.Numerics.Vector2(0, C_TOOLBAR_HEIGHT));

        var style = ImGui.GetStyle();

        // Responsive shrink:
        float availW = ImGui.GetContentRegionAvail().X;

        float navBtnW = ImGui.CalcTextSize("<").X + style.FramePadding.X * 2f;
        float folderLabelW = Math.Min(220f, ImGui.CalcTextSize(GetCurrentFolderDisplayName()).X);

        string viewLabelFull = m_viewMode == ViewMode.Grid ? "View: Grid" : "View: List";
        float viewBtnW = ImGui.CalcTextSize(viewLabelFull).X + style.FramePadding.X * 2f;

        float idealW =
            navBtnW * 2f +
            style.ItemSpacing.X * 6f +
            folderLabelW +
            viewBtnW +
            140f; // search minimal desired

        float scale = idealW > 0f ? Math.Clamp(availW / idealW, 0.70f, 1.0f) : 1.0f;

        var pad = new System.Numerics.Vector2(style.FramePadding.X * scale, style.FramePadding.Y * scale);
        var spc = new System.Numerics.Vector2(style.ItemSpacing.X * scale, style.ItemSpacing.Y * scale);

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, pad);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, spc);

        bool canBack = m_historyIndex > 0;
        bool canForward = m_historyIndex >= 0 && m_historyIndex < m_history.Count - 1;

        // Back
        ImGui.BeginDisabled(!canBack);
        if (ImGui.Button("<##Back")) NavigateHistory(-1);
        ImGui.EndDisabled();
        ImGui.SameLine();

        // Forward
        ImGui.BeginDisabled(!canForward);
        if (ImGui.Button(">##Forward")) NavigateHistory(+1);
        ImGui.EndDisabled();
        ImGui.SameLine();

        // Current folder name (top-left)
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(GetCurrentFolderDisplayName());
        ImGui.SameLine();

        // View toggle right before search; shorten label on tight widths
        string viewLabel = viewLabelFull;
        if (availW < 520f)
            viewLabel = (m_viewMode == ViewMode.Grid) ? "Grid" : "List";

        if (ImGui.Button(viewLabel))
            m_viewMode = m_viewMode == ViewMode.Grid ? ViewMode.List : ViewMode.Grid;

        ImGui.SameLine();

        // Search fills remaining width, never disappears
        float searchW = Math.Max(80f, ImGui.GetContentRegionAvail().X);
        ImGui.SetNextItemWidth(searchW);

        if (ImGui.InputTextWithHint("##Search", "Search", ref m_search, 256))
        {
            // mark search snapshot dirty (handled by RefreshSearchSnapshot conditions)
        }

        ImGui.PopStyleVar(2);
        ImGui.EndChild();
    }

    // ============================
    // Left Tree (folders + files)
    // ============================
    private void DrawDirectoryTree(string pathNative, string pathNormalized)
    {
        if (!Directory.Exists(pathNative))
            return;

        string displayName = IsSamePath(pathNormalized, m_rootPath) ? "Assets" : Path.GetFileName(pathNative);
        bool selected = IsSamePath(pathNormalized, m_currentDir);

        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanFullWidth;
        if (selected) flags |= ImGuiTreeNodeFlags.Selected;

        bool shouldOpen = IsAncestorOrSelf(pathNormalized, m_currentDir);
        ImGui.SetNextItemOpen(shouldOpen, ImGuiCond.Once);

        bool open = ImGui.TreeNodeEx($"##tree_{pathNormalized}", flags, $"{FolderIcon()}  {displayName}");

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            NavigateTo(pathNormalized, pushHistory: true);

        if (ImGui.BeginPopupContextItem($"##tree_ctx_{pathNormalized}"))
        {
            DrawCommonContextItems(pathNormalized, isFolderContext: true);
            ImGui.EndPopup();
        }

        if (open)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(pathNative))
                {
                    if (IsHidden(dir)) continue;

                    string childNative = dir;
                    string childNormalized = NormalizePath(childNative);
                    DrawDirectoryTree(childNative, childNormalized);
                }

                foreach (var file in Directory.GetFiles(pathNative))
                {
                    if (IsHidden(file)) continue;
                    if (IsEditorFilteredFile(file)) continue;

                    DrawTreeFileItem(file);
                }
            }
            catch (Exception e)
            {
                m_lastError = e.Message;
                OpenErrorPopup();
            }

            ImGui.TreePop();
        }
    }

    private void DrawTreeFileItem(string filePathNative)
    {
        string full = NormalizePath(filePathNative);
        string name = Path.GetFileName(full);

        var fi = new FileInfo(filePathNative);
        string type = ToType(fi.Extension);

        bool selected = IsSamePath(m_selectedPath, full);

        var flags =
            ImGuiTreeNodeFlags.Leaf |
            ImGuiTreeNodeFlags.NoTreePushOnOpen |
            ImGuiTreeNodeFlags.SpanFullWidth;

        if (selected) flags |= ImGuiTreeNodeFlags.Selected;

        ImGui.TreeNodeEx($"##tree_file_{full}", flags, $"{FileIcon(type)}  {name}");

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            m_selectedPath = full;

        if (ImGui.BeginPopupContextItem($"##tree_file_ctx_{full}"))
        {
            var entry = new Entry(
                fullPath: full,
                name: name,
                isDir: false,
                type: type,
                sizeBytes: fi.Exists ? fi.Length : 0,
                modified: fi.Exists ? fi.LastWriteTime : DateTime.MinValue
            );

            DrawItemContextItems(entry);
            ImGui.EndPopup();
        }
    }

    // ============================
    // Right Content
    // ============================
    private void DrawContent()
    {
        bool searching = !string.IsNullOrWhiteSpace(m_search);

        if (searching)
            RefreshSearchSnapshot(force: false);
        else
            RefreshSnapshot(force: false);

        if (ImGui.BeginPopupContextWindow("##content_ctx", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            DrawCommonContextItems(m_currentDir, isFolderContext: true);
            ImGui.EndPopup();
        }

        if (m_viewMode == ViewMode.List)
        {
            DrawListHeaderControls();
            ImGui.Separator();
        }

        var src = searching ? m_searchSnapshot.entries : m_snapshot.entries;
        var entries = ApplyFilterAndSort(src);

        if (m_viewMode == ViewMode.Grid)
            DrawGrid(entries);
        else
            DrawList(entries);
    }

    private void DrawListHeaderControls()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Sort:");
        ImGui.SameLine();

        SortCombo("##sortField", ref m_sortField);
        ImGui.SameLine();

        if (ImGui.Button(m_sortAscending ? "Asc##sort" : "Desc##sort"))
            m_sortAscending = !m_sortAscending;
    }

    private static void SortCombo(string id, ref SortField field)
    {
        ImGui.SetNextItemWidth(140f);
        if (ImGui.BeginCombo(id, field.ToString()))
        {
            foreach (SortField v in Enum.GetValues(typeof(SortField)))
            {
                bool selected = (v == field);
                if (ImGui.Selectable(v.ToString(), selected))
                    field = v;
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
    }

    private void DrawGrid(List<Entry> entries)
    {
        float availW = ImGui.GetContentRegionAvail().X;
        float cell = C_GRID_ICON_SIZE + C_GRID_CELL_PADDING;
        int cols = Math.Max(1, (int)(availW / cell));

        ImGui.Columns(cols, "##grid_cols", false);

        foreach (var e in entries)
        {
            DrawGridItem(e);
            ImGui.NextColumn();
        }

        ImGui.Columns(1);
    }

    private void DrawGridItem(Entry e)
    {
        ImGui.BeginGroup();
        ImGui.PushID(e.fullPath);

        var icon = e.isDir ? FolderIcon() : FileIcon(e.type);
        if (ImGui.Button(icon, new System.Numerics.Vector2(C_GRID_ICON_SIZE, C_GRID_ICON_SIZE)))
        {
            m_selectedPath = e.fullPath;
            if (e.isDir)
                NavigateTo(e.fullPath, pushHistory: true);
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (e.isDir)
                NavigateTo(e.fullPath, pushHistory: true);
        }

        if (ImGui.BeginPopupContextItem("##item_ctx"))
        {
            DrawItemContextItems(e);
            ImGui.EndPopup();
        }

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + C_GRID_ICON_SIZE);
        ImGui.TextWrapped(e.name);
        ImGui.PopTextWrapPos();

        ImGui.PopID();
        ImGui.EndGroup();
    }

    private void DrawList(List<Entry> entries)
    {
        var tableFlags =
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.BordersInnerV |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingFixedFit;

        var avail = ImGui.GetContentRegionAvail();
        float w = Math.Max(0f, avail.X);

        float nameW = Math.Max(60f, w * C_COL_NAME_RATIO);
        float typeW = Math.Max(50f, w * C_COL_TYPE_RATIO);
        float sizeW = Math.Max(40f, w * C_COL_SIZE_RATIO);
        float modW  = Math.Max(80f, w * C_COL_MOD_RATIO);

        float sum = nameW + typeW + sizeW + modW;
        if (sum > 0f && sum > w)
        {
            float k = w / sum;
            nameW *= k; typeW *= k; sizeW *= k; modW *= k;
        }

        if (ImGui.BeginTable("##list_table", 4, tableFlags, new System.Numerics.Vector2(0, avail.Y)))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, nameW);
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, typeW);
            ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, sizeW);

            // Finder-like: last column no resize handle, avoid "extra" right-side divider feeling
            ImGui.TableSetupColumn("Modified", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, modW);

            ImGui.TableHeadersRow();

            foreach (var e in entries)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, C_LIST_ROW_HEIGHT);

                ImGui.TableSetColumnIndex(0);
                bool selected = IsSamePath(m_selectedPath, e.fullPath);

                string label = $"{(e.isDir ? FolderIcon() : FileIcon(e.type))}  {e.name}";
                if (ImGui.Selectable(label, selected, ImGuiSelectableFlags.SpanAllColumns))
                {
                    m_selectedPath = e.fullPath;
                    if (e.isDir)
                        NavigateTo(e.fullPath, pushHistory: true);
                }

                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    if (e.isDir)
                        NavigateTo(e.fullPath, pushHistory: true);
                }

                if (ImGui.BeginPopupContextItem($"##list_ctx_{e.fullPath}"))
                {
                    DrawItemContextItems(e);
                    ImGui.EndPopup();
                }

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(e.isDir ? "Folder" : e.type);

                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(e.isDir ? "" : FormatBytes(e.sizeBytes));

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(e.modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            }

            ImGui.EndTable();
        }
    }

    // ============================
    // Context Menus
    // ============================
    private void DrawItemContextItems(Entry e)
    {
        if (e.isDir)
        {
            if (ImGui.MenuItem("Open"))
                NavigateTo(e.fullPath, pushHistory: true);
        }

        if (ImGui.MenuItem("Reveal in Finder/Explorer"))
            RevealInSystem(e.fullPath);

        ImGui.Separator();

        if (ImGui.MenuItem("Rename"))
            BeginRename(e.fullPath);

        if (ImGui.MenuItem("Delete"))
            BeginDelete(e.fullPath);
    }

    private void DrawCommonContextItems(string targetFolderNormalized, bool isFolderContext)
    {
        if (ImGui.MenuItem("New Folder"))
            CreateNewFolder(targetFolderNormalized);

        if (ImGui.MenuItem("Reveal in Finder/Explorer"))
            RevealInSystem(targetFolderNormalized);
    }

    // ============================
    // Navigation / History
    // ============================
    private void NavigateTo(string dirNormalized, bool pushHistory)
    {
        dirNormalized = NormalizePath(dirNormalized);

        // Native dir for IO
        string dirNative = ToNativePath(dirNormalized);
        if (!Directory.Exists(dirNative))
            return;

        m_currentDir = dirNormalized;
        m_currentDirNative = dirNative;
        m_selectedPath = null;

        if (pushHistory)
            PushHistory(dirNormalized);

        RefreshSnapshot(force: true);
    }

    private void PushHistory(string dir)
    {
        if (m_historyIndex >= 0 && m_historyIndex < m_history.Count - 1)
            m_history.RemoveRange(m_historyIndex + 1, m_history.Count - (m_historyIndex + 1));

        m_history.Add(dir);
        m_historyIndex = m_history.Count - 1;
    }

    private void NavigateHistory(int delta)
    {
        int next = m_historyIndex + delta;
        if (next < 0 || next >= m_history.Count)
            return;

        m_historyIndex = next;
        NavigateTo(m_history[m_historyIndex], pushHistory: false);
    }

    // ============================
    // Snapshot / IO
    // ============================
    private void RefreshSnapshot(bool force)
    {
        if (!force)
        {
            int ver = Volatile.Read(ref m_fsChangeVersion);
            bool fsChanged = ver != m_fsAppliedVersion;

            if (fsChanged)
            {
                var now = DateTime.UtcNow;
                if ((now - m_lastFsEventUtc).TotalSeconds < C_FS_DEBOUNCE_SECONDS)
                    return;

                m_fsAppliedVersion = ver;
            }
            else
            {
                var now = DateTime.UtcNow;
                if ((now - m_snapshotTime).TotalSeconds < C_SNAPSHOT_TTL_SECONDS)
                    return;
            }
        }

        m_snapshotTime = DateTime.UtcNow;
        m_snapshot.Clear();

        try
        {
            if (!Directory.Exists(m_currentDirNative))
                return;

            foreach (var d in Directory.GetDirectories(m_currentDirNative))
            {
                if (IsHidden(d)) continue;

                string dn = NormalizePath(d);
                m_snapshot.entries.Add(new Entry(
                    fullPath: dn,
                    name: Path.GetFileName(d),
                    isDir: true,
                    type: "Folder",
                    sizeBytes: 0,
                    modified: Directory.GetLastWriteTime(d)
                ));
            }

            foreach (var f in Directory.GetFiles(m_currentDirNative))
            {
                if (IsHidden(f)) continue;
                if (IsEditorFilteredFile(f)) continue;

                var fi = new FileInfo(f);

                m_snapshot.entries.Add(new Entry(
                    fullPath: NormalizePath(f),
                    name: fi.Name,
                    isDir: false,
                    type: ToType(fi.Extension),
                    sizeBytes: fi.Length,
                    modified: fi.LastWriteTime
                ));
            }
        }
        catch (Exception e)
        {
            m_lastError = e.Message;
            OpenErrorPopup();
        }
    }

    private void RefreshSearchSnapshot(bool force)
    {
        // rebuild when:
        // - search query changed
        // - fs changed (debounced)
        // - TTL fallback
        if (!force)
        {
            bool queryChanged = !string.Equals(m_search, m_searchLast, StringComparison.Ordinal);
            int ver = Volatile.Read(ref m_fsChangeVersion);
            bool fsChanged = ver != m_fsAppliedVersion;

            if (queryChanged)
            {
                // allow rebuild immediately, but still set last
            }
            else if (fsChanged)
            {
                var now = DateTime.UtcNow;
                if ((now - m_lastFsEventUtc).TotalSeconds < C_FS_DEBOUNCE_SECONDS)
                    return;
            }
            else
            {
                var now = DateTime.UtcNow;
                if ((now - m_searchSnapshotTime).TotalSeconds < C_SNAPSHOT_TTL_SECONDS)
                    return;
            }
        }

        m_searchLast = m_search;
        m_searchSnapshotTime = DateTime.UtcNow;

        // If FS changed, consume version (so both normal & search paths align)
        int v = Volatile.Read(ref m_fsChangeVersion);
        m_fsAppliedVersion = v;

        m_searchSnapshot.Clear();

        try
        {
            if (string.IsNullOrWhiteSpace(m_search))
                return;

            if (!Directory.Exists(m_currentDirNative))
                return;

            // Enumerate all dirs/files under current dir
            // NOTE: This can be heavy for huge projects; we debounce & cache.
            var dirs = Directory.EnumerateDirectories(m_currentDirNative, "*", SearchOption.AllDirectories);
            foreach (var d in dirs)
            {
                if (IsHidden(d)) continue;

                string normalized = NormalizePath(d);
                string rel = GetRelativeDisplay(m_currentDirNative, d);
                m_searchSnapshot.entries.Add(new Entry(
                    fullPath: normalized,
                    name: rel,
                    isDir: true,
                    type: "Folder",
                    sizeBytes: 0,
                    modified: Directory.GetLastWriteTime(d)
                ));
            }

            var files = Directory.EnumerateFiles(m_currentDirNative, "*", SearchOption.AllDirectories);
            foreach (var f in files)
            {
                if (IsHidden(f)) continue;
                if (IsEditorFilteredFile(f)) continue;

                var fi = new FileInfo(f);
                string normalized = NormalizePath(f);
                string rel = GetRelativeDisplay(m_currentDirNative, f);

                m_searchSnapshot.entries.Add(new Entry(
                    fullPath: normalized,
                    name: rel,
                    isDir: false,
                    type: ToType(fi.Extension),
                    sizeBytes: fi.Length,
                    modified: fi.LastWriteTime
                ));
            }
        }
        catch (Exception e)
        {
            m_lastError = e.Message;
            OpenErrorPopup();
        }
    }

    private List<Entry> ApplyFilterAndSort(List<Entry> src)
    {
        IEnumerable<Entry> q = src;

        if (!string.IsNullOrWhiteSpace(m_search))
        {
            string s = m_search.Trim();
            q = q.Where(e => e.name.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        q = q.OrderByDescending(e => e.isDir);

        Func<Entry, object> key = m_sortField switch
        {
            SortField.Name => e => e.name,
            SortField.Type => e => e.type,
            SortField.Size => e => e.sizeBytes,
            SortField.Modified => e => e.modified,
            _ => e => e.name
        };

        q = m_sortAscending ? q.OrderBy(key) : q.OrderByDescending(key);
        return q.ToList();
    }

    // ============================
    // Rename / Delete / Create
    // ============================
    private void BeginRename(string pathNormalized)
    {
        m_renameTargetPath = pathNormalized;
        m_renameBuffer = Path.GetFileName(pathNormalized);
        ImGui.OpenPopup("Rename##popup");
    }

    private void DrawRenamePopup()
    {
        if (ImGui.BeginPopupModal("Rename##popup", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("New name:");
            ImGui.SetNextItemWidth(360f);
            ImGui.InputText("##rename", ref m_renameBuffer, 256);

            ImGui.Spacing();

            bool ok = ImGui.Button("OK", new System.Numerics.Vector2(120, 0));
            ImGui.SameLine();
            bool cancel = ImGui.Button("Cancel", new System.Numerics.Vector2(120, 0));

            if (ok && m_renameTargetPath != null)
            {
                TryRename(m_renameTargetPath, m_renameBuffer);
                m_renameTargetPath = null;
                ImGui.CloseCurrentPopup();
            }
            else if (cancel)
            {
                m_renameTargetPath = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void TryRename(string oldPathNormalized, string newName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(newName))
                return;

            string dirNorm = NormalizePath(Path.GetDirectoryName(oldPathNormalized) ?? m_currentDir);
            string newPathNorm = NormalizePath(Path.Combine(dirNorm, newName));

            if (IsSamePath(oldPathNormalized, newPathNorm))
                return;

            string oldNative = ToNativePath(oldPathNormalized);
            string newNative = ToNativePath(newPathNorm);

            if (Directory.Exists(oldNative))
                Directory.Move(oldNative, newNative);
            else if (File.Exists(oldNative))
                File.Move(oldNative, newNative);
            else
                return;

            RefreshSnapshot(force: true);
        }
        catch (Exception e)
        {
            m_lastError = e.Message;
            OpenErrorPopup();
        }
    }

    private void BeginDelete(string pathNormalized)
    {
        m_deleteTargetPath = pathNormalized;
        ImGui.OpenPopup("Delete##popup");
    }

    private void DrawDeletePopup()
    {
        if (ImGui.BeginPopupModal("Delete##popup", ImGuiWindowFlags.AlwaysAutoResize))
        {
            string name = m_deleteTargetPath != null ? Path.GetFileName(m_deleteTargetPath) : "";
            ImGui.TextUnformatted($"Delete '{name}' ?");
            ImGui.TextDisabled("This action cannot be undone.");

            ImGui.Spacing();

            bool del = ImGui.Button("Delete", new System.Numerics.Vector2(120, 0));
            ImGui.SameLine();
            bool cancel = ImGui.Button("Cancel", new System.Numerics.Vector2(120, 0));

            if (del && m_deleteTargetPath != null)
            {
                TryDelete(m_deleteTargetPath);
                m_deleteTargetPath = null;
                ImGui.CloseCurrentPopup();
            }
            else if (cancel)
            {
                m_deleteTargetPath = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void TryDelete(string pathNormalized)
    {
        try
        {
            string native = ToNativePath(pathNormalized);

            if (Directory.Exists(native))
                Directory.Delete(native, recursive: true);
            else if (File.Exists(native))
                File.Delete(native);

            RefreshSnapshot(force: true);
        }
        catch (Exception e)
        {
            m_lastError = e.Message;
            OpenErrorPopup();
        }
    }

    private void CreateNewFolder(string parentFolderNormalized)
    {
        try
        {
            string parentNative = ToNativePath(parentFolderNormalized);
            if (!Directory.Exists(parentNative))
                return;

            string baseName = "New Folder";
            string candidateNative = Path.Combine(parentNative, baseName);
            int i = 1;
            while (Directory.Exists(candidateNative))
            {
                candidateNative = Path.Combine(parentNative, $"{baseName} ({i})");
                i++;
            }

            Directory.CreateDirectory(candidateNative);

            string candidateNorm = NormalizePath(candidateNative);
            RefreshSnapshot(force: true);
            BeginRename(candidateNorm);
        }
        catch (Exception e)
        {
            m_lastError = e.Message;
            OpenErrorPopup();
        }
    }

    // ============================
    // Splitter
    // ============================
    private static bool DrawSplitter(ref float leftWidth, float minLeft, float maxLeft)
    {
        float height = ImGui.GetContentRegionAvail().Y;

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(0, 0));
        ImGui.Button("##Splitter", new System.Numerics.Vector2(C_SPLITTER_WIDTH, height));
        ImGui.PopStyleVar(2);

        bool active = ImGui.IsItemActive();
        if (active)
        {
            float delta = ImGui.GetIO().MouseDelta.X;
            leftWidth = Math.Clamp(leftWidth + delta, minLeft, maxLeft);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);

        return active;
    }

    // ============================
    // Watcher
    // ============================
    private void SetupWatcher()
    {
        try
        {
            m_watcher?.Dispose();

            if (string.IsNullOrWhiteSpace(m_rootPathNative) || !Directory.Exists(m_rootPathNative))
                return;

            m_watcher = new FileSystemWatcher(m_rootPathNative)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };

            // Reduce missed events on bursty changes
            m_watcher.InternalBufferSize = 64 * 1024;

            m_watcher.Changed += OnFsChanged;
            m_watcher.Created += OnFsChanged;
            m_watcher.Deleted += OnFsChanged;
            m_watcher.Renamed += OnFsRenamed;

            m_watcher.EnableRaisingEvents = true;
        }
        catch (Exception e)
        {
            m_lastError = e.Message;
            OpenErrorPopup();
        }
    }

    private void OnFsChanged(object sender, FileSystemEventArgs e)
    {
        Interlocked.Increment(ref m_fsChangeVersion);
        m_lastFsEventUtc = DateTime.UtcNow;
    }

    private void OnFsRenamed(object sender, RenamedEventArgs e)
    {
        Interlocked.Increment(ref m_fsChangeVersion);
        m_lastFsEventUtc = DateTime.UtcNow;
    }

    // ============================
    // Statusbar (Finder-like path: clickable segments + horizontal scroll)
    // ============================
    private void DrawStatusBarFinderPath()
    {
        // Horizontal scrollbar + no wrapping
        ImGui.BeginChild(
            "##FileBrowserStatus",
            new System.Numerics.Vector2(0, C_STATUSBAR_HEIGHT),
            ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar
        );

        // Root segment
        if (ImGui.SmallButton("Assets##sb_root"))
            NavigateTo(m_rootPath, pushHistory: true);

        string running = m_rootPath;
        var parts = SplitPathRelativeToRoot(m_currentDir, m_rootPath);

        for (int i = 0; i < parts.Count; i++)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(">");
            ImGui.SameLine();

            string part = parts[i];

            running = NormalizePath(Path.Combine(running, part));

            // Segment is clickable
            if (ImGui.SmallButton($"{part}##sb_{i}"))
                NavigateTo(running, pushHistory: true);
        }

        ImGui.EndChild();
    }

    // ============================
    // Utilities
    // ============================
    private static string NormalizePath(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        return Path.GetFullPath(p).Replace('\\', '/');
    }

    private string ToNativePath(string normalized)
    {
        // Convert normalized path back to native by combining rootNative + relative
        // Assumption: normalized is under m_rootPath.
        string n = NormalizePath(normalized).TrimEnd('/');
        string rootN = m_rootPath.TrimEnd('/');

        if (IsSamePath(n, rootN))
            return m_rootPathNative;

        if (!n.StartsWith(rootN, StringComparison.OrdinalIgnoreCase))
        {
            // fallback
            return Path.GetFullPath(normalized);
        }

        string rel = n.Substring(rootN.Length).TrimStart('/');
        return Path.Combine(m_rootPathNative, rel.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool IsSamePath(string? a, string? b)
    {
        if (a == null || b == null) return false;
        return string.Equals(NormalizePath(a).TrimEnd('/'), NormalizePath(b).TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAncestorOrSelf(string ancestor, string path)
    {
        ancestor = NormalizePath(ancestor).TrimEnd('/') + "/";
        path = NormalizePath(path).TrimEnd('/') + "/";
        return path.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHidden(string path)
    {
        string name = Path.GetFileName(path);
        if (name.StartsWith(".", StringComparison.Ordinal)) return true;

        try
        {
            var attr = File.GetAttributes(path);
            return (attr & FileAttributes.Hidden) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsEditorFilteredFile(string file)
    {
        if (file.EndsWith(AssetManager.C_ASSET_POSTFIX, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static List<string> SplitPathRelativeToRoot(string fullPath, string rootPath)
    {
        fullPath = NormalizePath(fullPath).TrimEnd('/');
        rootPath = NormalizePath(rootPath).TrimEnd('/');

        if (IsSamePath(fullPath, rootPath))
            return new List<string>();

        if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            return new List<string>();

        string rel = fullPath.Substring(rootPath.Length).TrimStart('/');
        return rel.Length == 0
            ? new List<string>()
            : rel.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static string GetRelativeDisplay(string baseDirNative, string fullNative)
    {
        try
        {
            string rel = Path.GetRelativePath(baseDirNative, fullNative);
            return rel.Replace('\\', '/');
        }
        catch
        {
            return Path.GetFileName(fullNative);
        }
    }

    private static string ToType(string ext)
    {
        string e = ext ?? "";
        if (e.StartsWith(".")) e = e.Substring(1);
        if (string.IsNullOrWhiteSpace(e)) e = "File";
        return e.ToUpperInvariant();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024f:0.0} KB";
        if (bytes < 1024L * 1024L * 1024L) return $"{bytes / (1024f * 1024f):0.0} MB";
        return $"{bytes / (1024f * 1024f * 1024f):0.0} GB";
    }

    private static string FolderIcon() => "\uD83D\uDCC1"; // 📁

    private static string FileIcon(string type)
    {
        return type switch
        {
            "PNG" or "JPG" or "JPEG" or "TGA" or "BMP" => "\uD83D\uDDBC\uFE0F", // 🖼️
            "CS" => "\uD83D\uDCDD", // 📝
            "TXT" or "MD" => "\uD83D\uDCC4", // 📄
            _ => "\uD83D\uDCC4" // 📄
        };
    }

    private static void RevealInSystem(string path)
    {
        // no-op (route via Inno.Platform later)
    }

    private string GetCurrentFolderDisplayName()
    {
        if (IsSamePath(m_currentDir, m_rootPath))
            return "Assets";

        string name = Path.GetFileName(m_currentDirNative.TrimEnd(Path.DirectorySeparatorChar, '/', '\\'));
        return string.IsNullOrWhiteSpace(name) ? "Assets" : name;
    }

    // ============================
    // Error handling
    // ============================
    private void OpenErrorPopup()
    {
        if (!string.IsNullOrWhiteSpace(m_lastError))
            ImGui.OpenPopup("Error##popup");
    }

    private void DrawErrorPopup()
    {
        if (ImGui.BeginPopupModal("Error##popup", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextWrapped(m_lastError);
            ImGui.Spacing();

            if (ImGui.Button("OK", new System.Numerics.Vector2(120, 0)))
            {
                m_lastError = "";
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }
}
