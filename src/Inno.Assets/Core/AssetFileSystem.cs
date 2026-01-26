using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Inno.Assets.Core;

public enum AssetDirectoryChangeKind
{
    Created,
    Changed,
    Deleted,
    Renamed
}

public readonly struct AssetDirectoryChange
{
    public readonly AssetDirectoryChangeKind kind;
    public readonly string fullPath;
    public readonly string relativePath;
    public readonly string? oldFullPath;
    public readonly string? oldRelativePath;

    public AssetDirectoryChange(
        AssetDirectoryChangeKind kind,
        string fullPath,
        string relativePath,
        string? oldFullPath = null,
        string? oldRelativePath = null)
    {
        this.kind = kind;
        this.fullPath = fullPath;
        this.relativePath = relativePath;
        this.oldFullPath = oldFullPath;
        this.oldRelativePath = oldRelativePath;
    }
}

public delegate void AssetDirectoryChangedHandler(in AssetDirectoryChange change);
public delegate void AssetDirectoryChangesFlushedHandler(IReadOnlyList<AssetDirectoryChange> changes);

/// <summary>
/// Centralized file-system observation for Asset directory.
/// Owns FileSystemWatcher, coalescing/batching and versioning.
/// Also provides asset-aware file operations (create/rename/delete).
/// </summary>
internal sealed class AssetFileSystem : IDisposable
{
    #region Events

    public event AssetDirectoryChangedHandler? AssetDirectoryChanged;
    public event AssetDirectoryChangesFlushedHandler? AssetDirectoryChangesFlushed;

    #endregion

    #region Roots

    public string rootDirectory { get; }
    public string? binRootDirectory { get; }

    #endregion

    #region Watcher

    private readonly FileSystemWatcher m_watcher;

    private int m_changeVersion;
    public int changeVersion => Volatile.Read(ref m_changeVersion);

    private readonly int m_flushDelayMs;

    private readonly object m_changeSync = new();
    private readonly Dictionary<string, AssetDirectoryChange> m_pending = new(StringComparer.OrdinalIgnoreCase);
    private Timer? m_flushTimer;
    private bool m_flushScheduled;

