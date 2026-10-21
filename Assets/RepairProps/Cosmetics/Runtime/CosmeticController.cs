using System;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.Controls;
using BorderRepair.Core;
using BorderRepair.TwoNight;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace BorderRepair.RepairProps.Cosmetics
{
    [DefaultExecutionOrder(-100)]
    public sealed class CosmeticController : MonoBehaviour
    {
        public CosmeticMode Mode {get;private set;}
        public bool IsVR {get;private set;}
        public string Feedback {get;private set;}="";
        public Color PaintColor {get;set;}=new Color(.12f,.65f,.62f,1);
        public float Radius {get;set;}=.015f;
        public float Opacity {get;set;}=.8f;
        public float Rotation {get;set;}
        public bool CanUndo=>Available && Item!=null && Item.CanUndo;
        public bool CanRedo=>Available && Item!=null && Item.CanRedo;
        public UnifiedClinicDirector Clinic {get;private set;}
        public CosmeticItem Item {get;private set;}
        public RepairPropFlow Flow {get;private set;}
        public Func<bool> VrGate;
        public bool Available=>Clinic!=null && Clinic.Initialized && Flow!=null && Item!=null &&
            Flow.Session!=null && Flow.Session==Clinic.Station.Session && !Flow.Completed &&
            Flow.Session.Stage==RepairStage.Inspect && Clinic.Station.Inspector.CurrentItem==Flow.gameObject &&
            Clinic.ConsoleOpen && Clinic.TradeState==ClinicTradeState.InRepair && !Clinic.Carrying &&
            !Clinic.Station.InputLocked && !Clinic.Counter.DialogueVisible &&
            Flow.GetState("power")==0 && Flow.GetState("fixture")==0 && Flow.IsAssembled &&
            (IsVR ? VrGate!=null&&VrGate() : Clinic.Station.Inspector.isActiveAndEnabled && Clinic.Station.Inspector.InteractionEnabled);
        CosmeticPanel desktopPanel;
        bool oldReserved,oldSuppression,oldTint,oldScan,claimed;
        float nextStamp;
        bool heldPreviously;
        bool waitForRelease;
        readonly List<RaycastResult> uiHits=new List<RaycastResult>();
        public event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {SceneManager.sceneLoaded-=Loaded;SceneManager.sceneLoaded+=Loaded;}
        static void Loaded(Scene scene,LoadSceneMode mode)
        {
            if(scene.path!="Assets/RepairProps/Scenes/RepairProps_Clinic.unity" &&
                scene.path!="Assets/RepairPropsVR/Scenes/RepairProps_Clinic_VR.unity") return;
            if(scene.GetRootGameObjects().Any(g=>g.GetComponentInChildren<CosmeticController>(true)!=null)) return;
            var obj=new GameObject("Cosmetics_Runtime");SceneManager.MoveGameObjectToScene(obj,scene);
            obj.AddComponent<CosmeticController>();
        }
        void Start()
        {
            Clinic=FindFirstObjectByType<UnifiedClinicDirector>();
            CosmeticCatalog.Reload();
            // A separate VR adapter will attach the same panel to its tracked-device canvas.
            if(gameObject.scene.path!="Assets/RepairPropsVR/Scenes/RepairProps_Clinic_VR.unity")
            {desktopPanel=gameObject.AddComponent<CosmeticPanel>();desktopPanel.Initialize(this);}
        }
        public void BindVR(Func<bool> gate) {SetMode(CosmeticMode.Off);IsVR=true;VrGate=gate;}
        public void UnbindVR() {SetMode(CosmeticMode.Off);IsVR=false;VrGate=null;}

        void Update()
        {
            if(Clinic==null || Clinic.Station==null) return;
            var root=Clinic.Station.Inspector.CurrentItem;
            var next=root!=null ? root.GetComponent<RepairPropFlow>() : null;
            if(next!=Flow)
            {
                SetMode(CosmeticMode.Off);Flow=next;Item=null;
                if(Flow!=null)
                {
                    Item=root.GetComponent<CosmeticItem>() ?? root.AddComponent<CosmeticItem>();
                    Item.Configure(Flow);
                }
                Changed?.Invoke();
            }
            if(Mode!=CosmeticMode.Off && !Available) SetMode(CosmeticMode.Off);
            if(!Available && Mode==CosmeticMode.Off && Flow!=null)
                Feedback=Flow.GetState("power")!=0 || Flow.GetState("fixture")!=0 ? "涂装前请关机并拔下测试连接。" :
                    !Flow.IsAssembled ? "复装外壳后可涂装。" : "请将物品放在维修台。";
            if(!IsVR && Mode!=CosmeticMode.Off)
            {
                var point=RepairInput.PointerPosition;
                bool blocked=RepairInput.RightHeld || OverUI(point) ||
                    desktopPanel!=null && desktopPanel.BlocksPointer(point);
                var cam=Clinic.Station.Inspector.ViewCamera;
                ProcessRay(cam.ScreenPointToRay(point),RepairInput.LeftHeld,
                    blocked||!cam.pixelRect.Contains(point),Vector3.Distance(cam.transform.position,root.transform.position)+1);
            }
            desktopPanel?.Refresh();
        }

        public bool SetMode(CosmeticMode mode)
        {
            if(mode==Mode) return true;
            if(mode!=CosmeticMode.Off && (!Available || Clinic.Station.CleaningActive&&!claimed)) return false;
            Item?.EndStroke();heldPreviously=false;waitForRelease=true;
            var station=Clinic!=null ? Clinic.Station : null;
            if(Mode==CosmeticMode.Off && mode!=CosmeticMode.Off && station!=null)
            {
                oldReserved=station.Inspector.LeftPointerReserved;
                oldSuppression=station.Scanner.InteractionSuppressed;
                oldTint=station.Scanner.ShowScannedTint;
                oldScan=station.Session.ScanModeActive;
                station.SelectTool(null);station.Session.SetScanMode(false);
                station.CleaningActive=true;station.Inspector.LeftPointerReserved=true;
                station.Scanner.InteractionSuppressed=true;station.Scanner.ShowScannedTint=false;claimed=true;
            }
            if(mode==CosmeticMode.Off && station!=null && claimed)
            {
                station.CleaningActive=false;station.Inspector.LeftPointerReserved=oldReserved;
                station.Scanner.InteractionSuppressed=oldSuppression;station.Scanner.ShowScannedTint=oldTint;
                if(station.Session.Stage==RepairStage.Inspect) station.Session.SetScanMode(oldScan);
                claimed=false;
            }
            Mode=mode;Feedback="";Changed?.Invoke();return true;
        }
        public bool ProcessRay(Ray ray,bool held,bool blocked,float maxDistance=1.25f,Transform ignoredRoot=null)
        {
            if(!held) {Item?.EndStroke();heldPreviously=false;waitForRelease=false;return false;}
            if(!Available || Mode==CosmeticMode.Off || blocked)
            {Item?.EndStroke();heldPreviously=false;waitForRelease=true;return false;}
            if(waitForRelease) return false;
            bool first=!heldPreviously;heldPreviously=true;
            if(first) Item.BeginStroke();
            if(!TryHit(ray,maxDistance,ignoredRoot,out var hit)) return false;
            if(Mode==CosmeticMode.Replace)
            {
                if(!first) return false;
                bool ok=Item.Replace(Item.SurfaceFor(hit),CosmeticCatalog.Selected,false);
                Feedback=ok ? "已替换所选外壳贴图。" : "未选择图片或操作记录已满。";return ok;
            }
            if(Mode==CosmeticMode.Decal && !first || Mode==CosmeticMode.Spray && Time.unscaledTime<nextStamp) return false;
            if(Mode==CosmeticMode.Decal && CosmeticCatalog.Selected==null) return false;
            nextStamp=Time.unscaledTime+1f/30;
            bool painted=Item.Stamp(hit,PaintColor,Radius,Opacity,Rotation,Mode==CosmeticMode.Decal ? CosmeticCatalog.Selected:null);
            if(!painted) Feedback="操作记录已满，请撤销或清除。";
            return painted;
        }
        public bool TryHit(Ray ray,float distance,Transform ignoredRoot,out RaycastHit hit)
        {
            hit=default;
            if(Item==null || !float.IsFinite(distance) || distance<=0) return false;
            Physics.SyncTransforms();
            foreach(var candidate in Physics.RaycastAll(ray,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
            {
                if(ignoredRoot!=null && candidate.transform.IsChildOf(ignoredRoot)) continue;
                // Ineligible controls and room geometry occlude; never paint the next hit through them.
                if(Item.SurfaceFor(candidate)==null) {Feedback="这里只能涂装外壳。";return false;}
                hit=candidate;return true;
            }
            return false;
        }
        public void Undo() {if(Available){Item.EndStroke();Item.Undo();}}
        public void Redo() {if(Available){Item.EndStroke();Item.Redo();}}
        public void Clear() {if(Available){Item.Clear();Feedback="已清除当前物品涂装。";}}
        public void ReplaceWholeShell()
        {
            if(Available && Mode==CosmeticMode.Replace)
                Feedback=Item.Replace(null,CosmeticCatalog.Selected,true) ? "已替换整件外壳贴图。" : "未选择图片或操作记录已满。";
        }
        bool OverUI(Vector2 position)
        {
            if(EventSystem.current==null) return false;
            uiHits.Clear();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=position},uiHits);
            return uiHits.Count>0;
        }
        void OnDisable() {SetMode(CosmeticMode.Off);}
        void OnDestroy() {SetMode(CosmeticMode.Off);CosmeticCatalog.Dispose();}
    }
}
