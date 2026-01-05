using System;
using System.IO;
using Inno.Assets.AssetType;
using Inno.Assets.Serializer;
using Inno.Core.Resource;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Inno.Assets.Loader;

internal interface IAssetLoader
{
    protected static readonly IDeserializer DESERIALIZER = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IncludeNonPublicProperties()
        .WithTypeInspector(_ => new AssetPropertyTypeInspector())
        .WithObjectFactory(new AssetObjectFactory())
        .IgnoreUnmatchedProperties()
        .Build();

    protected static readonly ISerializer SERIALIZER = new SerializerBuilder()
        .IncludeNonPublicProperties()
        .WithTypeInspector(_ => new AssetPropertyTypeInspector())
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();
    
    /// <summary>
    /// Load the asset from disk / raw file
    /// </summary>
    InnoAsset? Load(string path);
}

internal abstract class InnoAssetLoader<T> : IAssetLoader where T : InnoAsset
{
    public abstract string[] validExtensions { get; }
    
    public InnoAsset? Load(string relativePath)
    {
        var assetMetaPath = Path.Combine(AssetManager.assetDirectory, relativePath + AssetManager.C_ASSET_POSTFIX);
        var assetBinPath  = Path.Combine(AssetManager.binDirectory, relativePath + AssetManager.C_BINARY_ASSET_POSTFIX);

        if (!File.Exists(assetMetaPath))
        {
            var absoluteSourcePath = Path.Combine(AssetManager.assetDirectory, relativePath);
            if (!File.Exists(absoluteSourcePath)) return null;
            
            var a = OnLoad(relativePath);
            a.guid = Guid.NewGuid();
            a.sourcePath = relativePath;
            a.RecomputeHash();
            
            SaveAsset(a, relativePath, assetMetaPath, assetBinPath);
            return a;
        }

        var yaml  = File.ReadAllText(assetMetaPath);
        var asset = IAssetLoader.DESERIALIZER.Deserialize<T>(yaml);

        string actualSource = Path.Combine(AssetManager.assetDirectory, asset.sourcePath);
        if (!File.Exists(actualSource))
        {
            DeleteAsset(assetMetaPath, assetBinPath);
            return null;
        }

        string old = asset.sourceHash;
        asset.RecomputeHash();
        if (old != asset.sourceHash)
        {
            var absoluteSourcePath = Path.Combine(AssetManager.assetDirectory, relativePath);
            if (File.Exists(absoluteSourcePath))
            {
                var a = OnLoad(relativePath);
                a.guid = asset.guid;
                a.sourcePath = relativePath;
                a.RecomputeHash();
                
                SaveAsset(a, relativePath, assetMetaPath, assetBinPath);
                return a;
            }
            
            DeleteAsset(assetMetaPath, assetBinPath);
            return null;
        }

        if (!File.Exists(assetBinPath))
        {
            byte[] binaries = OnBinarize(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(assetBinPath)!);
            File.WriteAllBytes(assetBinPath, binaries);
        }
        
        byte[] data = File.ReadAllBytes(assetBinPath);
        asset.assetBinaries = new ResourceBin(relativePath, data);

        return asset;
    }

    private void SaveAsset(T asset, string relativePath, string metaPath, string binPath)
    {
        string yaml = IAssetLoader.SERIALIZER.Serialize(asset);
        Directory.CreateDirectory(Path.GetDirectoryName(metaPath)!);
        File.WriteAllText(metaPath, yaml);

        byte[] binaries = OnBinarize(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(binPath)!);
        File.WriteAllBytes(binPath, binaries);
        asset.assetBinaries = new ResourceBin(relativePath, binaries);
    }

    private void DeleteAsset(string metaPath, string binPath)
    {
        if (File.Exists(metaPath))
        {
            File.Delete(metaPath);
        }

        if (File.Exists(binPath))
        {
            File.Delete(binPath);
        }
    }

    /// <summary>
    /// Called to load the asset from disk / raw file.
    /// </summary>
    /// <param name="relativePath">the relative path to the "Assets" directory</param>
    /// <returns>the InnoAsset in the specified type T</returns>
    protected abstract T OnLoad(string relativePath);

    /// <summary>
    /// Optionally called to compile the asset into binary form.
    /// </summary>
    /// <param name="relativePath">the relative path to the "Assets" directory</param>
    /// <returns>the compiled binaries in bytes.</returns>
    protected abstract byte[] OnBinarize(string relativePath);
}