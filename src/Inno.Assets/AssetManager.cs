using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

using Inno.Assets.AssetType;
using Inno.Assets.Loader;
using Inno.Core.Logging;

namespace Inno.Assets;

public static class AssetManager
{
    private static readonly Lock SYNC = new();

    private static readonly Dictionary<string, Guid> PATH_GUID_PAIRS = new();       // abs file path -> guid
    private static readonly Dictionary<Guid, InnoAsset> LOADED_ASSETS = new();      // guid -> asset
    private static readonly Dictionary<string, InnoAsset> EMBEDDED_ASSETS = new();  // asm|manifest -> asset

    private static FileSystemWatcher? m_watcher;

    public static string binDirectory { get; private set; } = null!;
    public static string assetDirectory { get; private set; } = null!;

    public const string C_ASSET_POSTFIX = ".asset";
    public const string C_BINARY_ASSET_POSTFIX = ".bin";

    public static void Initialize(string assetDir, string binDir)
    {
        assetDirectory = assetDir;
        binDirectory = binDir;

        m_watcher = new FileSystemWatcher(assetDir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
        };
        m_watcher.Changed += OnFileChanged;
        m_watcher.Deleted += OnFileChanged;
        m_watcher.EnableRaisingEvents = true;
    }

    public static void LoadAllFromAssetDirectory()
    {
        if (string.IsNullOrEmpty(assetDirectory) || !Directory.Exists(assetDirectory)) return;

        foreach (var file in Directory.GetFiles(assetDirectory, "*.*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(C_ASSET_POSTFIX, StringComparison.OrdinalIgnoreCase)) continue;
            if (!AssetLoaderRegistry.TryGetLoader(Path.GetExtension(file), out var loader) || loader == null) continue;

            var relativePath = Path.GetRelativePath(assetDirectory, file);
            var asset = loader.Load(relativePath);
            if (asset == null) continue;

            RegisterLoaded(relativePath, asset);
        }
    }

    public static bool Load<T>(string relativePath) where T : InnoAsset
        => Load(typeof(T), relativePath);

    private static bool Load(Type assetType, string relativePath)
    {
        if (!AssetLoaderRegistry.TryGetLoader(assetType, out var loader) || loader == null)
        {
            Log.Error($"Asset loader not found for {assetType.Name}");
            return false;
        }

        var loaded = loader.Load(relativePath);
        if (loaded == null)
        {
            Log.Error($"Asset load failed for {assetType.Name}");
            return false;
        }
        
        RegisterLoaded(relativePath, loaded);

        return true;
    }

    public static bool LoadEmbedded<T>(
        string nameOrSuffix,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase,
        bool endsWithMatch = true
    ) where T : InnoAsset
        => LoadEmbedded(typeof(T), Assembly.GetCallingAssembly(), nameOrSuffix, comparison, endsWithMatch);

    private static bool LoadEmbedded(
        Type assetType,
        Assembly assembly,
        string nameOrSuffix,
        StringComparison comparison,
        bool endsWithMatch)
    {
        if (string.IsNullOrWhiteSpace(nameOrSuffix))
        {
            Log.Error("Resource name must not be null/empty.", nameof(nameOrSuffix));
            return false;
        }

        if (!AssetLoaderRegistry.TryGetLoader(assetType, out var loader) || loader == null)
        {
            Log.Error("Resource loader not found.", nameof(nameOrSuffix));
            return false;
        }

        var manifestName = ResolveManifestName(assembly, nameOrSuffix, comparison, endsWithMatch);
        var embeddedKey = $"{assembly.FullName}|{manifestName}";

        using var s = assembly.GetManifestResourceStream(manifestName);
        if (s == null)
        {
            Log.Error($"Embedded resource stream '{manifestName}' not found in assembly '{assembly.FullName}'.");
            return false;
        }
        
        using var ms = new MemoryStream();
        s.CopyTo(ms);

        var bytes = ms.ToArray();
        var asset = loader.LoadRaw(Path.GetFileName(nameOrSuffix), bytes);

        RegisterEmbedded(embeddedKey, asset);
        return true;
    }

    public static AssetRef<T> Get<T>(string relativePath) where T : InnoAsset
    {
        if (PATH_GUID_PAIRS.TryGetValue(relativePath, out var guid))
        {
            return Get<T>(guid);
        }

        Log.Warn($"Could not find asset from path: {Path.GetFullPath(Path.Combine(assetDirectory, relativePath))}");
        return new AssetRef<T>(Guid.Empty, null);
    }

    public static AssetRef<T> Get<T>(Guid guid) where T : InnoAsset
    {
        if (LOADED_ASSETS.TryGetValue(guid, out var asset))
        {
            return new AssetRef<T>(asset.guid, null);
        }
        
        Log.Warn($"Could not find asset with guid: '{guid}'.");
        return new AssetRef<T>(Guid.Empty, null);
    }

    public static AssetRef<T> GetEmbedded<T>(
        string nameOrSuffix,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase,
        bool endsWithMatch = true) where T : InnoAsset
    {
        var asm = Assembly.GetCallingAssembly();
        var manifestName = ResolveManifestName(asm, nameOrSuffix, comparison, endsWithMatch);
        var embeddedKey = $"{asm.FullName}|{manifestName}";
        
        if (EMBEDDED_ASSETS.TryGetValue(embeddedKey, out _))
        {
            return new AssetRef<T>(Guid.Empty, embeddedKey);
        }
        
        Log.Warn($"Could not get embedded asset for {typeof(T).Name}");
        return new AssetRef<T>(Guid.Empty, null);
    }

    internal static T? ResolveAssetRef<T>(AssetRef<T> assetRef) where T : InnoAsset
    {
        if (!assetRef.isValid) return null;

        if (assetRef.isEmbedded)
        {
            return (T)EMBEDDED_ASSETS[assetRef.embeddedKey!];
        }
        
        return (T) LOADED_ASSETS[assetRef.guid];
    }

    private static void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var abs = Path.GetFullPath(e.FullPath);

        Guid guid;
        InnoAsset existing;

        lock (SYNC)
        {
            if (!PATH_GUID_PAIRS.TryGetValue(abs, out guid)) return;
            if (!LOADED_ASSETS.TryGetValue(guid, out existing!)) return;
        }

        if (!AssetLoaderRegistry.TryGetLoader(existing.GetType(), out var loader) || loader == null) return;

        var reloaded = loader.Load(existing.sourcePath);

        lock (SYNC)
        {
            if (reloaded == null)
            {
                PATH_GUID_PAIRS.Remove(abs);
                LOADED_ASSETS.Remove(guid);
                return;
            }

            // If sourcePath changed during reload, refresh mapping under the new path as well.
            var actualAbs = Path.GetFullPath(Path.Combine(assetDirectory, reloaded.sourcePath));
            PATH_GUID_PAIRS[actualAbs] = guid;
            LOADED_ASSETS[guid] = reloaded;
        }
    }

