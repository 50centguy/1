using System.Collections;
using System.Linq;
using BorderRepair.Tools;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BorderRepair.Tests
{
    static class TesterScreenUtil
    {
        /// <summary>
        /// 测试用近景相机：正对检测仪屏幕，只渲染到给定的 RenderTexture，不影响游戏相机。
        /// 屏幕网格不可读，所以用网格包围盒最薄的轴作法线，朝向远离检测仪机身的一侧。
        /// </summary>
        public static Camera CloseUp(TesterReadout tester, RenderTexture rt, float fill = 0.8f)
        {
            var screen = tester.Screen;
            var size = screen.GetComponent<MeshFilter>().sharedMesh.bounds.size;
            var axis = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
            var n = screen.transform.TransformDirection(axis).normalized;
            var body = tester.GetComponentsInChildren<Renderer>().Where(r => r != screen).Select(r => r.bounds.center).Aggregate(Vector3.zero, (a, c) => a + c)
                       / Mathf.Max(1, tester.GetComponentsInChildren<Renderer>().Length - 1);
            var b = screen.bounds;
            if (Vector3.Dot(n, b.center - body) < 0f) n = -n;
            var go = new GameObject("Test_TesterScreenCloseUp");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.targetTexture = rt;
            cam.fieldOfView = 20f;
            cam.nearClipPlane = 0.005f;
            cam.farClipPlane = 5f;
            float half = Mathf.Max(b.extents.x, b.extents.z, b.extents.y) / fill;
            float dist = half / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);   // 最长边按竖直视野放下，宽度方向留有余量
            go.transform.SetPositionAndRotation(b.center + n * dist, Quaternion.LookRotation(-n, Vector3.up));
            return cam;
        }

        public static Color32[] Grab(Camera cam)
        {
            cam.Render();
            var rt = cam.targetTexture;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            Object.Destroy(tex);
            return px;
        }

        public static RectInt Clamp(Rect r, int w, int h, float inset = 0f)
        {
            r = Rect.MinMaxRect(r.xMin + r.width * inset, r.yMin + r.height * inset, r.xMax - r.width * inset, r.yMax - r.height * inset);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(r.xMin), 0, w), y0 = Mathf.Clamp(Mathf.FloorToInt(r.yMin), 0, h);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(r.xMax), 0, w), y1 = Mathf.Clamp(Mathf.CeilToInt(r.yMax), 0, h);
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        /// <summary>矩形内最亮 5% 像素（字）的平均颜色。</summary>
        public static Color InkColor(Color32[] px, int width, RectInt r)
        {
            var list = new System.Collections.Generic.List<Color32>();
            for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++) list.Add(px[y * width + x]);
            var top = list.OrderByDescending(c => Mathf.Max(c.r, Mathf.Max(c.g, c.b))).Take(Mathf.Max(1, list.Count / 20)).ToArray();
            return new Color((float)top.Average(c => c.r) / 255f, (float)top.Average(c => c.g) / 255f, (float)top.Average(c => c.b) / 255f);
        }

        /// <summary>矩形内有差异的像素数（任一通道差值 &gt; tolerance）与最大通道差值。</summary>
        public static (int changed, int total, int maxDiff) Diff(Color32[] a, Color32[] b, int width, RectInt r, int tolerance = 0)
        {
            int changed = 0, total = 0, maxDiff = 0;
            for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++)
                {
                    var p = a[y * width + x]; var q = b[y * width + x];
                    int d = Mathf.Max(Mathf.Abs(p.r - q.r), Mathf.Max(Mathf.Abs(p.g - q.g), Mathf.Abs(p.b - q.b)));
                    total++;
                    maxDiff = Mathf.Max(maxDiff, d);
                    if (d > tolerance) changed++;
                }
            return (changed, total, maxDiff);
        }
    }

    /// <summary>
    /// 检测仪屏幕 shader 预设：跟随读数换预设、可关闭回原材质、不改材质资产；
    /// 切换后只短暂跳变，稳定后画面与时间无关（逐像素一致）。
    /// </summary>
    public class TesterScreenPlayModeTests
    {
        static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");

        TesterReadout tester;
        RenderTexture rt;
        Camera cam;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return WorkerHandTestUtil.Load(WorkerHandTestUtil.NarrativeScene, _ => { });
            tester = Object.FindFirstObjectByType<TesterReadout>();
            Assert.IsNotNull(tester, "叙事场景应有检测仪");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (cam != null) Object.Destroy(cam.gameObject);
            if (rt != null) rt.Release();
            yield return null;
        }

        [UnityTest]
        public IEnumerator PresetsFollowTheReadingAndCanBeSwitchedOff()
        {
            rt = new RenderTexture(960, 540, 24);
            cam = TesterScreenUtil.CloseUp(tester, rt);
            yield return null;
            Assert.IsTrue(tester.PresetsActive, "检测仪应已接上三个屏幕预设");
            var presets = tester.Presets.ToArray();
            var inks = presets.Select(m => m.GetColor("_InkColor")).ToArray();
            var screen = tester.Screen;
            var block = new MaterialPropertyBlock();

            foreach (var reading in new[] { TesterReadout.Reading.Bypass, TesterReadout.Reading.Log, TesterReadout.Reading.Ready })
            {
                tester.Show(reading);
                Assert.AreSame(presets[(int)reading], screen.sharedMaterial, $"{reading} 应换成对应预设");
                screen.GetPropertyBlock(block);
                Assert.AreEqual((int)reading / 3f, block.GetVector(BaseMapSt).z, 1e-4f, $"{reading} 的读数区域");
                yield return new WaitForSeconds(0.3f);               // 跳变结束
                // 用实际渲染结果检查：屏幕上字的颜色符合预设（batchmode 下 Material.passCount 不反映实际选用的 SubShader）
                var ink = TesterScreenUtil.InkColor(TesterScreenUtil.Grab(cam), rt.width, TesterScreenUtil.Clamp(PolishUtil.ScreenRect(cam, screen.bounds), rt.width, rt.height, 0.1f));
                bool ok = reading == TesterReadout.Reading.Ready ? ink.g > ink.r * 1.5f && ink.g > ink.b * 1.5f
                        : reading == TesterReadout.Reading.Bypass ? ink.r > ink.g * 1.5f && ink.g > ink.b
                        : Mathf.Min(ink.r, ink.g, ink.b) > 0.7f * ink.maxColorComponent;
                Assert.IsTrue(ok, $"{reading}：屏幕上字的平均颜色 {ink} 与预设不符");
            }

            tester.Show(TesterReadout.Reading.Bypass);
            tester.SetPresetsEnabled(false);
            Assert.IsFalse(tester.PresetsActive);
            Assert.AreEqual("M_Hand_Decal", screen.sharedMaterial.name, "关闭后回到原材质");
            screen.GetPropertyBlock(block);
            Assert.AreEqual(1f / 3f, block.GetVector(BaseMapSt).z, 1e-4f, "关闭预设后读数不变");
            tester.SetPresetsEnabled(true);
            Assert.AreSame(presets[1], screen.sharedMaterial);
            for (int i = 0; i < 3; i++) Assert.AreEqual(inks[i], presets[i].GetColor("_InkColor"), "不应修改材质资产");
        }

        [UnityTest]
        public IEnumerator ScreenJumpsBrieflyOnSwitchThenStaysStable()
        {
            rt = new RenderTexture(960, 540, 24);
            cam = TesterScreenUtil.CloseUp(tester, rt);
            yield return null;
            var rect = TesterScreenUtil.Clamp(PolishUtil.ScreenRect(cam, tester.Screen.bounds), rt.width, rt.height, 0.1f);   // 往里收一点，只比较屏幕本身
            Assert.Greater(rect.width * rect.height, 20000, "近景相机里屏幕应足够大");

            tester.Show(TesterReadout.Reading.Bypass);
            var jump = TesterScreenUtil.Grab(cam);                    // 切换的同一帧：跳变中
            yield return new WaitForSeconds(0.5f);
            var a = TesterScreenUtil.Grab(cam);
            yield return new WaitForSeconds(1.0f);
            var b = TesterScreenUtil.Grab(cam);

            var stable = TesterScreenUtil.Diff(a, b, rt.width, rect);
            Assert.AreEqual(0, stable.changed, $"稳定后不能闪烁：相隔 1 秒有 {stable.changed}/{stable.total} 个像素不同（最大差 {stable.maxDiff}）");
            var glitch = TesterScreenUtil.Diff(jump, a, rt.width, rect, 8);
            Assert.Greater(glitch.changed, glitch.total / 100, "切换瞬间应有短暂跳变");
        }
    }
}
