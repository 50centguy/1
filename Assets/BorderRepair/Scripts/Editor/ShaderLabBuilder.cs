using System;
using System.Collections.Generic;
using System.IO;
using BorderRepair.Inspection;
using BorderRepair.ShaderLab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// Shader 原型的独立测试入口：生成磨损遮罩贴图、三个磨损材质实例、诊断叠加材质与高亮配置，
    /// 以及 ShaderLab 场景（材质球 + 现有物品的 prefab 实例）。
    /// 物品实例上的材质替换只作为场景里的实例覆盖保存，不修改 prefab 资产；默认场景和叙事场景不受影响。
    /// 材质与配置只在缺失时创建（手动调过的参数不会被覆盖）；场景在缺失或 overwrite 时重建。
    /// 命令行：-executeMethod BorderRepair.EditorTools.ShaderLabBuilder.BuildFromCommandLine
    /// </summary>
    public static class ShaderLabBuilder
    {
        public const string Root = "Assets/BorderRepair/Art/ShaderLab";
        public const string TextureDir = Root + "/Textures";
        public const string MaterialDir = Root + "/Materials";
        public const string WearMaskPath = TextureDir + "/T_WearMask.png";
        public const string EmissionStripePath = TextureDir + "/T_EmissionStripe.png";
        public const string PaintedPath = MaterialDir + "/M_Worn_PaintedMetal.mat";
        public const string CeramicPath = MaterialDir + "/M_Worn_OldCeramic.mat";
        public const string BareMetalPath = MaterialDir + "/M_Worn_BareMetal.mat";
        public const string OverlayPath = MaterialDir + "/M_DiagnosticOverlay.mat";
        public const string ProfilePath = Root + "/DiagnosticHighlightProfile.asset";
        public const string ScenePath = "Assets/BorderRepair/Scenes/ShaderLab_Surfaces.unity";

        [MenuItem("Border Repair/ShaderLab/Build ShaderLab (create missing)", priority = 80)]
        static void MenuBuild()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(false);
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Border Repair/ShaderLab/Rebuild ShaderLab Scene", priority = 81)]
        static void MenuRebuild()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(true);
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Border Repair/ShaderLab/Open ShaderLab Scene", priority = 82)]
        static void MenuOpen()
        {
            if (!File.Exists(ScenePath)) { EditorUtility.DisplayDialog("ShaderLab", "场景尚未生成，请先执行 Build ShaderLab。", "好"); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Border Repair/ShaderLab/Reset Highlight Profile To Defaults", priority = 83)]
        public static void ResetProfileStyles()
        {
            var profile = AssetDatabase.LoadAssetAtPath<DiagnosticHighlightProfile>(ProfilePath);
            if (profile == null) throw new FileNotFoundException("找不到高亮配置", ProfilePath);
            profile.ResetStylesToDefaults();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("[BorderRepair] 高亮配置已恢复为代码默认值：" + ProfilePath);
        }

        public static void BuildFromCommandLine() => Build(false);
        public static void RebuildSceneFromCommandLine() => Build(true);

        /// <param name="rebuildScene">只重建场景；材质、贴图、配置仍然只在缺失时创建。</param>
        public static void Build(bool rebuildScene)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play 模式。");
            EnsureFolder(TextureDir);
            EnsureFolder(MaterialDir);

            if (!File.Exists(WearMaskPath)) WriteTexture(WearMaskPath, MakeWearMask(512), linear: true);
            if (!File.Exists(EmissionStripePath)) WriteTexture(EmissionStripePath, MakeEmissionStripe(128), linear: false);
            var wearMask = AssetDatabase.LoadAssetAtPath<Texture2D>(WearMaskPath);
            var stripe = AssetDatabase.LoadAssetAtPath<Texture2D>(EmissionStripePath);

            var worn = Shader.Find("BorderRepair/WornSurface");
            var overlayShader = Shader.Find("BorderRepair/DiagnosticOverlay");
            if (worn == null || overlayShader == null) throw new InvalidOperationException("找不到 BorderRepair/WornSurface 或 DiagnosticOverlay shader（检查编译错误）。");

            CreateIfMissing(PaintedPath, () => Worn(worn, wearMask, null, "#3F6F6C", 0.05f, 0.42f, "#A4AAAE", 1f, 0.32f, 0.32f, 0.04f, 0.35f, "#6E6452", Color.black));
            CreateIfMissing(CeramicPath, () => Worn(worn, wearMask, stripe, "#DCD7CB", 0f, 0.28f, "#7F7A70", 0f, 0.85f, 0.18f, 0.02f, 0.55f, "#B7A57F", new Color(0.25f, 0.85f, 1f) * 0.9f));
            CreateIfMissing(BareMetalPath, () => Worn(worn, wearMask, null, "#8B9196", 1f, 0.38f, "#C7CCD0", 1f, 0.14f, 0.45f, 0.03f, 0.4f, "#5A544A", Color.black));
            CreateIfMissing(OverlayPath, () => new Material(overlayShader));

            var profile = AssetDatabase.LoadAssetAtPath<DiagnosticHighlightProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<DiagnosticHighlightProfile>();
                profile.overlayMaterial = AssetDatabase.LoadAssetAtPath<Material>(OverlayPath);
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            AssetDatabase.SaveAssets();

            if (rebuildScene || !File.Exists(ScenePath)) BuildScene();
            Debug.Log($"[BorderRepair] ShaderLab 生成完成：{ScenePath}");
        }

        // ---------- 材质 ----------

        static Material Worn(Shader shader, Texture2D mask, Texture2D emissionMap, string baseHex, float metallic, float roughness,
                             string wearHex, float wearMetallic, float wearRoughness, float wearAmount, float softness,
                             float grime, string grimeHex, Color emission)
        {
            var m = new Material(shader);
            m.SetColor("_BaseColor", Hex(baseHex));
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Roughness", roughness);
            m.SetTexture("_WearMask", mask);
            m.SetFloat("_WearAmount", wearAmount);
            m.SetFloat("_WearSoftness", softness);
            m.SetColor("_WearColor", Hex(wearHex));
            m.SetFloat("_WearMetallic", wearMetallic);
            m.SetFloat("_WearRoughness", wearRoughness);
            m.SetFloat("_GrimeAmount", grime);
            m.SetColor("_GrimeColor", Hex(grimeHex));
            bool emits = emissionMap != null && emission.maxColorComponent > 0f;
            m.SetFloat("_UseEmission", emits ? 1f : 0f);
            if (emits)
            {
                m.EnableKeyword("_EMISSION");
                m.SetTexture("_EmissionMap", emissionMap);
                m.SetColor("_EmissionColor", emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }
            return m;
        }

        static void CreateIfMissing(string path, Func<Material> make)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            AssetDatabase.CreateAsset(make(), path);
        }

        static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.magenta;

        // ---------- 贴图（可平铺） ----------

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        static float ValueNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            int Wrap(int v) => ((v % period) + period) % period;
            float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
            float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int basePeriod, int seed)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            int period = basePeriod;
            for (int o = 0; o < 4; o++)
            {
                sum += ValueNoise(u * period, v * period, period, seed + o * 17) * amp;
                norm += amp;
                amp *= 0.5f;
                period *= 2;
            }
            return sum / norm;
        }

        /// <summary>R：磨穿倾向（大块掉漆 + 细划痕）；G：污渍；B、A 未使用。数据贴图（线性）。</summary>
        static Texture2D MakeWearMask(int size)
        {
            var r = new float[size * size];
            var g = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    float chips = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.78f, Fbm(u, v, 5, 11)));
                    r[y * size + x] = chips * 0.92f;
                    g[y * size + x] = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.82f, Fbm(u, v, 3, 29))) * 0.9f
                                      + (Hash(x, y, 7) > 0.985f ? 0.3f : 0f);
                }
            var rng = new System.Random(4);
            for (int i = 0; i < 180; i++)                  // 细划痕：值高，磨损程度不大时也会露出
            {
                float x0 = (float)rng.NextDouble() * size, y0 = (float)rng.NextDouble() * size;
                float ang = (float)(rng.NextDouble() * Math.PI), len = 8 + (float)rng.NextDouble() * 60;
                float val = 0.8f + (float)rng.NextDouble() * 0.2f;
                int steps = (int)len + 1;
                for (int s = 0; s < steps; s++)
                {
                    int px = ((int)(x0 + Mathf.Cos(ang) * s) % size + size) % size;
                    int py = ((int)(y0 + Mathf.Sin(ang) * s) % size + size) % size;
                    r[py * size + px] = Mathf.Max(r[py * size + px], val);
                }
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            var px32 = new Color32[size * size];
            for (int i = 0; i < px32.Length; i++)
                px32[i] = new Color32((byte)(Mathf.Clamp01(r[i]) * 255), (byte)(Mathf.Clamp01(g[i]) * 255), 0, 255);
            tex.SetPixels32(px32);
            tex.Apply();
            return tex;
        }

        /// <summary>自发光：黑底上一条细亮线（状态指示条），面积约 3%。</summary>
        static Texture2D MakeEmissionStripe(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool on = y >= size / 2 - 2 && y < size / 2 + 2 && x > size / 8 && x < size * 7 / 8 && (x / 6) % 3 != 2;
                    px[y * size + x] = on ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static void WriteTexture(string path, Texture2D tex, bool linear)
        {
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.sRGBTexture = !linear;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.mipmapEnabled = true;
            ti.alphaSource = TextureImporterAlphaSource.None;
            ti.SaveAndReimport();
        }

        // ---------- 场景 ----------

        static readonly Vector3 OrbitTarget = new Vector3(0.15f, 0.08f, 0.05f);

        static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.36f, 0.39f, 0.44f);
            RenderSettings.ambientEquatorColor = new Color(0.26f, 0.25f, 0.23f);
            RenderSettings.ambientGroundColor = new Color(0.1f, 0.1f, 0.1f);

            var light = new GameObject("Key Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.95f, 0.88f);
            light.intensity = 1.3f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, 25f, 0f);

            var camGo = new GameObject("Lab Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.08f);
            camGo.transform.position = OrbitTarget + Quaternion.Euler(20f, 0f, 0f) * new Vector3(0f, 0f, -1.3f);
            camGo.transform.LookAt(OrbitTarget);

            var env = new GameObject("Environment").transform;
            var floorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/BorderRepair/Art/Materials/M_env_floor.mat");
            var wallMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/BorderRepair/Art/Materials/M_env_wall.mat");
            Prim(env, PrimitiveType.Cube, "Floor", new Vector3(0.15f, -0.01f, 0.2f), new Vector3(4f, 0.02f, 3f), floorMat);
            Prim(env, PrimitiveType.Cube, "Backdrop", new Vector3(0.15f, 0.7f, 1.2f), new Vector3(4f, 1.4f, 0.02f), wallMat);

            var painted = AssetDatabase.LoadAssetAtPath<Material>(PaintedPath);
            var ceramic = AssetDatabase.LoadAssetAtPath<Material>(CeramicPath);
            var bare = AssetDatabase.LoadAssetAtPath<Material>(BareMetalPath);
            var reference = AssetDatabase.LoadAssetAtPath<Material>("Assets/BorderRepair/Art/Materials/M_metal_light.mat");

            // 材质球：URP Lit 参照 + 三个磨损材质（球 + 旋转的立方体，便于看高光与阴影）
            var balls = new GameObject("MaterialSamples").transform;
            Prim(balls, PrimitiveType.Sphere, "Ref_URPLit", new Vector3(-0.6f, 0.08f, 0.4f), Vector3.one * 0.16f, reference);
            Prim(balls, PrimitiveType.Sphere, "Painted", new Vector3(-0.35f, 0.08f, 0.4f), Vector3.one * 0.16f, painted);
            Prim(balls, PrimitiveType.Sphere, "Ceramic", new Vector3(-0.1f, 0.08f, 0.4f), Vector3.one * 0.16f, ceramic);
            Prim(balls, PrimitiveType.Sphere, "BareMetal", new Vector3(0.15f, 0.08f, 0.4f), Vector3.one * 0.16f, bare);
            Prim(balls, PrimitiveType.Cube, "PaintedCube", new Vector3(0.4f, 0.065f, 0.4f), Vector3.one * 0.13f, painted, new Vector3(0, 30, 0));
            Prim(balls, PrimitiveType.Cube, "CeramicCube", new Vector3(0.62f, 0.065f, 0.4f), Vector3.one * 0.13f, ceramic, new Vector3(0, 30, 0));
            Prim(balls, PrimitiveType.Cube, "BareCube", new Vector3(0.84f, 0.065f, 0.4f), Vector3.one * 0.13f, bare, new Vector3(0, 30, 0));

            // 现有物品（prefab 实例；材质替换只作为本场景的实例覆盖）
            var map = new Dictionary<string, Material>
            {
                { "M_Comm_Housing", painted }, { "M_Comm_Metal", bare },
                { "M_Beacon_Metal", bare },
                { "M_Drone_Shell", ceramic }, { "M_Drone_Metal", bare },
                { "M_plastic_grey", ceramic }, { "M_metal_light", painted }, { "M_metal_dark", bare },
            };
            var items = new GameObject("Items").transform;
            PlaceItem(items, "Assets/BorderRepair/Prefabs/Items/Item_Communicator_Final.prefab", new Vector3(-0.45f, 0.083f, -0.15f), map);
            PlaceItem(items, "Assets/BorderRepair/Prefabs/Items/Item_NavBeacon_Final.prefab", new Vector3(-0.1f, 0.105f, -0.15f), map);
            PlaceItem(items, "Assets/BorderRepair/Prefabs/Items/Item_SalvageDrone_Final.prefab", new Vector3(0.3f, 0.064f, -0.15f), map);
            PlaceItem(items, NarrativeSliceBuilder.PrefabPath, new Vector3(0.78f, 0.08f, -0.15f), map);

            var settings = new GameObject("DiagnosticHighlightSettings").AddComponent<DiagnosticHighlightSettings>();
            settings.Configure(AssetDatabase.LoadAssetAtPath<DiagnosticHighlightProfile>(ProfilePath));

            // 说明文字
            var canvasGo = new GameObject("LabUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var textGo = new GameObject("Status", typeof(RectTransform));
            textGo.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)textGo.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(20, -16);
            rt.sizeDelta = new Vector2(1100, 110);
            var text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 22;
            text.color = new Color(0.9f, 0.92f, 0.95f);
            text.supportRichText = true;
            text.raycastTarget = false;

            var interaction = camGo.AddComponent<ShaderLabInteraction>();
            var so = new SerializedObject(interaction);
            so.FindProperty("labCamera").objectReferenceValue = cam;
            so.FindProperty("settings").objectReferenceValue = settings;
            so.FindProperty("statusText").objectReferenceValue = text;
            so.FindProperty("orbitTarget").vector3Value = OrbitTarget;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("场景保存失败：" + ScenePath);
        }

        static void PlaceItem(Transform parent, string prefabPath, Vector3 position, Dictionary<string, Material> map)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { Debug.LogWarning("[BorderRepair] ShaderLab 缺少物品 prefab：" + prefabPath); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.position = position;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && map.TryGetValue(mats[i].name, out var m) && m != null) { mats[i] = m; changed = true; }
                if (changed) r.sharedMaterials = mats;       // 场景实例覆盖，不写回 prefab
            }
        }

        static GameObject Prim(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.eulerAngles = euler;
            go.transform.localScale = scale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
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