    private static void RegisterLoaded(string absSourcePath, InnoAsset asset)
    {
        lock (SYNC)
        {
            PATH_GUID_PAIRS[absSourcePath] = asset.guid;
            LOADED_ASSETS[asset.guid] = asset;
        }
    }

    private static void RegisterEmbedded(string embeddedKey, InnoAsset asset)
    {
        lock (SYNC)
        {
            EMBEDDED_ASSETS[embeddedKey] = asset;
        }
    }

    private static string ResolveManifestName(Assembly asm, string nameOrSuffix, StringComparison comparison, bool endsWithMatch)
    {
        var names = asm.GetManifestResourceNames();

        if (!endsWithMatch)
        {
            var exact = names.FirstOrDefault(n => string.Equals(n, nameOrSuffix, comparison));
            if (exact == null)
            {
                throw new FileNotFoundException($"Embedded resource '{nameOrSuffix}' not found in assembly '{asm.FullName}'.");
            }
            
            return exact;
        }

        var matches = names.Where(n => n.EndsWith(nameOrSuffix, comparison)).ToArray();
        if (matches.Length == 0) throw new FileNotFoundException($"Embedded resource '{nameOrSuffix}' not found in assembly '{asm.FullName}'.");
        if (matches.Length == 1) return matches[0];

        throw new AmbiguousMatchException($"Embedded resource suffix '{nameOrSuffix}' is ambiguous. Matches: {string.Join(", ", matches)}");
    }
}
