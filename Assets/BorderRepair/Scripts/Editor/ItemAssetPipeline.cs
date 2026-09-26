using System;
using System.Collections.Generic;
using System.IO;
using BorderRepair.Data;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// 把 Blender 导出的正式物品接入 Unity 的共用步骤：贴图导入设置、按材质清单建 URP 材质、FBX 导入与材质重映射、
    /// 包装 prefab 中的模型实例与碰撞体。新物品的构建器使用它；CommunicatorAssetBuilder 仍保持自己的独立实现。
    /// </summary>
    public static class ItemAssetPipeline
    {
        /// <summary>Blender 正面 -Y → FBX(-Z forward, Y up, Apply Transform) → Unity 正面 +Z；转 180° 让正面朝向检查台镜头。</summary>
        public static readonly Quaternion ModelRotation = Quaternion.Euler(0f, 180f, 0f);

        [Serializable]
        public class MaterialSpec
        {
            public string name;
            public string baseColor;
            public float metallic;
            public float smoothness;
            public string baseMap;
            public string emissionMap;
            public string emissionColor;
            public float emissionStrength;
        }

        [Serializable]
        public class MaterialManifest
        {
            public MaterialSpec[] materials;
        }

        public static MaterialManifest LoadManifest(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("找不到材质清单，请先运行 Blender 建模脚本导出", path);
            var manifest = JsonUtility.FromJson<MaterialManifest>(File.ReadAllText(path));
            if (manifest?.materials == null || manifest.materials.Length == 0) throw new InvalidDataException("材质清单为空：" + path);
            return manifest;
        }

        /// <summary>名称含 repeatKeyword 的贴图平铺（Repeat），其余（贴图集、屏幕、电路板）Clamp。最大尺寸取源图尺寸。</summary>
        public static void ConfigureTextures(string textureDir, string repeatKeyword)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { textureDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.GetSourceTextureWidthAndHeight(out int w, out int h);
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.None;
                ti.wrapMode = Path.GetFileName(path).Contains(repeatKeyword) ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 4;
                ti.maxTextureSize = Mathf.Max(32, Mathf.NextPowerOfTwo(Mathf.Max(w, h)));
                ti.SaveAndReimport();
            }
        }

        public static Dictionary<string, Material> BuildMaterials(MaterialManifest manifest, string materialDir, string textureDir)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("找不到 URP Lit 着色器。");
            EnsureFolder(materialDir);

            var result = new Dictionary<string, Material>();
            foreach (var spec in manifest.materials)
            {
                string path = $"{materialDir}/{spec.name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = shader;
                mat.SetColor("_BaseColor", ParseColor(spec.baseColor));
                mat.SetFloat("_Metallic", spec.metallic);
                mat.SetFloat("_Smoothness", spec.smoothness);
                var baseMap = LoadTexture(textureDir, spec.baseMap);
                mat.SetTexture("_BaseMap", baseMap);
                mat.mainTexture = baseMap;

                if (spec.emissionStrength > 0f)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetTexture("_EmissionMap", LoadTexture(textureDir, spec.emissionMap));
                    mat.SetColor("_EmissionColor", ParseColor(spec.emissionColor) * spec.emissionStrength);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else
                {
                    mat.DisableKeyword("_EMISSION");
                    mat.SetTexture("_EmissionMap", null);
                    mat.SetColor("_EmissionColor", Color.black);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                }
                EditorUtility.SetDirty(mat);
                result[spec.name] = mat;
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        public static void ConfigureModel(string path, Dictionary<string, Material> materials)
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) throw new FileNotFoundException("找不到 FBX，请先运行 Blender 建模脚本导出", path);

            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importBlendShapes = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = false;
            mi.addCollider = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;

            foreach (var id in new List<AssetImporter.SourceAssetIdentifier>(mi.GetExternalObjectMap().Keys))
                if (id.type == typeof(Material)) mi.RemoveRemap(id);
            foreach (var kv in materials)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m == null || !materials.ContainsValue(m))
                        Debug.LogError($"[BorderRepair] {path} 的 {r.name} 使用了未重映射的材质 {(m != null ? m.name : "null")}");
        }

        public static GameObject InstantiateModel(string fbxPath, Transform parent, string name)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) throw new FileNotFoundException("找不到 FBX", fbxPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            go.name = name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = ModelRotation;
            go.transform.localScale = Vector3.one;
            return go;
        }

        /// <summary>为 source 下每个网格在包装层自己的物体上加 MeshCollider（引用网格资产，不依赖 FBX 子物体名称）。</summary>
        public static void AddMeshColliders(GameObject source, Transform parent, string prefix)
        {
            int index = 0;
            foreach (var mf in source.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var c = new GameObject($"{prefix}_{index++:00}");
                c.transform.SetParent(parent, false);
                c.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                c.transform.localScale = mf.transform.lossyScale;
                c.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
        }

        /// <summary>用网格包围盒（含隐藏子物体）在 point 的本地空间拟合 BoxCollider。</summary>
        public static BoxCollider FitBoxCollider(GameObject point, float padding)
        {
            var toLocal = point.transform.worldToLocalMatrix;
            bool has = false;
            var bounds = new Bounds();
            foreach (var mf in point.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = toLocal * mf.transform.localToWorldMatrix;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!has) { bounds = new Bounds(p, Vector3.zero); has = true; }
                    else bounds.Encapsulate(p);
                }
            }
            if (!has) throw new InvalidOperationException($"{point.name} 下没有网格，无法生成碰撞体");
            var box = point.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size + Vector3.one * (padding * 2f);
            return box;
        }

        /// <summary>只修改案例的 itemPrefab 字段，其余（包括手工修改过的文本）保持不变。</summary>
        public static void AssignCasePrefab(string casePath, GameObject prefab)
        {
            var data = AssetDatabase.LoadAssetAtPath<RepairCaseData>(casePath);
            if (data == null) throw new FileNotFoundException("找不到案例数据", casePath);
            var so = new SerializedObject(data);
            so.FindProperty("itemPrefab").objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
        }

        public static long CountTriangles(string fbxPath)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            long tris = 0;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                    tris += mf.sharedMesh.GetIndexCount(s) / 3;
            return tris;
        }

        public static string DescribeTextures(string textureDir)
        {
            var list = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { textureDir }))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                list.Add($"{tex.name}={tex.width}x{tex.height}");
            }
            return string.Join(", ", list);
        }

        static Texture2D LoadTexture(string textureDir, string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{textureDir}/{fileName}");
            if (tex == null) throw new FileNotFoundException("找不到贴图", $"{textureDir}/{fileName}");
            return tex;
        }

        static Color ParseColor(string html)
        {
            if (!ColorUtility.TryParseHtmlString(html, out var c)) throw new FormatException("颜色格式错误：" + html);
            return c;
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
