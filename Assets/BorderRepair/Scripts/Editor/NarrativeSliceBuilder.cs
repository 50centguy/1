using System;
using System.Collections.Generic;
using System.IO;
using BorderRepair.Data;
using BorderRepair.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// 叙事竖切“工人义手”：生成占位 prefab、案件数据、独立营业数据，以及独立场景（从默认原型场景复制，
    /// 换成新的营业数据并加上维修工具栏）。默认原型场景、三件物品和它们的顺序都不改动。
    /// 默认只创建缺失的资产；Rebuild 才会覆盖本工具生成的资产。
    /// 命令行：-executeMethod BorderRepair.EditorTools.NarrativeSliceBuilder.BuildFromCommandLine
    /// </summary>
    public static class NarrativeSliceBuilder
    {
        public const string PrefabDir = "Assets/BorderRepair/Prefabs/Items/Narrative";
        public const string PrefabPath = PrefabDir + "/Item_WorkerProsthetic_Placeholder.prefab";
        public const string DataDir = "Assets/BorderRepair/Data/Narrative";
        public const string CasePath = DataDir + "/Case_N01_WorkerProsthetic.asset";
        public const string ShiftPath = DataDir + "/Shift_Narrative_WorkerHand.asset";
        public const string ScenePath = "Assets/BorderRepair/Scenes/Narrative_WorkerHand.unity";

        [MenuItem("Border Repair/Narrative/Build Worker Hand Slice (create missing)", priority = 60)]
        static void MenuBuild()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(false);
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Border Repair/Narrative/Rebuild Worker Hand Slice (overwrite generated)", priority = 61)]
        static void MenuRebuild()
        {
            if (!EditorUtility.DisplayDialog("重新生成叙事竖切",
                    "将覆盖“工人义手”的占位 prefab、案件数据、营业数据和场景，对它们的手动修改会丢失。\n默认原型场景和三件物品不受影响。\n\n继续吗？",
                    "覆盖", "取消")) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(true);
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Border Repair/Narrative/Open Worker Hand Scene", priority = 62)]
        static void MenuOpen()
        {
            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("叙事竖切", "场景尚未生成，请先执行 Border Repair > Narrative > Build Worker Hand Slice。", "好");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        public static void BuildFromCommandLine() => Build(false);
        public static void RebuildFromCommandLine() => Build(true);

        public static void Build(bool overwrite)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play 模式。");
            if (!File.Exists(PrototypeBuilder.ScenePath)) throw new FileNotFoundException("缺少默认原型场景，请先运行 Border Repair > Build Prototype", PrototypeBuilder.ScenePath);

            EnsureFolder(PrefabDir);
            EnsureFolder(DataDir);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null || overwrite)
            {
                var temp = WorkerHandPlaceholderFactory.Build();
                try
                {
                    prefab = PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath, out bool ok);
                    if (!ok) throw new IOException("prefab 保存失败：" + PrefabPath);
                }
                finally { Object.DestroyImmediate(temp); }
            }

            var caseData = AssetDatabase.LoadAssetAtPath<RepairCaseData>(CasePath);
            if (caseData == null)
            {
                caseData = ScriptableObject.CreateInstance<RepairCaseData>();
                WorkerHandCaseContent.Fill(caseData, prefab);
                AssetDatabase.CreateAsset(caseData, CasePath);
            }
            else if (overwrite)
            {
                WorkerHandCaseContent.Fill(caseData, prefab);
                EditorUtility.SetDirty(caseData);
            }

            var shift = AssetDatabase.LoadAssetAtPath<RepairShiftData>(ShiftPath);
            bool newShift = shift == null;
            if (newShift) shift = ScriptableObject.CreateInstance<RepairShiftData>();
            if (newShift || overwrite)
            {
                shift.shiftTitle = "边境维修站 · 夜班";
                shift.targetDurationSeconds = 480f;
                shift.cases = new List<RepairCaseData> { caseData };
            }
            if (newShift) AssetDatabase.CreateAsset(shift, ShiftPath);
            else EditorUtility.SetDirty(shift);
            AssetDatabase.SaveAssets();

            var errors = new List<string>();
            if (!caseData.Validate(errors)) throw new InvalidDataException("叙事案件数据校验失败：\n" + string.Join("\n", errors));

            if (overwrite || !File.Exists(ScenePath)) BuildScene();
            AssetDatabase.SaveAssets();
            Debug.Log($"[BorderRepair] 叙事竖切“工人义手”生成完成（overwrite={overwrite}）：{ScenePath}");
        }

        // ---------- 场景 ----------

        static void BuildScene()
        {
            if (File.Exists(ScenePath)) AssetDatabase.DeleteAsset(ScenePath);
            if (!AssetDatabase.CopyAsset(PrototypeBuilder.ScenePath, ScenePath)) throw new IOException("复制场景失败：" + ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // OpenScene 会卸载未被引用的资源，打开前持有的营业数据可能已失效，这里重新加载
            var shift = AssetDatabase.LoadAssetAtPath<RepairShiftData>(ShiftPath);
            if (shift == null) throw new FileNotFoundException("找不到叙事营业数据", ShiftPath);

            var controller = Object.FindFirstObjectByType<RepairStationController>();
            var view = Object.FindFirstObjectByType<RepairUIView>();
            if (controller == null || view == null) throw new InvalidOperationException("复制的场景里缺少 RepairStationController 或 RepairUIView");

            var toolbar = BuildToolbar(view.transform);
            var so = new SerializedObject(controller);
            so.FindProperty("shift").objectReferenceValue = shift;
            so.FindProperty("toolbar").objectReferenceValue = toolbar;
            so.ApplyModifiedPropertiesWithoutUndo();
            if (so.FindProperty("shift").objectReferenceValue == null) throw new InvalidOperationException("营业数据没有写入场景控制器");

            // 取景、反馈、面板收起、磨损材质与照明（只加在叙事场景里）
            WorkerHandPolishBuilder.ApplyToOpenScene(true);

            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("场景保存失败：" + ScenePath);
        }

        static readonly Color PanelColor = new Color(0.08f, 0.1f, 0.12f, 0.9f);
        static readonly Color TitleColor = new Color(1f, 0.72f, 0.3f);
        static readonly Color TextColor = new Color(0.87f, 0.9f, 0.93f);
        static readonly Color ButtonColor = new Color(0.24f, 0.28f, 0.33f);

        static RepairToolbarView BuildToolbar(Transform canvasRoot)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var container = Rect("RepairToolbar", canvasRoot);
            Place(container, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(20, 110), new Vector2(440, 380));
            var panel = Rect("Panel", container);
            Fill(panel, 0, 0, 0, 0);
            panel.gameObject.AddComponent<Image>().color = PanelColor;

            var title = Label(panel, "Title", "维修工具", 24, TextAnchor.MiddleLeft, FontStyle.Bold, TitleColor, font);
            Place(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(-36, 34));

            var buttons = Rect("Buttons", panel);
            Place(buttons, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -54), new Vector2(-32, 196));
            var layout = buttons.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var hand = MakeButton(buttons, "HandButton", "空手（扫描）", font);
            var fastener = MakeButton(buttons, "FastenerButton", "螺丝刀 · 卸下固定件", font);
            var housing = MakeButton(buttons, "HousingButton", "撬片 · 打开外壳", font);
            var module = MakeButton(buttons, "ModuleButton", "检测仪 · 检测 / 更换模块", font);

            var status = Label(panel, "Status", "", 18, TextAnchor.UpperLeft, FontStyle.Normal, TextColor, font);
            Fill(status.rectTransform, 18, 18, 262, 14);

            var view = container.gameObject.AddComponent<RepairToolbarView>();
            var so = new SerializedObject(view);
            so.FindProperty("panel").objectReferenceValue = panel.gameObject;
            so.FindProperty("handButton").objectReferenceValue = hand;
            so.FindProperty("fastenerButton").objectReferenceValue = fastener;
            so.FindProperty("housingButton").objectReferenceValue = housing;
            so.FindProperty("moduleButton").objectReferenceValue = module;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.ApplyModifiedPropertiesWithoutUndo();
            panel.gameObject.SetActive(false);
            return view;
        }

        static Button MakeButton(RectTransform parent, string name, string label, Font font)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = ButtonColor;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.normalColor = new Color(0.9f, 0.9f, 0.9f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.65f, 0.65f, 0.65f);
            colors.selectedColor = new Color(0.9f, 0.9f, 0.9f);
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.6f);
            b.colors = colors;
            rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
            var text = Label(rt, "Label", label, 19, TextAnchor.MiddleLeft, FontStyle.Bold, Color.white, font);
            Fill(text.rectTransform, 14, 10, 2, 2);
            return b;
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static Text Label(Transform parent, string name, string text, int size, TextAnchor align, FontStyle style, Color color, Font font)
        {
            var t = Rect(name, parent).gameObject.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.fontStyle = style;
            t.color = color;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.05f;
            t.raycastTarget = false;
            return t;
        }

        static void Place(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static void Fill(RectTransform rt, float left, float right, float top, float bottom)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
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
