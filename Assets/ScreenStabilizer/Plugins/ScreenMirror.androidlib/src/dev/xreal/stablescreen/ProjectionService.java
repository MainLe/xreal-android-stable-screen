package dev.xreal.stablescreen;

import android.app.*;
import android.content.*;
import android.content.pm.ServiceInfo;
import android.graphics.PixelFormat;
import android.hardware.display.*;
import android.media.*;
import android.media.projection.*;
import android.os.*;
import android.util.DisplayMetrics;
import android.util.Log;
import android.view.Display;
import android.view.Surface;

/** User-started, on-device whole-screen projection. Never records or transmits frames. */
public final class ProjectionService extends Service {
    private static final String CHANNEL="stable_screen_projection";
    private static final int NOTICE=4701;
    private static volatile ProjectionService instance;
    private HandlerThread worker;
    private Handler handler;
    private MediaProjection projection;
    private VirtualDisplay display;
    private ImageReader reader;
    private boolean started,cleaned;
    private int edge,sourceWidth,sourceHeight,density,glassesDisplayId=-1,targetFps;
    private long nextFrameNs,metricsAt,metricsImages,metricsPublished,copyTotalNs,lastLogAt;
    private int[] crop;
    private DisplayManager displays;
    public static boolean isStartingOrRunning(){return instance!=null;}
    private final MediaProjection.Callback callback=new MediaProjection.Callback(){
        @Override public void onStop(){cleanup("系统已停止投屏；再次开启需要重新授权");stopSelf();}
        @Override public void onCapturedContentResize(int w,int h){scheduleGeometry();}
    };
    private final DisplayManager.DisplayListener displayListener=new DisplayManager.DisplayListener(){
        @Override public void onDisplayAdded(int id){}
        @Override public void onDisplayChanged(int id){if(id==Display.DEFAULT_DISPLAY)scheduleGeometry();}
        @Override public void onDisplayRemoved(int id){if(id==glassesDisplayId){cleanup("眼镜已断开，投屏已停止");stopSelf();}}
    };
    private final Runnable geometryUpdate=()->{if(!cleaned&&display!=null)try{updateGeometry();}catch(Exception e){fail(e);}};
    private void scheduleGeometry(){if(handler!=null){handler.removeCallbacks(geometryUpdate);handler.postDelayed(geometryUpdate,80);}}
    public static void updateProfile(){ProjectionService service=instance;if(service!=null)service.handler.post(()->{if(!service.cleaned)try{service.configureSurface(false);}catch(Exception e){service.fail(e);}});}
    public static void resync(){ProjectionService service=instance;if(service!=null)service.handler.post(()->{if(!service.cleaned)try{service.configureSurface(true);}catch(Exception e){service.fail(e);}});}
    @Override public void onCreate(){
        super.onCreate();instance=this;ScreenBridge.init(this);ScreenBridge.captureState="正在准备投屏";
        worker=new HandlerThread("StableScreenCapture");worker.start();handler=new Handler(worker.getLooper());
        NotificationManager manager=(NotificationManager)getSystemService(NOTIFICATION_SERVICE);
        manager.createNotificationChannel(new NotificationChannel(CHANNEL,"稳屏投射",NotificationManager.IMPORTANCE_LOW));
        displays=(DisplayManager)getSystemService(DISPLAY_SERVICE);displays.registerDisplayListener(displayListener,handler);
    }
    private PendingIntent control(String action,int id){return PendingIntent.getBroadcast(this,id,new Intent(this,ControlReceiver.class).setAction(action),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);}
    private Notification notification(){
        PendingIntent open=PendingIntent.getActivity(this,1,new Intent(this,ControlActivity.class).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
        return new Notification.Builder(this,CHANNEL).setSmallIcon(android.R.drawable.ic_menu_view).setContentTitle("XREAL 稳屏正在投射手机屏幕").setContentText("点击打开控制；可随时停止投屏").setContentIntent(open).setOngoing(true).setOnlyAlertOnce(true).setCategory(Notification.CATEGORY_SERVICE)
            .addAction(new Notification.Action.Builder(null,"屏幕居中",control("stable.center",2)).build())
            .addAction(new Notification.Action.Builder(null,"停止投屏",control("stable.stop",3)).build()).build();
    }
    @Override public int onStartCommand(Intent intent,int flags,int startId){
        if(started)return START_NOT_STICKY;
        started=true;
        try{
            // Android 14+: startForeground must precede getMediaProjection, after the consent Activity.
            startForeground(NOTICE,notification(),ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION);
            if(intent==null)throw new IllegalArgumentException("授权已失效，请重新开启投屏");
            final int result=intent.getIntExtra("result",Activity.RESULT_CANCELED);
            final Intent grant=intent.getParcelableExtra("grant");
            if(result!=Activity.RESULT_OK||grant==null)throw new IllegalArgumentException("未取得系统录屏授权");
            handler.post(()->startProjection(result,grant));
        }catch(Exception e){fail(e);}
        return START_NOT_STICKY;
    }
    private void startProjection(int result,Intent grant){
        try{
            if(cleaned||instance!=this)return;
            Display glasses=ScreenBridge.glasses();if(glasses==null)throw new IllegalStateException("未连接眼镜");glassesDisplayId=glasses.getDisplayId();
            MediaProjectionManager manager=(MediaProjectionManager)getSystemService(MEDIA_PROJECTION_SERVICE);
            projection=manager.getMediaProjection(result,grant);
            if(projection==null)throw new IllegalStateException("录屏授权无效");
            projection.registerCallback(callback,handler);
            int profile=ScreenBridge.captureProfile();edge=CaptureSizing.profileEdge(profile);targetFps=CaptureSizing.profileFps(profile);
            density=getResources().getConfiguration().densityDpi;updateGeometry();
            reader=createReader(edge);voteFrameRate(reader.getSurface());
            ScreenBridge.frames.reset(true);ScreenBridge.receivedFrames=0;ScreenBridge.lastFrameElapsedMs=0;
            // Exactly one creation per token. This square surface never resizes on phone rotation.
            // Android 12L+ fits and centers the whole display; only its real content is packed below.
            display=projection.createVirtualDisplay("XREAL Stable Screen",edge,edge,density,DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,reader.getSurface(),null,handler);
            ScreenBridge.captureSurfaceEdge=edge;ScreenBridge.capturing=true;ScreenBridge.captureState="整屏投射中";
            metricsAt=SystemClock.elapsedRealtime();ScreenBridge.event("capture-start fixed-surface="+edge+" target-fps="+targetFps);
        }catch(Exception e){fail(e);}
    }
    private ImageReader createReader(int size){ImageReader next=ImageReader.newInstance(size,size,PixelFormat.RGBA_8888,3);next.setOnImageAvailableListener(this::onImage,handler);return next;}
    private void voteFrameRate(Surface surface){if(Build.VERSION.SDK_INT>=30)try{surface.setFrameRate(targetFps,Surface.FRAME_RATE_COMPATIBILITY_DEFAULT);}catch(RuntimeException e){Log.w("StableScreenCapture","Frame rate hint unavailable",e);}}
    private void updateGeometry(){
        Display phone=displays.getDisplay(Display.DEFAULT_DISPLAY);if(phone==null)throw new IllegalStateException("找不到手机主屏幕");
        DisplayMetrics metrics=new DisplayMetrics();phone.getRealMetrics(metrics);
        if(crop!=null&&metrics.widthPixels==sourceWidth&&metrics.heightPixels==sourceHeight&&ScreenBridge.captureSurfaceEdge==edge)return;
        sourceWidth=metrics.widthPixels;sourceHeight=metrics.heightPixels;crop=CaptureSizing.squareCrop(sourceWidth,sourceHeight,edge);
        ScreenBridge.sourceWidth=sourceWidth;ScreenBridge.sourceHeight=sourceHeight;ScreenBridge.sourceRefreshHz=phone.getRefreshRate();
        ScreenBridge.captureWidth=crop[2];ScreenBridge.captureHeight=crop[3];ScreenBridge.captureSurfaceEdge=edge;ScreenBridge.captureGeometryChanges++;
        ScreenBridge.frames.reset(ScreenBridge.capturing);nextFrameNs=0;
        ScreenBridge.event("capture-content "+sourceWidth+"x"+sourceHeight+" crop="+crop[0]+","+crop[1]+","+crop[2]+","+crop[3]+" fixed-surface="+edge);
    }
    private void onImage(ImageReader source){
        if(cleaned||source!=reader||instance!=this)return;
        try(Image image=source.acquireLatestImage()){
            if(image==null||cleaned||instance!=this||!ScreenBridge.capturing)return;
            metricsImages++;
            if(image.getWidth()!=edge||image.getHeight()!=edge)return;
            long now=System.nanoTime(),interval=1000000000L/targetFps;
            // Tolerate callback scheduling jitter instead of accidentally accepting only every other 60-Hz frame.
            if(now+Math.min(1800000L,interval/10)<nextFrameNs){publishMetrics();return;}
            Image.Plane plane=image.getPlanes()[0];long elapsed=SystemClock.elapsedRealtime(),copyAt=System.nanoTime();
            // Copy to owned direct memory before closing the Image. Stopping/rotating cannot invalidate a Unity lease.
            if(ScreenBridge.frames.publishCropped(plane.getBuffer(),edge,edge,plane.getRowStride(),plane.getPixelStride(),crop[0],crop[1],crop[2],crop[3],elapsed,image.getTimestamp())){
                nextFrameNs=Math.max(nextFrameNs,now-interval/2)+interval;metricsPublished++;copyTotalNs+=System.nanoTime()-copyAt;ScreenBridge.receivedFrames++;ScreenBridge.lastFrameElapsedMs=elapsed;
            }
            publishMetrics();
        }catch(IllegalStateException e){if(!cleaned)fail(e);}catch(Exception e){fail(e);}
    }
    private void publishMetrics(){
        long now=SystemClock.elapsedRealtime(),elapsed=now-metricsAt;if(elapsed<1000)return;
        ScreenBridge.sourceFrameFps=metricsImages*1000f/elapsed;ScreenBridge.acceptedFrameFps=metricsPublished*1000f/elapsed;
        ScreenBridge.captureCopyMs=metricsPublished==0?0:copyTotalNs/(metricsPublished*1000000f);
        if(now-lastLogAt>=5000){lastLogAt=now;Log.i("StableScreenCapture","source="+ScreenBridge.sourceFrameFps+" accepted="+ScreenBridge.acceptedFrameFps+" copy-ms="+ScreenBridge.captureCopyMs+" target="+targetFps+" content="+ScreenBridge.captureWidth+"x"+ScreenBridge.captureHeight);}
        metricsAt=now;metricsImages=metricsPublished=copyTotalNs=0;
    }
    private void configureSurface(boolean force){
        if(cleaned||display==null)return;
        int profile=ScreenBridge.captureProfile(),nextEdge=CaptureSizing.profileEdge(profile);targetFps=CaptureSizing.profileFps(profile);nextFrameNs=0;
        if(!force&&nextEdge==edge){voteFrameRate(reader.getSurface());return;}
        ImageReader next=createReader(nextEdge),previous=reader;display.setSurface(null);ScreenBridge.frames.reset(true);
        try{edge=nextEdge;crop=null;updateGeometry();display.resize(edge,edge,density);reader=next;voteFrameRate(next.getSurface());display.setSurface(next.getSurface());ScreenBridge.frames.reset(true);}
        catch(RuntimeException e){reader=null;next.close();if(previous!=null)previous.close();throw e;}
        if(previous!=null){previous.setOnImageAvailableListener(null,null);previous.close();}
        ScreenBridge.event("capture-surface-rebound edge="+edge+" target="+targetFps+" manual="+force);
    }
    private void fail(Exception e){Log.e("StableScreen", "Projection failed",e);cleanup("投屏失败："+e.getClass().getSimpleName()+" / "+e.getMessage());stopSelf();}
    private void cleanup(String reason){
        if(cleaned)return;cleaned=true;if(handler!=null)handler.removeCallbacks(geometryUpdate);
        if(instance==null||instance==this){ScreenBridge.capturing=false;ScreenBridge.captureState=reason;ScreenBridge.frames.reset(false);}
        if(display!=null){try{display.release();}catch(RuntimeException e){Log.w("StableScreen","Display release",e);}display=null;}
        if(reader!=null){try{reader.setOnImageAvailableListener(null,null);reader.close();}catch(RuntimeException e){Log.w("StableScreen","Reader release",e);}reader=null;}
        if(projection!=null){try{projection.unregisterCallback(callback);projection.stop();}catch(RuntimeException e){Log.w("StableScreen","Projection already stopped",e);}projection=null;}
    }
    @Override public void onDestroy(){
        if(instance==this)instance=null;
        if(displays!=null)displays.unregisterDisplayListener(displayListener);
        if(handler!=null){handler.post(()->cleanup("投屏已停止"));worker.quitSafely();}
        stopForeground(STOP_FOREGROUND_REMOVE);super.onDestroy();
    }
    @Override public IBinder onBind(Intent intent){return null;}
}
