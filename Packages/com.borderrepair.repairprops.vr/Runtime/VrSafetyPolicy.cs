namespace BorderRepair.RepairProps.VR
{
    // These spatial gates supplement, never replace, RepairPropFlow.CanExecute.
    public static class VrSafetyPolicy
    {
        public static bool CanWork(bool mounted, bool carrying, bool sameSession, bool locked,
            bool completed, float distanceMeters) =>
            mounted && !carrying && sameSession && !locked && !completed &&
            distanceMeters >= 0f && distanceMeters <= 1.25f;

        public static bool CanDock(float horizontalMeters, float bottomDeltaMeters) =>
            horizontalMeters >= 0f && horizontalMeters <= .45f &&
            bottomDeltaMeters >= -.12f && bottomDeltaMeters <= .4f;
    }
}
