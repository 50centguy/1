using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps
{
    // Art direction for the four-prop desktop scene (Docs/Integration/RepairPropsCyberBench/ART_DIRECTION.md), at play
    // time only. Every light gets one role and every zone one hue (cyberpunk clinic, after the CDPR ripperdoc mood
    // paintings): deep blue-cyan fluorescent room, icy-white surgical key over the table (brightest), hot orange-red at the
    // trade counter, amber work light at the bench, service teal at the robot dock; neon (ClinicCyberRuntime) supplies
    // magenta / cyan / orange accents. Deep blue-teal fog swallows far detail (the PS2-era way); the grade adds contrast,
    // bloom for neon, a touch of fringe, and no film grain (it fought the PS2 dither). White placeholder world labels
    // become small amber signs. Everything is restored when this object is destroyed.
    [AddComponentMenu("")]
    public sealed class ClinicMoodRuntime : MonoBehaviour
    {
        public const string ScenePath = RepairBenchV2Runtime.ScenePath;
        public static ClinicMoodRuntime Current { get; private set; }

        public enum Role { Unchanged, Fluorescent, AmbientFill, Practical, PracticalFill, CloseUpFill, Surgical, Retired }
        static readonly Color FluorescentTeal = new Color(.56f, .84f, .98f);    // blue-cyan tube light
        static readonly Color Tungsten = new Color(1f, .70f, .40f);             // bench amber
        static readonly Color HotTrade = new Color(1f, .42f, .22f);             // counter orange-red
        static readonly Color ServiceTeal = new Color(.45f, .95f, .82f);        // robot dock
        static readonly Color NeutralWarm = new Color(1f, .90f, .78f);
        static readonly Color SurgicalWhite = new Color(.86f, .95f, 1f);        // icy surgical key
        static readonly Color SignAmber = new Color(1f, .62f, .2f, .78f);

        readonly List<(Light light, Color color, float intensity, bool enabled)> lights = new List<(Light, Color, float, bool)>();
        readonly List<(TextMesh text, Color color, Vector3 scale)> labels = new List<(TextMesh, Color, Vector3)>();
        readonly List<Object> created = new List<Object>();
        readonly Dictionary<string, Role> roles = new Dictionary<string, Role>();
        Color sky, equator, ground, fogColor;
        bool fog;
        FogMode fogMode;
        float fogDensity;

        public IReadOnlyDictionary<string, Role> Roles => roles;
        public bool FogOn => RenderSettings.fog;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase) || UnityEngine.XR.XRSettings.isDeviceActive) return;
            var host = new GameObject("ClinicMood (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<ClinicMoodRuntime>();
        }

        public static Role RoleOf(string name, bool surgicalLampPresent)
        {
            if (name.StartsWith("BV2_", StringComparison.Ordinal)) return Role.Unchanged;                       // bench task lamp, already tungsten
            if (name.StartsWith("BGT_", StringComparison.Ordinal)) return Role.Surgical;
            if (name.StartsWith("Ceiling_Fluo", StringComparison.Ordinal)) return surgicalLampPresent ? Role.Retired : Role.Fluorescent;  // neon ring + surgical key replace it
            if (name.StartsWith("Fixture_Tube", StringComparison.Ordinal)
                || name.StartsWith("Fixture_Door", StringComparison.Ordinal) || name.StartsWith("Fixture_Unit07Cage", StringComparison.Ordinal)
                || name.StartsWith("ClinicEntranceFill", StringComparison.Ordinal)) return Role.Fluorescent;
            if (name.StartsWith("Room_Fill", StringComparison.Ordinal)) return Role.AmbientFill;
            if (name.StartsWith("Dock_ServiceLight", StringComparison.Ordinal) || name.StartsWith("ClinicTradeTaskLight", StringComparison.Ordinal)) return Role.Practical;
            if (name.StartsWith("Dock_InspectionFill", StringComparison.Ordinal) || name.StartsWith("Fixture_BenchFill", StringComparison.Ordinal)
                || name.StartsWith("Bench_UnderShelfFill", StringComparison.Ordinal)) return Role.PracticalFill;
            if (name.StartsWith("InspectionKey", StringComparison.Ordinal) || name.StartsWith("InspectionFill", StringComparison.Ordinal)) return Role.CloseUpFill;
            if (name.StartsWith("ClinicSurgicalTaskLight", StringComparison.Ordinal)) return surgicalLampPresent ? Role.Retired : Role.Surgical;
            return Role.Unchanged;
        }

        void Start()
        {
            Current = this;
            var sceneLights = gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true)).ToList();
            bool surgical = sceneLights.Any(l => l.name.StartsWith("BGT_", StringComparison.Ordinal));
            foreach (var light in sceneLights)
            {
                var role = RoleOf(light.name, surgical);
                roles[light.name] = role;
                if (role == Role.Unchanged) continue;
                lights.Add((light, light.color, light.intensity, light.enabled));
                switch (role)
                {
                    case Role.Fluorescent: light.color = FluorescentTeal; light.intensity *= .62f; break;
                    case Role.AmbientFill: light.color = new Color(.42f, .60f, .74f); light.intensity *= .8f; break;
                    case Role.Practical: light.color = Zone(light.name); light.intensity *= 1.15f; break;
                    case Role.PracticalFill: light.color = Zone(light.name); light.intensity *= .7f; break;
                    case Role.CloseUpFill: light.color = light.name.StartsWith("InspectionKey", StringComparison.Ordinal) ? NeutralWarm : Color.Lerp(NeutralWarm, Tungsten, .3f);
                        if (light.name.StartsWith("InspectionFill", StringComparison.Ordinal)) light.intensity *= .55f; break;
                    case Role.Surgical: light.color = SurgicalWhite; light.intensity = Mathf.Max(light.intensity, 5.5f); light.range = Mathf.Max(light.range, 3.4f); break;
                    case Role.Retired: light.enabled = false; break;
                }
            }

            // Cool, dim teal ambient (the room's one temperature), and distance fog of the same family.
            sky = RenderSettings.ambientSkyColor; equator = RenderSettings.ambientEquatorColor; ground = RenderSettings.ambientGroundColor;
            RenderSettings.ambientSkyColor = new Color(.09f, .15f, .21f);
            RenderSettings.ambientEquatorColor = new Color(.055f, .09f, .125f);
            RenderSettings.ambientGroundColor = new Color(.028f, .038f, .05f);
            fog = RenderSettings.fog; fogMode = RenderSettings.fogMode; fogColor = RenderSettings.fogColor; fogDensity = RenderSettings.fogDensity;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(.022f, .045f, .062f); RenderSettings.fogDensity = .12f;

            Grade("ClinicMood_RoomGrade", 30, contrast: 18f, saturation: 6f, temperature: -8f, vignette: .26f, bloom: .65f, fringe: .06f);
            Grade("ClinicMood_CloseUpGrade", 31, contrast: 6f, saturation: 2f, temperature: -2f, vignette: .08f, bloom: .3f, fringe: 0f);

            // White placeholder world labels -> small, dim amber signs.
            foreach (var text in gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<TextMesh>(true)))
            {
                labels.Add((text, text.color, text.transform.localScale));
                text.color = SignAmber;
                text.transform.localScale *= .6f;
            }
        }

        static Color Zone(string name) =>
            name.StartsWith("Dock_", StringComparison.Ordinal) ? ServiceTeal
            : name.StartsWith("ClinicTrade", StringComparison.Ordinal) ? HotTrade
            : Tungsten;

        void Grade(string name, int layer, float contrast, float saturation, float temperature, float vignette, float bloom, float fringe)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = name;
            created.Add(profile);
            var adjust = profile.Add<ColorAdjustments>(true);
            adjust.contrast.Override(contrast);
            adjust.saturation.Override(saturation);
            var white = profile.Add<WhiteBalance>(true);
            white.temperature.Override(temperature);
            white.tint.Override(0f);
            var grain = profile.Add<FilmGrain>(true);
            grain.intensity.Override(0f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(vignette);
            vig.color.Override(new Color(.01f, .018f, .016f));
            var glow = profile.Add<Bloom>(true);
            glow.intensity.Override(bloom);
            glow.threshold.Override(.85f);
            glow.scatter.Override(.72f);
            var aberration = profile.Add<ChromaticAberration>(true);
            aberration.intensity.Override(fringe);
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(transform, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 50; volume.weight = 1; volume.sharedProfile = profile;
            created.Add(go);
        }

        void OnDestroy()
        {
            foreach (var (light, color, intensity, enabled) in lights)
                if (light != null) { light.color = color; light.intensity = intensity; light.enabled = enabled; }
            foreach (var (text, color, scale) in labels)
                if (text != null) { text.color = color; text.transform.localScale = scale; }
            RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = equator; RenderSettings.ambientGroundColor = ground;
            RenderSettings.fog = fog; RenderSettings.fogMode = fogMode; RenderSettings.fogColor = fogColor; RenderSettings.fogDensity = fogDensity;
            foreach (var o in created) if (o != null) Destroy(o);
            if (Current == this) Current = null;
        }
    }
}
