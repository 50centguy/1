using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.Core;
using BorderRepair.Inspection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using Debug = UnityEngine.Debug;

namespace BorderRepair.Tests
{
    static class ShaderLabUtil
    {
        public const string LabScene = "Assets/BorderRepair/Scenes/ShaderLab_Surfaces.unity";
        public const string ProfilePath = "Assets/BorderRepair/Art/ShaderLab/DiagnosticHighlightProfile.asset";

        public static IEnumerator LoadScene(string path)
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(Path.GetFileNameWithoutExtension(path));
#endif
            yield return null;
        }

        public static InspectionPoint Point(string id)
        {
            var p = Object.FindObjectsByType<InspectionPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(x => x.PointId == id);
            Assert.IsNotNull(p, $"场景中没有检查点 {id}");
            return p;
        }

        public static Material Overlay => DiagnosticHighlightSettings.Active.Profile.overlayMaterial;

        public static int OverlayIndex(Renderer r) => System.Array.IndexOf(r.sharedMaterials, DiagnosticHighlightSettings.LastOverlayMaterial);

        public static Color32[] Render(Camera cam, RenderTexture rt)
        {
            cam.targetTexture = rt;
            cam.Render();
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

        /// <summary>把相机放在部位正前方（沿部位所属物品的 -Z 方向）并对准它。</summary>
        public static void FrameFront(Camera cam, InspectionPoint p, float distance, float lift = 0f)
        {
            var b = VisualBounds(p);
            cam.transform.position = b.center + new Vector3(0f, lift, -distance);
            cam.transform.LookAt(b.center);
        }

        public static Bounds VisualBounds(InspectionPoint p)
        {
            var rs = p.OwnedRenderers.Where(r => r != null && r.enabled && r.gameObject.activeInHierarchy).ToArray();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }

    /// <summary>诊断叠加层：只作用于本部位、不改材质资产、不覆盖其他属性块、嵌套部位互不干扰、不透墙。</summary>
    public class ShaderLabPlayModeTests
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int DiagParamsId = Shader.PropertyToID("_DiagParams");
        static readonly int DiagParams2Id = Shader.PropertyToID("_DiagParams2");
        static readonly int DiagColorId = Shader.PropertyToID("_DiagColor");

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return ShaderLabUtil.LoadScene(ShaderLabUtil.LabScene);
            Assert.IsNotNull(DiagnosticHighlightSettings.Active, "ShaderLab 场景应启用诊断高亮");
            Assert.IsTrue(DiagnosticHighlightSettings.Active.IsValid);
        }

        [UnityTest]
        public IEnumerator WornSurfaceRendersWithUrpSubshader()
        {
            yield return null;                                   // 至少渲染一帧，让 SubShader 选择生效
            var worn = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .SelectMany(r => r.sharedMaterials).Where(m => m != null && m.shader.name == "BorderRepair/WornSurface").Distinct().ToArray();
            Assert.AreEqual(3, worn.Length, "场景里应使用 3 个磨损材质实例");
            foreach (var m in worn)
            {
                var names = Enumerable.Range(0, m.passCount).Select(i => m.GetPassName(i)).ToArray();
                CollectionAssert.IsSupersetOf(names, new[] { "ForwardLit", "ShadowCaster", "DepthOnly", "DepthNormals" },
                    $"{m.name} 渲染时没有用上 URP SubShader（现有 pass：{string.Join(", ", names)}）");
            }
        }

