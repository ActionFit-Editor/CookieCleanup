#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ActionFit.CookieCleanup.Editor
{
    public static class CookieCleanupPackageMenu
    {
        private const string MenuRoot = "Tools/Package/ActionFit Cookie Cleanup/";
        private const string ReadmePath = "Packages/com.actionfit.cookie-cleanup/README.md";

        [MenuItem(MenuRoot + "README", false, 908)]
        private static void OpenReadme()
        {
            var readme = AssetDatabase.LoadAssetAtPath<TextAsset>(ReadmePath);
            if (readme == null)
            {
                EditorUtility.DisplayDialog("Package README", $"README was not found.\n{ReadmePath}", "OK");
                return;
            }
            Selection.activeObject = readme;
            AssetDatabase.OpenAsset(readme);
        }
    }
}
#endif
