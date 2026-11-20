using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // Depth for the playable copy's far view (Docs/Art/INTERIOR_B1_PASS.md section 25.10): near, middle and far apart.
    //   - Fog linear from 30 m to 170 m in the cold #3B4B54: the street and the shops stay sharp, the middle-distance
    //     blocks start to lift off into the fog, the far masses (their own fixed haze) and the cranes sit in it.
    //   - Far window light cold and dim (warm windows far away pull the far view forward): the city blocks, the far
    //     flats' window decals, the towers' window grid, the harbour sheds; the skyline's amber neon ring off. The red
    //     obstruction lamps stay.
    //   - A sky of its own: a cold scatter glow at the horizon (#2A3942) up to a dark slate zenith (#0E1519); the city
    //     glow on the cloud goes cold too. The towers' and cranes' tops cut out against it.
    //   - One faint band of air on a ring between the middle distance and the skyline.
    // Only copies saved under the playable folder are changed; the district's own materials and sky stay as they are.
    public static partial class ExteriorDistrictPass
    {
        static void FarDepth(Transform holder)
        {
            var renderers = holder.GetComponentsInChildren<Renderer>(true);
            var done = new List<string>();
            var copies = new Dictionary<Material, Material>();
            string File(Material m) => System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(m));
            bool Is(Material m, string shader) => m.shader != null && m.shader.name == shader;
            void Swap(string tag, Func<Renderer, Material, bool> match, Action<Material> edit)
            {
                int n = 0;
                foreach (var r in renderers)
                {
                    var shared = r.sharedMaterials; bool hit = false;
                    for (int i = 0; i < shared.Length; i++)
                    {
                        var m = shared[i];
                        if (m == null || !match(r, m)) continue;
                        if (!copies.TryGetValue(m, out var c)) { c = new Material(m); edit(c); copies[m] = c = Save("Depth - " + tag + " " + copies.Count, c); }
                        shared[i] = c; hit = true;
                    }
                    if (hit) { r.sharedMaterials = shared; n++; }
                }
                done.Add(tag + " (" + n + " r)");
            }

            // 1. Fog.
            var air = holder.GetComponentInChildren<DistrictAtmosphere>(true) ?? Object.FindFirstObjectByType<DistrictAtmosphere>();
            if (air != null)
            {
                done.Add("fog was " + air.fogMode + " " + air.fogDensity.ToString("F3") + " " + ColorUtility.ToHtmlStringRGB(air.fogColour));
                air.fog = true; air.fogMode = FogMode.Linear; air.fogStart = 30f; air.fogEnd = 170f; air.fogColour = FogTeal;
                EditorUtility.SetDirty(air);
                done.Add("fog now linear 30-170 #3B4B54");
            }

            // 2. Far window light.
            var coldDim = Hex("#55666E");
            Swap("far city blocks cold", (r, m) => Is(m, "BorderRepair/Exterior/CityBlock"), c =>
            {
                c.SetColor("_Warm", coldDim); c.SetColor("_Neutral", coldDim);
                if (c.HasProperty("_Gain")) c.SetFloat("_Gain", c.GetFloat("_Gain") * .3f);
            });
            Swap("far flats windows cold", (r, m) => File(m) == "Distant_windows", c => c.SetColor("_BaseColor", Hex("#3E4C54")));
            Swap("tower window grid dim", (r, m) => Is(m, "BorderRepair/Exterior/FarSilhouette") && m.HasProperty("_WindowGain"), c =>
            {
                c.SetFloat("_WindowGain", c.GetFloat("_WindowGain") * .3f); c.SetColor("_WindowColor", Hex("#5F7480"));
            });
            Swap("far sheds dim", (r, m) => File(m).StartsWith("Shed_door", StringComparison.Ordinal), c => c.SetColor("_BaseColor", Color.Lerp(c.GetColor("_BaseColor"), Hex("#2E3A40"), .75f)));
            int ring = 0;
            foreach (var r in renderers.Where(r => r.name == "Ring neon - amber")) { r.gameObject.SetActive(false); ring++; }
            done.Add("skyline amber ring off (" + ring + ")");
            Swap("skyline cyan ring dimmer", (r, m) => r.name == "Ring neon - cyan" && m.HasProperty("_Intensity"), c => c.SetFloat("_Intensity", c.GetFloat("_Intensity") * .5f));

            // 3. Sky and the city glow on the cloud.
            var skyShader = Shader.Find("BorderRepair/Exterior/NightSky");
            if (skyShader != null && air != null)
            {
                var sky = new Material(skyShader);
                sky.SetColor("_Horizon", Hex("#2A3942")); sky.SetColor("_Zenith", Hex("#0E1519"));
                sky.SetFloat("_Height", .32f); sky.SetFloat("_Cloud", .12f);
                air.skybox = Save("Depth - night sky", sky);
                EditorUtility.SetDirty(air);
                done.Add("sky horizon #2A3942 to zenith #0E1519");
            }
            else done.Add("night sky shader missing");
            Swap("horizon glow cold", (r, m) => Is(m, "BorderRepair/Exterior/HorizonGlow"), c =>
            {
                c.SetColor("_Low", Hex("#2A3942")); c.SetColor("_High", Hex("#141D22")); c.SetFloat("_Intensity", .3f);
            });

            // 4. A band of air between the middle distance and the skyline: on a ring round the skyline's centre, past the
            //    cranes and short of the nearest far mass.
            var farMin = renderers.Where(r => r.sharedMaterial != null && Is(r.sharedMaterial, "BorderRepair/Exterior/FarSilhouette"))
                .Select(r => Vector2.Distance(new Vector2(r.bounds.center.x, r.bounds.center.z), new Vector2(SkylineCentre.x, SkylineCentre.z)) - new Vector2(r.bounds.extents.x, r.bounds.extents.z).magnitude)
                .DefaultIfEmpty(150f).Min();
            var craneMax = holder.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Dock portal crane", StringComparison.Ordinal))
                .Select(t => Vector2.Distance(new Vector2(t.position.x, t.position.z), new Vector2(SkylineCentre.x, SkylineCentre.z))).DefaultIfEmpty(0f).Max();
            float radius = Mathf.Max(craneMax + 14f, farMin * .8f, 60f);
            var veilShader = Shader.Find("BorderRepair/Exterior/AirVeil");
            if (veilShader != null)
            {
                var g = Group(holder, "Air veil - between the middle distance and the skyline");
                g.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var mesh = new Mesher();
                const int seg = 64; const float y0 = -2, y1 = 55;
                Vector3 At(float a, float y) => SkylineCentre + new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius);
                for (int s = 0; s < seg; s++)
                {
                    float a = s * Mathf.PI * 2 / seg, b = (s + 1) * Mathf.PI * 2 / seg;
                    mesh.Quad(At(a, y0), At(b, y0), At(b, y1), At(a, y1), -(At(a, 0) - SkylineCentre).normalized,
                        new Vector2(s / (float)seg, 0), new Vector2((s + 1) / (float)seg, 0), new Vector2((s + 1) / (float)seg, 1), new Vector2(s / (float)seg, 1));
                }
                var veil = new Material(veilShader);
                veil.SetColor("_Color", FogTeal); veil.SetFloat("_Alpha", .2f); veil.SetFloat("_Fall", 1.6f);
                var go = MeshObj("Air veil", g, mesh.Save("Air veil ring", g), Save("Depth - air veil", veil), Vector3.zero, Quaternion.identity, false);
                go.transform.localPosition = Vector3.zero;
                done.Add("air veil ring r " + radius.ToString("F0") + " (cranes to " + craneMax.ToString("F0") + ", far masses from " + farMin.ToString("F0") + ")");
            }
            else done.Add("air veil shader missing");

            log.Append("far depth: ").Append(string.Join(", ", done)).Append('\n');
        }
    }
}

