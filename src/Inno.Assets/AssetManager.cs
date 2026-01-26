using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

using Inno.Assets.AssetType;
using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Core.Logging;

namespace Inno.Assets;

public static class AssetManager
{
    private static readonly Lock SYNC = new();

    private static readonly Dictionary<string, Guid> PATH_TO_GUID = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Guid, InnoAsset> LOADED_ASSETS = new();
    private static readonly Dictionary<Guid, InnoAsset> EMBEDDED_ASSETS = new();

    private static AssetFileSystem? m_fs;
    private static int m_suppressAutoReload;

    public static string binDirectory { get; private set; } = null!;
    public static string assetDirectory { get; private set; } = null!;

    public const string C_ASSET_POSTFIX = ".asset";
    public const string C_BINARY_ASSET_POSTFIX = ".bin";

    #region Notifications

    /// <summary>
    /// Emits coalesced file changes (watcher thread).
    /// </summary>
    public static event AssetDirectoryChangedHandler? AssetDirectoryChanged;

    /// <summary>
    /// Emits a flushed batch of changes (watcher thread).
    /// </summary>
    public static event AssetDirectoryChangesFlushedHandler? AssetDirectoryChangesFlushed;

    /// <summary>
    /// Gets the flushed change version counter.
    /// </summary>
    public static int assetDirectoryChangeVersion => m_fs?.changeVersion ?? 0;

    #endregion

    #region Initialize / Shutdown

    /// <summary>
    /// Initializes the asset system.
    /// </summary>
    /// <param name="assetDir">Absolute asset directory path.</param>
    /// <param name="binDir">Absolute bin directory path.</param>
    /// <param name="enableAutoReload">Enables auto reload from disk change events.</param>
    public static void Initialize(string assetDir, string binDir, bool enableAutoReload = true)
    {
        if (string.IsNullOrWhiteSpace(assetDir)) throw new ArgumentException(nameof(assetDir));
        if (string.IsNullOrWhiteSpace(binDir)) throw new ArgumentException(nameof(binDir));

        assetDirectory = assetDir;
        binDirectory = binDir;

        m_fs?.Dispose();
        m_fs = new AssetFileSystem(assetDir, binDir);

        m_fs.AssetDirectoryChanged += ForwardDirectoryChanged;
        m_fs.AssetDirectoryChangesFlushed += ForwardDirectoryChangesFlushed;

        if (enableAutoReload)
            m_fs.AssetDirectoryChangesFlushed += OnDirectoryChangesFlushed_AutoReload;
    }

    /// <summary>
    /// Shuts down the asset system and clears caches.
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

    #endregion

    #region Bulk load

