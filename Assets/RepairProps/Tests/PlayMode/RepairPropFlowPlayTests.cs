using System.Collections;
using BorderRepair.Core;
using BorderRepair.Inspection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BorderRepair.RepairProps.Tests
{
    public sealed class RepairPropFlowPlayTests
    {
        [UnityTest]
        public IEnumerator PowerTrayCarriesChildrenAndReplacementNeverSharesOldSeat()
        {
            using (var rig = new PropFlowTestRig(RepairPropKind.Power))
            {
                var tray = rig.flow.GetObject("PS_OutputTray");
                var old = rig.flow.GetObject("PS_OutputModule_Fault");
                var replacement = rig.flow.GetObject("PS_OutputModule_New");
                var home = old.localPosition;
                Assert.That(old.parent, Is.SameAs(tray));
                Assert.That(replacement.gameObject.activeSelf, Is.False);
                rig.Diagnose(); rig.OpenToModule();
                yield return null;
                Assert.That(old.parent, Is.SameAs(tray), "FBX hierarchy must remain intact.");
                Assert.That(Vector3.Distance(old.position, tray.TransformPoint(home) + Vector3.up * 0.04f), Is.LessThan(0.00001f));
                rig.Do("module.install.new");
                yield return null;
                Assert.That(old.gameObject.activeSelf, Is.False);
                Assert.That(replacement.gameObject.activeSelf, Is.True);
                Assert.That(replacement.localPosition, Is.EqualTo(home));
                var part = replacement.GetComponent<RepairPart>();
                Assert.That(part.State, Is.EqualTo(BorderRepair.Data.RepairPartState.Installed));
                Assert.That(rig.Try("tray.install"), Is.False, "Module screws must be refitted first.");
            }
        }

        [UnityTest]
        public IEnumerator OmamoriReleasedFlexAndPlugFollowRemovedModuleAndReturnHome()
        {
            using (var rig = new PropFlowTestRig(RepairPropKind.Omamori))
            {
                var flex = rig.flow.GetObject("OM_WakeFlex_Released");
                var plug = rig.flow.GetObject("OM_WakePlug");
                var flexHome = flex.position;
                var plugHome = plug.position;
                var flexParent = flex.parent;
                rig.Diagnose(); rig.OpenToModule();
                yield return null;
                Assert.That(flex.position.z, Is.EqualTo(flexHome.z - 0.022f).Within(0.00001f));
                Assert.That(plug.position.z, Is.EqualTo(plugHome.z - 0.026f).Within(0.00001f));
                Assert.That(flex.parent, Is.SameAs(flexParent));
                Assert.That(rig.flow.GetObject("OM_WakeFlex_Connected").gameObject.activeSelf, Is.False);
                Assert.That(flex.gameObject.activeSelf, Is.True);
                rig.CloseWithReplacement(); yield return null;
                Assert.That(plug.position, Is.EqualTo(plugHome));
                Assert.That(flex.gameObject.activeSelf, Is.False);
                Assert.That(rig.flow.GetObject("OM_WakeFlex_Connected").gameObject.activeSelf, Is.True);
            }
        }

        [UnityTest]
        public IEnumerator ReceiverFxRequirePowerAndDeployedStripAndPassingSignal()
        {
            using (var rig = new PropFlowTestRig(RepairPropKind.Receiver))
            {
                var noise = rig.flow.GetObject("RX_LightCurtain_Noise");
                var locked = rig.flow.GetObject("RX_LightCurtain_Locked");
                Assert.That(noise.gameObject.activeSelf, Is.False);
                rig.Do("strip.deploy"); yield return null;
                Assert.That(noise.gameObject.activeSelf, Is.True);
                Assert.That(locked.gameObject.activeSelf, Is.False);
                rig.Diagnose(); rig.OpenToModule();
                Assert.That(noise.gameObject.activeSelf, Is.False);
                rig.CloseWithReplacement(); rig.Retest(); yield return null;
                Assert.That(locked.gameObject.activeSelf, Is.True);
                Assert.That(noise.gameObject.activeSelf, Is.False);
                rig.Do("strip.fold");
                Assert.That(locked.gameObject.activeSelf, Is.False);
                Assert.That(rig.flow.Completed, Is.False, "Deploy/fold does not complete a repair.");
                Assert.That(rig.flow.Session.Stage, Is.EqualTo(RepairStage.Inspect));
            }
        }
    }
}
