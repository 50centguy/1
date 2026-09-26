using System;
using System.Collections.Generic;
using System.IO;
using BorderRepair.Narrative;
using BorderRepair.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Cue = BorderRepair.Narrative.RepairSoundSynth.Cue;
using Object = UnityEngine.Object;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// “工人义手”这一单的打磨（只改叙事场景和这件占位 prefab；默认原型场景、三件正式物品不动）：
    /// - prefab：封条加上可读的印字（原来是一根灰色条代表文字）；
    /// - 场景：分阶段取景、操作反馈（声音 + 视觉）、可收起的顾客说明 / 线索面板、紧凑的工具栏、
    ///   沿用 ShaderLab 的三种磨损材质（场景级替换，不改 prefab）、工作台照明与反射探针。
    /// 默认只补缺失的部分；overwrite 时重建本工具生成的对象（NarrativePolish 根物体、面板按钮等）。
    /// 命令行：-executeMethod BorderRepair.EditorTools.WorkerHandPolishBuilder.ApplyFromCommandLine（或 ReapplyFromCommandLine）
    /// </summary>
    public static class WorkerHandPolishBuilder
    {
        public const string RootName = "NarrativePolish";
        public const string RingMaterialPath = "Assets/BorderRepair/Art/Narrative/M_Fx_Ring.mat";
        const string WornDir = "Assets/BorderRepair/Art/ShaderLab/Materials";
        const string PlaceholderMatDir = "Assets/BorderRepair/Art/Materials";
        const string ToggleName = "CollapseToggle";

        [MenuItem("Border Repair/Narrative/Apply Worker Hand Polish (create missing)", priority = 63)]
        static void MenuApply()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Apply(false);
            EditorSceneManager.OpenScene(NarrativeSliceBuilder.ScenePath);
        }

        [MenuItem("Border Repair/Narrative/Reapply Worker Hand Polish (overwrite polish objects)", priority = 64)]
        static void MenuReapply()
        {
            if (!EditorUtility.DisplayDialog("重新应用打磨",
                    "将重建叙事场景里的 NarrativePolish 物体（取景、反馈、换材质、照明）以及面板收起按钮，对它们的手动修改会丢失。\n默认原型场景不受影响。\n\n继续吗？",
                    "重建", "取消")) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Apply(true);
            EditorSceneManager.OpenScene(NarrativeSliceBuilder.ScenePath);
        }

        public static void ApplyFromCommandLine() => Apply(false);
        public static void ReapplyFromCommandLine() => Apply(true);

        public static void Apply(bool overwrite)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play 模式。");
            if (!File.Exists(NarrativeSliceBuilder.ScenePath))
                throw new FileNotFoundException("叙事场景不存在，请先运行 Border Repair > Narrative > Build Worker Hand Slice", NarrativeSliceBuilder.ScenePath);

            EnsurePrefabDetails();
            var ring = EnsureRingMaterial();
            var scene = EditorSceneManager.OpenScene(NarrativeSliceBuilder.ScenePath, OpenSceneMode.Single);
            ApplyToOpenScene(overwrite, ring);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("场景保存失败：" + NarrativeSliceBuilder.ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BorderRepair] 工人义手打磨已应用（overwrite={overwrite}）");
        }

        // ---------- prefab：封条印字 ----------

        static void EnsurePrefabDetails()
        {
            var root = PrefabUtility.LoadPrefabContents(NarrativeSliceBuilder.PrefabPath);
            try
            {
                var seal = FindDeep(root.transform, "Point_lease_seal");
                if (seal == null) throw new InvalidDataException("prefab 中找不到 Point_lease_seal");
                var intact = seal.Find("Intact");
                var torn = seal.Find("Torn");
                bool changed = WorkerHandPlaceholderFactory.AddSealLabels(intact, torn);
                // 旧版用一根灰条代表文字：有了真正的印字后隐藏它（不删除，保留原结构）
                var bar = intact != null ? intact.Find("SealText") : null;
                if (bar != null && bar.gameObject.activeSelf) { bar.gameObject.SetActive(false); changed = true; }
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, NarrativeSliceBuilder.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static Material EnsureRingMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(RingMaterialPath);
            if (mat != null) return mat;
            var dir = Path.GetDirectoryName(RingMaterialPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Path.GetDirectoryName(dir).Replace('\\', '/'), Path.GetFileName(dir));
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("找不到 Sprites/Default shader");
            mat = new Material(shader) { name = "M_Fx_Ring" };
            AssetDatabase.CreateAsset(mat, RingMaterialPath);
            return mat;
        }

        // ---------- 场景 ----------

        /// <summary>对当前打开的叙事场景应用打磨（也被 NarrativeSliceBuilder 在重建场景时调用）。</summary>
        internal static void ApplyToOpenScene(bool overwrite, Material ring = null)
        {
            if (ring == null) ring = EnsureRingMaterial();
            var controller = Object.FindFirstObjectByType<RepairStationController>();
            var view = Object.FindFirstObjectByType<RepairUIView>();
            if (controller == null || view == null) throw new InvalidOperationException("叙事场景里缺少 RepairStationController 或 RepairUIView");

            var existing = GameObject.Find(RootName);
            if (existing != null && overwrite) { Object.DestroyImmediate(existing); existing = null; }
            var root = existing != null ? existing.transform : new GameObject(RootName).transform;

            var anchor = FindDeep(null, "ItemAnchor");
            if (anchor == null) throw new InvalidOperationException("场景里找不到 ItemAnchor");

            if (root.Find("BenchLighting") == null) BuildLighting(root, anchor.position);
            if (root.GetComponentInChildren<NarrativeStageFraming>(true) == null) BuildFraming(root, controller);
            if (root.GetComponentInChildren<RepairFeedbackFx>(true) == null) BuildFx(root, controller, ring);
            if (root.GetComponentInChildren<ItemMaterialSkin>(true) == null) BuildSkin(root, controller);
            if (root.GetComponentInChildren<NarrativeHud>(true) == null || overwrite) BuildHud(root, controller, view, overwrite);
            CompactToolbar(controller, overwrite);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        }

        static void BuildLighting(Transform root, Vector3 anchor)
        {
            var group = new GameObject("BenchLighting").transform;
            group.SetParent(root, false);

            // 主光：中性白的聚光，从左前上方照向检查位，让封条、螺丝、线路的明暗清楚（原场景的台灯偏暖，会把灰色零件染成橙色）
            var key = new GameObject("BenchKeySpot").AddComponent<Light>();
            key.transform.SetParent(group, false);
            key.type = LightType.Spot;
            key.color = new Color(1f, 0.98f, 0.95f);
            key.intensity = 1.5f;       // 旧白陶瓷很亮，再高会让舱内背景过曝、零件轮廓变弱
            key.range = 3f;
            key.spotAngle = 42f;
            key.innerSpotAngle = 26f;
            key.shadows = LightShadows.None;
            key.transform.position = anchor + new Vector3(-0.35f, 0.75f, -0.6f);
            key.transform.LookAt(anchor);

            // 补光：右前方偏冷的弱点光，照亮盖板下的舱内
            var fill = new GameObject("BenchFillLight").AddComponent<Light>();
            fill.transform.SetParent(group, false);
            fill.type = LightType.Point;
            fill.color = new Color(0.86f, 0.92f, 1f);
            fill.intensity = 0.5f;
            fill.range = 1.4f;
            fill.shadows = LightShadows.None;
            fill.transform.position = anchor + new Vector3(0.45f, 0.2f, -0.4f);

            // 反射探针：场景没有天空盒，金属度高的磨损材质（裸露金属螺丝）没有反射会发黑
            var probe = new GameObject("BenchReflectionProbe").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(group, false);
            probe.transform.position = anchor + new Vector3(0f, 0.1f, 0f);
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.size = new Vector3(3f, 2f, 3f);
            probe.resolution = 128;
            probe.intensity = 1f;

            // 叙事场景里把暖色台灯调暗一些（只在它仍是生成时的默认值时调整，保留手动修改）
            var lamp = FindDeep(null, "Lamp Light");
            var lampLight = lamp != null ? lamp.GetComponent<Light>() : null;
            if (lampLight != null && Mathf.Abs(lampLight.intensity - 1.2f) < 1e-3f) lampLight.intensity = 0.6f;
        }

        static void BuildFraming(Transform root, RepairStationController controller)
        {
            var go = new GameObject("StageFraming");
            go.transform.SetParent(root, false);
            var framing = go.AddComponent<NarrativeStageFraming>();
            framing.Configure(controller, DefaultShots(), 1.1f, 1.2f);
        }

        /// <summary>每个操作目标的取景。数值在叙事场景的 StageFraming 物体上可调。</summary>
        internal static List<NarrativeStageFraming.Shot> DefaultShots() => new List<NarrativeStageFraming.Shot>
        {
            // fill 取得偏小：零件在画面中央，同时保留周围的外壳作参照（知道自己在看哪里）
            // 卸螺丝 A：连同封条一起框住（卸下时封条会撕开）
            Shot("fastener_a", new[] { "fastener_a", "lease_seal" }, 0f, 0f, 0.26f, 0.03f),
            Shot("fastener_b", new[] { "fastener_b" }, 0f, 0f, 0.22f, 0.03f),
            Shot("shell", new[] { "shell" }, 0f, 0f, 0.9f, 0.05f),
            Shot("force_limiter", new[] { "force_limiter" }, 0f, 0f, 0.26f, 0.035f),
            Shot("drive", new[] { "drive" }, 0f, 0f, 0.36f, 0.035f),
            Shot("control_board", new[] { "control_board" }, 0f, 0f, 0.36f, 0.035f),
            // 数据接口在手腕顶面：把物品向镜头俯一些，让接口和插上的检测线朝向镜头
            Shot("data_port", new[] { "data_port" }, 0f, -35f, 0.26f, 0.035f),
        };

        static NarrativeStageFraming.Shot Shot(string id, string[] frame, float yaw, float pitch, float fill, float minRadius) =>
            new NarrativeStageFraming.Shot { pointId = id, frameIds = frame, yaw = yaw, pitch = pitch, fill = fill, minRadius = minRadius };

        static void BuildFx(Transform root, RepairStationController controller, Material ring)
        {
            var go = new GameObject("RepairFeedbackFx");
            go.transform.SetParent(root, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 1f;
            var fx = go.AddComponent<RepairFeedbackFx>();
            fx.Configure(controller, source, ring, new List<RepairFeedbackFx.StepCue>
            {
                new RepairFeedbackFx.StepCue { stepId = "remove_fastener_a", cue = Cue.Unscrew },
                new RepairFeedbackFx.StepCue { stepId = "remove_fastener_b", cue = Cue.Unscrew },
                new RepairFeedbackFx.StepCue { stepId = "open_housing", cue = Cue.OpenCover },
                new RepairFeedbackFx.StepCue { stepId = "test_limiter", cue = Cue.ProbeWarning },
                new RepairFeedbackFx.StepCue { stepId = "read_log", cue = Cue.DataRead },
            });
        }

        static void BuildSkin(Transform root, RepairStationController controller)
        {
            var go = new GameObject("ItemMaterialSkin");
            go.transform.SetParent(root, false);
            var skin = go.AddComponent<ItemMaterialSkin>();
            // 与 ShaderLab 场景里对这只手的对应关系相同：浅灰塑料 → 旧白陶瓷，浅色金属盖板 → 烤漆金属，深色金属（螺丝等）→ 裸露金属
            skin.Configure(controller, new List<ItemMaterialSkin.Swap>
            {
                SwapOf("plastic_grey", "M_Worn_OldCeramic"),
                SwapOf("metal_light", "M_Worn_PaintedMetal"),
                SwapOf("metal_dark", "M_Worn_BareMetal"),
            });
        }

        static ItemMaterialSkin.Swap SwapOf(string placeholderKey, string worn)
        {
            var from = AssetDatabase.LoadAssetAtPath<Material>($"{PlaceholderMatDir}/M_{placeholderKey}.mat");
            var to = AssetDatabase.LoadAssetAtPath<Material>($"{WornDir}/{worn}.mat");
            if (from == null || to == null)
                throw new FileNotFoundException($"缺少材质 M_{placeholderKey} 或 {worn}（磨损材质来自 Border Repair > ShaderLab > Build）");
            return new ItemMaterialSkin.Swap { from = from, to = to };
        }

        // ---------- 界面 ----------

        static void BuildHud(Transform root, RepairStationController controller, RepairUIView view, bool overwrite)
        {
            var canvas = view.transform;
            var customer = canvas.Find("CustomerPanel") as RectTransform;
            var findings = canvas.Find("FindingsPanel") as RectTransform;
            if (customer == null || findings == null) throw new InvalidOperationException("场景界面里找不到 CustomerPanel / FindingsPanel");

            var customerCollapse = MakeCollapsible(customer, 400f, 58f, new[] { "CustomerText" }, "收起 ▴", "展开 ▾", overwrite);
            var clueCollapse = MakeCollapsible(findings, 400f, 118f, new[] { "FindingsText" }, "收起 ▴ [Tab]", "查看线索 ▾ [Tab]", overwrite);
            var title = findings.Find("Title");
            if (title != null && title.GetComponent<Text>() != null) title.GetComponent<Text>().text = "线索记录";

            // 底部提示加一块半透明底：镜头拉近后，零件盘的黄色边缘会落在提示文字后面
            var hint = canvas.Find("Hint") as RectTransform;
            var oldBacking = canvas.Find("HintBacking");
            if (oldBacking != null && overwrite) { Object.DestroyImmediate(oldBacking.gameObject); oldBacking = null; }
            if (hint != null && oldBacking == null)
            {
                var backing = new GameObject("HintBacking", typeof(RectTransform)).GetComponent<RectTransform>();
                backing.gameObject.layer = hint.gameObject.layer;
                backing.SetParent(canvas, false);
                backing.SetSiblingIndex(hint.GetSiblingIndex());
                backing.anchorMin = hint.anchorMin;
                backing.anchorMax = hint.anchorMax;
                backing.pivot = hint.pivot;
                backing.anchoredPosition = hint.anchoredPosition;
                backing.sizeDelta = new Vector2(620, hint.sizeDelta.y);
                var img = backing.gameObject.AddComponent<Image>();
                img.color = new Color(0.05f, 0.06f, 0.08f, 0.85f);
                img.raycastTarget = false;
            }

            var existingHud = root.GetComponentInChildren<NarrativeHud>(true);
            if (existingHud != null) Object.DestroyImmediate(existingHud.gameObject);
            var go = new GameObject("NarrativeHud");
            go.transform.SetParent(root, false);
            go.AddComponent<NarrativeHud>().Configure(controller, customerCollapse, clueCollapse);
        }

        static CollapsiblePanel MakeCollapsible(RectTransform panel, float expanded, float collapsed, string[] bodyNames,
                                                string expandedLabel, string collapsedLabel, bool overwrite)
        {
            var old = panel.GetComponent<CollapsiblePanel>();
            var oldToggle = panel.Find(ToggleName);
            if (old != null && oldToggle != null && !overwrite) return old;
            if (old != null) Object.DestroyImmediate(old);
            if (oldToggle != null) Object.DestroyImmediate(oldToggle.gameObject);

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var rt = new GameObject(ToggleName, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.gameObject.layer = LayerMask.NameToLayer("UI");
            rt.SetParent(panel, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-12, -12);
            rt.sizeDelta = new Vector2(190, 36);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0.24f, 0.28f, 0.33f, 0.95f);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var label = new GameObject("Label", typeof(RectTransform)).AddComponent<Text>();
            label.gameObject.layer = rt.gameObject.layer;
            label.rectTransform.SetParent(rt, false);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(8, 2);
            label.rectTransform.offsetMax = new Vector2(-8, -2);
            label.font = font;
            label.fontSize = 17;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.supportRichText = true;
            label.raycastTarget = false;
            label.text = expandedLabel;

            var body = new List<GameObject>();
            foreach (var n in bodyNames)
            {
                var t = panel.Find(n);
                if (t != null) body.Add(t.gameObject);
            }
            var c = panel.gameObject.AddComponent<CollapsiblePanel>();
            c.Configure(panel, expanded, collapsed, body.ToArray(), button, label, expandedLabel, collapsedLabel);
            return c;
        }

        /// <summary>叙事场景的工具栏改得更窄、更矮，让出画面左侧；按钮文字缩短。</summary>
        static void CompactToolbar(RepairStationController controller, bool overwrite)
        {
            var so = new SerializedObject(controller);
            var toolbar = so.FindProperty("toolbar").objectReferenceValue as RepairToolbarView;
            if (toolbar == null) return;
            var container = (RectTransform)toolbar.transform;
            if (!overwrite && Mathf.Abs(container.sizeDelta.x - 440f) > 0.5f) return;   // 已经调整过（或被手动改过）

            container.sizeDelta = new Vector2(300, 350);
            var panel = container.Find("Panel");
            var buttons = panel != null ? panel.Find("Buttons") as RectTransform : null;
            if (buttons != null)
            {
                buttons.anchoredPosition = new Vector2(0, -50);
                buttons.sizeDelta = new Vector2(-24, 178);
                var layout = buttons.GetComponent<VerticalLayoutGroup>();
                if (layout != null) layout.spacing = 6;
                string[] labels = { "空手 · 扫描", "螺丝刀 · 卸螺丝", "撬片 · 开盖", "检测仪 · 检测 / 读取" };
                for (int i = 0; i < buttons.childCount && i < labels.Length; i++)
                {
                    var b = buttons.GetChild(i);
                    var le = b.GetComponent<LayoutElement>();
                    if (le != null) le.preferredHeight = 40;
                    var text = b.GetComponentInChildren<Text>(true);
                    if (text != null) { text.text = labels[i]; text.fontSize = 18; }
                }
            }
            var status = panel != null ? panel.Find("Status") as RectTransform : null;
            if (status != null)
            {
                status.offsetMin = new Vector2(14, 12);
                status.offsetMax = new Vector2(-12, -240);
                var t = status.GetComponent<Text>();
                if (t != null) t.fontSize = 16;
            }
            var title = panel != null ? panel.Find("Title") as RectTransform : null;
            if (title != null) title.sizeDelta = new Vector2(-28, 34);
        }

        static Transform FindDeep(Transform parent, string name)
        {
            if (parent == null)
            {
                foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                {
                    if (go.name == name) return go.transform;
                    var t = FindDeep(go.transform, name);
                    if (t != null) return t;
                }
                return null;
            }
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                var t = FindDeep(child, name);
                if (t != null) return t;
            }
            return null;
        }
    }
}
