using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.XR;
using Unity.XR.XREAL;

/// <summary>Bounded frame/heap/GC telemetry. Unsupported provider timings stay -1, never fabricated as zero.</summary>
public sealed class XrRuntimeHealth : MonoBehaviour
{
    [Serializable] public sealed class Snapshot
    {
        public string version,graphicsApi,trackingPath;
        public double uptimeSeconds,intervalSeconds,frameMeanMs,frameMaxMs,frameP95Ms,cpuFrameMs=-1,gpuFrameMs=-1;
        public float updateFps,deviceHz,posePhaseDegrees,posePhaseMm,mailboxAgeMs,textureUploadMs;
        public long managedUsedBytes,unityAllocatedBytes,unityReservedBytes;
        public int gcCollections,targetFps,nativeTargetFps=-1,slowFrames,frames,droppedFrames=-1,presentCount=-1,trackingMode;
        public bool rotationTracked,positionTracked,captureActive;
        public long handPollCalls,handNoTrackedResults,handNativeReadReturnedFalse,handBeforeRenderCalls,handBeforeRenderReuses;
        public double handPollTotalMs,handPollWorstMs;
        public bool handPollingAuditAvailable,handPollReuse;
    }
    public static Snapshot Latest { get; private set; }=new Snapshot();
    public static string LatestJson { get; private set; }="{}";
    public static string AndroidJson { get; private set; }="{}";
    public static float PosePhaseDegrees,PosePhaseMm,MailboxAgeMs,TextureUploadMs;
    public static bool RotationTracked,PositionTracked,CaptureActive;
    public static int TrackingMode;
    public static string TrackingPath="raw camera / fixed world anchor";
    readonly FrameTiming[] timings=new FrameTiming[1];
    readonly List<XRDisplaySubsystem> displays=new List<XRDisplaySubsystem>();
    readonly double[] intervals=new double[2048];
    double previous,windowStart,sum,max,nextSample;int frames,slow,lastGc;
    bool resetClock;
#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaClass android;
#endif
    public static void Attach(GameObject host){if(!host.GetComponent<XrRuntimeHealth>())host.AddComponent<XrRuntimeHealth>();}
    void Start()
    {
        previous=windowStart=Time.realtimeSinceStartupAsDouble;nextSample=windowStart+5;lastGc=GC.CollectionCount(0);
        uint hz=60;if(!XREALPlugin.GetDeviceRefreshRate(ref hz)||hz<30||hz>240)hz=60;
        Application.targetFrameRate=Mathf.Clamp((int)hz,30,120);XREALPlugin.SetTargetFrameRate(Application.targetFrameRate);
#if UNITY_ANDROID && !UNITY_EDITOR
        try{android=new AndroidJavaClass("dev.xreal.common.RuntimeHealthBridge");using(var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))using(var activity=player.GetStatic<AndroidJavaObject>("currentActivity"))android.CallStatic("initialize",activity);}
        catch(Exception error){Debug.LogWarning("[XrHealth] Android recorder unavailable: "+error.Message);android?.Dispose();android=null;}
#endif
    }
    void Update()
    {
        double now=Time.realtimeSinceStartupAsDouble;
        if(resetClock){resetClock=false;previous=windowStart=now;nextSample=now+5;frames=slow=0;sum=max=0;lastGc=GC.CollectionCount(0);}
        double ms=(now-previous)*1000;previous=now;
        // Only explicit lifecycle pauses reset the clock. Real multi-second stalls must remain visible.
        if(ms>0){intervals[frames%intervals.Length]=ms;frames++;sum+=ms;max=Math.Max(max,ms);if(ms>1000.0/Math.Max(30,Application.targetFrameRate)*1.5)slow++;}
        FrameTimingManager.CaptureFrameTimings();if(now<nextSample)return;nextSample=now+5;
        uint hz=0;XREALPlugin.GetDeviceRefreshRate(ref hz);int gc=GC.CollectionCount(0),count=Math.Min(frames,intervals.Length);
        Array.Sort(intervals,0,count);double p95=count==0?0:intervals[Math.Max(0,(int)Math.Ceiling(count*.95)-1)];
        var sample=new Snapshot{version=Application.version,graphicsApi=SystemInfo.graphicsDeviceType.ToString(),trackingPath=TrackingPath,uptimeSeconds=now,intervalSeconds=now-windowStart,frames=frames,updateFps=(float)(frames/Math.Max(.001,now-windowStart)),frameMeanMs=frames==0?0:sum/frames,frameMaxMs=max,frameP95Ms=p95,slowFrames=slow,gcCollections=gc-lastGc,managedUsedBytes=Profiler.GetMonoUsedSizeLong(),unityAllocatedBytes=Profiler.GetTotalAllocatedMemoryLong(),unityReservedBytes=Profiler.GetTotalReservedMemoryLong(),deviceHz=hz,targetFps=Application.targetFrameRate,nativeTargetFps=XREALPlugin.GetTargetFrameRate(),trackingMode=TrackingMode,rotationTracked=RotationTracked,positionTracked=PositionTracked,posePhaseDegrees=PosePhaseDegrees,posePhaseMm=PosePhaseMm,captureActive=CaptureActive,mailboxAgeMs=MailboxAgeMs,textureUploadMs=TextureUploadMs};
        if(FrameTimingManager.GetLatestTimings(1,timings)>0){if(timings[0].cpuFrameTime>0)sample.cpuFrameMs=timings[0].cpuFrameTime;if(timings[0].gpuFrameTime>0)sample.gpuFrameMs=timings[0].gpuFrameTime;}
        displays.Clear();SubsystemManager.GetInstances(displays);
        foreach(var display in displays)if(display.running){if(display.TryGetDroppedFrameCount(out int dropped))sample.droppedFrames=dropped;if(display.TryGetFramePresentCount(out int present))sample.presentCount=present;break;}
#if XREAL_HAND_RUNTIME_AUDIT && UNITY_ANDROID
        sample.handPollingAuditAvailable=true;sample.handPollCalls=XREALHandSubSystem.PollingDiagnostics.Calls;sample.handNoTrackedResults=XREALHandSubSystem.PollingDiagnostics.NoTrackedResults;sample.handNativeReadReturnedFalse=XREALHandSubSystem.PollingDiagnostics.NativeReadReturnedFalse;
        sample.handBeforeRenderCalls=XREALHandSubSystem.PollingDiagnostics.BeforeRenderCalls;sample.handBeforeRenderReuses=XREALHandSubSystem.PollingDiagnostics.BeforeRenderReuses;
        sample.handPollTotalMs=XREALHandSubSystem.PollingDiagnostics.TotalMs;sample.handPollWorstMs=XREALHandSubSystem.PollingDiagnostics.WorstMs;sample.handPollReuse=XREALHandSubSystem.PollingDiagnostics.ReuseBeforeRender;
#endif
        Latest=sample;LatestJson=JsonUtility.ToJson(sample);
#if UNITY_ANDROID && !UNITY_EDITOR
        try{if(android!=null){android.CallStatic("submit",LatestJson);AndroidJson=android.CallStatic<string>("latest");}}catch(Exception error){Debug.LogWarning("[XrHealth] Recorder: "+error.Message);}
#endif
        frames=slow=0;sum=max=0;windowStart=now;lastGc=gc;
    }
    void OnApplicationPause(bool paused){resetClock=true;}
    void OnDestroy(){
#if UNITY_ANDROID && !UNITY_EDITOR
        android?.Dispose();
#endif
    }
}
