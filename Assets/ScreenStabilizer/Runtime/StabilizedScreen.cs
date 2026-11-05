using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using Unity.XR.XREAL;

/// <summary>Whole-phone capture is a texture on a world-fixed screen; pose updates independently of capture.</summary>
public sealed class StabilizedScreen : MonoBehaviour
{
    public Camera Head;
    public FeatureHandRig Hands;
    public PlaneScreenPin PlanePin;
    public Transform ScreenAnchor;
    public MeshRenderer Image,Bezel;
    public Material ReferenceMaterial;
    public TextMesh Message;
    public bool PhoneInputReady=>capturing&&!referenceMode&&tracked&&anchored&&!switching&&texture&&Image.enabled&&transferError.Length==0&&(!leftHandBound||leftPanel.Visible)&&(!PlanePin||!PlanePin.Active||PlanePin.InputReady);
    public bool FlipVertical=>flip;
    public int InputGeometryVersion { get; private set; }
    public float ScreenDistance=>PlanePin&&PlanePin.HasPlacement?PlanePin.SizeDistance:leftHandBound?leftPanel.SizeDistance:screenDistance;
    public bool PlacementTrackingReady=>tracked&&actualMode==6&&!switching;
    public Vector2 PlanePreviewSize(float distance)=>FitSize(aspect,scale,distance);
    public bool LeftHandBound=>leftHandBound;
    readonly LeftHandPanelAnchor leftPanel=new LeftHandPanelAnchor();
    bool leftHandBound;
    float handHeading=90;
    Texture2D texture;
    Material material;
    Quaternion rotation=Quaternion.identity;
    Vector3 position;
    Pose rawPose,smoothPose,anchorPose;
    readonly StablePoseFilter poseFilter=new StablePoseFilter();
    bool tracked,positionTracked,anchored,headLocked,flip=true,capturing,switching,exitRequested,referenceMode;
    int actualMode=3,requestedMode=3,smoothing,lastFilteredFrame=-1;
    Pose updatePose;
    float scale=1,screenDistance=2,aspect=9f/20f,recenterAt=-1,lastMetrics;
    float uploadMsTotal,lastUploadMs;
    string modeMessage="";
    int rendered,uploaded;
    string transferError="";
    readonly List<XRDisplaySubsystem> sdkDisplays=new List<XRDisplaySubsystem>();
    double nextTextureReport;
#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaClass bridge;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void BootstrapDiagnostics()
    {
        Application.logMessageReceived-=ReportUnityError;Application.logMessageReceived+=ReportUnityError;
        BootStage("managed-runtime-entered");
    }
    static void ReportUnityError(string condition,string stack,LogType type)
    {
        if(type!=LogType.Error&&type!=LogType.Exception&&type!=LogType.Assert)return;
        try{using(var target=new AndroidJavaClass("dev.xreal.stablescreen.ScreenBridge"))target.CallStatic("managedError",condition,stack);}catch(Exception){}
    }
    static void BootStage(string stage)
    {
        try{using(var target=new AndroidJavaClass("dev.xreal.stablescreen.ScreenBridge"))target.CallStatic("managedStage",stage);}catch(Exception e){Debug.LogWarning("[StableScreen] Boot reporting: "+e.Message);}
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    static void BeforeSplash(){BootStage("before-splash-screen");}
#endif
    void Awake()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        BootStage("scene-awake");
#endif
    }
    void Start()
    {
        try
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            bridge=new AndroidJavaClass("dev.xreal.stablescreen.ScreenBridge");bridge.CallStatic("managedStage","scene-start");
#endif
            if(!Head||!ScreenAnchor||!Image||!Bezel||!Message||!Image.sharedMaterial||!ReferenceMaterial)throw new InvalidOperationException("Missing screen scene references");
            Application.targetFrameRate=60;Application.runInBackground=true;Screen.sleepTimeout=SleepTimeout.NeverSleep;
            actualMode=XREALPlugin.GetTrackingType()==TrackingType.MODE_6DOF?6:3;
            material=new Material(Image.sharedMaterial);Image.sharedMaterial=material;Image.enabled=false;Bezel.enabled=false;
            Application.onBeforeRender+=BeforeRender;lastMetrics=Time.unscaledTime;XrRuntimeHealth.Attach(gameObject);
            ReadSettings();
#if UNITY_ANDROID && !UNITY_EDITOR
            bridge.CallStatic("initializeUnity");
#endif
        }
        catch(Exception e)
        {
            transferError=e.Message;enabled=false;Debug.LogError("[StableScreen] Android initialization: "+e);
#if UNITY_ANDROID && !UNITY_EDITOR
            bridge?.CallStatic("showControls");
#endif
        }
    }
    public static Vector2 FitSize(float sourceAspect,float zoom,float distance=2)
    {
        float maxWidth=2*distance*Mathf.Tan(20*Mathf.Deg2Rad),maxHeight=2*distance*Mathf.Tan(11*Mathf.Deg2Rad);
        float width=Mathf.Min(maxWidth,maxHeight*sourceAspect);
        return new Vector2(width,width/sourceAspect)*zoom;
    }
    public static Pose ShiftDistance(Pose anchor,float before,float after)=>new Pose(anchor.position+anchor.rotation*(Vector3.forward*(after-before)),anchor.rotation);
    void Layout()
    {
        Vector2 size=FitSize(aspect,scale,ScreenDistance);float bezelWidth=.025f*ScreenDistance/2;Image.transform.localScale=new Vector3(size.x,size.y,1);Bezel.transform.localScale=new Vector3(size.x+bezelWidth,size.y+bezelWidth,1);
        material.mainTextureScale=new Vector2(1,flip?-1:1);material.mainTextureOffset=new Vector2(0,flip?1:0);
    }
    void CenterNow()
    {
        if(PlanePin&&PlanePin.Active)return;
        if(leftHandBound){leftPanel.Reset();return;}
        if(!tracked)return;
        anchorPose=new Pose(rawPose.position+rawPose.rotation*(Vector3.forward*screenDistance),rawPose.rotation);anchored=true;Layout();
    }
    void ApplyHeadPose()
    {
        var sample=XrHeadPoseSample.Read();bool rotationValid=sample.RotationTracked;
        positionTracked=sample.PositionTracked;
        bool wasTracked=tracked;tracked=rotationValid&&(actualMode!=6||positionTracked)&&!switching;
        if(rotationValid)rotation=sample.Pose.rotation;
        if(actualMode==3)position=Vector3.zero;else if(positionTracked)position=sample.Pose.position;
        rawPose=new Pose(position,rotation);
        if(tracked&&!wasTracked){poseFilter.Reset();lastFilteredFrame=-1;}
        Head.transform.localPosition=position;Head.transform.localRotation=rotation;
        XrRuntimeHealth.RotationTracked=rotationValid;XrRuntimeHealth.PositionTracked=positionTracked;XrRuntimeHealth.TrackingMode=actualMode;
    }
    void BeforeRender()
    {
        ApplyHeadPose();
        if(!tracked)return;
        if(PlanePin&&PlanePin.Active){if(PlanePin.HasPlacement)ScreenAnchor.SetPositionAndRotation(anchorPose.position,anchorPose.rotation);return;}
        if(leftHandBound){if(leftPanel.Bound)ScreenAnchor.SetPositionAndRotation(anchorPose.position,anchorPose.rotation);return;}
        XrRuntimeHealth.PosePhaseDegrees=StablePoseFilter.Angle(updatePose.rotation,rawPose.rotation);XrRuntimeHealth.PosePhaseMm=Vector3.Distance(updatePose.position,rawPose.position)*1000;
        if(tracked&&(!anchored||headLocked))CenterNow();
        // Match the spatial demo by default: native camera pose, completely stationary world anchor.
        // OFF bypasses compensation algebra altogether, including same-frame sub-millisecond samples.
        Pose renderedAnchor=anchorPose;
        if(smoothing>0&&!headLocked)
        {
            if(lastFilteredFrame!=Time.frameCount){smoothPose=poseFilter.Step(rawPose,Time.realtimeSinceStartupAsDouble,smoothing,actualMode==6);lastFilteredFrame=Time.frameCount;}
            renderedAnchor=StablePoseFilter.Compensate(rawPose,smoothPose,anchorPose);
        }
        ScreenAnchor.SetPositionAndRotation(renderedAnchor.position,renderedAnchor.rotation);
    }
    void LateUpdate()
    {
        if(!leftHandBound)return;
        bool wasBound=leftPanel.Bound,wasTracked=leftPanel.Tracked,wasVisible=leftPanel.Visible;Pose palm=default;
        bool valid=tracked&&!switching&&Hands&&Hands.TryGetPalmSurfacePose(0,out palm);
        if(leftPanel.Step(valid,palm,rawPose,handHeading))
        {
            anchorPose=leftPanel.Pose;anchored=true;
            if(!wasBound)Layout();
            ScreenAnchor.SetPositionAndRotation(anchorPose.position,anchorPose.rotation);
        }
        RefreshPresentation();
        if(wasTracked!=leftPanel.Tracked||wasBound!=leftPanel.Bound||wasVisible!=leftPanel.Visible)
        {
            InputGeometryVersion++;
#if UNITY_ANDROID && !UNITY_EDITOR
            bridge?.CallStatic("updateHandScreen",leftHandBound,leftPanel.Tracked,leftPanel.Visible,ScreenDistance,leftPanel.FacingDot);
#endif
        }
    }
    void ReadSettings()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if(bridge==null)return;
        bool previous=headLocked,previousFlip=flip;float previousScale=scale,previousDistance=screenDistance;int previousSmoothing=smoothing,previousMode=requestedMode;
        bool previousHand=leftHandBound;float previousHeading=handHeading;
        scale=bridge.CallStatic<float>("screenScale");screenDistance=bridge.CallStatic<float>("screenDistance");headLocked=bridge.CallStatic<bool>("headLocked");flip=bridge.CallStatic<bool>("flipVertical");
        smoothing=bridge.CallStatic<int>("smoothing");requestedMode=bridge.CallStatic<int>("trackingMode");
        leftHandBound=bridge.CallStatic<bool>("leftHandScreenRequested");
        handHeading=bridge.CallStatic<float>("handScreenHeadingDegrees");
        if(previousHeading!=handHeading)InputGeometryVersion++;
        if(previousScale!=scale||previousDistance!=screenDistance||previousFlip!=flip||previous!=headLocked||previousMode!=requestedMode)InputGeometryVersion++;
        if(previousHand!=leftHandBound){leftPanel.Reset();InputGeometryVersion++;anchored=false;if(!leftHandBound)CenterNow();}
        if(previousDistance!=screenDistance&&anchored&&!leftHandBound&&(!PlanePin||!PlanePin.Active))anchorPose=ShiftDistance(anchorPose,previousDistance,screenDistance);
        if(previousSmoothing!=smoothing){poseFilter.Reset();lastFilteredFrame=-1;}
        uint hz=60;XREALPlugin.GetDeviceRefreshRate(ref hz);if(hz<30||hz>240)hz=60;
        bridge.CallStatic("updateDisplayRate",(float)hz);
        XrRuntimeHealth.TrackingPath=smoothing==0?"raw camera / fixed world anchor":"experimental content compensation; once per rendered frame";
        if(requestedMode!=actualMode&&!switching)SwitchMode();
        if(previous!=headLocked)CenterNow();
