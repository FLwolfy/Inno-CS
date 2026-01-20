using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.ECS;
using Inno.Core.Events;
using Inno.Core.Math;
using Inno.Editor.Core;
using Inno.Editor.GUI;

using ImGuiNET;
using ImGuiNet = ImGuiNET.ImGui;

namespace Inno.Editor.Panel;

public class HierarchyPanel : EditorPanel
{
    public override string title => "Hierarchy";

    private const string C_GAMEOBJECT_GUID_TYPE = "GameObjectGUID";
    private readonly Queue<Action> m_pendingGuiUpdateAction = new();
    
    internal HierarchyPanel() {}

    internal override void OnGUI()
    {
        // Scrollable area for the whole hierarchy
        ImGuiNet.BeginChild(
            "##HierarchyScroll",
            new Vector2(0,0),
            ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar
        );

        // Draw Scene root
        DrawSceneObjectRoot();

        // Draw root GameObjects
        foreach (var obj in SceneManager.GetActiveScene()!.GetAllRootGameObjects())
        {
            DrawRootGameObject(obj);
        }

        // Handle Menu Events (must be inside the child, so hover checks refer to this child)
        HandleMenu();

        // Apply delayed actions
        while (m_pendingGuiUpdateAction.Count > 0)
        {
            m_pendingGuiUpdateAction.Dequeue().Invoke();
        }

        ImGuiNet.EndChild();
    }

    private void HandleMenu()
    {
        if (!ImGuiNet.IsAnyItemHovered() && ImGuiNet.IsWindowHovered() && ImGuiNet.GetIO().MouseClicked[(int)Input.MouseButton.Right])
        {
            ImGuiNet.OpenPopup("HierarchyContextMenu");
        }

        if (ImGuiNet.BeginPopup("HierarchyContextMenu"))
        {
            if (ImGuiNet.BeginMenu("Create"))
            {
                if (ImGuiNet.MenuItem("GameObject"))
                {
                    m_pendingGuiUpdateAction.Enqueue(() =>
                    {
                        var go = new GameObject("New GameObject");
                        EditorManager.selection.Select(go);
                    });
                }
                ImGuiNet.EndMenu();
            }
            ImGuiNet.EndPopup();
        }
    }

    private void DrawSceneObjectRoot()
    {
        // Draw "Scene Root" as non-selectable, non-draggable
        ImGuiNet.Text("[ Scene Root ]");
        if (ImGuiNet.BeginDragDropTarget())
        {
            var payload = EditorImGuiEx.AcceptDragPayload<Guid>(C_GAMEOBJECT_GUID_TYPE);
            if (payload != null)
            {
                var obj = SceneManager.GetActiveScene()!.FindGameObject(payload.Value);
                m_pendingGuiUpdateAction.Enqueue(() => obj?.transform.SetParent(null));
            }
            ImGuiNet.EndDragDropTarget();
        }
    }

    private void DrawRootGameObject(GameObject obj)
    {
        var selection = EditorManager.selection;
        bool isSelected = selection.IsSelected(obj);
        bool hasChildren = obj.transform.children.Count > 0;

        // TreeNodeFlags with Selected flag
        var flags = hasChildren ? ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick : ImGuiTreeNodeFlags.Leaf;
        flags |= ImGuiTreeNodeFlags.SpanFullWidth;
        if (isSelected)
        {
            flags |= ImGuiTreeNodeFlags.Selected;
        }

        ////////////// Begin Tree Node //////////////
        bool isOpenTree = ImGuiNet.TreeNodeEx($"###{obj.id}", flags);
        
        // Handle Right Click menu
        if (ImGuiNet.BeginPopupContextItem($"Popup_{obj.id}"))
        {
            if (ImGuiNet.MenuItem("Delete"))
            {
                m_pendingGuiUpdateAction.Enqueue(() =>
                {
                    obj.scene.UnregisterGameObject(obj);
                });
            }

            ImGuiNet.EndPopup();
        }
        
        // Handle click selection
        if (ImGuiNet.IsItemClicked((int)Input.MouseButton.Left))
        {
            selection.Select(obj);
        }

        // Drag Source
        if (ImGuiNet.BeginDragDropSource())
        {
            EditorImGuiEx.SetDragPayload(C_GAMEOBJECT_GUID_TYPE, obj.id);
            ImGuiNet.Text($"Dragging {obj.name}");
            ImGuiNet.EndDragDropSource();
        }

        // Drag Target
        if (ImGuiNet.BeginDragDropTarget())
        {
            var payload = EditorImGuiEx.AcceptDragPayload<Guid>(C_GAMEOBJECT_GUID_TYPE);
            if (payload != null && payload != obj.id)
            {
                var payloadObj = SceneManager.GetActiveScene()!.FindGameObject(payload.Value);
                m_pendingGuiUpdateAction.Enqueue(() => payloadObj?.transform.SetParent(obj.transform));
            }
            ImGuiNet.EndDragDropTarget();
        }
        
        // Tree Node Text
        var isCamera = obj.GetAllComponents().Any(c => c.GetType().IsAssignableTo(typeof(GameCamera)));
        ImGuiNet.SameLine();
        EditorImGuiEx.DrawIconAndText(isCamera ? ImGuiIcon.Camera : ImGuiIcon.Cube, obj.name);

        // Draw Children
        if (hasChildren && isOpenTree)
        {
            foreach (var child in obj.transform.children) DrawRootGameObject(child.gameObject);
        }
        
        ////////////// End Tree Node //////////////
        if (isOpenTree)
        {
            ImGuiNet.TreePop();
        }
    }
}