namespace BorderRepair.RepairProps.EditorTools
{
    // The dock cranes (section 25.11): read as steel in the snow night instead of black cut-outs. The steelwork's own
    // copy goes from rust brown to a faded cold industrial blue-grey (#3A474F, lifted for the dim sky light), so the sky light picks
    // out lit and shadowed faces; the dark ties and bogies come up toward the same grey; thin snow lies on the jibs'
    // sloped tops too; a dim red obstruction lamp hangs at each jib tip; a cold floodlight high on
    // the street side lights the members facing it.
    public static partial class ExteriorDistrictPass
    {
        static void CraneDetail(Transform holder)
        {
            var cranes = holder.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Dock portal crane", StringComparison.Ordinal)).ToArray();
            var copies = new Dictionary<Material, Material>();
            int steel = 0, dark = 0, tips = 0;
            var industrial = Hex("#3A474F");
            var tipLamp = Beacon("Crane jib tip lamp", AlarmRed, 1.6f, .45f, .4f);
            foreach (var crane in cranes)
            {
                foreach (var r in crane.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name == "Crane obstruction lamp" || r.name == "Cab glazing") continue;
                    var shared = r.sharedMaterials; bool hit = false;
                    for (int i = 0; i < shared.Length; i++)
                    {
                        var m = shared[i];
                        if (m == null || !m.HasProperty("_BaseColor")) continue;
                        bool surface = m.shader != null && m.shader.name.EndsWith("/Surface", StringComparison.Ordinal);
                        if (!copies.TryGetValue(m, out var c))
                        {
                            c = new Material(m);
                            if (surface)
                            {
                                c.SetColor("_BaseColor", industrial * 1.75f);              // #3A474F lifted (dim sky light, thin members)
                                c.SetFloat("_TopSnow", 1f); c.SetFloat("_SnowSlope", .4f);  // thin snow on the sloped jib tops
                                c.SetFloat("_Specular", Mathf.Max(c.GetFloat("_Specular"), .25f));
                            }
                            else
                            {
                                var bc = industrial * 1.4f;
                                c.SetColor("_BaseColor", bc); if (c.HasProperty("_Color")) c.SetColor("_Color", bc);
                            }
                            copies[m] = c = Save("Crane - " + (surface ? "steelwork, cold grey" : "dark steel, lifted") + " " + copies.Count, c);
                        }
                        shared[i] = c; hit = true;
                        if (surface) steel++; else dark++;
                    }
                    if (hit) r.sharedMaterials = shared;
                }

                // Jib tip: the fly jib's vertex farthest out from the crane's slewing centre.
                var jib = crane.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name == "Fly jib" && f.sharedMesh != null);
                if (jib == null) continue;
                var root = crane.position;
                Vector3 best = Vector3.zero; float far = -1;
                foreach (var v in jib.sharedMesh.vertices)
                {
                    var w = jib.transform.TransformPoint(v);
                    float d = new Vector2(w.x - root.x, w.z - root.z).magnitude + .2f * w.y;
                    if (d > far) { far = d; best = w; }
                }
                Sphere("Crane jib tip lamp", crane, best + Vector3.down * .25f, .16f, tipLamp);
                tips++;

