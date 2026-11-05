using System;
using System.Collections.Generic;
using System.IO;
using Unity.XR.CoreUtils;
using Unity.XR.XREAL;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>Scan, preview, confirm, then retain a real session anchor at the selected plane pose.</summary>
[DefaultExecutionOrder(50)]
public sealed class PlaneScreenPin : MonoBehaviour
{
    public StabilizedScreen Screen;
    public ARPlaneManager Planes;
    public ARAnchorManager Anchors;
    public XROrigin Origin;
    public Material BoundaryMaterial,SelectedMaterial;
    public bool Active { get; private set; }
    public bool Selecting { get; private set; }
    public bool HasPlacement=>creating||pinned;
    public bool TrackingReady { get; private set; }
    public bool InputReady=>Active&&pinned&&TrackingReady&&Vector3.Dot(-(currentPose.rotation*Vector3.forward),Screen.Head.transform.position-currentPose.position)>0;
    public bool NeedsHands=>Active&&Selecting&&selectionReady;
    public float SizeDistance { get; private set; }=2;
    public string Stage { get; private set; }="off";
    public string Status { get; private set; }="未固定到平面";
    readonly Dictionary<TrackableId,LineRenderer> boundaries=new Dictionary<TrackableId,LineRenderer>();
    readonly HashSet<TrackableId> keep=new HashSet<TrackableId>();
    readonly List<TrackableId> stale=new List<TrackableId>();
    readonly PlanePinGesture gesture=new PlanePinGesture();
    readonly PlaneAnchorCreation creation=new PlaneAnchorCreation();
    readonly AnchorDiagnostics diagnostics=new AnchorDiagnostics();
    ARAnchor anchor;
    ARPlane candidate;
    LineRenderer preview,rayLine;
    Pose currentPose,candidatePose;
    bool creating,pinned,queuedConfirm,selectionReady;
    int side=1,aimMode,planeCount;
    float boundaryAt,reportAt,settingsAt,confirmAt=-1,stopAnchorsAt,noticeUntil,serviceReadyAt=-1,lastAnchorChange=-1,nextMapAttempt;
    bool mapConfigured;
    string mapPath="",lastAnchorError="";
    [Serializable] sealed class AnchorDiagnostics
    {
        public bool managerEnabled,serviceRunning,mapConfigured,hasAnchor,validId,pending;
        public int attempts,addedEvents,updatedEvents,removedEvents;
        public float creationElapsedSeconds,lastChangesAgeSeconds;
        public string provider,mapPath,lastError,tracking;
    }
#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaClass bridge;
#endif
    void Start()
    {
        preview=Line("Plane screen preview",SelectedMaterial,true);preview.loop=true;preview.positionCount=4;
        rayLine=Line("Plane selection ray",SelectedMaterial,true);rayLine.positionCount=2;
        Anchors.anchorsChanged+=AnchorChanges;XREALCallbackHandler.OnXREALError+=AnchorError;
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge=new AndroidJavaClass("dev.xreal.stablescreen.ScreenBridge");
#endif
    }
    LineRenderer Line(string name,Material material,bool world)
    {
        var line=new GameObject(name).AddComponent<LineRenderer>();line.sharedMaterial=material;line.useWorldSpace=world;line.startWidth=line.endWidth=.003f;line.gameObject.SetActive(false);return line;
    }
    public void BeginSelection()
    {
        ReleaseAnchor();Active=Selecting=true;creating=pinned=TrackingReady=selectionReady=false;candidate=null;queuedConfirm=false;confirmAt=-1;noticeUntil=0;lastAnchorError="";gesture.Reset();
        Stage="selecting";Status="请缓慢观察墙面或桌面，瞄准后捏合固定";Screen.PlanePlacementChanged();Report(true);
    }
    public void ConfirmAfterDelay(){if(Selecting&&Screen.PlacementTrackingReady){confirmAt=Time.unscaledTime+2;Status="2 秒后固定，请瞄准目标平面";Report(true);}}
    public void Clear(bool restore=true)
    {
        ReleaseAnchor();Active=Selecting=creating=pinned=TrackingReady=false;candidate=null;confirmAt=-1;queuedConfirm=false;gesture.Reset();
        StopPlanes();if(preview)preview.gameObject.SetActive(false);if(rayLine)rayLine.gameObject.SetActive(false);
        Stage="off";Status="未固定到平面";
        if(restore)Screen.RestoreAfterPlanePin();Report(true);
    }
    void ReleaseAnchor()
    {
        creation.Cancel();
        if(anchor){Destroy(anchor.gameObject);anchor=null;}stopAnchorsAt=Time.unscaledTime+.1f;
    }
    void LateUpdate()
    {
        float now=Time.unscaledTime;
        if(!Active){if(Anchors.enabled&&now>=stopAnchorsAt){Anchors.enabled=false;mapConfigured=false;serviceReadyAt=-1;Report(true);}return;}
#if UNITY_ANDROID && !UNITY_EDITOR
        if(bridge!=null&&now>=settingsAt){settingsAt=now+.25f;int nextSide=bridge.CallStatic<int>("handControlSide"),nextAim=bridge.CallStatic<int>("planeAimMode");if(nextSide!=side||nextAim!=aimMode){gesture.Reset();candidate=null;confirmAt=-1;}side=nextSide;aimMode=nextAim;}
#endif
        bool wasReady=TrackingReady;
        if(Screen.PlacementTrackingReady&&!Anchors.enabled){mapConfigured=false;serviceReadyAt=-1;nextMapAttempt=0;Anchors.enabled=true;}
        bool serviceReady=Screen.PlacementTrackingReady&&PrepareAnchorService(now);
        if(Selecting)
        {
            if(Screen.PlacementTrackingReady)
            {
                selectionReady=true;
                if(!Planes.enabled){Planes.requestedDetectionMode=PlaneDetectionMode.Horizontal|PlaneDetectionMode.Vertical;Planes.enabled=true;}
                Stage="selecting";FindCandidate();
                bool hand=Screen.Hands.TryGetIndexPointer(side,out _,out bool held);
                if(gesture.Sample(hand,held))queuedConfirm=true;
                if(confirmAt>=0&&now>=confirmAt){confirmAt=-1;queuedConfirm=true;}
                if(queuedConfirm){queuedConfirm=false;Confirm();}
                else if(confirmAt>=0)Status=$"{Mathf.CeilToInt(confirmAt-now)} 秒后固定，请瞄准目标平面";
                else if(now>=noticeUntil)Status=candidate?"已瞄准平面，捏合固定；也可用手机延时确认":aimMode==1?"用所选手食指射线瞄准平面；没有手部跟踪时可切换视线":"缓慢观察墙面或桌面，让视线中心落在已识别平面内";
                if(Selecting&&now>=boundaryAt){boundaryAt=now+.25f;DrawBoundaries();}
            }
            else {candidate=null;TrackingReady=false;gesture.Reset();confirmAt=-1;queuedConfirm=false;Stage="waiting-6dof";Status="等待真实 6DoF 定位，缓慢观察周围环境";if(preview)preview.gameObject.SetActive(false);if(rayLine)rayLine.gameObject.SetActive(false);foreach(var line in boundaries.Values)if(line)line.gameObject.SetActive(false);}
        }
        if(creating||pinned)
        {
            if(creating)
            {
                var decision=creation.Step(now,serviceReady&&Screen.PlacementTrackingReady,anchor,ValidAnchorId(),anchor&&anchor.pending,anchor?anchor.trackingState:TrackingState.None);
                if(decision==PlaneAnchorCreation.Decision.Attempt)
                {
                    TryCreateAnchor();
                    decision=creation.Step(now,false,anchor,ValidAnchorId(),anchor&&anchor.pending,anchor?anchor.trackingState:TrackingState.None);
                }
                if(decision==PlaneAnchorCreation.Decision.TimedOut)
                {
                    string reason=lastAnchorError.Length>0?lastAnchorError:anchor?"原生锚点未进入 Tracking":"锚点服务或注册尚未就绪";
                    ReleaseAnchor();creating=false;Selecting=true;candidate=null;gesture.Reset();Stage="selecting";Notice("锚点未能就绪："+reason+"；请重新选择");Screen.PlanePlacementChanged();
                }
            }
            TrackingReady=Screen.PlacementTrackingReady&&anchor&&ValidAnchorId()&&anchor.trackingState==TrackingState.Tracking;
            if(TrackingReady)
            {
                currentPose=new Pose(anchor.transform.position,anchor.transform.rotation);
                if(creating){creating=false;pinned=true;creation.Cancel();StopPlanes();Stage="pinned";Status="屏幕已固定到平面";Screen.PlanePlacementChanged();}
                Stage="pinned";Status=InputReady?"屏幕已固定到平面":"屏幕背面，移动到屏幕正面查看";
            }
            else if(creating){Stage="creating";Status=anchor?"空间锚点已注册，等待 Tracking":$"正在创建空间锚点 · 尝试 {creation.Attempts}/{PlaneAnchorCreation.MaximumAttempts}";}
            else if(pinned){Stage="waiting-anchor";Status="等待空间锚点恢复，手机触控已暂停";}
            if(HasPlacement)Screen.ApplyPlanePose(currentPose);
        }
        if(wasReady!=TrackingReady){Screen.PlanePlacementChanged();Report(true);}
        Report(false);
    }
    void FindCandidate()
    {
        candidate=null;Ray ray=new Ray(Screen.Head.transform.position,Screen.Head.transform.forward);
        if(aimMode==1)
        {
            if(!Screen.Hands.TryGetIndexTip(side,out Vector3 tip)||Vector3.Distance(tip,Screen.Head.transform.position)<.05f){preview.gameObject.SetActive(false);rayLine.gameObject.SetActive(false);return;}
            ray=new Ray(tip,(tip-Screen.Head.transform.position).normalized);
        }
        float nearest=float.PositiveInfinity;Vector3 point=default;
        if(Planes.subsystem!=null&&Planes.subsystem.running)foreach(var plane in Planes.trackables)
        {
            if(plane.trackingState!=TrackingState.Tracking||plane.subsumedBy!=null)continue;
            if(PlanePinMath.Hit(ray,new Pose(plane.transform.position,plane.transform.rotation),plane.boundary,out Vector3 hit,out float distance)&&distance<nearest){candidate=plane;nearest=distance;point=hit;}
        }
        bool valid=candidate&&PlanePinMath.Placement(point,candidate.transform.up,new Pose(Screen.Head.transform.position,Screen.Head.transform.rotation),out candidatePose);
        if(!valid)candidate=null;
        preview.gameObject.SetActive(valid);rayLine.gameObject.SetActive(aimMode==1&&valid);
        if(!valid)return;
        float reference=Mathf.Clamp(Vector3.Distance(candidatePose.position,Screen.Head.transform.position),.25f,5);
        Vector2 size=Screen.PlanePreviewSize(reference);preview.startWidth=preview.endWidth=.003f*Mathf.Clamp(reference,.5f,3);
        for(int i=0;i<4;i++){Vector3 corner=new Vector3((i==0||i==3?-.5f:.5f)*size.x,(i<2?-.5f:.5f)*size.y,0);preview.SetPosition(i,candidatePose.position+candidatePose.rotation*corner);}
        if(aimMode==1){rayLine.SetPosition(0,ray.origin);rayLine.SetPosition(1,candidatePose.position);}
        foreach(var item in boundaries)if(item.Value)item.Value.sharedMaterial=item.Key.Equals(candidate.trackableId)?SelectedMaterial:BoundaryMaterial;
    }
    void Confirm()
    {
        if(!Selecting||!Screen.PlacementTrackingReady||!candidate||candidate.trackingState!=TrackingState.Tracking||candidate.subsumedBy!=null){Notice("未命中有效平面，请重新瞄准");return;}
        currentPose=candidatePose;SizeDistance=Mathf.Clamp(Vector3.Distance(currentPose.position,Screen.Head.transform.position),.25f,5);
        Selecting=false;creating=true;pinned=TrackingReady=false;creation.Begin(Time.unscaledTime);confirmAt=-1;lastAnchorError="";Stage="creating";Status="正在创建空间锚点";
        Debug.Log("[PlaneScreenPin] Anchor creation started on plane "+candidate.trackableId+" at "+currentPose.position);
        candidate=null;foreach(var line in boundaries.Values)if(line)line.gameObject.SetActive(false);
        preview.gameObject.SetActive(false);rayLine.gameObject.SetActive(false);Screen.PlanePlacementChanged();Report(true);
    }
    bool ValidAnchorId()=>anchor&&!anchor.trackableId.Equals(TrackableId.invalidId);
    bool PrepareAnchorService(float now)
    {
        if(!Anchors.enabled||Anchors.subsystem==null||!Anchors.subsystem.running){mapConfigured=false;serviceReadyAt=-1;return false;}
        if(!mapConfigured)
        {
            if(now<nextMapAttempt)return false;nextMapAttempt=now+1;
            try
            {
                if(!Origin||!Origin.TrackablesParent)return false;
                mapPath=Path.Combine(Application.persistentDataPath,"StablePlaneMaps");Directory.CreateDirectory(mapPath);
                Anchors.SetAndCreateAnchorMappingDirectory(mapPath);mapConfigured=true;serviceReadyAt=now+.5f;
                Debug.Log("[PlaneScreenPin] Anchor map directory configured: "+mapPath);
            }
            catch(Exception error){lastAnchorError="锚点初始化："+error.Message;return false;}
        }
        return now>=serviceReadyAt;
    }
    void TryCreateAnchor()
    {
        try
        {
            anchor=PlaneNativeAnchorFactory.TryCreate(Anchors,currentPose);
            if(!anchor){lastAnchorError="原生 TryAddAnchor 返回 false";Debug.LogWarning("[PlaneScreenPin] Registration deferred, attempt="+creation.Attempts);return;}
            if(!ValidAnchorId()){lastAnchorError="原生注册返回了无效 ID";Destroy(anchor.gameObject);anchor=null;return;}
            lastAnchorError="";anchor.gameObject.name="Plane pinned phone anchor";
            Debug.Log("[PlaneScreenPin] Registered id="+anchor.trackableId+" state="+anchor.trackingState+" pending="+anchor.pending+" attempts="+creation.Attempts);
        }
        catch(Exception error){lastAnchorError=error.GetType().Name+": "+error.Message;Debug.LogWarning("[PlaneScreenPin] Registration attempt "+creation.Attempts+": "+lastAnchorError);}
    }
    void AnchorChanges(ARAnchorsChangedEventArgs args)
    {diagnostics.addedEvents+=args.added.Count;diagnostics.updatedEvents+=args.updated.Count;diagnostics.removedEvents+=args.removed.Count;lastAnchorChange=Time.unscaledTime;}
    void AnchorError(XREALErrorCode code,string message)
    {if(message!=null&&message.IndexOf("anchor",StringComparison.OrdinalIgnoreCase)>=0)lastAnchorError=code+": "+message;}
    void Notice(string value){Status=value;noticeUntil=Time.unscaledTime+3;Report(true);}
    void DrawBoundaries()
    {
        keep.Clear();planeCount=0;
        foreach(var plane in Planes.trackables)
        {
            if(plane.trackingState!=TrackingState.Tracking||plane.subsumedBy!=null||!plane.boundary.IsCreated||plane.boundary.Length<3)continue;
            planeCount++;if(keep.Count>=32)continue;keep.Add(plane.trackableId);
            if(!boundaries.TryGetValue(plane.trackableId,out var line)||!line){line=Line("Detected plane outline",BoundaryMaterial,false);line.loop=true;line.transform.SetParent(plane.transform,false);boundaries[plane.trackableId]=line;}
            line.gameObject.SetActive(true);line.sharedMaterial=candidate&&candidate.trackableId.Equals(plane.trackableId)?SelectedMaterial:BoundaryMaterial;
            var polygon=plane.boundary;line.positionCount=polygon.Length;for(int i=0;i<polygon.Length;i++)line.SetPosition(i,new Vector3(polygon[i].x,.003f,polygon[i].y));
        }
        stale.Clear();foreach(var pair in boundaries)if(!keep.Contains(pair.Key))stale.Add(pair.Key);
        foreach(var id in stale){if(boundaries[id])Destroy(boundaries[id].gameObject);boundaries.Remove(id);}
    }
    void StopPlanes()
    {
        if(Planes){if(Planes.enabled)Planes.requestedDetectionMode=PlaneDetectionMode.None;Planes.enabled=false;}
        foreach(var line in boundaries.Values)if(line)Destroy(line.gameObject);boundaries.Clear();planeCount=0;
    }
    void Report(bool force)
    {
        if(!force&&Time.unscaledTime<reportAt)return;reportAt=Time.unscaledTime+.2f;
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge?.CallStatic("updatePlanePin",Active,Selecting,HasPlacement,InputReady,candidate!=null,planeCount,Stage,Status,anchor?anchor.trackableId.ToString():"");
        diagnostics.managerEnabled=Anchors.enabled;diagnostics.serviceRunning=Anchors.subsystem!=null&&Anchors.subsystem.running;diagnostics.mapConfigured=mapConfigured;
        diagnostics.hasAnchor=anchor;diagnostics.validId=ValidAnchorId();diagnostics.pending=anchor&&anchor.pending;diagnostics.attempts=creation.Attempts;
        diagnostics.creationElapsedSeconds=creating?Time.unscaledTime-creation.StartedAt:0;diagnostics.lastChangesAgeSeconds=lastAnchorChange<0?-1:Time.unscaledTime-lastAnchorChange;
        diagnostics.provider=Anchors.descriptor==null?"":Anchors.descriptor.id;diagnostics.mapPath=mapPath;diagnostics.lastError=lastAnchorError;diagnostics.tracking=anchor?anchor.trackingState.ToString():"None";
        bridge?.CallStatic("updatePlaneAnchorDiagnostics",JsonUtility.ToJson(diagnostics));
#endif
    }
    void OnDestroy()
    {
        if(Anchors)Anchors.anchorsChanged-=AnchorChanges;XREALCallbackHandler.OnXREALError-=AnchorError;
        ReleaseAnchor();StopPlanes();if(preview)Destroy(preview.gameObject);if(rayLine)Destroy(rayLine.gameObject);
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge?.Dispose();
#endif
    }
}
