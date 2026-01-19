using System;
using System.Collections.Generic;
using System.IO;

using ImGuiNET;

using Inno.Core.Math;
using Inno.Platform.Graphics;
using Inno.Platform.Window;

namespace Inno.Platform.ImGui.Bridge;

/// <summary>
/// Platform-level ImGui implementation which relies only on Inno.Platform interfaces.
/// </summary>
internal sealed class ImGuiImpl : IImGui
{
    private readonly IWindowFactory m_windowFactory;
    private readonly IGraphicsDevice m_graphicsDevice;
    private readonly IWindow m_mainWindow;

    private readonly ICommandList m_commandList;
    private readonly ImGuiController m_controller;

    // Contexts (your editor expects two pointers; at the platform level we keep them identical for now)
    public IntPtr mainMainContextPtrImpl { get; }
    public IntPtr virtualContextPtrImpl { get; }

    // Fonts
    private float m_zoomRate = 1f;
    private ImGuiAlias m_currentFont;
    private readonly float m_dpiScale = 1f; // intentionally fixed until a Veldrid-free DPI API exists

    private readonly Dictionary<ImGuiFontSize, ImFontPtr> m_fontRegular = new();
    private readonly Dictionary<ImGuiFontSize, ImFontPtr> m_fontBold = new();
    private readonly Dictionary<ImGuiFontSize, ImFontPtr> m_fontItalic = new();
    private readonly Dictionary<ImGuiFontSize, ImFontPtr> m_fontBoldItalic = new();
    private readonly Dictionary<ImGuiFontSize, ImFontPtr> m_icon = new();

    // Ini
    private readonly string m_iniPath;
    private DateTime m_lastIniWriteUtc;

    public ImGuiImpl(IWindowFactory windowFactory, ImGuiColorSpaceHandling colorSpaceHandling)
    {
        m_windowFactory = windowFactory;
        m_graphicsDevice = windowFactory.graphicsDevice;
        m_mainWindow = windowFactory.mainWindow;

        m_commandList = m_graphicsDevice.CreateCommandList();
        m_controller = new ImGuiController(windowFactory, colorSpaceHandling);

        // Fonts: caller/editor owns the policy; platform ships sane defaults.
        m_controller.ClearAllFonts();
        SetupFonts(m_dpiScale);
        SetupIcons(m_dpiScale);

        m_controller.RebuildFontTexture();

        mainMainContextPtrImpl = ImGuiNET.ImGui.GetCurrentContext();
        virtualContextPtrImpl = mainMainContextPtrImpl;
        ImGuiNET.ImGui.SetCurrentContext(mainMainContextPtrImpl);
        ImGuiNET.ImGui.GetIO().FontGlobalScale = 1f / m_dpiScale;
        ImGuiNET.ImGui.PushFont(m_fontRegular[IImGui.C_DEFAULT_FONT_SIZE]);

        SetupImGuiStyle();

        // Ini / persistent storage
        m_iniPath = ImGuiNET.ImGui.GetIO().IniFilename;
        if (!string.IsNullOrWhiteSpace(m_iniPath) && File.Exists(m_iniPath))
        {
            ImGuiNET.ImGui.LoadIniSettingsFromDisk(m_iniPath);
            ImGuiIniDataFile.LoadAndEnsure(m_iniPath);
            m_lastIniWriteUtc = File.GetLastWriteTimeUtc(m_iniPath);
        }
        else
        {
            m_lastIniWriteUtc = DateTime.MinValue;
        }

        m_currentFont = new ImGuiAlias(ImGuiFontStyle.Regular, (float)IImGui.C_DEFAULT_FONT_SIZE);
    }

    public void BeginLayoutImpl(float deltaTime)
    {
        // Poll events here (best-effort) to avoid adding new engine-facing API.
        // The engine may already pump events elsewhere; this remains deterministic because
        // the adapter operates on the snapshot, not raw device state.
        var snapshot = m_mainWindow.PumpEvents(null);

        m_controller.Update(deltaTime, snapshot);

        // Default font
        UseFontImpl(ImGuiFontStyle.Regular, (float)IImGui.C_DEFAULT_FONT_SIZE);
    }

