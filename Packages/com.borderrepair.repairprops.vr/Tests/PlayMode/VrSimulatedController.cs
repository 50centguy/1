using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace BorderRepair.RepairProps.VR.Tests
{
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct VrControllerState : IInputStateTypeInfo
    {
        public FourCC format => new FourCC('V','R','T','S');
        [InputControl(name="isTracked",layout="Button",format="BIT",bit=0)]
        [FieldOffset(0)] public byte tracked;
        [InputControl(layout="Integer")]
        [FieldOffset(4)] public int trackingState;
        [InputControl(name="devicePosition",layout="Vector3")]
        [FieldOffset(8)] public Vector3 position;
        [InputControl(name="deviceRotation",layout="Quaternion")]
        [FieldOffset(20)] public Quaternion rotation;
        [InputControl(name="gripPressed",layout="Button",format="BIT",bit=0,usage="GripButton")]
        [FieldOffset(64)] public byte gripPressed;
        [InputControl(name="triggerPressed",layout="Button",format="BIT",bit=0,usage="TriggerButton")]
        [FieldOffset(65)] public byte triggerPressed;
        [InputControl(name="grip",layout="Axis",usage="Grip")]
        [FieldOffset(68)] public float grip;
    }

    [InputControlLayout(stateType=typeof(VrControllerState))]
    public class VrSimulatedController : UnityEngine.InputSystem.XR.XRController { }
}
