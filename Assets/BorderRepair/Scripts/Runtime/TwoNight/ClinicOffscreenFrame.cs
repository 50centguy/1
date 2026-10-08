using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BorderRepair.TwoNight
{
    // Opt-in probes render a real camera surface because hidden native windows may have no valid backbuffer.
    public sealed class ClinicOffscreenFrame : IDisposable
    {
        sealed class CanvasState
        {
            public Canvas canvas;
            public RenderMode mode;
            public Camera camera;
            public float distance;
        }
        readonly Camera camera;
        readonly RenderTexture target, previousTarget;
        readonly bool previousEnabled;
        readonly CanvasState[] canvases;

        public ClinicOffscreenFrame(Camera source, int width, int height)
        {
            if (source == null) throw new InvalidOperationException("Missing camera for offscreen acceptance.");
            camera = source;
            previousTarget = camera.targetTexture;
            previousEnabled = camera.enabled;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;
            camera.enabled = false;
            canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(c => c.isActiveAndEnabled && c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
                .Select(c => new CanvasState { canvas = c, mode = c.renderMode, camera = c.worldCamera, distance = c.planeDistance }).ToArray();
            foreach (var state in canvases)
            {
                state.canvas.renderMode = RenderMode.ScreenSpaceCamera;
                state.canvas.worldCamera = camera;
                state.canvas.planeDistance = camera.nearClipPlane + .1f;
            }
            Canvas.ForceUpdateCanvases();
        }

        public void Render() => camera.Render();

        public void SavePng(string path)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                int lit = image.GetPixels32().Count(p => p.r > 15 || p.g > 15 || p.b > 15);
                if (lit < target.width * target.height / 1000)
                    throw new InvalidOperationException("Offscreen acceptance image is blank: " + path);
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(image); }
        }

        public void Dispose()
        {
            foreach (var state in canvases)
            {
                if (state.canvas == null) continue;
                state.canvas.renderMode = state.mode;
                state.canvas.worldCamera = state.camera;
                state.canvas.planeDistance = state.distance;
            }
            camera.targetTexture = previousTarget;
            camera.enabled = previousEnabled;
            target.Release();
            UnityEngine.Object.Destroy(target);
            Canvas.ForceUpdateCanvases();
        }
    }
}
