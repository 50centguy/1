using UnityEditor;
using UnityEngine;

namespace WorkbenchArea.EditorTools
{
    /// <summary>
    /// 工作台区域 FBX 的导入设置与自定义属性。只作用于 Assets/WorkbenchArea/Art/ 下的模型。
    /// Blender 写入的属性（wb_role、grab_point、hinge_axis_local、slide_axis_local、rot_axis_local、note 等）逐对象写成 WbPartProperties。
    /// </summary>
    public class WorkbenchAreaPostprocessor : AssetPostprocessor
    {
        public const string ArtDir = "Assets/WorkbenchArea/Art/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ArtDir)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = true;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
            mi.isReadable = true;            // 静态 MeshCollider 与编辑器测试读取三角面
            mi.addCollider = false;          // 碰撞由构建器按角色添加
            mi.importNormals = ModelImporterNormals.Import;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtDir)) return;
            var ti = (TextureImporter)assetImporter;
            bool text = assetPath.Contains("Paper") || assetPath.Contains("Screen") || assetPath.Contains("_Mat");
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.filterMode = FilterMode.Point;                      // 低分辨率像素颗粒
            ti.mipmapEnabled = !text;                               // 文字贴图不做 mip，保持手写字可读
            ti.wrapMode = text ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.alphaSource = TextureImporterAlphaSource.None;
            ti.maxTextureSize = 1024;
        }

        void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] names, object[] values)
        {
            if (!assetPath.StartsWith(ArtDir)) return;
            var props = go.GetComponent<WbPartProperties>() ?? go.AddComponent<WbPartProperties>();
            for (int i = 0; i < names.Length; i++)
                props.Set(names[i], System.Convert.ToString(values[i], System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
