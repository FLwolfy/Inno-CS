using Inno.Assets.AssetType;
using Inno.Core.ECS;

namespace Inno.Assets.Loader;

/// <summary>
/// Imports *.scene source files into runtime binaries using <see cref="SceneSnapshotBinaryCodec"/>.
/// The raw source bytes and runtime binaries are identical (canonicalized by re-encoding).
/// </summary>
internal sealed class SceneAssetLoader : InnoAssetLoader<SceneAsset>
{
    public override string[] validExtensions => [".scene"];

    protected override byte[] OnLoadBinaries(string assetName, byte[] rawBytes, out SceneAsset asset)
    {
        var snapshot = SceneSnapshotBinaryCodec.Deserialize(rawBytes);

        // Canonicalize to ensure stable bytes across versions of writers.
        var bin = SceneSnapshotBinaryCodec.Serialize(snapshot);

        asset = new SceneAsset(snapshot.sceneName, snapshot.objects?.Count ?? 0);
        return bin;
    }
}