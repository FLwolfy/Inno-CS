using System;
using System.Collections.Generic;

using Inno.Assets;
using Inno.Assets.AssetType;
using Inno.Core.Math;
using Inno.Graphics.Decoder;
using Inno.Graphics.Resources.CpuResources;
using Inno.Graphics.Resources.GpuResources.Bindings;
using Inno.Graphics.Resources.GpuResources.Compilers;
using Inno.Platform.Graphics;

namespace Inno.Graphics;

public static class Renderer2D
{
    // Graphics Device Backend
    private static IGraphicsDevice m_graphicsDevice = null!;

    // Quad Resources
    private static RenderableGpuBinding m_quadOpaque = null!;
    private static RenderableGpuBinding m_quadAlpha = null!;

    // Textured Resources
    private static readonly Dictionary<Guid, RenderableGpuBinding> m_texturedOpaqueCache = new();
    private static readonly Dictionary<Guid, RenderableGpuBinding> m_texturedAlphaCache = new();


    public static void Initialize(IGraphicsDevice graphicsDevice)
    {
        m_graphicsDevice = graphicsDevice;
    }

    public static void LoadResources()
    {
        LoadEmbeddedResources();
        CreateSolidQuadResources();
    }

    private static void LoadEmbeddedResources()
    {
        AssetManager.LoadEmbedded<ShaderAsset>("SolidQuad.vert");
        AssetManager.LoadEmbedded<ShaderAsset>("SolidQuad.frag");
        AssetManager.LoadEmbedded<ShaderAsset>("TexturedQuad.vert");
        AssetManager.LoadEmbedded<ShaderAsset>("TexturedQuad.frag");
    }

    private static void CreateSolidQuadResources()
    {
        // Mesh
        var mesh = new Mesh("Quad");
        mesh.renderState = new MeshRenderState
        {
            topology = PrimitiveTopology.TriangleList
        };
        mesh.SetAttribute("Position", new Vector3[]
        {
            new(-1.0f,  1.0f, 0f),
            new( 1.0f,  1.0f, 0f),
            new(-1.0f, -1.0f, 0f),
            new( 1.0f, -1.0f, 0f)
        });
        mesh.SetIndices([
            0, 1, 2,
            2, 1, 3
        ]);
        
        // Opaque Material
        var opaqueMat = new Material("QuadOpaque");
        opaqueMat.renderState = new MaterialRenderState
        {
            blendMode = BlendMode.Opaque,
            depthStencilState = DepthStencilState.DepthOnlyLessEqual
        };
        opaqueMat.shaders = new ShaderProgram();
        opaqueMat.shaders.Add(ResourceDecoder.DecodeBinaries<Shader, ShaderAsset>(
            AssetManager.GetEmbedded<ShaderAsset>("SolidQuad.vert").Resolve()!
        ));
        opaqueMat.shaders.Add(ResourceDecoder.DecodeBinaries<Shader, ShaderAsset>(
            AssetManager.GetEmbedded<ShaderAsset>("SolidQuad.frag").Resolve()!
        ));
        
        // Alpha Material
        var alphaMat = new Material("QuadAlpha");
        alphaMat.renderState = new MaterialRenderState
        {
            blendMode = BlendMode.AlphaBlend,
            depthStencilState = DepthStencilState.DepthReadOnlyLessEqual
        };
        alphaMat.shaders = new ShaderProgram();
        alphaMat.shaders.Add(ResourceDecoder.DecodeBinaries<Shader, ShaderAsset>(
            AssetManager.GetEmbedded<ShaderAsset>("SolidQuad.vert").Resolve()!
        ));
        alphaMat.shaders.Add(ResourceDecoder.DecodeBinaries<Shader, ShaderAsset>(
            AssetManager.GetEmbedded<ShaderAsset>("SolidQuad.frag").Resolve()!
        ));
        
        // Opaque Renderable
        m_quadOpaque = RenderableGpuCompiler.Compile(
            m_graphicsDevice,
            mesh,
            new[] { opaqueMat },
            new List<(string name, Type type)>
            {
                ("MVP", typeof(Matrix)),
                ("Color", typeof(Color))
            }
        );

        // Alpha Renderable
        m_quadAlpha = RenderableGpuCompiler.Compile(
            m_graphicsDevice,
            mesh,
            new[] { alphaMat },
            new List<(string name, Type type)>
            {
                ("MVP", typeof(Matrix)),
                ("Color", typeof(Color))
            }
        );
    }
    
