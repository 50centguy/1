using System.IO;
using System.Linq;
using BorderRepair.Narrative;
using BorderRepair.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.Tests
{
    /// <summary>工人义手打磨：只加在叙事场景里；默认场景不含任何打磨组件；封条印字可读且不透露真相。</summary>
    public class WorkerHandPolishAssetTests
    {
        const string DefaultScene = "Assets/BorderRepair/Scenes/RepairStation_Prototype.unity";
        const string NarrativeScene = "Assets/BorderRepair/Scenes/Narrative_WorkerHand.unity";
        const string PrefabPath = "Assets/BorderRepair/Prefabs/Items/Narrative/Item_WorkerProsthetic_Placeholder.prefab";

        static string ScriptGuid<T>() where T : MonoBehaviour
        {
            var guid = AssetDatabase.FindAssets($"{typeof(T).Name} t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileNameWithoutExtension(p) == typeof(T).Name)
                .Select(AssetDatabase.AssetPathToGUID).FirstOrDefault();
            Assert.IsNotNull(guid, $"找不到脚本 {typeof(T).Name}");
            return guid;
        }

        static string[] PolishGuids() => new[]
        {
            ScriptGuid<NarrativeStageFraming>(), ScriptGuid<RepairFeedbackFx>(), ScriptGuid<NarrativeHud>(),
            ScriptGuid<ItemMaterialSkin>(), ScriptGuid<CollapsiblePanel>(),
        };

        [Test]
        public void DefaultSceneHasNoPolishComponents()
        {
            string text = File.ReadAllText(DefaultScene);
            foreach (var g in PolishGuids()) StringAssert.DoesNotContain(g, text, "默认场景不应包含叙事打磨组件");
            StringAssert.DoesNotContain("NarrativePolish", text);
        }

        [Test]
        public void NarrativeSceneHasEveryPolishComponent()
        {
            string text = File.ReadAllText(NarrativeScene);
            foreach (var g in PolishGuids()) StringAssert.Contains(g, text, "叙事场景缺少打磨组件（Border Repair > Narrative > Apply Worker Hand Polish）");
        }

        [Test]
        public void SealHasLitLabelQuadsForIntactAndTorn()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab);
            var seal = prefab.GetComponentsInChildren<Transform>(true).First(t => t.name == "Point_lease_seal");
            foreach (var state in new[] { "Intact", "Torn" })
            {
                var label = seal.Find(state + "/SealLabel");
                Assert.IsNotNull(label, $"封条 {state} 缺少印字");
                Assert.IsNull(label.GetComponent<TextMesh>(), "印字不能用 TextMesh（内置文字 shader 会透过其他物体显示）");
                Assert.IsNull(label.GetComponent<Collider>(), "印字面片不应挡住封条本身的拾取");
                var mr = label.GetComponent<MeshRenderer>();
                Assert.IsNotNull(mr);
                Assert.AreEqual("Universal Render Pipeline/Lit", mr.sharedMaterial.shader.name, "印字应接受场景光照");
                Assert.IsNotNull(mr.sharedMaterial.GetTexture("_BaseMap"), "印字材质缺少文字贴图");
            }
            var torn = seal.Find("Torn/SealLabel").GetComponent<MeshRenderer>().sharedMaterial;
            Assert.Less(torn.GetTextureScale("_BaseMap").x, 0.5f, "撕开的左半边只显示印字左侧");
            var bar = seal.Find("Intact/SealText");
            if (bar != null) Assert.IsFalse(bar.gameObject.activeSelf, "有了印字后旧的灰色文字条应隐藏");
        }

        [Test]
        public void PrefabKeepsPlaceholderMaterials()
        {
            // 磨损材质只在叙事场景里替换；prefab 本身仍引用原占位材质
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null) StringAssert.DoesNotStartWith("M_Worn_", m.name, $"{r.name} 在 prefab 里被换成了磨损材质");
        }

        [Test]
        public void EverySoundCueIsNonEmptyAndShort()
        {
            foreach (RepairSoundSynth.Cue cue in System.Enum.GetValues(typeof(RepairSoundSynth.Cue)))
            {
                if (cue == RepairSoundSynth.Cue.None) continue;
                var clip = RepairSoundSynth.Create(cue);
                Assert.IsNotNull(clip, cue.ToString());
                Assert.That(clip.length, Is.InRange(0.15f, 1.0f), $"{cue} 应是 1 秒以内的短音");
                var data = new float[clip.samples];
                clip.GetData(data, 0);
                Assert.Greater(data.Max(Mathf.Abs), 0.5f, $"{cue} 音量过小");
                Assert.LessOrEqual(data.Max(Mathf.Abs), 0.81f, $"{cue} 削波");
                Object.DestroyImmediate(clip);
            }
        }

        /// <summary>
        /// 四个主要操作的声音在时长、过零率（音高 / 噪声感）、起音次数上两两有明显差别。
        /// 这只是防止改动后变得雷同的粗检查；是否“好辨认”仍要人耳试听。
        /// </summary>
        [Test]
        public void MainOperationCuesDifferMeasurably()
        {
            var cues = new[] { RepairSoundSynth.Cue.Unscrew, RepairSoundSynth.Cue.OpenCover, RepairSoundSynth.Cue.ProbeWarning, RepairSoundSynth.Cue.DataRead };
            var features = cues.ToDictionary(c => c, c => Features(RepairSoundSynth.Create(c)));
            for (int i = 0; i < cues.Length; i++)
                for (int j = i + 1; j < cues.Length; j++)
                {
                    var a = features[cues[i]];
                    var b = features[cues[j]];
                    bool differs = Mathf.Abs(a.duration - b.duration) > 0.08f
                                   || Mathf.Abs(a.zcr - b.zcr) / Mathf.Max(a.zcr, b.zcr) > 0.3f
                                   || Mathf.Abs(a.onsets - b.onsets) >= 3;
                    Assert.IsTrue(differs, $"{cues[i]} 与 {cues[j]} 太接近：{a} / {b}");
                }
        }

        static (float duration, float zcr, int onsets) Features(AudioClip clip)
        {
            var d = new float[clip.samples];
            clip.GetData(d, 0);
            int crossings = 0, loud = 0;
            for (int i = 1; i < d.Length; i++)
            {
                if (Mathf.Abs(d[i]) < 0.02f) continue;
                loud++;
                if ((d[i] > 0) != (d[i - 1] > 0)) crossings++;
            }
            // 起音：5 ms 窗口的峰值从低于 0.1 升到 0.3 以上的次数
            int win = RepairSoundSynth.Rate / 200, onsets = 0;
            bool quiet = true;
            for (int s = 0; s + win <= d.Length; s += win)
            {
                float peak = 0f;
                for (int i = s; i < s + win; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
                if (quiet && peak > 0.3f) { onsets++; quiet = false; }
                else if (peak < 0.1f) quiet = true;
            }
            float result = clip.length;
            Object.DestroyImmediate(clip);
            return (result, loud > 0 ? crossings / (float)loud : 0f, onsets);
        }
    }
}