    public void EndLayoutImpl()
    {
        m_commandList.Begin();
        m_commandList.SetFrameBuffer(m_graphicsDevice.swapchainFrameBuffer);

        m_controller.Render(m_commandList, m_graphicsDevice.swapchainFrameBuffer);

        // Ini self-heal
        if (!string.IsNullOrWhiteSpace(m_iniPath) && File.Exists(m_iniPath))
        {
            var writeUtc = File.GetLastWriteTimeUtc(m_iniPath);
            if (writeUtc != m_lastIniWriteUtc)
            {
                ImGuiIniDataFile.EnsureSectionPresent(m_iniPath);
                m_lastIniWriteUtc = File.GetLastWriteTimeUtc(m_iniPath);
            }
        }

        m_commandList.End();
        m_graphicsDevice.Submit(m_commandList);
    }

    public IntPtr GetOrBindTextureImpl(ITexture texture) => m_controller.GetOrBindTexture(texture);

    public void UnbindTextureImpl(ITexture texture) => m_controller.UnbindTexture(texture);

    public void UseFontImpl(ImGuiFontStyle style, float? size)
    {
        m_currentFont = size == null
            ? new ImGuiAlias(style, m_currentFont.size)
            : new ImGuiAlias(style, (float)size);

        var requested = m_currentFont.size * m_zoomRate;
        var family = style switch
        {
            ImGuiFontStyle.Bold => m_fontBold,
            ImGuiFontStyle.Italic => m_fontItalic,
            ImGuiFontStyle.BoldItalic => m_fontBoldItalic,
            ImGuiFontStyle.Icon => m_icon,
            _ => m_fontRegular
        };

        ImGuiFontSize nearest = default;
        float nearestDist = float.MaxValue;
        bool exact = false;

        foreach (ImGuiFontSize s in Enum.GetValues(typeof(ImGuiFontSize)))
        {
            float v = (float)s;
            float dist = MathF.Abs(requested - v);
            if (MathHelper.AlmostEquals(requested, v))
            {
                nearest = s;
                exact = true;
                break;
            }
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = s;
            }
        }

        float scale = exact ? 1f : requested / (float)nearest;
        var font = family[nearest];

        ImGuiNET.ImGui.PopFont();
        ImGuiNET.ImGui.PushFont(font);
        ImGuiNET.ImGui.GetIO().FontGlobalScale = scale / m_dpiScale;
    }

    public ImGuiAlias GetCurrentFontImpl() => m_currentFont;

    public void ZoomImpl(float zoomRate) => m_zoomRate = zoomRate;

    public void SetStorageDataImpl(string key, object? value)
    {
        ImGuiDataStore.DATA[key] = ImGuiDataCodec.Encode(value);
        ImGuiNET.ImGui.GetIO().WantSaveIniSettings = true;
    }

    public T? GetStorageDataImpl<T>(string key, T? defaultValue)
    {
        return ImGuiDataStore.DATA.TryGetValue(key, out var payload)
            ? ImGuiDataCodec.Decode(payload, defaultValue)
            : defaultValue;
    }

    private void SetupFonts(float scale)
    {
        foreach (var fontSize in Enum.GetValues<ImGuiFontSize>())
        {
            var px = (float)fontSize * scale;
            m_fontRegular[fontSize] = m_controller.AddFontBase("JetBrainsMono-Regular.ttf", px);
            m_fontBold[fontSize] = m_controller.AddFontBase("JetBrainsMono-Bold.ttf", px);
            m_fontItalic[fontSize] = m_controller.AddFontBase("JetBrainsMono-Italic.ttf", px);
            m_fontBoldItalic[fontSize] = m_controller.AddFontBase("JetBrainsMono-BoldItalic.ttf", px);
        }
    }

    private void SetupIcons(float scale)
    {
        foreach (var fontSize in Enum.GetValues<ImGuiFontSize>())
        {
            var px = (float)fontSize * scale;
            m_icon[fontSize] = m_controller.AddFontIconMerged("FA-Solid-900.ttf", px, 0xE000, 0xF8FF);
        }
    }

    private static void SetupImGuiStyle()
    {
        var style = ImGuiNET.ImGui.GetStyle();
        style.WindowRounding = 6f;
        style.FrameRounding = 4f;
        style.ScrollbarRounding = 6f;
        style.GrabRounding = 4f;
    }

    public void Dispose()
    {
        m_controller.Dispose();
        m_commandList.Dispose();
    }
}