#endif
        Layout();
    }
    async void SwitchMode()
    {
        if(switching)return;switching=true;modeMessage="正在切换跟踪模式";
        try
        {
            while(this&&requestedMode!=actualMode)
            {
                int target=requestedMode;
                if(target==6&&!XREALPlugin.IsHMDFeatureSupported(XREALSupportedFeature.XREAL_FEATURE_PERCEPTION_HEAD_TRACKING_POSITION)){modeMessage="设备未报告 6DoF 定位能力";break;}
                bool accepted=await XREALPlugin.SwitchTrackingTypeAsync(target==6?TrackingType.MODE_6DOF:TrackingType.MODE_3DOF);
                if(!this)return;
                actualMode=XREALPlugin.GetTrackingType()==TrackingType.MODE_6DOF?6:3;
                if(!accepted||actualMode!=target){modeMessage="跟踪切换失败，请复制诊断；实际为 "+actualMode+"DoF";break;}
                modeMessage="";anchored=false;leftPanel.Reset();poseFilter.Reset();
            }
        }
        catch(Exception error){modeMessage="跟踪切换异常："+error.Message;Debug.LogWarning("[StableScreen] "+modeMessage);}
        finally{if(this){switching=false;if(exitRequested)StartCoroutine(Exit());}}
    }
    void Update()
    {
        ApplyHeadPose();updatePose=rawPose;rendered++;
        if(recenterAt>=0&&Time.unscaledTime>=recenterAt){if(tracked){InputGeometryVersion++;CenterNow();recenterAt=-1;}}
#if UNITY_ANDROID && !UNITY_EDITOR
        if(bridge!=null)
        {
            try{capturing=bridge.CallStatic<bool>("isCapturing");if(capturing&&!referenceMode)ReceiveFrame();else ClearTexture();}
            catch(Exception e){if(transferError!=e.Message)Debug.LogError("[StableScreen] Frame transfer: "+e.Message);transferError=e.Message;}
        }
#endif
        XrRuntimeHealth.CaptureActive=capturing;
        RefreshPresentation();
        if(Time.unscaledTime-lastMetrics>=1)
        {
            float elapsed=Time.unscaledTime-lastMetrics;
#if UNITY_ANDROID && !UNITY_EDITOR
            if(bridge!=null){
                float sdkHz=0;int sdkPresent=-1;sdkDisplays.Clear();SubsystemManager.GetInstances(sdkDisplays);
                foreach(var display in sdkDisplays)if(display.running){display.TryGetDisplayRefreshRate(out sdkHz);if(display.TryGetFramePresentCount(out int present))sdkPresent=present;break;}
                bridge.CallStatic("updateSdkOutput",sdkHz,sdkPresent);
                bridge.CallStatic("updateHandScreen",leftHandBound,leftPanel.Tracked,leftPanel.Visible,ScreenDistance,leftPanel.FacingDot);
                lastUploadMs=uploaded==0?0:uploadMsTotal/uploaded;XrRuntimeHealth.TextureUploadMs=lastUploadMs;bridge.CallStatic("updateTracking",actualMode,positionTracked,switching,modeMessage,poseFilter.RotationCorrection,poseFilter.PositionCorrection*1000,lastUploadMs);bridge.CallStatic("updateRuntime",transferError.Length>0?"画面传输异常："+transferError:modeMessage.Length>0?modeMessage:PlanePin&&PlanePin.Active?PlanePin.Status:(referenceMode?"静态标定网格 · ":"")+(leftHandBound?(leftPanel.Visible?"屏幕平行掌心 · 显示中":leftPanel.Tracked?"掌心未朝向眼睛 · 屏幕隐藏":"等待左手 · 屏幕隐藏"):headLocked?"随视线移动":actualMode+"DoF 空间固定")+(tracked?" · 姿态有效":" · 等待真实定位"),tracked,rendered/elapsed,uploaded/elapsed);
            }
#endif
            rendered=0;uploaded=0;uploadMsTotal=0;lastMetrics=Time.unscaledTime;
        }
    }
    void RefreshPresentation()
    {
        bool hiddenByPalm=leftHandBound&&!leftPanel.Visible;
        bool hiddenByPlane=PlanePin&&PlanePin.Active&&!PlanePin.InputReady;
        bool show=!hiddenByPalm&&!hiddenByPlane&&(referenceMode||(capturing&&texture))&&tracked&&anchored&&transferError.Length==0;
        Image.enabled=show;Bezel.enabled=show;
        Message.gameObject.SetActive(!hiddenByPalm&&(!show||recenterAt>=0));
        Message.transform.localPosition=new Vector3(0,PlanePin&&PlanePin.Active?-.28f:0,1.8f);
        Message.text=switching?"Switching tracking mode...":PlanePin&&PlanePin.Active&&hiddenByPlane?(PlanePin.Selecting?"Scan a wall or table\nAim, then pinch to pin the screen":PlanePin.Stage=="creating"?"Creating spatial anchor...":PlanePin.TrackingReady?"Move to the front of the pinned screen":"Waiting for tracked screen anchor"):
            recenterAt>=0?"Centering in 1 second...":!tracked?(actualMode==6?"Waiting for real 6DoF position tracking":"Waiting for glasses rotation tracking"):!capturing&&!referenceMode?"XREAL STABLE SCREEN\nAllow whole-screen sharing on your phone":transferError.Length>0?"Capture transfer error\nOpen controls and copy diagnostics":"Waiting for phone screen...";
    }
    public void ApplyPlanePose(Pose pose){anchorPose=pose;anchored=true;ScreenAnchor.SetPositionAndRotation(pose.position,pose.rotation);RefreshPresentation();}
    public void PlanePlacementChanged(){InputGeometryVersion++;if(material)Layout();RefreshPresentation();}
    public void RestoreAfterPlanePin(){anchored=false;InputGeometryVersion++;CenterNow();RefreshPresentation();}
