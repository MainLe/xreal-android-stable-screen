using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;
using Unity.XR.XREAL;
using SerializableGuid = UnityEngine.XR.ARSubsystems.SerializableGuid;

/// <summary>Real SDK integration harness. READY, running, tracking and persistence are distinct states.</summary>
public sealed class SpatialFeatureDemo : MonoBehaviour
{
    public bool EnableHandCompatibility;
    public bool EnableHandDspCompatibility;
    public XROrigin Origin;
    public Camera Head;
    public FeatureHeadPose Pose;
    public FeatureHandRig Hands;
    public ARAnchorManager Anchors;
    public ARPlaneManager Planes;
    public ARMeshManager Meshes;
    public ARTrackedImageManager Images;
    public ARCameraManager CameraFeed;
    public Material MarkerMaterial, AnchorMaterial, PlaneMaterial, ImageMaterial;
    public TextMesh Hud;
    public Transform Marker;
    public StaticPhoneComparison ScreenshotComparison;
    public InertialCubeInteraction CubeInteraction;
    readonly Queue<string> events=new Queue<string>();
    readonly Dictionary<TrackableId,ARAnchor> live=new Dictionary<TrackableId,ARAnchor>();
    readonly Dictionary<TrackableId,LineRenderer> planeLines=new Dictionary<TrackableId,LineRenderer>();
    readonly Dictionary<TrackableId,GameObject> imageViews=new Dictionary<TrackableId,GameObject>();
    readonly List<SerializableGuid> saved=new List<SerializableGuid>();
    readonly HashSet<Guid> loadRequested=new HashSet<Guid>();
    readonly List<string> capabilities=new List<string>();
    readonly List<Transform> markers=new List<Transform>();
    string page="tracking",last="Starting XR session",mapPath,handNativeError="";
    string lastCapturedError="";
    double lastCapturedErrorAt;
    bool stopFailedHands;
    bool switching,busy,mapConfigured,dead,pinchAnchors,rgbSupported,phoneReady,performanceBaseline;
    int savedIndex=-1,selectedIndex,frames,cameraFrames,grabbing=-1;
    Vector3 grabOffset;
    float fps,lastFps,nextStatus,nextEnvironment,nextCamera,lastPinchAnchor;
    ARAnchor selected;
#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaClass phone;
#endif
    async void Start()
    {
        Application.targetFrameRate=60;Screen.sleepTimeout=SleepTimeout.NeverSleep;
        if(EnableHandDspCompatibility)HandDspCompatibility.Prepare("scene-start");
        // The compatibility watcher polls dl_iterate_phdr every 5 ms while its module is absent.
        // Do not start that worker in a tracking-only session. Start it only when hands are requested.
        XrRuntimeHealth.Attach(gameObject);
        Application.logMessageReceived+=CaptureLog;
        XREALCallbackHandler.OnXREALError+=NativeError;
        Anchors.anchorsChanged+=AnchorsChanged;Images.trackedImagesChanged+=ImagesChanged;
        Hands.PinchStarted+=PinchStart;Hands.PinchMoved+=PinchMove;Hands.PinchEnded+=PinchEnd;
        mapPath=Path.Combine(Application.persistentDataPath,"SpatialMaps");
        Directory.CreateDirectory(mapPath);
        for(int i=0;i<5;i++)
        {
            var marker=GameObject.CreatePrimitive(PrimitiveType.Cube);Destroy(marker.GetComponent<Collider>());
            marker.name="World-locked reference "+i;marker.transform.position=new Vector3((i-2)*0.4f,-0.2f,2);
            marker.transform.localScale=Vector3.one*0.07f;marker.GetComponent<Renderer>().sharedMaterial=MarkerMaterial;markers.Add(marker.transform);
        }
        lastFps=Time.unscaledTime;Note("Demo started; 6DoF requested, environment modules off until selected.");
#if UNITY_ANDROID && !UNITY_EDITOR
        try{phone=new AndroidJavaClass("dev.xreal.demo.FeatureDebugPanel");page=ScreenshotComparison?"tracking":phone.CallStatic<string>("initialPage")??"tracking";phone.CallStatic("configureScreenshotComparison",ScreenshotComparison!=null);phone.CallStatic("show");phoneReady=true;}
        catch(Exception e){Note("Phone panel: "+e.Message);}
#endif
        await Task.Delay(1200);if(dead)return;QueryCapabilities();
        if(page=="hands")SetHands(true);
    }
    void CaptureLog(string message,string stack,LogType type)
    {
        if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)
        {
            double now=Time.realtimeSinceStartupAsDouble;if(message==lastCapturedError&&now-lastCapturedErrorAt<1)return;
            lastCapturedError=message;lastCapturedErrorAt=now;
            events.Enqueue(DateTime.Now.ToString("HH:mm:ss")+" "+type+" "+message);
            while(events.Count>150)events.Dequeue();
        }
    }
    void Note(string text)
    {
        if(dead)return;last=text;events.Enqueue(DateTime.Now.ToString("HH:mm:ss")+" "+text);
        while(events.Count>150)events.Dequeue();Debug.Log("[SpatialDemo] "+text);
    }
    void NativeError(XREALErrorCode code,string message)
    {
        if(message!=null&&message.IndexOf("Hand",StringComparison.OrdinalIgnoreCase)>=0)
        {
            string error=code+" / "+message;if(error==handNativeError)return;handNativeError=error;stopFailedHands=true;
            Hands.ShowHands=false;grabbing=-1;
            Note("Hand algorithm failed: "+handNativeError+". Input switch acceptance does not confirm algorithm initialization.");
        }
    }
    bool SixDofReady => !switching&&XREALPlugin.GetTrackingType()==TrackingType.MODE_6DOF&&Pose.PositionTracked&&ARSession.state==ARSessionState.SessionTracking;
    void QueryCapabilities()
    {
        capabilities.Clear();
        foreach(XREALSupportedFeature feature in Enum.GetValues(typeof(XREALSupportedFeature)))
        {
            try{bool supported=XREALPlugin.IsHMDFeatureSupported(feature);capabilities.Add(feature+"="+supported);if(feature==XREALSupportedFeature.XREAL_FEATURE_RGB_CAMERA)rgbSupported=supported;}
            catch(Exception e){capabilities.Add(feature+"=error "+e.Message);}
        }
        try{capabilities.Add("HandTracking="+XREALPlugin.IsHandTrackingSupported());}catch(Exception e){capabilities.Add("HandTracking="+e.Message);}
        Note("Native capabilities queried; unsupported hardware features remain disabled.");
    }
    void Update()
    {
        frames++;float now=Time.unscaledTime;
        if(stopFailedHands&&!switching&&!busy){stopFailedHands=false;bool stopped=XREALPlugin.SetInputSource(InputSource.None);Hands.ShowHands=false;Note("Stopped native hand input after reported algorithm error: "+stopped+". Enable hands manually to retry.");}
        if(now-lastFps>=1){fps=frames/(now-lastFps);frames=0;lastFps=now;}
        if(CubeInteraction){CubeInteraction.InteractionEnabled=!pinchAnchors&&!performanceBaseline&&!switching;CubeInteraction.IdleSpin=ScreenshotComparison&&!ScreenshotComparison.FreezeCube;}
        if(!CubeInteraction&&Marker&&grabbing<0&&!performanceBaseline&&(!ScreenshotComparison||!ScreenshotComparison.FreezeCube))Marker.Rotate(0,25*Time.unscaledDeltaTime,0,Space.World);
        if(Anchors.subsystem!=null&&Anchors.subsystem.running)
        {
            if(!mapConfigured)
            {
                try{Anchors.SetAndCreateAnchorMappingDirectory(mapPath);mapConfigured=true;RefreshSaved();}
                catch(Exception e){Note("Map directory failed: "+e.Message);}
            }
        }
        else mapConfigured=false;
        if(now>=nextEnvironment){nextEnvironment=now+0.3f;UpdatePlanes();UpdateImages();}
        if(CameraFeed.enabled&&now>=nextCamera)
        {
            nextCamera=now+0.5f;
            try{if(CameraFeed.TryAcquireLatestCpuImage(out var image)){cameraFrames++;image.Dispose();}}
            catch(Exception e){CameraFeed.enabled=false;Note("RGB frame acquisition disabled after error: "+e.Message);}
        }
        if(now>=nextStatus)
        {
            nextStatus=now+(performanceBaseline?5:1);string status=Status();
            if(Hud&&Hud.gameObject.activeSelf)Hud.text=$"XREAL SPATIAL DEBUG | {fps:F0} fps | #{Time.frameCount}\n{XREALPlugin.GetTrackingType()} | pos {Pose.PositionTracked} / rot {Pose.RotationTracked}\n{ARSession.state} | anchors {live.Count} | hands {Hands.Running}\n{last}";
#if UNITY_ANDROID && !UNITY_EDITOR
            try{phone?.CallStatic("updateStatus",status);}catch(Exception e){Debug.LogWarning(e.Message);}
#endif
        }
    }
    string Status()
    {
        string anchor=selected?selected.trackableId+" / "+selected.trackingState:"none";
        string savedId=savedIndex>=0&&savedIndex<saved.Count?saved[savedIndex].guid.ToString():"none";
        return $"{XREALPlugin.GetTrackingType()} | {ARSession.state} | {fps:F1} FPS / #{Time.frameCount}\nPosition tracking: {Pose.PositionTracked} | Rotation: {Pose.RotationTracked}\nXR flags: {Pose.TrackingState} | reason: {ARSession.notTrackingReason}\nPosition: {Head.transform.localPosition:F3}\nAnchors: {live.Count} | selected {anchor}\nSaved: {saved.Count} | {savedId} | busy {busy}\nL: {Hands.LeftStatus}\nR: {Hands.RightStatus}\nHand input: {XREALPlugin.GetInputSource()} | native error: {handNativeError}\nCompat: {HandGraphCompatibility.Status}\nPlanes {Planes.trackables.count} | Meshes {Meshes.meshes.Count} | Images {Images.trackables.count} | RGB frames {cameraFrames}\n{last}"+(CubeInteraction?$"\nCube: {CubeInteraction.State} | v={CubeInteraction.Velocity.magnitude:F2} m/s spin={CubeInteraction.Spin.magnitude:F2} rad/s":"")+(EnableHandDspCompatibility?"\nDSP env: "+HandDspCompatibility.Status:"");
    }
    /// <summary>Called from native phone UI. All SDK changes run on Unity's main thread.</summary>
    [UnityEngine.Scripting.Preserve]
    public void Command(string command)
    {
        if(dead)return;
        try
        {
            if(command.StartsWith("tab:")){page=command.Substring(4);return;}
            switch(command)
            {
                case "6dof": SwitchTracking(TrackingType.MODE_6DOF);break;
                case "3dof": SwitchTracking(TrackingType.MODE_3DOF);break;
                case "reset-marker": ResetMarkers();break;
                case "cube-stop": if(CubeInteraction){CubeInteraction.StopMotion();Note("Cube inertia stopped.");}break;
                case "compare-center": if(ScreenshotComparison){ScreenshotComparison.CenterAfterDelay();Note("Screenshot and cube will be placed together in one second.");}break;
                case "compare-motion": if(ScreenshotComparison){ScreenshotComparison.FreezeCube=!ScreenshotComparison.FreezeCube;Note("Comparison cube rotation="+!ScreenshotComparison.FreezeCube);}break;
                case "compare-image": if(ScreenshotComparison){ScreenshotComparison.ToggleImage();Note("Static phone screenshot visible="+ScreenshotComparison.ImageVisible);}break;
                case "anchor-create": CreateAnchor(Head.transform.position+Head.transform.forward);break;
                case "anchor-next": SelectNextAnchor();break;
                case "anchor-quality": AnchorQuality();break;
                case "anchor-save": SaveAnchor();break;
                case "saved-next": RefreshSaved(true);break;
                case "anchor-load": LoadAnchor();break;
                case "anchor-erase": EraseAnchor();break;
                case "anchor-remap": if(selected&&SixDofReady&&!busy)Note("Remap accepted="+Anchors.TryRemap(selected.trackableId));else Note("Select a tracked anchor in 6DoF first.");break;
                case "anchor-clear": ClearAnchors();break;
                case "hands-on": SetHands(true);break;
                case "hands-off": SetHands(false);break;
                case "hand-poll-reuse":
#if XREAL_HAND_RUNTIME_AUDIT && UNITY_ANDROID
                    XREALHandSubSystem.PollingDiagnostics.ReuseBeforeRender=!XREALHandSubSystem.PollingDiagnostics.ReuseBeforeRender;
                    Note("Experimental hand read reuse at before-render="+XREALHandSubSystem.PollingDiagnostics.ReuseBeforeRender+"; native inference rate unchanged.");
#else
                    Note("Hand poll A/B is available only in the runtime-audit build.");
#endif
                    break;
                case "pinch-anchor": pinchAnchors=!pinchAnchors;Note("Pinch creates anchor="+pinchAnchors);break;
                case "planes": ToggleEnvironment("planes");break;
                case "mesh": ToggleEnvironment("mesh");break;
                case "images": ToggleEnvironment("images");break;
                case "camera": ToggleEnvironment("camera");break;
                case "environment-off": DisableEnvironment();break;
                case "runtime-baseline": DisableEnvironment();if(XREALPlugin.GetInputSource()!=InputSource.None)SetHands(false);performanceBaseline=true;if(Hud)Hud.gameObject.SetActive(false);Note("Static tracking baseline: environment/hand input off, HUD and marker animation off; anchors/maps retained.");break;
                case "runtime-hud": performanceBaseline=false;if(Hud)Hud.gameObject.SetActive(true);Note("HUD and marker animation restored. Other modules remain as selected.");break;
                case "capabilities": QueryCapabilities();break;
                case "report": WriteReport();break;
                case "exit": if(busy)Note("Wait for the current native map operation before exiting.");else Application.Quit();break;
                default: Note("Unknown command: "+command);break;
            }
        }
        catch(Exception e){Note(command+" failed: "+e);}
    }
    async void SwitchTracking(TrackingType mode)
    {
        if(switching||busy){Note("Another native operation is in progress.");return;}
        if(mode==TrackingType.MODE_3DOF&&(live.Count>0||FindObjectsOfType<ARAnchor>().Length>0)){Note("Remove live anchors before switching to 3DoF; saved maps are retained.");return;}
        switching=true;DisableEnvironment();
        try{bool ok=await XREALPlugin.SwitchTrackingTypeAsync(mode);Note("Tracking switch "+mode+" accepted="+ok+"; position validity is reported separately.");}
        catch(Exception e){Note("Tracking switch failed: "+e.Message);}
        finally{switching=false;}
    }
    void ResetMarkers()
    {
        if(CubeInteraction)CubeInteraction.StopMotion();grabbing=-1;Marker.position=Head.transform.position+Head.transform.forward*0.65f;Marker.rotation=Head.transform.rotation;
        Vector3 right=Vector3.Cross(Vector3.up,Head.transform.forward).normalized;
        for(int i=0;i<markers.Count;i++)markers[i].position=Head.transform.position+Head.transform.forward*2+right*((i-2)*0.4f)+Vector3.down*0.2f;
        if(ScreenshotComparison)ScreenshotComparison.Place();
        Note("Test objects placed in front of the head; coordinate system and maps were retained.");
    }
    void AnchorsChanged(ARAnchorsChangedEventArgs args)
    {
        foreach(var a in args.added){live[a.trackableId]=a;if(!selected)selected=a;AnchorVisual(a);Note("Anchor added: "+a.trackableId+" / "+a.trackingState);}
        foreach(var a in args.removed){live.Remove(a.trackableId);if(selected==a)selected=null;}
        foreach(var a in args.updated)AnchorVisual(a);
    }
    void AnchorVisual(ARAnchor a)
    {
        var child=a.transform.Find("Anchor visual");
        if(!child)
        {
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);Destroy(cube.GetComponent<Collider>());cube.name="Anchor visual";child=cube.transform;
            child.SetParent(a.transform,false);child.localScale=Vector3.one*0.1f;cube.GetComponent<Renderer>().sharedMaterial=AnchorMaterial;
            var label=new GameObject("Anchor ID").AddComponent<TextMesh>();label.transform.SetParent(a.transform,false);label.transform.localPosition=Vector3.up*0.08f;
            label.characterSize=0.007f;label.fontSize=32;label.anchor=TextAnchor.LowerCenter;label.text=a.trackableId.ToString().Substring(0,8);
        }
        // Limited/None never appears as a successfully localized anchor.
        child.gameObject.SetActive(a.trackingState==TrackingState.Tracking);
    }
    void CreateAnchor(Vector3 point)
    {
        if(!SixDofReady||!mapConfigured||busy){Note("Anchor creation requires a tracking 6DoF session and ready anchor subsystem.");return;}
        if(FindObjectsOfType<ARAnchor>().Length>=12){Note("Demo limit: 12 live anchors. Clear some before creating more.");return;}
        var go=new GameObject("Spatial anchor");go.transform.SetParent(Origin.TrackablesParent,false);
        go.transform.SetPositionAndRotation(point,Quaternion.identity);selected=go.AddComponent<ARAnchor>();
        Note("Anchor requested; waiting for subsystem tracking confirmation.");
    }
    void SelectNextAnchor()
    {
        var all=live.Values.Where(a=>a).OrderBy(a=>a.trackableId.ToString()).ToArray();
        if(all.Length==0){selected=null;Note("No live anchors.");return;}
        selectedIndex=(selectedIndex+1)%all.Length;selected=all[selectedIndex];Note("Selected "+selected.trackableId+" / "+selected.trackingState);
    }
    XREALAnchorEstimateQuality Quality()
    {
        var relative=new UnityEngine.Pose(Origin.transform.InverseTransformPoint(Head.transform.position),Quaternion.Inverse(Origin.transform.rotation)*Head.transform.rotation);
        return Anchors.GetAnchorQuality(selected.trackableId,relative);
    }
    void AnchorQuality()
    {
        if(!selected||selected.trackingState!=TrackingState.Tracking||!SixDofReady||busy){Note("Select a tracking anchor first.");return;}
        Note("Anchor quality="+Quality()+". Observe from several angles to improve mapping.");
    }
    async Task<T> ObserveNative<T>(Task<T> task)
    {
        if(await Task.WhenAny(task,Task.Delay(30000))!=task)Note("Native map operation exceeds 30 seconds; still waiting. New map operations remain blocked.");
        return await task;
    }
    async void SaveAnchor()
    {
        if(busy||!SixDofReady||!selected||selected.trackingState!=TrackingState.Tracking){Note("Save requires a tracked anchor and 6DoF.");return;}
        if(Quality()==XREALAnchorEstimateQuality.XREAL_ANCHOR_ESTIMATE_QUALITY_INSUFFICIENT){Note("Mapping quality insufficient. Scan surrounding textured surfaces before saving.");return;}
        busy=true;
        try{var result=await ObserveNative(Anchors.TrySaveAnchorAsync(selected));if(dead)return;Note("Map save success="+result.success+" / "+result.value.guid);RefreshSaved();}
        catch(Exception e){Note("Map save failed: "+e.Message);}finally{busy=false;}
    }
    void RefreshSaved(bool next=false)
    {
        if(!mapConfigured||busy&&next){Note("Anchor subsystem not ready or busy.");return;}
        Guid previous=savedIndex>=0&&savedIndex<saved.Count?saved[savedIndex].guid:Guid.Empty;
        var found=Anchors.TryGetSavedAnchorIds();saved.Clear();if(found!=null)saved.AddRange(found.OrderBy(s=>s.guid.ToString()));
        savedIndex=saved.FindIndex(s=>s.guid==previous);if(saved.Count>0){if(savedIndex<0)savedIndex=0;else if(next)savedIndex=(savedIndex+1)%saved.Count;}else savedIndex=-1;
        if(next)Note("Saved map: "+(savedIndex>=0?saved[savedIndex].guid.ToString():"none"));
    }
    void LoadAnchor()
    {
        if(busy||!SixDofReady||!mapConfigured||savedIndex<0){Note("Select a saved map in a tracking 6DoF session first.");return;}
        var guid=saved[savedIndex];if(loadRequested.Contains(guid.guid)){Note("Map already requested in this session. Clear live anchors to retry.");return;}
        bool accepted=Anchors.TryLoadAnchor(guid,out var anchor);if(accepted)loadRequested.Add(guid.guid);
        Note("Map load accepted="+accepted+" / "+anchor.trackableId+". Revisit the mapped place; only Tracking state confirms relocalization.");
    }
    async void EraseAnchor()
    {
        if(busy||!mapConfigured||savedIndex<0){Note("Select a saved map first.");return;}
        var guid=saved[savedIndex];busy=true;
        try{bool ok=await ObserveNative(Anchors.TryEraseAnchorAsync(guid));if(dead)return;Note("Map erase success="+ok+" / "+guid.guid);loadRequested.Remove(guid.guid);RefreshSaved();}
        catch(Exception e){Note("Map erase failed: "+e.Message);}finally{busy=false;}
    }
    void ClearAnchors()
    {
        if(busy){Note("Wait for map operation to finish.");return;}
        foreach(var a in FindObjectsOfType<ARAnchor>())Destroy(a.gameObject);
        live.Clear();selected=null;loadRequested.Clear();Note("Live anchors removed; saved map files retained.");
    }
    void SetHands(bool enabled)
    {
        handNativeError="";stopFailedHands=false;
        if(enabled&&EnableHandDspCompatibility)HandDspCompatibility.Prepare("before-hands-request");
        if(enabled&&!XREALPlugin.IsHandTrackingSupported()){Note("Native runtime reports hand tracking unsupported.");return;}
        if(enabled&&EnableHandCompatibility)HandGraphCompatibility.Start();
        bool ok=XREALPlugin.SetInputSource(enabled?InputSource.Hands:InputSource.None);
        if(ok){Hands.ShowHands=enabled;if(!enabled){if(CubeInteraction)CubeInteraction.StopMotion();grabbing=-1;pinchAnchors=false;}}
        Note("Hand input requested="+enabled+" accepted="+ok+"; see per-hand joint validity.");
    }
    void PinchStart(int side,Vector3 point)
    {
        if(pinchAnchors){if(Time.unscaledTime-lastPinchAnchor>1){lastPinchAnchor=Time.unscaledTime;CreateAnchor(point);}return;}
        if(CubeInteraction)return;
        if(grabbing<0&&Vector3.Distance(point,Marker.position)<0.18f){grabbing=side;grabOffset=Marker.position-point;Note((side==0?"Left":"Right")+" pinch grabbed cube.");}
    }
    void PinchMove(int side,Vector3 point){if(grabbing==side)Marker.position=point+grabOffset;}
    void PinchEnd(int side){if(grabbing==side){grabbing=-1;Note("Cube released (or hand tracking lost).");}}
    void DisableEnvironment(){Planes.enabled=false;Meshes.enabled=false;Images.enabled=false;CameraFeed.enabled=false;foreach(var line in planeLines.Values)if(line)line.gameObject.SetActive(false);foreach(var mesh in Meshes.meshes)if(mesh)mesh.gameObject.SetActive(false);foreach(var view in imageViews.Values)if(view)view.SetActive(false);Note("Environment modules disabled.");}
    void ToggleEnvironment(string kind)
    {
        if(!SixDofReady){Note("Environment modules require a tracking 6DoF session.");return;}
        if(kind=="planes")Planes.enabled=!Planes.enabled;
        if(kind=="mesh"){Meshes.enabled=!Meshes.enabled;foreach(var mesh in Meshes.meshes)if(mesh)mesh.gameObject.SetActive(Meshes.enabled);}
        if(kind=="images")Images.enabled=!Images.enabled;
        if(kind=="camera"){if(!rgbSupported){Note("RGB camera unsupported by this hardware/runtime; grayscale tracking cameras are separate.");return;}CameraFeed.enabled=!CameraFeed.enabled;}
        Note($"Environment enabled: planes={Planes.enabled}, mesh={Meshes.enabled}, images={Images.enabled}, RGB={CameraFeed.enabled}");
    }
    void UpdatePlanes()
    {
        if(!Planes.enabled)return;
        var keep=new HashSet<TrackableId>();
        foreach(var plane in Planes.trackables)
        {
            keep.Add(plane.trackableId);
            if(!planeLines.TryGetValue(plane.trackableId,out var line)||!line)
            {
                line=new GameObject("Plane boundary").AddComponent<LineRenderer>();line.transform.SetParent(plane.transform,false);
                line.sharedMaterial=PlaneMaterial;line.useWorldSpace=false;line.loop=true;line.startWidth=line.endWidth=0.008f;planeLines[plane.trackableId]=line;
            }
            bool valid=Planes.enabled&&plane.trackingState==TrackingState.Tracking&&plane.subsumedBy==null;
            line.gameObject.SetActive(valid);if(!valid)continue;var boundary=plane.boundary;
            line.positionCount=boundary.Length;for(int i=0;i<boundary.Length;i++)line.SetPosition(i,new Vector3(boundary[i].x,0.003f,boundary[i].y));
        }
        foreach(var id in planeLines.Keys.Where(id=>!keep.Contains(id)).ToArray()){if(planeLines[id])Destroy(planeLines[id].gameObject);planeLines.Remove(id);}
    }
    void ImagesChanged(ARTrackedImagesChangedEventArgs args){foreach(var image in args.added)Note("Image detected: "+image.referenceImage.name+" / "+image.trackingState);}
    void UpdateImages()
    {
        if(!Images.enabled)return;
        var keep=new HashSet<TrackableId>();
        foreach(var image in Images.trackables)
        {
            keep.Add(image.trackableId);
            if(!imageViews.TryGetValue(image.trackableId,out var view)||!view)
            {
                view=GameObject.CreatePrimitive(PrimitiveType.Cube);Destroy(view.GetComponent<Collider>());view.name="Tracked image indicator";
                view.transform.SetParent(image.transform,false);view.transform.localScale=new Vector3(0.1f,0.01f,0.1f);view.GetComponent<Renderer>().sharedMaterial=ImageMaterial;imageViews[image.trackableId]=view;
            }
            view.SetActive(Images.enabled&&image.trackingState==TrackingState.Tracking);
        }
        foreach(var id in imageViews.Keys.Where(id=>!keep.Contains(id)).ToArray()){if(imageViews[id])Destroy(imageViews[id]);imageViews.Remove(id);}
    }
    [Serializable] sealed class AnchorReport{public string id,state;public Vector3 position;}
    [Serializable] sealed class Report
    {
        public string version,utc,device,os,session,notTrackingReason,trackingFlags,mode,lastMessage,mapDirectory,leftHand,rightHand,handNativeError,handInputSource,handCompatibility;
        public HandDspCompatibility.Snapshot handDspEnvironment;
        public XrRuntimeHealth.Snapshot runtimeHealth;
        public string androidRuntimeHealth;
        public float fps;public int frames,cameraFrames,planes,meshes,images;
        public bool positionTracked,rotationTracked,handSubsystem,planesEnabled,meshesEnabled,imagesEnabled,cameraEnabled,nativeOperationPending;
        public Vector3 headPosition;public string[] capabilities,savedMaps,events;public AnchorReport[] anchors;
        public string cubeState;public Vector3 cubeVelocity,cubeSpin;
    }
    void WriteReport()
    {
        var report=new Report{version=Application.version,runtimeHealth=XrRuntimeHealth.Latest,androidRuntimeHealth=XrRuntimeHealth.AndroidJson,handDspEnvironment=HandDspCompatibility.Report(),utc=DateTime.UtcNow.ToString("O"),device=SystemInfo.deviceModel,os=SystemInfo.operatingSystem,session=ARSession.state.ToString(),notTrackingReason=ARSession.notTrackingReason.ToString(),trackingFlags=Pose.TrackingState.ToString(),mode=XREALPlugin.GetTrackingType().ToString(),lastMessage=last,mapDirectory=mapPath,
            leftHand=Hands.LeftStatus,rightHand=Hands.RightStatus,handNativeError=handNativeError,handInputSource=XREALPlugin.GetInputSource().ToString(),handCompatibility=HandGraphCompatibility.Status,fps=fps,frames=Time.frameCount,cameraFrames=cameraFrames,planes=Planes.trackables.count,meshes=Meshes.meshes.Count,images=Images.trackables.count,
            positionTracked=Pose.PositionTracked,rotationTracked=Pose.RotationTracked,headPosition=Head.transform.localPosition,handSubsystem=Hands.Running,planesEnabled=Planes.enabled,meshesEnabled=Meshes.enabled,imagesEnabled=Images.enabled,cameraEnabled=CameraFeed.enabled,nativeOperationPending=busy,
            capabilities=capabilities.ToArray(),savedMaps=saved.Select(s=>s.guid.ToString()).ToArray(),events=events.ToArray(),anchors=live.Values.Where(a=>a).Select(a=>new AnchorReport{id=a.trackableId.ToString(),state=a.trackingState.ToString(),position=a.transform.position}).ToArray()};
        if(CubeInteraction){report.cubeState=CubeInteraction.State;report.cubeVelocity=CubeInteraction.Velocity;report.cubeSpin=CubeInteraction.Spin;}
        string json=JsonUtility.ToJson(report,true),path=Path.Combine(Application.persistentDataPath,"xreal-feature-report.json");File.WriteAllText(path,json);
#if UNITY_ANDROID && !UNITY_EDITOR
        phone?.CallStatic("copyReport",json);
#else
        GUIUtility.systemCopyBuffer=json;
#endif
        Note("Report saved: "+path);
    }
    void OnGUI()
    {
        if(phoneReady)return;
        // Fallback controls for Editor and builds where the native phone overlay is unavailable.
        GUI.skin.label.fontSize=18;GUI.skin.button.fontSize=18;
        GUILayout.BeginArea(new Rect(15,15,620,Mathf.Min(Screen.height-30,850)));GUILayout.Label(Status());
        GUILayout.BeginHorizontal();if(GUILayout.Button("6DoF",GUILayout.Height(45)))Command("6dof");if(GUILayout.Button("3DoF",GUILayout.Height(45)))Command("3dof");if(GUILayout.Button("Report",GUILayout.Height(45)))Command("report");GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();if(GUILayout.Button("Anchor +",GUILayout.Height(45)))Command("anchor-create");if(GUILayout.Button("Save",GUILayout.Height(45)))Command("anchor-save");if(GUILayout.Button("Load",GUILayout.Height(45)))Command("anchor-load");GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();if(GUILayout.Button("Hands ON",GUILayout.Height(45)))Command("hands-on");if(GUILayout.Button("Hands OFF",GUILayout.Height(45)))Command("hands-off");GUILayout.EndHorizontal();GUILayout.EndArea();
    }
    void OnApplicationPause(bool paused)
    {
        // The provider resets its map directory when restarted. Reapply our directory after resume.
        if(paused){mapConfigured=false;grabbing=-1;}
        Note("Application paused="+paused);
    }
    void OnDestroy()
    {
        dead=true;Application.logMessageReceived-=CaptureLog;
        XREALCallbackHandler.OnXREALError-=NativeError;
        if(Anchors)Anchors.anchorsChanged-=AnchorsChanged;if(Images)Images.trackedImagesChanged-=ImagesChanged;
        if(Hands){Hands.PinchStarted-=PinchStart;Hands.PinchMoved-=PinchMove;Hands.PinchEnded-=PinchEnd;}
#if UNITY_ANDROID && !UNITY_EDITOR
        phone?.CallStatic("hide");phone?.Dispose();phone=null;
#endif
    }
}
