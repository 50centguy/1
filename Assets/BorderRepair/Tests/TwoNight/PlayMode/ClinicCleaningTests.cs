using System;
using System.Collections;
using System.IO;
using System.Linq;
using BorderRepair.Cleaning;
using BorderRepair.FirstOrder;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.TwoNight.Tests
{
    public sealed class ClinicCleaningTests
    {
        UnifiedClinicDirector clinic;
        ClinicCleaningController cleaning;
        Mouse mouse;
        Keyboard keyboard;
        Action mouseCleanup;
        string previousSave;
        CursorLockMode previousCursor;
        bool previousVisible;

        [UnitySetUp] public IEnumerator Setup()
        {
            previousSave=TwoNightSave.OverrideDirectory;
            TwoNightSave.OverrideDirectory=Path.Combine(Path.GetTempPath(),"ClinicCleaning_"+Guid.NewGuid().ToString("N"));
            previousCursor=Cursor.lockState; previousVisible=Cursor.visible;
            TwoNightRun.NewGame(null,true);
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic.unity",new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(TwoNightScenes.Clinic,LoadSceneMode.Single);
#endif
            for(int i=0;i<12;i++) yield return null;
            clinic=UnityEngine.Object.FindFirstObjectByType<UnifiedClinicDirector>();
            cleaning=UnityEngine.Object.FindFirstObjectByType<ClinicCleaningController>();
            Assert.IsNotNull(cleaning);
            clinic.Robot.Flow.Rig.Walker.LeaveSystemCursor=true;
            mouse=FirstOrderAcceptanceDriver.CreateVirtualMouse(out mouseCleanup);
            keyboard=InputSystem.AddDevice<Keyboard>("CleaningAcceptanceKeyboard"); keyboard.MakeCurrent();
            Assert.IsTrue(clinic.Interact(ClinicTradeAction.Receive));
            if(clinic.Counter.DialogueVisible) clinic.Counter.DismissDialogue();
            Assert.IsTrue(clinic.Interact(ClinicTradeAction.Console));
            for(int i=0;i<8;i++) yield return null;
            Assert.IsTrue(cleaning.Available);
        }

        [UnityTearDown] public IEnumerator TearDown()
        {
            if(cleaning!=null) cleaning.SetCleaning(false);
            if(keyboard!=null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            mouseCleanup?.Invoke(); TwoNightSave.OverrideDirectory=previousSave; TwoNightRun.Clear();
            Cursor.lockState=previousCursor; Cursor.visible=previousVisible;
            yield return null;
        }

        [UnityTest] public IEnumerator ActualKeyboardAndWipe_PreservesInspectInputAndShowsCleanMaterial()
        {
            var inspector=clinic.Station.Inspector;
            int clicks=0; inspector.Clicked+=_=>clicks++;
            inspector.Zoom(2); inspector.SnapToTarget(); yield return null;
            Capture("dirty");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.C)); yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
            Assert.IsTrue(cleaning.Cleaning);
            Assert.IsTrue(inspector.LeftPointerReserved);
            Assert.IsTrue(clinic.Station.Scanner.InteractionSuppressed);
            var point=FindPoint();
            Quaternion before=inspector.ItemAnchor.rotation;
            float start=cleaning.Progress;
            float wipeEnd=Time.realtimeSinceStartup+1.2f;
            while(Time.realtimeSinceStartup<wipeEnd)
            {
                InputSystem.QueueStateEvent(mouse,new MouseState {position=point+new Vector2(Mathf.Sin(Time.realtimeSinceStartup*5)*20,0),buttons=1});
                yield return null;
            }
            InputSystem.QueueStateEvent(mouse,new MouseState {position=point}); yield return null;
            Assert.Greater(cleaning.Progress,start,"Actual held mouse input must remove visible dirt.");
            Assert.Less(cleaning.Progress,.9f,"One local wipe must not clean the entire device.");
            Assert.AreEqual(0,clicks,"Cleaning release must not trigger scan clicks.");
            Assert.Less(Quaternion.Angle(before,inspector.ItemAnchor.rotation),.1f,"Left wiping must not rotate the object.");
            Capture("partial");
            float partial=cleaning.Progress;
            InputSystem.QueueStateEvent(mouse,new MouseState {position=point,buttons=2}); yield return null;
            for(int frame=0;frame<12;frame++)
            {
                InputSystem.QueueStateEvent(mouse,new MouseState {position=point+new Vector2(frame*5,frame*2),delta=new Vector2(5,2),buttons=2});
                yield return null;
            }
            InputSystem.QueueStateEvent(mouse,new MouseState {position=point}); yield return null;
            yield return new WaitForSeconds(.2f);
            Assert.Greater(Quaternion.Angle(before,inspector.ItemAnchor.rotation),.5f,"Right dragging still rotates.");
            Assert.AreEqual(partial,cleaning.Progress,"Right dragging must not erase dirt.");
            inspector.ResetView(true); inspector.Zoom(2); inspector.SnapToTarget(); yield return null;
            var surfaces=inspector.CurrentItem.GetComponentsInChildren<CleanableSurface>().Where(s=>s.Ready).ToArray();
            foreach(var surface in surfaces) surface.CleanAll();
            yield return null; Capture("clean");
            Assert.AreEqual(1,cleaning.Progress);
            cleaning.SetCleaning(false);
            Assert.IsFalse(inspector.LeftPointerReserved); Assert.IsFalse(clinic.Station.Scanner.InteractionSuppressed);
            Assert.IsFalse(clinic.Station.CleaningActive);
            foreach(var surface in surfaces) surface.ResetDirt();
            yield return null; Assert.AreEqual(0,cleaning.Progress);
            Assert.AreEqual(500,clinic.State.Cash,"Cleaning is not a delivery or an income event.");
        }

        [UnityTest] public IEnumerator UiMissAndClosedConsoleCannotClean_ProgressSurvivesReopening()
        {
            Assert.IsTrue(cleaning.SetCleaning(true));
            var point=FindPoint();
            for(int i=0;i<8;i++) cleaning.WipeAt(point,.1f);
            float value=cleaning.Progress; Assert.Greater(value,0);
            Assert.IsFalse(cleaning.WipeAt(new Vector2(-100,-100),.1f));
            Assert.AreEqual(value,cleaning.Progress);
            Assert.IsFalse(cleaning.WipeAt(new Vector2(60,40),.1f),"Cleaning button/UI must consume the pointer.");
            Assert.AreEqual(value,cleaning.Progress);
            clinic.ShowRoom(); yield return null;
            Assert.IsFalse(cleaning.Cleaning); Assert.IsFalse(cleaning.WipeAt(point,.1f));
            clinic.OpenConsole(); for(int i=0;i<3;i++) yield return null;
            Assert.AreEqual(value,cleaning.Progress);
            Assert.IsTrue(cleaning.SetCleaning(true));
            Assert.IsTrue(cleaning.WipeAt(FindPoint(),.1f));
        }

        Vector2 FindPoint()
        {
            var cam=clinic.Station.Inspector.ViewCamera;
            var surfaces=clinic.Station.Inspector.CurrentItem.GetComponentsInChildren<CleanableSurface>().Where(s=>s.Ready).ToArray();
            Physics.SyncTransforms();
            var candidates=new System.Collections.Generic.List<Vector2>();
            for(int y=cam.pixelHeight/4;y<cam.pixelHeight*3/4;y+=8)
                for(int x=cam.pixelWidth/3;x<cam.pixelWidth*2/3;x+=8) candidates.Add(new Vector2(x,y));
            foreach(var point in candidates.OrderBy(p=>(p-new Vector2(cam.pixelWidth*.5f,cam.pixelHeight*.34f)).sqrMagnitude))
                {
                    if(FirstOrderInput.IsOverUI(point)) continue;
                    if(cleaning.TryGetCleaningHit(point,out var closest,out var hit) && hit.textureCoord2!=Vector2.zero &&
                        closest.Target.name.Contains("Housing") && closest.MaskTexture.GetPixelBilinear(hit.textureCoord2.x,hit.textureCoord2.y).r>.3f) return point;
                }
            Assert.Fail("No visible cleanable body ray hit with unique cleaning UV1."); return default;
        }

        [UnityTest] public IEnumerator OpaqueScreenBlocksCleaningOfTheHousingBehindIt()
        {
            Assert.IsTrue(cleaning.SetCleaning(true));
            var screen=clinic.Station.Inspector.CurrentItem.GetComponentsInChildren<CleaningOccluder>()
                .First(o=>o.Target.name.Contains("Screen"));
            Vector2 point=clinic.Station.Inspector.ViewCamera.WorldToScreenPoint(screen.Target.bounds.center);
            float before=cleaning.Progress;
            Assert.IsFalse(cleaning.TryGetCleaningHit(point,out _,out _));
            Assert.IsFalse(cleaning.WipeAt(point,.1f)); Assert.AreEqual(before,cleaning.Progress);
            yield return null;
        }

        void Capture(string stage)
        {
            string folder="Logs/cleaning"; Directory.CreateDirectory(folder);
            using var frame=new ClinicOffscreenFrame(clinic.Station.Inspector.ViewCamera,1280,720);
            frame.Render(); frame.Render(); frame.SavePng(folder+"/"+stage+".png");
        }
    }
}
