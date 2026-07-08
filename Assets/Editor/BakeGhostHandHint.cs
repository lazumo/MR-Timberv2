using Oculus.Interaction.HandGrab;
using Oculus.Interaction.HandGrab.Visuals;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把選中的 HandGrabPose（Hand Grab Pose Recorder 錄的）烘成一隻「定格姿勢的靜態 ghost 手」。
/// 用法：Hierarchy 選中 HandGrabPose → 選單 TimberLand → Bake Selected HandGrabPose To Ghost Hand。
/// 產出的手是純視覺（所有 ISDK component 都拆掉），掛在 pose 的 RelativeTo（把手）底下，
/// 可以直接複製進 NewColorFactory 的提示層級使用。
/// </summary>
public static class BakeGhostHandHint
{
    private const string MenuPath = "TimberLand/Bake Selected HandGrabPose To Ghost Hand";

    [MenuItem(MenuPath)]
    private static void Bake()
    {
        var go = Selection.activeGameObject;
        var pose = go != null ? go.GetComponent<HandGrabPose>() : null;

        if (pose == null || pose.HandPose == null)
        {
            EditorUtility.DisplayDialog("Bake Ghost Hand",
                "請先在 Hierarchy 選中一個 HandGrabPose（要含錄好的 HandPose）。", "OK");
            return;
        }

        // 取得 ghost 手 prefab（OpenXR 版優先，對應 recorder 預設的 ghost provider）
        var provider =
            AssetDatabase.LoadAssetAtPath<HandGhostProvider>(
                "Packages/com.meta.xr.sdk.interaction/Runtime/Prefabs/HandGrab/OpenXRGhostProvider.asset")
            ?? AssetDatabase.LoadAssetAtPath<HandGhostProvider>(
                "Packages/com.meta.xr.sdk.interaction/Runtime/Prefabs/HandGrab/GhostProvider.asset");

        if (provider == null)
        {
            EditorUtility.DisplayDialog("Bake Ghost Hand",
                "找不到 HandGhostProvider（Interaction SDK 的 ghost prefab 資源）。", "OK");
            return;
        }

        var handedness = pose.HandPose.Handedness;
        HandGhost ghostPrefab = provider.GetHand(handedness);
        if (ghostPrefab == null)
        {
            EditorUtility.DisplayDialog("Bake Ghost Hand", $"Provider 裡沒有 {handedness} 手的 ghost。", "OK");
            return;
        }

        // 生成 ghost、套上錄好的姿勢（關節角度 + 相對把手的位置）
        HandGhost ghost = Object.Instantiate(ghostPrefab);
        ghost.SetPose(pose);

        GameObject baked = ghost.gameObject;
        baked.name = $"GhostHandHint_{handedness}";

        // 掛到把手（RelativeTo）底下，之後把手動它就跟著動
        if (pose.RelativeTo != null)
            baked.transform.SetParent(pose.RelativeTo, worldPositionStays: true);

        // 拆掉所有 ISDK 腳本 → 變成純靜態視覺（姿勢已寫進關節 transform）
        foreach (var mb in baked.GetComponentsInChildren<MonoBehaviour>(true))
            Object.DestroyImmediate(mb);

        Undo.RegisterCreatedObjectUndo(baked, "Bake Ghost Hand Hint");
        Selection.activeGameObject = baked;

        Debug.Log($"[BakeGhostHandHint] Baked '{baked.name}' under '{(pose.RelativeTo != null ? pose.RelativeTo.name : "scene root")}'.");
    }
}
