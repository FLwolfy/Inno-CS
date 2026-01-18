using Inno.Core.ECS;

namespace Inno.Editor.Core;

/// <summary>
/// Owns the Editor's Play/Pause/Stop lifecycle.
///
/// Design goals:
/// - Single authoritative state machine
/// - Scene snapshot on Play, full restore on Stop
/// - No runtime logic runs when paused
/// </summary>
public static class EditorRuntimeController
{
    private static SceneSnapshot.SceneSnapshotData? m_sceneSnapshot;

    public static bool isPlaying => EditorManager.mode == EditorMode.Play;
    public static bool isPaused => EditorManager.mode == EditorMode.Pause;

    public static void Play()
    {
        if (EditorManager.mode != EditorMode.Edit) return;

        var scene = SceneManager.GetActiveScene();
        if (scene == null) return;

        // Snapshot BEFORE runtime mutates anything.
        m_sceneSnapshot = SceneSnapshot.Capture(scene);

        SceneManager.BeginRuntime();
        EditorManager.SetMode(EditorMode.Play);
    }

    public static void Pause()
    {
        if (EditorManager.mode != EditorMode.Play) return;
        EditorManager.SetMode(EditorMode.Pause);
    }

    public static void Resume()
    {
        if (EditorManager.mode != EditorMode.Pause) return;
        EditorManager.SetMode(EditorMode.Play);
    }

    public static void Stop()
    {
        if (EditorManager.mode == EditorMode.Edit) return;

        var scene = SceneManager.GetActiveScene();
        if (scene == null)
        {
            EditorManager.SetMode(EditorMode.Edit);
            SceneManager.EndRuntime();
            m_sceneSnapshot = null;
            return;
        }

        // Ensure we are back in edit-state BEFORE restoring.
        EditorManager.SetMode(EditorMode.Edit);
        SceneManager.EndRuntime();

        if (m_sceneSnapshot != null)
        {
            SceneSnapshot.Restore(scene, m_sceneSnapshot);
        }
        
        // Editor selection should not assume object identity survived.
        EditorManager.selection.Deselect();

        m_sceneSnapshot = null;
    }

    /// <summary>
    /// Called every frame by EditorLayer.
    /// </summary>
    public static void Update()
    {
        if (EditorManager.mode != EditorMode.Play) return;
        SceneManager.UpdateActiveScene();
    }
}
