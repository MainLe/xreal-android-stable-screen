package dev.xreal.stablescreen;

import android.app.Activity;
import android.os.Bundle;
import android.util.Log;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.SeekBar;
import android.widget.TextView;

/** User-operated test surface. Records only events delivered to this activity. */
public final class PhoneInputTestActivity extends Activity {
    private TextView events,results;
    private int clicks,longClicks,moves,slider;
    private long lastMoveLog;
    @Override protected void onCreate(Bundle saved){
        super.onCreate(saved);getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        LinearLayout root=new LinearLayout(this);root.setOrientation(1);root.setPadding(24,32,24,24);root.setBackgroundColor(0xff071923);
        label(root,"手势触控测试",23);
        label(root,"快速捏合并张开：点击 +1；保持捏合：长按 +1。捏住后移动手掌，拖动滑条或滚动下面的列表。直接触屏模式则用食指触入、滑动、退出。",16);
        events=label(root,"等待手机收到触控事件",16);results=label(root,"",18);showResults();
        Button target=new Button(this);target.setText("点按 / 长按测试区");target.setMinHeight(180);root.addView(target);
        target.setOnClickListener(v->{clicks++;showResults();Log.i("StablePhoneProbe","CLICK count="+clicks);});
        target.setOnLongClickListener(v->{longClicks++;showResults();Log.i("StablePhoneProbe","LONG_CLICK count="+longClicks);return true;});
        SeekBar seek=new SeekBar(this);seek.setMax(100);root.addView(seek,new LinearLayout.LayoutParams(-1,140));
        seek.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){
            public void onStartTrackingTouch(SeekBar s){Log.i("StablePhoneProbe","SLIDER start");}
            public void onStopTrackingTouch(SeekBar s){Log.i("StablePhoneProbe","SLIDER stop value="+slider);}
            public void onProgressChanged(SeekBar s,int progress,boolean user){slider=progress;showResults();}
        });
        ScrollView scroll=new ScrollView(this);LinearLayout list=new LinearLayout(this);list.setOrientation(1);
        for(int i=1;i<=25;i++){TextView row=label(list,"可滚动测试列表 · 第 "+i+" 行",18);row.setPadding(20,32,20,32);}
        scroll.addView(list);root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));
        Button back=new Button(this);back.setText("返回稳屏控制");back.setOnClickListener(v->finish());root.addView(back);setContentView(root);
    }
    private TextView label(LinearLayout root,String text,int size){TextView v=new TextView(this);v.setText(text);v.setTextSize(size);v.setTextColor(0xffcdeee7);v.setPadding(0,10,0,10);root.addView(v);return v;}
    private void showResults(){if(results!=null)results.setText("点击 "+clicks+" · 长按 "+longClicks+" · 滑条 "+slider);}
    @Override public boolean dispatchTouchEvent(MotionEvent event){
        int action=event.getActionMasked();if(action==MotionEvent.ACTION_DOWN)moves=0;if(action==MotionEvent.ACTION_MOVE)moves++;
        String message=MotionEvent.actionToString(action)+" x="+Math.round(event.getX())+" y="+Math.round(event.getY())+" heldMs="+(event.getEventTime()-event.getDownTime())+" moves="+moves;
        if(events!=null)events.setText(message);
        long now=android.os.SystemClock.elapsedRealtime();
        if(action!=MotionEvent.ACTION_MOVE||now-lastMoveLog>=250){lastMoveLog=now;Log.i("StablePhoneProbe",message);}
        return super.dispatchTouchEvent(event);
    }
}
