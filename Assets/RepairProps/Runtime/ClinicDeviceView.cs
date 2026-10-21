using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BorderRepair.RepairProps
{
    // Using an in-world device, 2077 style: the view glides from where the player is to square in front of a physical
    // screen, the program runs on that screen (a world-space canvas laid on it) and the mouse works it directly; on
    // exit the view glides back. Done with a temporary camera cloned from the active one (same URP data, retro look,
    // PS2 pass), so the first-person rig and the bench inspector keep owning their own cameras.
    [AddComponentMenu("")]
    public sealed class ClinicDeviceView : MonoBehaviour
    {
        const float Travel = .45f, Fov = 30f;
        Camera cam;
        Transform source;
        Vector3 targetPosition;
        Quaternion targetRotation;
        float sourceFov, t;
        bool leaving;

        public Camera Camera => cam;
        public bool Arrived => !leaving && t >= 1f;

        /// <summary>Starts the glide toward the screen (a world-space RectTransform whose front faces -forward).</summary>
        public static ClinicDeviceView Begin(Camera from, RectTransform screen)
        {
            var go = Instantiate(from.gameObject, from.transform.position, from.transform.rotation);
            go.name = "DeviceViewCamera";
            go.tag = "Untagged";   // Camera.main stays the player's camera
            SceneManager.MoveGameObjectToScene(go, from.gameObject.scene);
            foreach (Transform child in go.transform) Destroy(child.gameObject);
            // Keep only what renders: the camera, its URP data and the clinic's retro look.
            foreach (var behaviour in go.GetComponents<Behaviour>().Reverse())
            {
                if (behaviour is Camera) continue;
                string type = behaviour.GetType().Name;
                // The PS2 pass stays attached but switched off (the PS2 runtime re-adds a missing one): its 448-line
                // virtual resolution would smear a screen read up close.
                if (type == "RetroPS2CameraEffect") { behaviour.enabled = false; continue; }
                if (type == "UniversalAdditionalCameraData" || type.Contains("Retro")) continue;
                DestroyImmediate(behaviour);
            }
            // Reading a screen: the clinic's readability profile (lighter PS2 pass, less diffusion), as at the bench.
            foreach (var behaviour in go.GetComponents<Behaviour>())
            {
                var field = behaviour.GetType().GetField("inspectionCamera");
                if (field != null && field.FieldType == typeof(bool)) field.SetValue(behaviour, true);
            }
            var view = go.AddComponent<ClinicDeviceView>();
            view.cam = go.GetComponent<Camera>();
            view.cam.depth = from.depth + 20;
            view.cam.enabled = true;
            view.source = from.transform;
            view.sourceFov = from.fieldOfView;
            var corners = new Vector3[4];
            screen.GetWorldCorners(corners);
            var centre = (corners[0] + corners[2]) * .5f;
            float width = (corners[3] - corners[0]).magnitude, height = (corners[1] - corners[0]).magnitude;
            var normal = -screen.forward;
            float aspect = Mathf.Max(.5f, (float)Screen.width / Mathf.Max(1, Screen.height));
            float half = Mathf.Tan(Fov * .5f * Mathf.Deg2Rad);
            float distance = Mathf.Max(height * .5f / (half * .88f), width * .5f / (half * aspect * .92f));
            view.targetPosition = centre + normal * distance;
            view.targetRotation = Quaternion.LookRotation(-normal, screen.up);
            return view;
        }

        public void End() => leaving = true;

        void LateUpdate()
        {
            if (cam == null) return;
            t = Mathf.Clamp01(t + (leaving ? -1 : 1) * Time.unscaledDeltaTime / Travel);
            float e = HudMotion.EaseOut(t);
            var fromPosition = source != null ? source.position : targetPosition;
            var fromRotation = source != null ? source.rotation : targetRotation;
            cam.transform.SetPositionAndRotation(Vector3.Lerp(fromPosition, targetPosition, e), Quaternion.Slerp(fromRotation, targetRotation, e));
            cam.fieldOfView = Mathf.Lerp(sourceFov, Fov, e);
            if (leaving && t <= 0f) Destroy(gameObject);
        }
    }
}
