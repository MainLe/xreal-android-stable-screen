"""Run the actual Java gesture-service state machine against a small deterministic Android adapter.

This checks dispatch ordering/continuations and fail-closed conditions, not Android's real
accessibility authorization, input delivery, or XR coordinates (which require a phone).
"""
from pathlib import Path
import subprocess

ROOT=Path(__file__).resolve().parent
OUT=ROOT/'build/gesture-service-tests'
JAVA=Path('C:/Program Files/Java/jdk-17/bin')
sources={
'android/content/Context.java':'''package android.content; public class Context { public <T>T getSystemService(Class<T> c){return c.cast(new android.hardware.display.DisplayManager());} }''',
'android/graphics/Point.java':'''package android.graphics; public class Point { public int x,y; }''',
'android/graphics/Path.java':'''package android.graphics; public class Path {public float sx,sy,x,y; public void moveTo(float a,float b){sx=x=a;sy=y=b;}public void lineTo(float a,float b){x=a;y=b;} }''',
'android/os/Looper.java':'''package android.os; public class Looper { public static Looper getMainLooper(){return new Looper();} }''',
'android/os/Handler.java':'''package android.os; public class Handler {
 public static java.util.Map<Runnable,Long> delayed=new java.util.HashMap<>();
 public Handler(Looper l){}public void post(Runnable r){r.run();}
 public void postDelayed(Runnable r,long delay){delayed.put(r,SystemClock.now+delay);}
 public void removeCallbacks(Runnable r){delayed.remove(r);}
 public static void advance(long ms){SystemClock.now+=ms;for(Runnable r:new java.util.ArrayList<>(delayed.keySet()))if(delayed.containsKey(r)&&delayed.get(r)<=SystemClock.now){delayed.remove(r);r.run();}}
}''',
'android/util/Log.java':'''package android.util;public class Log {public static int i(String tag,String message){return 0;} }''',
'android/os/SystemClock.java':'''package android.os; public class SystemClock {public static long now=1000;public static long elapsedRealtime(){return now;} }''',
'android/view/Display.java':'''package android.view; public class Display {public static final int DEFAULT_DISPLAY=0;public static int width=1200,height=2670,rotation;public void getRealSize(android.graphics.Point p){p.x=width;p.y=height;}public int getRotation(){return rotation;} }''',
'android/hardware/display/DisplayManager.java':'''package android.hardware.display; public class DisplayManager { public android.view.Display getDisplay(int id){return new android.view.Display();} }''',
'android/view/ViewConfiguration.java':'''package android.view; public class ViewConfiguration {public static ViewConfiguration get(android.content.Context c){return new ViewConfiguration();}public int getScaledTouchSlop(){return 16;} }''',
'android/view/accessibility/AccessibilityEvent.java':'''package android.view.accessibility; public class AccessibilityEvent {}''',
'android/accessibilityservice/GestureDescription.java':'''package android.accessibilityservice;
public class GestureDescription {
 public StrokeDescription stroke; public int display;
 public static class StrokeDescription {
  static int ids;public int id=++ids,from;public android.graphics.Path path;public boolean continuing;public long duration;public boolean continued;
  public StrokeDescription(android.graphics.Path p,long start,long d,boolean c){path=p;continuing=c;duration=d;}
  public StrokeDescription continueStroke(android.graphics.Path p,long start,long d,boolean c){
   if(!continuing||p.sx!=path.x||p.sy!=path.y)throw new IllegalArgumentException("discontinuous stroke");
   StrokeDescription n=new StrokeDescription(p,start,d,c);n.continued=true;n.from=id;return n;
  }
 }
 public static class Builder {GestureDescription g=new GestureDescription();public Builder setDisplayId(int d){g.display=d;return this;}public Builder addStroke(StrokeDescription s){g.stroke=s;return this;}public GestureDescription build(){return g;}}
}''',
'android/accessibilityservice/AccessibilityService.java':'''package android.accessibilityservice;
public class AccessibilityService extends android.content.Context {
 public static final int GLOBAL_ACTION_BACK=1,GLOBAL_ACTION_HOME=2,GLOBAL_ACTION_RECENTS=3;
 public GestureDescription pending;public GestureResultCallback callback;public int dispatches,navigations,downs,ups,moves,emptyContinuations;public boolean accept=true,touchDown;public float px,py;public int strokeId;
 protected void onServiceConnected(){}public void onInterrupt(){}public void onDestroy(){}public void onAccessibilityEvent(android.view.accessibility.AccessibilityEvent e){}
 public boolean performGlobalAction(int id){navigations++;return true;}
 public boolean dispatchGesture(GestureDescription g,GestureResultCallback c,android.os.Handler h){
  if(!accept)return false;if(pending!=null)throw new AssertionError("overlapping dispatch");pending=g;callback=c;dispatches++;return true;
 }
 // Model AOSP MotionEventInjector: completion follows the LAST REAL EVENT, not
 // the nominal duration. A stationary continued stroke with willContinue=true
 // produces no DOWN, MOVE or UP; it is rejected and does not lift the old finger.
 public void complete(){GestureDescription g=pending;GestureResultCallback c=callback;pending=null;callback=null;
  GestureDescription.StrokeDescription s=g.stroke;float sx=Math.round(s.path.sx),sy=Math.round(s.path.sy),ex=Math.round(s.path.x),ey=Math.round(s.path.y);
  if(s.continued&&(!touchDown||s.from!=strokeId||sx!=px||sy!=py)){touchDown=false;c.onCancelled(g);return;}
  boolean changed=ex!=sx||ey!=sy;strokeId=s.id;
  if(s.continued&&s.continuing&&!changed){emptyContinuations++;c.onCancelled(g);return;}
  if(!s.continued){touchDown=true;downs++;}if(changed)moves++;
  if(changed||!s.continuing)android.os.SystemClock.now+=s.duration;
  px=ex;py=ey;if(!s.continuing){touchDown=false;ups++;}c.onCompleted(g);
 }
 public void cancelFromSystem(){GestureDescription g=pending;GestureResultCallback c=callback;pending=null;callback=null;touchDown=false;c.onCancelled(g);}
 public static class GestureResultCallback {public void onCompleted(GestureDescription g){}public void onCancelled(GestureDescription g){}}
}''',
'dev/xreal/stablescreen/ScreenBridge.java':'''package dev.xreal.stablescreen;
public class ScreenBridge {public static int sourceWidth=1200,sourceHeight=2670,captureWidth=540,captureHeight=1200,captureGeometryChanges;public static boolean allowed;public static boolean phoneInputAllowed(){return allowed;}public static void init(android.content.Context c){} }''',
'dev/xreal/stablescreen/GestureServiceTest.java':'''package dev.xreal.stablescreen;
import android.os.SystemClock;import android.view.Display;
public class GestureServiceTest {
 static void ok(boolean v,String m){if(!v)throw new AssertionError(m);}
 static void begin(PhoneGestureService s){PhoneGestureService.pointer(0,.5f,.5f);ok(s.pending!=null,"pinch down missing");ok(s.pending.display==0,"touch sent to eye display");}
 static void drain(PhoneGestureService s){int n=0;while(s.pending!=null){ok(++n<20,"unbounded gesture resubmission");s.complete();}}
 static void end(PhoneGestureService s){PhoneGestureService.pointer(2,.5f,.5f);drain(s);ok(!s.touchDown&&s.pending==null,"contact did not terminate with actual UP");}
 public static void main(String[] args){
  PhoneGestureService s=new PhoneGestureService();s.onServiceConnected();
  PhoneGestureService.pointer(0,.5f,.5f);ok(s.dispatches==0,"injected while disarmed");
  ScreenBridge.allowed=true;begin(s);drain(s);long quickStart=SystemClock.now;end(s);
  ok(SystemClock.now-quickStart<200,"quick click became a long press");
  ok(s.emptyContinuations==0,"stationary continuation rejected by Android injector");
  begin(s);drain(s);float start=s.px;
  PhoneGestureService.pointer(1,.51f,.5f);drain(s);ok(s.px==start&&s.touchDown,"small jitter became drag or released finger");
  PhoneGestureService.pointer(1,.8f,.4f);ok(s.pending!=null&&s.pending.stroke.continued,"drag not continued");drain(s);ok(s.px>start&&s.touchDown,"drag lost its original pointer");end(s);
  begin(s);drain(s);int count=s.dispatches;
  for(int i=0;i<15;i++){PhoneGestureService.pointer(1,.5f,.5f);android.os.Handler.advance(50);drain(s);}
  ok(s.touchDown&&s.dispatches==count,"stationary hold dispatched an empty continuation");end(s);
  begin(s);drain(s);android.os.Handler.advance(400);drain(s);ok(!s.touchDown,"idle stale tracking kept contact down");
  begin(s);drain(s);Display.rotation=1;android.os.Handler.advance(50);drain(s);ok(!s.touchDown,"rotation did not end stationary contact");Display.rotation=0;
  begin(s);drain(s);ScreenBridge.allowed=false;android.os.Handler.advance(50);drain(s);ok(!s.touchDown,"capture loss did not end stationary contact");ScreenBridge.allowed=true;
  begin(s);s.cancelFromSystem();int n=s.dispatches;PhoneGestureService.pointer(0,.5f,.5f);ok(s.dispatches==n,"system cancellation allowed stuck-pinch retry");PhoneGestureService.pointer(2,0,0);begin(s);end(s);
  PhoneGestureService.pointer(0,Float.NaN,.5f);ok(s.pending==null,"invalid coordinate injected");
  begin(s);int nav=s.navigations;PhoneGestureService.navigation(1);ok(s.navigations==nav,"navigation interrupted active finger");end(s);PhoneGestureService.navigation(2);ok(s.navigations==nav+1,"explicit navigation missing");
  begin(s);PhoneGestureService.cancelContact();drain(s);ok(!s.touchDown,"explicit stop failed");
  begin(s);drain(s);PhoneGestureService.pointer(1,.5001f,.5001f);drain(s);end(s);ok(s.emptyContinuations==0,"subpixel movement became empty continuation");
  begin(s);drain(s);PhoneGestureService.pointer(1,.8f,.4f);PhoneGestureService.pointer(2,.8f,.4f);drain(s);ok(!s.touchDown,"release during moving segment lost UP");
  begin(s);drain(s);PhoneGestureService.pointer(1,.8f,.4f);drain(s);int before=s.dispatches;
  PhoneGestureService.pointer(1,.80001f,.40001f);drain(s);ok(s.dispatches==before,"rounded zero movement dispatched after drag started");end(s);
  int oldDowns=s.downs,oldUps=s.ups;for(int i=0;i<8;i++){begin(s);end(s);}ok(s.downs-oldDowns==8&&s.ups-oldUps==8,"rapid clicks lost a DOWN/UP pair");
  begin(s);drain(s);for(int i=0;i<305;i++){PhoneGestureService.pointer(1,.5f,.5f);android.os.Handler.advance(50);drain(s);}ok(!s.touchDown,"15-second bound failed with fresh stationary input");end(s);
  System.out.println("PASSED: AOSP event-derived callbacks; quick tap UP; stationary hold without empty continuations; quantized drag; idle watchdog; rotation/capture loss; release in flight; gates/cancellation/navigation");
 }
}'''
}
for name,code in sources.items():
    target=OUT/name;target.parent.mkdir(parents=True,exist_ok=True);target.write_text(code,encoding='utf8')
service=ROOT.parent/'Assets/ScreenStabilizer/Plugins/ScreenMirror.androidlib/src/dev/xreal/stablescreen/PhoneGestureService.java'
classes=OUT/'classes';classes.mkdir(exist_ok=True)
subprocess.run([str(JAVA/'javac.exe'),'-J-Xmx192m','-encoding','UTF-8','--release','8','-d',str(classes),*[str(OUT/n) for n in sources],str(service)],check=True)
subprocess.run([str(JAVA/'java.exe'),'-Xmx128m','-cp',str(classes),'dev.xreal.stablescreen.GestureServiceTest'],check=True)
