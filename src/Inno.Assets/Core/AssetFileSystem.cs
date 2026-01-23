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
/// </summary>
internal sealed class AssetFileSystem : IDisposable
{
    /// <summary>
    /// Fired for every coalesced change (still emitted from watcher thread).
    /// Kept mainly for compatibility / very granular listeners.
    /// </summary>
    public event AssetDirectoryChangedHandler? AssetDirectoryChanged;

    /// <summary>
    /// Coalesced/batched changes flush event (emitted from watcher thread).
    /// Most editor code should prefer polling <see cref="changeVersion"/> if it needs main-thread affinity.
    /// </summary>
    public event AssetDirectoryChangesFlushedHandler? AssetDirectoryChangesFlushed;

    public string rootDirectory { get; }

    private readonly FileSystemWatcher m_watcher;

    private int m_changeVersion;
    public int changeVersion => Volatile.Read(ref m_changeVersion);

    // Coalescing: avoid event storms during file copies/git checkouts.
    private readonly int m_flushDelayMs;

    private readonly object m_changeSync = new();
    private readonly Dictionary<string, AssetDirectoryChange> m_pending = new(StringComparer.OrdinalIgnoreCase);
    private Timer? m_flushTimer;
    private bool m_flushScheduled;

    public AssetFileSystem(string assetDirectory, int flushDelayMs = 120, int internalBufferSizeBytes = 64 * 1024)
    {
        if (string.IsNullOrWhiteSpace(assetDirectory)) throw new ArgumentException("assetDirectory is null/empty.");
        if (!Directory.Exists(assetDirectory)) throw new DirectoryNotFoundException(assetDirectory);

        rootDirectory = assetDirectory;
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
                // push out the flush window to coalesce bursts
                m_flushTimer?.Change(m_flushDelayMs, Timeout.Infinite);
            }
        }
    }

    private void MergePending(in AssetDirectoryChange change)
    {
        // Key by current relative path.
        string key = change.relativePath;

        // If rename, old path is no longer relevant: drop any pending change for it.
        if (change.kind == AssetDirectoryChangeKind.Renamed && !string.IsNullOrEmpty(change.oldRelativePath))
        {
            m_pending.Remove(change.oldRelativePath!);
        }

        if (!m_pending.TryGetValue(key, out var existing))
        {
            m_pending[key] = change;
            return;
        }

        // Merge policy (UI-friendly):
        // - Renamed always wins.
        // - Deleted overrides everything.
        // - Created + Changed => keep Created.
        // - Created then Deleted => treat as Deleted.
        // - Changed is lowest priority.
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
            {
                AssetDirectoryChanged.Invoke(changes[i]);
            }
        }

        AssetDirectoryChangesFlushed?.Invoke(changes);
    }
}
