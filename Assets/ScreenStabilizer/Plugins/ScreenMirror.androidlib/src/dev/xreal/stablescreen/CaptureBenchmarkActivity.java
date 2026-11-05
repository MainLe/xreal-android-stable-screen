package dev.xreal.stablescreen;

import android.app.Activity;
import android.os.Bundle;
import android.view.*;
import android.graphics.*;
import android.widget.*;

/** Moving, non-private phone content for measuring real projection throughput. */
public final class CaptureBenchmarkActivity extends Activity implements Choreographer.FrameCallback {
    private boolean active;
    private View pattern;
    private long frames,lastAt;
    private float fps;
    @Override protected void onCreate(Bundle saved){
        super.onCreate(saved);getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        WindowManager.LayoutParams attributes=getWindow().getAttributes();attributes.preferredRefreshRate=ScreenBridge.targetCaptureFps();getWindow().setAttributes(attributes);
        FrameLayout root=new FrameLayout(this);
        pattern=new View(this){final Paint paint=new Paint(3);@Override protected void onDraw(Canvas canvas){
            canvas.drawColor(0xff071923);float phase=(System.nanoTime()/1000000L%4000)/4000f;
            int w=getWidth(),h=getHeight();paint.setColor(0xff203d50);paint.setStrokeWidth(2);
            for(int i=0;i<=12;i++){canvas.drawLine(i*w/12f,0,i*w/12f,h,paint);canvas.drawLine(0,i*h/12f,w,i*h/12f,paint);}
            paint.setColor(0xff60e5b9);canvas.drawRect(phase*(w+200)-200,h*.4f,phase*(w+200),h*.75f,paint);
            paint.setColor(Color.WHITE);paint.setTextSize(Math.max(24,w/24f));canvas.drawText("XREAL CAPTURE MOTION TEST",w*.05f,h*.23f,paint);
            canvas.drawText("Phone animation "+Math.round(fps)+" fps",w*.05f,h*.30f,paint);
            canvas.drawText("Frame "+frames,w*.05f,h*.37f,paint);
            canvas.drawText("Projection "+Math.round(ScreenBridge.uploadFps)+" fps",w*.05f,h*.84f,paint);
            canvas.drawText("Glasses "+Math.round(ScreenBridge.renderFps)+" fps",w*.05f,h*.91f,paint);
        }};
        root.addView(pattern,new FrameLayout.LayoutParams(-1,-1));Button back=new Button(this);back.setText("返回稳屏控制");back.setOnClickListener(v->finish());FrameLayout.LayoutParams lp=new FrameLayout.LayoutParams(-2,-2,Gravity.TOP|Gravity.RIGHT);lp.topMargin=130;root.addView(back,lp);setContentView(root);
    }
    @Override protected void onResume(){super.onResume();active=true;lastAt=System.nanoTime();frames=0;Choreographer.getInstance().postFrameCallback(this);}
    @Override protected void onPause(){active=false;Choreographer.getInstance().removeFrameCallback(this);super.onPause();}
    @Override public void doFrame(long now){if(!active)return;frames++;if(now-lastAt>=1000000000L){fps=frames*1000000000f/(now-lastAt);frames=0;lastAt=now;}pattern.invalidate();Choreographer.getInstance().postFrameCallback(this);}
}
