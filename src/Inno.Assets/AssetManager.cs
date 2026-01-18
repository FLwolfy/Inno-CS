using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Inno.Assets.AssetType;
using Inno.Assets.Loader;
using Inno.Core.Logging;

namespace Inno.Assets;

public static class AssetManager
{
    private static readonly Lock SYNC = new();

    // relative path (under assetDirectory) -> guid
    private static readonly Dictionary<string, Guid> PATH_TO_GUID = new(StringComparer.OrdinalIgnoreCase);

    // guid -> loaded asset snapshot
    private static readonly Dictionary<Guid, InnoAsset> LOADED_ASSETS = new();

    // embedded guid -> loaded asset snapshot
    private static readonly Dictionary<Guid, InnoAsset> EMBEDDED_ASSETS = new();

    private static AssetFileSystem? m_fs;

    public static string binDirectory { get; private set; } = null!;
    public static string assetDirectory { get; private set; } = null!;

    public const string C_ASSET_POSTFIX = ".asset";
    public const string C_BINARY_ASSET_POSTFIX = ".bin";

    // ===========
    // Notifications (re-exposed from AssetFileSystem, but still "owned" by filesystem)
    // ===========

    /// <summary>
    /// Granular coalesced change (still emitted from watcher thread).
    /// Most editor code should prefer <see cref="AssetDirectoryChangesFlushed"/> or polling <see cref="assetDirectoryChangeVersion"/>.
    /// </summary>
    public static event AssetDirectoryChangedHandler? AssetDirectoryChanged;

    /// <summary>
    /// Batched/coalesced changes flush event (emitted from watcher thread).
    /// If main-thread affinity is required, poll <see cref="assetDirectoryChangeVersion"/>.
    /// </summary>
    public static event AssetDirectoryChangesFlushedHandler? AssetDirectoryChangesFlushed;

    /// <summary>
    /// Monotonically increasing version for asset-directory changes (incremented per flush).
    /// </summary>
    public static int assetDirectoryChangeVersion => m_fs?.changeVersion ?? 0;

    // ===========
    // Initialize / Shutdown
    // ===========

    public static void Initialize(string assetDir, string binDir, bool enableAutoReload = true)
    {
        if (string.IsNullOrWhiteSpace(assetDir)) throw new ArgumentException(nameof(assetDir));
        if (string.IsNullOrWhiteSpace(binDir)) throw new ArgumentException(nameof(binDir));

        assetDirectory = assetDir;
        binDirectory = binDir;

        m_fs?.Dispose();
        m_fs = new AssetFileSystem(assetDir);

        // forward events (keep external API stable & clean)
        m_fs.AssetDirectoryChanged += ForwardDirectoryChanged;
        m_fs.AssetDirectoryChangesFlushed += ForwardDirectoryChangesFlushed;

        if (enableAutoReload)
        {
            // AssetManager auto-maintains LOADED_ASSETS snapshot from disk changes.
            m_fs.AssetDirectoryChangesFlushed += OnDirectoryChangesFlushed_AutoReload;
        }
    }

    /// <summary>
    /// Shutdown and clear asset cachces.
    /// </summary>
    public static void Shutdown()
    {
        if (m_fs != null)
        {
            m_fs.AssetDirectoryChanged -= ForwardDirectoryChanged;
            m_fs.AssetDirectoryChangesFlushed -= ForwardDirectoryChangesFlushed;
            m_fs.AssetDirectoryChangesFlushed -= OnDirectoryChangesFlushed_AutoReload;

            m_fs.Dispose();
            m_fs = null;
        }

        lock (SYNC)
        {
            PATH_TO_GUID.Clear();
            LOADED_ASSETS.Clear();
            EMBEDDED_ASSETS.Clear();
        }
    }

    // ===========
    // Bulk load
    // ===========

    public static void LoadAllFromAssetDirectory()
    {
        if (string.IsNullOrEmpty(assetDirectory) || !Directory.Exists(assetDirectory)) return;

        foreach (var file in Directory.GetFiles(assetDirectory, "*.*", SearchOption.AllDirectories))
        {
            // Skip meta/binary files as you intended
            if (file.EndsWith(C_ASSET_POSTFIX, StringComparison.OrdinalIgnoreCase)) continue;

            var ext = Path.GetExtension(file);
            if (!AssetLoaderRegistry.TryGetLoader(ext, out var loader) || loader == null) continue;

            var relativePath = Path.GetRelativePath(assetDirectory, file);
            var asset = loader.Load(relativePath);
            if (asset == null) continue;

            RegisterLoaded(relativePath, asset);
        }
    }

    // ===========
    // Load from disk
    // ===========

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

    // ===========
    // Load embedded
    // ===========

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

