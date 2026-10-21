#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.RepairProps.UI.Tests
{
    // Screen.SetResolution does not resize the editor GameView. Keep test-only fixed presets reversible.
    internal sealed class UiTestGameView : IDisposable
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.FlattenHierarchy;
        readonly EditorWindow window;
        readonly object group;
        readonly PropertyInfo selection;
        readonly int previous;
        readonly List<object> added=new List<object>();
        static Type Type(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("UnityEditor."+name,false)).First(t=>t!=null);
        static object Call(object instance,string method,params object[] args)=>instance.GetType().GetMethod(method,Flags).Invoke(instance,args);
        public UiTestGameView()
        {
            var sizes=Type("GameViewSizes").GetProperty("instance",Flags).GetValue(null);
            group=Call(sizes,"GetGroup",Enum.Parse(Type("GameViewSizeGroupType"),"Standalone"));
            window=EditorWindow.GetWindow(Type("GameView"));
            selection=window.GetType().GetProperty("selectedSizeIndex",Flags);
            previous=(int)selection.GetValue(window);
        }
        public void Set(int width,int height)
        {
            int count=(int)Call(group,"GetTotalCount");int found=-1;
            for(int i=0;i<count;i++)
            {
                var size=Call(group,"GetGameViewSize",i);
                if((int)size.GetType().GetProperty("width",Flags).GetValue(size)==width &&
                   (int)size.GetType().GetProperty("height",Flags).GetValue(size)==height)
                {
                    found=i;
                    if((string)size.GetType().GetProperty("baseText",Flags).GetValue(size)=="UI Test "+width+"x"+height) added.Add(size);
                    break;
                }
            }
            if(found<0)
            {
                var value=Activator.CreateInstance(Type("GameViewSize"),Flags,null,
                    new object[]{Enum.Parse(Type("GameViewSizeType"),"FixedResolution"),width,height,"UI Test "+width+"x"+height},null);
                Call(group,"AddCustomSize",value);added.Add(value);found=count;
            }
            selection.SetValue(window,found);window.Repaint();
        }
        public void Dispose()
        {
            selection.SetValue(window,previous);
            for(int i=(int)Call(group,"GetTotalCount")-1;i>=0;i--)
                if(added.Contains(Call(group,"GetGameViewSize",i)))
                    Call(group,"RemoveCustomSize",i);
            window.Repaint();
        }
    }
}
#endif
