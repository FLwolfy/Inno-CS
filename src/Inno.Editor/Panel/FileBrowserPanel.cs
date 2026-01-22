using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Inno.Assets;
using Inno.Core.Logging;
using Inno.Core.Math;
using Inno.Editor.Core;
using Inno.Editor.GUI;

using ImGuiNET;
using Inno.ImGui;
using ImGuiNet = ImGuiNET.ImGui;

namespace Inno.Editor.Panel;

public sealed class FileBrowserPanel : EditorPanel
{
    public override string title => "File";
    
    // Drag typeID
    public const string C_ASSET_GUID_TYPE = "FileAssetGUID";

    // Splitter
    private const float C_SPLITTER_DEFAULT_WIDTH = 280f;
    private const float C_SPLITTER_WIDTH = 3f;
    private const float C_LEFT_MIN_WIDTH = 10f;
    private const float C_RIGHT_MIN_WIDTH = 20f;
    private float m_leftWidth;
    private float m_leftRatio = -1f; // keep ratio when window resizes

    // Grid
    private const float C_GRID_ICON_SIZE = 54f;

    // Root Paths
    private readonly string m_rootPath;
    private string m_currentDir;
    private readonly string m_rootPathNative;
    private string m_currentDirNative;

    // Selection
    private string? m_selectedPath;

    // Reveal selection in tree (one-frame force-open)
    private readonly HashSet<string> m_revealOpenPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool m_revealOpenPending;

    // Cached directory snapshot
    private const double C_SNAPSHOT_TTL_SECONDS = 0.5;
    private readonly DirectorySnapshot m_snapshot = new();
    private DateTime m_snapshotTime = DateTime.MinValue;
    private readonly List<string> m_history = new();
    private int m_historyIndex = -1;

    // Asset directory change versioning (single source of truth is AssetManager watcher)
    private int m_assetAppliedVersion;

    // Search
    private readonly DirectorySnapshot m_searchSnapshot = new();
    private DateTime m_searchSnapshotTime = DateTime.MinValue;
    private string m_search = "";
    private string m_searchLast = "";

    // View
    private ViewMode m_viewMode = ViewMode.Grid;
    private SortField m_sortField = SortField.Name;
    private bool m_sortAscending = true;

    // Grid scale
    private const float C_GRID_SCALE_MIN = 0.2f;
    private const float C_GRID_SCALE_MAX = 5.0f;
    private float m_gridScale = 1.0f;

    // Rename / Delete UI state
    private string? m_renameTargetPath;
    private string m_renameBuffer = "";
    private string? m_deleteTargetPath;

    private enum ViewMode { Grid, List }
    private enum SortField { Name, Type, Source }

    private readonly struct Entry(
        string fullPath,
        string name,
        bool isDir,
        string type,
        string source)
    {
        public readonly string fullPath = fullPath;  // normalized
        public readonly string name = name;          // display name (file/folder name)
        public readonly bool isDir = isDir;
        public readonly string type = type;

        // Finder-like "Source" column: relative to current dir, using "~" prefix
        public readonly string source = source;
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

        // Selection
        m_selectedPath = null;

        // UI
        m_leftWidth = ImGuiHost.GetStorageData("Editor.File.SplitterLeftWidth", C_SPLITTER_DEFAULT_WIDTH);

        PushHistory(m_currentDir);

        // Snapshot refresh is driven by AssetManager's single watcher (coalesced).
        m_assetAppliedVersion = AssetManager.assetDirectoryChangeVersion;
        RefreshSnapshot(force: true);
    }