    private static RenderableGpuBinding GetOrCreateTexturedQuadResource(Texture texture, bool opaque)
    {
        var cache = opaque ? m_texturedOpaqueCache : m_texturedAlphaCache;
        if (cache.TryGetValue(texture.guid, out var res))
            return res;

        // Mesh (Position + UV) - 這個 mesh 每次 new 也能跑，但你之後應該抽成 shared CPU mesh
        var mesh = new Mesh("TexturedQuad");
        mesh.renderState = new MeshRenderState { topology = PrimitiveTopology.TriangleList };

        mesh.SetAttribute("Position", new Vector3[]
        {
            new(-1.0f,  1.0f, 0f),
            new( 1.0f,  1.0f, 0f),
            new(-1.0f, -1.0f, 0f),
            new( 1.0f, -1.0f, 0f)
        });

        mesh.SetAttribute("TexCoord0", new Vector2[]
        {
            new(0f, 0f),
            new(1f, 0f),
            new(0f, 1f),
            new(1f, 1f)
        });

        mesh.SetIndices([0, 1, 2, 2, 1, 3]);

        var mat = new Material(opaque ? "TexturedQuadOpaque" : "TexturedQuadAlpha");
        mat.renderState = new MaterialRenderState
        {
            blendMode = opaque ? BlendMode.Opaque : BlendMode.AlphaBlend,
            depthStencilState = opaque ? DepthStencilState.DepthOnlyLessEqual : DepthStencilState.DepthReadOnlyLessEqual
        };

        mat.shaders = new ShaderProgram();
        mat.shaders.Add(ResourceDecoder.DecodeBinaries<Shader, ShaderAsset>(
            AssetManager.GetEmbedded<ShaderAsset>("TexturedQuad.vert").Resolve()!
        ));
        mat.shaders.Add(ResourceDecoder.DecodeBinaries<Shader, ShaderAsset>(
            AssetManager.GetEmbedded<ShaderAsset>("TexturedQuad.frag").Resolve()!
        ));

        mat.SetTexture("MainTex", texture);

        res = RenderableGpuCompiler.Compile(
            m_graphicsDevice,
            mesh,
            new[] { mat },
            new List<(string name, Type type)>
            {
                ("MVP", typeof(Matrix)),
                ("Color", typeof(Color)),
                ("UVRect", typeof(Vector4))
            }
        );

        cache[texture.guid] = res;
        return res;
    }
    
    public static void DrawQuad(RenderContext ctx, Matrix transform, Color color)
    {
        var mvp = transform * ctx.viewProjection;

        if (MathHelper.AlmostEquals(color.a, 1.0f))
        {
            m_quadOpaque.UpdatePerObject(ctx.commandList, "MVP", mvp);
            m_quadOpaque.UpdatePerObject(ctx.commandList, "Color", color);
            m_quadOpaque.DrawAll(ctx.commandList);
        }
        else
        {
            m_quadAlpha.UpdatePerObject(ctx.commandList, "MVP", mvp);
            m_quadAlpha.UpdatePerObject(ctx.commandList, "Color", color);
            m_quadAlpha.DrawAll(ctx.commandList);
        }
    }
    
    public static void DrawTexturedQuad(RenderContext ctx, Matrix transform, Texture? texture, Vector4 uv, Color color)
    {
        if (texture == null)
        {
            DrawQuad(ctx, transform, color);
            return;
        }

        bool opaque = false; // 你之後可以根據資產 alpha 信息判斷
        var res = GetOrCreateTexturedQuadResource(texture, opaque);

        var mvp = transform * ctx.viewProjection;

        res.UpdatePerObject(ctx.commandList, "MVP", mvp);
        res.UpdatePerObject(ctx.commandList, "Color", color);
        res.UpdatePerObject(ctx.commandList, "UVRect", uv);

        res.DrawAll(ctx.commandList);
    }
    
    public static void FillColor(RenderContext ctx, Color color)
    {
        var mvp = Matrix.identity;
        
        m_quadAlpha.UpdatePerObject(ctx.commandList, "MVP", mvp);
        m_quadAlpha.UpdatePerObject(ctx.commandList, "Color", color);
        m_quadAlpha.DrawAll(ctx.commandList);
    }


    public static void CleanResources()
    {
        m_quadOpaque.Dispose();
        m_quadAlpha.Dispose();

        foreach (var kv in m_texturedOpaqueCache) kv.Value.Dispose();
        foreach (var kv in m_texturedAlphaCache) kv.Value.Dispose();

        m_texturedOpaqueCache.Clear();
        m_texturedAlphaCache.Clear();
    }
}
