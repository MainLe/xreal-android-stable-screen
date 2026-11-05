package dev.xreal.stablescreen;

import android.app.Activity;
import android.content.*;
import android.media.projection.*;
import android.os.*;
import android.widget.*;

/** Consent stays on display 0. No projection token is persisted or reused. */
public final class ProjectionConsentActivity extends Activity {
    private static final int CAPTURE=91;
    @Override public void onCreate(Bundle state){
        super.onCreate(state);ScreenBridge.init(this);
        TextView label=new TextView(this);label.setText("请在系统提示中允许共享整个屏幕，画面将仅投射到连接的眼镜。");label.setPadding(32,60,32,32);setContentView(label);
        if(state==null){
            try{
                MediaProjectionManager manager=(MediaProjectionManager)getSystemService(MEDIA_PROJECTION_SERVICE);
                Intent consent=Build.VERSION.SDK_INT>=34?manager.createScreenCaptureIntent(MediaProjectionConfig.createConfigForDefaultDisplay()):manager.createScreenCaptureIntent();
                startActivityForResult(consent,CAPTURE);
            }catch(Exception e){ScreenBridge.captureState="无法申请录屏授权："+e.getMessage();finish();}
        }
    }
    @Override protected void onActivityResult(int request,int result,Intent data){
        super.onActivityResult(request,result,data);if(request!=CAPTURE)return;
        if(result==RESULT_OK&&data!=null){
            Intent service=new Intent(this,ProjectionService.class).putExtra("result",result).putExtra("grant",data);
            try{startForegroundService(service);}catch(Exception e){ScreenBridge.captureState="无法开启投屏服务："+e.getMessage();}
        }else ScreenBridge.captureState="投屏未授权，可再次点击开启";
        finish();
    }
}
