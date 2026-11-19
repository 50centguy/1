using System;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.RepairProps;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // Docs/Art/INTERIOR_B1_PASS.md section 25, on the playable copy only (the district scene is not touched):
    //   - ColourScript: the palette (Docs/Art/PALETTE_BR2049_PS2.md) put back over what later passes brought in. Outside
    //     the cold fog holds the frame and amber is the clinic's door and windows (the noodle bar a dimmer second); holo
    //     cyan is only on holograms (the one magenta ad and its lens turn cyan, the skyline's magenta neon is off, its
    //     cyan and amber dimmed, the searchlights halved); the neighbours' reds, greens and blues fade into the rust and
    //     teal-grey families; the orange pipe paint is rust. Inside, both battens are the same cool tube, the street lamp
    //     through the window is a third as strong and grey, the dock fill and bounce lose their blue. The B1 runtimes
    //     take the rest (B1Palette). Materials are copied into the playable's Materials folder and only this scene's
    //     renderers point at the copies.
    //   - GlowFix: the district's emissive materials never had their _EMISSION keyword (GI flag None, which URP 17 turns
    //     into "keyword off"); the playable's renderers point at copies that glow, the street's at controlled strengths.
    //   - B1Cyber: restrained cyber dressing in the B1 (style guide v2: second-hand, leased tech; each hologram has its
    //     projector and a purpose; screens CRT green; no glowing strips): the counter's queue number projected from the
    //     ceiling, a portable CRT on the sill cabinet showing the port authority's channel, a biomonitor with a running
    //     trace on the surgery bay wall, a spare leased forearm on a hook, the bay's power from a junction box (cables
    //     along the ceiling conduit, one along the floor to the table), and the gate's lock panel.
    public static partial class ExteriorDistrictPass
    {
        const string PlayableTexDir = PlayableDir + "/Textures";

        static Texture2D PTex(string name, bool repeat = false)
        {
            var path = PlayableTexDir + "/" + name + ".png";
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp == null) { log.Append("texture missing: ").Append(path).Append('\n'); return null; }
            var wrap = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            if (imp.textureType != TextureImporterType.Default || !imp.sRGBTexture || !imp.alphaIsTransparency || !imp.mipmapEnabled || imp.wrapMode != wrap || imp.maxTextureSize != 1024)
            {
                imp.textureType = TextureImporterType.Default; imp.sRGBTexture = true; imp.alphaIsTransparency = true; imp.mipmapEnabled = true;
                imp.filterMode = FilterMode.Bilinear; imp.maxTextureSize = 1024; imp.wrapMode = wrap;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Same brightness, the given hue and saturation.
        static Color Muted(Color c, float hue, float sat, float value = 1)
        {
            Color.RGBToHSV(c, out _, out _, out var v);
            var o = Color.HSVToRGB(hue / 360f, sat, Mathf.Min(1, v * value));
            o.a = c.a;
            return o;
        }

        static void SetAlbedo(Material m, Color c)
        {
            foreach (var p in new[] { "_BaseColor", "_Color" }) if (m.HasProperty(p)) m.SetColor(p, c);
        }

        static void SetTextures(Material m, Texture t)
        {
            if (t == null) return;
            foreach (var p in new[] { "_BaseMap", "_MainTex", "_EmissionMap" }) if (m.HasProperty(p) && m.GetTexture(p) != null) m.SetTexture(p, t);
        }

        static void ScaleEmission(Material m, float k)
        {
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", m.GetColor("_EmissionColor") * k);
        }

        // ------------------------------------------------------------------------------------------------ colour script
        static void ColourScript(Transform holder)
        {
            var lights = holder.GetComponentsInChildren<Light>(true);
            var renderers = holder.GetComponentsInChildren<Renderer>(true);
            var done = new List<string>();

            void L(string name, Color? colour, float? intensity)
            {
                int n = 0;
                foreach (var l in lights.Where(l => l.name == name))
                {
                    if (colour.HasValue) l.color = colour.Value;
                    if (intensity.HasValue) l.intensity = intensity.Value;
                    n++;
                }
                done.Add(name + (n == 1 ? "" : " (" + n + ")"));
            }
            // Second round (the user: too cold, inside and on the street): the tubes are warm white, not mint; fills and
            // bounces neutral; the street's lamps sodium-warm; only the sky fill keeps a trace of cool.
            var tube = Hex("#FFF1DC");
            // Inside: one tube colour for both battens.
            L("Batten - workshop light", tube, 4.2f);
            L("Batten - workshop ceiling halo light", tube, .45f);
            L("Batten - waiting side light", tube, null);
            L("Batten - waiting side ceiling halo light", tube, null);
            L("Dim alcove fill", Hex("#FFF3E2"), null);
            L("Cool room bounce", Hex("#B0AAA2"), null);
            L("Low dock fill", Hex("#D8D2C8"), null);
            // The street lamp through the window (a third as strong as before): the sodium lamp outside.
            L("Street lamp through the window light", Hex("#E8C9A0"), 22f);
            // Outside: lamps sodium-warm, the gate's spill and the stair warm-grey, the sky fill a cool grey.
            L("Aged sodium street lamp", Hex("#FFC98A"), null);
            L("Wharf amber pool", Hex("#FFD29A"), null);
            L("Court wall sconce", Hex("#F2DDBE"), null);
            L("Store spill light", Hex("#F0F0EA"), null);
            L("Vending glow", Hex("#E6E8E2"), null);
            L("Street spill through the gate light", Hex("#C8BBA6"), null);
            L("Stair ambient", Hex("#B4B2AC"), null);
            L("Street fill", Hex("#B4B8B8"), null);
            L("Blue overcast at upper landing", Hex("#AEB4B6"), null);
            L("Cold overcast sky light", Hex("#B2B6B6"), null);
            L("Noodle bar spill light", null, 1.5f);
            // The flickering lamps keep their colour in DistrictMotion (it sets the light every frame): follow the lights.
            int synced = 0;
            foreach (var motion in holder.GetComponentsInChildren<DistrictMotion>(true))
            {
                foreach (var lamp in motion.lamps) if (lamp.light != null && lamp.colour != lamp.light.color) { lamp.colour = lamp.light.color; synced++; }
                EditorUtility.SetDirty(motion);
            }
            done.Add("flicker lamp colours (" + synced + ")");
            L("Pharmacy cross light", null, .35f);

            // The workshop batten's tubes: the waiting side's cool white.
            var coolTube = renderers.FirstOrDefault(r => r.name == "Tube" && r.transform.parent != null && r.transform.parent.name == "Batten - waiting side")?.sharedMaterial;
            int tubes = 0;
            if (coolTube != null)
                foreach (var r in renderers.Where(r => r.name == "Tube" && r.transform.parent != null && r.transform.parent.name == "Batten - workshop")) { r.sharedMaterial = coolTube; tubes++; }
            done.Add("workshop batten tubes cool white (" + tubes + ")");

            var copies = new Dictionary<Material, Material>();
            void Recolour(string tag, Func<Renderer, Material, bool> match, Action<Material> edit)
            {
                int n = 0, k = 0;
                foreach (var r in renderers)
                {
                    var shared = r.sharedMaterials; bool hit = false;
                    for (int i = 0; i < shared.Length; i++)
                    {
                        var m = shared[i];
                        if (m == null || !match(r, m)) continue;
                        if (!copies.TryGetValue(m, out var c))
                        {
                            c = new Material(m);
                            edit(c);
                            copies[m] = c = Save("Palette - " + tag + (k++ == 0 ? "" : " " + k), c);
                        }
                        shared[i] = c; hit = true;
                    }
                    if (hit) { r.sharedMaterials = shared; n++; }
                }
                done.Add(tag + " (" + n + " r)");
            }
            // The neighbours' materials are this scene's own (named after their shader), so they are told apart by the
            // shop they belong to, their texture and their colour.
            bool Under(Renderer r, string group) { for (var t = r.transform; t != null; t = t.parent) if (t.name.StartsWith(group, StringComparison.Ordinal)) return true; return false; }
            string TexOf(Material m) { foreach (var p in new[] { "_BaseMap", "_MainTex", "_EmissionMap" }) if (m.HasProperty(p) && m.GetTexture(p) != null) return m.GetTexture(p).name; return ""; }
            string Fam(Material m, string p)
            {
                if (!m.HasProperty(p)) return "";
                var c = m.GetColor(p);
                Color.RGBToHSV(c, out var h, out var s, out var v);
                h *= 360;
                if (s < .3f || c.maxColorComponent < .06f) return "neutral";
                return h < 15 || h >= 345 ? "red" : h < 48 ? "amber" : h < 70 ? "yellow" : h < 160 ? "green" : h < 200 ? "cyan" : h < 255 ? "blue" : "magenta";
            }
            bool ShaderIs(Material m, string name) => m.shader != null && m.shader.name == "BorderRepair/Exterior/" + name;

            // Holo cyan only on holograms: the prosthetics ad and its projector lens were magenta.
            Recolour("holo ad", (r, m) => ShaderIs(m, "Hologram") && Fam(m, "_Color") == "magenta", c => { c.SetColor("_Color", HoloCyan); c.SetFloat("_Intensity", 1.15f); });
            Recolour("holo ad lens", (r, m) => ShaderIs(m, "Beacon") && Fam(m, "_Color") == "magenta" && r.name != "Ring neon - magenta", c => c.SetColor("_Color", HoloCyan));
            // The skyline: no magenta; the cyan and amber neon bands and the searchlights quieter.
            int off = 0;
            foreach (var r in renderers.Where(r => r.name == "Ring neon - magenta")) { r.gameObject.SetActive(false); off++; }
            done.Add("ring neon magenta off (" + off + ")");
            Recolour("ring neon cyan", (r, m) => r.name == "Ring neon - cyan" && m.HasProperty("_Intensity"), c => c.SetFloat("_Intensity", c.GetFloat("_Intensity") * .55f));
            Recolour("ring neon amber", (r, m) => r.name == "Ring neon - amber" && m.HasProperty("_Intensity"), c => c.SetFloat("_Intensity", c.GetFloat("_Intensity") * .6f));
            Recolour("sky searchlights", (r, m) => ShaderIs(m, "SkyBeam"), c => c.SetFloat("_Intensity", c.GetFloat("_Intensity") * .5f));
            Recolour("horizon glow", (r, m) => ShaderIs(m, "HorizonGlow"),
                c => { c.SetColor("_Low", Color.Lerp(c.GetColor("_Low"), FogTeal, .5f)); c.SetFloat("_Intensity", c.GetFloat("_Intensity") * .85f); });
            // Pipe paint (the B1's ceiling conduit and the street lamp arms) was a bright orange: cold rust.
            Recolour("pipe paint, cold rust", (r, m) => m.name == "M_UC_Pipe", c => { var k = ColdRust * 1.1f; k.a = 1; SetAlbedo(c, k); });
            // The noodle bar's reds fade to terracotta (still the noodle bar, now in the amber family); its red neon
            // blade burns amber; the paper lantern glows warm instead of red.
            const string noodles = "Shop across - noodle", store = "Shop across - conven", pharmacy = "Shop down the road";
            Recolour("noodle bar, red to terracotta", (r, m) => Under(r, noodles) && TexOf(m) != "nb_blade_noodle" && Fam(m, "_BaseColor") == "red", c =>
            {
                SetAlbedo(c, Muted(c.GetColor("_BaseColor"), 18, .36f, .95f));
                // (GlowFix turns the keyword on afterwards.)
                if (c.HasProperty("_EmissionColor") && c.GetColor("_EmissionColor").maxColorComponent > .2f) c.SetColor("_EmissionColor", new Color(1f, .55f, .3f) * 1.1f);
            });
            Recolour("noodle bar, neon blade amber", (r, m) => Under(r, noodles) && TexOf(m) == "nb_blade_noodle", c => { SetTextures(c, PTex("nb_blade_noodle_muted")); ScaleEmission(c, .8f); });
            // The convenience store: green shutter teal-grey, blue crates grey-blue, its sign and goods half as loud.
            Recolour("store, green to teal-grey", (r, m) => Under(r, store) && Fam(m, "_BaseColor") == "green", c => SetAlbedo(c, Muted(c.GetColor("_BaseColor"), 165, .14f)));
            Recolour("store, blue to grey-blue", (r, m) => Under(r, store) && Fam(m, "_BaseColor") == "blue", c => SetAlbedo(c, Muted(c.GetColor("_BaseColor"), 205, .3f, .9f)));
            Recolour("store sign", (r, m) => TexOf(m) == "nb_store_sign", c => { SetTextures(c, PTex("nb_store_sign_muted")); ScaleEmission(c, .85f); });
            Recolour("store interior", (r, m) => TexOf(m) == "nb_store_interior", c => SetTextures(c, PTex("nb_store_interior_muted")));
            Recolour("pharmacy cross", (r, m) => Under(r, pharmacy) && TexOf(m) == "nb_pharmacy_cross", c => ScaleEmission(c, .5f));
            // The lamp cones: sodium-warm like their lamps.
            Recolour("lamp cones warm", (r, m) => ShaderIs(m, "LampVolume"), c => c.SetColor("_Color", Color.Lerp(c.GetColor("_Color"), Hex("#FFD7A8"), .7f)));
            // The air: the street's ambient was blue (.31, .35, .40) and the fog teal; both lay over everything, the B1
            // too through the join. Neutral grey ambient at the same brightness, the fog a grey with only a trace of cool.
            var air = Object.FindFirstObjectByType<DistrictAtmosphere>();
            if (air != null)
            {
                air.ambientSky = air.ambientEquator = air.ambientGround = new Color(.35f, .345f, .34f);
                air.fogColour = new Color(.27f, .285f, .29f);
                EditorUtility.SetDirty(air);
                done.Add("air neutral (ambient, fog)");
            }
            log.Append("colour script: ").Append(string.Join(", ", done)).Append('\n');
        }

        // ------------------------------------------------------------------------------------------------ glow fix
        // No bulb, tube, lamp head, lit window, sign or screen of the district ever glowed: the builder gave its emissive
        // materials the GI flag None, and URP 17 re-derives _EMISSION from that flag whenever it validates a material
        // (on creation, on import), so the keyword never reached disk and the lights shone from dark fittings. Lit uses
        // the realtime-emissive flag now (the scenes have no realtime GI, so it changes nothing else); the district's
        // existing materials are not rebuilt here, so in the copy their renderers point at copies with the flag and the
        // keyword:
        //   - in the B1 all of them; the festive string's green, blue and red bulbs come into the amber family.
        //   - on the street the light-motivated ones at their designed strength (lamp heads, lit windows, the clinic's
        //     entrance; the brightest lit windows at .7, flat peach panels at full), the screens a little lower, the
        //     neighbours' signs and shop windows and the vending machine clearly below the clinic's door, the pharmacy's
        //     green cross lowest (it was the most saturated thing in the street). The district wayfinding signs (one was changed outside these passes) and
        //     materials these passes did not make stay as they are.
        static readonly (string prefix, float scale)[] StreetGlow =
        {
            ("Lamp_head_-_", 1f), ("Window_-_warm_dim", 1f), ("Window_-_warm", .7f), ("Window_-_", 1f), ("Wheelhouse_window", 1f), ("Ent_-_", 1f), ("Ship_-_canopy", 1f),
            ("Charge_screen", .8f), ("Shore_terminal_screen", .8f), ("Locker_screen", .8f),
            ("Nb_-_", .65f), ("Palette_-_noodle", .65f), ("Palette_-_store", .65f), ("Palette_-_pharmacy", .45f), ("Vending_front_-_", .55f),
            ("Blade_sign_enamel", .8f), ("Junction_arrow_enamel", 1f), ("Street_plate_enamel", 1f),
        };
        static readonly string[] GlowSkip = { "Jp_-_", "Berth_board_-_cold_enamel", "Lamp_head_-_cold_mercury", "Window_-_kitchen" };
        static readonly Dictionary<string, Color> GlowTint = new Dictionary<string, Color>
        {
            { "Int_-_festive_bulb_1", new Color(1.30f, .66f, .32f) },
            { "Int_-_festive_bulb_2", new Color(1.30f, 1.02f, .58f) },
            { "Int_-_festive_bulb_4", new Color(1.05f, .95f, .80f) },
            { "Int_-_fluorescent_tube__cool_white", new Color(1.36f, 1.28f, 1.14f) },   // warm white like the light it gives
        };

        static void GlowFix(Transform holder)
        {
            var copies = new Dictionary<Material, Material>();
            var users = new Dictionary<Material, (bool b1, List<string> names)>();
            int b1 = 0, street = 0;
            var left = new SortedSet<string>();
            foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                bool inB1 = B1Room.Contains(r.bounds.center);
                var shared = r.sharedMaterials; bool hit = false;
                for (int i = 0; i < shared.Length; i++)
                {
                    var m = shared[i];
                    if (m == null || m.shader == null || !m.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal) || !m.HasProperty("_EmissionColor")
                        || m.IsKeywordEnabled("_EMISSION") || m.GetColor("_EmissionColor").maxColorComponent < .02f) continue;
                    // Only this builder's materials (other props' FX materials switch their own glow).
                    var path = AssetDatabase.GetAssetPath(m);
                    if (!path.StartsWith("Assets/RepairProps/Scenes/Temp_", StringComparison.Ordinal) && !path.StartsWith(PlayableDir + "/", StringComparison.Ordinal)) continue;
                    var file = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (GlowSkip.Any(file.StartsWith)) { left.Add(file); continue; }
                    float scale = 1;
                    var rule = StreetGlow.FirstOrDefault(g => file.StartsWith(g.prefix, StringComparison.Ordinal));
                    if (rule.prefix != null) scale = rule.scale;
                    else if (!inB1) { left.Add(file); continue; }
                    if (!copies.TryGetValue(m, out var c))
                    {
                        c = Save("Glow - " + file, new Material(m));
                        var e = GlowTint.TryGetValue(file, out var tint) ? tint : m.GetColor("_EmissionColor") * scale;
                        c.SetColor("_EmissionColor", e);
                        c.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                        c.EnableKeyword("_EMISSION");
                        EditorUtility.SetDirty(c);
                        copies[m] = c;
                        users[m] = (inB1, new List<string>());
                    }
                    if (users[m].names.Count < 3 && !users[m].names.Contains(r.name)) users[m].names.Add(r.name);
                    shared[i] = c; hit = true;
                }
                if (hit) { r.sharedMaterials = shared; if (inB1) b1++; else street++; }
            }
            log.Append("glow fix: renderers B1 ").Append(b1).Append(", street ").Append(street).Append('\n');
            foreach (var kv in users)
                log.Append("  glow ").Append(kv.Value.b1 ? "B1     " : "street ").Append(System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(kv.Key)))
                   .Append(" -> #").Append(ColorUtility.ToHtmlStringRGB(copies[kv.Key].GetColor("_EmissionColor") / Mathf.Max(1, copies[kv.Key].GetColor("_EmissionColor").maxColorComponent)))
                   .Append(" x").Append(copies[kv.Key].GetColor("_EmissionColor").maxColorComponent.ToString("F2")).Append(" (").Append(string.Join(", ", kv.Value.names)).Append(")\n");
            log.Append("  left as they are: ").Append(string.Join(", ", left)).Append('\n');
        }

        // ------------------------------------------------------------------------------------------------ B1 cyber
        static void B1Cyber(Transform holder)
        {
            var pass = holder.GetComponentsInChildren<Transform>(true).First(t => t.name.StartsWith("09 Interior pass", StringComparison.Ordinal));
            var all = pass.GetComponentsInChildren<Transform>(true);
            Transform Named(string name) => all.FirstOrDefault(t => t.name == name && B1Room.Contains(BoundsOf(t).center));
            var root = new GameObject("B1 cyber (generated)").transform;
            root.SetParent(holder, true);
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var made = new List<string>();

            var steel = Worn("B1 - dark steel", Hex("#2D3133"), .4f, .5f);
            var grey = Worn("B1 - grey enamel, chipped", Hex("#4C5254"), .35f, .3f);
            var black = Lit("B1 - black rubber", Hex("#141516"), .35f);
            var chrome = Worn("B1 - chrome, dull", Hex("#9A9C9A"), .7f, .9f);
            var tag = Lit("B1 - lease tag, NSP PORT 0412", Color.white, .25f, 0, PTex("b1_lease_tag"));
            var runLed = Beacon("B1 - run LED, CRT green", CrtGreen, 1.6f, 0, 1);
            var deadLed = Lit("B1 - fault LED, off", Hex("#3A1414"), .6f);
            Material ScreenMat(string name, string tex, float glow, bool repeat = false, Vector2? tiling = null)
            {
                if (mats.TryGetValue(name, out var have)) return have;
                var t = PTex(tex, repeat);
                var m = Lit(name, new Color(.06f, .07f, .065f), .85f, 0, t, Color.white * glow, t);
                if (tiling.HasValue) { m.SetTextureScale("_BaseMap", tiling.Value); m.SetTextureScale("_EmissionMap", tiling.Value); EditorUtility.SetDirty(m); }
                return m;
            }

            // -- 1. The queue number, projected from the ceiling over the counter, facing whoever works the counter.
            {
                var g = Group(root, "Holo - queue number over the counter (projector on the ceiling)");
                var at = new Vector3(.80f, 2.12f, -2.36f);
                BBox("Projector", g, new Vector3(at.x, 2.512f, at.z - .02f), new Vector3(.15f, .065f, .12f), steel);
                Cyl("Projector lens", g, new Vector3(at.x, 2.474f, at.z + .01f), .017f, .012f, Beacon("B1 - projector lens, holo cyan", HoloCyan, 1.4f, 0, 1));
                Decal("Lease sticker", g, new Vector3(at.x + .0751f, 2.512f, at.z - .02f), Vector3.right, .07f, .035f, tag, .001f);
                var holo = new Material(Shader.Find("BorderRepair/Exterior/Hologram"));
                holo.SetTexture("_MainTex", PTex("b1_holo_queue"));
                holo.SetColor("_Color", HoloCyan); holo.SetFloat("_Intensity", 1.1f); holo.SetFloat("_Haze", -1); holo.SetFloat("_Flicker", .2f);
                holo.SetFloat("_Cull", 0); holo.SetFloat("_Spin", 0);
                Decal("Hologram - 受付 07", g, at, Vector3.forward, .56f, .23f, Save("B1 - holo, queue number", holo), 0f);
                made.Add("queue hologram");
            }

            // -- 2. A portable CRT on the sill cabinet, turned toward the counter: the port authority's notice channel.
            var cabinets = Named("Under the window - low cabinets");
            var worktop = cabinets != null ? cabinets.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name == "Worktop") : null;
            if (worktop != null)
            {
                var g = Group(root, "CRT - port notice channel, on the sill cabinet");
                var top = worktop.bounds;
                var rot = Quaternion.Euler(0, -18, 0);
                var c = new Vector3(2.36f, top.max.y + .135f, top.center.z + .03f);
                Vector3 P(float x, float y, float z) => c + rot * new Vector3(x, y, z);
                var casing = Worn("B1 - CRT casing, aged beige", Hex("#6F6A60"), .4f);
                var bezel = Lit("B1 - CRT bezel, dark plastic", Hex("#262523"), .35f);
                BBox("Casing", g, P(0, 0, .02f), new Vector3(.34f, .27f, .24f), casing, rot);
                BBox("Tube back", g, P(0, .01f, -.15f), new Vector3(.24f, .19f, .1f), casing, rot);
                Box("Bezel", g, P(0, .012f, .143f), new Vector3(.3f, .225f, .006f), bezel, rot);
                Decal("Screen - 北坂港務局 告知", g, P(0, .016f, .1465f), rot * Vector3.forward, .25f, .19f, ScreenMat("B1 - CRT, port notice", "b1_crt_bulletin", 1.25f), 0f, rot * Vector3.up);
                Box("Controls", g, P(0, -.118f, .143f), new Vector3(.3f, .028f, .006f), bezel, rot);
                Cyl("Knob", g, P(-.1f, -.118f, .149f), .009f, .008f, chrome, rot * Quaternion.Euler(90, 0, 0));
                Cyl("Knob", g, P(-.07f, -.118f, .149f), .009f, .008f, chrome, rot * Quaternion.Euler(90, 0, 0));
                Sphere("Power LED", g, P(.12f, -.118f, .147f), .004f, runLed);
                Rod("Rabbit ear", g, P(-.03f, .135f, -.02f), P(-.16f, .36f, -.06f), .003f, chrome);
                Rod("Rabbit ear", g, P(.03f, .135f, -.02f), P(.15f, .35f, -.03f), .003f, chrome);
                Decal("Lease sticker", g, P(.1705f, -.02f, .04f), rot * Vector3.right, .1f, .05f, tag, .001f);
                made.Add("CRT at x " + c.x.ToString("F2"));
            }

            // -- 3. The biomonitor on the surgery bay's east wall, on a short arm, turned toward the bay's way in.
            {
                var g = Group(root, "Biomonitor - surgery bay wall");
                var n = new Vector3(-.6f, 0, -.8f).normalized;
                var rot = Quaternion.LookRotation(n);
                var m = new Vector3(4.84f, 1.56f, 1.96f);
                BBox("Wall plate", g, new Vector3(5.01f, 1.56f, 2.02f), new Vector3(.02f, .14f, .1f), steel);
                Rod("Arm", g, new Vector3(5.0f, 1.56f, 2.02f), new Vector3(4.88f, 1.56f, 1.99f), .012f, steel);
                BBox("Body", g, m, new Vector3(.3f, .22f, .07f), grey, rot);
                Box("Bezel", g, m + n * .036f, new Vector3(.28f, .19f, .004f), Lit("B1 - monitor bezel", Hex("#1C1E1E"), .4f), rot);
                var trace = Decal("Screen - ECG", g, m + n * .0385f + Vector3.up * .042f, n, .26f, .075f, ScreenMat("B1 - monitor, ECG", "b1_ecg", 1.2f, true, new Vector2(3, 1)), 0f);
                var scroll = trace.AddComponent<B1Screen>();
                scroll.tiling = new Vector2(3, 1); scroll.speed = .6f;   // two beats a tile: 72 a minute
                Decal("Screen - vitals", g, m + n * .0385f - Vector3.up * .048f, n, .26f, .065f, ScreenMat("B1 - monitor, vitals", "b1_vitals", 1.1f), 0f);
                Sphere("Run LED", g, m + n * .037f + rot * new Vector3(.125f, -.1f, 0), .004f, runLed);
                Decal("Lease sticker", g, m + rot * new Vector3(.151f, 0, 0), rot * Vector3.right, .09f, .045f, tag, .001f);
                made.Add("biomonitor");
            }

            // -- 4. A spare forearm-and-hand unit on a hook by the bay wall, leased, one panel touched up.
            {
                var g = Group(root, "Spare arm on a hook - leased");
                const float x = 4.955f, z = .61f;
                var shell = Worn("B1 - prosthetic shell, scuffed white", Hex("#B9B6AC"), .45f);
                var touch = Worn("B1 - prosthetic shell, touched-up paint", Hex("#CFCBBE"), .3f);
                var joint = Worn("B1 - prosthetic joint, brushed steel", Hex("#8A8E8C"), .55f, .8f);
                Rod("Hook", g, new Vector3(5.03f, 1.84f, z), new Vector3(4.96f, 1.86f, z), .006f, steel);
                Rod("Hook tip", g, new Vector3(4.96f, 1.86f, z), new Vector3(4.955f, 1.9f, z), .006f, steel);
                Cyl("Socket ring", g, new Vector3(x, 1.80f, z), .05f, .03f, joint);
                Cyl("Socket", g, new Vector3(x, 1.75f, z), .056f, .08f, black);
                Prim(PrimitiveType.Capsule, "Upper shell", g, new Vector3(x, 1.585f, z), new Vector3(.1f, .14f, .1f), Quaternion.identity, shell);
                Box("Touched-up panel", g, new Vector3(x - .047f, 1.6f, z), new Vector3(.006f, .09f, .05f), touch);
                Sphere("Elbow joint", g, new Vector3(x, 1.43f, z), .05f, joint);
                Prim(PrimitiveType.Capsule, "Forearm shell", g, new Vector3(x, 1.285f, z), new Vector3(.09f, .13f, .09f), Quaternion.identity, shell);
                Box("Forearm seam", g, new Vector3(x - .046f, 1.29f, z), new Vector3(.004f, .2f, .012f), Lit("B1 - prosthetic seam, black", Hex("#1C1E1E"), .4f));
                Sphere("Wrist joint", g, new Vector3(x, 1.145f, z), .036f, joint);
                BBox("Palm", g, new Vector3(x, 1.075f, z), new Vector3(.035f, .1f, .09f), shell);
                for (int i = 0; i < 4; i++)
                {
                    var f0 = new Vector3(x, 1.025f, z - .033f + i * .022f);
                    Rod("Finger", g, f0, f0 + new Vector3(-.006f, -.07f + (i == 3 ? .02f : 0), 0), .009f, joint);
                }
                Rod("Thumb", g, new Vector3(x - .01f, 1.07f, z + .045f), new Vector3(x - .03f, 1.02f, z + .07f), .01f, joint);
                // The tag on a string from the wrist.
                Rod("Tag string", g, new Vector3(x - .03f, 1.15f, z - .04f), new Vector3(x - .05f, 1.05f, z - .07f), .0015f, Lit("B1 - string, grey", Hex("#8C8A84"), .2f));
                Decal("Lease tag", g, new Vector3(x - .052f, 1.02f, z - .07f), Vector3.left, .08f, .04f, tag, .001f);
                made.Add("spare arm");
            }

            // -- 5. The bay's power: a junction box under the ceiling conduit (run lamp, lease sticker), cables looped
            // along the conduit from the dock's tray to it, and one down the wall and along the floor to the table.
            {
                var g = Group(root, "Bay power - junction box and cables");
                var box = new Vector3(3.12f, 1.98f, 2.575f);
                BBox("Junction box", g, box, new Vector3(.22f, .28f, .08f), grey);
                Box("Lid line", g, box + new Vector3(0, .1f, -.041f), new Vector3(.2f, .004f, .002f), black);
                Rod("Conduit drop", g, box + Vector3.up * .14f, new Vector3(box.x, 2.37f, box.z), .016f, grey);
                Sphere("Run LED", g, box + new Vector3(.07f, .09f, -.041f), .006f, runLed);
                Sphere("Fault LED", g, box + new Vector3(.09f, .09f, -.041f), .006f, deadLed);
                Decal("Lease sticker", g, box + new Vector3(-.03f, -.06f, -.0405f), Vector3.back, .09f, .045f, tag, .001f);
                // Loops along the conduit (y 2.405, z 2.565), tied at every half metre.
                float tieY = 2.36f, tieZ = 2.548f;
                var ties = new[] { .95f, 1.45f, 1.95f, 2.45f };
                for (int i = 0; i + 1 < ties.Length; i++)
                {
                    Tube("Cable", g, Catenary(new Vector3(ties[i], tieY, tieZ), new Vector3(ties[i + 1], tieY, tieZ), .1f + .02f * i, 10), .011f, black);
                    Tube("Cable", g, Catenary(new Vector3(ties[i], tieY + .005f, tieZ - .012f), new Vector3(ties[i + 1], tieY + .005f, tieZ - .012f), .07f, 10), .007f, grey);
                }
                foreach (var t in ties) Box("Cable tie", g, new Vector3(t, tieY + .03f, tieZ), new Vector3(.012f, .08f, .05f), black);
                Tube("Cable", g, Catenary(new Vector3(2.8f, tieY, tieZ), new Vector3(box.x - .07f, box.y + .14f, tieZ), .08f, 8), .011f, black);
                // Down the wall and along the floor to the table's base.
                Tube("Cable to the table", g, new List<Vector3>
                {
                    box + new Vector3(0, -.14f, -.01f), new Vector3(box.x, .3f, 2.567f), new Vector3(box.x + .02f, .045f, 2.53f),
                    new Vector3(3.4f, .025f, 2.38f), new Vector3(3.62f, .025f, 2.22f), new Vector3(3.8f, .06f, 2.09f)
                }, .013f, black);
                made.Add("bay power");
            }

            // -- 6. The gate's lock panel on the lining beside it: a small CRT readout, a card slot, the red release.
            {
                var g = Group(root, "Gate lock panel");
                var p = new Vector3(.92f, 1.3f, -3.38f);
                BBox("Panel", g, p, new Vector3(.11f, .16f, .035f), grey);
                Decal("Readout - 施錠", g, p + new Vector3(0, .035f, .0185f), Vector3.forward, .08f, .04f, ScreenMat("B1 - gate readout", "b1_gate_lcd", 1.0f), 0f);
                Box("Card slot", g, p + new Vector3(0, -.025f, .019f), new Vector3(.07f, .006f, .004f), black);
                Cyl("Release", g, p + new Vector3(0, -.058f, .02f), .012f, .01f, Lit("B1 - release button, alarm red", AlarmRed, .5f), Quaternion.Euler(90, 0, 0));
                Rod("Conduit", g, p + new Vector3(0, .08f, -.005f), new Vector3(p.x, 2.53f, p.z - .005f), .008f, grey);
                made.Add("gate panel");
            }

            foreach (var t in root.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            log.Append("B1 cyber: ").Append(string.Join(", ", made)).Append('\n');
        }
    }
}