    internal override void OnGUI()
    {
        ImGuiNet.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 10));
        ImGuiNet.BeginChild("##FileBrowserRoot", new Vector2(0, 0));

        float statusH = ImGuiNet.GetFrameHeight(); // fits SmallButton nicely
        var avail = ImGuiNet.GetContentRegionAvail();
        float bodyH = Math.Max(0f, avail.Y - statusH - 6f);

        ImGuiNet.BeginChild("##FileBrowserBody", new Vector2(0, bodyH));

        var region = ImGuiNet.GetContentRegionAvail();
        float totalW = Math.Max(0f, region.X);
        if (m_leftRatio < 0f && totalW > 0f)
            m_leftRatio = Math.Clamp(m_leftWidth / totalW, 0f, 1f);

        // Left: tree
        {
            ImGuiNet.BeginChild(
                "##Tree",
                new Vector2(m_leftWidth, 0),
                ImGuiChildFlags.None,
                ImGuiWindowFlags.HorizontalScrollbar
            );

            DrawDirectoryTree(m_rootPathNative, m_rootPath);
            ImGuiNet.EndChild();
        }

        // Clear one-frame reveal request after tree has been rendered
        if (m_revealOpenPending)
        {
            m_revealOpenPending = false;
            m_revealOpenPaths.Clear();
        }

        // Middle: Splitter
        {
            ImGuiNet.SameLine();
            float maxLeft = Math.Max(C_LEFT_MIN_WIDTH, totalW - C_RIGHT_MIN_WIDTH);
            bool draggingSplitter = DrawSplitter(ref m_leftWidth, minLeft: C_LEFT_MIN_WIDTH, maxLeft: maxLeft);
            ImGuiNet.SameLine();

            if (totalW > 0f)
            {
                float maxLeft2 = Math.Max(C_LEFT_MIN_WIDTH, totalW - C_RIGHT_MIN_WIDTH);

                if (draggingSplitter)
                {
                    m_leftRatio = Math.Clamp(m_leftWidth / totalW, 0f, 1f);
                }
                else
                {
                    float target = m_leftRatio * totalW;
                    m_leftWidth = Math.Clamp(target, C_LEFT_MIN_WIDTH, maxLeft2);
                }
            }
        }

        // Right: content
        {
            ImGuiNet.BeginChild("##Content", new Vector2(0, 0));
            DrawToolbar();
            DrawContent();
            ImGuiNet.EndChild();
        }

        ImGuiNet.EndChild(); // body

        ImGuiNet.Separator();
        DrawStatusBarFinderPath(statusH);

        ImGuiNet.EndChild(); // root
        ImGuiNet.PopStyleVar();

        DrawRenamePopup();
        DrawDeletePopup();
    }

    // ============================
    // Toolbar
    // ============================
    private void DrawToolbar()
    {
        ImGuiNet.AlignTextToFramePadding();

        bool canBack = m_historyIndex > 0;
        bool canForward = m_historyIndex >= 0 && m_historyIndex < m_history.Count - 1;

        ImGuiNet.BeginDisabled(!canBack);
        if (ImGuiNet.Button("<##Back")) NavigateHistory(-1);
        ImGuiNet.EndDisabled();
        ImGuiNet.SameLine();

        ImGuiNet.BeginDisabled(!canForward);
        if (ImGuiNet.Button(">##Forward")) NavigateHistory(+1);
        ImGuiNet.EndDisabled();
        ImGuiNet.SameLine();

        ImGuiNet.TextUnformatted(GetCurrentFolderDisplayName());
    }

    // ============================
    // Left Tree (folders + files)
    // ============================
    private void DrawDirectoryTree(string pathNative, string pathNormalized)
    {
        if (!Directory.Exists(pathNative))
            return;

        string displayName = IsSamePath(pathNormalized, m_rootPath) ? "Assets" : Path.GetFileName(pathNative);

        // Highlight is driven by selection (single-click), not currentDir.
        bool selected = IsSelected(pathNormalized);

        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanFullWidth;
        if (selected) flags |= ImGuiTreeNodeFlags.Selected;

        // Keep currentDir open behavior
        bool shouldOpen = IsAncestorOrSelf(pathNormalized, m_currentDir);

        // One-frame forced reveal for selection path chain
        if (m_revealOpenPending && m_revealOpenPaths.Contains(pathNormalized))
        {
            ImGuiNet.SetNextItemOpen(true, ImGuiCond.Always);
        }
        else
        {
            ImGuiNet.SetNextItemOpen(shouldOpen, ImGuiCond.Once);
        }

        bool open = ImGuiNet.TreeNodeEx($"##tree_{pathNormalized}", flags);

        // Single click: select only (do NOT enter)
        if (ImGuiNet.IsItemClicked(ImGuiMouseButton.Left))
        {
            SelectFolder(pathNormalized);
        }

        // Double click: enter (navigate)
        if (ImGuiNet.IsItemHovered() && ImGuiNet.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            NavigateTo(pathNormalized, pushHistory: true);
        }

        bool insideDirectory = IsSamePath(pathNormalized, m_currentDir);
        if (insideDirectory)
        {
            ImGuiHost.UseFont(ImGuiFontStyle.BoldItalic);
            ImGuiNet.SameLine();
            EditorImGuiEx.DrawIconAndText(ImGuiIcon.Folder, displayName);
            EditorImGuiEx.UnderlineLastItem();
            ImGuiHost.UseFont(ImGuiFontStyle.Regular);
        }
        else
        {
            ImGuiNet.SameLine();
            EditorImGuiEx.DrawIconAndText(ImGuiIcon.Folder, displayName);
        }

        if (ImGuiNet.BeginPopupContextItem($"##tree_ctx_{pathNormalized}"))
        {
            DrawCommonContextItems(pathNormalized);
            ImGuiNet.EndPopup();
        }

        if (open)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(pathNative))
                {
                    if (IsHidden(dir)) continue;
                    DrawDirectoryTree(dir, NormalizePath(dir));
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
                Log.Error(e.Message);
            }

            ImGuiNet.TreePop();
        }
    }

    private void DrawTreeFileItem(string filePathNative)
    {
        string full = NormalizePath(filePathNative);
        string name = Path.GetFileName(full);

        var fi = new FileInfo(filePathNative);
        string type = ToType(fi.Extension);
        bool selected = IsSelected(full);

        var flags =
            ImGuiTreeNodeFlags.Leaf |
            ImGuiTreeNodeFlags.NoTreePushOnOpen |
            ImGuiTreeNodeFlags.SpanFullWidth;

        if (selected) flags |= ImGuiTreeNodeFlags.Selected;

        ImGuiNet.TreeNodeEx($"##tree_file_{full}", flags);

        // Click
        if (ImGuiNet.IsItemClicked(ImGuiMouseButton.Left))
        {
            SelectFile(full);
        }
            
        // Double Click
        if (ImGuiNet.IsItemHovered() && ImGuiNet.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            TryOpenFile(full);
        }

        ImGuiNet.SameLine();
        EditorImGuiEx.DrawIconAndText(FileIcon(type), name);

        if (ImGuiNet.BeginPopupContextItem($"##tree_file_ctx_{full}"))
        {
            DrawItemContextItems(new Entry(
                fullPath: full,
                name: name,
                isDir: false,
                type: type,
                source: "~"
            ));
            ImGuiNet.EndPopup();
        }
    }

    // ============================
    // Right Content
    // ============================
    private void DrawContent()
    {
        // TopBar: View toggle + Search
        DrawViewTopBar();
        ImGuiNet.Separator();

        bool searching = !string.IsNullOrWhiteSpace(m_search);

        if (searching) RefreshSearchSnapshot(force: false);
        else RefreshSnapshot(force: false);

        if (ImGuiNet.BeginPopupContextWindow("##content_ctx", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            DrawCommonContextItems(m_currentDir);
            ImGuiNet.EndPopup();
        }

        var src = searching ? m_searchSnapshot.entries : m_snapshot.entries;
        var entries = ApplyFilterAndSort(src);

        if (m_viewMode == ViewMode.Grid)
            DrawGridWithScaleBar(entries);
        else
            DrawListWithSortBar(entries);
    }

    private void DrawViewTopBar()
    {
        ImGuiNet.AlignTextToFramePadding();

        string viewLabel = m_viewMode == ViewMode.Grid ? "Grid" : "List";
        if (ImGuiNet.Button(viewLabel))
            m_viewMode = m_viewMode == ViewMode.Grid ? ViewMode.List : ViewMode.Grid;

        ImGuiNet.SameLine();

        float avail = ImGuiNet.GetContentRegionAvail().X;
        ImGuiNet.SetNextItemWidth(Math.Max(120f, avail));
        ImGuiNet.InputTextWithHint("##Search", "Search", ref m_search, 256);
    }

    // ============================
    // Grid (fixed spacing; no periodic jitter on resize)
    // ============================
    private void DrawGrid(List<Entry> entries, float iconSize)
    {
        float availW = ImGuiNet.GetContentRegionAvail().X;
        float cellW = iconSize;

        int cols = Math.Max(1, (int)Math.Floor(availW / cellW));

        var flags =
            ImGuiTableFlags.SizingFixedFit |
            ImGuiTableFlags.PadOuterX |
            ImGuiTableFlags.NoBordersInBody |
            ImGuiTableFlags.NoSavedSettings;

        if (!ImGuiNet.BeginTable("##grid_table", cols, flags)) return;

        for (int c = 0; c < cols; c++)
            ImGuiNet.TableSetupColumn($"##gc{c}", ImGuiTableColumnFlags.WidthFixed, cellW);

        int col = 0;
        ImGuiNet.TableNextRow();

        foreach (var e in entries)
        {
            ImGuiNet.TableSetColumnIndex(col);
            DrawGridItem(e, iconSize, 15f);

            col++;
            if (col >= cols)
            {
                col = 0;
                ImGuiNet.TableNextRow();
            }
        }

        ImGuiNet.EndTable();
    }

    private void DrawGridItem(Entry e, float itemSize, float iconScale)
    {
        ImGuiNet.BeginGroup();
        ImGuiNet.PushID(e.fullPath);

        // Hoverable
        Vector2 p0 = ImGuiNet.GetCursorScreenPos();
        Vector2 size = new Vector2(itemSize, itemSize);
        
        ImGuiNet.InvisibleButton("##grid_item_btn", size);
        
        bool selected = IsSelected(e.fullPath);
        bool hovered = ImGuiNet.IsItemHovered();
        bool clicked = ImGuiNet.IsItemClicked(ImGuiMouseButton.Left);
        if (hovered || selected)
        {
            var col = ImGuiNet.GetColorU32(selected ? ImGuiCol.HeaderActive : ImGuiCol.HeaderHovered);
            uint a = (uint)(hovered && !selected ? 80 : 110);
            uint bg = (col & 0x00FFFFFFu) | (a << 24);
            float rounding = 10f;
            ImGuiNet.GetWindowDrawList().AddRectFilled(p0, p0 + size, bg, rounding);
        }

        // Click event
        if (clicked)
        {
            if (e.isDir) SelectFolder(e.fullPath);
            else SelectFile(e.fullPath);
        }

        // Double click
        if (hovered && ImGuiNet.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (e.isDir)
            {
                NavigateTo(e.fullPath, pushHistory: true);
            }
            else
            {
                TryOpenFile(e.fullPath);
            }
        }

        if (ImGuiNet.BeginPopupContextItem("##item_ctx"))
        {
            DrawItemContextItems(e);
            ImGuiNet.EndPopup();
        }
        
        // Drag
        if (!e.isDir && ImGuiNet.BeginDragDropSource())
        {
            var relativePath = GetRelativeDisplay(AssetManager.assetDirectory, e.fullPath);
            EditorImGuiEx.SetDragPayload(C_ASSET_GUID_TYPE, AssetManager.GetGuid(relativePath));
            ImGuiNet.Text($"Dragging {relativePath}");
            ImGuiNet.EndDragDropSource();
        }
        
        // Icon
        var icon = e.isDir ? ImGuiIcon.Folder : FileIcon(e.type);
        var currentFont = ImGuiHost.GetCurrentFont();
        ImGuiHost.UseFont(ImGuiFontStyle.Icon, itemSize * iconScale / ImGuiNet.GetFontSize());
        
        Vector2 winPos = ImGuiNet.GetWindowPos();
        Vector2 p0Screen = p0;
        Vector2 p0Local = p0Screen - winPos;
        Vector2 iconTextSize = ImGuiNet.CalcTextSize(icon);
        float iconX = MathF.Floor(p0Local.x + (itemSize - iconTextSize.x) * 0.5f);
        float iconY = MathF.Floor(p0Local.y + (itemSize - iconTextSize.y) * 0.5f);

        ImGuiNet.SetCursorPos(new Vector2(iconX, iconY));
        ImGuiNet.TextUnformatted(icon);
        ImGuiHost.UseFont(currentFont);
        ImGuiNet.SetCursorPos(new Vector2(p0Local.x, p0Local.y + itemSize));

        // Text
        ImGuiNet.PushTextWrapPos(ImGuiNet.GetCursorPosX() + itemSize);
        var textWidth = ImGuiNet.CalcTextSize(e.name).X;
        var currentCursorX = ImGuiNet.GetCursorPosX();
        var offsetX = textWidth <= itemSize ? (itemSize - textWidth) / 2 : 0;
        ImGuiNet.SetCursorPosX(currentCursorX + offsetX);
        ImGuiNet.TextWrapped(e.name);
        ImGuiNet.PopTextWrapPos();

        ImGuiNet.PopID();
        ImGuiNet.EndGroup();
    }

    private void DrawGridWithScaleBar(List<Entry> entries)
    {
        float barH = ImGuiNet.GetFrameHeight() + ImGuiNet.GetStyle().ItemSpacing.Y * 2f;

        var avail = ImGuiNet.GetContentRegionAvail();
        float gridH = Math.Max(0f, avail.Y - barH);

        ImGuiNet.BeginChild("##GridArea", new Vector2(0, gridH));
        float iconSize = C_GRID_ICON_SIZE * m_gridScale;
        DrawGrid(entries, iconSize);
        ImGuiNet.EndChild();

        ImGuiNet.Separator();

        ImGuiNet.BeginChild("##GridScaleBar", new Vector2(0, 0), ImGuiChildFlags.None);
        DrawGridScaleSlider();
        ImGuiNet.EndChild();
    }

    private void DrawGridScaleSlider()
    {
        ImGuiNet.AlignTextToFramePadding();
        ImGuiNet.TextDisabled("Size");
        ImGuiNet.SameLine();

        float avail = ImGuiNet.GetContentRegionAvail().X;
        float sliderW = Math.Max(120f, avail);

        ImGuiNet.SetNextItemWidth(sliderW);

        if (ImGuiNet.SliderFloat(
                "##grid_scale",
                ref m_gridScale,
                C_GRID_SCALE_MIN,
                C_GRID_SCALE_MAX,
                "%.2fx",
                ImGuiSliderFlags.NoInput
            ))
        {
            m_gridScale = Math.Clamp(m_gridScale, C_GRID_SCALE_MIN, C_GRID_SCALE_MAX);
        }
    }

    // List: ONLY Name + Type + Source
    private void DrawList(List<Entry> entries)
    {
        var tableFlags =
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.BordersInnerV |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingFixedFit;

        var avail = ImGuiNet.GetContentRegionAvail();

        if (!ImGuiNet.BeginTable("##list_table", 3, tableFlags, new Vector2(0, avail.Y)))
            return;

        ImGuiNet.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed);
        ImGuiNet.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed);
        ImGuiNet.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch);

        ImGuiNet.TableHeadersRow();

        foreach (var e in entries)
        {
            float rowH = ImGuiNet.GetFrameHeight();

            ImGuiNet.TableNextRow(ImGuiTableRowFlags.None, rowH);
            ImGuiNet.TableSetColumnIndex(0);

            bool selected = IsSelected(e.fullPath);
            string selId = $"##name_{e.fullPath}";

            // Click
            if (ImGuiNet.Selectable(selId, selected, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0, rowH)))
            {
                if (e.isDir) SelectFolder(e.fullPath);
                else SelectFile(e.fullPath);
            }

            // Double Click
            if (ImGuiNet.IsItemHovered() && ImGuiNet.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                if (e.isDir)
                {
                    NavigateTo(e.fullPath, pushHistory: true);
                }
                else
                {
                    TryOpenFile(e.fullPath);
                }
            }

            if (ImGuiNet.BeginPopupContextItem($"##list_ctx_{e.fullPath}"))
            {
                DrawItemContextItems(e);
                ImGuiNet.EndPopup();
            }

            // Col 0: Name
            ImGuiNet.SameLine();
            ImGuiNet.SetCursorPosY(ImGuiNet.GetCursorPosY() + ImGuiNet.GetStyle().FramePadding.Y);
            EditorImGuiEx.DrawIconAndText(e.isDir ? ImGuiIcon.Folder : FileIcon(e.type), e.name);

            // Col 1: Type
            ImGuiNet.TableSetColumnIndex(1);
            ImGuiNet.AlignTextToFramePadding();
            ImGuiNet.TextUnformatted(e.isDir ? "FOLDER" : e.type);

            // Col 2: Source
            ImGuiNet.TableSetColumnIndex(2);
            ImGuiNet.AlignTextToFramePadding();
            ImGuiNet.TextUnformatted(e.source);
        }

        ImGuiNet.EndTable();
    }

    private void DrawListWithSortBar(List<Entry> entries)
    {
        float barH = ImGuiNet.GetFrameHeight() + ImGuiNet.GetStyle().ItemSpacing.Y * 2f;
        var avail = ImGuiNet.GetContentRegionAvail();
        float listH = Math.Max(0f, avail.Y - barH);

        ImGuiNet.BeginChild("##ListArea", new Vector2(0, listH));
        DrawList(entries);
        ImGuiNet.EndChild();

        ImGuiNet.Separator();

        ImGuiNet.BeginChild("##ListSortBar", new Vector2(0, 0), ImGuiChildFlags.None);
        DrawListSortBar();
        ImGuiNet.EndChild();
    }

    private void DrawListSortBar()
    {
        ImGuiNet.AlignTextToFramePadding();

        ImGuiNet.TextDisabled("Sort");
        ImGuiNet.SameLine();

        if (ImGuiNet.Button(m_sortAscending ? "Asc##sort" : "Desc##sort"))
            m_sortAscending = !m_sortAscending;

        ImGuiNet.SameLine();

        float w = ImGuiNet.GetContentRegionAvail().X;
        ImGuiNet.SetNextItemWidth(Math.Max(80f, w));

        SortCombo("##sortField", ref m_sortField);
    }

    private static void SortCombo(string id, ref SortField field)
    {
        if (ImGuiNet.BeginCombo(id, field.ToString()))
        {
            foreach (SortField v in Enum.GetValues(typeof(SortField)))
            {
                bool selected = (v == field);
                if (ImGuiNet.Selectable(v.ToString(), selected))
                    field = v;
                if (selected) ImGuiNet.SetItemDefaultFocus();
            }
            ImGuiNet.EndCombo();
        }
    }

    // ============================
    // Context Menus
    // ============================
    private void DrawItemContextItems(Entry e)
    {
        if (e.isDir)
        {
            if (ImGuiNet.MenuItem("Open"))
                NavigateTo(e.fullPath, pushHistory: true);
        }

        if (ImGuiNet.MenuItem("Reveal in Finder/Explorer"))
            RevealInSystem(e.fullPath);

        ImGuiNet.Separator();

        if (ImGuiNet.MenuItem("Rename"))
            BeginRename(e.fullPath);

        if (ImGuiNet.MenuItem("Delete"))
            BeginDelete(e.fullPath);
    }

    private void DrawCommonContextItems(string targetFolderNormalized)
    {
        if (ImGuiNet.MenuItem("New Folder"))
            CreateNewFolder(targetFolderNormalized);

        if (ImGuiNet.MenuItem("Reveal in Finder/Explorer"))
            RevealInSystem(targetFolderNormalized);
    }

    // ============================
    // Navigation / History
    // ============================
    private void NavigateTo(string dirNormalized, bool pushHistory)
    {
        dirNormalized = NormalizePath(dirNormalized);

        // If same as current, do nothing (no history, no refresh)
        if (IsSamePath(dirNormalized, m_currentDir))
            return;

        string dirNative = ToNativePath(dirNormalized);
        if (!Directory.Exists(dirNative))
            return;

        m_currentDir = dirNormalized;
        m_currentDirNative = dirNative;

        // Finder-like behavior: entering a folder clears selection
        ClearSelection();

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
            int ver = AssetManager.assetDirectoryChangeVersion;
            bool changed = ver != m_assetAppliedVersion;

            if (changed)
            {
                m_assetAppliedVersion = ver;
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

                m_snapshot.entries.Add(new Entry(
                    fullPath: NormalizePath(d),
                    name: Path.GetFileName(d),
                    isDir: true,
                    type: "Folder",
                    source: "~"
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
                    source: "~"
                ));
            }
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    private void RefreshSearchSnapshot(bool force)
    {
        if (!force)
        {
            bool queryChanged = !string.Equals(m_search, m_searchLast, StringComparison.Ordinal);
            int ver = AssetManager.assetDirectoryChangeVersion;
            bool changed = ver != m_assetAppliedVersion;

            if (!queryChanged)
            {
                if (changed)
                {
                    // AssetManager already coalesces bursts; refresh immediately.
                }
                else
                {
                    var now = DateTime.UtcNow;
                    if ((now - m_searchSnapshotTime).TotalSeconds < C_SNAPSHOT_TTL_SECONDS)
                        return;
                }
            }
        }

        m_searchLast = m_search;
        m_searchSnapshotTime = DateTime.UtcNow;

        m_assetAppliedVersion = AssetManager.assetDirectoryChangeVersion;

        m_searchSnapshot.Clear();

        try
        {
            if (string.IsNullOrWhiteSpace(m_search))
                return;

            if (!Directory.Exists(m_currentDirNative))
                return;

            foreach (var d in Directory.EnumerateDirectories(m_currentDirNative, "*", SearchOption.AllDirectories))
            {
                if (IsHidden(d)) continue;

                string rel = GetRelativeDisplay(m_currentDirNative, d);
                string name = Path.GetFileName(d);
                string src = BuildSourceFromRelative(rel);

                m_searchSnapshot.entries.Add(new Entry(
                    fullPath: NormalizePath(d),
                    name: string.IsNullOrWhiteSpace(name) ? rel : name,
                    isDir: true,
                    type: "Folder",
                    source: src
                ));
            }

            foreach (var f in Directory.EnumerateFiles(m_currentDirNative, "*", SearchOption.AllDirectories))
            {
                if (IsHidden(f)) continue;
                if (IsEditorFilteredFile(f)) continue;

                var fi = new FileInfo(f);

                string rel = GetRelativeDisplay(m_currentDirNative, f);
                string src = BuildSourceFromRelative(rel);

                m_searchSnapshot.entries.Add(new Entry(
                    fullPath: NormalizePath(f),
                    name: fi.Name,
                    isDir: false,
                    type: ToType(fi.Extension),
                    source: src
                ));
            }
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    private List<Entry> ApplyFilterAndSort(List<Entry> src)
    {
        IEnumerable<Entry> q = src;

        if (!string.IsNullOrWhiteSpace(m_search))
        {
            string s = m_search.Trim();
            q = q.Where(e =>
                e.name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                e.source.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        q = q.OrderByDescending(e => e.isDir);

        Func<Entry, object> key = m_sortField switch
        {
            SortField.Name => e => e.name,
            SortField.Type => e => e.type,
            SortField.Source => e => e.source,
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
        ImGuiNet.OpenPopup("Rename##popup");
    }

    private void DrawRenamePopup()
    {
        if (!ImGuiNet.BeginPopupModal("Rename##popup", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGuiNet.TextUnformatted("New name:");
        ImGuiNet.SetNextItemWidth(360f);
        ImGuiNet.InputText("##rename", ref m_renameBuffer, 256);

        ImGuiNet.Spacing();

        bool ok = ImGuiNet.Button("OK", new Vector2(120, 0));
        ImGuiNet.SameLine();
        bool cancel = ImGuiNet.Button("Cancel", new Vector2(120, 0));

        if (ok && m_renameTargetPath != null)
        {
            TryRename(m_renameTargetPath, m_renameBuffer);
            m_renameTargetPath = null;
            ImGuiNet.CloseCurrentPopup();
        }
        else if (cancel)
        {
            m_renameTargetPath = null;
            ImGuiNet.CloseCurrentPopup();
        }

        ImGuiNet.EndPopup();
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

            bool renamed = false;

            if (Directory.Exists(oldNative))
            {
                Directory.Move(oldNative, newNative);
                renamed = true;
            }
            else if (File.Exists(oldNative))
            {
                File.Move(oldNative, newNative);
                renamed = true;
            }

            if (renamed)
            {
                // keep selection on renamed item
                if (IsSelected(oldPathNormalized))
                    SelectPath(newPathNorm);

                RefreshSnapshot(force: true);
            }
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    private void BeginDelete(string pathNormalized)
    {
        m_deleteTargetPath = pathNormalized;
        ImGuiNet.OpenPopup("Delete##popup");
    }

    private void DrawDeletePopup()
    {
        if (!ImGuiNet.BeginPopupModal("Delete##popup", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        string name = m_deleteTargetPath != null ? Path.GetFileName(m_deleteTargetPath) : "";
        ImGuiNet.TextUnformatted($"Delete '{name}' ?");
        ImGuiNet.TextDisabled("This action cannot be undone.");

        ImGuiNet.Spacing();

        bool del = ImGuiNet.Button("Delete", new Vector2(120, 0));
        ImGuiNet.SameLine();
        bool cancel = ImGuiNet.Button("Cancel", new Vector2(120, 0));

        if (del && m_deleteTargetPath != null)
        {
            TryDelete(m_deleteTargetPath);
            m_deleteTargetPath = null;
            ImGuiNet.CloseCurrentPopup();
        }
        else if (cancel)
        {
            m_deleteTargetPath = null;
            ImGuiNet.CloseCurrentPopup();
        }

        ImGuiNet.EndPopup();
    }

    private void TryDelete(string pathNormalized)
    {
        try
        {
            string native = ToNativePath(pathNormalized);

            bool deleted = false;

            if (Directory.Exists(native))
            {
                Directory.Delete(native, recursive: true);
                deleted = true;
            }
            else if (File.Exists(native))
            {
                File.Delete(native);
                deleted = true;
            }

            if (deleted)
            {
                if (IsSelected(pathNormalized))
                    ClearSelection();

                RefreshSnapshot(force: true);
            }
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
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

            // select then rename
            SelectFolder(candidateNorm);
            BeginRename(candidateNorm);
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    // ============================
    // Splitter
    // ============================
    private static bool DrawSplitter(ref float leftWidth, float minLeft, float maxLeft)
    {
        float height = ImGuiNet.GetContentRegionAvail().Y;

        ImGuiNet.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        ImGuiNet.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, 0));
        ImGuiNet.Button("##Splitter", new Vector2(C_SPLITTER_WIDTH, height));
        ImGuiNet.PopStyleVar(2);

        bool active = ImGuiNet.IsItemActive();
        if (active)
        {
            float delta = ImGuiNet.GetIO().MouseDelta.X;
            leftWidth = Math.Clamp(leftWidth + delta, minLeft, maxLeft);
            ImGuiHost.SetStorageData("Editor.File.SplitterLeftWidth", leftWidth);
        }

        if (ImGuiNet.IsItemHovered())
            ImGuiNet.SetMouseCursor(ImGuiMouseCursor.ResizeEW);

        return active;
    }

    // ============================
    // Statusbar (Finder-like path: clickable segments + horizontal scroll)
    // ============================
    private void DrawStatusBarFinderPath(float height)
    {
        ImGuiNet.BeginChild(
            "##FileBrowserStatus",
            new Vector2(0, height),
            ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar
        );

        if (ImGuiNet.SmallButton("Assets##sb_root"))
            NavigateTo(m_rootPath, pushHistory: true);

        string running = m_rootPath;
        var parts = SplitPathRelativeToRoot(m_currentDir, m_rootPath);

        for (int i = 0; i < parts.Count; i++)
        {
            ImGuiNet.SameLine();
            ImGuiNet.TextDisabled(">");
            ImGuiNet.SameLine();

            running = NormalizePath(Path.Combine(running, parts[i]));
            if (ImGuiNet.SmallButton($"{parts[i]}##sb_{i}"))
                NavigateTo(running, pushHistory: true);
        }

        ImGuiNet.EndChild();
    }

    // ============================
    // Selection API (extensible; no syncing)
    // ============================
    private void SelectPath(string? pathNormalized)
    {
        string? n = string.IsNullOrWhiteSpace(pathNormalized) ? null : NormalizePath(pathNormalized);
        if (IsSamePath(n, m_selectedPath))
            return;

        m_selectedPath = n;

        // Ensure tree opens to reveal selection
        BuildRevealOpenPathsForSelection(n);
    }

    private void BuildRevealOpenPathsForSelection(string? selectedNormalized)
    {
        m_revealOpenPaths.Clear();
        m_revealOpenPending = false;

        if (string.IsNullOrWhiteSpace(selectedNormalized))
            return;

        // If selection is a file, reveal its parent folder chain
        string targetFolderNorm = GetFolderNormalizedForPath(selectedNormalized);

        // Must be inside root to reveal
        if (!IsAncestorOrSelf(m_rootPath, targetFolderNorm))
            return;

        // Build root -> ... -> target chain
        string running = m_rootPath.TrimEnd('/');
        var parts = SplitPathRelativeToRoot(targetFolderNorm, m_rootPath);

        m_revealOpenPaths.Add(m_rootPath);

        foreach (var part in parts)
        {
            running = NormalizePath(Path.Combine(running, part));
            m_revealOpenPaths.Add(running);
        }

        m_revealOpenPending = true;
    }

    private string GetFolderNormalizedForPath(string pathNormalized)
    {
        try
        {
            string native = ToNativePath(pathNormalized);

            if (Directory.Exists(native))
                return NormalizePath(native);

            string? parent = Path.GetDirectoryName(native);
            if (string.IsNullOrWhiteSpace(parent))
                return pathNormalized;

            return NormalizePath(parent);
        }
        catch
        {
            return pathNormalized;
        }
    }

    private void ClearSelection()
    {
        m_selectedPath = null;
        m_revealOpenPending = false;
        m_revealOpenPaths.Clear();
    }

    private void SelectFile(string filePathNormalized)
    {
        // TODO: Future extension point: open inspector, preview, etc.
        SelectPath(filePathNormalized);
    }

    private void SelectFolder(string folderPathNormalized)
    {
        // TODO: Future extension point: show folder meta, etc.
        SelectPath(folderPathNormalized);
    }

    private bool IsSelected(string? pathNormalized)
    {
        if (string.IsNullOrWhiteSpace(pathNormalized) || string.IsNullOrWhiteSpace(m_selectedPath))
            return false;

        return IsSamePath(pathNormalized, m_selectedPath);
    }

    // ============================
    // Utility
    // ============================
    private static string NormalizePath(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        return Path.GetFullPath(p).Replace('\\', '/');
    }

    private string ToNativePath(string normalized)
    {
        string n = NormalizePath(normalized).TrimEnd('/');
        string rootN = m_rootPath.TrimEnd('/');

        if (IsSamePath(n, rootN))
            return m_rootPathNative;

        if (!n.StartsWith(rootN, StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(normalized);

        string rel = n.Substring(rootN.Length).TrimStart('/');
        return Path.Combine(m_rootPathNative, rel.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool IsSamePath(string? a, string? b)
    {
        if (a == null || b == null) return false;

        return string.Equals(
            NormalizePath(a).TrimEnd('/'),
            NormalizePath(b).TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase
        );
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
        return file.EndsWith(AssetManager.C_ASSET_POSTFIX, StringComparison.OrdinalIgnoreCase);
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
            return Path.GetRelativePath(baseDirNative, fullNative).Replace('\\', '/');
        }
        catch
        {
            return Path.GetFileName(fullNative);
        }
    }

    private static string ToType(string? ext)
    {
        string e = ext ?? "";
        if (e.StartsWith(".")) e = e.Substring(1);
        if (string.IsNullOrWhiteSpace(e)) e = "File";
        return e.ToUpperInvariant();
    }

    private static string FileIcon(string type)
    {
        return type switch
        {
            "PNG" => ImGuiIcon.Image,
            "SCENE" => ImGuiIcon.ObjectGroup,
            _ => ImGuiIcon.File
        };
    }

    private static void TryOpenFile(string fullPathNormalized)
    {
        var ext = Path.GetExtension(fullPathNormalized);
        if (!ext.Equals(".scene", StringComparison.OrdinalIgnoreCase))
            return;

        // Convert normalized path (/) back into a native path so Path APIs behave.
        string fullNative = fullPathNormalized.Replace('/', Path.DirectorySeparatorChar);
        string rel = GetRelativeDisplay(AssetManager.assetDirectory, fullNative);
        if (rel.StartsWith("..", StringComparison.Ordinal))
            return;

        EditorSceneAssetIO.OpenScene(rel);
    }

    private static void RevealInSystem(string path)
    {
        throw new NotImplementedException(path);
    }

    private string GetCurrentFolderDisplayName()
    {
        if (IsSamePath(m_currentDir, m_rootPath))
            return "Assets";

        string name = Path.GetFileName(m_currentDirNative.TrimEnd(Path.DirectorySeparatorChar, '/', '\\'));
        return string.IsNullOrWhiteSpace(name) ? "Assets" : name;
    }

    // Build Finder-like source:
    // - If item is in current dir => "~"
    // - Else => "~/<dirRel>" (for a file, dirRel excludes filename; for a dir, dirRel excludes its own name)
    private static string BuildSourceFromRelative(string rel)
    {
        if (string.IsNullOrWhiteSpace(rel))
            return "~";

        rel = rel.Replace('\\', '/').Trim('/');
        if (rel.Length == 0)
            return "~";

        int lastSlash = rel.LastIndexOf('/');
        if (lastSlash < 0)
            return "~";

        string parent = rel.Substring(0, lastSlash).Trim('/');
        if (string.IsNullOrWhiteSpace(parent))
            return "~";

        return "~/" + parent;
    }
}