        [UnityTest]
        public IEnumerator OverlayOnlyOnOwnedRenderers()
        {
            var seal = ShaderLabUtil.Point("seal");
            var all = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var before = all.ToDictionary(r => r, r => r.sharedMaterials);
            var owned = new HashSet<Renderer>(seal.OwnedRenderers);
            Assert.IsNotEmpty(owned);

            foreach (var state in new[] { InspectionPoint.VisualState.Hover, InspectionPoint.VisualState.Scanned, InspectionPoint.VisualState.Anomaly })
            {
                seal.SetVisualState(state);
                foreach (var r in all)
                {
                    if (owned.Contains(r))
                    {
                        Assert.AreEqual(before[r].Length + 1, r.sharedMaterials.Length, $"{r.name} 应只多出一个叠加槽");
                        Assert.AreEqual(ShaderLabUtil.Overlay, r.sharedMaterials[r.sharedMaterials.Length - 1]);
                    }
                    else CollectionAssert.AreEqual(before[r], r.sharedMaterials, $"{r.name} 不属于 seal，不应被改动");
                }
            }
            seal.SetVisualState(InspectionPoint.VisualState.Normal);
            foreach (var r in all) CollectionAssert.AreEqual(before[r], r.sharedMaterials, $"{r.name} 恢复普通后材质槽应与原来一致");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SharedMaterialAssetsAreNeverModified()
        {
            var seal = ShaderLabUtil.Point("seal");
            var mats = seal.OwnedRenderers.SelectMany(r => r.sharedMaterials).Append(ShaderLabUtil.Overlay).Distinct().ToArray();
            var snapshot = mats.ToDictionary(m => m, m => (
                m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : default,
                m.HasProperty(DiagParamsId) ? m.GetVector(DiagParamsId) : default,
                m.HasProperty(DiagColorId) ? m.GetColor(DiagColorId) : default,
                m.renderQueue));

            foreach (var state in new[] { InspectionPoint.VisualState.Hover, InspectionPoint.VisualState.Anomaly, InspectionPoint.VisualState.Scanned, InspectionPoint.VisualState.Normal })
                seal.SetVisualState(state);
            DiagnosticHighlightSettings.Active.enabled = false;           // 旧染色路径也不能改材质资产
            seal.SetVisualState(InspectionPoint.VisualState.Hover);
            seal.SetVisualState(InspectionPoint.VisualState.Normal);
            Object.FindFirstObjectByType<DiagnosticHighlightSettings>(FindObjectsInactive.Include).enabled = true;

            foreach (var kv in snapshot)
            {
                var m = kv.Key;
                Assert.AreEqual(kv.Value.Item1, m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : default, $"{m.name} 的 _BaseColor 被改了");
                Assert.AreEqual(kv.Value.Item2, m.HasProperty(DiagParamsId) ? m.GetVector(DiagParamsId) : default, $"{m.name} 的 _DiagParams 被改了");
                Assert.AreEqual(kv.Value.Item3, m.HasProperty(DiagColorId) ? m.GetColor(DiagColorId) : default, $"{m.name} 的 _DiagColor 被改了");
                Assert.AreEqual(kv.Value.Item4, m.renderQueue);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator OverlayParametersFollowProfilePerState()
        {
            var profile = DiagnosticHighlightSettings.Active.Profile;
            var seal = ShaderLabUtil.Point("seal");
            var r = seal.OwnedRenderers[0];
            var b = new MaterialPropertyBlock();

            seal.SetVisualState(InspectionPoint.VisualState.Hover);
            r.GetPropertyBlock(b, ShaderLabUtil.OverlayIndex(r));
            Assert.AreEqual(profile.hover.scanlines, b.GetVector(DiagParamsId).w, 1e-4f);
            Assert.AreEqual(0f, b.GetVector(DiagParams2Id).z, 1e-4f, "悬停没有异常斜纹");

            seal.SetVisualState(InspectionPoint.VisualState.Anomaly);
            r.GetPropertyBlock(b, ShaderLabUtil.OverlayIndex(r));
            Assert.AreEqual(profile.anomaly.hatch, b.GetVector(DiagParams2Id).z, 1e-4f);
            Assert.AreEqual(profile.anomaly.pulseSpeed, b.GetVector(DiagParams2Id).y, 1e-4f);
            Assert.Less(Vector4.Distance(profile.anomaly.color, b.GetColor(DiagColorId)), 1e-3f, "异常颜色应来自配置");

            seal.SetVisualState(InspectionPoint.VisualState.Normal);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExistingPropertyBlocksArePreserved()
        {
            var seal = ShaderLabUtil.Point("seal");
            var r = seal.OwnedRenderers[0];
            var external = new MaterialPropertyBlock();
            external.SetColor(BaseColorId, Color.magenta);
            external.SetFloat("_ExternalTest", 3f);
            r.SetPropertyBlock(external);

            // 诊断叠加层：参数写在叠加槽位上，渲染器级属性块不受影响
            foreach (var state in new[] { InspectionPoint.VisualState.Hover, InspectionPoint.VisualState.Anomaly, InspectionPoint.VisualState.Normal })
            {
                seal.SetVisualState(state);
                var b = new MaterialPropertyBlock();
                r.GetPropertyBlock(b);
                Assert.AreEqual(Color.magenta, b.GetColor(BaseColorId), $"{state}：外部 _BaseColor 被覆盖");
                Assert.AreEqual(3f, b.GetFloat("_ExternalTest"), $"{state}：外部属性被清掉");
            }

            // 旧染色路径：只临时改 _BaseColor，恢复时还原为外部设置的值，其他属性保留
            DiagnosticHighlightSettings.Active.enabled = false;
            seal.SetVisualState(InspectionPoint.VisualState.Hover);
            var t = new MaterialPropertyBlock();
            r.GetPropertyBlock(t);
            Assert.AreNotEqual(Color.magenta, t.GetColor(BaseColorId), "旧路径悬停应染色");
            Assert.AreEqual(3f, t.GetFloat("_ExternalTest"), "旧路径不应清掉其他属性");
            seal.SetVisualState(InspectionPoint.VisualState.Normal);
            r.GetPropertyBlock(t);
            Assert.AreEqual(Color.magenta, t.GetColor(BaseColorId), "旧路径恢复后应还原外部 _BaseColor");
            Assert.AreEqual(3f, t.GetFloat("_ExternalTest"));

            // 旧路径：原本没有属性块的渲染器，恢复后仍然没有
            var clean = ShaderLabUtil.Point("lamp").OwnedRenderers[0];
            clean.SetPropertyBlock(null);
            ShaderLabUtil.Point("lamp").SetVisualState(InspectionPoint.VisualState.Scanned);
            Assert.IsTrue(clean.HasPropertyBlock());
            ShaderLabUtil.Point("lamp").SetVisualState(InspectionPoint.VisualState.Normal);
            Assert.IsFalse(clean.HasPropertyBlock(), "原本没有属性块的渲染器恢复后不应残留属性块");

            Object.FindFirstObjectByType<DiagnosticHighlightSettings>(FindObjectsInactive.Include).enabled = true;
            r.SetPropertyBlock(null);
            yield return null;
        }

        /// <summary>
        /// 回归：原属性块只有其他参数、没有 _BaseColor 时，旧染色恢复后不应残留 _BaseColor
        /// （否则它会一直盖住材质颜色），其他参数（含染色期间被外部改过的）都要保留。
        /// </summary>
        [UnityTest]
        public IEnumerator LegacyRestoreLeavesNoBaseColorWhenOriginalBlockHadNone()
        {
            DiagnosticHighlightSettings.Active.enabled = false;
            var seal = ShaderLabUtil.Point("seal");
            var r = seal.OwnedRenderers[0];
            var shader = r.sharedMaterial.shader;
            int floatId = FirstProperty(shader, UnityEngine.Rendering.ShaderPropertyType.Float, UnityEngine.Rendering.ShaderPropertyType.Range);
            int colorId = FirstProperty(shader, UnityEngine.Rendering.ShaderPropertyType.Color, UnityEngine.Rendering.ShaderPropertyType.Color);
            var foreignColor = new Vector4(0.1f, 0.2f, 0.3f, 1f);

            var external = new MaterialPropertyBlock();
            external.SetFloat(floatId, 0.37f);
            external.SetVector(colorId, foreignColor);
            external.SetFloat("_ExternalTest", 3f);
            r.SetPropertyBlock(external);

            var b = new MaterialPropertyBlock();
            r.GetPropertyBlock(b);
            Assert.IsFalse(b.HasColor(BaseColorId), "前提：原属性块没有 _BaseColor");

            seal.SetVisualState(InspectionPoint.VisualState.Hover);
            r.GetPropertyBlock(b);
            Assert.IsTrue(b.HasColor(BaseColorId), "旧路径悬停应写入染色");
            var expectedTint = Color.Lerp(r.sharedMaterial.GetColor(BaseColorId), new Color(0.35f, 0.95f, 1f), 0.6f);
            Assert.Less(Vector4.Distance(expectedTint, b.GetColor(BaseColorId)), 1e-3f, "染色应基于材质颜色（原块没有 _BaseColor）");

            // 染色期间外部代码按“读-改-写”调整了一个参数
            b.SetFloat(floatId, 0.55f);
            r.SetPropertyBlock(b);

            seal.SetVisualState(InspectionPoint.VisualState.Scanned);
            seal.SetVisualState(InspectionPoint.VisualState.Normal);
            r.GetPropertyBlock(b);
            Assert.IsFalse(b.HasColor(BaseColorId) || b.HasVector(BaseColorId), "恢复后不应残留 _BaseColor");
            Assert.AreEqual(0.55f, b.GetFloat(floatId), 1e-5f, "染色期间外部改过的参数应保留");
            Assert.Less(Vector4.Distance(foreignColor, b.GetVector(colorId)), 1e-5f, "原有颜色参数应保留");
            Assert.AreEqual(3f, b.GetFloat("_ExternalTest"), "原有非 shader 参数应保留");

            // 原本没有属性块、染色期间外部写入了参数：恢复后只剩外部参数
            var lamp = ShaderLabUtil.Point("lamp");
            var clean = lamp.OwnedRenderers[0];
            clean.SetPropertyBlock(null);
            int lampFloat = FirstProperty(clean.sharedMaterial.shader, UnityEngine.Rendering.ShaderPropertyType.Float, UnityEngine.Rendering.ShaderPropertyType.Range);
            lamp.SetVisualState(InspectionPoint.VisualState.Scanned);
            clean.GetPropertyBlock(b);
            b.SetFloat(lampFloat, 0.42f);
            clean.SetPropertyBlock(b);
            lamp.SetVisualState(InspectionPoint.VisualState.Normal);
            clean.GetPropertyBlock(b);
            Assert.IsFalse(b.HasColor(BaseColorId) || b.HasVector(BaseColorId), "恢复后不应残留 _BaseColor");
            Assert.AreEqual(0.42f, b.GetFloat(lampFloat), 1e-5f);

            Object.FindFirstObjectByType<DiagnosticHighlightSettings>(FindObjectsInactive.Include).enabled = true;
            r.SetPropertyBlock(null);
            clean.SetPropertyBlock(null);
            yield return null;
        }

        static int FirstProperty(Shader shader, UnityEngine.Rendering.ShaderPropertyType a, UnityEngine.Rendering.ShaderPropertyType b)
        {
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                var t = shader.GetPropertyType(i);
                int id = shader.GetPropertyNameId(i);
                if ((t == a || t == b) && id != BaseColorId) return id;
            }
            Assert.Fail($"{shader.name} 没有可用于测试的 {a} 属性");
            return -1;
        }

        /// <summary>H 切换高亮路径后，鼠标下的部位仍显示悬停（两条路径各自的悬停样式），切回后旧染色不残留。</summary>
        [UnityTest]
        public IEnumerator ToggleModeKeepsCurrentHover()
        {
            var lab = Object.FindFirstObjectByType<Camera>().GetComponent<BorderRepair.ShaderLab.ShaderLabInteraction>();
            lab.enabled = false;                                 // 不跑鼠标射线，直接驱动
            var profile = DiagnosticHighlightSettings.Active.Profile;
            var seal = ShaderLabUtil.Point("seal");
            var others = new[] { ShaderLabUtil.Point("lamp"), ShaderLabUtil.Point("port") };
            var rs = seal.OwnedRenderers.Where(x => x != null && x.gameObject.activeInHierarchy).ToArray();
            var b = new MaterialPropertyBlock();

            lab.SetHovered(seal);
            Assert.AreEqual(InspectionPoint.VisualState.Hover, seal.CurrentState);

            lab.ToggleHighlightMode();                           // → 旧染色
            Assert.IsNull(DiagnosticHighlightSettings.Active);
            Assert.AreEqual(InspectionPoint.VisualState.Hover, seal.CurrentState, "切到旧染色后应保持悬停");
            foreach (var r in rs)
            {
                Assert.Less(ShaderLabUtil.OverlayIndex(r), 0, "旧染色路径下不应留有叠加层");
                r.GetPropertyBlock(b);
                var expected = Color.Lerp(r.sharedMaterial.GetColor(BaseColorId), new Color(0.35f, 0.95f, 1f), 0.6f);
                Assert.Less(Vector4.Distance(expected, b.GetColor(BaseColorId)), 1e-3f, $"{r.name} 应显示旧路径的悬停染色");
            }
            foreach (var p in others) Assert.AreEqual(InspectionPoint.VisualState.Normal, p.CurrentState, "未悬停的部位不应被带成悬停");

            lab.ToggleHighlightMode();                           // → 叠加层
            Assert.IsNotNull(DiagnosticHighlightSettings.Active);
            Assert.AreEqual(InspectionPoint.VisualState.Hover, seal.CurrentState, "切回叠加层后应保持悬停");
            foreach (var r in rs)
            {
                int i = ShaderLabUtil.OverlayIndex(r);
                Assert.GreaterOrEqual(i, 0, $"{r.name} 应显示悬停叠加层");
                r.GetPropertyBlock(b, i);
                Assert.AreEqual(profile.hover.scanlines, b.GetVector(DiagParamsId).w, 1e-4f, "应为悬停样式");
                r.GetPropertyBlock(b);
                Assert.IsFalse(b.HasColor(BaseColorId), $"{r.name} 旧染色应已完全还原");
            }

            lab.SetHovered(null);
            Assert.AreEqual(InspectionPoint.VisualState.Normal, seal.CurrentState);
            foreach (var r in rs) Assert.Less(ShaderLabUtil.OverlayIndex(r), 0);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NestedPointsAreIndependent()
        {
            var shell = ShaderLabUtil.Point("shell");
            var seal = ShaderLabUtil.Point("lease_seal");
            Assert.IsTrue(seal.transform.IsChildOf(shell.transform), "前提：封条嵌套在盖板下");
            Assert.IsFalse(shell.OwnedRenderers.Intersect(seal.OwnedRenderers).Any(), "盖板不应接管封条的渲染器");

            seal.SetVisualState(InspectionPoint.VisualState.Anomaly);
            shell.SetVisualState(InspectionPoint.VisualState.Hover);
            shell.SetVisualState(InspectionPoint.VisualState.Normal);
            foreach (var r in seal.OwnedRenderers.Where(x => x.gameObject.activeInHierarchy))
            {
                Assert.GreaterOrEqual(ShaderLabUtil.OverlayIndex(r), 0, "盖板恢复普通不应清掉封条的异常高亮");
                var b = new MaterialPropertyBlock();
                r.GetPropertyBlock(b, ShaderLabUtil.OverlayIndex(r));
                Assert.Less(Vector4.Distance(DiagnosticHighlightSettings.Active.Profile.anomaly.color, b.GetColor(DiagColorId)), 1e-3f, "封条应保持异常样式");
                Assert.AreEqual(DiagnosticHighlightSettings.Active.Profile.anomaly.pulseSpeed, b.GetVector(DiagParams2Id).y, 1e-4f);
            }
            foreach (var r in shell.OwnedRenderers) Assert.Less(ShaderLabUtil.OverlayIndex(r), 0);
            seal.SetVisualState(InspectionPoint.VisualState.Normal);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HighlightDoesNotShowThroughOccluders()
        {
            var seal = ShaderLabUtil.Point("seal");
            var cam = Object.FindFirstObjectByType<Camera>();
            cam.GetComponent<BorderRepair.ShaderLab.ShaderLabInteraction>().enabled = false;          // 暂停交互脚本，固定机位
            ShaderLabUtil.FrameFront(cam, seal, 0.25f);
            var rt = new RenderTexture(320, 240, 24);
            var b = ShaderLabUtil.VisualBounds(seal);

            // 没有遮挡时：异常与普通应有明显差别（证明比较有效）
            var normal = ShaderLabUtil.Render(cam, rt);
            seal.SetVisualState(InspectionPoint.VisualState.Anomaly);
            var anomaly = ShaderLabUtil.Render(cam, rt);
            Assert.Greater(DiffCount(normal, anomaly), 200, "未遮挡时异常高亮应可见");

            // 在部位和镜头之间放一块完全挡住它的板：被挡住后两种状态的画面应完全相同
            var occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            occluder.transform.position = b.center + new Vector3(0f, 0f, -0.06f);
            occluder.transform.localScale = new Vector3(b.size.x * 3f, b.size.y * 3f, 0.01f);
            seal.SetVisualState(InspectionPoint.VisualState.Normal);
            var hiddenNormal = ShaderLabUtil.Render(cam, rt);
            seal.SetVisualState(InspectionPoint.VisualState.Anomaly);
            var hiddenAnomaly = ShaderLabUtil.Render(cam, rt);
            Assert.AreEqual(0, DiffCount(hiddenNormal, hiddenAnomaly), "被遮挡的部位不应透出高亮");

            Object.DestroyImmediate(occluder);   // 立即销毁：后面同一帧还会收集场景渲染器
            seal.SetVisualState(InspectionPoint.VisualState.Normal);
            cam.targetTexture = null;
            rt.Release();
            yield return null;
        }

        static int DiffCount(Color32[] a, Color32[] b)
        {
            int n = 0;
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i].r - b[i].r) > 3 || Mathf.Abs(a[i].g - b[i].g) > 3 || Mathf.Abs(a[i].b - b[i].b) > 3) n++;
            return n;
        }
    }

    /// <summary>接入现有扫描流程：默认场景临时加上诊断高亮设置，扫描状态应映射为叠加样式；不加时保持原染色。</summary>
    public class DiagnosticHighlightScannerTests
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int DiagParams2Id = Shader.PropertyToID("_DiagParams2");
        static readonly int DiagColorId = Shader.PropertyToID("_DiagColor");

        RepairStationController controller;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return ShaderLabUtil.LoadScene("Assets/BorderRepair/Scenes/RepairStation_Prototype.unity");
            controller = Object.FindFirstObjectByType<RepairStationController>();
            Assert.IsNotNull(controller);
            Assert.IsNull(DiagnosticHighlightSettings.Active, "默认场景不应带诊断高亮设置");
        }

