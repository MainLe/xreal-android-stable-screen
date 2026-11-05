package dev.xreal.stablescreen;

import android.accessibilityservice.AccessibilityService;
import android.accessibilityservice.GestureDescription;
import android.graphics.Path;
import android.graphics.Point;
import android.hardware.display.DisplayManager;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.util.Log;
import android.view.Display;
import android.view.accessibility.AccessibilityEvent;

/** Explicitly armed, user-enabled default-display gestures. No node-tree access or autonomous actions. */
public final class PhoneGestureService extends AccessibilityService {
    private static volatile PhoneGestureService instance;
    private static volatile String status="请在系统无障碍中开启服务";
    private final Handler main=new Handler(Looper.getMainLooper());
    private GestureDescription.StrokeDescription stroke;
    private boolean inFlight,down,ending,blocked,dragging;
    private float x,y,endX,endY,startX,startY;
    private int width,height,rotation,geometry;
    private long lastInput,started;
    private int segments,messages;
    private long lastMoveLog;
    private final Runnable watchdog=new Runnable(){@Override public void run(){
        if(!down)return;
        if(!ScreenBridge.phoneInputAllowed()||!geometryMatches()||SystemClock.elapsedRealtime()-lastInput>350||SystemClock.elapsedRealtime()-started>15000){
            Log.i("StablePhoneInput","watchdog release age="+(SystemClock.elapsedRealtime()-lastInput));finishContact();
        }else main.postDelayed(this,50);
    }};
    static boolean connected(){return instance!=null;}
    static String status(){return status;}
    @Override protected void onServiceConnected(){ScreenBridge.init(this);instance=this;status="无障碍已连接，等待手势操控";}
    @Override public void onAccessibilityEvent(AccessibilityEvent event){} // Deliberately do not inspect content.
    @Override public void onInterrupt(){finishContact();}
    @Override public void onDestroy(){finishContact();instance=null;status="无障碍服务已断开";super.onDestroy();}
    public static void pointer(int phase,float u,float v){PhoneGestureService s=instance;if(s!=null)s.main.post(()->s.update(phase,u,v));else if(phase==0)status="无障碍未连接，请先在系统设置中开启";}
    public static void cancelContact(){PhoneGestureService s=instance;if(s!=null)s.main.post(s::finishContact);}
    public static void navigation(int action){PhoneGestureService s=instance;if(s!=null)s.main.post(()->{
        if(!ScreenBridge.phoneInputAllowed()||s.down||s.inFlight)return;
        int id=action==1?GLOBAL_ACTION_BACK:action==2?GLOBAL_ACTION_HOME:action==3?GLOBAL_ACTION_RECENTS:0;
        if(id!=0)status=s.performGlobalAction(id)?"导航已执行":"系统未接受导航";
    });}
    private Display display(){return getSystemService(DisplayManager.class).getDisplay(Display.DEFAULT_DISPLAY);}
    private boolean geometryMatches(){
        Display d=display();if(d==null)return false;Point size=new Point();d.getRealSize(size);
        return size.x==width&&size.y==height&&d.getRotation()==rotation&&ScreenBridge.sourceWidth==width&&ScreenBridge.sourceHeight==height&&geometry==ScreenBridge.captureGeometryChanges&&ScreenBridge.captureHeight>0&&Math.abs((float)ScreenBridge.captureWidth/ScreenBridge.captureHeight-(float)width/height)<.03f;
    }
    private void update(int phase,float u,float v){
        if(phase==3){finishContact();return;}
        if(phase==2){if(down)Log.i("StablePhoneInput","release heldMs="+(SystemClock.elapsedRealtime()-started)+" moves="+messages);blocked=false;down=false;ending=true;main.removeCallbacks(watchdog);if(!inFlight)segment();return;}
        if(!Float.isFinite(u)||!Float.isFinite(v)||u<0||u>1||v<0||v>1||!ScreenBridge.phoneInputAllowed()){finishContact();return;}
        if(phase==0){
            if(inFlight||stroke!=null||blocked)return;
            Display d=display();if(d==null)return;Point size=new Point();d.getRealSize(size);width=size.x;height=size.y;rotation=d.getRotation();geometry=ScreenBridge.captureGeometryChanges;
            if(!geometryMatches()){status="等待横竖屏采集同步";return;}
            // GestureDescription rounds samples to integer pixels. Compare the same
            // coordinates before dispatching, otherwise subpixel paths can be empty.
            x=endX=pixel(u,width);y=endY=pixel(v,height);
            startX=x;startY=y;dragging=false;
            down=true;ending=false;segments=messages=0;started=lastInput=SystemClock.elapsedRealtime();
            Log.i("StablePhoneInput","down x="+x+" y="+y+" display=0 size="+width+"x"+height);
            main.removeCallbacks(watchdog);main.postDelayed(watchdog,50);segment();
        }else if(phase==1&&down){
            lastInput=SystemClock.elapsedRealtime();
            if(!geometryMatches()){finishContact();return;}
            float nx=pixel(u,width),ny=pixel(v,height);messages++;
            int slop=android.view.ViewConfiguration.get(this).getScaledTouchSlop();
            if(dragging||(nx-startX)*(nx-startX)+(ny-startY)*(ny-startY)>slop*slop){dragging=true;x=nx;y=ny;}
            if(SystemClock.elapsedRealtime()-lastMoveLog>=250){lastMoveLog=SystemClock.elapsedRealtime();Log.i("StablePhoneInput","move x="+x+" y="+y+" dragging="+dragging);}
            if(!inFlight)segment();
        }
    }
    private static float pixel(float normalized,int extent){return Math.max(1,Math.min(extent-2,Math.round(normalized*(extent-1))));}
    private void finishContact(){down=false;ending=true;blocked=false;main.removeCallbacks(watchdog);x=endX;y=endY;if(!inFlight)segment();}
    private void segment(){
        if(inFlight)return;
        if(stroke==null&&!down){ending=false;return;}
        long now=SystemClock.elapsedRealtime();
        if(!ScreenBridge.phoneInputAllowed()||!geometryMatches()||now-lastInput>350||now-started>15000){down=false;ending=true;x=endX;y=endY;}
        if(stroke==null&&!down){ending=false;main.removeCallbacks(watchdog);return;}
        // AOSP emits MOVE only when a sampled coordinate changes. A stationary
        // continued stroke marked willContinue produces NO events, is reported
        // cancelled, and can leave the old pointer down. Keep the existing stroke
        // instead; movement or release continues it. The watchdog runs separately
        // because a stationary first stroke can complete immediately after DOWN.
        if(stroke!=null&&down&&!ending&&x==endX&&y==endY){status="手势按住，等待移动或松开";return;}
        boolean continuing=down&&!ending;Path path=new Path();path.moveTo(endX,endY);path.lineTo(x,y);
        GestureDescription.StrokeDescription next;
        try{next=stroke==null?new GestureDescription.StrokeDescription(path,0,24,continuing):stroke.continueStroke(path,0,continuing?24:1,continuing);}
        catch(RuntimeException e){Log.i("StablePhoneInput","path error "+e);reset("手势路径已取消");return;}
        endX=x;endY=y;stroke=next;inFlight=true;segments++;long sentAt=SystemClock.elapsedRealtime();
        GestureDescription gesture=new GestureDescription.Builder().setDisplayId(Display.DEFAULT_DISPLAY).addStroke(next).build();
        boolean accepted=dispatchGesture(gesture,new GestureResultCallback(){
            @Override public void onCompleted(GestureDescription description){inFlight=false;if(continuing){status="手势触控中";segment();}else{Log.i("StablePhoneInput","up completed heldMs="+(SystemClock.elapsedRealtime()-started)+" segments="+segments+" callbackMs="+(SystemClock.elapsedRealtime()-sentAt));reset("手势已释放");}}
            @Override public void onCancelled(GestureDescription description){Log.i("StablePhoneInput","cancelled continuing="+continuing+" callbackMs="+(SystemClock.elapsedRealtime()-sentAt));reset("系统取消了手势，请松开后重试");blocked=true;}
        },main);
        if(!accepted){Log.i("StablePhoneInput","dispatch rejected");reset("系统未接受手势注入");}
    }
    private void reset(String message){main.removeCallbacks(watchdog);stroke=null;inFlight=down=ending=false;status=message;}
}
