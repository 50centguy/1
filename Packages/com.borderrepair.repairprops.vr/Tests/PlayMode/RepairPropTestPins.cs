using BorderRepair.RepairProps;
using NUnit.Framework;

// Scene tests here script the bench through panel presses and expect each press to commit at once, against the
// receiver's original fault (the RF board). For the whole assembly: the receiver's fault is pinned and the hand
// gestures are off. Tests of the other faults or of the gestures set RepairPropFlow.FaultOverride or
// RepairPropFeel.Enabled themselves and put them back.
[SetUpFixture]
public sealed class RepairPropTestPins
{
    [OneTimeSetUp]
    public void Pin()
    {
        RepairPropFlow.FaultOverride = 1;
        RepairPropFeel.Enabled = false;
    }

    [OneTimeTearDown]
    public void Unpin()
    {
        RepairPropFlow.FaultOverride = 0;
        RepairPropFeel.Enabled = true;
        RepairPropFeelInput.Simulated = false;
    }
}