#if UNITY_ANDROID && !UNITY_EDITOR
    void ReceiveFrame()
    {
        using(var frame=bridge.CallStatic<AndroidJavaObject>("acquireFrame"))
        {
            if(frame==null)return;
            try
            {
                double uploadAt=Time.realtimeSinceStartupAsDouble;
                int w=frame.Get<int>("width"),h=frame.Get<int>("height");
                XrRuntimeHealth.MailboxAgeMs=frame.Get<float>("ageAtAcquireMs");
                if(w<=0||h<=0||w>2048||h>2048)throw new InvalidOperationException("Unexpected capture size");
                if(!texture||texture.width!=w||texture.height!=h)
                {
                    ClearTexture();texture=new Texture2D(w,h,TextureFormat.RGBA32,false,false){name="Phone screen",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                    aspect=(float)w/h;InputGeometryVersion++;Layout();
                }
                using(var buffer=frame.Get<AndroidJavaObject>("buffer"))
                {
                    // GetDirectByteBuffer returns a borrowed NativeArray. Its Java-owned memory must not be disposed.
                    var source=AndroidJNI.GetDirectByteBuffer(buffer.GetRawObject());
                    if(!MirrorFrameCopy.Copy(source,frame.Get<int>("byteCount"),w,h,frame.Get<int>("rowStride"),frame.Get<int>("pixelStride"),texture.GetRawTextureData<byte>()))
                        throw new InvalidOperationException("Invalid frame stride or truncated buffer");
                }
                texture.Apply(false,false);
                // Real captured content always restores the dedicated phone material, including after a grid session.
                referenceMode=false;Image.sharedMaterial=material;material.SetTexture("_MainTex",texture);
                uploaded++;uploadMsTotal+=(float)((Time.realtimeSinceStartupAsDouble-uploadAt)*1000);transferError="";
                if(Time.realtimeSinceStartupAsDouble>=nextTextureReport)
                {
                    nextTextureReport=Time.realtimeSinceStartupAsDouble+5;
                    var pixels=texture.GetRawTextureData<byte>();int nonBlack=0;
                    for(int i=0;i<64;i++){int offset=(int)((long)i*(w*h-1)/63)*4;if(pixels[offset]>8||pixels[offset+1]>8||pixels[offset+2]>8)nonBlack++;}
                    Debug.Log("[StableScreenImage] phone="+w+"x"+h+" rgbNonBlack="+nonBlack+"/64 shader="+material.shader.name+" textureBound="+(material.GetTexture("_MainTex")==texture)+" rendererPhone="+(Image.sharedMaterial==material));
                }
            }
            finally{frame.Call("close");}
        }
    }
#endif
    void ClearTexture(){if(texture){material.SetTexture("_MainTex",null);Destroy(texture);texture=null;}}
    public void Command(string command)
    {
        if(command=="plane-select"){recenterAt=-1;PlanePin.BeginSelection();}
        else if(command=="plane-confirm")PlanePin.ConfirmAfterDelay();
        else if(command=="plane-clear")PlanePin.Clear();
        else if(command=="recenter")recenterAt=Time.unscaledTime+1;
        else if(command=="settings")ReadSettings();
        else if(command=="reference"||command=="reference-off")
        {
            referenceMode=command=="reference"&&!referenceMode;
#if UNITY_ANDROID && !UNITY_EDITOR
            if(referenceMode)bridge?.CallStatic("stopCapture");
#endif
            Image.sharedMaterial=referenceMode?ReferenceMaterial:material;
            if(referenceMode){aspect=16f/9;Layout();}else if(texture){aspect=(float)texture.width/texture.height;Layout();}
            nextTextureReport=0;transferError="";
        }
        else if(command=="exit"){if(switching)exitRequested=true;else StartCoroutine(Exit());}
    }
    IEnumerator Exit()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge?.CallStatic("stopCapture");
#endif
        yield return new WaitForSecondsRealtime(.35f);XREALPlugin.QuitApplication();
    }
    void OnApplicationPause(bool paused){Debug.Log("[StableScreen] Application pause="+paused+"; SDK owns eye output; offscreen primary retains the Unity player.");}
    void OnDestroy()
    {
        Application.onBeforeRender-=BeforeRender;
#if UNITY_ANDROID && !UNITY_EDITOR
        try{bridge?.CallStatic("stopCapture");bridge?.CallStatic("unityStopped");}catch(Exception e){Debug.LogWarning(e.Message);}bridge?.Dispose();
#endif
        ClearTexture();if(material)Destroy(material);
    }
}
