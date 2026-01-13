using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;

using Inno.Core.ECS;
using Inno.Core.Events;
using Inno.Core.Math;
using Inno.Editor.Core;
using Inno.Editor.GUI;
using Inno.Runtime.Component;

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
        ImGui.BeginChild(
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

        ImGui.EndChild();
    }

    private void HandleMenu()
    {
        if (!ImGui.IsAnyItemHovered() && ImGui.IsWindowHovered() && ImGui.GetIO().MouseClicked[(int)Input.MouseButton.Right])
        {
            ImGui.OpenPopup("HierarchyContextMenu");
        }

        if (ImGui.BeginPopup("HierarchyContextMenu"))
        {
            if (ImGui.BeginMenu("Create"))
            {
                if (ImGui.MenuItem("GameObject"))
                {
                    m_pendingGuiUpdateAction.Enqueue(() =>
                    {
                        var go = new GameObject("New GameObject");
                        EditorManager.selection.Select(go);
                    });
                }
                ImGui.EndMenu();
            }
            ImGui.EndPopup();
        }
    }

    private void DrawSceneObjectRoot()
    {
        // Draw "Scene Root" as non-selectable, non-draggable
        ImGui.Text("[ Scene Root ]");
        if (ImGui.BeginDragDropTarget())
        {
            var payload = EditorImGuiEx.AcceptDragDropPayload<Guid>(C_GAMEOBJECT_GUID_TYPE);
            if (payload != null)
            {
                var obj = SceneManager.GetActiveScene()!.FindGameObject(payload.Value);
                m_pendingGuiUpdateAction.Enqueue(() => obj?.transform.SetParent(null));
            }
            ImGui.EndDragDropTarget();
        }
    }

    private void DrawRootGameObject(GameObject obj)
    {
        var selection = EditorManager.selection;
        bool isSelected = selection.IsSelected(obj);
        bool hasChildren = obj.transform.children.Count > 0;

        // TreeNodeFlags with Selected flag
        var flags = hasChildren ? ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick : ImGuiTreeNodeFlags.Leaf;
        if (isSelected) { flags |= ImGuiTreeNodeFlags.Selected; }

        ////////////// Begin Tree Node //////////////
        var isCamera = obj.GetAllComponents().Any(c => c.GetType().IsAssignableTo(typeof(GameCamera)));
        var icon = isCamera ? ImGuiIcon.Camera : ImGuiIcon.Cube;
        bool isOpenTree = ImGui.TreeNodeEx($"{icon} {obj.name}###{obj.id}", flags);
        
        // Handle Right Click menu
        if (ImGui.BeginPopupContextItem($"Popup_{obj.id}"))
        {
            if (ImGui.MenuItem("Delete"))
            {
                m_pendingGuiUpdateAction.Enqueue(() =>
                {
                    obj.scene.UnregisterGameObject(obj);
                });
            }

            ImGui.EndPopup();
        }
        
        // Handle click selection
        if (ImGui.IsItemClicked((int)Input.MouseButton.Left))
        {
            selection.Select(obj);
        }

        // Drag Source
        if (ImGui.BeginDragDropSource())
        {
            EditorImGuiEx.SetDragDropPayload(C_GAMEOBJECT_GUID_TYPE, obj.id);
            ImGui.Text($"Dragging {obj.name}");
            ImGui.EndDragDropSource();
        }

        // Drag Target
        if (ImGui.BeginDragDropTarget())
        {
            var payload = EditorImGuiEx.AcceptDragDropPayload<Guid>(C_GAMEOBJECT_GUID_TYPE);
            if (payload != null && payload != obj.id)
            {
                var payloadObj = SceneManager.GetActiveScene()!.FindGameObject(payload.Value);
                m_pendingGuiUpdateAction.Enqueue(() => payloadObj?.transform.SetParent(obj.transform));
            }
            ImGui.EndDragDropTarget();
        }

        // Draw Children
        if (hasChildren && isOpenTree)
        {
            foreach (var child in obj.transform.children) DrawRootGameObject(child.gameObject);
        }
        
        ////////////// End Tree Node //////////////
        if (isOpenTree) { ImGui.TreePop(); }
    }
}
