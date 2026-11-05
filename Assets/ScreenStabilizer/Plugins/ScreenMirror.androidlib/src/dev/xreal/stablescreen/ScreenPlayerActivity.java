package dev.xreal.stablescreen;

import android.app.*;
import android.content.*;
import android.content.res.Configuration;
import android.graphics.PixelFormat;
import android.hardware.HardwareBuffer;
import android.hardware.display.DisplayManager;
import android.media.Image;
import android.media.ImageReader;
import android.os.*;
import android.view.*;
import com.unity3d.player.IUnityPlayerLifecycleEvents;
import com.unity3d.player.UnityPlayer;

/** The SDK exclusively owns the glasses window; an offscreen primary surface keeps Unity alive. */
public final class ScreenPlayerActivity extends Activity implements IUnityPlayerLifecycleEvents, DisplayManager.DisplayListener {
    private UnityPlayer player;
    private ImageReader primarySink;
    private HandlerThread sinkThread;
    private SdkRefreshDriver refreshDriver;
    private DisplayManager displays;
    private int glassesId=-1;
    private boolean destroying,playerRunning,surfaceBound,receiverRegistered;
    private final Handler main=new Handler(Looper.getMainLooper());
    private final BroadcastReceiver powerEvents=new BroadcastReceiver(){
        @Override public void onReceive(Context context,Intent intent){
            if(Intent.ACTION_SCREEN_OFF.equals(intent.getAction())){ScreenBridge.stopCapture();pausePlayer();}
            else resumePlayer();
        }
    };
    @Override protected void onCreate(Bundle saved){
        super.onCreate(saved);ScreenBridge.init(this);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        displays=getSystemService(DisplayManager.class);displays.registerDisplayListener(this,main);
        IntentFilter events=new IntentFilter(Intent.ACTION_SCREEN_OFF);events.addAction(Intent.ACTION_SCREEN_ON);events.addAction(Intent.ACTION_USER_PRESENT);
        if(Build.VERSION.SDK_INT>=33)registerReceiver(powerEvents,events,Context.RECEIVER_NOT_EXPORTED);else registerReceiver(powerEvents,events);
        receiverRegistered=true;
        try{
            player=new UnityPlayer(this,this);setContentView(player);player.requestFocus();
            createPrimarySink();
            refreshDriver=new SdkRefreshDriver(glassesId);refreshDriver.setActive(unlocked());refreshDriver.start();
        }catch(RuntimeException error){ScreenBridge.recordFailure("SDK output host",android.util.Log.getStackTraceString(error));ScreenBridge.showControls();}
    }
    private boolean unlocked(){return getSystemService(PowerManager.class).isInteractive()&&!getSystemService(KeyguardManager.class).isKeyguardLocked();}
    private void resumePlayer(){
        if(destroying||player==null||!surfaceBound||!unlocked())return;
        if(!playerRunning){player.resume();playerRunning=true;ScreenBridge.event("sdk-host-player-resumed");}
        if(refreshDriver!=null)refreshDriver.setActive(true);
        player.windowFocusChanged(true);
    }
    private void pausePlayer(){
        if(player==null||!playerRunning)return;
        if(refreshDriver!=null)refreshDriver.setActive(false);
        ScreenBridge.updateSdkOutput(0,-1);player.windowFocusChanged(false);player.pause();playerRunning=false;ScreenBridge.event("sdk-host-player-paused");
    }
    private void createPrimarySink(){
        Display display=ScreenBridge.glasses();
        if(display==null)throw new IllegalStateException("Glasses display unavailable");
        glassesId=display.getDisplayId();
        // The phone Activity may stop while another app is being captured. Its normal SurfaceView
        // would then disappear. Drain this tiny mirror target without creating another eye window.
        // XR eye texture sizes and native single-buffer presentation remain owned by the SDK.
        sinkThread=new HandlerThread("StableUnityPrimarySink");sinkThread.start();
        primarySink=ImageReader.newInstance(64,64,PixelFormat.RGBA_8888,3,
                HardwareBuffer.USAGE_GPU_COLOR_OUTPUT|HardwareBuffer.USAGE_GPU_SAMPLED_IMAGE);
        primarySink.setOnImageAvailableListener(reader->{
            try(Image image=reader.acquireLatestImage()){
                // Consume and release immediately; this is an unused mirror, not phone capture.
            }catch(IllegalStateException ignored){/* Reader may close during player shutdown. */}
        },new Handler(sinkThread.getLooper()));
        if(!player.displayChanged(0,primarySink.getSurface()))throw new IllegalStateException("Unity rejected the offscreen primary surface");
        surfaceBound=true;ScreenBridge.event("offscreen-primary-bound 64x64; SDK owns glasses display="+glassesId);resumePlayer();
    }
    @Override protected void onResume(){super.onResume();resumePlayer();}
    @Override protected void onPause(){
        // Native eye presentation and the retained primary target survive phone app switches.
        if(!surfaceBound||isFinishing()||!unlocked())pausePlayer();
        super.onPause();
    }
    @Override protected void onStop(){if(!surfaceBound||isFinishing())pausePlayer();super.onStop();}
    @Override public void onWindowFocusChanged(boolean focus){super.onWindowFocusChanged(focus);if(surfaceBound)resumePlayer();else if(player!=null)player.windowFocusChanged(focus);}
    @Override public void onConfigurationChanged(Configuration config){super.onConfigurationChanged(config);if(player!=null)player.configurationChanged(config);}
    @Override public void onLowMemory(){super.onLowMemory();if(player!=null)player.lowMemory();}
    @Override public void onTrimMemory(int level){super.onTrimMemory(level);if(level==TRIM_MEMORY_RUNNING_CRITICAL&&player!=null)player.lowMemory();}
    @Override protected void onNewIntent(Intent intent){super.onNewIntent(intent);setIntent(intent);if(player!=null)player.newIntent(intent);resumePlayer();}
    @Override public void onBackPressed(){ScreenBridge.showControls();}
    @Override public void onUnityPlayerUnloaded(){ScreenBridge.unityStopped();finish();}
    @Override public void onUnityPlayerQuitted(){ScreenBridge.unityStopped();}
    @Override public void onDisplayAdded(int id){}
    @Override public void onDisplayChanged(int id){if(id==glassesId&&surfaceBound)resumePlayer();}
    @Override public void onDisplayRemoved(int id){if(id==glassesId){surfaceBound=false;ScreenBridge.stopCapture();pausePlayer();ScreenBridge.unityStopped();ScreenBridge.showControls();}}
    @Override protected void onDestroy(){
        destroying=true;ScreenBridge.stopCapture();
        if(refreshDriver!=null){refreshDriver.stop();refreshDriver=null;}
        if(receiverRegistered)unregisterReceiver(powerEvents);if(displays!=null)displays.unregisterDisplayListener(this);
        surfaceBound=false;ScreenBridge.updateSdkOutput(0,-1);
        if(player!=null){player.destroy();player=null;}
        if(primarySink!=null){primarySink.setOnImageAvailableListener(null,null);primarySink.close();primarySink=null;}
        if(sinkThread!=null){sinkThread.quitSafely();sinkThread=null;}
        super.onDestroy();
    }
}
