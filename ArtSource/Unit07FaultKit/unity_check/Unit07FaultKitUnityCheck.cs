// 七号左引擎故障美术包 · Unity 导入设置与核对（临时编辑器脚本：运行时复制到 Assets/…/Editor，跑完删除，不留在项目里）。
// 只处理本资产包目录 Assets/BorderRepair/Art/Unit07FaultKit 里的新资源；不改 RobotV4、现有场景、预制体、维修逻辑。
// 1. 贴图导入设置（法线贴图 / 线性贴图）；2. 按 materials.json 建 URP Lit 材质，并把 FBX 里的材质名映射到这些材质；
// 3. 在一个不保存的临时场景里，把 RobotV4 原 FBX 与本包 FBX 都按导入时的变换放到原点，核对：
//    新轴承 / 磨损轴承与原 Engine_BearingTop_L 是否重合（尺寸、中心、轴向），进气堵塞、贴纸是否在对应零件处；
//    算出每个资源挂到建议父对象下需要的本地位置 / 旋转；
// 4. 用 URP 渲染几张核对图（批处理模式下的离屏渲染，不代表游戏内灯光）。
// 命令行：-executeMethod Unit07FaultKitUnityCheck.Run
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class Unit07FaultKitUnityCheck
{
    const string Dir = "Assets/BorderRepair/Art/Unit07FaultKit";
    const string RobotFbx = "Assets/RobotV4/Model/robot-final.fbx";
    const string OutDir = "ArtSource/Unit07FaultKit/unity_check";
    static readonly StringBuilder Log = new StringBuilder();
    static void Note(string s) { Log.AppendLine(s); Debug.Log("[Unit07FaultKit] " + s); }

    [Serializable] class MatSpec { public string name, shader, baseMap, baseColor, metallicGlossMap, smoothnessSource, normalMap; public float metallic, smoothness; }
    [Serializable] class MatFile { public MatSpec[] materials; }

    public static void Run()
    {
        int code = 0;
        try { Setup(); Check(); }
        catch (Exception e) { Debug.LogException(e); Note("异常：" + e.Message); code = 1; }
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "unity_check.txt"), Log.ToString(), new UTF8Encoding(false));
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    // ------------------------------------------------------------------ 1/2. 导入设置与材质
    static void Setup()
    {
        Note($"Unity {Application.unityVersion}，渲染管线 {GraphicsSettings.currentRenderPipeline?.GetType().Name}，图形 {SystemInfo.graphicsDeviceType}");
        foreach (var p in Directory.GetFiles(Dir + "/Textures", "*.png"))
        {
            var path = p.Replace('\\', '/');
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            bool normal = path.EndsWith("_Normal.png"), linear = path.EndsWith("_MetallicSmoothness.png");
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !(normal || linear);
            ti.alphaSource = linear ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 4;
            ti.SaveAndReimport();
        }
        var spec = JsonUtility.FromJson<MatFile>(File.ReadAllText("ArtSource/Unit07FaultKit/materials.json"));
        Directory.CreateDirectory(Dir + "/Materials");
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var mats = new Dictionary<string, Material>();
        foreach (var m in spec.materials)
        {
            var path = $"{Dir}/Materials/{m.name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(lit); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = lit;
            ColorUtility.TryParseHtmlString(string.IsNullOrEmpty(m.baseColor) ? "#FFFFFF" : m.baseColor, out var col);
            mat.SetColor("_BaseColor", col);
            Texture2D T(string f) => string.IsNullOrEmpty(f) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/Textures/{f}");
            mat.SetTexture("_BaseMap", T(m.baseMap));
            var ms = T(m.metallicGlossMap);
            mat.SetTexture("_MetallicGlossMap", ms);
            if (ms != null) { mat.EnableKeyword("_METALLICSPECGLOSSMAP"); mat.SetFloat("_Smoothness", 1f); mat.SetFloat("_SmoothnessTextureChannel", 0f); }
            else { mat.DisableKeyword("_METALLICSPECGLOSSMAP"); mat.SetFloat("_Metallic", m.metallic); mat.SetFloat("_Smoothness", m.smoothness); }
            var nm = T(m.normalMap);
            mat.SetTexture("_BumpMap", nm);
            if (nm != null) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
            mat.SetFloat("_WorkflowMode", 1f);   // Metallic
            EditorUtility.SetDirty(mat);
            mats[m.name] = mat;
        }
        AssetDatabase.SaveAssets();
        foreach (var p in Directory.GetFiles(Dir + "/Models", "*.fbx"))
        {
            var path = p.Replace('\\', '/');
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = false;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var kv in mats) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.SaveAndReimport();
            Note($"导入 {Path.GetFileName(path)}：材质映射到 {string.Join("、", AssetDatabase.LoadAllAssetsAtPath(path).OfType<MeshRenderer>().SelectMany(r => r.sharedMaterials).Select(x => x ? x.name : "空").Distinct())}");
        }
    }

    // ------------------------------------------------------------------ 3/4. 核对
    static Transform FindDeep(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

    /// <summary>环形零件（世界坐标顶点）：质心、最薄方向（转轴，3×3 协方差的最小特征向量）、最大径向直径、沿轴宽度。</summary>
    static (Vector3 c, Vector3 axis, float dia, float width) RingMetrics(Mesh mesh, Transform t)
    {
        var v = mesh.vertices.Select(t.TransformPoint).ToArray();
        var c = v.Aggregate(Vector3.zero, (s, p) => s + p) / v.Length;
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var p in v) { var d = p - c; xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z; }
        // 逆幂迭代求最小特征向量：(C + εI)^-1 用伴随矩阵
        var m = new Matrix4x4();
        m.SetRow(0, new Vector4((float)xx, (float)xy, (float)xz, 0)); m.SetRow(1, new Vector4((float)xy, (float)yy, (float)yz, 0));
        m.SetRow(2, new Vector4((float)xz, (float)yz, (float)zz, 0)); m.SetRow(3, new Vector4(0, 0, 0, 1));
        var inv = m.inverse;
        var a = new Vector3(0.3f, 1f, 0.2f).normalized;
        for (int i = 0; i < 60; i++) a = inv.MultiplyVector(a).normalized;
        if (a.y < 0) a = -a;
        float rmax = 0, hmin = float.MaxValue, hmax = float.MinValue;
        foreach (var p in v)
        {
            var d = p - c; float h = Vector3.Dot(d, a);
            rmax = Mathf.Max(rmax, (d - h * a).magnitude); hmin = Mathf.Min(hmin, h); hmax = Mathf.Max(hmax, h);
        }
        return (c, a, rmax * 2, hmax - hmin);
    }

    static void Check()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);   // 临时场景，不保存
        var robot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RobotFbx));
        GameObject Inst(string f) => (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/Models/{f}"));
        var worn = Inst("UNIT07_FK_BearingWorn.fbx");
        var nu = Inst("UNIT07_FK_BearingNew.fbx");
        var clog = Inst("UNIT07_FK_IntakeClog.fbx");
        var label = Inst("UNIT07_FK_CoverLabel.fbx");
        Physics.SyncTransforms();

        // 单个对象的 FBX 导入后会被合并到根上，渲染器名字变成文件名：同时按网格名找
        Renderer R(GameObject g, string name) => g.GetComponentsInChildren<Renderer>(true).First(r => r.name == name || r.GetComponent<MeshFilter>()?.sharedMesh?.name == name);
        var origBearing = FindDeep(robot.transform, "Engine_BearingTop_L");
        var ob = origBearing.GetComponent<Renderer>().bounds;
        // 用网格顶点（世界坐标）比较：质心、最薄方向（= 转轴）、沿轴 / 径向尺寸。Renderer.bounds 对倾斜的零件会偏大，不能直接比。
        var (oc, oa, oDia, oW) = RingMetrics(origBearing.GetComponent<MeshFilter>().sharedMesh, origBearing);
        Note($"原 Engine_BearingTop_L（网格顶点）：质心 {oc:F5}，转轴 {oa:F4}，外径 {oDia * 1000f:F2} mm，宽 {oW * 1000f:F2} mm");
        foreach (var (g, n) in new[] { (worn, "UNIT07_FK_BearingTop_L_Worn"), (nu, "UNIT07_FK_BearingTop_L_New") })
        {
            var r = R(g, n);
            var mf = r.GetComponent<MeshFilter>().sharedMesh;
            var (c, a, dia, w) = RingMetrics(mf, r.transform);
            float ang = Vector3.Angle(a, oa); if (ang > 90f) ang = 180f - ang;
            Note($"{n}：质心差 {(c - oc).magnitude * 1000f:F3} mm，转轴夹角 {ang:F3}°，外径 {dia * 1000f:F2} mm，宽 {w * 1000f:F2} mm");
            Note($"  网格 {mf.vertexCount} 顶点 / {mf.triangles.Length / 3} 三角，子网格 {mf.subMeshCount}，切线 {(mf.tangents.Length > 0 ? "有" : "无")}，材质 {r.sharedMaterial?.name}");
        }
        var guardT = FindDeep(robot.transform, "Engine_IntakeGuard_L");
        var guard = guardT.GetComponent<Renderer>().bounds;
        var (gc, _, gDia, _) = RingMetrics(guardT.GetComponent<MeshFilter>().sharedMesh, guardT);
        var clogR = R(clog, "UNIT07_FK_IntakeClog_L_DustMat");
        var (cc, _, cDia, _) = RingMetrics(clogR.GetComponent<MeshFilter>().sharedMesh, clogR.transform);
        var toClog = cc - gc;
        Note($"进气堵塞（积尘毡层）：沿转轴在护栅质心 {Vector3.Dot(toClog, oa) * 1000f:F2} mm 处（负 = 护栅下方），径向偏差 {(toClog - Vector3.Dot(toClog, oa) * oa).magnitude * 1000f:F2} mm，直径 {cDia * 1000f:F1} mm（护栅 {gDia * 1000f:F1} mm）");
        var cover = FindDeep(robot.transform, "Engine_UpperCover_L").GetComponent<Renderer>().bounds;
        var labB = R(label, "UNIT07_FK_CoverInnerLabel_L").bounds;
        Note($"上盖内侧贴纸中心 {labB.center:F4}，在上盖包围盒内 {cover.Contains(labB.center)}");

        // 挂载：建议父对象下的本地位姿
        var mounts = new (GameObject g, string child, string parent)[]
        {
            (worn, "UNIT07_FK_BearingTop_L_Worn", "Engine_BearingTop_L"),
            (nu, "UNIT07_FK_BearingTop_L_New", "Engine_BearingTop_L"),
            (clog, "UNIT07_FK_IntakeClog_L_DustMat", "Engine_IntakeGuard_L"),
            (label, "UNIT07_FK_CoverInnerLabel_L", "Engine_UpperCover_L"),
        };
        Note("挂载（把下面“FBX 根”拖到父对象下，按此本地位姿摆放；父对象在七号静态姿态下）：");
        foreach (var (g, child, parent) in mounts)
        {
            var p = FindDeep(robot.transform, parent);
            var root = g.transform;
            var lp = p.InverseTransformPoint(root.position);
            var lr = Quaternion.Inverse(p.rotation) * root.rotation;
            var ls = new Vector3(root.lossyScale.x / p.lossyScale.x, root.lossyScale.y / p.lossyScale.y, root.lossyScale.z / p.lossyScale.z);
            Note($"  {Path.GetFileName(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(g)))} → 父 {AnimationUtility.CalculateTransformPath(p, robot.transform)}：" +
                 $"localPosition {lp.x:F5}, {lp.y:F5}, {lp.z:F5}；localRotation(四元数) {lr.x:F5}, {lr.y:F5}, {lr.z:F5}, {lr.w:F5}（欧拉 {lr.eulerAngles:F3}）；localScale {ls:F4}");
            // 回代核对：挂上去之后世界位姿不变
            var go = (GameObject)PrefabUtility.InstantiatePrefab(PrefabUtility.GetCorrespondingObjectFromSource(g));
            go.transform.SetParent(p, false);
            go.transform.localPosition = lp; go.transform.localRotation = lr; go.transform.localScale = ls;
            var a = R(go, child).bounds; var b = R(g, child).bounds;
            Note($"    回代：挂在父对象下后与原位置的中心差 {(a.center - b.center).magnitude * 1000f:F3} mm");
            Object.DestroyImmediate(go);
        }

        // 4. URP 离屏渲染（核对材质在 URP 里是否正常显示；灯光是临时的，不代表游戏内效果）
        try
        {
            var lightGo = new GameObject("TmpKey");
            var lgt = lightGo.AddComponent<Light>();
            lgt.type = LightType.Directional; lgt.intensity = 1.6f; lgt.color = new Color(1f, 0.9f, 0.78f);
            lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.34f, 0.36f);
            var camGo = new GameObject("TmpCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.08f, 0.09f, 0.09f);
            cam.nearClipPlane = 0.005f; cam.fieldOfView = 30f;
            var rt = new RenderTexture(1280, 960, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            void Shot(string file, Vector3 pos, Vector3 look)
            {
                cam.transform.position = pos; cam.transform.LookAt(look);
                cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(OutDir, file + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Note($"URP 渲染 {file}.png");
            }
            var bc = ob.center;
            var coverParts = new[] { "Engine_UpperCover_L", "Engine_IntakeLip_L", "Engine_IntakeGuard_L", "Engine_IntakeDuct_L" };
            void SetCover(bool on) { foreach (var n in coverParts) FindDeep(robot.transform, n).GetComponent<Renderer>().enabled = on; label.SetActive(on); clog.SetActive(on && worn.activeSelf); }
            origBearing.GetComponent<Renderer>().enabled = false;
            nu.SetActive(false);
            SetCover(true);
            Shot("U01_urp_intake_clogged", guard.center + new Vector3(-0.09f, 0.16f, -0.12f), guard.center);
            SetCover(false);
            Shot("U02_urp_worn_bearing_seated", bc + new Vector3(-0.06f, 0.09f, -0.08f), bc);
            worn.SetActive(false); nu.SetActive(true);
            Shot("U03_urp_new_bearing_seated", bc + new Vector3(-0.06f, 0.09f, -0.08f), bc);
            SetCover(true);
            Shot("U04_urp_intake_cleaned", guard.center + new Vector3(-0.09f, 0.16f, -0.12f), guard.center);
            // 贴纸：从引擎内部朝上看（上盖装着时）
            foreach (var n in new[] { "Engine_MotorHousing_L", "Engine_MountBase_L", "Engine_LowerCover_L" }) FindDeep(robot.transform, n).GetComponent<Renderer>().enabled = false;
            Shot("U05_urp_cover_label_from_inside", labB.center - Vector3.up * 0.09f + new Vector3(0.01f, 0, -0.02f), labB.center);
            rt.Release();
        }
        catch (Exception e) { Note("URP 渲染失败（未验证）：" + e.Message); }
        // 结束：不保存临时场景
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }
}
