using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

using ImGuiNET;

using Inno.Core.Events;
using Inno.Core.Math;
using Inno.Platform.Graphics;
using Inno.Platform.Window;

namespace Inno.Platform.ImGui.Bridge;

/// <summary>
/// Renderer/controller which bridges ImGui.NET to Inno.Platform.Graphics interfaces.
///
/// Design goals:
/// - No direct dependency on Veldrid types.
/// - All GPU work goes through IGraphicsDevice / ICommandList / resource interfaces.
/// - Texture binding is exposed as IntPtr ids (ImGui texture IDs) mapped to IResourceSet.
///
/// Notes:
/// - This implementation intentionally disables multi-viewport rendering. The engine-level graphics/window
///   abstractions currently expose only a single swapchain framebuffer, so rendering secondary viewports
///   would require additional API surface (per-window framebuffers / swapchains).
/// </summary>
internal sealed class ImGuiController : IDisposable
{
    private readonly IWindowFactory m_windowFactory;
    private readonly IGraphicsDevice m_graphicsDevice;
    private readonly Assembly m_assembly;
    private readonly ImGuiColorSpaceHandling m_colorSpaceHandling;

    private bool m_frameBegun;

    // GPU resources
    private IVertexBuffer? m_vertexBuffer;
    private IIndexBuffer? m_indexBuffer;
    private IUniformBuffer? m_projBuffer;

    private ITexture? m_fontTexture;
    private ISampler? m_fontSampler;

    private IShader? m_vertexShader;
    private IShader? m_fragmentShader;
    private IPipelineState? m_pipeline;

    private IResourceSet? m_set0;
    private IResourceSet? m_fontTextureSet;

    // Texture bindings (set = 1)
    private readonly Dictionary<ITexture, IntPtr> m_idsByTexture = new();
    private readonly Dictionary<IntPtr, (ITexture tex, IResourceSet set1)> m_bindingById = new();
    private int m_lastAssignedId = 100;
    private readonly IntPtr m_fontAtlasId = 1;

    // Fonts
    private readonly ImFontConfigPtr m_baseCfg;
    private readonly ImFontConfigPtr m_mergeCfg;
    private readonly List<string> m_iconNames = new();
    private readonly Dictionary<string, (IntPtr, int)> m_fontCache = new();
    private readonly Dictionary<string, (float, (ushort, ushort))> m_iconSizeCache = new();
    private readonly Dictionary<string, IntPtr> m_rangePtrCache = new();

    public ImGuiController(IWindowFactory windowFactory, ImGuiColorSpaceHandling colorSpaceHandling)
    {
        m_windowFactory = windowFactory;
        m_graphicsDevice = windowFactory.graphicsDevice;
        m_colorSpaceHandling = colorSpaceHandling;
        m_assembly = typeof(ImGuiController).GetTypeInfo().Assembly;

        var ctx = ImGuiNET.ImGui.CreateContext();
        ImGuiNET.ImGui.SetCurrentContext(ctx);

        var io = ImGuiNET.ImGui.GetIO();
        io.ConfigWindowsMoveFromTitleBarOnly = true;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        // IMPORTANT: disable multi-viewports until the platform exposes per-window swapchains/framebuffers.
        io.ConfigFlags &= ~ImGuiConfigFlags.ViewportsEnable;

        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
        io.BackendFlags |= ImGuiBackendFlags.HasSetMousePos;

        // Font configs
        unsafe
        {
            m_baseCfg = ImGuiNative.ImFontConfig_ImFontConfig();
            m_baseCfg.FontDataOwnedByAtlas = false;
            m_baseCfg.PixelSnapH = true;
            m_baseCfg.OversampleH = 2;
            m_baseCfg.OversampleV = 2;

            m_mergeCfg = ImGuiNative.ImFontConfig_ImFontConfig();
            m_mergeCfg.MergeMode = true;
            m_mergeCfg.FontDataOwnedByAtlas = false;
            m_mergeCfg.PixelSnapH = true;
        }

        // Default font (caller typically clears & re-adds their own)
        io.Fonts.AddFontDefault();
        io.Fonts.Flags |= ImFontAtlasFlags.NoBakedLines;

        CreateDeviceResources();
    }

