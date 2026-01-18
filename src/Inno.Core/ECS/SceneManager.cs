using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Core.ECS;

/// <summary>
/// Manages all scenes and tracks the active scene.
/// </summary>
public static class SceneManager
{
    private static readonly Dictionary<Guid, GameScene> SCENES = new();
    private static GameScene? m_activeScene;
    private static bool m_runtimeStarted = false;

    // TODO: Add scene asset loading and unloading
    // public static void loadScene(SceneAsset sceneAsset) {}
    // public static void unloadScene(Scene scene) {}

    /// <summary>
    /// Creates and registers a new scene.
    /// </summary>
    public static GameScene CreateScene()
    {
        var scene = new GameScene();
        SCENES[scene.id] = scene;
        return scene;
    }
    
    /// <summary>
    /// Creates and registers a new scene with the given name.
    /// </summary>
    public static GameScene CreateScene(string name)
    {
        if (m_runtimeStarted)
        {
            throw new InvalidOperationException("Cannot create a game scene while running.");
        }
        
        var scene = new GameScene(name);
        SCENES[scene.id] = scene;
        return scene;
    }
    
    /// <summary>
    /// Sets the active scene.
    /// </summary>
    public static void SetActiveScene(GameScene scene)
    {
        m_activeScene = scene;
    }

    /// <summary>
    /// Gets the currently active scene. Null if not set.
    /// </summary>
    public static GameScene? GetActiveScene()
    {
        return m_activeScene;
    }

    /// <summary>
    /// Gets a scene by name, or null if not found.
    /// </summary>
    public static GameScene? GetScene(string name)
    {
        return SCENES.Values.FirstOrDefault(scene => scene.name == name);
    }

    /// <summary>
    /// Gets a scene by uuid, or null if not found.
    /// </summary>
    public static GameScene? GetScene(Guid id)
    {
        return SCENES.GetValueOrDefault(id);
    }
    
    /// <summary>
    /// Gets all the scenes loaded.
    /// </summary>
    public static IReadOnlyCollection<GameScene> GetAllScenes() => SCENES.Values;

    /// <summary>
    /// Starts the runtime, initializing all scenes.
    /// </summary>
    public static void BeginRuntime()
    {
        m_runtimeStarted = true;
        foreach (var scene in SceneManager.GetAllScenes()) { scene.BeginRuntime(); }
    }

    /// <summary>
    /// Ends runtime. This does NOT destroy scenes; it only returns ECS to an editor-safe state.
    /// </summary>
    public static void EndRuntime()
    {
        m_runtimeStarted = false;
        foreach (var scene in SceneManager.GetAllScenes()) { scene.EndRuntime(); }
    }

    /// <summary>
    /// Updates the current active scene, if the runtime is started.
    /// </summary>
    public static void UpdateActiveScene()
    {
        if (!m_runtimeStarted) return; 
        m_activeScene?.Update();
    }
}