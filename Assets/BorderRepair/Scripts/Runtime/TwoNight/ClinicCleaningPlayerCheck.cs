using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.Cleaning;
using BorderRepair.FirstOrder;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace BorderRepair.TwoNight
{
    /// <summary>Opt-in native-player acceptance; never runs in normal gameplay or touches the user's save.</summary>
    public sealed class ClinicCleaningPlayerCheck : MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public bool passed, independentMasks, rightDragWorks, inputRestored, progressPreserved;
            public float partialProgress, rotationDegrees;
            public int surfaces;
            public string error, startedUtc, finishedUtc, gpu;
        }
        string output;
        Report report;
        Mouse mouse;
        Keyboard keyboard;
        Action mouseCleanup;
        UnifiedClinicDirector clinic;
        ClinicCleaningController cleaning;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            if (Application.isEditor) return;
            var args=Environment.GetCommandLineArgs(); int flag=Array.IndexOf(args,"-clinicCleaningCheck");
            if(flag<0) return;
            if(flag+1>=args.Length) {Application.Quit(1);return;}
            var check=new GameObject("ClinicCleaningPlayerCheck").AddComponent<ClinicCleaningPlayerCheck>();
            DontDestroyOnLoad(check); Application.runInBackground=true;
            check.output=Path.GetFullPath(args[flag+1]); Directory.CreateDirectory(check.output);
            TwoNightSave.OverrideDirectory=Path.Combine(check.output,"IsolatedSave");
            check.report=new Report {startedUtc=DateTime.UtcNow.ToString("O"),gpu=SystemInfo.graphicsDeviceName};
            check.StartCoroutine(check.Execute());
        }

        static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
        IEnumerator Execute()
        {
            var routines=new Stack<IEnumerator>(); routines.Push(Run());
            float deadline=Time.realtimeSinceStartup+120;
            while(routines.Count>0)
            {
                bool moved=false; object next=null;
                try
                {
                    Require(Time.realtimeSinceStartup<deadline,"Native cleaning check timed out.");
                    moved=routines.Peek().MoveNext(); if(moved) next=routines.Peek().Current;
                }
                catch(Exception e) {report.error=e.ToString();break;}
                if(!moved) {routines.Pop();continue;}
                if(next is IEnumerator nested) routines.Push(nested); else yield return next;
            }
            mouseCleanup?.Invoke(); if(keyboard!=null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            report.passed=report.error==null; report.finishedUtc=DateTime.UtcNow.ToString("O");
            File.WriteAllText(Path.Combine(output,"native-cleaning.json"),JsonUtility.ToJson(report,true));
            Debug.Log("[ClinicCleaningCheck] "+JsonUtility.ToJson(report)); Application.Quit(report.passed?0:1);
        }

        IEnumerator Run()
        {
            TwoNightRun.NewGame(null,true); yield return SceneManager.LoadSceneAsync(TwoNightScenes.Clinic);
            for(int i=0;i<15;i++) yield return null;
            clinic=FindFirstObjectByType<UnifiedClinicDirector>(); cleaning=FindFirstObjectByType<ClinicCleaningController>();
            Require(clinic!=null && clinic.Initialized && cleaning!=null,"Clinic cleaning bridge not loaded.");
            clinic.Robot.Flow.Rig.Walker.LeaveSystemCursor=true;
            mouse=FirstOrderAcceptanceDriver.CreateVirtualMouse(out mouseCleanup);
            keyboard=InputSystem.AddDevice<Keyboard>("CleaningNativeAcceptance"); keyboard.MakeCurrent();
            Require(clinic.Interact(ClinicTradeAction.Receive),"Cannot receive item.");
            if(clinic.Counter.DialogueVisible) clinic.Counter.DismissDialogue();
            Require(clinic.Interact(ClinicTradeAction.Console),"Cannot inspect item.");
            for(int i=0;i<10;i++) yield return null;
            var inspector=clinic.Station.Inspector; inspector.Zoom(2); inspector.SnapToTarget(); yield return null;
            Require(cleaning.Available,"Cleaning tool unavailable.");
            var surfaces=inspector.CurrentItem.GetComponentsInChildren<CleanableSurface>().Where(s=>s.Ready).ToArray();
            report.surfaces=surfaces.Length;
            report.independentMasks=surfaces.Select(s=>s.MaskTexture).Distinct().Count()==surfaces.Length;
            Require(report.independentMasks && surfaces.Length>0,"Missing or shared dirt mask.");
            Require(surfaces.All(s=>s.Target.GetComponent<MeshFilter>().sharedMesh.isReadable),"Player cleaning UV mesh is not readable.");
            yield return Capture("dirty");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.C)); yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
            Require(cleaning.Cleaning,"C input did not toggle cleaning.");
            var point=FindPoint(surfaces); var rotation=inspector.ItemAnchor.rotation;
            float wipeEnd=Time.realtimeSinceStartup+1.2f;
            while(Time.realtimeSinceStartup<wipeEnd)
            {
                InputSystem.QueueStateEvent(mouse,new MouseState {position=point+new Vector2(Mathf.Sin(Time.realtimeSinceStartup*5)*25,0),buttons=1});
                yield return null;
            }
            InputSystem.QueueStateEvent(mouse,new MouseState {position=point}); yield return null;
            report.partialProgress=cleaning.Progress;
            Require(report.partialProgress>.0001f && report.partialProgress<.9f,"Actual local wipe did not remove local dirt.");
            Require(Quaternion.Angle(rotation,inspector.ItemAnchor.rotation)<.1f,"Left wiping rotated the item.");
            yield return Capture("partial");
            InputSystem.QueueStateEvent(mouse,new MouseState {position=point,buttons=2}); yield return null;
            for(int i=0;i<12;i++)
            {
                InputSystem.QueueStateEvent(mouse,new MouseState {position=point+new Vector2(i*5,i*2),delta=new Vector2(5,2),buttons=2});
                yield return null;
            }
            InputSystem.QueueStateEvent(mouse,new MouseState {position=point}); yield return null;
            yield return new WaitForSeconds(.2f);
            report.rotationDegrees=Quaternion.Angle(rotation,inspector.ItemAnchor.rotation);
            report.rightDragWorks=report.rotationDegrees>.5f && Mathf.Approximately(cleaning.Progress,report.partialProgress);
            Require(report.rightDragWorks,"Right rotation did not work or also wiped dirt.");
            Require(!cleaning.WipeAt(new Vector2(60,40),.1f),"UI click cleaned the item.");
            clinic.ShowRoom(); yield return null;
            Require(!cleaning.Cleaning,"Leaving console did not end cleaning.");
            clinic.OpenConsole(); for(int i=0;i<5;i++) yield return null;
            report.progressPreserved=Mathf.Approximately(cleaning.Progress,report.partialProgress);
            Require(report.progressPreserved,"Reopening discarded the dirt mask.");
            Require(cleaning.SetCleaning(true),"Cannot resume cleaning.");
            inspector.ResetView(true); inspector.Zoom(2); inspector.SnapToTarget();
            foreach(var surface in surfaces) surface.CleanAll(); yield return null;
            Require(cleaning.Progress==1,"CleanAll did not finish."); yield return Capture("clean");
            cleaning.SetCleaning(false);
            report.inputRestored=!inspector.LeftPointerReserved && !clinic.Station.Scanner.InteractionSuppressed && !clinic.Station.CleaningActive;
            Require(report.inputRestored && clinic.State.Cash==500,"Cleaning modified workflow or cash.");
            foreach(var surface in surfaces) surface.ResetDirt(); yield return null;
            Require(cleaning.Progress==0,"ResetDirt did not restore initial dirt.");
        }

        Vector2 FindPoint(CleanableSurface[] surfaces)
        {
            var cam=clinic.Station.Inspector.ViewCamera; var points=new List<Vector2>(); Physics.SyncTransforms();
            for(int y=cam.pixelHeight/4;y<cam.pixelHeight*3/4;y+=6)
                for(int x=cam.pixelWidth/3;x<cam.pixelWidth*2/3;x+=6) points.Add(new Vector2(x,y));
            foreach(var point in points.OrderBy(p=>(p-new Vector2(cam.pixelWidth*.5f,cam.pixelHeight*.34f)).sqrMagnitude))
            {
                if(FirstOrderInput.IsOverUI(point)) continue;
                if(cleaning.TryGetCleaningHit(point,out var closest,out var hit) && closest.Target.name.Contains("Housing") &&
                    closest.MaskTexture.GetPixelBilinear(hit.textureCoord2.x,hit.textureCoord2.y).r>.3f) return point;
            }
            throw new InvalidOperationException("No visible dirty housing point in native player.");
        }

        IEnumerator Capture(string stage)
        {
            yield return null;
            using var frame=new ClinicOffscreenFrame(clinic.Station.Inspector.ViewCamera,1280,720);
            frame.Render(); frame.Render(); frame.SavePng(Path.Combine(output,stage+".png"));
        }
    }
}
