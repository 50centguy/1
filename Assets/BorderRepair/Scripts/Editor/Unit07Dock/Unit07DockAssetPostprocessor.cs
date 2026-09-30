using BorderRepair.Dock;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// 维修座 FBX 的导入设置与自定义属性。只作用于 Assets/BorderRepair/Art/Unit07ServiceDock/ 下的模型。
    /// - 导入设置按 ArtSource/Unit07ServiceDock/README.md：缩放 1、转换单位、无动画、不导入相机与灯光。
    /// - Blender 写入的自定义属性（dock_role、unity_open_deg、unity_on_deg / unity_off_deg 等）逐对象写成 DockPartProperties，
    ///   运行时由 Unit07DockController 读取和核对。
    /// </summary>
    public class Unit07DockAssetPostprocessor : AssetPostprocessor
    {
        public const string DockArtDir = "Assets/BorderRepair/Art/Unit07ServiceDock/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(DockArtDir)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = true;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
            mi.isReadable = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        }

        void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] names, object[] values)
        {
            if (!assetPath.StartsWith(DockArtDir)) return;
            var props = go.GetComponent<DockPartProperties>() ?? go.AddComponent<DockPartProperties>();
            for (int i = 0; i < names.Length; i++)
                props.Set(names[i], System.Convert.ToString(values[i], System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
