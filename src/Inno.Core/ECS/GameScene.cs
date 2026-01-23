using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Serialization;

namespace Inno.Core.ECS;

/// <summary>
/// Represents a scene that contains and manages GameObjects.
/// </summary>
public class GameScene : ISerializable
{
    public readonly Guid id = Guid.NewGuid();
    public string name = "GameScene";
    
    private bool m_isRunning;
    private bool m_isUpdating;
    
    private readonly List<GameObject> m_gameObjects = [];
    private readonly List<GameObject> m_pendingGameObjectRemoves = [];
    private readonly ComponentManager m_componentManager = new();

    internal GameCamera? mainCamera;

    internal GameScene() {}
    
    internal GameScene(string name)
    {
        this.name = name;
    }
    
    /// <summary>
    /// Gets the component manager of the current game scene.
    /// </summary>
    internal ComponentManager GetComponentManager() => m_componentManager;

    internal void EndRuntime()
    {
        m_isRunning = false;
        m_isUpdating = false;
        m_componentManager.EndRuntime();
        m_pendingGameObjectRemoves.Clear();
    }

    internal void ClearForRestore()
    {
        mainCamera = null;
        m_pendingGameObjectRemoves.Clear();
        m_componentManager.ClearAll();
        m_gameObjects.Clear();
        m_isRunning = false;
        m_isUpdating = false;
    }

    internal void RestoreFromSnapshot(SceneSnapshot.SceneSnapshotData data)
    {
        // Defensive: only restore into an edit-state scene.
        ClearForRestore();

        // First pass: create all objects (so parenting can resolve).
        var created = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        foreach (var o in data.objects)
        {
            var go = new GameObject(this, o.name);
            created[o.name] = go;
        }

        // Second pass: restore components and state.
        foreach (var o in data.objects)
        {
            if (!created.TryGetValue(o.name, out var go)) continue;

            foreach (var c in o.components)
            {
                var compType = Type.GetType(c.type);
                if (compType == null) continue;

                GameComponent? comp;
                if (compType == typeof(Transform))
                {
                    comp = go.transform;
                }
                else
                {
                    comp = go.AddComponent(compType);
                }

                if (comp == null) continue;

                if (comp is ISerializable s)
                    s.RestoreState(c.state);
            }
        }

        // Third pass: restore parenting (keep local transform).
        foreach (var o in data.objects)
        {
            if (o.parentName == null) continue;
            if (!created.TryGetValue(o.name, out var child)) continue;
            if (!created.TryGetValue(o.parentName, out var parent)) continue;
            child.transform.SetParent(parent.transform, worldTransformStays: false);
        }
    }

    /// <summary>
    /// Gets all components of a specific type in the current game scene.
    /// </summary>
    public IEnumerable<T> GetAllComponents<T>() where T : GameComponent
    {
        return m_componentManager.GetAll<T>();
    }

    /// <summary>
    /// Gets all components assignable to a specific type in the current game scene.
    /// </summary>
    public IEnumerable<T> GetAllComponentsAs<T>() where T : GameComponent
    {
        return m_componentManager.GetAllAssignableTo<T>();
    }
    
    /// <summary>
    /// Gets all components of a specific type in the current game scene.
    /// </summary>
    public IEnumerable<T> GetAllComponent<T>() where T : GameComponent
    {
        return m_componentManager.GetAll<T>();
    }
        
    /// <summary>
    /// Gets the main camera of the current game scene.
    /// </summary>
    public GameCamera? GetMainCamera() => mainCamera;

    /// <summary>
    /// Registers a GameObject to this scene.
    /// </summary>
    public void RegisterGameObject(GameObject obj)
    {
        if (m_gameObjects.Contains(obj)) { return; }
        m_gameObjects.Add(obj);
    }

    /// <summary>
    /// Unregisters a GameObject from this scene.
    /// </summary>
    public void UnregisterGameObject(GameObject obj)
    {
        var components = m_componentManager.GetAll(obj.id);
        foreach (var comp in components)
        {
            m_componentManager.Remove(obj.id, comp);
        }

        if (m_isRunning || m_isUpdating)
        {
            m_pendingGameObjectRemoves.Add(obj);
        }
        else
        {
            m_gameObjects.Remove(obj);
        }
    }

    /// <summary>
    /// Get a gameobject with its uuid.
    /// </summary>
    public GameObject? FindGameObject(Guid uid)
    {
        return m_gameObjects.Find(obj => obj.id == uid);
    }
    
    /// <summary>
    /// Get a gameobject with its name.
    /// </summary>
    public GameObject? FindGameObject(string objName)
    {
        return m_gameObjects.Find(obj => obj.name == objName);
    }

    /// <summary>
    /// Gets all GameObjects in this scene.
    /// </summary>
    public IReadOnlyList<GameObject> GetAllGameObjects()
    {
        return m_gameObjects;
    }

    /// <summary>
    /// Gets all GameObjects that have no parent in this scene.
    /// </summary>
    public IReadOnlyList<GameObject> GetAllRootGameObjects()
    {
        return m_gameObjects.Where(go => go.transform.parent == null).ToList();
    }

    /// <summary>
    /// Called when the game started.
    /// </summary>
    internal void BeginRuntime()
    {
        m_componentManager.WakeAll();
        m_componentManager.BeginRuntime();
        m_isRunning = true;
    }

    /// <summary>
    /// Update the GameScene and its gameObjects and components.
    /// </summary>
    internal void Update()
    {
        // Updates
        m_isUpdating = true;
        m_componentManager.UpdateAll();
        m_isUpdating = false;
        
        // Remove objects
        foreach (var gameObject in m_pendingGameObjectRemoves) { m_gameObjects.Remove(gameObject); }
        m_pendingGameObjectRemoves.Clear();
    }
}