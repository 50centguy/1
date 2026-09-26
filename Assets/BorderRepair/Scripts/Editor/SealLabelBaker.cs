using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// 封条印字：在编辑器里用内置字体把文字渲染成一张贴图，再用 URP Lit 材质贴在封条前的小面片上。
    /// 这样印字和其他零件一样接受光照、正确参与深度遮挡（TextMesh 的内置文字 shader 会透过其他物体显示）。
    /// 撕开的左半边复用同一张贴图，只取左侧对应的部分，文字正好在撕口处断开。
    /// </summary>
    internal static class SealLabelBaker
    {
        public const string TexturePath = "Assets/BorderRepair/Art/Narrative/Textures/T_SealLabel.png";
        public const string IntactMaterialPath = "Assets/BorderRepair/Art/Narrative/M_SealLabel.mat";
        public const string TornMaterialPath = "Assets/BorderRepair/Art/Narrative/M_SealLabel_TornLeft.mat";
        /// <summary>只写租赁方信息，不透露限力被远程关闭的真相。内置字体只保证 ASCII。</summary>
        public const string Text = "NSP PORT LEASED\nVOID IF OPENED";
        /// <summary>撕开后留下的左半边占整条封条宽度的比例（封条 36 mm，左半边 14 mm）。</summary>
        public const float TornLeftFraction = 14f / 36f;

        static readonly Color SealRed = new Color(0.78f, 0.13f, 0.11f);
        static readonly Color Ink = new Color(1f, 0.95f, 0.86f);
        const int Width = 1024, Height = 256;
        const int BakeLayer = 31;

        public static (Material intact, Material torn) EnsureMaterials()
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath) == null) BakeTexture();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (tex == null) throw new IOException("封条印字贴图生成失败：" + TexturePath);
            var intact = EnsureMaterial(IntactMaterialPath, tex, Vector2.one, Vector2.zero);
            var torn = EnsureMaterial(TornMaterialPath, tex, new Vector2(TornLeftFraction, 1f), Vector2.zero);
            return (intact, torn);
        }

        static Material EnsureMaterial(string path, Texture2D tex, Vector2 tiling, Vector2 offset)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("找不到 URP Lit shader");
            mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", tiling);
            mat.SetTextureOffset("_BaseMap", offset);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.25f);
            mat.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static void BakeTexture()
        {
            EnsureFolder(Path.GetDirectoryName(TexturePath).Replace('\\', '/'));
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var textGo = new GameObject("SealLabelBake_Text") { hideFlags = HideFlags.HideAndDontSave, layer = BakeLayer };
            var camGo = new GameObject("SealLabelBake_Camera") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            try
            {
                var tm = textGo.AddComponent<TextMesh>();
                tm.font = font;
                tm.text = Text;
                tm.fontSize = 128;
                tm.characterSize = 0.1f;
                tm.lineSpacing = 0.95f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.fontStyle = FontStyle.Bold;
                tm.color = Ink;
                var mr = textGo.GetComponent<MeshRenderer>();
                mr.sharedMaterial = font.material;
                textGo.transform.position = new Vector3(0f, -1000f, 0f);

                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = SealRed;
                cam.cullingMask = 1 << BakeLayer;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 10f;
                cam.aspect = Width / (float)Height;
                // 文字宽高按贴图留边：宽度最多占 88%，高度最多占 80%
                var b = mr.bounds;
                float byHeight = b.size.y / 0.8f * 0.5f;
                float byWidth = b.size.x / 0.88f * 0.5f / cam.aspect;
                cam.orthographicSize = Mathf.Max(byHeight, byWidth);
                cam.transform.position = b.center + new Vector3(0f, 0f, -5f);
                cam.transform.rotation = Quaternion.identity;
                cam.targetTexture = rt;
                cam.Render();

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(TexturePath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                if (camGo != null) { camGo.GetComponent<Camera>().targetTexture = null; Object.DestroyImmediate(camGo); }
                Object.DestroyImmediate(textGo);
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
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
