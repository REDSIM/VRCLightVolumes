#if UNITY_EDITOR
using UnityEditor;

[InitializeOnLoad]
internal static class LightVolumesDefineManager {
    private const string LegacyDefineSymbol = "VRC_LIGHT_VOLUMES";

    // Temporary discovery signal for released LTCGI versions. New optional integrations use asmdef references/versionDefines instead of project-wide symbols.
    static LightVolumesDefineManager() {
        var targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
        var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
        string[] symbols = defines.Split(';');
        for (int i = 0; i < symbols.Length; i++) {
            if (symbols[i] == LegacyDefineSymbol) return;
        }
        string separator = string.IsNullOrEmpty(defines) || defines.EndsWith(";") ? string.Empty : ";";
        PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, defines + separator + LegacyDefineSymbol);
    }
}
#endif
