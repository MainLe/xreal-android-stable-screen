package dev.xreal.stablescreen;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.*;
import android.provider.Settings;
import android.view.*;
import android.widget.*;

/** Phone-side controls remain independent from the XR Activity on the glasses display. */
public final class ControlActivity extends Activity {
    private static final int BG=0xff0e1820,TEXT=0xffe7f0f4,MUTED=0xffa4b8c4,ACCENT=0xff60e5b9;
    private final Handler main=new Handler(Looper.getMainLooper());
    private TextView status,scaleLabel,handState,handScreenState,planeState;
    private Button startXR,startCapture,stop;
    private Spinner modeChoice;
    private final java.util.ArrayList<View> distanceControls=new java.util.ArrayList<>();
    private Switch fixedScreen;
    private Button planeConfirm,planeClear,recenter;
    private Switch leftBind;
    private final Runnable refresh=new Runnable(){@Override public void run(){updateStatus();main.postDelayed(this,1000);}};
    private int dp(float x){return Math.round(x*getResources().getDisplayMetrics().density);}
    @Override public void onCreate(Bundle state){super.onCreate(state);ScreenBridge.init(this);getWindow().setStatusBarColor(BG);getWindow().setNavigationBarColor(BG);render();}
    private GradientDrawable background(int color){GradientDrawable d=new GradientDrawable();d.setColor(color);d.setCornerRadius(dp(16));return d;}
    private TextView text(LinearLayout parent,String value,int size,int color){TextView t=new TextView(this);t.setText(value);t.setTextSize(size);t.setTextColor(color);t.setPadding(0,dp(5),0,dp(7));parent.addView(t);return t;}
    private LinearLayout card(LinearLayout parent){LinearLayout box=new LinearLayout(this);box.setOrientation(1);box.setPadding(dp(18),dp(14),dp(18),dp(16));box.setBackground(background(0xff192a35));LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-1,-2);lp.setMargins(0,dp(14),0,0);parent.addView(box,lp);return box;}
    private Button button(LinearLayout box,String name,Runnable action,boolean primary){Button b=new Button(this);b.setText(name);b.setAllCaps(false);b.setTextSize(16);b.setTextColor(primary?BG:TEXT);b.setBackground(background(primary?ACCENT:0xff2a4050));LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-1,dp(50));lp.setMargins(0,dp(8),0,0);box.addView(b,lp);b.setOnClickListener(v->{try{action.run();}catch(Exception e){Toast.makeText(this,e.getMessage(),Toast.LENGTH_LONG).show();}});return b;}
    private Spinner choice(LinearLayout box,String[] labels,int selected,java.util.function.IntConsumer changed){
        Spinner spinner=new Spinner(this);ArrayAdapter<String> adapter=new ArrayAdapter<String>(this,android.R.layout.simple_spinner_item,labels){
            @Override public View getView(int p,View v,ViewGroup group){TextView t=(TextView)super.getView(p,v,group);t.setTextColor(TEXT);t.setTextSize(16);return t;}
            @Override public View getDropDownView(int p,View v,ViewGroup group){TextView t=(TextView)super.getDropDownView(p,v,group);t.setTextColor(TEXT);t.setBackgroundColor(0xff192a35);t.setPadding(dp(16),dp(16),dp(16),dp(16));return t;}
        };adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);spinner.setAdapter(adapter);spinner.setSelection(selected);spinner.setTag(selected);box.addView(spinner,new LinearLayout.LayoutParams(-1,dp(56)));
        spinner.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> parent){}public void onItemSelected(AdapterView<?> parent,View view,int position,long id){if(((Integer)spinner.getTag())!=position){spinner.setTag(position);changed.accept(position);}}});return spinner;
    }
    private void render(){
        ScrollView scroll=new ScrollView(this);scroll.setFillViewport(true);scroll.setBackgroundColor(BG);
        scroll.setOnApplyWindowInsetsListener((view,insets)->{if(Build.VERSION.SDK_INT>=30){android.graphics.Insets bars=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());view.setPadding(bars.left,bars.top,bars.right,bars.bottom);}else view.setPadding(insets.getSystemWindowInsetLeft(),insets.getSystemWindowInsetTop(),insets.getSystemWindowInsetRight(),insets.getSystemWindowInsetBottom());return insets;});
        LinearLayout body=new LinearLayout(this);body.setOrientation(1);body.setPadding(dp(22),dp(24),dp(22),dp(30));scroll.addView(body);setContentView(scroll);
        text(body,"XREAL  稳屏",30,TEXT).setTypeface(null,Typeface.BOLD);
        text(body,"把手机原来的画面，放在眼前稳稳看。",16,MUTED);
        LinearLayout state=card(body);text(state,"设备与投屏",18,ACCENT);status=text(state,"正在检查…",14,TEXT);
        LinearLayout connection=card(body);text(connection,"1  准备眼镜",19,TEXT);
        text(connection,"先在 XREAL Server 启动完整服务，并切到立体 3840×1080。然后启动眼镜画面。",14,MUTED);
        button(connection,"打开 XREAL Server",()->{Intent i=getPackageManager().getLaunchIntentForPackage("dev.xreal.server");if(i==null)throw new IllegalStateException("请先安装 XREAL Server");startActivity(i);},false);
        startXR=button(connection,"启动眼镜画面",()->{
            if(ScreenBridge.unityReady){ScreenBridge.command("recenter");return;}
            android.view.Display d=ScreenBridge.glasses();if(d==null)throw new IllegalStateException("请先连接眼镜并启动 Server");
            if(d.getMode().getPhysicalWidth()!=3840||d.getMode().getPhysicalHeight()!=1080)throw new IllegalStateException("请先在 Server 切换到立体 3840×1080");
            ScreenBridge.launchXR(this);
        },false);
        LinearLayout capture=card(body);text(capture,"2  投射整个手机屏幕",19,TEXT);
        text(capture,"授权后，桌面和切换到的普通 APP 会一起显示在眼镜中。继续在手机上触控即可。",14,MUTED);
        choice(capture,new String[]{"节能 · 960 / 30 fps","流畅 · 1200 / 60 fps","清晰 · 1600 / 60 fps"},ScreenBridge.captureProfile(),ScreenBridge::setCaptureProfile);
        text(capture,"数字为最长边像素和目标帧率。实际取决于原 APP、手机及眼镜刷新率；静止画面会少更新。",12,MUTED);
        startCapture=button(capture,"允许整屏投射",this::requestCapture,true);
        button(capture,"返回手机桌面",()->ScreenBridge.returnHome(this),false);
        stop=button(capture,"停止投屏",ScreenBridge::stopCapture,false);
        button(capture,"重新同步画面",()->{if(ScreenBridge.capturing)ScreenBridge.resyncCapture();else Toast.makeText(this,"请先开启整屏投射",Toast.LENGTH_SHORT).show();},false);
        button(capture,"测试投屏流畅度",()->{if(!ScreenBridge.capturing)throw new IllegalStateException("请先开启整屏投射");startActivity(new Intent(this,CaptureBenchmarkActivity.class));},false);
        button(capture,"静态标定网格：开 / 关（暂停投屏）",()->{if(!ScreenBridge.unityReady)throw new IllegalStateException("请先启动眼镜画面");ScreenBridge.command("reference");},false);
        text(capture,"标定网格由眼镜本地绘制，不采集手机画面。可用来区分定位抖动与投屏负载；返回真实投屏需重新授权。",12,MUTED);
        Switch refresh=new Switch(this);refresh.setText("连续刷新眼镜（兼容）");refresh.setTextColor(TEXT);refresh.setPadding(0,dp(12),0,dp(8));refresh.setChecked(ScreenBridge.continuousRefreshEnabled());capture.addView(refresh);refresh.setOnCheckedChangeListener((v,on)->ScreenBridge.setContinuousRefresh(on));
        text(capture,"手机界面静止时也保持眼镜刷新，会增加耗电；关闭可作对照。",12,MUTED);
        LinearLayout hands=card(body);text(hands,"手势操作手机",19,TEXT);
        text(hands,"直接触屏：把屏幕调到伸手可及，食指从屏幕前方触入，直接滑动或保持长按，退出接触区域即松开，不需要捏合。捏合模式使用食指垂直投影瞄准，保留点按、拖动和下方 BACK / HOME / APPS 导航。",14,MUTED);
        text(hands,"需要手动开启下方无障碍服务，才能把你的手势发送给其他 APP。服务只接收你的触控和导航命令，不读取界面节点、不输入文字或上传数据。",13,MUTED);
        button(hands,"打开系统无障碍设置",()->{Intent intent=new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS);startActivity(intent);},false);
        button(hands,"启用手势操控（本次会话）",()->{if(!ScreenBridge.unityReady)throw new IllegalStateException("请先启动眼镜画面");if(!PhoneGestureService.connected())throw new IllegalStateException("请先在系统无障碍设置中开启 XREAL 稳屏手势操作");ScreenBridge.setHandControl(true);},false);
        button(hands,"停止手势操控",()->ScreenBridge.setHandControl(false),false);
        choice(hands,new String[]{"左手操控","右手操控"},ScreenBridge.handControlSide(),ScreenBridge::setHandSide);
        choice(hands,new String[]{"食指垂直投影 + 捏合","直接触屏 + 食指滑动"},ScreenBridge.handTouchMode(),ScreenBridge::setHandTouchMode);
        TextView depthLabel=text(hands,"屏幕后方触摸厚度",14,MUTED);SeekBar depth=new SeekBar(this);depth.setMax(200);depth.setProgress(Math.round(ScreenBridge.directTouchDepth()*1000));hands.addView(depth);
        depth.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){public void onStartTrackingTouch(SeekBar b){}public void onStopTrackingTouch(SeekBar b){}public void onProgressChanged(SeekBar b,int value,boolean user){depthLabel.setText("屏幕后方触摸厚度  "+value+" 毫米");if(user)ScreenBridge.setDirectTouchDepth(value/1000f);}});depthLabel.setText("屏幕后方触摸厚度  "+Math.round(ScreenBridge.directTouchDepth()*1000)+" 毫米");
        TextView marginLabel=text(hands,"触摸保持容差",14,MUTED);SeekBar margin=new SeekBar(this);margin.setMax(30);margin.setProgress(Math.round(ScreenBridge.directTouchMargin()*1000));hands.addView(margin);
        margin.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){public void onStartTrackingTouch(SeekBar b){}public void onStopTrackingTouch(SeekBar b){}public void onProgressChanged(SeekBar b,int value,boolean user){marginLabel.setText("触摸保持容差（前后及边缘）  "+value+" 毫米");if(user)ScreenBridge.setDirectTouchMargin(value/1000f);}});marginLabel.setText("触摸保持容差（前后及边缘）  "+Math.round(ScreenBridge.directTouchMargin()*1000)+" 毫米");
        button(hands,"精确触屏：厚度 0 / 容差 0 毫米",()->{depth.setProgress(0);margin.setProgress(0);ScreenBridge.setDirectTouchDepth(0);ScreenBridge.setDirectTouchMargin(0);},false);
        button(hands,"连续滑动：厚度 10 / 容差 2 毫米",()->{depth.setProgress(10);margin.setProgress(2);ScreenBridge.setDirectTouchDepth(.01f);ScreenBridge.setDirectTouchMargin(.002f);},false);
        text(hands,"厚度从 0～200 毫米、容差从 0～30 毫米调节。0 毫米表示屏幕平面，穿过平面也能点按；连续滑动可增加少量厚度。容差为 0 时不额外扩大前后和边缘区域。每次新触碰先退回屏幕前方约 5 毫米，调参数会松开触控。旧的厚度设置保留，可用“精确触屏”一键归零。",12,MUTED);
        handState=text(hands,"等待手势",13,ACCENT);
        text(hands,"指示点是食指到屏幕的垂直投影。灰色底点大小固定，上层彩点越靠近平面越小；绿色表示按住，黄色表示短暂漏检保持原触点。捏合模式移动食指拖动、张开松手。",13,MUTED);
        Switch skeleton=new Switch(this);skeleton.setText("显示透明手部骨骼");skeleton.setTextColor(TEXT);skeleton.setChecked(ScreenBridge.handSkeletonVisible());hands.addView(skeleton);skeleton.setOnCheckedChangeListener((v,on)->ScreenBridge.setHandSkeletonVisible(on));
        button(hands,"打开手势触控测试页",()->startActivity(new Intent(this,PhoneInputTestActivity.class)),false);
        text(hands,"首次启动手部算法可能短暂停顿。只在已授权整屏投射、定位有效且选定手可见时操作手机；每次启动应用默认关闭操控。",12,MUTED);
        LinearLayout handPanel=card(body);text(handPanel,"左手持屏",19,TEXT);
        Switch bind=new Switch(this);bind.setText("将屏幕绑定到左手（本次会话）");bind.setTextColor(TEXT);bind.setChecked(ScreenBridge.leftHandScreenRequested());handPanel.addView(bind);
        bind.setOnCheckedChangeListener((v,on)->{if(on&&!ScreenBridge.unityReady){Toast.makeText(this,"请先启动眼镜画面",Toast.LENGTH_SHORT).show();v.setChecked(false);return;}ScreenBridge.setLeftHandScreen(on);});
        text(handPanel,"屏幕平面平行左手掌面，在掌心前方约 3 厘米，顶部默认朝大拇指侧，随手掌移动和转动。只有掌心朝向眼睛时显示，翻到掌背或侧面时隐藏并结束触控。",14,MUTED);
        TextView headingLabel=text(handPanel,"顶部朝向",14,MUTED);SeekBar heading=new SeekBar(this);heading.setMax(18);heading.setProgress(Math.round(ScreenBridge.handScreenHeadingDegrees()/5));handPanel.addView(heading);
        heading.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){public void onStartTrackingTouch(SeekBar b){}public void onStopTrackingTouch(SeekBar b){}public void onProgressChanged(SeekBar b,int value,boolean user){headingLabel.setText("向大拇指侧偏转  "+value*5+"°");if(user)ScreenBridge.setHandScreenHeadingDegrees(value*5f);}});headingLabel.setText("向大拇指侧偏转  "+Math.round(ScreenBridge.handScreenHeadingDegrees())+"°");
        text(handPanel,"0° 顶部朝四指，90° 顶部朝大拇指侧；可微调为舒服的握持角度。",12,MUTED);
        button(handPanel,"1 秒后重新适配左手屏幕大小",()->{if(!ScreenBridge.leftHandScreenRequested())throw new IllegalStateException("请先开启左手绑定");ScreenBridge.command("recenter");Toast.makeText(this,"请将左手掌心朝向眼睛",Toast.LENGTH_SHORT).show();},false);
        text(handPanel,"建议用右手操作。左手丢失时隐藏屏幕并暂停触控，恢复后按当前掌面显示。绑定会开启手部跟踪；手机操控仍需单独启用。屏幕大小可调，关闭后恢复原观看模式与距离。",13,MUTED);
        handScreenState=text(handPanel,"左手绑定已关闭",13,ACCENT);
        leftBind=bind;
        LinearLayout planePanel=card(body);text(planePanel,"将屏幕固定到平面",19,TEXT);
        text(planePanel,"扫描墙面或桌面，选择平面上的一个位置，再固定手机屏幕。进入扫描会切换到 6DoF，并暂停手机触控。青色为已识别边界，黄色轮廓为屏幕预览。",14,MUTED);
        choice(planePanel,new String[]{"视线中心瞄准","所选手食指射线"},ScreenBridge.planeAimMode(),ScreenBridge::setPlaneAimMode);
        button(planePanel,"扫描 / 重新选择平面",ScreenBridge::startPlanePin,false);
        planeConfirm=button(planePanel,"2 秒后固定瞄准的平面",ScreenBridge::confirmPlanePin,true);
        planeClear=button(planePanel,"取消选择 / 解除平面固定",ScreenBridge::clearPlanePin,false);
        text(planePanel,"先张开手，再捏合可立即固定；也可按手机按钮后抬头瞄准，2 秒后确认。选择平面不需要无障碍权限。固定成功后仍需开启整屏投射，手势操作手机需单独启用。固定在本次运行中保持，重新启动后请重选；与左手持屏互相切换。",12,MUTED);
        planeState=text(planePanel,"未固定到平面",13,ACCENT);
        LinearLayout controls=card(body);text(controls,"3  调整观看",19,TEXT);
        modeChoice=choice(controls,new String[]{"3DoF · 固定方向","6DoF · 固定空间位置"},ScreenBridge.trackingMode()==6?1:0,p->ScreenBridge.setTrackingMode(p==1?6:3));
        text(controls,"6DoF 使用眼镜真实位置，移动时屏幕留在原处；定位丢失会显示等待状态。切换模式后自动重新放置屏幕。",12,MUTED);
        recenter=button(controls,"1 秒后把屏幕放到视线中央",()->{ScreenBridge.command("recenter");Toast.makeText(this,"请看向希望固定屏幕的位置",Toast.LENGTH_SHORT).show();},false);
        scaleLabel=text(controls,"屏幕大小",14,MUTED);SeekBar scale=new SeekBar(this);scale.setMax(120);scale.setProgress(Math.round((ScreenBridge.screenScale()-.6f)*100));controls.addView(scale);
        scale.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){public void onStartTrackingTouch(SeekBar b){}public void onStopTrackingTouch(SeekBar b){}public void onProgressChanged(SeekBar b,int value,boolean user){float factor=.6f+value/100f;scaleLabel.setText("屏幕大小  "+Math.round(factor*100)+"%");if(user)ScreenBridge.setScale(factor);}});scaleLabel.setText("屏幕大小  "+Math.round(ScreenBridge.screenScale()*100)+"%");
        TextView distanceLabel=text(controls,"屏幕距离",14,MUTED);SeekBar distance=new SeekBar(this);distance.setMax(95);distance.setProgress(Math.round((ScreenBridge.screenDistance()-.25f)/.05f));controls.addView(distance);
        distance.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){public void onStartTrackingTouch(SeekBar b){}public void onStopTrackingTouch(SeekBar b){}public void onProgressChanged(SeekBar b,int value,boolean user){float metres=.25f+value*.05f;distanceLabel.setText(String.format(java.util.Locale.ROOT,"屏幕距离  %.2f 米",metres));if(user)ScreenBridge.setScreenDistance(metres);}});distanceLabel.setText(String.format(java.util.Locale.ROOT,"屏幕距离  %.2f 米",ScreenBridge.screenDistance()));distanceControls.add(distance);
        distanceControls.add(button(controls,"距离预设：0.45 米（近距离触屏）",()->{distance.setProgress(4);ScreenBridge.setScreenDistance(.45f);},false));
        distanceControls.add(button(controls,"距离预设：0.60 米",()->{distance.setProgress(7);ScreenBridge.setScreenDistance(.60f);},false));
        distanceControls.add(button(controls,"距离预设：2.00 米",()->{distance.setProgress(35);ScreenBridge.setScreenDistance(2f);},false));
        text(controls,"距离范围 0.25～5 米。调距离时保持观看角大小；屏幕大小仍可独立调整。调节或重新居中会结束当前手势，请退回屏幕前再触碰。",12,MUTED);
        fixedScreen=new Switch(this);fixedScreen.setText("空间悬停（关闭后跟随视线）");fixedScreen.setTextColor(TEXT);fixedScreen.setPadding(0,dp(12),0,dp(10));fixedScreen.setChecked(!ScreenBridge.headLocked());controls.addView(fixedScreen);fixedScreen.setOnCheckedChangeListener((v,on)->ScreenBridge.setHeadLocked(!on));
        text(controls,"开启时，转头不会让屏幕跟着晃动；关闭后随视线移动，用于对照。手机横竖屏变化会自动适配。",13,MUTED);
        text(controls,"细小抖动过滤（实验）",16,TEXT);choice(controls,new String[]{"原生固定锚点 · 推荐默认","实验：轻微过滤","实验：均衡过滤","实验：较强过滤"},ScreenBridge.smoothing(),ScreenBridge::setSmoothing);
        text(controls,"默认使用与空间 Demo 相同的原始姿态和固定锚点。实验滤波会移动内容作补偿，可能带来拖尾或与原生重投影时序不一致。",12,MUTED);
        Switch flip=new Switch(this);flip.setText("画面上下校正");flip.setTextColor(TEXT);flip.setPadding(0,dp(12),0,dp(8));flip.setChecked(ScreenBridge.flipVertical());controls.addView(flip);flip.setOnCheckedChangeListener((v,on)->ScreenBridge.setFlip(on));
        LinearLayout extra=card(body);text(extra,"使用提示",18,TEXT);text(extra,"系统禁止截屏或 DRM 保护的内容可能为黑屏。锁屏后系统会停止共享，解锁后需重新授权。当前只投射画面，声音仍走手机原有音频输出。",13,MUTED);
        button(extra,"复制诊断信息",()->{
            Toast.makeText(this,"正在汇总本应用的启动记录…",Toast.LENGTH_SHORT).show();
            new Thread(()->{String report=ScreenBridge.diagnosticWithLogs();runOnUiThread(()->{((ClipboardManager)getSystemService(CLIPBOARD_SERVICE)).setPrimaryClip(ClipData.newPlainText("XREAL 稳屏诊断",report));Toast.makeText(this,"诊断信息已复制",Toast.LENGTH_SHORT).show();});},"StableScreenReport").start();
        },false);
        button(extra,"退出稳屏",()->{ScreenBridge.stopCapture();ScreenBridge.command("exit");finish();},false);
        text(body,"本地投射 · 0.8.1",12,MUTED);
    }
    private void requestCapture(){
        if(!ScreenBridge.unityReady||!ScreenBridge.isSdkOutputReady())throw new IllegalStateException("请等待 SDK 原生眼镜输出就绪");
        if(ProjectionService.isStartingOrRunning())throw new IllegalStateException("投屏正在运行或准备中，请先停止再重新开启");
        ScreenBridge.command("reference-off");
        if(Build.VERSION.SDK_INT>=33&&checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED){requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS},22);return;}
        startActivity(new Intent(this,ProjectionConsentActivity.class));
    }
    @Override public void onRequestPermissionsResult(int request,String[] permissions,int[] results){super.onRequestPermissionsResult(request,permissions,results);if(request==22)startActivity(new Intent(this,ProjectionConsentActivity.class));}
    private void updateStatus(){
        if(status==null)return;
        if(handState!=null)handState.setText((PhoneGestureService.connected()?"无障碍已连接":"无障碍未开启")+" · "+ScreenBridge.handStatus+"\n"+PhoneGestureService.status());
        if(handScreenState!=null)handScreenState.setText(!ScreenBridge.leftHandScreenRequested()?"左手绑定已关闭":!ScreenBridge.handScreenActive||!ScreenBridge.handScreenTracked?"等待左手 · 屏幕隐藏":ScreenBridge.handScreenVisible?"屏幕平行掌心 · 显示中":"请将掌心朝向眼睛 · 屏幕已隐藏");
        if(leftBind!=null&&leftBind.isChecked()!=ScreenBridge.leftHandScreenRequested())leftBind.setChecked(ScreenBridge.leftHandScreenRequested());
        if(planeState!=null)planeState.setText(ScreenBridge.planePinStatus+"\n平面 "+ScreenBridge.planeCount+" · "+ScreenBridge.planePinStage+(ScreenBridge.planeCandidate?" · 已命中":""));
        if(planeConfirm!=null)planeConfirm.setEnabled(ScreenBridge.planeSelecting&&"selecting".equals(ScreenBridge.planePinStage));
        if(planeClear!=null)planeClear.setEnabled(ScreenBridge.planePinRequested());
        for(View distanceControl:distanceControls)distanceControl.setEnabled(!ScreenBridge.leftHandScreenRequested()&&!ScreenBridge.planePinRequested());
        if(fixedScreen!=null)fixedScreen.setEnabled(!ScreenBridge.leftHandScreenRequested()&&!ScreenBridge.planePinRequested());
        if(recenter!=null)recenter.setEnabled(!ScreenBridge.planePinRequested());
        String frame=ScreenBridge.capturing?"\n采集 "+ScreenBridge.captureWidth+"×"+ScreenBridge.captureHeight+" · 画面 "+Math.round(ScreenBridge.uploadFps)+" / 目标 "+ScreenBridge.targetCaptureFps()+" fps\n眼镜渲染 "+Math.round(ScreenBridge.renderFps)+" fps · 上传 "+String.format(java.util.Locale.ROOT,"%.1f",ScreenBridge.uploadMs)+" ms":"";
        status.setText(ScreenBridge.glassesStatus()+"\n"+ScreenBridge.captureState+"\n"+(ScreenBridge.unityReady&&!ScreenBridge.isSdkOutputReady()?"等待 SDK 原生眼镜输出":ScreenBridge.unityStatus)+frame);
        startXR.setText(ScreenBridge.unityReady?(ScreenBridge.planePinRequested()?"眼镜画面已启动 · 平面放置中":"眼镜画面已启动 · 点击重新居中"):ScreenBridge.launchInProgress?"正在启动眼镜画面…":"启动眼镜画面");startXR.setEnabled(!ScreenBridge.launchInProgress&&!(ScreenBridge.unityReady&&ScreenBridge.planePinRequested()));
        startCapture.setEnabled(ScreenBridge.unityReady&&ScreenBridge.isSdkOutputReady()&&!ProjectionService.isStartingOrRunning());stop.setEnabled(ProjectionService.isStartingOrRunning());
        modeChoice.setEnabled(!ScreenBridge.trackingSwitching&&!ScreenBridge.planePinRequested());
        if(ScreenBridge.planePinRequested()&&modeChoice.getSelectedItemPosition()!=1){modeChoice.setTag(1);modeChoice.setSelection(1);}
    }
    @Override protected void onResume(){super.onResume();main.removeCallbacks(refresh);main.post(refresh);}
    @Override protected void onPause(){main.removeCallbacks(refresh);super.onPause();}
    @Override protected void onDestroy(){main.removeCallbacks(refresh);super.onDestroy();}
}
