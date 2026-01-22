using Inno.Core.ECS;
using Inno.Core.Serialization;

namespace Inno.Assets.AssetType;

/// <summary>
/// Scene asset backed by <see cref="SceneSnapshotBinaryCodec"/> bytes.
/// </summary>
public sealed class SceneAsset : InnoAsset
{
    [SerializableProperty] public string sceneName { get; private set; }
    [SerializableProperty] public int objectCount { get; private set; }

    internal SceneAsset(string sceneName, int objectCount)
    {
        this.sceneName = sceneName;
        this.objectCount = objectCount;
    }

    /// <summary>
    /// Decodes this asset's binaries into a snapshot suitable for <see cref="SceneSnapshot.Restore"/>.
    /// </summary>
    public SceneSnapshot.SceneSnapshotData ToSnapshot()
    {
        return SceneSnapshotBinaryCodec.Deserialize(assetBinaries);
    }

    /// <summary>
    /// Encodes a snapshot into canonical bytes used for *.scene source files.
    /// </summary>
    public static byte[] Encode(SceneSnapshot.SceneSnapshotData snapshot)
    {
        return SceneSnapshotBinaryCodec.Serialize(snapshot);
    }
}