        [UnityTest]
        public IEnumerator DefaultSceneKeepsLegacyTint()
        {
            var s = controller.Session;
            s.AcceptItem();
            s.ToggleScanMode();
            controller.ScanPoint("antenna");                   // 关键异常：旧路径下按“已扫描”染色
            Assert.IsTrue(controller.TryGetPoint("antenna", out var antenna));
            Assert.AreEqual(InspectionPoint.VisualState.Anomaly, antenna.CurrentState);
            foreach (var r in antenna.OwnedRenderers.Where(x => x.gameObject.activeInHierarchy))
            {
                Assert.IsFalse(r.sharedMaterials.Any(m => m != null && m.shader.name == "BorderRepair/DiagnosticOverlay"), "默认场景不应出现叠加层");
                var b = new MaterialPropertyBlock();
                r.GetPropertyBlock(b);
                var baseColor = r.sharedMaterial.GetColor(BaseColorId);
                var expected = Color.Lerp(baseColor, new Color(0.45f, 1f, 0.5f), 0.6f);
                Assert.Less(Vector4.Distance(expected, b.GetColor(BaseColorId)), 1e-3f, "异常在旧路径下应与“已扫描”染色相同");
            }
            yield return null;
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator ScannerStatesDriveOverlayWhenEnabled()
        {
            var profile = AssetDatabase.LoadAssetAtPath<DiagnosticHighlightProfile>(ShaderLabUtil.ProfilePath);
            Assert.IsNotNull(profile);
            var go = new GameObject("RuntimeHighlightSettings");
            go.AddComponent<DiagnosticHighlightSettings>().Configure(profile);

            var s = controller.Session;
            s.AcceptItem();
            s.ToggleScanMode();
            controller.ScanPoint("antenna");                   // 关键异常 → Anomaly
            controller.ScanPoint("battery");                   // 正常部位 → Scanned
            Assert.IsTrue(controller.TryGetPoint("antenna", out var antenna));
            Assert.IsTrue(controller.TryGetPoint("battery", out var battery));

            var block = new MaterialPropertyBlock();
            foreach (var r in antenna.OwnedRenderers.Where(x => x.gameObject.activeInHierarchy))
            {
                int i = System.Array.IndexOf(r.sharedMaterials, profile.overlayMaterial);
                Assert.GreaterOrEqual(i, 0, "异常部位应有叠加层");
                r.GetPropertyBlock(block, i);
                Assert.AreEqual(profile.anomaly.hatch, block.GetVector(DiagParams2Id).z, 1e-4f);
                Assert.Less(Vector4.Distance(profile.anomaly.color, block.GetColor(DiagColorId)), 1e-3f, "异常颜色应来自配置");
            }
            foreach (var r in battery.OwnedRenderers.Where(x => x.gameObject.activeInHierarchy))
            {
                int i = System.Array.IndexOf(r.sharedMaterials, profile.overlayMaterial);
                Assert.GreaterOrEqual(i, 0, "已扫描部位应有叠加层");
                r.GetPropertyBlock(block, i);
                Assert.Less(Vector4.Distance(profile.scanned.color, block.GetColor(DiagColorId)), 1e-3f, "已扫描颜色应来自配置");
            }

            s.ToggleScanMode();                                // 关闭扫描 → 全部恢复普通，叠加层移除
            foreach (var p in new[] { antenna, battery })
                foreach (var r in p.OwnedRenderers)
                    Assert.Less(System.Array.IndexOf(r.sharedMaterials, profile.overlayMaterial), 0);

            Object.Destroy(go);
            yield return null;
        }
#endif
    }

