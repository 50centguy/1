using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.FirstOrder.EditorTools.LayoutAB;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BorderRepair.FirstOrder.EditorTools.FaultArtQA
{
    /// <summary>诊断：反射探针有没有真的作用到轴承 / 风扇上（Play 模式，写日志后退出）。</summary>
    [InitializeOnLoad]
    public static class FaultArtProbeDiag
    {
        const string Key = "FaultArtProbeDiag";
        static FaultArtProbeDiag()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (!SessionState.GetBool(Key, false)) return;
                if (s == PlayModeStateChange.EnteredPlayMode) new GameObject("Diag").AddComponent<Host>();
                if (s == PlayModeStateChange.EnteredEditMode) { SessionState.SetBool(Key, false); EditorApplication.Exit(0); }
            };
        }

        public static void Begin()
        {
            SessionState.SetBool(Key, true);
            EditorSceneManager.OpenScene(LayoutABScenes.SceneB, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        class Host : MonoBehaviour
        {
            IEnumerator Start()
            {
                for (int i = 0; i < 20; i++) yield return null;
                var fix = Object.FindFirstObjectByType<FaultArtLookFix>();
                Debug.Log($"[FaultArtQA] 默认反射：mode {RenderSettings.defaultReflectionMode}，skybox {(RenderSettings.skybox != null ? RenderSettings.skybox.name : "无")}，customReflection {(RenderSettings.customReflectionTexture != null ? RenderSettings.customReflectionTexture.name : "无")}");
                foreach (var p in fix.probes) Debug.Log($"[FaultArtQA] 探针 {p.name} enabled {p.enabled} active {p.gameObject.activeInHierarchy} mode {p.mode} texture {(p.texture != null ? p.texture.name + " " + p.texture.width : "null")} realtimeTexture {(p.realtimeTexture != null ? "有" : "null")} bounds {p.bounds}");
                var names = new[] { "UNIT07_FK_BearingWorn", "Engine_Fan_L", "Engine_UpperCover_L" };
                foreach (var on in new[] { true, false, true })
                {
                    fix.Apply(on);
                    yield return null; yield return null;
                    foreach (var n in names)
                    {
                        var r = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == n);
                        if (r == null) continue;
                        var list = new List<ReflectionProbeBlendInfo>();
                        r.GetClosestReflectionProbes(list);
                        Debug.Log($"[FaultArtQA] 调整{(on ? "开" : "关")} {n}：probeUsage {r.reflectionProbeUsage}，用到的探针 {string.Join(", ", list.Select(b => b.probe.name + " w" + b.weight.ToString("F2")))}");
                    }
                }
                EditorApplication.ExitPlaymode();
            }
        }
    }
}
