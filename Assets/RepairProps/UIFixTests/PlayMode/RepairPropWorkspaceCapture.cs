using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.UI.Tests
{
    // Test-driven composite of the live inspection camera plus every active overlay canvas, re-targeted to that camera
    // for one render. UI coverage is measured from the pixel difference against a camera-only render of the same frame.
    internal static class RepairPropWorkspaceCapture
    {
        public static string Folder => Environment.GetEnvironmentVariable("BORDER_REPAIR_UI_SHOTS");
        public static bool Enabled => !string.IsNullOrEmpty(Folder);
        static readonly List<Shot> shots = new List<Shot>();

        [Serializable] public sealed class Shot
        {
            public string file, state, kind = "test-driven composite: live inspection camera + overlay canvases";
            public int requestedWidth, requestedHeight, actualWidth, actualHeight;
            public float itemUiCoverage, parkedUiCoverage, freeSpanFraction, uiCoverage;
            public int parkedParts;
        }
        [Serializable] sealed class ShotList { public List<Shot> shots = new List<Shot>(); }

        public static Shot Capture(Camera camera, string state, Vector2Int requested, IEnumerable<Renderer> item, IEnumerable<Transform> parked)
        {
            int width = Screen.width, height = Screen.height;
            var overlays = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(c => c.isActiveAndEnabled && c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
                .Select(c => (canvas: c, camera: c.worldCamera, plane: c.planeDistance)).ToArray();
            var target = RenderTexture.GetTemporary(width, height, 24);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            Color32[] plain, composite;
            byte[] png;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                plain = pixels.GetPixels32();
                foreach (var o in overlays)
                {
                    o.canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    o.canvas.worldCamera = camera;
                    o.canvas.planeDistance = camera.nearClipPlane + .1f;
                }
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                composite = pixels.GetPixels32();
                png = pixels.EncodeToPNG();
            }
            finally
            {
                foreach (var o in overlays)
                { o.canvas.renderMode = RenderMode.ScreenSpaceOverlay; o.canvas.worldCamera = o.camera; o.canvas.planeDistance = o.plane; }
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(pixels);
                Canvas.ForceUpdateCanvases();
            }
            var covered = new bool[width * height];
            int coveredCount = 0;
            for (int i = 0; i < covered.Length; i++)
            {
                var a = plain[i]; var b = composite[i];
                covered[i] = Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 18;
                if (covered[i]) coveredCount++;
            }
            var itemRect = ScreenBounds(camera, item);
            // Mean coverage over each visible parked proxy, so one far-away part cannot dilute a covered one.
            var parkedRects = parked.Select(p => ScreenBounds(camera, p.GetComponentsInChildren<Renderer>())).Where(r => r.width > 0 && r.height > 0).ToList();
            var shot = new Shot
            {
                state = state, requestedWidth = requested.x, requestedHeight = requested.y, actualWidth = width, actualHeight = height,
                uiCoverage = coveredCount / (float)covered.Length,
                itemUiCoverage = Coverage(covered, width, height, itemRect),
                parkedUiCoverage = parkedRects.Count == 0 ? 0 : parkedRects.Average(r => Coverage(covered, width, height, r)),
                parkedParts = parkedRects.Count,
                freeSpanFraction = FreeSpan(covered, width, height, itemRect) / (float)width
            };
            if (Enabled)
            {
                Directory.CreateDirectory(Folder);
                shot.file = state + "-requested-" + requested.x + "x" + requested.y + "-actual-" + width + "x" + height + ".png";
                File.WriteAllBytes(Path.Combine(Folder, shot.file), png);
                shots.Add(shot);
                File.WriteAllText(Path.Combine(Folder, "captures.json"), JsonUtility.ToJson(new ShotList { shots = shots }, true));
                TestContext.WriteLine("UI composite " + shot.file + " itemUi=" + shot.itemUiCoverage.ToString("0.000")
                    + " parkedUi=" + shot.parkedUiCoverage.ToString("0.000") + " freeSpan=" + shot.freeSpanFraction.ToString("0.000"));
            }
            return shot;
        }

        public static Rect ScreenBounds(Camera camera, IEnumerable<Renderer> renderers)
        {
            bool any = false; Vector2 min = default, max = default;
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    var p = camera.WorldToScreenPoint(corner);
                    if (p.z <= 0) continue;
                    if (!any) { min = max = p; any = true; }
                    else { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
                }
            }
            return any ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : Rect.zero;
        }

        static float Coverage(bool[] covered, int width, int height, Rect rect)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(rect.xMin), 0, width), x1 = Mathf.Clamp(Mathf.CeilToInt(rect.xMax), 0, width);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(rect.yMin), 0, height), y1 = Mathf.Clamp(Mathf.CeilToInt(rect.yMax), 0, height);
            int total = 0, hit = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++) { total++; if (covered[y * width + x]) hit++; }
            return total == 0 ? 0 : hit / (float)total;
        }

        // Widest run of columns that UI leaves at least 90% clear across the item's vertical band.
        static int FreeSpan(bool[] covered, int width, int height, Rect band)
        {
            int y0 = Mathf.Clamp(Mathf.FloorToInt(band.yMin), 0, height), y1 = Mathf.Clamp(Mathf.CeilToInt(band.yMax), 0, height);
            if (y1 <= y0) { y0 = height / 4; y1 = height * 3 / 4; }
            int best = 0, run = 0;
            for (int x = 0; x < width; x++)
            {
                int hit = 0;
                for (int y = y0; y < y1; y++) if (covered[y * width + x]) hit++;
                if (hit <= (y1 - y0) * .1f) { run++; best = Mathf.Max(best, run); } else run = 0;
            }
            return best;
        }
    }
}
