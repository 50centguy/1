using System;
using System.Collections.Generic;
using System.IO;
using BorderRepair.Data;
using BorderRepair.Inspection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// 把 Blender 导出的通讯器（ArtSource/Communicator/build_communicator.py）接入项目：
    /// 设置贴图与 FBX 导入、按 Communicator_Materials.json 创建 URP 材质并重映射，
    /// 生成外层包装 prefab，并让 Case_01_Communicator 引用它。
    /// 包装 prefab 只引用各 FBX 的根对象；InspectionPoint、碰撞体、换件切换都挂在包装层自己的物体上，
    /// 不依赖 FBX 内部子物体的名称。
    /// 命令行：-executeMethod BorderRepair.EditorTools.CommunicatorAssetBuilder.BuildFromCommandLine
    /// </summary>
    public static class CommunicatorAssetBuilder
    {
        const string ArtDir = "Assets/BorderRepair/Art/Communicator";
        const string ModelDir = ArtDir + "/Models";
        const string TextureDir = ArtDir + "/Textures";
        const string MaterialDir = ArtDir + "/Materials";
        const string ManifestPath = ArtDir + "/Communicator_Materials.json";
        const string CasePath = PrototypeBuilder.CaseDir + "/Case_01_Communicator.asset";
        public const string PrefabPath = "Assets/BorderRepair/Prefabs/Items/Item_Communicator_Final.prefab";

        static readonly string[] Groups = { "Body", "Screen", "Battery", "AntennaBroken", "AntennaRepaired" };

        // Blender 中正面朝 -Y；按 (-Z forward, Y up, Apply Transform) 导出后，在 Unity 中正面朝 +Z。
        // 检查台镜头从 -Z 方向看物品，所以模型绕 Y 转 180°。
        static readonly Quaternion ModelRotation = Quaternion.Euler(0f, 180f, 0f);

        [Serializable]
        class MaterialSpec
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
        class MaterialManifest
        {
            public MaterialSpec[] materials;
        }

        [MenuItem("Border Repair/Assets/Build Communicator (from Blender export)", priority = 40)]
        static void MenuBuild() => Build();

        public static void BuildFromCommandLine() => Build();

        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play 模式。");
            AssetDatabase.Refresh();

            var manifest = LoadManifest();
            ConfigureTextures();
            var materials = BuildMaterials(manifest);
            foreach (var g in Groups) ConfigureModel($"{ModelDir}/Communicator_{g}.fbx", materials);

            var prefab = BuildPrefab();
            AssignToCase(prefab);
            AssetDatabase.SaveAssets();
            LogStats(materials);
            Debug.Log($"[BorderRepair] 通讯器正式资产已接入：{PrefabPath}");
        }

        // ---------- 导入设置 ----------

        static MaterialManifest LoadManifest()
        {
            if (!File.Exists(ManifestPath)) throw new FileNotFoundException("找不到材质清单，请先运行 Blender 建模脚本导出", ManifestPath);
            var manifest = JsonUtility.FromJson<MaterialManifest>(File.ReadAllText(ManifestPath));
            if (manifest?.materials == null || manifest.materials.Length == 0) throw new InvalidDataException("材质清单为空：" + ManifestPath);
            return manifest;
        }

        static void ConfigureTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                bool isScreen = path.Contains("Screen");
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.None;
                ti.wrapMode = isScreen ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 4;
                ti.maxTextureSize = isScreen ? 512 : 1024;
                ti.SaveAndReimport();
            }
        }

        static Dictionary<string, Material> BuildMaterials(MaterialManifest manifest)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("找不到 URP Lit 着色器。");
            if (!AssetDatabase.IsValidFolder(MaterialDir)) AssetDatabase.CreateFolder(ArtDir, "Materials");

            var result = new Dictionary<string, Material>();
            foreach (var spec in manifest.materials)
            {
                string path = $"{MaterialDir}/{spec.name}.mat";
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
                var baseMap = LoadTexture(spec.baseMap);
                mat.SetTexture("_BaseMap", baseMap);
                mat.mainTexture = baseMap;

                var emissionMap = LoadTexture(spec.emissionMap);
                if (emissionMap != null)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetTexture("_EmissionMap", emissionMap);
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

        static Texture2D LoadTexture(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/{fileName}");
            if (tex == null) throw new FileNotFoundException("找不到贴图", $"{TextureDir}/{fileName}");
            return tex;
        }

        static Color ParseColor(string html)
        {
            if (!ColorUtility.TryParseHtmlString(html, out var c)) throw new FormatException("颜色格式错误：" + html);
            return c;
        }

        static void ConfigureModel(string path, Dictionary<string, Material> materials)
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

            // 检查是否有 FBX 材质没有被重映射到 URP 材质
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m == null || !materials.ContainsValue(m))
                        Debug.LogError($"[BorderRepair] {path} 的 {r.name} 使用了未重映射的材质 {(m != null ? m.name : "null")}");
        }

        // ---------- 包装 prefab ----------

        static GameObject BuildPrefab()
        {
            var root = new GameObject("Item_Communicator_Final");
            try
            {
                var body = InstantiateModel("Body", root.transform, "Model_Body");

                // 机身碰撞：包装层自己的物体上挂 MeshCollider，直接引用网格资产
                var bodyColliders = new GameObject("BodyColliders").transform;
                bodyColliders.SetParent(root.transform, false);
                int index = 0;
                foreach (var mf in body.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var c = new GameObject($"BodyCollider_{index++:00}");
                    c.transform.SetParent(bodyColliders, false);
                    c.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                    c.transform.localScale = mf.transform.lossyScale;
                    c.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                }

                var screen = CreatePoint(root.transform, "screen");
                InstantiateModel("Screen", screen.transform, "Model_Screen");
                screen.Configure("screen", null, null);
                FitBoxCollider(screen.gameObject, 0.003f);

                var battery = CreatePoint(root.transform, "battery");
                InstantiateModel("Battery", battery.transform, "Model_Battery");
                battery.Configure("battery", null, null);
                FitBoxCollider(battery.gameObject, 0.0015f);

                var antenna = CreatePoint(root.transform, "antenna");
                var broken = InstantiateModel("AntennaBroken", antenna.transform, "Model_AntennaBroken");
                var repaired = InstantiateModel("AntennaRepaired", antenna.transform, "Model_AntennaRepaired");
                antenna.Configure("antenna", broken, repaired);
                FitBoxCollider(antenna.gameObject, 0.002f);   // 同时包住损坏和修好两种外观
                repaired.SetActive(false);

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
                if (!ok) throw new IOException("prefab 保存失败：" + PrefabPath);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static GameObject InstantiateModel(string group, Transform parent, string name)
        {
            string path = $"{ModelDir}/Communicator_{group}.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) throw new FileNotFoundException("找不到 FBX", path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            go.name = name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = ModelRotation;
            go.transform.localScale = Vector3.one;
            return go;
        }

        static InspectionPoint CreatePoint(Transform root, string pointId)
        {
            var go = new GameObject("Point_" + pointId);
            go.transform.SetParent(root, false);
            return go.AddComponent<InspectionPoint>();
        }

        /// <summary>用网格包围盒（含隐藏的子物体）在检查点本地空间拟合一个 BoxCollider。</summary>
        static void FitBoxCollider(GameObject point, float padding)
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
        }

        static void AssignToCase(GameObject prefab)
        {
            var data = AssetDatabase.LoadAssetAtPath<RepairCaseData>(CasePath);
            if (data == null) throw new FileNotFoundException("找不到案例数据", CasePath);
            data.itemPrefab = prefab;
            EditorUtility.SetDirty(data);
        }

        static void LogStats(Dictionary<string, Material> materials)
        {
            var lines = new List<string>();
            int bodySet = 0, maxAntenna = 0;
            foreach (var g in Groups)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/Communicator_{g}.fbx");
                long tris = 0;
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                    for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                        tris += mf.sharedMesh.GetIndexCount(s) / 3;
                lines.Add($"{g}={tris}");
                if (g.StartsWith("Antenna")) maxAntenna = Mathf.Max(maxAntenna, (int)tris);
                else bodySet += (int)tris;
            }
            var textures = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureDir }))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                textures.Add($"{tex.name}={tex.width}x{tex.height}");
            }
            Debug.Log($"[BorderRepair] 通讯器 Unity 导入统计：三角面 {string.Join(", ", lines)}；机身+屏幕+电池={bodySet}；同屏最大={bodySet + maxAntenna}；" +
                      $"材质 {materials.Count} 个；贴图 {string.Join(", ", textures)}");
        }
    }
}
