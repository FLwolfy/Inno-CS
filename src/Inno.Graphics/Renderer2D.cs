using Inno.Core.Math;
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

    public static void Initialize(IGraphicsDevice graphicsDevice)
    {
        m_graphicsDevice = graphicsDevice;
    }

    public static void LoadResources()
    {
        CreateSolidQuadResources();
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
        opaqueMat.shaders.Add(ShaderLibrary.LoadEmbeddedShader("SolidQuad.vert"));
        opaqueMat.shaders.Add(ShaderLibrary.LoadEmbeddedShader("SolidQuad.frag"));
        
        // Alpha Material
        var alphaMat = new Material("QuadAlpha");
        alphaMat.renderState = new MaterialRenderState
        {
            blendMode = BlendMode.AlphaBlend,
            depthStencilState = DepthStencilState.DepthReadOnlyLessEqual
        };
        alphaMat.shaders = new ShaderProgram();
        alphaMat.shaders.Add(ShaderLibrary.LoadEmbeddedShader("SolidQuad.vert"));
        alphaMat.shaders.Add(ShaderLibrary.LoadEmbeddedShader("SolidQuad.frag"));
        
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
    
    public static void ClearColor(RenderContext ctx, Color color)
    {
        var mvp = Matrix.identity;
        
        m_quadAlphaResources.UpdatePerObjectUniform(ctx.commandList, "MVP", mvp);
        m_quadAlphaResources.UpdatePerObjectUniform(ctx.commandList, "Color", color);
        m_quadAlphaResources.ApplyAll(ctx.commandList);
    }


    public static void CleanResources()
    {
        // Quad
        m_quadOpaqueResources.Dispose();
        m_quadAlphaResources.Dispose();
    }
}
