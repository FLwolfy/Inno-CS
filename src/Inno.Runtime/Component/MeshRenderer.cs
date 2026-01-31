using System;
using Inno.Assets;
using Inno.Assets.AssetType;
using Inno.Assets.Core;
using Inno.Core.Math;
using Inno.Core.Serialization;
using Inno.Graphics.Decoder;
using Inno.Graphics.Resources.CpuResources;
using Inno.Core.ECS;

namespace Inno.Runtime.Component;

/// <summary>
/// MeshRenderer component draws a 3D mesh (currently unlit + solid color).
/// </summary>
public class MeshRenderer : GameBehavior
{
    public override ComponentTag orderTag => ComponentTag.Render;

    // CPU decoded mesh (runtime only)
    public Mesh? mesh { get; private set; }

    // Serialized: reference to MeshAsset
    [SerializableProperty] private AssetRef<MeshAsset>? m_meshAsset;

    /// <summary>
    /// Tint color (unlit).
    /// </summary>
    [SerializableProperty]
    public Color color { get; set; } = Color.WHITE;

    /// <summary>
    /// Assign mesh by CPU mesh (also stores asset ref if it exists in AssetManager).
    /// </summary>
    public void SetMesh(Mesh newMesh)
    {
        mesh = newMesh;

        // If this mesh was loaded from asset, keep the reference for serialization.
        // (MeshDecoder uses asset.guid -> mesh.guid, so this usually matches)
        var asset = AssetManager.Get<MeshAsset>(newMesh.guid);
        if (asset.isValid)
            m_meshAsset = asset;
    }

    /// <summary>
    /// Assign mesh by MeshAsset reference.
    /// </summary>
    public void SetMeshAsset(AssetRef<MeshAsset> meshAsset)
    {
        m_meshAsset = meshAsset;
        var asset = meshAsset.Resolve();
        mesh = asset != null ? ResourceDecoder.DecodeBinaries<Mesh, MeshAsset>(asset) : null;
    }

    [OnSerializableRestored]
    private void OnAfterRestore()
    {
        if (m_meshAsset != null)
        {
            var asset = m_meshAsset.Value.Resolve();
            if (asset != null)
                mesh = ResourceDecoder.DecodeBinaries<Mesh, MeshAsset>(asset);
        }
    }
    
    // TODO: DEBUG
    public override void OnAttach()
    {
        var asset = AssetManager.Get<MeshAsset>(new Guid("452f9ca5-7e76-46c0-b9d8-a5dee9d7dde9"));
        SetMeshAsset(asset);
    }
}