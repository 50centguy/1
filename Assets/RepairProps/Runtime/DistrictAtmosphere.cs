using UnityEngine;

namespace BorderRepair.RepairProps
{
    // The district's own render settings, kept in the district copy: a scene loaded additively does not bring its
    // lighting settings, so Berth04Join applies these outdoors and the clinic's own indoors.
    [DisallowMultipleComponent]
    public sealed class DistrictAtmosphere : MonoBehaviour
    {
        public bool fog = true;
        public FogMode fogMode = FogMode.ExponentialSquared;
        public Color fogColour = Color.grey;
        public float fogDensity = .025f, fogStart, fogEnd = 300;
        public Color ambientSky = Color.grey, ambientEquator = Color.grey, ambientGround = Color.black;
        public Material skybox;
        public Light sun;
        /// <summary>The copy's offset from the district's own frame (also on its DistrictMotion).</summary>
        public Vector3 offset;
        /// <summary>Where a player who falls off the map is put back: the top of the B1 stairs, facing the shop.</summary>
        public Vector3 respawn;
        public float respawnYaw;

        /// <summary>The district's review camera poses, moved with the copy (the copy keeps no cameras): for comparing
        /// the joined scene's cost with the district's from the same views.</summary>
        [System.Serializable]
        public struct View
        {
            public string name;
            public Vector3 position;
            public Quaternion rotation;
            public float fov;
        }
        public View[] reviewViews = new View[0];
    }
}
