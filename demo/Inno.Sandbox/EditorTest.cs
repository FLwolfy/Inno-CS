using Inno.Assets;
using Inno.Assets.AssetType;
using Inno.Core.ECS;
using Inno.Core.Layers;
using Inno.Core.Math;
using Inno.Core.Serialization;
using Inno.Core.Utility;
using Inno.Editor.Core;
using Inno.Graphics.Decoder;
using Inno.Graphics.Resources;
using Inno.Graphics.Resources.CpuResources;
using Inno.Platform.Window;
using Inno.Runtime.Component;
using Inno.Runtime.Core;

namespace Inno.Sandbox;

public class EditorTest
{
    public void Run()
    {
        TestEngineCore testCore = new TestEngineCore();
        testCore.Run();
    }
    
    private class TestEngineCore : EngineCore
    {
        private TestEditorLayer m_editorLayer = null!;
        
        protected override void Setup()
        {
            m_editorLayer = new TestEditorLayer(GetWindowFactory());
            
            SetWindowSize(1200, 720);
            SetWindowResizable(true);
        }
        protected override void RegisterLayers(LayerStack layerStack)
        {
            layerStack.PushLayer(m_editorLayer);
        }
    }
    
    private class TestEditorLayer(IWindowFactory windowFactory) : EditorLayer(windowFactory)
    {
        public override void OnAttach()
        {
            // Editor Initialization
            base.OnAttach();
            
            // TEST SCENE SETUP
            GameScene testScene = SceneManager.CreateScene("Test Scene");
            SceneManager.SetActiveScene(testScene);
            
            // Camera Setup
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.transform.worldPosition = new Vector3(200, 0, 0);
            OrthographicCamera camera = cameraObject.AddComponent<OrthographicCamera>();
            camera.isMainCamera = true;
            camera.aspectRatio = 16f / 9f;
            camera.size = 1080f;
        
            // Object 1
            GameObject testObject = new GameObject("Test Object 1");
            testObject.transform.worldPosition = new Vector3(0, 0, 0);
            testObject.transform.worldScale = new Vector3(100f, 100f, 1f);
            testObject.transform.localRotationZ = 45;
            testObject.AddComponent<SpriteRenderer>();
            
            // Object 2 - 5
            for (int i = 2; i <= 5; i++)
            {
                GameObject to = new GameObject("Test Object" + i);
                to.transform.worldPosition = new Vector3(150 * (i - 2), 0, 0);
                to.transform.worldScale = new Vector3(50f, 50f, 1f);
                SpriteRenderer sr = to.AddComponent<SpriteRenderer>();
                sr.color = Color.BLACK;
            
                to.transform.SetParent(testObject.transform);
            }
            
            // Object 6
            GameObject testObject6 = new GameObject("Textured Object");
            testObject6.transform.worldPosition = new Vector3(0, 200, 0);
            testObject6.transform.worldScale = new Vector3(1f, 1f, 1f);
            testObject6.transform.localRotationZ = 0;
            SpriteRenderer sr6 = testObject6.AddComponent<SpriteRenderer>();
            testObject6.AddComponent<TestComponent>();

            AssetRef<TextureAsset> testTextureAsset = AssetManager.Get<TextureAsset>("TestTextures/coin.png");
            Texture testTexture = ResourceDecoder.DecodeBinaries<Texture, TextureAsset>(testTextureAsset.Resolve()!);
            sr6.sprite = Sprite.FromTexture(testTexture);
        }
    }

    private class TestComponent : GameBehavior
    {
        public override ComponentTag orderTag => ComponentTag.Behavior;

        [SerializableProperty] public float rotationSpeed = 100f;
        
        public override void Update()
        {
            transform.localRotationZ += Time.deltaTime * rotationSpeed;
        }
    }
}

