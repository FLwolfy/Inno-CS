using System;
using System.Collections.Generic;

using Inno.Assets;
using Inno.Assets.AssetType;
using Inno.Core.Logging;
using Inno.Core.Math;
using Inno.Graphics.Decoder;
using Inno.Graphics.Resources;
using Inno.Platform.Graphics;

namespace Inno.Graphics;

public static class Renderer2D
{
    // Graphics Device Backend
    private static IGraphicsDevice m_graphicsDevice = null!;

    // Quad Resources
    private static GraphicsResource m_quadOpaqueResources = null!;
    private static GraphicsResource m_quadAlphaResources = null!;
    
    // Textured Quad Resource Cache (per Texture)
    private static Dictionary<Texture, GraphicsResource> m_texturedOpaqueCache = null!;
    private static Dictionary<Texture, GraphicsResource> m_texturedAlphaCache = null!;

    public static void Initialize(IGraphicsDevice graphicsDevice)
    {
        m_graphicsDevice = graphicsDevice;

        m_texturedOpaqueCache = new();
        m_texturedAlphaCache = new();
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
        
        // Opaque Resource
        m_quadOpaqueResources = new GraphicsResource(mesh, [opaqueMat]);
        m_quadOpaqueResources.RegisterPerObjectUniform("MVP", typeof(Matrix));
        m_quadOpaqueResources.RegisterPerObjectUniform("Color", typeof(Color));
        m_quadOpaqueResources.Create(m_graphicsDevice);
        
        // Alpha Resource 
        m_quadAlphaResources = new GraphicsResource(mesh, [alphaMat]);
        m_quadAlphaResources.RegisterPerObjectUniform("MVP", typeof(Matrix));
        m_quadAlphaResources.RegisterPerObjectUniform("Color", typeof(Color));
        m_quadAlphaResources.Create(m_graphicsDevice);
    }
    
    private static GraphicsResource GetOrCreateTexturedQuadResource(Texture texture, bool opaque)
    {
        var cache = opaque ? m_texturedOpaqueCache : m_texturedAlphaCache;
        if (cache.TryGetValue(texture, out var res))
            return res;

        // Mesh (Position + UV)
        var mesh = new Mesh("TexturedQuad");
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

        // UV (location = 1)
        mesh.SetAttribute("TexCoord0", new Vector2[]
        {
            new(0f, 0f),
            new(1f, 0f),
            new(0f, 1f),
            new(1f, 1f)
        });

        mesh.SetIndices([
            0, 1, 2,
            2, 1, 3
        ]);

        // Material
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

        // Bind Texture into material
        mat.SetTexture("MainTex", texture);

        res = new GraphicsResource(mesh, [mat]);
        res.RegisterPerObjectUniform("MVP", typeof(Matrix));
        res.RegisterPerObjectUniform("Color", typeof(Color));
        res.RegisterPerObjectUniform("UVRect", typeof(Vector4));
        res.Create(m_graphicsDevice);

        cache[texture] = res;
        return res;
    }
    
    public static void DrawQuad(RenderContext ctx, Matrix transform, Color color)
    {
        var mvp = transform * ctx.viewProjection;

        if (MathHelper.AlmostEquals(color.a, 1.0f))
        {
            m_quadOpaqueResources.UpdatePerObjectUniform(ctx.commandList, "MVP", mvp);
            m_quadOpaqueResources.UpdatePerObjectUniform(ctx.commandList, "Color", color);
            m_quadOpaqueResources.ApplyAll(ctx.commandList);
        }
        else
        {
            m_quadAlphaResources.UpdatePerObjectUniform(ctx.commandList, "MVP", mvp);
            m_quadAlphaResources.UpdatePerObjectUniform(ctx.commandList, "Color", color);
            m_quadAlphaResources.ApplyAll(ctx.commandList);
        }
    }
    
    public static void DrawTexturedQuad(RenderContext ctx, Matrix transform, Texture? texture, Vector4 uv, Color color)
    {
        // Solid-color fallback
        if (texture == null)
        {
            DrawQuad(ctx, transform, color);
            return;
        }

        // TODO: make texture has "alpha" check
        bool opaque = false;
        var res = GetOrCreateTexturedQuadResource(texture, opaque);

        var mvp = transform * ctx.viewProjection;

        res.UpdatePerObjectUniform(ctx.commandList, "MVP", mvp);
        res.UpdatePerObjectUniform(ctx.commandList, "Color", color);
        res.UpdatePerObjectUniform(ctx.commandList, "UVRect", uv);

        res.ApplyAll(ctx.commandList);
    }

    
    public static void FillColor(RenderContext ctx, Color color)
    {
        var mvp = Matrix.identity;
        
        m_quadAlphaResources.UpdatePerObjectUniform(ctx.commandList, "MVP", mvp);
        m_quadAlphaResources.UpdatePerObjectUniform(ctx.commandList, "Color", color);
        m_quadAlphaResources.ApplyAll(ctx.commandList);
    }


    public static void CleanResources()
    {
        // Solid Quad
        m_quadOpaqueResources.Dispose();
        m_quadAlphaResources.Dispose();
        
        // Textured Quad
        foreach (var kv in m_texturedOpaqueCache) kv.Value.Dispose();
        foreach (var kv in m_texturedAlphaCache) kv.Value.Dispose();
        m_texturedOpaqueCache.Clear();
        m_texturedAlphaCache.Clear();
    }
}
