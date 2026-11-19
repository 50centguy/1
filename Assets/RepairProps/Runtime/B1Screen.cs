using UnityEngine;

namespace BorderRepair.RepairProps
{
    // A screen whose picture runs sideways (the surgery monitor's trace): the base / emission map's offset, per renderer.
    [AddComponentMenu("")]
    public sealed class B1Screen : MonoBehaviour
    {
        public Vector2 tiling = Vector2.one;
        public float speed = .6f;     // tiles per second
        static readonly int St = Shader.PropertyToID("_BaseMap_ST");
        Renderer screen;
        MaterialPropertyBlock block;
        public float Offset { get; private set; }

        void Awake() { screen = GetComponent<Renderer>(); block = new MaterialPropertyBlock(); }

        void Update()
        {
            if (screen == null) return;
            Offset = Mathf.Repeat(Offset + speed * Time.deltaTime, 1f);
            screen.GetPropertyBlock(block);
            block.SetVector(St, new Vector4(tiling.x, tiling.y, Offset, 0));
            screen.SetPropertyBlock(block);
        }
    }
}
