package dev.xreal.demo;

import android.app.Activity;
import android.content.*;
import android.graphics.Color;
import android.view.*;
import android.widget.*;
import com.unity3d.player.UnityPlayer;

/** Native phone controls remain usable while Unity renders to the glasses. */
public final class FeatureDebugPanel {
    private static LinearLayout root,body;
    private static TextView status;
    private static String page="tracking",lastStatus="Waiting for XR session";
    private static Activity activity;
    private static boolean comparisonMode;
    public static void configureScreenshotComparison(boolean enabled){comparisonMode=enabled;}
    private static void send(String command){UnityPlayer.UnitySendMessage("FeatureDemo","Command",command);}
    public static String initialPage(){try{return UnityPlayer.currentActivity.getIntent().getStringExtra("xreal.debug.page");}catch(Exception e){return "tracking";}}
    public static void show(){Activity current=UnityPlayer.currentActivity;current.runOnUiThread(()->{
        activity=current;hideView();root=new LinearLayout(activity);root.setOrientation(1);root.setPadding(16,16,16,12);root.setBackgroundColor(Color.rgb(16,22,32));
        TextView title=new TextView(activity);title.setText("XREAL 空间功能调试 · 运行诊断");title.setTextSize(22);title.setTextColor(Color.WHITE);root.addView(title);
        status=new TextView(activity);status.setTextColor(Color.rgb(100,235,190));status.setTextSize(13);status.setText(lastStatus);root.addView(status);
        LinearLayout tabs=new LinearLayout(activity);root.addView(tabs);
        String[] names={"追踪","锚点","手势","环境","报告"},ids={"tracking","anchors","hands","environment","report"};
        for(int i=0;i<names.length;i++){final String id=ids[i];Button b=new Button(activity);b.setText(names[i]);b.setTextSize(12);tabs.addView(b,new LinearLayout.LayoutParams(0,-2,1));b.setOnClickListener(v->{page=id;render();send("tab:"+id);});}
        ScrollView scroll=new ScrollView(activity);body=new LinearLayout(activity);body.setOrientation(1);scroll.addView(body);root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));
        activity.addContentView(root,new ViewGroup.LayoutParams(-1,-1));String initial=initialPage();if(comparisonMode)page="tracking";else if(initial!=null)page=initial;render();
    });}
    private static void text(String s){TextView t=new TextView(activity);t.setText(s);t.setTextColor(Color.WHITE);t.setTextSize(15);t.setPadding(4,12,4,12);body.addView(t);}
    private static void button(String label,String command){Button b=new Button(activity);b.setText(label);body.addView(b);b.setOnClickListener(v->send(command));}
    private static void render(){body.removeAllViews();
        if(page.equals("anchors")){
            text("请先在明亮、有纹理的环境确认位置追踪有效。创建锚点后缓慢从多个角度观察，再查询质量并保存。地图保存在本 Demo 私有目录，覆盖安装会保留。");
            button("在视线前方 1 米创建锚点","anchor-create");button("选择下一个活动锚点","anchor-next");
            button("查询所选锚点质量","anchor-quality");button("保存所选锚点地图","anchor-save");
            button("刷新已保存地图 / 选择下一个","saved-next");button("加载所选地图（等待真实重定位）","anchor-load");
            button("重新映射所选活动锚点","anchor-remap");button("删除所选保存地图","anchor-erase");button("移除所有活动锚点（保留地图）","anchor-clear");
        }else if(page.equals("hands")){
            text("开启后把双手放在眼镜前方。靠近方块捏合可抓取，移动手可平移，转腕可旋转；另一手靠近并捏合可双手旋转。挥动后松开可甩出，张掌靠近并向方块推可推动。无重力，惯性有阻尼，会逐渐停止。跟踪丢失不会触发甩出。");
            button("启用手部跟踪","hands-on");button("关闭手部跟踪","hands-off");button("捏合创建锚点：开 / 关","pinch-anchor");button("把交互方块放回面前","reset-marker");
            button("停止立方体惯性","cube-stop");
            text("运行诊断版可切换手部读取方式作性能对照。默认保持 SDK 两阶段读取；实验模式复用本帧数据，减少渲染前同步读取，不改变原生模型推理帧率。");
            button("实验：每帧读取一次 / SDK 默认两阶段","hand-poll-reuse");
        }else if(page.equals("environment")){
            text("逐项开启以便定位性能问题。平面、网格和图像需要 6DoF；RGB 由硬件能力判断，不支持时会明确报告。图像测试请打印 Build/Android/XREAL-ImageTarget.png，宽度 20 cm。");
            button("平面检测：开 / 关","planes");button("空间网格：开 / 关","mesh");button("图像跟踪：开 / 关","images");button("RGB CPU 帧诊断：开 / 关","camera");button("关闭所有环境模块","environment-off");
        }else if(page.equals("report")){
            text("报告包含本 Demo 的设备与 SDK 能力、实际追踪、关节、锚点、环境模块计数和错误日志。保存到本应用 files 目录，同时复制 JSON，便于反馈。");
            button("保存并复制诊断报告","report");button("重新查询原生能力","capabilities");button("返回 Server（先退出当前 XR 会话）","exit");
            text("长时间运行会保留有容量上限的性能记录，包含帧耗时、内存、GC 和温控状态，不记录画面。SDK 不支持的计时值标为 -1。");
        }else{
            if(comparisonMode){
                text("静态截图对照：立方体和中文手机截图固定在同一空间坐标，距离相同。立方体默认不自转；截图不是实时投屏。先保持手势、环境模块关闭，分别观察立方体边缘和文字。");
                button("1 秒后把截图和立方体放到面前","compare-center");button("立方体自转：开 / 关","compare-motion");button("手机截图显示：开 / 关","compare-image");
            }
            text("默认启动 6DoF。眼镜里的静止标记用于检查平移视差；旋转方块和递增计数用于区分画面静止与渲染卡死。请先移动头部确认 Position tracking 为 True，再测试空间功能。");
            button("切换到 6DoF","6dof");button("切换到 3DoF 对照（先清除活动锚点）","3dof");
            button("把测试标记放回面前","reset-marker");button("保存并复制诊断报告","report");button("退出 Demo","exit");
            text("排查长时间抖动：静态基线会关闭手部与环境模块、隐藏 HUD、停止方块动画，保留锚点和地图。");
            button("静态追踪基线（降低额外负载）","runtime-baseline");button("恢复 HUD 与方块动画","runtime-hud");
        }
    }
    public static void updateStatus(String value){lastStatus=value;if(activity!=null)activity.runOnUiThread(()->{if(status!=null)status.setText(lastStatus);});}
    public static void copyReport(String json){if(activity!=null)activity.runOnUiThread(()->{((ClipboardManager)activity.getSystemService(Context.CLIPBOARD_SERVICE)).setPrimaryClip(ClipData.newPlainText("XREAL diagnostic",json));Toast.makeText(activity,"诊断 JSON 已复制",Toast.LENGTH_LONG).show();});}
    private static void hideView(){if(root!=null&&root.getParent() instanceof ViewGroup)((ViewGroup)root.getParent()).removeView(root);root=null;body=null;status=null;}
    public static void hide(){if(activity!=null)activity.runOnUiThread(()->{hideView();activity=null;});}
}
