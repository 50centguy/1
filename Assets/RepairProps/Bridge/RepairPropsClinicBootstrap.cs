using BorderRepair.TwoNight;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    // The opt-in acceptance scene must not inherit an unrelated night's in-memory progress.
    [DefaultExecutionOrder(-2000)]
    public sealed class RepairPropsClinicBootstrap : MonoBehaviour
    {
        void Awake() => TwoNightRun.NewGame(null, true);
    }
}
