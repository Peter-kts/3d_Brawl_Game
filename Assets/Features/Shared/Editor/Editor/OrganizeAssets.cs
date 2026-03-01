using UnityEditor;
using UnityEngine;

/// <summary>
/// One-time menu action to move root-level assets into Scenes, Animation, and Models folders.
/// Uses AssetDatabase so all references (GUIDs) stay valid. Run from Tools > Organize Assets.
/// </summary>
public static class OrganizeAssets
{
    const string ScenesFolder = "Assets/Scenes";
    const string AnimationFolder = "Assets/Animation";
    const string ModelsFolder = "Assets/Models";

    [MenuItem("Tools/Organize Assets")]
    public static void Run()
    {
        int moved = 0;
        string err;

        EnsureFolder("Assets", "Scenes");
        EnsureFolder("Assets", "Animation");
        EnsureFolder("Assets", "Models");

        if (Move("Assets/dasd.unity", ScenesFolder + "/dasd.unity", out err)) moved++;
        else if (!string.IsNullOrEmpty(err)) Debug.LogWarning("OrganizeAssets: " + err);

        if (Move("Assets/main_char.controller", AnimationFolder + "/main_char.controller", out err)) moved++;
        else if (!string.IsNullOrEmpty(err)) Debug.LogWarning("OrganizeAssets: " + err);

        if (Move("Assets/enemy.controller", AnimationFolder + "/enemy.controller", out err)) moved++;
        else if (!string.IsNullOrEmpty(err)) Debug.LogWarning("OrganizeAssets: " + err);

        if (Move("Assets/jbombentry.fbx", ModelsFolder + "/jbombentry.fbx", out err)) moved++;
        else if (!string.IsNullOrEmpty(err)) Debug.LogWarning("OrganizeAssets: " + err);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (moved > 0) Debug.Log("OrganizeAssets: Moved " + moved + " asset(s).");
    }

    static void EnsureFolder(string parent, string name)
    {
        if (AssetDatabase.IsValidFolder(parent) && !AssetDatabase.IsValidFolder(parent + "/" + name))
            AssetDatabase.CreateFolder(parent, name);
    }

    static bool Move(string from, string to, out string error)
    {
        error = null;
        string result = AssetDatabase.MoveAsset(from, to);
        if (!string.IsNullOrEmpty(result))
        {
            error = result;
            return false;
        }
        return true;
    }
}
