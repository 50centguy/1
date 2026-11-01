using System;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.Art.UnifiedClinic.EditorTools
{
    /// <summary>Hand-tunable post-processing numbers for the clinic. Editor-only: the values are written into the
    /// Room / Inspection volume profiles, which are what the scenes and player builds actually reference.</summary>
    public sealed class RetroGradingSettings : ScriptableObject
    {
        public const string AssetPath = ClinicRetroLook.Root + "/RetroGradingSettings.asset";

        [Serializable]
        public sealed class Grading
        {
            [Header("曝光")]
            [Tooltip("整体曝光（EV）。每 +1 亮度翻倍，每 -1 减半；0 = 不增不减。觉得画面偏亮先调这里。")]
            [Range(-3f, 3f)] public float postExposure;

            [Header("泛光")]
            [Tooltip("泛光强度。越大，灯具和屏幕周围的光晕越明显；0 = 没有泛光。")]
            [Min(0f)] public float bloomIntensity;
            [Tooltip("泛光阈值。亮度超过这个值才发光；调低后更多物体会发光，画面也会显得更亮。")]
            [Min(0f)] public float bloomThreshold;
            [Tooltip("泛光扩散范围。越大，光晕铺得越开越柔和；越小越贴近光源。")]
            [Range(0f, 1f)] public float bloomScatter;
            [Tooltip("泛光亮度上限。限制极亮像素的贡献，防止个别高光炸开。")]
            [Min(0f)] public float bloomClamp;

            [Header("暗角")]
            [Tooltip("暗角强度。越大，画面四周压得越暗；0 = 没有暗角。")]
            [Range(0f, 1f)] public float vignetteIntensity;

            [Header("颗粒")]
            [Tooltip("胶片颗粒强度。越大噪点越明显；近景要读屏幕和标签，建议保持很低。")]
            [Range(0f, 1f)] public float grainIntensity;

            public static Grading RoomDefaults() => new Grading
            {
                postExposure = 0f, bloomIntensity = .24f, bloomThreshold = 1.05f, bloomScatter = .63f, bloomClamp = 12f,
                vignetteIntensity = .13f, grainIntensity = .028f,
            };

            public static Grading InspectionDefaults() => new Grading
            {
                postExposure = .10f, bloomIntensity = .10f, bloomThreshold = 1.05f, bloomScatter = .63f, bloomClamp = 12f,
                vignetteIntensity = .07f, grainIntensity = .008f,
            };

            /// <summary>The values that were hard-coded before this asset existed; used when the asset is missing.</summary>
            public static Grading Fallback(bool inspection)
            {
                var values = inspection ? InspectionDefaults() : RoomDefaults();
                values.postExposure = inspection ? .10f : 0f;
                return values;
            }

            internal Grading WithInvalidFieldsFrom(Grading fallback)
            {
                float Pick(float value, float other) => float.IsNaN(value) || float.IsInfinity(value) ? other : value;
                return new Grading
                {
                    postExposure = Pick(postExposure, fallback.postExposure),
                    bloomIntensity = Pick(bloomIntensity, fallback.bloomIntensity),
                    bloomThreshold = Pick(bloomThreshold, fallback.bloomThreshold),
                    bloomScatter = Pick(bloomScatter, fallback.bloomScatter),
                    bloomClamp = Pick(bloomClamp, fallback.bloomClamp),
                    vignetteIntensity = Pick(vignetteIntensity, fallback.vignetteIntensity),
                    grainIntensity = Pick(grainIntensity, fallback.grainIntensity),
                };
            }
        }

        [Header("房间（平时走动的视角）")]
        public Grading room = Grading.RoomDefaults();

        [Header("近景检查（读屏幕、读标签，略亮一点保证可读）")]
        public Grading inspection = Grading.InspectionDefaults();

        public void ResetToDefaults()
        {
            room = Grading.RoomDefaults();
            inspection = Grading.InspectionDefaults();
        }

        /// <summary>Safe to call with a null asset or broken fields: anything unusable resolves to <see cref="Grading.Fallback"/>.</summary>
        public static Grading Resolve(RetroGradingSettings settings, bool inspection)
        {
            var fallback = Grading.Fallback(inspection);
            var values = settings == null ? null : inspection ? settings.inspection : settings.room;
            return values == null ? fallback : values.WithInvalidFieldsFrom(fallback);
        }

        /// <summary>Returns null (with a warning) when the asset is missing; never creates it.</summary>
        public static RetroGradingSettings Load()
        {
            var settings = AssetDatabase.LoadAssetAtPath<RetroGradingSettings>(AssetPath);
            if (settings == null)
                Debug.LogWarning("[RetroClinic] " + AssetPath + " 不存在，后处理回退到代码内置值（房间 +0.00 EV / 近景 +0.10 EV）。" +
                                 "用菜单 Border Repair/Clinic Base/Retro Look/Select Grading Settings 重新创建。");
            return settings;
        }

        [MenuItem("Border Repair/Clinic Base/Retro Look/Select Grading Settings", priority = 2002)]
        public static void SelectOrCreate()
        {
            var settings = AssetDatabase.LoadAssetAtPath<RetroGradingSettings>(AssetPath);
            if (settings == null)
            {
                settings = CreateInstance<RetroGradingSettings>();
                AssetDatabase.CreateAsset(settings, AssetPath);
                ClinicRetroLook.ApplyGradingToProfiles(settings);
                AssetDatabase.SaveAssets();
            }
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }
    }

    /// <summary>Inspector edits go straight into the profile assets, so a value change shows up in every clinic
    /// scene (Game view included, during Play) without re-applying the look.</summary>
    [CustomEditor(typeof(RetroGradingSettings))]
    sealed class RetroGradingSettingsEditor : UnityEditor.Editor
    {
        void OnEnable()
        {
            serializedObject.FindProperty(nameof(RetroGradingSettings.room)).isExpanded = true;
            serializedObject.FindProperty(nameof(RetroGradingSettings.inspection)).isExpanded = true;
            Undo.undoRedoPerformed += PushToProfiles;
        }

        void OnDisable() => Undo.undoRedoPerformed -= PushToProfiles;

        void PushToProfiles()
        {
            if (target != null) ClinicRetroLook.ApplyGradingToProfiles((RetroGradingSettings)target);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            if (serializedObject.ApplyModifiedProperties()) PushToProfiles();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("改动会立刻写入 Room_Profile / Inspection_Profile，三个诊所场景共用这两个文件。\n" +
                                    "这里的字段以本资产为准：直接改 Profile 里的同名数值，下次 Apply 或在这里改动时会被覆盖。", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset to defaults"))
                {
                    Undo.RecordObject(target, "Reset Retro Grading Settings");
                    ((RetroGradingSettings)target).ResetToDefaults();
                    EditorUtility.SetDirty(target);
                    serializedObject.Update();
                    PushToProfiles();
                }
                if (GUILayout.Button("写入 Profile 并保存"))
                {
                    PushToProfiles();
                    AssetDatabase.SaveAssets();
                }
            }
        }
    }
}
