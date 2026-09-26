using System;
using System.IO;
using BorderRepair.Tools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// 工作台检测仪屏幕：生成 BorderRepair/DiagnosticScreen 的三个材质预设（READY 暗绿 / BYPASS 警示红橙 / LOG 冷白），
    /// 并接到叙事场景的检测仪上（TesterReadout）。只改检测仪屏幕，不改界面、灯光或场景其他部分。
    /// 默认只创建缺失的材质；overwrite 时按代码里的预设值重写。
    /// 命令行：-executeMethod BorderRepair.EditorTools.TesterScreenBuilder.ApplyFromCommandLine
    /// </summary>
    public static class TesterScreenBuilder
    {
        public const string ShaderName = "BorderRepair/DiagnosticScreen";
        public const string MaterialDir = "Assets/BorderRepair/Art/WorkerHand/Materials/Screen";
        const string AtlasPath = "Assets/BorderRepair/Art/WorkerHand/Textures/T_Hand_Decals.png";

        public static readonly string[] PresetNames = { "M_TesterScreen_Ready", "M_TesterScreen_Bypass", "M_TesterScreen_Log" };

        // (字的颜色, 底色)：检视面板里的颜色值（Unity 会换算到线性空间）。项目没有色调映射，乘上背光（约 1.25）后不能超过 1，否则会被截成黄白色
        static readonly (Color ink, Color back)[] Presets =
        {
            (new Color(0.08f, 0.45f, 0.1f), new Color(0.04f, 0.12f, 0.05f)),         // READY：暗绿
            (new Color(0.9f, 0.13f, 0.03f), new Color(0.13f, 0.04f, 0.02f)),         // BYPASS：警示红橙
            (new Color(0.55f, 0.66f, 0.8f), new Color(0.06f, 0.08f, 0.11f)),         // LOG：冷白
        };

        [MenuItem("Border Repair/Narrative/Apply Tester Screen Shader (create missing)", priority = 67)]
        static void MenuApply()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Apply(false);
        }

        public static void ApplyFromCommandLine() => Apply(false);
        public static void ReapplyFromCommandLine() => Apply(true);

        public static void Apply(bool overwrite)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play 模式。");
            var mats = EnsureMaterials(overwrite);
            var scene = EditorSceneManager.OpenScene(NarrativeSliceBuilder.ScenePath, OpenSceneMode.Single);
            var readout = Object.FindFirstObjectByType<TesterReadout>();
            if (readout == null) throw new InvalidOperationException("叙事场景里没有检测仪（先运行 Build Worker Hand v2）");
            AssignPresets(readout, mats);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("场景保存失败");
            Debug.Log("[BorderRepair] 检测仪屏幕 shader 预设已接入");
        }

        public static Material[] EnsureMaterials(bool overwrite)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException("找不到 shader " + ShaderName);
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            if (atlas == null) throw new FileNotFoundException("找不到读数贴图集", AtlasPath);
            EnsureFolder(MaterialDir);
            var result = new Material[3];
            for (int i = 0; i < 3; i++)
            {
                string path = $"{MaterialDir}/{PresetNames[i]}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool fresh = mat == null;
                if (fresh)
                {
                    mat = new Material(shader) { name = PresetNames[i] };
                    AssetDatabase.CreateAsset(mat, path);
                }
                if (fresh || overwrite)
                {
                    mat.shader = shader;
                    mat.SetTexture("_BaseMap", atlas);
                    mat.SetColor("_InkColor", Presets[i].ink);
                    mat.SetColor("_BackColor", Presets[i].back);
                    EditorUtility.SetDirty(mat);
                }
                result[i] = mat;
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        /// <summary>给检测仪配上三个预设（v2 构建器重建检测仪后也调用它）。</summary>
        public static void AssignPresets(TesterReadout readout, Material[] mats)
        {
            var so = new SerializedObject(readout);
            var arr = so.FindProperty("presets");
            arr.arraySize = 3;
            for (int i = 0; i < 3; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = mats[i];
            so.FindProperty("usePresets").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static Material[] LoadExisting()
        {
            var mats = new Material[3];
            for (int i = 0; i < 3; i++)
            {
                mats[i] = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/{PresetNames[i]}.mat");
                if (mats[i] == null) return null;
            }
            return mats;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
