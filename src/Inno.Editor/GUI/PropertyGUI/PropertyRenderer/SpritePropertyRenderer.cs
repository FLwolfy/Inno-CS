using System;
using Inno.Assets;
using Inno.Assets.AssetType;
using Inno.Core.Math;
using Inno.Editor.Panel;
using Inno.Graphics.Decoder;
using Inno.Graphics.Resources.CpuResources;

namespace Inno.Editor.GUI.PropertyGUI.PropertyRenderer;

public class SpritePropertyRenderer : PropertyRenderer<Sprite>
{
    protected override void Bind(string name, Func<Sprite?> getter, Action<Sprite?> setter, bool enabled)
    {
        var sprite = getter.Invoke();
        if (sprite == null) return;

        if (EditorGUILayout.CollapsingLabel(name, true, enabled))
        {
            // Texture Source
            Guid spriteGuid = sprite.texture?.guid ?? Guid.Empty;
            string? displayName = sprite.texture?.name;
            EditorGUILayout.Indent(16);
            if (EditorGUILayout.GuidRef("source", FileBrowserPanel.ASSET_GUID_PAYLOAD_TYPE, ref spriteGuid, displayName, enabled))
            {
                var assetRef = AssetManager.Get<TextureAsset>(spriteGuid);
                if (!assetRef.isValid)
                {
                    setter(Sprite.SolidColor(Vector2.ONE));
                }
                else
                {
                    var texture = ResourceDecoder.DecodeBinaries<Texture, TextureAsset>(assetRef.Resolve()!);
                    setter(Sprite.FromTexture(texture));
                }
            }
            
            // Size
            EditorGUILayout.Indent(16);
            EditorGUILayout.Vector2Field("size", ref sprite.size);
            
            // UV
            EditorGUILayout.Indent(16);
            EditorGUILayout.Vector4Field("uv", ref sprite.uv);
        }
    }
}