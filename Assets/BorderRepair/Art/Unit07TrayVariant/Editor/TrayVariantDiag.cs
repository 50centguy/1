using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.Art.Unit07TrayVariant.EditorTools
{
    /// <summary>只读诊断：脚本类能否解析、原场景里托盘的组件。</summary>
    public static class TrayVariantDiag
    {
        public static void Reimport()
        {
            AssetDatabase.StartAssetEditing();
            try { foreach (var g in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" })) AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate); }
            finally { AssetDatabase.StopAssetEditing(); }
            Debug.Log("[Diag] 已强制重新导入 Assets 下全部脚本（复制来的 Library 里脚本到类的对应关系失效）");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        public static void ReimportModels()
        {
            foreach (var p in new[] { "Assets/BorderRepair/Art/Unit07ServiceDock/UNIT07_ServiceDock.fbx" }) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            Debug.Log("[Diag] 已重新导入维修座 FBX（导入后处理会加 DockPartProperties）");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        public static void Run()
        {
            foreach (var p in new[] { "Assets/BorderRepair/Scripts/Runtime/Dock/DockPickable.cs", "Assets/BorderRepair/Scripts/Runtime/Dock/DockInteractable.cs", "Assets/BorderRepair/Scripts/Runtime/Dock/DockPartProperties.cs" })
            {
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(p);
                Debug.Log($"[Diag] {p}: MonoScript {(ms == null ? "null" : "ok")}, class {(ms?.GetClass() == null ? "null" : ms.GetClass().AssemblyQualifiedName)}, guid {AssetDatabase.AssetPathToGUID(p)}");
            }
            EditorSceneManager.OpenScene("Assets/BorderRepair/Scenes/Slice/Unit07_Night.unity", OpenSceneMode.Single);
            var go = GameObject.Find("Dock_PartsTray");
            Debug.Log("[Diag] 原场景托盘组件：" + string.Join(",", go.GetComponents<Component>().Select(c => c == null ? "(missing)" : c.GetType().Name)));
            int miss = 0; foreach (var g in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)) miss += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(g);
            Debug.Log("[Diag] 原场景缺脚本的组件总数：" + miss);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