    // ============================================================
    // Fonts
    // ============================================================
    
    public void ClearAllFonts()
    {
        var io = ImGuiNET.ImGui.GetIO();
        io.Fonts.Clear();
        
        foreach (var (ptr, _) in m_fontCache.Values) if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
        m_fontCache.Clear();
        
        foreach (var p in m_rangePtrCache.Values) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
        m_rangePtrCache.Clear();
        
        m_iconNames.Clear();
        m_iconSizeCache.Clear();
    }

    public void RegisterFontIcon(string baseFontFile, float sizePixels, (ushort, ushort) range)
    {
        LoadEmbeddedFontTTF(baseFontFile, out _);
        m_iconNames.Add(baseFontFile);
        m_iconSizeCache[baseFontFile] = (sizePixels, range);
    }

    public ImFontPtr AddIcons()
    {
        var io = ImGuiNET.ImGui.GetIO();
        ImFontPtr iconFont = new ImFontPtr();

        bool first = true;
        foreach (var iconName in m_iconNames)
        {
            var fontCache = m_fontCache[iconName];
            var sizeCache = m_iconSizeCache[iconName];
            var rangePtr = GetOrCreateIconRangePtr(iconName, sizeCache.Item2);

            if (first)
            {
                iconFont = io.Fonts.AddFontFromMemoryTTF(fontCache.Item1, fontCache.Item2, sizeCache.Item1, m_baseCfg, rangePtr);
                first = false;
            }
            else
            {
                io.Fonts.AddFontFromMemoryTTF(fontCache.Item1, fontCache.Item2, sizeCache.Item1, m_mergeCfg, rangePtr);
            }
            
        }
        
        return iconFont;
    }

    public ImFontPtr AddFontBase(string baseFontFile, float sizePixels)
    {
        var io = ImGuiNET.ImGui.GetIO();
        var basePtr = LoadEmbeddedFontTTF(baseFontFile, out var baseLen);
        var baseFont = io.Fonts.AddFontFromMemoryTTF(basePtr, baseLen, sizePixels, m_baseCfg);

        return baseFont;
    }
    