    /// <summary>
    /// Loads all loadable assets from <see cref="assetDirectory"/>.
    /// </summary>
    public static void LoadAllFromAssetDirectory()
    {
        if (string.IsNullOrEmpty(assetDirectory) || !Directory.Exists(assetDirectory)) return;

        foreach (var file in Directory.GetFiles(assetDirectory, "*.*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(C_ASSET_POSTFIX, StringComparison.OrdinalIgnoreCase)) continue;

            var ext = Path.GetExtension(file);
            if (!AssetLoaderRegistry.TryGetLoader(ext, out var loader) || loader == null) continue;

            var relativePath = Path.GetRelativePath(assetDirectory, file);
            var asset = loader.Load(relativePath);
            if (asset == null) continue;

            RegisterLoaded(relativePath, asset);
        }
    }

    #endregion

    #region Load from disk

    /// <summary>
    /// Loads an asset from disk.
    /// </summary>
    /// <typeparam name="T">Asset type.</typeparam>
    /// <param name="relativePath">Path relative to <see cref="assetDirectory"/>.</param>
    /// <returns>True if loaded; otherwise false.</returns>
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

    #endregion

    #region Load embedded

    /// <summary>
    /// Loads an embedded asset from the calling assembly.
    /// </summary>
    /// <typeparam name="T">Asset type.</typeparam>
    /// <param name="nameOrSuffix">Manifest name or suffix.</param>
    /// <param name="comparison">String comparison rule.</param>
    /// <param name="endsWithMatch">True to match by suffix; false for exact.</param>
    /// <returns>True if loaded; otherwise false.</returns>
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

    #endregion

    #region Save

    /// <summary>
    /// Saves an asset using its current <see cref="InnoAsset.sourcePath"/>.
    /// </summary>
    /// <param name="asset">Asset instance.</param>
    /// <returns>True if saved; otherwise false.</returns>
    public static bool Save(InnoAsset asset)
    {
        if (string.IsNullOrWhiteSpace(asset.sourcePath))
        {
            Log.Error("Asset has no sourcePath.");
            return false;
        }

        var assetType = asset.GetType();
        if (!AssetLoaderRegistry.TryGetLoader(assetType, out var loader) || loader == null)
        {
            Log.Error($"Asset loader not found for {assetType.Name}");
            return false;
        }

        loader.SaveSource(asset.sourcePath, asset);
        return true;
    }

    /// <summary>
    /// Sets the asset source path and saves it.
    /// </summary>
    /// <param name="relativePath">Path relative to <see cref="assetDirectory"/>.</param>
    /// <param name="asset">Asset instance.</param>
    /// <returns>True if saved; otherwise false.</returns>
    public static bool Save(string relativePath, InnoAsset asset)
    {
        asset.SetSourcePath(relativePath);
        return Save(asset);
    }

    #endregion

    #region Get (AssetRef)

    /// <summary>
    /// Gets a loaded asset GUID by relative path.
    /// </summary>
    /// <param name="relativePath">Path relative to <see cref="assetDirectory"/>.</param>
    /// <returns>GUID if tracked; otherwise <see cref="Guid.Empty"/>.</returns>
    public static Guid GetGuid(string relativePath)
    {
        lock (SYNC)
        {
            if (PATH_TO_GUID.TryGetValue(relativePath, out var guid))
                return guid;
        }

        Log.Warn($"Could not find asset guid for {relativePath}. Has the asset already loaded?");
        return Guid.Empty;
    }

    /// <summary>
    /// Gets an <see cref="AssetRef{T}"/> from a relative path.
    /// </summary>
    /// <typeparam name="T">Asset type.</typeparam>
    /// <param name="relativePath">Path relative to <see cref="assetDirectory"/>.</param>
    /// <returns>Asset reference.</returns>
    public static AssetRef<T> Get<T>(string relativePath) where T : InnoAsset
    {
        lock (SYNC)
        {
            if (PATH_TO_GUID.TryGetValue(relativePath, out var guid))
                return Get<T>(guid);
        }

        Log.Warn($"Could not find asset from path: {Path.GetFullPath(Path.Combine(assetDirectory, relativePath))}");
        return new AssetRef<T>(Guid.Empty, false);
    }

    /// <summary>
    /// Gets an <see cref="AssetRef{T}"/> from a GUID.
    /// </summary>
    /// <typeparam name="T">Asset type.</typeparam>
    /// <param name="guid">Asset GUID.</param>
    /// <returns>Asset reference.</returns>
    public static AssetRef<T> Get<T>(Guid guid) where T : InnoAsset
    {
        lock (SYNC)
        {
            if (LOADED_ASSETS.TryGetValue(guid, out var asset))
                return new AssetRef<T>(asset.guid, false);
        }

        Log.Warn($"Could not find asset with guid: '{guid}'.");
        return new AssetRef<T>(Guid.Empty, false);
    }

    /// <summary>
    /// Gets an embedded asset reference.
    /// </summary>
    /// <typeparam name="T">Asset type.</typeparam>
    /// <param name="nameOrSuffix">Manifest name or suffix.</param>
    /// <param name="comparison">String comparison rule.</param>
    /// <param name="endsWithMatch">True to match by suffix; false for exact.</param>
    /// <returns>Embedded asset reference.</returns>
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
                return new AssetRef<T>(embeddedGuid, true);
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
                return EMBEDDED_ASSETS.TryGetValue(assetRef.guid, out var a) ? (T)a : null;

            return LOADED_ASSETS.TryGetValue(assetRef.guid, out var b) ? (T)b : null;
        }
    }

    #endregion

    #region Auto reload

    private static void ForwardDirectoryChanged(in AssetDirectoryChange change)
        => AssetDirectoryChanged?.Invoke(change);

    private static void ForwardDirectoryChangesFlushed(IReadOnlyList<AssetDirectoryChange> changes)
        => AssetDirectoryChangesFlushed?.Invoke(changes);

    private static void OnDirectoryChangesFlushed_AutoReload(IReadOnlyList<AssetDirectoryChange> changes)
    {
        if (Volatile.Read(ref m_suppressAutoReload) > 0)
            return;

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
                guid = Guid.Empty;
                existing = null;
            }
            else
            {
                if (!LOADED_ASSETS.TryGetValue(guid, out existing!))
                    existing = null;
            }
        }

        if (guid == Guid.Empty || existing == null)
        {
            TryLoadNewFile(relativePath);
            return;
        }

        if (!AssetLoaderRegistry.TryGetLoader(existing.GetType(), out var loader) || loader == null) return;

        var reloaded = loader.Load(existing.sourcePath);

        lock (SYNC)
        {
            if (reloaded == null)
            {
                PATH_TO_GUID.Remove(relativePath);
                LOADED_ASSETS.Remove(guid);
                return;
            }

            reloaded.guid = guid;
            PATH_TO_GUID[reloaded.sourcePath] = guid;
            LOADED_ASSETS[guid] = reloaded;
        }
    }

    private static void RemoveIfLoaded(string relativePath)
    {
        lock (SYNC)
        {
            if (!PATH_TO_GUID.TryGetValue(relativePath, out var guid)) return;
            if (!LOADED_ASSETS.TryGetValue(guid, out _))
            {
                PATH_TO_GUID.Remove(relativePath);
                return;
            }

            PATH_TO_GUID.Remove(relativePath);
            LOADED_ASSETS.Remove(guid);
        }
    }

    private static void HandleRename(string? oldRelativePath, string newRelativePath)
    {
        if (string.IsNullOrEmpty(oldRelativePath))
        {
            ReloadIfLoadedOrLoadNew(newRelativePath);
            return;
        }

        Guid guid;
        InnoAsset? existing;

        lock (SYNC)
        {
            if (!PATH_TO_GUID.TryGetValue(oldRelativePath, out guid))
            {
                guid = Guid.Empty;
                existing = null;
            }
            else
            {
                if (!LOADED_ASSETS.TryGetValue(guid, out existing!))
                    existing = null;

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
            reloaded.SetSourcePath(newRelativePath);

            LOADED_ASSETS[guid] = reloaded;
            PATH_TO_GUID[newRelativePath] = guid;
        }
    }

    #endregion

    #region Internal registration

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

    #endregion

    #region Embedded helpers

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
                throw new FileNotFoundException($"Embedded resource '{nameOrSuffix}' not found in assembly '{asm.FullName}'.");

            return exact;
        }

        var matches = names.Where(n => n.EndsWith(nameOrSuffix, comparison)).ToArray();
        if (matches.Length == 0) throw new FileNotFoundException($"Embedded resource '{nameOrSuffix}' not found in assembly '{asm.FullName}'.");
        if (matches.Length == 1) return matches[0];

        throw new AmbiguousMatchException($"Embedded resource suffix '{nameOrSuffix}' is ambiguous. Matches: {string.Join(", ", matches)}");
    }

    #endregion

    #region Asset-aware file operations

    private readonly struct AutoReloadSuppressor : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref m_suppressAutoReload);
    }

    private static AutoReloadSuppressor SuppressAutoReload()
    {
        Interlocked.Increment(ref m_suppressAutoReload);
        return new AutoReloadSuppressor();
    }

    /// <summary>
    /// Creates a folder under the asset root.
    /// </summary>
    /// <param name="relativeDirectory">Path relative to <see cref="assetDirectory"/>.</param>
    /// <returns>True if created; otherwise false.</returns>
    public static bool CreateFolder(string relativeDirectory)
    {
        if (m_fs == null) return false;

        using var _ = SuppressAutoReload();
        return m_fs.CreateDirectory(relativeDirectory);
    }

    /// <summary>
    /// Deletes a file or directory under the asset root, including meta/bin.
    /// </summary>
    /// <param name="relativePath">Path relative to <see cref="assetDirectory"/>.</param>
    /// <returns>True if deleted; otherwise false.</returns>
    public static bool DeletePath(string relativePath)
    {
        if (m_fs == null) return false;

        using var _ = SuppressAutoReload();

        bool ok = m_fs.DeletePath(relativePath);
        if (!ok) return false;

        RemoveCacheByPrefix(relativePath);
        return true;
    }

    /// <summary>
    /// Renames/moves a file or directory to an exact target path.
    /// </summary>
    /// <param name="oldRelativePath">Old path relative to <see cref="assetDirectory"/>.</param>
    /// <param name="newRelativePath">New path relative to <see cref="assetDirectory"/>.</param>
    /// <returns>True if renamed; otherwise false.</returns>
    public static bool RenamePath(string oldRelativePath, string newRelativePath)
    {
        if (m_fs == null) return false;

        using var _ = SuppressAutoReload();

        bool ok = m_fs.RenamePath(oldRelativePath, newRelativePath);
        if (!ok) return false;

        UpdateCacheByRename(oldRelativePath, newRelativePath);
        return true;
    }

    /// <summary>
    /// Moves a file or directory into an existing destination directory, preserving the source name.
    /// </summary>
    /// <param name="sourceRelativePath">Source path relative to <see cref="assetDirectory"/>.</param>
    /// <param name="destinationRelativeDirectory">Destination directory relative to <see cref="assetDirectory"/>.</param>
    /// <returns>True if moved; otherwise false.</returns>
    public static bool MovePath(string sourceRelativePath, string destinationRelativeDirectory)
    {
        if (m_fs == null) return false;

        using var _ = SuppressAutoReload();

        string sourceNorm = NormalizeRel(sourceRelativePath);
        string destDirNorm = NormalizeRel(destinationRelativeDirectory);

        string name = Path.GetFileName(sourceNorm.TrimEnd('/'));
        if (string.IsNullOrWhiteSpace(name)) return false;

        string destPathNorm = NormalizeRel(Path.Combine(destDirNorm, name));

        bool ok = m_fs.MovePath(sourceNorm, destDirNorm);
        if (!ok) return false;

        UpdateCacheByRename(sourceNorm, destPathNorm);
        return true;
    }

    /// <summary>
    /// Converts an absolute/native full path under <see cref="assetDirectory"/> into a relative asset path.
    /// </summary>
    /// <param name="fullPath">Absolute/native path.</param>
    /// <returns>Relative path using '/' separators, or empty string if not under asset root.</returns>
    public static string ToRelativePathFromAssetDirectory(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return "";

        try
        {
            string abs = Path.GetFullPath(fullPath);
            string root = Path.GetFullPath(assetDirectory);

            string rel = Path.GetRelativePath(root, abs);
            if (rel.StartsWith("..", StringComparison.Ordinal)) return "";

            return rel.Replace('\\', '/');
        }
        catch
        {
            return "";
        }
    }

    private static void RemoveCacheByPrefix(string relativePath)
    {
        relativePath = NormalizeRel(relativePath);

        lock (SYNC)
        {
            if (PATH_TO_GUID.TryGetValue(relativePath, out var guid))
            {
                PATH_TO_GUID.Remove(relativePath);
                LOADED_ASSETS.Remove(guid);
            }

            string prefix = relativePath.Length == 0 ? "" : (relativePath + "/");
            if (prefix.Length == 0) return;

            var toRemove = PATH_TO_GUID.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            for (int i = 0; i < toRemove.Count; i++)
            {
                string k = toRemove[i];
                if (PATH_TO_GUID.TryGetValue(k, out var g))
                    LOADED_ASSETS.Remove(g);

                PATH_TO_GUID.Remove(k);
            }
        }
    }

    private static void UpdateCacheByRename(string oldRelativePath, string newRelativePath)
    {
        oldRelativePath = NormalizeRel(oldRelativePath);
        newRelativePath = NormalizeRel(newRelativePath);

        lock (SYNC)
        {
            if (PATH_TO_GUID.TryGetValue(oldRelativePath, out var guid))
            {
                PATH_TO_GUID.Remove(oldRelativePath);
                PATH_TO_GUID[newRelativePath] = guid;

                if (LOADED_ASSETS.TryGetValue(guid, out var a))
                    a.SetSourcePath(newRelativePath);

                return;
            }

            string oldPrefix = oldRelativePath.Length == 0 ? "" : (oldRelativePath.TrimEnd('/') + "/");
            if (oldPrefix.Length == 0) return;

            string newPrefix = newRelativePath.Length == 0 ? "" : (newRelativePath.TrimEnd('/') + "/");

            var keys = PATH_TO_GUID.Keys
                .Where(k => k.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            for (int i = 0; i < keys.Count; i++)
            {
                string oldKey = keys[i];
                string suffix = oldKey.Substring(oldPrefix.Length);
                string newKey = newPrefix + suffix;

                if (!PATH_TO_GUID.TryGetValue(oldKey, out var g)) continue;

                PATH_TO_GUID.Remove(oldKey);
                PATH_TO_GUID[newKey] = g;

                if (LOADED_ASSETS.TryGetValue(g, out var asset))
                    asset.SetSourcePath(newKey);
            }
        }
    }

    private static string NormalizeRel(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        p = p.Replace('\\', '/').Trim();
        while (p.StartsWith("/", StringComparison.Ordinal)) p = p.Substring(1);
        while (p.EndsWith("/", StringComparison.Ordinal)) p = p.Substring(0, p.Length - 1);
        return p;
    }

    #endregion
}
