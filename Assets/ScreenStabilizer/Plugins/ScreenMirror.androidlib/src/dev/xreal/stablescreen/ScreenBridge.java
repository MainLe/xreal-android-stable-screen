package dev.xreal.stablescreen;

import android.app.*;
import android.content.*;
import android.hardware.display.DisplayManager;
import android.os.*;
import android.provider.Settings;
import android.util.Log;
import android.view.Display;
import com.unity3d.player.UnityPlayer;
import org.json.JSONObject;
import org.json.JSONArray;
import java.util.ArrayDeque;
import java.io.BufferedReader;
import java.io.InputStreamReader;

public final class ScreenBridge {
    public static final FrameMailbox frames=new FrameMailbox();
    static volatile boolean unityReady,capturing;
    static volatile boolean launchInProgress,unityActivitySeen,glassesActivitySeen;
    static volatile String launchStage="not-requested",managedStage="not-entered",startupError="";
    static volatile String displayStartAllowed="not-checked";
    static volatile boolean sdkOutputReady;
    static volatile float sdkReportedRefreshHz;
    static volatile int sdkFramePresentCount=-1;
    static volatile long sdkOutputReportedAt;
    static volatile boolean refreshDriverAttached;
    static volatile float refreshPulseDrawFps;
    private static volatile boolean handRequested,handFresh;
    private static volatile long handReportedAt;
    static volatile String handStatus="手势操作未开启";
    private static volatile boolean leftHandScreen;
    static volatile boolean handScreenActive,handScreenTracked,handScreenVisible;
    static volatile float handScreenSizeDistance,handScreenFacingDot;
    private static volatile boolean planePinRequested;
    static volatile boolean planePinActive,planeSelecting,planeHasAnchor,planeInputReady,planeCandidate;
    static volatile int planeCount;
    static volatile String planePinStage="off",planePinStatus="未固定到平面",planeAnchorId="";
    private static volatile String planeAnchorDiagnostics="{}";
    static volatile long launchStartedAt,lastRuntimeAt;
    private static long lastMetricsLog;
    private static int launchGeneration;
    static volatile String captureState="尚未开启投屏",unityStatus="等待眼镜画面启动";
    static volatile int captureWidth,captureHeight;
    static volatile int captureSurfaceEdge,sourceWidth,sourceHeight,captureGeometryChanges,actualTrackingMode=3;
    static volatile boolean positionTracked,trackingSwitching;
    static volatile String trackingMessage="";
    static volatile float sourceFrameFps,acceptedFrameFps,captureCopyMs,uploadMs,sourceRefreshHz,glassesRefreshHz,filterAngle,filterMm;
    static volatile long receivedFrames,lastFrameElapsedMs;
    static volatile float uploadFps,renderFps;
    static volatile boolean poseTracked;
    private static Context context;
    private static final Handler main=new Handler(Looper.getMainLooper());
    private static final ArrayDeque<String> pending=new ArrayDeque<>();
    private static final ArrayDeque<String> events=new ArrayDeque<>();
    private ScreenBridge(){}
    public static synchronized void init(Context value){context=value.getApplicationContext();}
    static Context context(){if(context==null&&UnityPlayer.currentActivity!=null)init(UnityPlayer.currentActivity);if(context==null)throw new IllegalStateException("Application unavailable");return context;}
    static android.content.SharedPreferences preferences(){return context().getSharedPreferences("stable_screen",Context.MODE_PRIVATE);}
    public static void event(String value){
        String line=SystemClock.elapsedRealtime()+" "+value;Log.i("StableScreenBoot",line);
        synchronized(events){events.add(line);while(events.size()>48)events.removeFirst();}
    }
    public static void recordFailure(String source,String detail){
        startupError=source+": "+detail;launchStage="failed";launchInProgress=false;unityStatus="启动异常，请复制诊断信息";
        event(startupError.length()>1800?startupError.substring(0,1800):startupError);
        try{preferences().edit().putString("previousStartupFailure",startupError).commit();}catch(RuntimeException ignored){}
    }
    static void activityEvent(String name,String phase,int display,int task){
        event(name+" "+phase+" display="+display+" task="+task);
        if(name.equals("ai.nreal.activitylife.UnityPlayerActivity")||name.equals("dev.xreal.stablescreen.ScreenPlayerActivity")){unityActivitySeen=true;if(launchInProgress)launchStage="unity-activity-"+phase;}
        if(name.equals("ai.nreal.activitylife.NRFakeActivity")){glassesActivitySeen=true;if(launchInProgress)launchStage="glasses-activity-"+phase+"-display-"+display;}
    }
    public static void managedStage(String value){managedStage=value;event("managed "+value);}
    /** Provider evidence, not a claim that the user has visually verified the image. */
    public static void updateSdkOutput(float refreshHz,int presentCount){
        boolean ready=refreshHz>=30&&refreshHz<=240&&presentCount>0;
        sdkReportedRefreshHz=refreshHz;sdkFramePresentCount=presentCount;sdkOutputReportedAt=SystemClock.elapsedRealtime();
        if(sdkOutputReady!=ready)event("sdk-rendering-ready="+ready+" providerHz="+refreshHz+" framePresent="+presentCount);
        sdkOutputReady=ready;
    }
    static boolean isSdkOutputReady(){return sdkOutputReady&&SystemClock.elapsedRealtime()-sdkOutputReportedAt<5000;}
    static boolean continuousRefreshEnabled(){return preferences().getBoolean("continuousRefresh",true);}
    static void setContinuousRefresh(boolean enabled){preferences().edit().putBoolean("continuousRefresh",enabled).apply();event("continuous-refresh="+enabled);}
    static void updateRefreshDriver(boolean attached,float fps){refreshDriverAttached=attached;refreshPulseDrawFps=fps;}
    public static boolean handControlRequested(){return handRequested;}
    public static boolean leftHandScreenRequested(){return leftHandScreen;}
    public static float handScreenHeadingDegrees(){float v=preferences().getFloat("handScreenHeadingDegrees",90f);return Float.isFinite(v)?Math.max(0,Math.min(90,v)):90;}
    static void setHandScreenHeadingDegrees(float value){if(!Float.isFinite(value))return;PhoneGestureService.cancelContact();preferences().edit().putFloat("handScreenHeadingDegrees",Math.max(0,Math.min(90,value))).apply();command("settings");}
    public static boolean handSkeletonVisible(){return preferences().getBoolean("handSkeletonVisible",true);}
    public static void setLeftHandScreen(boolean enabled){PhoneGestureService.cancelContact();if(enabled&&planePinRequested)clearPlanePin();leftHandScreen=enabled;handScreenTracked=handScreenVisible=false;command("settings");}
    public static boolean planePinRequested(){return planePinRequested;}
    public static int planeAimMode(){return preferences().getInt("planeAimMode",0)==1?1:0;}
    static void setPlaneAimMode(int mode){preferences().edit().putInt("planeAimMode",mode==1?1:0).apply();}
    static void startPlanePin(){
        if(!unityReady)throw new IllegalStateException("请先启动眼镜画面");
        PhoneGestureService.cancelContact();leftHandScreen=false;handScreenTracked=handScreenVisible=false;planePinRequested=true;planeInputReady=false;
        preferences().edit().putInt("trackingMode",6).apply();command("settings");command("plane-select");
    }
    static void confirmPlanePin(){if(planePinRequested)command("plane-confirm");}
    public static void clearPlanePin(){PhoneGestureService.cancelContact();planePinRequested=false;planeInputReady=false;command("plane-clear");}
    public static void updatePlanePin(boolean active,boolean selecting,boolean hasAnchor,boolean inputReady,boolean candidate,int count,String stage,String status,String anchorId){planePinActive=active;planeSelecting=selecting;planeHasAnchor=hasAnchor;planeInputReady=inputReady;planeCandidate=candidate;planeCount=count;planePinStage=stage;planePinStatus=status;planeAnchorId=anchorId;}
    public static void updatePlaneAnchorDiagnostics(String json){if(json!=null&&json.length()<=8192)planeAnchorDiagnostics=json;}
    static void setHandSkeletonVisible(boolean enabled){preferences().edit().putBoolean("handSkeletonVisible",enabled).apply();}
    public static void updateHandScreen(boolean active,boolean tracked,boolean visible,float distance,float facingDot){handScreenActive=active;handScreenTracked=active&&tracked;handScreenVisible=active&&tracked&&visible;handScreenSizeDistance=distance;handScreenFacingDot=facingDot;}
    public static int handControlSide(){return preferences().getInt("handSide",1)==0?0:1;}
    public static int handTouchMode(){return preferences().getInt("handTouchMode",1)==0?0:1;}
    public static float directTouchDepth(){float v=preferences().getFloat("directTouchDepth",0f);return Float.isFinite(v)?Math.max(0,Math.min(.20f,v)):0;}
    public static float directTouchMargin(){float v=preferences().getFloat("directTouchMargin",0f);return Float.isFinite(v)?Math.max(0,Math.min(.03f,v)):0;}
    public static float screenDistance(){float v=preferences().getFloat("screenDistance",2f);return Float.isFinite(v)?Math.max(.25f,Math.min(5f,v)):2f;}
    static void setHandTouchMode(int mode){PhoneGestureService.cancelContact();preferences().edit().putInt("handTouchMode",mode==0?0:1).apply();}
    static void setDirectTouchDepth(float value){if(!Float.isFinite(value))return;PhoneGestureService.cancelContact();preferences().edit().putFloat("directTouchDepth",Math.max(0,Math.min(.20f,value))).apply();}
    static void setDirectTouchMargin(float value){if(!Float.isFinite(value))return;PhoneGestureService.cancelContact();preferences().edit().putFloat("directTouchMargin",Math.max(0,Math.min(.03f,value))).apply();}
    static void setScreenDistance(float value){if(!Float.isFinite(value))return;PhoneGestureService.cancelContact();preferences().edit().putFloat("screenDistance",Math.max(.25f,Math.min(5f,value))).apply();command("settings");}
    static void setHandSide(int side){PhoneGestureService.cancelContact();preferences().edit().putInt("handSide",side==0?0:1).apply();}
    public static void setHandControl(boolean enabled){handRequested=enabled;if(!enabled){handFresh=false;PhoneGestureService.cancelContact();}command("hand-settings");}
    public static void updateHandControl(boolean valid,String message){handFresh=valid;handReportedAt=SystemClock.elapsedRealtime();handStatus=message;if(!valid)PhoneGestureService.cancelContact();}
    static boolean phoneInputAllowed(){return handRequested&&handFresh&&unityReady&&capturing&&isSdkOutputReady()&&(!leftHandScreen||handScreenVisible)&&(!planePinRequested||planeInputReady)&&SystemClock.elapsedRealtime()-handReportedAt<600&&SystemClock.elapsedRealtime()-lastRuntimeAt<2500&&context().getSystemService(PowerManager.class).isInteractive()&&!context().getSystemService(KeyguardManager.class).isKeyguardLocked();}
    public static void phonePointer(int phase,float u,float v){PhoneGestureService.pointer(phase,u,v);}
    public static void phoneNavigation(int action){PhoneGestureService.navigation(action);}
    public static void managedError(String condition,String stack){recordFailure("Unity",condition+"\n"+stack);}
    public static void initializeUnity(){
        init(UnityPlayer.currentActivity);managedStage("scene-ready");unityReady=true;launchInProgress=false;launchStage="ready";startupError="";unityStatus="场景已启动，等待姿态更新";
        synchronized(pending){while(!pending.isEmpty())UnityPlayer.UnitySendMessage("Stabilizer","Command",pending.removeFirst());}
        showControls();
    }
    public static void unityStopped(){unityReady=false;sdkOutputReady=false;handRequested=false;handFresh=false;leftHandScreen=false;handScreenActive=handScreenTracked=handScreenVisible=false;planePinRequested=planePinActive=planeSelecting=planeHasAnchor=planeInputReady=planeCandidate=false;planePinStage="off";planePinStatus="未固定到平面";planeCount=0;planeAnchorId="";PhoneGestureService.cancelContact();launchInProgress=false;launchStage="stopped";unityStatus="眼镜画面已退出";event("unity-stopped");}
    public static boolean isCapturing(){return capturing;}
    public static FrameMailbox.Frame acquireFrame(){FrameMailbox.Frame frame=frames.acquire();if(frame!=null)frame.ageAtAcquireMs=Math.max(0,SystemClock.elapsedRealtime()-frame.capturedAtElapsedMs);return frame;}
    public static float screenScale(){return preferences().getFloat("scale",1f);}
    public static boolean headLocked(){return preferences().getBoolean("headLocked",false);}
    public static boolean flipVertical(){return preferences().getBoolean("flipVertical",true);}
    public static int trackingMode(){return preferences().getInt("trackingMode",3)==6?6:3;}
    public static int smoothing(){return Math.max(0,Math.min(3,preferences().getInt("smoothingBaselineV2",0)));}
    public static int captureProfile(){return Math.max(0,Math.min(2,preferences().getInt("captureProfile",1)));}
    public static int targetCaptureFps(){return CaptureSizing.profileFps(captureProfile());}
    public static void setTrackingMode(int mode){if(planePinRequested&&mode!=6)throw new IllegalStateException("请先解除平面固定，再切换到 3DoF");preferences().edit().putInt("trackingMode",mode==6?6:3).apply();command("settings");}
    public static void setSmoothing(int value){preferences().edit().putInt("smoothingBaselineV2",Math.max(0,Math.min(3,value))).apply();command("settings");}
    public static void setCaptureProfile(int value){preferences().edit().putInt("captureProfile",Math.max(0,Math.min(2,value))).apply();ProjectionService.updateProfile();command("settings");}
    public static void updateTracking(int actual,boolean position,boolean switching,String message,float angle,float mm,float textureMs){actualTrackingMode=actual;positionTracked=position;trackingSwitching=switching;trackingMessage=message;filterAngle=angle;filterMm=mm;uploadMs=textureMs;}
    public static void updateDisplayRate(float hz){glassesRefreshHz=hz;}
    public static void resyncCapture(){ProjectionService.resync();}
    public static void setScale(float scale){preferences().edit().putFloat("scale",Math.max(.6f,Math.min(1.8f,scale))).apply();command("settings");}
    public static void setHeadLocked(boolean value){preferences().edit().putBoolean("headLocked",value).apply();command("settings");}
    public static void setFlip(boolean value){preferences().edit().putBoolean("flipVertical",value).apply();command("settings");}
    public static void command(String value){
        if(!unityReady&&"exit".equals(value)){synchronized(pending){pending.clear();}return;}
        if(unityReady)UnityPlayer.UnitySendMessage("Stabilizer","Command",value);
        else synchronized(pending){if(pending.size()<12)pending.add(value);}
    }
    public static void updateRuntime(String status,boolean tracked,float render,float upload){unityStatus=status;poseTracked=tracked;renderFps=render;uploadFps=upload;lastRuntimeAt=SystemClock.elapsedRealtime();if(lastRuntimeAt-lastMetricsLog>=5000){lastMetricsLog=lastRuntimeAt;Log.i("StableScreenMetrics","mode="+actualTrackingMode+" rot="+tracked+" pos="+positionTracked+" switching="+trackingSwitching+" source="+sourceFrameFps+" accepted="+acceptedFrameFps+" uploaded="+upload+" render="+render+" copy-ms="+captureCopyMs+" upload-ms="+uploadMs+" filter-deg="+filterAngle+" content="+captureWidth+"x"+captureHeight);}}
    public static void showControls(){main.post(()->{Intent i=new Intent(context(),ControlActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_SINGLE_TOP);ActivityOptions options=ActivityOptions.makeBasic();options.setLaunchDisplayId(Display.DEFAULT_DISPLAY);context().startActivity(i,options.toBundle());});}
    static void launchXR(Activity activity){
        if(launchInProgress)return;
        final int generation=++launchGeneration;
        launchInProgress=true;launchStartedAt=SystemClock.elapsedRealtime();unityActivitySeen=false;glassesActivitySeen=false;startupError="";managedStage="not-entered";launchStage="sdk-initializing";unityStatus="正在启动眼镜画面…";
        try{
            Display display=glasses();
            if(display==null)throw new IllegalStateException("Glasses display disappeared before launch");
            Intent external=new Intent(activity,ScreenPlayerActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            boolean allowed=((ActivityManager)activity.getSystemService(Context.ACTIVITY_SERVICE)).isActivityStartAllowedOnDisplay(activity,display.getDisplayId(),external);
            displayStartAllowed=Boolean.toString(allowed);event("display-preflight id="+display.getDisplayId()+" allowed="+allowed+" mirrorSetting="+mirrorSetting());
            // This check is diagnostic only. Presentation remains usable when the display cannot host tasks.
            Intent player=new Intent(activity,ScreenPlayerActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_SINGLE_TOP);
            ActivityOptions options=ActivityOptions.makeBasic();options.setLaunchDisplayId(Display.DEFAULT_DISPLAY);
            launchStage="sdk-host-launch-requested";activity.startActivity(player,options.toBundle());event("sdk-host-launch-returned");
        }catch(Exception e){recordFailure("SDK host launch",Log.getStackTraceString(e));showControls();}
        main.postDelayed(()->{
            if(generation==launchGeneration&&!unityReady&&launchInProgress){
                event("startup-timeout last-stage="+launchStage);launchInProgress=false;launchStage="startup-timeout";unityStatus="眼镜画面启动超时，请复制诊断信息";showControls();
            }
        },20000);
    }
    static Display glasses(){
        for(Display d:((DisplayManager)context().getSystemService(Context.DISPLAY_SERVICE)).getDisplays()){
            if(d.getDisplayId()!=Display.DEFAULT_DISPLAY&&new com.xreal.glassesdisplayplugevent.display.DisplayModel(d).getNrealDisplay()>=0)return d;
        }
        return null;
    }
    static String mirrorSetting(){try{String value=Settings.Secure.getString(context().getContentResolver(),"mirror_built_in_display");return value==null?"unset":value;}catch(RuntimeException e){return "unavailable";}}
    static String glassesStatus(){Display d=glasses();if(d==null)return "未发现 XREAL 外接显示器";Display.Mode m=d.getMode();return d.getName()+" · "+m.getPhysicalWidth()+"×"+m.getPhysicalHeight();}
    public static void stopCapture(){handFresh=false;PhoneGestureService.cancelContact();context().stopService(new Intent(context(),ProjectionService.class));}
    static void returnHome(Activity activity){activity.startActivity(new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));}
    static String diagnostic(){
        try{
            JSONArray lifecycle; synchronized(events){lifecycle=new JSONArray(events);}
            String version=context().getPackageManager().getPackageInfo(context().getPackageName(),0).versionName;
            return new JSONObject().put("appVersion",version).put("model",Build.MODEL).put("manufacturer",Build.MANUFACTURER).put("androidApi",Build.VERSION.SDK_INT).put("os",Build.VERSION.RELEASE).put("glasses",glassesStatus()).put("displayPath","SDK native Rendering / SDK-owned Presentation; retained offscreen Unity primary").put("sdkOutputReady",isSdkOutputReady()).put("handControlRequested",handControlRequested()).put("handControlSide",handControlSide()).put("handTouchMode",handTouchMode()==1?"direct-index-contact":"index-normal-projection-pinch").put("cursorProjection","index tip orthogonal to screen plane").put("handSkeletonVisible",handSkeletonVisible()).put("planePinRequested",planePinRequested()).put("planePinActive",planePinActive).put("planePinStage",planePinStage).put("planePinStatus",planePinStatus).put("planeSelecting",planeSelecting).put("planeCount",planeCount).put("planeCandidate",planeCandidate).put("planeHasAnchor",planeHasAnchor).put("planeInputReady",planeInputReady).put("planeAnchorId",planeAnchorId).put("planeAnchorDiagnostics",new JSONObject(planeAnchorDiagnostics)).put("planeAimMode",planeAimMode()==0?"head-centre":"index-ray").put("leftHandScreenRequested",leftHandScreenRequested()).put("leftHandScreenActive",handScreenActive).put("leftHandScreenTracked",handScreenTracked).put("handScreenFollowRotation",true).put("handScreenAlignment","parallel to anatomical palm; 3cm palmar offset; adjustable roll towards thumb side").put("handScreenHeadingDegrees",handScreenHeadingDegrees()).put("leftHandScreenVisible",handScreenVisible).put("handScreenFacingDot",handScreenFacingDot).put("handScreenSizeDistanceMetres",handScreenSizeDistance).put("directTouchDepthMetres",directTouchDepth()).put("directTouchMarginMetres",directTouchMargin()).put("directTouchRearmMetres",.005f).put("directTouchZeroMode","swept plane contact; no hidden hold expansion at margin zero").put("screenDistanceMetres",screenDistance()).put("handInputFresh",handFresh).put("handStatus",handStatus).put("accessibilityConnected",PhoneGestureService.connected()).put("gestureStatus",PhoneGestureService.status()).put("continuousRefreshEnabled",continuousRefreshEnabled()).put("refreshDriverAttached",refreshDriverAttached).put("refreshPulseDrawFps",refreshPulseDrawFps).put("sdkReportedRefreshHz",sdkReportedRefreshHz).put("sdkFramePresentCount",sdkFramePresentCount).put("sdkOutputReportAgeMs",sdkOutputReportedAt==0?-1:SystemClock.elapsedRealtime()-sdkOutputReportedAt).put("displayStartAllowed",displayStartAllowed).put("systemMirrorSetting",mirrorSetting()).put("unityReady",unityReady).put("launchInProgress",launchInProgress).put("launchStage",launchStage).put("managedStage",managedStage).put("unityActivitySeen",unityActivitySeen).put("glassesActivitySeen",glassesActivitySeen).put("startupError",startupError).put("previousStartupFailure",preferences().getString("previousStartupFailure","")).put("startupEvents",lifecycle).put("runtimeHeartbeatAgeMs",lastRuntimeAt==0?-1:SystemClock.elapsedRealtime()-lastRuntimeAt).put("poseTracked",poseTracked).put("requestedTrackingMode",trackingMode()).put("actualTrackingMode",actualTrackingMode).put("positionTracked",positionTracked).put("trackingSwitching",trackingSwitching).put("trackingMessage",trackingMessage).put("smoothing",smoothing()).put("filterCorrectionDegrees",filterAngle).put("filterCorrectionMm",filterMm).put("captureProfile",captureProfile()).put("targetCaptureFps",targetCaptureFps()).put("sourceFrameFps",sourceFrameFps).put("acceptedFrameFps",acceptedFrameFps).put("captureCopyMs",captureCopyMs).put("textureUploadMs",uploadMs).put("sourceRefreshHz",sourceRefreshHz).put("glassesRefreshHz",glassesRefreshHz).put("captureSurfaceEdge",captureSurfaceEdge).put("sourceWidth",sourceWidth).put("sourceHeight",sourceHeight).put("geometryChanges",captureGeometryChanges).put("renderFps",renderFps).put("uploadedFrameFps",uploadFps).put("captureActive",capturing).put("captureState",captureState).put("width",captureWidth).put("height",captureHeight).put("receivedFrames",receivedFrames).put("lastFrameAgeMs",lastFrameElapsedMs==0?-1:SystemClock.elapsedRealtime()-lastFrameElapsedMs).put("mailboxPublished",frames.stats()[0]).put("mailboxReplaced",frames.stats()[1]).put("mailboxDropped",frames.stats()[2]).put("scale",screenScale()).put("headLocked",headLocked()).put("flipVertical",flipVertical()).put("overlayPermission",Settings.canDrawOverlays(context())).put("runtime",unityStatus).put("captureMode","MediaProjection default display; no audio, recording or network transport").toString(2);
        }catch(Exception e){return e.toString();}
    }
    /** Called only from the report worker after an explicit user tap; Android limits this to our own PID. */
    static String diagnosticWithLogs(){
        try{Object health=Class.forName("dev.xreal.common.RuntimeHealthBridge").getMethod("latest").invoke(null);return new JSONObject(diagnostic()).put("runtimeHealth",new JSONObject(health.toString())).put("ownProcessLog",ownLog()).toString(2);}catch(Exception e){return diagnostic();}
    }
    private static String ownLog(){
        java.lang.Process process=null;Runnable timeout=null;
        try{
            process=new ProcessBuilder("logcat","-d","-v","brief","--pid="+android.os.Process.myPid(),"-t","160").redirectErrorStream(true).start();
            final java.lang.Process active=process;timeout=active::destroy;main.postDelayed(timeout,1800);
            StringBuilder text=new StringBuilder();
            try(BufferedReader reader=new BufferedReader(new InputStreamReader(process.getInputStream()))){String line;while((line=reader.readLine())!=null&&text.length()<24000)text.append(line).append('\n');}
            return text.toString();
        }catch(Exception e){return "Own-process log unavailable: "+e.getMessage();}
        finally{if(timeout!=null)main.removeCallbacks(timeout);if(process!=null)process.destroy();}
    }
}