    private IntPtr LoadEmbeddedFontTTF(string shortName, out int length)
    {
        if (m_fontCache.TryGetValue(shortName, out var cached))
        {
            length = cached.Item2;
            return cached.Item1;
        }

        var resources = m_assembly.GetManifestResourceNames();
        var match = resources.FirstOrDefault(r => r.EndsWith(shortName, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            throw new FileNotFoundException($"Embedded resource '{shortName}' not found.");

        byte[] fontData;
        using (var s = m_assembly.GetManifestResourceStream(match)!)
        using (var ms = new MemoryStream())
        {
            s.CopyTo(ms);
            fontData = ms.ToArray();
        }

        var ptr = Marshal.AllocHGlobal(fontData.Length);
        Marshal.Copy(fontData, 0, ptr, fontData.Length);

        m_fontCache[shortName] = (ptr, fontData.Length);

        length = fontData.Length;
        return ptr;
    }
    
    private IntPtr GetOrCreateIconRangePtr(string iconName, (ushort start, ushort end) range)
    {
        string key = $"{iconName}:{range.start:X4}-{range.end:X4}";
        if (m_rangePtrCache.TryGetValue(key, out var p) && p != IntPtr.Zero)
            return p;

        IntPtr mem = Marshal.AllocHGlobal(sizeof(ushort) * 3);

        unsafe
        {
            ushort* u = (ushort*)mem;
            u[0] = range.start;
            u[1] = range.end;
            u[2] = 0;
        }

        m_rangePtrCache[key] = mem;
        return mem;
    }

    public void RebuildFontTexture()
    {
        RecreateFontDeviceTexture();
    }

    public IntPtr GetOrBindTexture(ITexture texture)
    {
        if (texture == null) throw new ArgumentNullException(nameof(texture));

        if (m_idsByTexture.TryGetValue(texture, out var id))
            return id;

        id = ++m_lastAssignedId;

        var set1 = m_graphicsDevice.CreateResourceSet(new ResourceSetBinding
        {
            shaderStages = ShaderStage.Fragment,
            textures = [texture],
            uniformBuffers = [],
            samplers = []
        });

        m_idsByTexture[texture] = id;
        m_bindingById[id] = (texture, set1);
        return id;
    }

    public void UnbindTexture(ITexture texture)
    {
        if (!m_idsByTexture.Remove(texture, out var id)) return;

        if (m_bindingById.TryGetValue(id, out var entry))
        {
            entry.set1.Dispose();
            m_bindingById.Remove(id);
        }
    }

    // ============================================================
    // Frame lifecycle
    // ============================================================

    public void Update(float deltaSeconds, EventSnapshot snapshot)
    {
        if (m_frameBegun)
            throw new InvalidOperationException("ImGuiController.Update called while a frame is active.");
        
        SetPerFrameImGuiData(deltaSeconds);
        UpdateImGuiInput(snapshot);
        ImGuiNET.ImGui.NewFrame();
        m_frameBegun = true;
    }

    public void Render(ICommandList commandList, IFrameBuffer targetFrameBuffer)
    {
        if (!m_frameBegun)
            return;

        m_frameBegun = false;
        ImGuiNET.ImGui.Render();

        var drawData = ImGuiNET.ImGui.GetDrawData();
        RenderImDrawData(drawData, commandList, targetFrameBuffer);
    }

    // ============================================================
    // GPU setup
    // ============================================================

    private void CreateDeviceResources()
    {
        // Buffers
        m_vertexBuffer = m_graphicsDevice.CreateVertexBuffer(1024 * 1024);
        m_indexBuffer = m_graphicsDevice.CreateIndexBuffer(512 * 1024);

        // Projection uniform
        m_projBuffer = m_graphicsDevice.CreateUniformBuffer("Projection", typeof(Matrix));

        // Sampler (for textures)
        m_fontSampler = m_graphicsDevice.CreateSampler(new SamplerDescription
        {
            filter = SamplerFilter.Linear,
            addressU = SamplerAddressMode.Clamp,
            addressV = SamplerAddressMode.Clamp
        });

        // Shaders
        var (vs, fs) = m_graphicsDevice.CreateVertexFragmentShader(
            new ShaderDescription
            {
                stage = ShaderStage.Vertex,
                sourceBytes = LoadEmbeddedShaderCode("imgui-vertex", ShaderStage.Vertex, m_colorSpaceHandling)
            },
            new ShaderDescription
            {
                stage = ShaderStage.Fragment,
                sourceBytes = LoadEmbeddedShaderCode("imgui-frag", ShaderStage.Fragment, m_colorSpaceHandling)
            });

        m_vertexShader = vs;
        m_fragmentShader = fs;

        // IMPORTANT:
        // Create font texture BEFORE pipeline, so we can define set=1 layout with 1 texture.
        RecreateFontDeviceTexture();

        // Pipeline
        m_pipeline = m_graphicsDevice.CreatePipelineState(new PipelineStateDescription
        {
            vertexShader = m_vertexShader,
            fragmentShader = m_fragmentShader,
            vertexLayoutTypes = [typeof(Vector2), typeof(Vector2), typeof(Color)],

            blendMode = BlendMode.AlphaBlend,
            primitiveTopology = PrimitiveTopology.TriangleList,
            depthStencilState = DepthStencilState.Disabled,

            // set = 0 and set = 1
            resourceLayoutSpecifiers =
            [
                // set 0: projection + sampler
                new ResourceSetBinding
                {
                    shaderStages = ShaderStage.Vertex | ShaderStage.Fragment,
                    uniformBuffers = [m_projBuffer],
                    textures = [],
                    samplers = [m_fontSampler]
                },

                // set 1: ONE texture (ImGui draw command binds texture id here)
                new ResourceSetBinding
                {
                    shaderStages = ShaderStage.Fragment,
                    uniformBuffers = [],
                    textures = [m_fontTexture!],
                    samplers = []
                }
            ]
        });

        // set0 (projection + sampler)
        m_set0 = m_graphicsDevice.CreateResourceSet(new ResourceSetBinding
        {
            shaderStages = ShaderStage.Vertex | ShaderStage.Fragment,
            uniformBuffers = [m_projBuffer],
            textures = [],
            samplers = [m_fontSampler]
        });
    }


    private void RecreateFontDeviceTexture()
    {
        // Dispose previous
        m_fontTextureSet?.Dispose();
        m_fontTexture?.Dispose();
        m_fontTextureSet = null;
        m_fontTexture = null;

        var io = ImGuiNET.ImGui.GetIO();

        unsafe
        {
            io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int width, out int height, out int bytesPerPixel);
            if (pixels == null || width <= 0 || height <= 0)
                throw new InvalidOperationException("ImGui font atlas pixels were not generated.");

            int size = width * height * bytesPerPixel;
            var managed = new byte[size];
            Marshal.Copy((IntPtr)pixels, managed, 0, size);

            m_fontTexture = m_graphicsDevice.CreateTexture(new TextureDescription
            {
                width = width,
                height = height,
                mipLevelCount = 1,
                format = PixelFormat.R8_G8_B8_A8_UNorm,
                usage = TextureUsage.Sampled,
                dimension = TextureDimension.Texture2D
            });
            m_fontTexture.Set(ref managed);

            m_fontTextureSet = m_graphicsDevice.CreateResourceSet(new ResourceSetBinding
            {
                shaderStages = ShaderStage.Fragment,
                uniformBuffers = [],
                textures = [m_fontTexture],
                samplers = []
            });

            // Tell ImGui what to use as "font texture id"
            io.Fonts.SetTexID(m_fontAtlasId);
        }
    }

    // ============================================================
    // Input
    // ============================================================

    private void SetPerFrameImGuiData(float deltaSeconds)
    {
        var io = ImGuiNET.ImGui.GetIO();
        io.DeltaTime = deltaSeconds <= 0 ? 1f / 60f : deltaSeconds;
        io.DisplaySize = new Vector2(m_windowFactory.mainWindow.width, m_windowFactory.mainWindow.height);
    }

    private void UpdateImGuiInput(EventSnapshot snapshot)
    {
        var io = ImGuiNET.ImGui.GetIO();
        
        // Input chars
        foreach (var c in snapshot.GetInputChars())
        {
            io.AddInputCharacter(c);
        }

        // Mouse move
        foreach (var e in snapshot.GetEvents(EventType.MouseMoved))
        {
            if (e is MouseMovedEvent mm)
                io.AddMousePosEvent(mm.x, mm.y);
        }

        // Mouse wheel
        foreach (var e in snapshot.GetEvents(EventType.MouseScrolled))
        {
            if (e is MouseScrolledEvent mw)
                io.AddMouseWheelEvent(mw.offsetX, mw.offsetY);
        }

        // Mouse buttons
        foreach (var e in snapshot.GetEvents(EventType.MouseButtonPressed))
        {
            if (e is MouseButtonPressedEvent mb)
                io.AddMouseButtonEvent(ToMouseButton(mb.button), true);
        }
        foreach (var e in snapshot.GetEvents(EventType.MouseButtonReleased))
        {
            if (e is MouseButtonReleasedEvent mb)
                io.AddMouseButtonEvent(ToMouseButton(mb.button), false);
        }

        // Keys
        foreach (var e in snapshot.GetEvents(EventType.KeyPressed))
        {
            if (e is KeyPressedEvent kp)
            {
                if (TryMapKey(kp.key, out var imguiKey))
                {
                    io.AddKeyEvent(imguiKey, true);
                }

                UpdateModifiers(io, kp.modifiers);
            }
        }
        foreach (var e in snapshot.GetEvents(EventType.KeyReleased))
        {
            if (e is KeyReleasedEvent kr)
            {
                if (TryMapKey(kr.key, out var imguiKey))
                    io.AddKeyEvent(imguiKey, false);

                UpdateModifiers(io, kr.modifiers);
            }
        }
    }

    private static void UpdateModifiers(ImGuiIOPtr io, Input.KeyModifier mods)
    {
        io.AddKeyEvent(ImGuiKey.ModCtrl, (mods & Input.KeyModifier.Control) != 0);
        io.AddKeyEvent(ImGuiKey.ModShift, (mods & Input.KeyModifier.Shift) != 0);
        io.AddKeyEvent(ImGuiKey.ModAlt, (mods & Input.KeyModifier.Alt) != 0);
        io.AddKeyEvent(ImGuiKey.ModSuper, (mods & Input.KeyModifier.Super) != 0);
    }

    private static int ToMouseButton(Input.MouseButton b)
    {
        return b switch
        {
            Input.MouseButton.Left => 0,
            Input.MouseButton.Right => 1,
            Input.MouseButton.Middle => 2,
            Input.MouseButton.XButton1 => 3,
            Input.MouseButton.XButton2 => 4,
            _ => 0
        };
    }

    private static bool TryMapKey(Input.KeyCode key, out ImGuiKey imguiKey)
    {
        // Letters
        if (key >= Input.KeyCode.A && key <= Input.KeyCode.Z)
        {
            imguiKey = ImGuiKey.A + ((int)key - (int)Input.KeyCode.A);
            return true;
        }

        // Digits
        if (key >= Input.KeyCode.D0 && key <= Input.KeyCode.D9)
        {
            imguiKey = ImGuiKey._0 + ((int)key - (int)Input.KeyCode.D0);
            return true;
        }

        imguiKey = key switch
        {
            Input.KeyCode.Tab => ImGuiKey.Tab,
            Input.KeyCode.LeftArrow => ImGuiKey.LeftArrow,
            Input.KeyCode.RightArrow => ImGuiKey.RightArrow,
            Input.KeyCode.UpArrow => ImGuiKey.UpArrow,
            Input.KeyCode.DownArrow => ImGuiKey.DownArrow,
            Input.KeyCode.PageUp => ImGuiKey.PageUp,
            Input.KeyCode.PageDown => ImGuiKey.PageDown,
            Input.KeyCode.Home => ImGuiKey.Home,
            Input.KeyCode.End => ImGuiKey.End,
            Input.KeyCode.Insert => ImGuiKey.Insert,
            Input.KeyCode.Delete => ImGuiKey.Delete,
            Input.KeyCode.Backspace => ImGuiKey.Backspace,
            Input.KeyCode.Space => ImGuiKey.Space,
            Input.KeyCode.Enter => ImGuiKey.Enter,
            Input.KeyCode.Escape => ImGuiKey.Escape,
            Input.KeyCode.LeftCtrl => ImGuiKey.LeftCtrl,
            Input.KeyCode.RightCtrl => ImGuiKey.RightCtrl,
            Input.KeyCode.LeftShift => ImGuiKey.LeftShift,
            Input.KeyCode.RightShift => ImGuiKey.RightShift,
            Input.KeyCode.LeftAlt => ImGuiKey.LeftAlt,
            Input.KeyCode.RightAlt => ImGuiKey.RightAlt,
            Input.KeyCode.LeftSuper => ImGuiKey.LeftSuper,
            Input.KeyCode.RightSuper => ImGuiKey.RightSuper,
            Input.KeyCode.F1 => ImGuiKey.F1,
            Input.KeyCode.F2 => ImGuiKey.F2,
            Input.KeyCode.F3 => ImGuiKey.F3,
            Input.KeyCode.F4 => ImGuiKey.F4,
            Input.KeyCode.F5 => ImGuiKey.F5,
            Input.KeyCode.F6 => ImGuiKey.F6,
            Input.KeyCode.F7 => ImGuiKey.F7,
            Input.KeyCode.F8 => ImGuiKey.F8,
            Input.KeyCode.F9 => ImGuiKey.F9,
            Input.KeyCode.F10 => ImGuiKey.F10,
            Input.KeyCode.F11 => ImGuiKey.F11,
            Input.KeyCode.F12 => ImGuiKey.F12,
            _ => ImGuiKey.None
        };

        return imguiKey != ImGuiKey.None;
    }

    // ============================================================
    // Rendering
    // ============================================================

    [StructLayout(LayoutKind.Sequential)]
    private struct ImGuiVertex
    {
        public Vector2 pos;
        public Vector2 uv;
        public Color color;
    }

    private void RenderImDrawData(ImDrawDataPtr drawData, ICommandList cl, IFrameBuffer fb)
    {
        var io = ImGuiNET.ImGui.GetIO();

        // HiDPI / Retina fix: make ImGui aware of framebuffer scaling.
        {
            float winW = MathF.Max(1.0f, m_windowFactory.mainWindow.width);
            float winH = MathF.Max(1.0f, m_windowFactory.mainWindow.height);

            // DisplaySize is in "logical" units (window coords)
            io.DisplaySize = new Vector2(winW, winH);

            // FramebufferScale is "framebuffer pixels per logical pixel"
            io.DisplayFramebufferScale = new Vector2(fb.width / winW, fb.height / winH);
        }
        
        if (m_pipeline == null || m_vertexBuffer == null || m_indexBuffer == null || m_projBuffer == null || m_set0 == null || m_fontTextureSet == null)
            return;

        if (drawData.CmdListsCount == 0)
            return;

        float L = drawData.DisplayPos.X;
        float R = drawData.DisplayPos.X + drawData.DisplaySize.X;
        float T = drawData.DisplayPos.Y;
        float B = drawData.DisplayPos.Y + drawData.DisplaySize.Y;

        var proj = new Matrix(
            2.0f / (R - L), 0.0f, 0.0f, 0.0f,
            0.0f, 2.0f / (T - B), 0.0f, 0.0f,
            0.0f, 0.0f, -1.0f, 0.0f,
            (R + L) / (L - R), (T + B) / (B - T), 0.0f, 1.0f);
        m_projBuffer.Set(ref proj);

        // Upload vertices/indices into managed arrays and send to GPU
        int totalVtx = drawData.TotalVtxCount;
        int totalIdx = drawData.TotalIdxCount;

        var vtx = new ImGuiVertex[totalVtx];
        var idx = new uint[totalIdx];

        int vtxOffset = 0;
        int idxOffset = 0;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];

            unsafe
            {
                for (int i = 0; i < cmdList.VtxBuffer.Size; i++)
                {
                    ImDrawVertPtr dv = cmdList.VtxBuffer[i];
                    uint c = dv.col;
                    byte r = (byte)(c & 0xFF);
                    byte g = (byte)((c >> 8) & 0xFF);
                    byte b = (byte)((c >> 16) & 0xFF);
                    byte a = (byte)((c >> 24) & 0xFF);

                    vtx[vtxOffset + i] = new ImGuiVertex
                    {
                        pos = dv.pos,
                        uv = dv.uv,
                        color = Color.FromBytes(r, g, b, a)
                    };
                }

                // ImGui index buffer is ushort by default in ImGui.NET.
                // Expand to uint to match platform index format (VeldridCommandList uses UInt32).
                for (int i = 0; i < cmdList.IdxBuffer.Size; i++)
                {
                    idx[idxOffset + i] = cmdList.IdxBuffer[i];
                }
            }

            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }

