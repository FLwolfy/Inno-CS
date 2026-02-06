using System.Collections.Generic;

namespace Inno.Editor.ImGui;

internal static class ImGuiDataStore
{
    // ini-friendly: we store as string payload
    public static readonly Dictionary<string, string> DATA = new();
}