    public AssetFileSystem(
        string assetDirectory,
        string binDirectory,
        int flushDelayMs = 120,
        int internalBufferSizeBytes = 64 * 1024)
    {
        if (string.IsNullOrWhiteSpace(assetDirectory)) throw new ArgumentException("assetDirectory is null/empty.");
        if (!Directory.Exists(assetDirectory)) throw new DirectoryNotFoundException(assetDirectory);

        rootDirectory = assetDirectory;
        binRootDirectory = binDirectory;

        m_flushDelayMs = Math.Max(1, flushDelayMs);

        m_watcher = new FileSystemWatcher(assetDirectory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = Math.Clamp(internalBufferSizeBytes, 4 * 1024, 256 * 1024)
        };

        m_watcher.Created += OnCreated;
        m_watcher.Changed += OnChanged;
        m_watcher.Deleted += OnDeleted;
        m_watcher.Renamed += OnRenamed;

        m_watcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        m_watcher.EnableRaisingEvents = false;

        m_watcher.Created -= OnCreated;
        m_watcher.Changed -= OnChanged;
        m_watcher.Deleted -= OnDeleted;
        m_watcher.Renamed -= OnRenamed;

        m_watcher.Dispose();

        lock (m_changeSync)
        {
            m_flushTimer?.Dispose();
            m_flushTimer = null;
            m_pending.Clear();
            m_flushScheduled = false;
        }
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
        => Enqueue(new AssetDirectoryChange(
            AssetDirectoryChangeKind.Created,
            e.FullPath,
            Path.GetRelativePath(rootDirectory, e.FullPath)));

    private void OnChanged(object sender, FileSystemEventArgs e)
        => Enqueue(new AssetDirectoryChange(
            AssetDirectoryChangeKind.Changed,
            e.FullPath,
            Path.GetRelativePath(rootDirectory, e.FullPath)));

    private void OnDeleted(object sender, FileSystemEventArgs e)
        => Enqueue(new AssetDirectoryChange(
            AssetDirectoryChangeKind.Deleted,
            e.FullPath,
            Path.GetRelativePath(rootDirectory, e.FullPath)));

    private void OnRenamed(object sender, RenamedEventArgs e)
        => Enqueue(new AssetDirectoryChange(
            AssetDirectoryChangeKind.Renamed,
            e.FullPath,
            Path.GetRelativePath(rootDirectory, e.FullPath),
            oldFullPath: e.OldFullPath,
            oldRelativePath: Path.GetRelativePath(rootDirectory, e.OldFullPath)));

    private void Enqueue(in AssetDirectoryChange change)
    {
        lock (m_changeSync)
        {
            MergePending(change);

            if (!m_flushScheduled)
            {
                m_flushScheduled = true;
                m_flushTimer ??= new Timer(_ => Flush(), null, m_flushDelayMs, Timeout.Infinite);
                m_flushTimer.Change(m_flushDelayMs, Timeout.Infinite);
            }
            else
            {
                m_flushTimer?.Change(m_flushDelayMs, Timeout.Infinite);
            }
        }
    }

    private void MergePending(in AssetDirectoryChange change)
    {
        string key = change.relativePath;

        if (change.kind == AssetDirectoryChangeKind.Renamed && !string.IsNullOrEmpty(change.oldRelativePath))
        {
            m_pending.Remove(change.oldRelativePath!);
        }

        if (!m_pending.TryGetValue(key, out var existing))
        {
            m_pending[key] = change;
            return;
        }

        if (change.kind == AssetDirectoryChangeKind.Renamed)
        {
            m_pending[key] = change;
            return;
        }

        if (change.kind == AssetDirectoryChangeKind.Deleted)
        {
            m_pending[key] = change;
            return;
        }

        if (existing.kind == AssetDirectoryChangeKind.Deleted) return;
        if (existing.kind == AssetDirectoryChangeKind.Renamed) return;
        if (existing.kind == AssetDirectoryChangeKind.Created) return;

        m_pending[key] = change;
    }

    private void Flush()
    {
        AssetDirectoryChange[] changes;

        lock (m_changeSync)
        {
            m_flushScheduled = false;

            if (m_pending.Count == 0) return;

            changes = m_pending.Values.ToArray();
            m_pending.Clear();
        }

        Interlocked.Increment(ref m_changeVersion);

        if (AssetDirectoryChanged != null)
        {
            for (int i = 0; i < changes.Length; i++)
                AssetDirectoryChanged.Invoke(changes[i]);
        }

        AssetDirectoryChangesFlushed?.Invoke(changes);
    }

    #endregion

    #region Asset-aware file operations

    public bool CreateDirectory(string relativeDirectory)
    {
        relativeDirectory = NormalizeRelativePath(relativeDirectory);

        try
        {
            Directory.CreateDirectory(AbsAsset(relativeDirectory));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool DeletePath(string relativePath)
    {
        relativePath = NormalizeRelativePath(relativePath);

        try
        {
            string abs = AbsAsset(relativePath);

            if (Directory.Exists(abs))
                return DeleteDirectory(relativePath);

            if (File.Exists(abs))
                return DeleteFile(relativePath);

            return false;
        }
        catch
        {
            return false;
        }
    }

    public bool DeleteFile(string relativeFilePath)
    {
        relativeFilePath = NormalizeRelativePath(relativeFilePath);

        try
        {
            SafeDeleteFile(AbsAsset(relativeFilePath));
            SafeDeleteFile(AbsMeta(relativeFilePath));
            SafeDeleteFile(AbsBin(relativeFilePath));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool DeleteDirectory(string relativeDirectory)
    {
        relativeDirectory = NormalizeRelativePath(relativeDirectory);

        try
        {
            string absAssetDir = AbsAsset(relativeDirectory);
            if (Directory.Exists(absAssetDir))
            {
                SafeDeleteDirectoryRecursive(absAssetDir);
                SafeDeleteDirectoryWithRetry(absAssetDir);
            }

            string absBinDir = AbsBinDir(relativeDirectory);
            if (Directory.Exists(absBinDir))
            {
                SafeDeleteDirectoryRecursive(absBinDir);
                SafeDeleteDirectoryWithRetry(absBinDir);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool RenamePath(string oldRelativePath, string newRelativePath)
    {
        oldRelativePath = NormalizeRelativePath(oldRelativePath);
        newRelativePath = NormalizeRelativePath(newRelativePath);

        try
        {
            string absOld = AbsAsset(oldRelativePath);
            string absNew = AbsAsset(newRelativePath);

            if (Directory.Exists(absOld))
            {
                SafeEnsureParentDirectory(absNew);
                if (Directory.Exists(absNew)) return false;

                Directory.Move(absOld, absNew);

                // move bin mirror folder
                string absOldBinDir = AbsBinDir(oldRelativePath);
                string absNewBinDir = AbsBinDir(newRelativePath);

                if (Directory.Exists(absOldBinDir))
                {
                    SafeEnsureParentDirectory(absNewBinDir);

                    if (Directory.Exists(absNewBinDir))
                        SafeDeleteDirectoryRecursive(absNewBinDir);

                    Directory.Move(absOldBinDir, absNewBinDir);
                }

                // rewrite all meta sourcePath prefix under moved folder
                UpdateMetaSourcePathPrefix(absNew, oldRelativePath, newRelativePath);
                return true;
            }

            if (File.Exists(absOld))
            {
                SafeEnsureParentDirectory(absNew);
                if (File.Exists(absNew)) return false;

                File.Move(absOld, absNew);

                // move meta
                string absOldMeta = AbsMeta(oldRelativePath);
                string absNewMeta = AbsMeta(newRelativePath);
                if (File.Exists(absOldMeta))
                {
                    SafeEnsureParentDirectory(absNewMeta);
                    File.Move(absOldMeta, absNewMeta);
                    UpdateMetaSourcePathExact(absNewMeta, oldRelativePath, newRelativePath);
                }

                // move bin
                string absOldBin = AbsBin(oldRelativePath);
                string absNewBin = AbsBin(newRelativePath);
                if (File.Exists(absOldBin))
                {
                    SafeEnsureParentDirectory(absNewBin);
                    File.Move(absOldBin, absNewBin);
                }

                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Meta rewrite

    private static void UpdateMetaSourcePathPrefix(string movedAbsFolder, string oldPrefix, string newPrefix)
    {
        string[] metaFiles;

        try
        {
            metaFiles = Directory.GetFiles(
                movedAbsFolder,
                "*" + AssetManager.C_ASSET_POSTFIX,
                SearchOption.AllDirectories);
        }
        catch
        {
            return;
        }

        oldPrefix = oldPrefix.TrimEnd('/', '\\');
        newPrefix = newPrefix.TrimEnd('/', '\\');

        for (int i = 0; i < metaFiles.Length; i++)
            UpdateMetaSourcePath(metaFiles[i], oldPrefix, newPrefix, isPrefix: true);
    }

    private static void UpdateMetaSourcePathExact(string metaAbsPath, string oldRel, string newRel)
        => UpdateMetaSourcePath(metaAbsPath, oldRel, newRel, isPrefix: false);

    private static void UpdateMetaSourcePath(string metaAbsPath, string oldRel, string newRel, bool isPrefix)
    {
        try
        {
            string yaml = File.ReadAllText(metaAbsPath);
            var state = AssetYamlSerializer.DeserializeStateFromYaml(yaml);

            if (!state.TryGetValue("sourcePath", out var v)) return;
            if (v is not string s) return;

            string updated;

            if (isPrefix)
            {
                if (s.Equals(oldRel, StringComparison.OrdinalIgnoreCase))
                    updated = newRel;
                else if (s.StartsWith(oldRel + "/", StringComparison.OrdinalIgnoreCase))
                    updated = newRel + s.Substring(oldRel.Length);
                else
                    return;
            }
            else
            {
                if (!s.Equals(oldRel, StringComparison.OrdinalIgnoreCase)) return;
                updated = newRel;
            }

            var dict = new Dictionary<string, object?>(state.values, StringComparer.OrdinalIgnoreCase)
            {
                ["sourcePath"] = updated
            };

            var newYaml = AssetYamlSerializer.SerializeStateToYaml(
                new Inno.Core.Serialization.SerializingState(dict));

            File.WriteAllText(metaAbsPath, newYaml);
        }
        catch
        {
            // ignored
        }
    }

    #endregion

    #region Helpers

    private string AbsAsset(string rel) => Path.Combine(rootDirectory, rel);

    private string AbsMeta(string rel) => Path.Combine(
        rootDirectory,
        rel + AssetManager.C_ASSET_POSTFIX);

    private string AbsBin(string rel)
    {
        if (binRootDirectory == null) return string.Empty;
        return Path.Combine(binRootDirectory, rel + AssetManager.C_BINARY_ASSET_POSTFIX);
    }

    private string AbsBinDir(string relDir)
    {
        if (binRootDirectory == null) return string.Empty;
        return Path.Combine(binRootDirectory, relDir);
    }

    private static void SafeEnsureParentDirectory(string absPath)
    {
        var parent = Path.GetDirectoryName(absPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);
    }

    private static void SafeDeleteFile(string absPath)
    {
        if (string.IsNullOrWhiteSpace(absPath)) return;

        try
        {
            if (File.Exists(absPath))
                File.Delete(absPath);
        }
        catch
        {
            // ignored
        }
    }

    private static void SafeDeleteDirectoryRecursive(string absDirectory)
    {
        try
        {
            if (!Directory.Exists(absDirectory)) return;

            foreach (var file in Directory.GetFiles(absDirectory, "*", SearchOption.AllDirectories))
                SafeDeleteFile(file);
        }
        catch
        {
            // ignored
        }
    }

    private static void SafeDeleteDirectoryWithRetry(string absDirectory)
    {
        const int retries = 10;
        const int sleepMs = 30;

        for (int i = 0; i < retries; i++)
        {
            try
            {
                if (!Directory.Exists(absDirectory)) return;

                Directory.Delete(absDirectory, recursive: false);
                return;
            }
            catch
            {
                Thread.Sleep(sleepMs);
            }
        }

        try
        {
            if (Directory.Exists(absDirectory))
                Directory.Delete(absDirectory, recursive: true);
        }
        catch
        {
            // ignored
        }
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        relativePath = (relativePath ?? string.Empty).Replace('\\', '/').Trim();
        relativePath = relativePath.TrimStart('/');

        while (relativePath.Contains("//", StringComparison.Ordinal))
            relativePath = relativePath.Replace("//", "/", StringComparison.Ordinal);

        relativePath = relativePath.TrimEnd('/');

        return relativePath;
    }

    #endregion
}
