using System.Linq;
using BorderRepair.FirstOrder.Slice;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace BorderRepair.FirstOrder.Tests
{
    /// <summary>两晚切片的场景接线：打包字体、EventSystem、界面、调试 HUD 默认关；原场景不带切片组件。</summary>
    public class SliceSceneTests
    {
        const string Slice = "Assets/BorderRepair/FirstOrder/Scenes/Slice/TwoNightSlice.unity";
        const string FontPath = "Assets/BorderRepair/Art/Fonts/NotoSansSC/NotoSansSC-Regular.otf";

        [Test]
        public void BundledFont_IsDynamic_AndIncludesData()
        {
            var imp = (TrueTypeFontImporter)AssetImporter.GetAtPath(FontPath);
            Assert.IsNotNull(imp, FontPath);
            Assert.AreEqual(FontTextureCase.Dynamic, imp.fontTextureCase);
            Assert.IsTrue(imp.includeFontData, "字体数据要打进包里，不能依赖玩家电脑上的字体");
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            foreach (var c in "七号左引擎维修座断电轴承保养记录误拆右侧观察手册返回【】←")
                Assert.IsTrue(font.HasCharacter(c) || RequestAndHas(font, c), "字体里有：" + c);
        }

        static bool RequestAndHas(Font f, char c) { f.RequestCharactersInTexture(c.ToString(), 16); return f.HasCharacter(c); }

        [Test]
        public void SliceScene_IsWired()
        {
            EditorSceneManager.OpenScene(Slice, OpenSceneMode.Single);
            var es = Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            Assert.AreEqual(1, es.Length, "一个 EventSystem");
            Assert.IsNotNull(es[0].GetComponent<InputSystemUIInputModule>(), "界面输入走 Input System");
            var view = Object.FindFirstObjectByType<SliceView>();
            Assert.IsNotNull(view);
            var so = new SerializedObject(view);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Font>(FontPath), so.FindProperty("font").objectReferenceValue, "界面用打包字体");
            Assert.IsNotNull(so.FindProperty("flow").objectReferenceValue);
            Assert.IsNotNull(so.FindProperty("input").objectReferenceValue);
            var input = Object.FindFirstObjectByType<FirstOrderInput>();
            Assert.IsFalse(input.ShowHud, "调试 HUD 默认关");
            Assert.IsTrue(input.DebugKeys, "数字键保留为可选调试操作");
            Assert.AreEqual(1, Object.FindObjectsByType<FirstOrderFlow>(FindObjectsSortMode.None).Length, "只有一个维修流程状态机");
            Assert.IsNotNull(Object.FindFirstObjectByType<SlicePlayerSelfCheck>());
        }

        [TestCase("Assets/BorderRepair/FirstOrder/Scenes/Unit07FirstOrder_Test.unity")]
        [TestCase("Assets/BorderRepair/FirstOrder/Scenes/LayoutAB/Unit07FirstOrder_LayoutB.unity")]
        public void OriginalScenes_HaveNoSliceComponents(string path)
        {
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Assert.IsNull(Object.FindFirstObjectByType<SliceView>(), "原场景保留作对照，不加切片界面");
            Assert.IsTrue(Object.FindFirstObjectByType<FirstOrderInput>().ShowHud, "原场景的调试 HUD 保持原样（开）");
        }
    }
}