    /// <summary>生成对比截图与 batchmode 性能粗测。Explicit：只有用 -testFilter 点名时才运行。</summary>
    [Explicit("仅用于生成截图与性能粗测")]
    public class ShaderLabCaptureTests
    {
        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "ShaderLab", "Screenshots"));
        static string PerfPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "ShaderLab", "perf_batchmode.txt"));

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return ShaderLabUtil.LoadScene(ShaderLabUtil.LabScene);
        }

        [UnityTest]
        public IEnumerator CaptureStatesAndMeasure()
        {
            Directory.CreateDirectory(OutDir);
            var cam = Object.FindFirstObjectByType<Camera>();
            cam.GetComponent<BorderRepair.ShaderLab.ShaderLabInteraction>().enabled = false;
            var ui = Object.FindFirstObjectByType<Canvas>();
            if (ui != null) ui.gameObject.SetActive(false);
            var rt = new RenderTexture(1600, 1000, 24);
            yield return null;

            Save(cam, rt, "00_overview");
            var ballsTarget = new Vector3(0.12f, 0.08f, 0.4f);
            cam.transform.position = ballsTarget + new Vector3(0f, 0.18f, -0.75f);
            cam.transform.LookAt(ballsTarget);
            Save(cam, rt, "01_materials");

            // 可读性对比：同一机位的普通 / 悬停 / 已扫描 / 异常，外加上一版异常参数（带斜纹）作对照。
            // 异常带脉冲，统一等到脉冲峰值（最亮、对细节影响最大）再截图，结果可比。
            var readability = new StringBuilder();
            readability.AppendLine("ShaderLab 可读性粗测（batchmode 截图，1600×1000；区域 = 部位包围盒投影到屏幕）");
            readability.AppendLine("细节相关系数：与“普通”截图的亮度皮尔逊相关，1 = 明暗细节完全保留（对整体提亮不敏感，对斜纹、大面积染色敏感）");
            readability.AppendLine("对比度保留：区域亮度标准差 / 普通状态的标准差；平均色差：每像素 RGB 平均绝对差（0–255），衡量状态是否看得出来");
            readability.AppendLine();

            var settings = DiagnosticHighlightSettings.Active;
            var currentProfile = settings.Profile;
            var oldProfile = Object.Instantiate(currentProfile);   // 运行时副本，不改资产
            oldProfile.anomaly.tint = 0.07f; oldProfile.anomaly.rim = 1.1f; oldProfile.anomaly.rimPower = 2.2f;
            oldProfile.anomaly.hatch = 0.16f; oldProfile.anomaly.hatchPeriodPx = 18f;

            var seal = ShaderLabUtil.Point("seal");
            ShaderLabUtil.FrameFront(cam, seal, 0.22f, 0.02f);
            yield return CaptureStates(cam, rt, seal, "02_seal", settings, currentProfile, oldProfile, readability);

            var board = ShaderLabUtil.Point("mainboard");
            var bb = ShaderLabUtil.VisualBounds(board);
            cam.transform.position = bb.center + new Vector3(0f, 0.2f, -0.16f);
            cam.transform.LookAt(bb.center);
            yield return CaptureStates(cam, rt, board, "03_mainboard", settings, currentProfile, oldProfile, readability);
            Object.Destroy(oldProfile);

            // 遮挡：异常中的封条被一块板挡住左半边
            ShaderLabUtil.FrameFront(cam, seal, 0.22f, 0.02f);
            var sb = ShaderLabUtil.VisualBounds(seal);
            var occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            occluder.transform.position = sb.center + new Vector3(-sb.size.x * 0.25f, 0f, -0.05f);
            occluder.transform.localScale = new Vector3(sb.size.x * 0.5f, sb.size.y * 2f, 0.004f);
            seal.SetVisualState(InspectionPoint.VisualState.Anomaly);
            Save(cam, rt, "04_seal_anomaly_half_occluded");
            Object.DestroyImmediate(occluder);   // 立即销毁：后面同一帧还会收集场景渲染器
            seal.SetVisualState(InspectionPoint.VisualState.Normal);

            // 嵌套部位：盖板悬停 + 封条异常
            var shell = ShaderLabUtil.Point("shell");
            var hb = ShaderLabUtil.VisualBounds(shell);
            cam.transform.position = hb.center + new Vector3(0f, 0.06f, -0.3f);
            cam.transform.LookAt(hb.center);
            ShaderLabUtil.Point("lease_seal").SetVisualState(InspectionPoint.VisualState.Anomaly);
            shell.SetVisualState(InspectionPoint.VisualState.Hover);
            Save(cam, rt, "05_hand_shell_hover_seal_anomaly");
            shell.SetVisualState(InspectionPoint.VisualState.Normal);
            ShaderLabUtil.Point("lease_seal").SetVisualState(InspectionPoint.VisualState.Normal);

            // 对比：旧底色染色路径下的“已扫描”
            DiagnosticHighlightSettings.Active.enabled = false;
            ShaderLabUtil.FrameFront(cam, seal, 0.22f, 0.02f);
            var sealRect = ScreenRect(cam, rt, ShaderLabUtil.VisualBounds(seal));
            var legacyNormal = Save(cam, rt, null);
            seal.SetVisualState(InspectionPoint.VisualState.Scanned);
            var legacyScanned = Save(cam, rt, "06_seal_legacy_tint_scanned");
            readability.AppendLine("seal（旧底色染色路径，对照）");
            readability.AppendLine("  " + Compare("已扫描（旧染色）", legacyNormal, legacyScanned, rt.width, sealRect));
            seal.SetVisualState(InspectionPoint.VisualState.Normal);
            Object.FindFirstObjectByType<DiagnosticHighlightSettings>(FindObjectsInactive.Include).enabled = true;

            File.WriteAllText(ReadabilityPath, readability.ToString(), new UTF8Encoding(true));
            Debug.Log("[ShaderLab] " + readability);

            yield return Measure(cam);
            rt.Release();
        }

        static string ReadabilityPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "ShaderLab", "readability_batchmode.txt"));

        static IEnumerator CaptureStates(Camera cam, RenderTexture rt, InspectionPoint point, string prefix,
            DiagnosticHighlightSettings settings, DiagnosticHighlightProfile current, DiagnosticHighlightProfile old, StringBuilder log)
        {
            var rect = ScreenRect(cam, rt, ShaderLabUtil.VisualBounds(point));
            log.AppendLine($"{point.PointId}（区域 {rect.width}×{rect.height} 像素）");
            var states = new[] { InspectionPoint.VisualState.Normal, InspectionPoint.VisualState.Hover, InspectionPoint.VisualState.Scanned, InspectionPoint.VisualState.Anomaly };
            Color32[] normal = null;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i] == InspectionPoint.VisualState.Anomaly) yield return WaitForPulsePeak(current.anomaly.pulseSpeed);
                point.SetVisualState(states[i]);
                var px = Save(cam, rt, $"{prefix}_{i + 1}_{states[i].ToString().ToLower()}");
                if (i == 0) normal = px;
                else log.AppendLine("  " + Compare(StateLabel(states[i]), normal, px, rt.width, rect));
            }

            // 对照：上一版异常参数（斜纹 0.16），同样在脉冲峰值截图
            settings.Configure(old);
            yield return WaitForPulsePeak(old.anomaly.pulseSpeed);
            point.SetVisualState(InspectionPoint.VisualState.Anomaly);
            var oldPx = Save(cam, rt, $"{prefix}_5_anomaly_previous_hatch");
            log.AppendLine("  " + Compare("异常（上一版：斜纹 0.16）", normal, oldPx, rt.width, rect));
            settings.Configure(current);
            point.SetVisualState(InspectionPoint.VisualState.Normal);
            log.AppendLine();
        }

        /// <summary>等到 shader 脉冲接近峰值（0.65 + 0.35·sin(t·speed) ≥ 0.98）。_Time.y = 场景加载后的时间。</summary>
        static IEnumerator WaitForPulsePeak(float speed)
        {
            if (speed <= 0f) yield break;
            for (int i = 0; i < 2000 && Mathf.Sin(Time.timeSinceLevelLoad * speed) < 0.94f; i++) yield return null;
        }

        static string StateLabel(InspectionPoint.VisualState s) =>
            s == InspectionPoint.VisualState.Hover ? "悬停" : s == InspectionPoint.VisualState.Scanned ? "已扫描" : s == InspectionPoint.VisualState.Anomaly ? "异常（当前）" : "普通";

        static RectInt ScreenRect(Camera cam, RenderTexture rt, Bounds b)
        {
            cam.targetTexture = rt;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var s = cam.WorldToScreenPoint(c);
                x0 = Mathf.Min(x0, s.x); y0 = Mathf.Min(y0, s.y); x1 = Mathf.Max(x1, s.x); y1 = Mathf.Max(y1, s.y);
            }
            cam.targetTexture = null;
            int ix0 = Mathf.Clamp(Mathf.CeilToInt(x0), 0, rt.width - 1), iy0 = Mathf.Clamp(Mathf.CeilToInt(y0), 0, rt.height - 1);
            int ix1 = Mathf.Clamp(Mathf.FloorToInt(x1), ix0 + 1, rt.width), iy1 = Mathf.Clamp(Mathf.FloorToInt(y1), iy0 + 1, rt.height);
            return new RectInt(ix0, iy0, ix1 - ix0, iy1 - iy0);
        }

        static string Compare(string label, Color32[] a, Color32[] b, int width, RectInt rc)
        {
            double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0, diff = 0;
            int n = 0;
            for (int y = rc.yMin; y < rc.yMax; y++)
                for (int x = rc.xMin; x < rc.xMax; x++)
                {
                    var p = a[y * width + x]; var q = b[y * width + x];
                    double la = 0.2126 * p.r + 0.7152 * p.g + 0.0722 * p.b;
                    double lb = 0.2126 * q.r + 0.7152 * q.g + 0.0722 * q.b;
                    sa += la; sb += lb; saa += la * la; sbb += lb * lb; sab += la * lb;
                    diff += (System.Math.Abs(p.r - q.r) + System.Math.Abs(p.g - q.g) + System.Math.Abs(p.b - q.b)) / 3.0;
                    n++;
                }
            double ma = sa / n, mb = sb / n;
            double va = saa / n - ma * ma, vb = sbb / n - mb * mb, cov = sab / n - ma * mb;
            double corr = cov / System.Math.Sqrt(System.Math.Max(va * vb, 1e-9));
            double ratio = System.Math.Sqrt(System.Math.Max(vb, 0)) / System.Math.Sqrt(System.Math.Max(va, 1e-9));
            return $"{label}：细节相关系数 {corr:0.000}，对比度保留 {ratio:0.00}，平均色差 {diff / n:0.0}";
        }

        /// <summary>渲染一帧并读回像素；name 不为空时同时存 PNG。</summary>
        static Color32[] Save(Camera cam, RenderTexture rt, string name)
        {
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            if (name != null) File.WriteAllBytes(Path.Combine(OutDir, $"20260924_ShaderLab_{name}.png"), tex.EncodeToPNG());
            var px = tex.GetPixels32();
            Object.Destroy(tex);
            cam.targetTexture = null;
            return px;
        }

        /// <summary>batchmode 粗测：同一机位连续渲染 N 帧，最后读回 1 像素强制等待 GPU，取平均毫秒。</summary>
        static IEnumerator Measure(Camera cam)
        {
            var rt = new RenderTexture(1920, 1080, 24);
            cam.transform.position = new Vector3(0.15f, 0.525f, -1.17f);
            cam.transform.LookAt(new Vector3(0.15f, 0.08f, 0.05f));
            var points = Object.FindObjectsByType<InspectionPoint>(FindObjectsSortMode.None);
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r != null).ToArray();
            var wornRenderers = renderers.Where(r => r.sharedMaterials.Any(m => m != null && m.shader.name == "BorderRepair/WornSurface")).ToArray();
            int pointRenderers = points.Sum(p => p.OwnedRenderers.Count(r => r != null && r.gameObject.activeInHierarchy));

            var sb = new StringBuilder();
            sb.AppendLine("ShaderLab batchmode 粗测（1920×1080 离屏渲染，同一机位，300 帧平均；每组最后读回 1 像素等待 GPU）");
            sb.AppendLine($"GPU: {SystemInfo.graphicsDeviceName} / {SystemInfo.graphicsDeviceType}");
            sb.AppendLine($"场景渲染器 {renderers.Length} 个；使用 WornSurface 的 {wornRenderers.Length} 个；检查点 {points.Length} 个，共拥有 {pointRenderers} 个渲染器");
            sb.AppendLine();

            yield return Timing(cam, rt, "A 磨损材质，无高亮", sb);

            var reference = Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m.name == "M_metal_light");
            var saved = wornRenderers.ToDictionary(r => r, r => r.sharedMaterials);
            foreach (var r in wornRenderers) r.sharedMaterials = r.sharedMaterials.Select(m => m != null && m.shader.name == "BorderRepair/WornSurface" ? reference : m).ToArray();
            yield return Timing(cam, rt, "B 同样的网格换成 URP Lit（参照），无高亮", sb);
            foreach (var kv in saved) kv.Key.sharedMaterials = kv.Value;

            ShaderLabUtil.Point("seal").SetVisualState(InspectionPoint.VisualState.Hover);
            yield return Timing(cam, rt, "C 磨损材质 + 1 个部位悬停", sb);
            foreach (var p in points) p.SetVisualState(InspectionPoint.VisualState.Anomaly);
            int overlays = renderers.Count(r => r.sharedMaterials.Any(m => m == DiagnosticHighlightSettings.Active.Profile.overlayMaterial));
            yield return Timing(cam, rt, $"D 磨损材质 + 全部 {points.Length} 个部位异常（{overlays} 个叠加绘制，极端情况）", sb);
            foreach (var p in points) p.SetVisualState(InspectionPoint.VisualState.Normal);

            File.WriteAllText(PerfPath, sb.ToString(), new UTF8Encoding(true));
            Debug.Log("[ShaderLab] " + sb);
            rt.Release();
        }

        static IEnumerator Timing(Camera cam, RenderTexture rt, string label, StringBuilder sb)
        {
            yield return null;
            cam.targetTexture = rt;
            var one = new Texture2D(1, 1, TextureFormat.RGB24, false);
            for (int i = 0; i < 10; i++) cam.Render();
            RenderTexture.active = rt;
            one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            const int frames = 300;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < frames; i++) cam.Render();
            RenderTexture.active = rt;
            one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            one.Apply();
            sw.Stop();
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.Destroy(one);
            sb.AppendLine($"{label}：{sw.Elapsed.TotalMilliseconds / frames:0.000} ms/帧");
        }
    }
}
