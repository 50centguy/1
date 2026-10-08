using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.TwoNight.Tests
{
    /// <summary>
    /// 统一诊所观感采集（运行时、真实总控和第一人称，房间相机含后处理）：在玩家眼高从几个站位拍图并统计亮度，
    /// 另拍维修台上维修的画面。只生成截图和数据，不做断言。Explicit：只有用 -testFilter 点名时才运行。
    /// 输出 Logs/look/&lt;tag&gt;_*.png 与 Logs/look/look_&lt;tag&gt;.txt（tag 取环境变量 CLINIC_LOOK_TAG，默认 now）。
    /// </summary>
    public class UnifiedClinicLookCapture
    {
        static readonly (string name, Vector3 feet, Vector3 look)[] Views =
        {
            ("entrance", new Vector3(0f, .05f, -2.25f), new Vector3(0f, 1.1f, 1.5f)),
            ("surgery", new Vector3(1.0f, .05f, -1.2f), new Vector3(-0.2f, 0.9f, 0f)),
            ("dock", new Vector3(-1.95f, .05f, 0f), new Vector3(-2.73f, 1.1f, 0f)),
            ("workbench", new Vector3(0f, .05f, 1.95f), new Vector3(0f, 1.0f, 2.9f)),
            ("trade_customer", new Vector3(1.35f, .05f, 0f), new Vector3(1.9f, 1.05f, 0f)),
            ("back_left", new Vector3(1.0f, .05f, 2.0f), new Vector3(-2.5f, 1.0f, -2.5f)),
            ("back_right", new Vector3(-1.0f, .05f, -2.0f), new Vector3(2.2f, 1.0f, 2.8f)),
        };

        [UnityTest, Explicit("仅用于生成观感截图")]
        public IEnumerator CaptureRoomLook()
        {
            string tag = Environment.GetEnvironmentVariable("CLINIC_LOOK_TAG") ?? "now";
            TwoNightRun.NewGame(null, true);
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            for (int i = 0; i < 10; i++) yield return null;
            var clinic = UnityEngine.Object.FindFirstObjectByType<UnifiedClinicDirector>();
            var rig = clinic.Robot.Flow.Rig;
            var walker = rig.Walker;
            walker.LeaveSystemCursor = true;
            rig.Walk(true);
            Directory.CreateDirectory("Logs/look");
            var sb = new StringBuilder("view | mean lum | p5 | p50 | p95 | <0.03 near black | >0.97 clipped\n");
            foreach (var (name, feet, look) in Views)
            {
                walker.PlaceAt(feet, 0f);
                yield return null;
                walker.LookAt(look);
                for (int i = 0; i < 4; i++) yield return null;
                sb.AppendLine(Capture(rig.Cam, $"{tag}_{name}", name));
            }
            // 维修台上维修
            Assert.IsTrue(clinic.Interact(ClinicTradeAction.Receive));
            if (clinic.Counter.DialogueVisible) clinic.Counter.DismissDialogue();
            Assert.IsTrue(clinic.Interact(ClinicTradeAction.Console));
            double until = Time.realtimeSinceStartupAsDouble + .8; while (Time.realtimeSinceStartupAsDouble < until) yield return null;
            sb.AppendLine(Capture(clinic.Station.Inspector.ViewCamera, $"{tag}_bench_repair", "bench_repair"));
            File.WriteAllText($"Logs/look/look_{tag}.txt", sb.ToString(), new UTF8Encoding(false));
            TwoNightRun.Clear();
        }

        static string Capture(Camera cam, string file, string label)
        {
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var prev = cam.targetTexture;
            try
            {
                cam.targetTexture = rt; cam.Render(); cam.targetTexture = prev;
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes($"Logs/look/{file}.png", tex.EncodeToPNG());
                var lum = tex.GetPixels().Select(c => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b).OrderBy(x => x).ToArray();
                float Q(float q) => lum[Mathf.Clamp((int)(q * lum.Length), 0, lum.Length - 1)];
                return $"{label} | {lum.Average():F3} | {Q(.05f):F3} | {Q(.5f):F3} | {Q(.95f):F3} | {lum.Count(x => x < .03f) * 100f / lum.Length:F1}% | {lum.Count(x => x > .97f) * 100f / lum.Length:F1}%";
            }
            finally { cam.targetTexture = prev; rt.Release(); UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(tex); }
        }
    }
}
