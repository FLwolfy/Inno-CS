using System;
using System.IO;

using Inno.Assets;
using Inno.Assets.AssetType;
using Inno.Core.ECS;
using Inno.Core.Logging;
using Inno.Core.Serialization;

namespace Inno.Editor.Core;

public static class EditorSceneAssetIO
{
    /// <summary>
    /// Relative path (to AssetManager.assetDirectory) of the currently opened scene.
    /// Null when the active scene has never been saved/opened from disk.
    /// </summary>
    public static string? currentScenePath { get; private set; }

    public static bool SaveActiveScene()
    {
        if (string.IsNullOrWhiteSpace(currentScenePath))
            return SaveActiveSceneAsDefaultFolder();

        return SaveActiveSceneAs(currentScenePath);
    }

    public static bool SaveActiveSceneAsDefaultFolder()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene == null)
        {
            Log.Warn("Save failed: no active scene.");
            return false;
        }

        string scenesDirRel = "Scenes";
        string scenesDirAbs = Path.Combine(AssetManager.assetDirectory, scenesDirRel);
        Directory.CreateDirectory(scenesDirAbs);

        string baseName = SanitizeFileName(scene.name);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "Untitled";

        string candidateRel = Path.Combine(scenesDirRel, baseName + ".scene");
        string candidateAbs = Path.Combine(AssetManager.assetDirectory, candidateRel);

        int i = 1;
        while (File.Exists(candidateAbs))
        {
            candidateRel = Path.Combine(scenesDirRel, $"{baseName}_{i}.scene");
            candidateAbs = Path.Combine(AssetManager.assetDirectory, candidateRel);
            i++;
        }

        return SaveActiveSceneAs(candidateRel.Replace('\\', '/'));
    }

    public static bool SaveActiveSceneAs(string relativePath)
    {
        var scene = SceneManager.GetActiveScene();
        if (scene == null)
        {
            Log.Warn("Save failed: no active scene.");
            return false;
        }
        
        if (EditorManager.mode != EditorMode.Edit)
        {
            Log.Warn("Save is only allowed in Edit mode.");
            return false;
        }
        
        relativePath = NormalizeRelativePath(relativePath);
        if (!relativePath.EndsWith(".scene", StringComparison.OrdinalIgnoreCase))
            relativePath += ".scene";

        var sceneState = ((ISerializable)scene).CaptureState();
        var sceneAsset = new SceneAsset(sceneState);

        var result = AssetManager.Save(relativePath, sceneAsset);

        if (result)
        {
            Log.Info($"Scene saved: {relativePath}");
            return true;
        }
        else
        {
            Log.Warn($"Scene saved: {relativePath} failed to save.");
            return false;
        }
    }

    public static bool OpenScene(string relativePath)
    {
        relativePath = NormalizeRelativePath(relativePath);
        
        // If user is playing, stop first.
        if (EditorManager.mode != EditorMode.Edit)
        {
            EditorRuntimeController.Stop();
        }
        
        if (!AssetManager.Load<SceneAsset>(relativePath))
        {
            Log.Error($"Failed to load scene asset: {relativePath}");
            return false;
        }
        
        var sceneRef = AssetManager.Get<SceneAsset>(relativePath);
        var sceneAsset = sceneRef.Resolve();
        if (sceneAsset == null)
        {
            Log.Error($"Failed to resolve scene asset: {relativePath}");
            return false;
        }
        
        var sceneState = SerializingState.Deserialize(sceneAsset.assetBinaries);
        
        var active = SceneManager.CreateScene();
        ((ISerializable)active).RestoreState(sceneState);
        SceneManager.SetActiveScene(active);
        
        EditorManager.selection.Deselect();
        
        currentScenePath = relativePath;
        Log.Info($"Scene opened: {relativePath}");
        return true;
    }

    private static string NormalizeRelativePath(string rel)
    {
        rel = rel.Replace('\\', '/').TrimStart('/');
        return rel;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name.Trim();
    }
}
