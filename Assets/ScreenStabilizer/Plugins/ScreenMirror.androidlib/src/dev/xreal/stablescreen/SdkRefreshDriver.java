package dev.xreal.stablescreen;

import android.graphics.Canvas;
import android.graphics.Paint;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.view.SurfaceView;
import android.view.View;
import android.view.ViewGroup;
import android.view.inspector.WindowInspector;
import android.widget.FrameLayout;

/** Keep the SDK's existing single-buffer eye window presenting when the phone UI is idle. */
final class SdkRefreshDriver {
    private final int displayId;
    private final Handler main=new Handler(Looper.getMainLooper());
    private ViewGroup parent;
    private PulseView pulse;
    private boolean active,stopped;
    private long lastReport=SystemClock.elapsedRealtime(),lastDrawCount;
    private final Runnable monitor=this::check;

    SdkRefreshDriver(int displayId){this.displayId=displayId;}
    void start(){main.removeCallbacks(monitor);main.post(monitor);}
    void setActive(boolean value){active=value;if(pulse!=null)pulse.refreshState();}
    void stop(){
        stopped=true;main.removeCallbacks(monitor);detach();
        ScreenBridge.updateRefreshDriver(false,0);
    }
    private static SurfaceView eyeSurface(View view){
        if(view instanceof SurfaceView&&((SurfaceView)view).getHolder().getSurface().isValid())return (SurfaceView)view;
        if(view instanceof ViewGroup){ViewGroup group=(ViewGroup)view;for(int i=0;i<group.getChildCount();i++){SurfaceView found=eyeSurface(group.getChildAt(i));if(found!=null)return found;}}
        return null;
    }
    private void detach(){
        if(pulse!=null){pulse.running=false;if(pulse.getParent() instanceof ViewGroup)((ViewGroup)pulse.getParent()).removeView(pulse);}
        pulse=null;parent=null;lastDrawCount=0;lastReport=SystemClock.elapsedRealtime();
    }
    private void check(){
        if(stopped)return;
        if(parent!=null&&(!parent.isAttachedToWindow()||parent.getDisplay()==null||parent.getDisplay().getDisplayId()!=displayId))detach();
        if(parent==null){
            // Only inspect our own process's public window roots. Never create a second window,
            // reflect into SDK internals, touch its render surface, or draw over the phone UI.
            for(View root:WindowInspector.getGlobalWindowViews()){
                if(!(root instanceof ViewGroup)||!root.isAttachedToWindow()||root.getDisplay()==null||root.getDisplay().getDisplayId()!=displayId||eyeSurface(root)==null)continue;
                parent=(ViewGroup)root;pulse=new PulseView(parent.getContext());
                if(parent instanceof FrameLayout)parent.addView(pulse,new FrameLayout.LayoutParams(2,2,android.view.Gravity.TOP|android.view.Gravity.LEFT));
                else parent.addView(pulse,new ViewGroup.LayoutParams(2,2));
                pulse.setX(0);pulse.setY(0);
                ScreenBridge.event("sdk-refresh-driver attached display="+displayId+" hardware="+parent.isHardwareAccelerated());
                break;
            }
        }
        if(pulse!=null)pulse.refreshState();
        long now=SystemClock.elapsedRealtime();
        if(now-lastReport>=1000){
            long count=pulse==null?0:pulse.drawCount;
            ScreenBridge.updateRefreshDriver(pulse!=null,(count-lastDrawCount)*1000f/(now-lastReport));
            lastDrawCount=count;lastReport=now;
        }
        main.postDelayed(monitor,parent==null?200:500);
    }
    private final class PulseView extends View {
        final Paint paint=new Paint();
        boolean running;
        long drawCount;
        PulseView(android.content.Context context){
            super(context);setWillNotDraw(false);setClickable(false);setFocusable(false);
            setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        }
        void refreshState(){
            boolean next=active&&!stopped&&ScreenBridge.continuousRefreshEnabled();
            if(next!=running){running=next;setVisibility(next?View.VISIBLE:View.INVISIBLE);if(next)postInvalidateOnAnimation();}
        }
        @Override protected void onDraw(Canvas canvas){
            if(!running)return;
            // A changing 2x2 dark corner ensures a real window buffer/damage update rather than
            // a no-op invalidation. SDK pose prediction and eye pixels are left to its renderer.
            paint.setColor((drawCount&1)==0?0xff000000:0xff010101);
            canvas.drawRect(0,0,2,2,paint);drawCount++;
            postInvalidateOnAnimation();
        }
    }
}
