using System;
using System.Collections.Generic;
using Inno.Core.Serialization;

namespace Inno.Core.ECS;

/// <summary>
/// In-memory snapshot for Editor Play/Stop.
///
/// Key design choice:
/// - Snapshot is NOT persisted to disk and does not participate in the YAML asset pipeline.
/// - We capture only members explicitly marked with [SerializableProperty] on types derived from Serializable.
/// - Allowed member types are enforced by Serializable (primitives/engine value-types/nested Serializable).
///
/// This keeps Play/Stop deterministic and avoids JSON ctor/cycle issues (e.g. Sprite).
/// </summary>
public static class SceneSnapshot
{
    public sealed class SceneSnapshotData
    {
        public string sceneName { get; init; } = "";
        public List<GameObjectSnapshotData> objects { get; init; } = [];
    }

    public sealed class GameObjectSnapshotData
    {
        public string name { get; init; } = "";
        public string? parentName { get; init; }
        public List<ComponentSnapshotData> components { get; init; } = [];
    }

    public sealed class ComponentSnapshotData
    {
        public string type { get; init; } = "";

        /// <summary>
        /// Component state node produced by Serializable.CaptureState().
        /// Values are primitives/value-types or nested dictionaries for Serializable.
        /// </summary>
        public Dictionary<string, object?> state { get; init; } = new(StringComparer.Ordinal);
    }

    public static SceneSnapshotData Capture(GameScene scene)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));

        return new SceneSnapshotData
        {
            sceneName = scene.name,
            objects = CaptureObjects(scene)
        };
    }

    public static void Restore(GameScene scene, SceneSnapshotData snapshot)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        scene.RestoreFromSnapshot(snapshot);
    }

    private static List<GameObjectSnapshotData> CaptureObjects(GameScene scene)
    {
        var list = new List<GameObjectSnapshotData>();
        foreach (var go in scene.GetAllGameObjects())
        {
            list.Add(new GameObjectSnapshotData
            {
                name = go.name,
                parentName = go.transform.parent?.gameObject.name,
                components = CaptureComponents(go)
            });
        }
        return list;
    }

    private static List<ComponentSnapshotData> CaptureComponents(GameObject go)
    {
        var comps = go.GetAllComponents();
        var result = new List<ComponentSnapshotData>(comps.Count);

        foreach (var comp in comps)
        {
            var compType = comp.GetType();

            if (comp is not Serializable s)
                continue;

            result.Add(new ComponentSnapshotData
            {
                type = compType.AssemblyQualifiedName ?? compType.FullName ?? compType.Name,
                state = s.CaptureState()
            });
        }

        return result;
    }
}
