using Inno.Assets;
using Inno.Assets.AssetType;
using Inno.Assets.Core;
using Inno.Core.ECS;
using Inno.Core.Layers;
using Inno.Core.Math;
using Inno.Core.Serialization;
using Inno.Core.Utility;
using Inno.Editor.Core;
using Inno.Graphics.Decoder;
using Inno.Graphics.Resources;
using Inno.Graphics.Resources.CpuResources;
using Inno.Platform;
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
        private EditorLayer m_editorLayer = null!;
        
        protected override void Setup()
        {
            m_editorLayer = new EditorLayer(GetImplementedPlatform());
        }
        protected override void RegisterLayers(LayerStack layerStack)
        {
            layerStack.PushLayer(m_editorLayer);
        }
    }

    private class TestComponent : GameBehavior
    {
        public override ComponentTag orderTag => ComponentTag.Behavior;

        [SerializableProperty] public float rotationSpeed = 100f;
        
        public override void Update()
        {
            var localRotationEuler = transform.localRotation.ToEulerAnglesXYZDegrees();
            var newRotationEuler = localRotationEuler + new Vector3(0, 0, Time.deltaTime * rotationSpeed);
            transform.localRotation = Quaternion.FromEulerAnglesXYZDegrees(newRotationEuler);
        }
    }
}

