using UnityEngine;

namespace RobotV4
{
    /// <summary>
    /// V4 导入验收用：切换机器人屏幕表情。只改 ScreenContent_Animated 这一个渲染器的属性块
    /// （_BaseMap 与 _EmissionMap），不改材质资产，也不影响外框、玻璃。
    /// </summary>
    [ExecuteAlways]
    public class RobotScreenFace : MonoBehaviour
    {
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");

        [SerializeField] Renderer screen;
        [SerializeField] Texture2D[] faces = new Texture2D[0];
        [SerializeField] int index;

        MaterialPropertyBlock block;

        public Renderer Screen => screen;
        public int Index => index;
        public int Count => faces != null ? faces.Length : 0;

        public void Configure(Renderer screenRenderer, Texture2D[] faceTextures)
        {
            screen = screenRenderer;
            faces = faceTextures;
            Show(index);
        }

        public void Show(int i)
        {
            if (screen == null || faces == null || faces.Length == 0) return;
            index = Mathf.Clamp(i, 0, faces.Length - 1);
            block ??= new MaterialPropertyBlock();
            screen.GetPropertyBlock(block);
            block.SetTexture(BaseMap, faces[index]);
            block.SetTexture(EmissionMap, faces[index]);
            screen.SetPropertyBlock(block);
        }

        void OnEnable() => Show(index);
    }
}
