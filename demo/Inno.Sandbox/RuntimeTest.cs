using Inno.Core.ECS;
using Inno.Core.Events;
using Inno.Core.Layers;
using Inno.Core.Math;
using Inno.Core.Utility;
using Inno.Runtime.Component;
using Inno.Runtime.Core;

namespace Inno.Sandbox;

public class RuntimeTest
{
    public void Run()
    {
#if DEBUG
        TestEngineCore testCore = new TestEngineCore(true);
#else
        TestEngineCore testCore = new TestEngineCore(false);
#endif
        testCore.Run();
    }
    
    private class TestEngineCore(bool debug) : EngineCore(debug)
    {
        private TestGameLayer m_gameLayer = null!;
        
        protected override void Setup()
        {
            m_gameLayer = new TestGameLayer();
            
            SetWindowSize(900, 900);
            SetWindowResizable(true);
        }
        protected override void RegisterLayers(LayerStack layerStack)
        {
            layerStack.PushLayer(m_gameLayer);
        }
    }
    
    private class TestGameLayer : GameLayer
    {
        private GameObject m_mainTestObj = null!;
        
        private bool m_shouldRotate = false;
        
        public override void OnUpdate()
        {
            if (m_shouldRotate)
            {
                m_mainTestObj.transform.localRotationZ += Time.deltaTime * 100f;
                Console.WriteLine(1 / Time.deltaTime);
            }
            
            base.OnUpdate();
        }

        public override void OnEvent(EventSnapshot snapshot)
        {
            base.OnEvent(snapshot);

            foreach (var e in snapshot.GetEvents(EventType.KeyPressed))
            {
                var keyEvent = (e as KeyPressedEvent);

                if (keyEvent!.key == Input.KeyCode.R && !keyEvent!.repeat)
                {
                    m_shouldRotate = true;
                }
            }

            foreach (var e in snapshot.GetEvents(EventType.KeyReleased))
            {
                var keyEvent = e as KeyReleasedEvent;

                if (keyEvent!.key == Input.KeyCode.R)
                {
                    m_shouldRotate = false;
                }
            }
        }

        public override void OnAttach()
        {
            // TEST SCENE SETUP
            GameScene testScene = SceneManager.CreateScene("Test Scene");
            SceneManager.SetActiveScene(testScene);
        
            // Camera Setup
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.transform.worldPosition = new Vector3(500, 200, 0);
            OrthographicCamera camera = cameraObject.AddComponent<OrthographicCamera>();
            camera.isMainCamera = true;
            camera.aspectRatio = 16f / 9f;
            camera.size = 720f;
        
            // Object 1
            m_mainTestObj = new GameObject("Test Object 1");
            m_mainTestObj.transform.worldPosition = new Vector3(0, 0, 1);
            m_mainTestObj.transform.worldScale = new Vector3(100f, 100f, 1f);
            m_mainTestObj.transform.localRotationZ = 45;
            var mainSR = m_mainTestObj.AddComponent<SpriteRenderer>();
            mainSR.layerDepth = 1;

            // TODO: DEBUG: Why Depth test not applied on Metal
            
            // Object 2
            GameObject to = new GameObject("Test Object" + 2);
            to.transform.worldPosition = new Vector3(0, 0, 0f);
            to.transform.worldScale = new Vector3(50f, 50f, 1f);
            SpriteRenderer sr = to.AddComponent<SpriteRenderer>();
            sr.color = Color.BLACK;
            
            // Scene Begin Runtime
            base.OnAttach();
        }
    }
}

