using UnityEngine;

namespace BorderRepair.TwoNight
{
    public enum ClinicTradeAction { Receive, Deliver, Console }

    // Attach to the art anchor, so both its proxy and item colliders resolve to this zone.
    public class ClinicTradeZone : MonoBehaviour
    {
        [SerializeField] UnifiedClinicDirector clinic;
        [SerializeField] ClinicTradeAction action;
        [SerializeField] TextMesh caption;
        public ClinicTradeAction Action => action;
        public void Configure(UnifiedClinicDirector owner, ClinicTradeAction command) { clinic = owner; action = command; }
        public bool Activate() => clinic != null && clinic.Interact(action);
        public void ConfigureLabel(TextMesh label) => caption = label;
        void LateUpdate()
        {
            var camera = Camera.main;
            if (caption != null && camera != null)
                caption.transform.rotation = Quaternion.LookRotation(caption.transform.position - camera.transform.position, camera.transform.up);
        }
    }
}