        m_vertexBuffer.Set(vtx);
        m_indexBuffer.Set(idx);

        // Set global state
        cl.SetFrameBuffer(fb);
        cl.SetPipelineState(m_pipeline);
        cl.SetVertexBuffer(m_vertexBuffer);
        cl.SetIndexBuffer(m_indexBuffer);
        cl.SetResourceSet(0, m_set0);

        // Viewport
        cl.SetViewPort(0, new Rect(0, 0, fb.width, fb.height));

        // Draw
        int globalVtxOffset = 0;
        int globalIdxOffset = 0;

        Vector2 clipOff = drawData.DisplayPos;
        Vector2 clipScale = drawData.FramebufferScale;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];

            for (int cmdi = 0; cmdi < cmdList.CmdBuffer.Size; cmdi++)
            {
                var pcmd = cmdList.CmdBuffer[cmdi];

                if (pcmd.UserCallback != IntPtr.Zero)
                {
                    // User callbacks are not supported in this backend.
                    continue;
                }

                // Bind texture (set = 1)
                IResourceSet set1 = ResolveTextureSet(pcmd.TextureId);
                cl.SetResourceSet(1, set1);

                // Project scissor rect into framebuffer space
                var cr = pcmd.ClipRect;
                float clipX1 = (cr.X - clipOff.x) * clipScale.x;
                float clipY1 = (cr.Y - clipOff.y) * clipScale.y;
                float clipX2 = (cr.Z - clipOff.x) * clipScale.x;
                float clipY2 = (cr.W - clipOff.y) * clipScale.y;

                if (clipX2 <= clipX1 || clipY2 <= clipY1)
                    continue;

                // Clamp
                int scX = (int)MathF.Max(0, clipX1);
                int scY = (int)MathF.Max(0, clipY1);
                int scW = (int)MathF.Min(fb.width - scX, clipX2 - clipX1);
                int scH = (int)MathF.Min(fb.height - scY, clipY2 - clipY1);
                if (scW <= 0 || scH <= 0) continue;

                cl.SetScissorRect(0, new Rect(scX, scY, scW, scH));

                cl.DrawIndexed(
                    (uint)pcmd.ElemCount,
                    (uint)(pcmd.IdxOffset + globalIdxOffset),
                    (int)(pcmd.VtxOffset + globalVtxOffset));
            }

            globalIdxOffset += cmdList.IdxBuffer.Size;
            globalVtxOffset += cmdList.VtxBuffer.Size;
        }
    }

    private IResourceSet ResolveTextureSet(IntPtr textureId)
    {
        if (textureId == IntPtr.Zero || textureId == m_fontAtlasId)
            return m_fontTextureSet!;

        if (m_bindingById.TryGetValue(textureId, out var entry))
            return entry.set1;

        // Fallback: font texture
        return m_fontTextureSet!;
    }

    // ============================================================
    // Embedded resources: shaders / fonts
    // ============================================================

    private byte[] LoadEmbeddedShaderCode(
        string name,
        ShaderStage stage,
        ImGuiColorSpaceHandling colorSpaceHandling)
    {
        return GetEmbeddedResourceBytes($"{name}{(stage == ShaderStage.Vertex && colorSpaceHandling == ImGuiColorSpaceHandling.Legacy ? "-legacy" : "")}.spv");
    }

    private byte[] GetEmbeddedResourceBytes(string shortName)
    {
        var resources = m_assembly.GetManifestResourceNames();
        var match = resources.FirstOrDefault(r => r.EndsWith(shortName, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            throw new FileNotFoundException($"Embedded resource '{shortName}' not found. Ensure it exists under ImGui/Assets and is marked as EmbeddedResource.");

        using Stream s = m_assembly.GetManifestResourceStream(match)!;
        byte[] ret = new byte[s.Length];
        s.ReadExactly(ret, 0, (int)s.Length);
        return ret;
    }

    public void Dispose()
    {
        // Texture bindings
        foreach (var kv in m_bindingById.Values)
            kv.set1.Dispose();
        m_bindingById.Clear();
        m_idsByTexture.Clear();

        m_fontTextureSet?.Dispose();
        m_fontTexture?.Dispose();
        m_set0?.Dispose();

        m_pipeline?.Dispose();
        m_vertexShader?.Dispose();
        m_fragmentShader?.Dispose();

        m_fontSampler?.Dispose();
        m_projBuffer?.Dispose();
        m_vertexBuffer?.Dispose();
        m_indexBuffer?.Dispose();

        unsafe
        {
            ImGuiNative.ImFontConfig_destroy(m_baseCfg);
            ImGuiNative.ImFontConfig_destroy(m_mergeCfg);
        }
    }
}
