using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Controller = UnityEngine.InputSystem.XR.XRController;

namespace BorderRepair.RepairProps.VR
{
    public sealed class VrCarryItem : MonoBehaviour
    {
        ClinicVrSession session;
        XRGrabInteractable grab;
        BoxCollider proxy;
        XRSelectFilterDelegate filter;
        InteractorHandedness heldHand;

        public void Configure(ClinicVrSession owner)
        {
            session = owner;
            var body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var proxyObject = new GameObject("VR_CarryCollider");
            proxyObject.transform.SetParent(transform, false);
            proxy = proxyObject.AddComponent<BoxCollider>();
            bool found = false;
            Bounds bounds = default;
            foreach (var mesh in GetComponentsInChildren<MeshFilter>())
            {
                if (mesh.sharedMesh == null || !mesh.gameObject.activeInHierarchy) continue;
                var b = mesh.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = transform.InverseTransformPoint(mesh.transform.TransformPoint(corner));
                    if (!found) { found = true; bounds = new Bounds(p, Vector3.zero); }
                    else bounds.Encapsulate(p);
                }
            }
            if (!found) { proxy.enabled = false; enabled = false; return; }
            proxy.center = bounds.center;
            proxy.size = bounds.size;
            grab = gameObject.AddComponent<XRGrabInteractable>();
            grab.enabled = false;
            grab.colliders.Clear();
            grab.colliders.Add(proxy);
            grab.throwOnDetach = false;
            grab.retainTransformParent = false;
            grab.useDynamicAttach = true;
            grab.movementType = XRBaseInteractable.MovementType.Kinematic;
            filter = new XRSelectFilterDelegate((hand, _) => session.Ready && TrackingValid(Hand(hand)) && session.clinic.CarriedItem == gameObject &&
                Vector3.Distance(hand.GetAttachTransform(grab).position, proxy.ClosestPoint(hand.GetAttachTransform(grab).position)) <= .25f);
            grab.selectFilters.Add(filter);
            grab.selectEntered.AddListener(Selected);
            grab.selectExited.AddListener(Released);
            grab.enabled = true;
        }

        void Released(SelectExitEventArgs args)
        {
            if (isActiveAndEnabled && session != null && session.Ready)
                StartCoroutine(SettleAfterRelease(args.isCanceled || !TrackingValid(heldHand)));
        }

        void Selected(SelectEnterEventArgs args) => heldHand = Hand(args.interactorObject);

        static InteractorHandedness Hand(IXRSelectInteractor interactor) =>
            interactor is XRBaseInteractor value ? value.handedness : InteractorHandedness.None;

        static bool TrackingValid(InteractorHandedness hand)
        {
            if (hand != InteractorHandedness.Left && hand != InteractorHandedness.Right) return false;
            var usage = hand == InteractorHandedness.Left ? CommonUsages.LeftHand : CommonUsages.RightHand;
            return InputSystem.devices.OfType<Controller>().Any(d => d.usages.Contains(usage) && d.isTracked.isPressed);
        }

        void Update()
        {
            if (grab == null || !grab.isSelected || TrackingValid(heldHand)) return;
            foreach (var interactor in grab.interactorsSelecting.ToArray())
                grab.interactionManager.SelectCancel(interactor, (IXRSelectInteractable)grab);
        }

        IEnumerator SettleAfterRelease(bool canceled)
        {
            // Resume after XRI's release cycle; works in batch-mode tests as well as rendered frames.
            yield return null;
            if (session == null || !session.Ready || grab.isSelected) yield break;
            if (canceled || !session.TryDock(gameObject)) session.ReturnToIntake(gameObject);
        }

        public void Suspend()
        {
            if (grab != null) grab.enabled = false;
            if (proxy != null) proxy.enabled = false;
        }

        void OnDestroy()
        {
            if (grab == null) return;
            grab.selectExited.RemoveListener(Released);
            grab.selectEntered.RemoveListener(Selected);
            if (filter != null) grab.selectFilters.Remove(filter);
        }
    }
}