                // A cold working floodlight on the crane: from the street the cranes are against the sky
                // light, so without it every member is one black silhouette.
                var bounds = crane.GetComponentsInChildren<Renderer>(true).Where(r => r is MeshRenderer).Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
                var toStreet = new Vector3(-root.x, 0, -5f - root.z).normalized;
                var flood = new GameObject("Crane floodlight - quay, cold").AddComponent<Light>();
                flood.transform.SetParent(crane, true);
                // High in front, above the street's roofs (set far back at mid height it lit a near building's wall),
                // looking down on the crane: the street-side faces and the snow on the jib tops. Intensity by the
                // inverse square, about .18 at the crane.
                var at = bounds.center + toStreet * (bounds.extents.magnitude * .7f + 10f) + Vector3.up * (bounds.extents.y + 12f);
                float back = Vector3.Distance(at, bounds.center);
                flood.transform.position = at;
                flood.transform.LookAt(bounds.center);
                flood.type = LightType.Spot; flood.color = Hex("#9FB4BE"); flood.intensity = .18f * back * back;
                flood.range = back + bounds.extents.magnitude * 2f;
                flood.spotAngle = 48f; flood.innerSpotAngle = 26f; flood.shadows = LightShadows.None;
            }
            log.Append("cranes: ").Append(cranes.Length).Append(" cranes, steelwork ").Append(steel).Append(" r cold grey + snow on slopes, dark steel ")
                .Append(dark).Append(" r lifted, jib tip lamps ").Append(tips).Append('\n');
        }
    }
}
