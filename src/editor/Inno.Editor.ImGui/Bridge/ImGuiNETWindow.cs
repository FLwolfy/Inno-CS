using System;
using System.Runtime.InteropServices;

using Inno.Core.Events;
using Inno.Core.Mathematics;
using Inno.Platform.Display;
using Inno.Platform.Graphics;

using ImGuiNET;

namespace Inno.Editor.ImGui.Bridge;

internal class ImGuiNETWindow : IDisposable
{
    // Handle
    private GCHandle m_gcHandle;
    
    // Window
    private readonly IDisplaySystem m_displaySystem;
    private readonly bool m_isMainWindow;
    
    // Graphics
    [StructLayout(LayoutKind.Sequential)]
    private struct ImGuiVertex
    {
        public Vector2 pos;
        public Vector2 uv;
        public Color color;
    }
    private readonly IVertexBuffer m_vertexBuffer;
    private readonly IIndexBuffer m_indexBuffer;
    
    public IWindow window { get; }

    public ImGuiNETWindow(
        IDisplaySystem displaySystem, 
        IGraphicsDevice graphicsDevice,
        ImGuiViewportPtr vp, 
        bool isMainWindow)
    {
        // Handle
        m_gcHandle = GCHandle.Alloc(this);
        vp.PlatformUserData = (IntPtr)m_gcHandle;

        m_displaySystem = displaySystem;
        m_isMainWindow = isMainWindow;

        if (isMainWindow)
        {
            window = displaySystem.mainWindow;
        }
        else
        {
            // Create window inner
            var flags = WindowFlags.Hidden | WindowFlags.AllowHighDpi;
            if ((vp.Flags & ImGuiViewportFlags.NoTaskBarIcon) != 0)
            {
                flags |= WindowFlags.SkipTaskbar;
            }
            if ((vp.Flags & ImGuiViewportFlags.NoDecoration) == 0)
            {
                flags |= WindowFlags.Decorated;
            }
            else
            {
                flags |= WindowFlags.Resizable;
            }
            if ((vp.Flags & ImGuiViewportFlags.TopMost) != 0)
            {
                flags |= WindowFlags.AlwaysOnTop;
            }
        
            var info = new WindowInfo
            {
                name = "ImGui",
                x = (int)vp.Pos.X, 
                y = (int)vp.Pos.Y,
                width = (int)vp.Size.X,
                height = (int)vp.Size.Y,
                flags = flags,
            };
            
            window = displaySystem.CreateWindow(info);
        }
        
        // Buffers
        m_vertexBuffer = graphicsDevice.CreateVertexBuffer(1024 * 1024);
        m_indexBuffer = graphicsDevice.CreateIndexBuffer(512 * 1024);
        
        // Events
        window.Resized += () => vp.PlatformRequestResize = true;
        window.Closed += () => vp.PlatformRequestClose = true;
        window.Moved += _ => vp.PlatformRequestMove = true;
    }

    public void Apply(ImDrawDataPtr drawData, ICommandList commandList)
    {
        // Framebuffer
        commandList.SetFrameBuffer(window.frameBuffer);
        
        // Viewport
        Vector2 fbScale = drawData.FramebufferScale;
        int fbW = (int)(drawData.DisplaySize.X * fbScale.x);
        int fbH = (int)(drawData.DisplaySize.Y * fbScale.y);
        if (fbW <= 0) fbW = window.frameBuffer.width;
        if (fbH <= 0) fbH = window.frameBuffer.height;
        commandList.SetViewPort(0, new Rect(0, 0, fbW, fbH));
        
        // Clear color
        var color = ImGuiNET.ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];
        commandList.ClearColor(new Color(color.X, color.Y, color.Z, color.W));
        
        // Vertex/Index buffer
        int totalVtx = drawData.TotalVtxCount;
        int totalIdx = drawData.TotalIdxCount;

        var vtx = new ImGuiVertex[totalVtx];
        var idx = new uint[totalIdx];

        int vtxOffset = 0;
        int idxOffset = 0;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];
            
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

            for (int i = 0; i < cmdList.IdxBuffer.Size; i++)
            {
                idx[idxOffset + i] = cmdList.IdxBuffer[i];
            }

            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }
        
        m_vertexBuffer.Set(vtx);
        m_indexBuffer.Set(idx);
        
        commandList.SetVertexBuffer(m_vertexBuffer);
        commandList.SetIndexBuffer(m_indexBuffer);
    }

    public void PumpEvents() => window.PumpEvents(null);
    public EventSnapshot GetPumpedEvents() => window.GetPumpedEvents();

    public void Dispose()
    {
        m_gcHandle.Free();
        
        if (!m_isMainWindow)
        {
            m_displaySystem.DestroyWindow(window);
        }
        
        m_vertexBuffer.Dispose();
        m_indexBuffer.Dispose();
    }
}