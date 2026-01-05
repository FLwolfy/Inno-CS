using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Inno.Assets.AssetType;
using Inno.Assets.Loader;

namespace Inno.Assets;

public static class AssetManager
{
    private static readonly object SYNC = new();

    private static readonly Dictionary<string, Guid> PATH_GUID_PAIRS = new();   // abs file path -> guid
    private static readonly Dictionary<Guid, InnoAsset> LOADED_ASSETS = new();  // guid -> asset

    // Caches (no duplicate loads)
    private static readonly Dictionary<string, InnoAsset> LOAD_PATH_ASSET = new();      // abs file path -> asset
    private static readonly Dictionary<string, InnoAsset> EMBEDDED_KEY_ASSET = new();  // asm|manifest -> asset

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

    public static void LoadAllAssets()
    {
        if (string.IsNullOrEmpty(assetDirectory) || !Directory.Exists(assetDirectory)) return;

        foreach (var file in Directory.GetFiles(assetDirectory, "*.*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(C_ASSET_POSTFIX, StringComparison.OrdinalIgnoreCase)) continue;

            var abs = Path.GetFullPath(file);
            lock (SYNC)
                if (LOAD_PATH_ASSET.ContainsKey(abs) || PATH_GUID_PAIRS.ContainsKey(abs))
                    continue;

            if (!AssetLoaderRegistry.TryGetLoader(Path.GetExtension(file), out var loader) || loader == null) continue;

            var asset = loader.Load(Path.GetRelativePath(assetDirectory, file));
            if (asset == null) continue;

            lock (SYNC) RegisterLoaded(abs, asset);
        }
    }

    public static T? Load<T>(string relativePath) where T : InnoAsset
        => (T?)Load(typeof(T), relativePath);

    private static InnoAsset? Load(Type assetType, string relativePath)
    {
        // Normalize to an absolute source path key for caching.
        // We must compute this key from the requested relativePath first.
        // If the loader later resolves/migrates sourcePath internally, we register again under the actual source path.
        var requestedAbs = Path.GetFullPath(Path.Combine(assetDirectory, relativePath.TrimEnd('/', '\\')));

        lock (SYNC)
            if (LOAD_PATH_ASSET.TryGetValue(requestedAbs, out var cached))
                return cached;

        if (!AssetLoaderRegistry.TryGetLoader(assetType, out var loader) || loader == null) return null;

        var loaded = loader.Load(relativePath);
        if (loaded == null) return null;

        var actualAbs = Path.GetFullPath(Path.Combine(assetDirectory, loaded.sourcePath));

        lock (SYNC)
        {
            // If the key differs (moved/renamed sourcePath), ensure both map to the same instance.
            RegisterLoaded(actualAbs, loaded);
            if (!string.Equals(actualAbs, requestedAbs, StringComparison.OrdinalIgnoreCase))
                LOAD_PATH_ASSET[requestedAbs] = loaded;
        }

        return loaded;
    }

    public static T? LoadEmbedded<T>(
        string nameOrSuffix,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase,
        bool endsWithMatch = true
    ) where T : InnoAsset
        => (T?)LoadEmbedded(typeof(T), Assembly.GetCallingAssembly(), nameOrSuffix, comparison, endsWithMatch);

    private static InnoAsset? LoadEmbedded(
        Type assetType,
        Assembly assembly,
        string nameOrSuffix,
        StringComparison comparison,
        bool endsWithMatch
    )
    {
        if (string.IsNullOrWhiteSpace(nameOrSuffix))
            throw new ArgumentException("Resource name must not be null/empty.", nameof(nameOrSuffix));

        if (!AssetLoaderRegistry.TryGetLoader(assetType, out var loader) || loader == null) return null;

        var manifestName = ResolveManifestName(assembly, nameOrSuffix, comparison, endsWithMatch);
        var embeddedKey = $"{assembly.FullName}|{manifestName}";

        lock (SYNC)
            if (EMBEDDED_KEY_ASSET.TryGetValue(embeddedKey, out var cached))
                return cached;

        using var s = assembly.GetManifestResourceStream(manifestName)
                      ?? throw new FileNotFoundException($"Embedded resource stream '{manifestName}' not found in assembly '{assembly.FullName}'.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);

        var bytes = ms.ToArray();
        var asset = loader.LoadRaw(Path.GetFileName(nameOrSuffix), bytes);

        lock (SYNC)
        {
            LOADED_ASSETS[asset.guid] = asset;
            EMBEDDED_KEY_ASSET[embeddedKey] = asset;
        }

        return asset;
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
                LOAD_PATH_ASSET.Remove(abs);
                return;
            }

            LOADED_ASSETS[guid] = reloaded;
            LOAD_PATH_ASSET[abs] = reloaded;

            // If sourcePath changed during reload, refresh mapping under the new path as well.
            var actualAbs = Path.GetFullPath(Path.Combine(assetDirectory, reloaded.sourcePath));
            PATH_GUID_PAIRS[actualAbs] = guid;
            LOAD_PATH_ASSET[actualAbs] = reloaded;
        }
    }

    private static void RegisterLoaded(string absSourcePath, InnoAsset asset)
    {
        PATH_GUID_PAIRS[absSourcePath] = asset.guid;
        LOADED_ASSETS[asset.guid] = asset;
        LOAD_PATH_ASSET[absSourcePath] = asset;
    }

    private static string ResolveManifestName(Assembly asm, string nameOrSuffix, StringComparison comparison, bool endsWithMatch)
    {
        var names = asm.GetManifestResourceNames();

        if (!endsWithMatch)
        {
            var exact = names.FirstOrDefault(n => string.Equals(n, nameOrSuffix, comparison));
            if (exact == null)
                throw new FileNotFoundException($"Embedded resource '{nameOrSuffix}' not found in assembly '{asm.FullName}'.");
            return exact;
        }

        var matches = names.Where(n => n.EndsWith(nameOrSuffix, comparison)).ToArray();
        if (matches.Length == 0)
            throw new FileNotFoundException($"Embedded resource '{nameOrSuffix}' not found in assembly '{asm.FullName}'.");

        if (matches.Length == 1) return matches[0];

        throw new AmbiguousMatchException(
            $"Embedded resource suffix '{nameOrSuffix}' is ambiguous. Matches: {string.Join(", ", matches)}");
    }
}
