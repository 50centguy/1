using UnityEngine;

namespace BorderRepair
{
    // Automated test runs (Unity started with -batchmode) are silent: every sound goes through the one AudioListener,
    // so its master volume at zero mutes the whole run. Play in the editor and built games never run in batch mode.
    static class BatchModeMute
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Mute()
        {
            if (Application.isBatchMode) AudioListener.volume = 0f;
        }
    }
}