        var embeddedGuid = GenerateGuidFromEmbeddedKey(embeddedKey);
        var bytes = ms.ToArray();

        var asset = loader.LoadRaw(Path.GetFileName(nameOrSuffix), embeddedGuid, bytes);
        RegisterEmbedded(embeddedKey, asset);
        return true;
    }

    // ===========
    // Get (AssetRef)
    // ===========

    public static Guid GetGuid(string relativePath)
    {
        lock (SYNC)
        {
            if (PATH_TO_GUID.TryGetValue(relativePath, out var guid))
            {
                return guid;
            }
        }
        
        Log.Warn($"Could not find asset guid for {relativePath}. Has the asset already loaded?");
        return Guid.Empty;
    }

    public static AssetRef<T> Get<T>(string relativePath) where T : InnoAsset
    {
        lock (SYNC)
        {
            if (PATH_TO_GUID.TryGetValue(relativePath, out var guid))
            {
                return Get<T>(guid);
            }
        }

        Log.Warn($"Could not find asset from path: {Path.GetFullPath(Path.Combine(assetDirectory, relativePath))}");
        return new AssetRef<T>(Guid.Empty, false);
    }

    public static AssetRef<T> Get<T>(Guid guid) where T : InnoAsset
    {
        lock (SYNC)
        {
            if (LOADED_ASSETS.TryGetValue(guid, out var asset))
            {
                return new AssetRef<T>(asset.guid, false);
            }
        }

        Log.Warn($"Could not find asset with guid: '{guid}'.");
        return new AssetRef<T>(Guid.Empty, false);
    }

    public static AssetRef<T> GetEmbedded<T>(
        string nameOrSuffix,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase,
        bool endsWithMatch = true) where T : InnoAsset
    {
        var asm = Assembly.GetCallingAssembly();
        var manifestName = ResolveManifestName(asm, nameOrSuffix, comparison, endsWithMatch);
        var embeddedKey = $"{asm.FullName}|{manifestName}";
        var embeddedGuid = GenerateGuidFromEmbeddedKey(embeddedKey);

        lock (SYNC)
        {
            if (EMBEDDED_ASSETS.TryGetValue(embeddedGuid, out _))
            {
                return new AssetRef<T>(embeddedGuid, true);
            }
        }

        Log.Warn($"Could not get embedded asset for {typeof(T).Name}");
        return new AssetRef<T>(Guid.Empty, true);
    }

    internal static T? ResolveAssetRef<T>(AssetRef<T> assetRef) where T : InnoAsset
    {
        if (!assetRef.isValid) return null;

        lock (SYNC)
        {
            if (assetRef.isEmbedded)
            {
                return EMBEDDED_ASSETS.TryGetValue(assetRef.guid, out var a) ? (T)a : null;
            }

            return LOADED_ASSETS.TryGetValue(assetRef.guid, out var b) ? (T)b : null;
        }
    }

    // ===========
    // Auto reload (subscribed by Initialize(enableAutoReload: true))
    // ===========
    private static void ForwardDirectoryChanged(in AssetDirectoryChange change)
        => AssetDirectoryChanged?.Invoke(change);

    private static void ForwardDirectoryChangesFlushed(IReadOnlyList<AssetDirectoryChange> changes)
        => AssetDirectoryChangesFlushed?.Invoke(changes);

    private static void OnDirectoryChangesFlushed_AutoReload(IReadOnlyList<AssetDirectoryChange> changes)
    {
        // NOTE: This is called on watcher thread. We keep locks short and do heavy IO outside locks where possible.
        // Policy:
        // - Created: if loader exists for extension -> load and register.
        // - Changed: if already loaded -> reload (preserve guid) ; if not loaded but loader exists -> load and register.
        // - Deleted: if loaded -> remove.
        // - Renamed: update mapping; if loaded -> reload under new name preserving guid.
        for (int i = 0; i < changes.Count; i++)
        {
            var c = changes[i];

            try
            {
                switch (c.kind)
                {
                    case AssetDirectoryChangeKind.Created:
                        TryLoadNewFile(c.relativePath);
                        break;

                    case AssetDirectoryChangeKind.Changed:
                        ReloadIfLoadedOrLoadNew(c.relativePath);
                        break;

                    case AssetDirectoryChangeKind.Deleted:
                        RemoveIfLoaded(c.relativePath);
                        break;

                    case AssetDirectoryChangeKind.Renamed:
                        HandleRename(c.oldRelativePath, c.relativePath);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Asset auto-reload failed for '{c.relativePath}' ({c.kind}): {ex.Message}");
            }
        }
    }

    private static void TryLoadNewFile(string relativePath)
    {
        var abs = Path.Combine(assetDirectory, relativePath);
        if (!File.Exists(abs)) return;

        if (relativePath.EndsWith(C_ASSET_POSTFIX, StringComparison.OrdinalIgnoreCase)) return;

        var ext = Path.GetExtension(relativePath);
        if (!AssetLoaderRegistry.TryGetLoader(ext, out var loader) || loader == null) return;

        var asset = loader.Load(relativePath);
        if (asset == null) return;

        RegisterLoaded(relativePath, asset);
    }

    private static void ReloadIfLoadedOrLoadNew(string relativePath)
    {
        Guid guid;
        InnoAsset? existing;

        lock (SYNC)
        {
            if (!PATH_TO_GUID.TryGetValue(relativePath, out guid))
            {
                // Not tracked yet -> treat as new.
                guid = Guid.Empty;
                existing = null;
            }
            else
            {
                if (!LOADED_ASSETS.TryGetValue(guid, out existing!))
                {
                    // Mapping exists but asset missing -> treat as new
                    existing = null;
                }
            }
        }

        if (guid == Guid.Empty || existing == null)
        {
            TryLoadNewFile(relativePath);
            return;
        }

        if (!AssetLoaderRegistry.TryGetLoader(existing.GetType(), out var loader) || loader == null) return;

        // Reload from existing.sourcePath (best effort)
        var reloaded = loader.Load(existing.sourcePath);

        lock (SYNC)
        {
            if (reloaded == null)
            {
                PATH_TO_GUID.Remove(relativePath);
                LOADED_ASSETS.Remove(guid);
                return;
            }

            // Preserve GUID on hot reload
            reloaded.guid = guid;

            // If sourcePath changed during reload, refresh mapping under the new path as well.
            PATH_TO_GUID[reloaded.sourcePath] = guid;
            LOADED_ASSETS[guid] = reloaded;
        }
    }

    private static void RemoveIfLoaded(string relativePath)
    {
        lock (SYNC)
        {
            if (!PATH_TO_GUID.TryGetValue(relativePath, out var guid)) return;
            if (!LOADED_ASSETS.TryGetValue(guid, out var existing))
            {
                existing = null!;
            }
            
            if (!AssetLoaderRegistry.TryGetLoader(existing.GetType(), out var loader) || loader == null) return;

            loader.Load(relativePath);
            PATH_TO_GUID.Remove(relativePath);
            LOADED_ASSETS.Remove(guid);
        }
    }

    private static void HandleRename(string? oldRelativePath, string newRelativePath)
    {
        if (string.IsNullOrEmpty(oldRelativePath))
        {
            // If we don't know old path, just treat as new.
            ReloadIfLoadedOrLoadNew(newRelativePath);
            return;
        }

        Guid guid;
        InnoAsset? existing;

        lock (SYNC)
        {
            if (!PATH_TO_GUID.TryGetValue(oldRelativePath, out guid))
            {
                // Not tracked -> treat as new
                guid = Guid.Empty;
                existing = null;
            }
            else
            {
                if (!LOADED_ASSETS.TryGetValue(guid, out existing!))
                {
                    existing = null;
                }

                PATH_TO_GUID.Remove(oldRelativePath);
                PATH_TO_GUID[newRelativePath] = guid;
            }
        }

        if (guid == Guid.Empty || existing == null)
        {
            ReloadIfLoadedOrLoadNew(newRelativePath);
            return;
        }

        if (!AssetLoaderRegistry.TryGetLoader(existing.GetType(), out var loader) || loader == null) return;

        // Best-effort reload under the new path, preserving the GUID.
        loader.Load(oldRelativePath);
        var reloaded = loader.Load(newRelativePath);

        lock (SYNC)
        {
            if (reloaded == null)
            {
                LOADED_ASSETS.Remove(guid);
                PATH_TO_GUID.Remove(newRelativePath);
                return;
            }

            reloaded.guid = guid;
            reloaded.sourcePath = newRelativePath;

            LOADED_ASSETS[guid] = reloaded;
            PATH_TO_GUID[newRelativePath] = guid;
        }
    }

    // ===========
    // Internal registration
    // ===========

    private static void RegisterLoaded(string relativePath, InnoAsset asset)
    {
        lock (SYNC)
        {
            PATH_TO_GUID[relativePath] = asset.guid;
            LOADED_ASSETS[asset.guid] = asset;
        }
    }

    private static void RegisterEmbedded(string embeddedKey, InnoAsset asset)
    {
        lock (SYNC)
        {
            var guid = GenerateGuidFromEmbeddedKey(embeddedKey);
            EMBEDDED_ASSETS[guid] = asset;
        }
    }

    // ===========
    // Embedded helpers
    // ===========

    private static Guid GenerateGuidFromEmbeddedKey(string embeddedKey)
    {
        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(embeddedKey);
        var hash = md5.ComputeHash(bytes);
        return new Guid(hash.AsSpan(0, 16));
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
