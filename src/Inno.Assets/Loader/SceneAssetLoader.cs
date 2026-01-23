// using Inno.Assets.AssetType;
// using Inno.Core.ECS;
// using Inno.Core.Serialization;
//
// namespace Inno.Assets.Loader;
//
// /// <summary>
// /// The raw source bytes and runtime binaries are identical (canonicalized by re-encoding).
// /// </summary>
// internal sealed class SceneAssetLoader : InnoAssetLoader<SceneAsset>
// {
//     public override string[] validExtensions => [".scene"];
//
//     protected override byte[] OnLoadBinaries(string assetName, byte[] rawBytes, out SceneAsset asset)
//     {
//         var snapshot = ISerializable.Deserialize<SceneSnapshot.SceneSnapshotData>(rawBytes);
//
//         // Canonicalize to ensure stable bytes across versions of writers.
//         var bin = ISerializable.Serialize(snapshot);
//
//         asset = new SceneAsset(snapshot!.sceneName, snapshot.objects?.Count ?? 0);
//         return bin;
//     }
// }