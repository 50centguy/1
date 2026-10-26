using BorderRepair.TwoNight;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    // The opt-in acceptance scene must not inherit an unrelated night's in-memory progress. The one exception is the
    // menu's "continue" into night 2 after night 1 ended here: that run is resumed in this clinic, not reset.
    [DefaultExecutionOrder(-2000)]
    public sealed class RepairPropsClinicBootstrap : MonoBehaviour
    {
        void Awake()
        {
            if (RepairPropLauncher.Resuming(gameObject.scene)) return;
            TwoNightRun.NewGame(null, true);
        }
    }
}
