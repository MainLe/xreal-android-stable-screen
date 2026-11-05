package dev.xreal.common;

import android.app.Activity;
import android.content.*;
import android.hardware.display.DisplayManager;
import android.os.*;
import android.util.Log;
import android.view.Display;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.atomic.*;
import org.json.*;

/** Own-process diagnostics only. Coalesced worker, three 2-MiB files; no growing work queue. */
public final class RuntimeHealthBridge {
    private static final long FILE_LIMIT=2*1024*1024;
    private static Context context;
    private static Handler worker;
    private static final AtomicReference<String> pending=new AtomicReference<>();
    private static final AtomicBoolean scheduled=new AtomicBoolean();
    private static volatile String latest="{}";
    private static long memoryAt,pssKb=-1;
    private static int threads=-1,fds=-1;
    public static synchronized void initialize(Activity activity){
        if(context!=null||activity==null)return;context=activity.getApplicationContext();
        HandlerThread thread=new HandlerThread("XrRuntimeHealth",android.os.Process.THREAD_PRIORITY_BACKGROUND);thread.start();worker=new Handler(thread.getLooper());
    }
    public static String latest(){return latest;}
    public static void submit(String json){if(worker==null||json==null||json.length()>24000)return;pending.set(json);schedule();}
    private static void schedule(){if(scheduled.compareAndSet(false,true))worker.post(()->{
        try{String value=pending.getAndSet(null);if(value!=null)record(value);}
        finally{scheduled.set(false);if(pending.get()!=null)schedule();}
    });}
    private static void record(String json){try{
        JSONObject data=new JSONObject(json);long now=SystemClock.elapsedRealtime();
        data.put("elapsedRealtimeMs",now).put("wallTimeMs",System.currentTimeMillis()).put("pid",android.os.Process.myPid());
        PowerManager power=(PowerManager)context.getSystemService(Context.POWER_SERVICE);
        data.put("thermalStatus",Build.VERSION.SDK_INT>=29?power.getCurrentThermalStatus():-1).put("powerSaveMode",power.isPowerSaveMode()).put("nativeHeapBytes",Debug.getNativeHeapAllocatedSize());
        Intent battery=context.registerReceiver(null,new IntentFilter(Intent.ACTION_BATTERY_CHANGED));
        if(battery!=null)data.put("batteryTemperatureC",battery.getIntExtra(BatteryManager.EXTRA_TEMPERATURE,0)/10.0).put("plugged",battery.getIntExtra(BatteryManager.EXTRA_PLUGGED,0));
        if(memoryAt==0||now-memoryAt>=30000){memoryAt=now;pssKb=Debug.getPss();String[] fdNames=new File("/proc/self/fd").list(),taskNames=new File("/proc/self/task").list();fds=fdNames==null?-1:fdNames.length;threads=taskNames==null?-1:taskNames.length;}
        data.put("pssKb",pssKb).put("memorySampleAgeMs",now-memoryAt).put("threads",threads).put("fileDescriptors",fds);
        JSONArray modes=new JSONArray();for(Display display:((DisplayManager)context.getSystemService(Context.DISPLAY_SERVICE)).getDisplays()){Display.Mode mode=display.getMode();modes.put(new JSONObject().put("id",display.getDisplayId()).put("name",display.getName()).put("width",mode.getPhysicalWidth()).put("height",mode.getPhysicalHeight()).put("hz",display.getRefreshRate()));}data.put("displays",modes);
        File directory=new File(context.getExternalFilesDir(null)==null?context.getFilesDir():context.getExternalFilesDir(null),"runtime-health");
        if(!directory.isDirectory()&&!directory.mkdirs())throw new IOException("Cannot create health directory");
        data.put("historyDirectory",directory.getAbsolutePath()).put("historyMaxBytes",3*FILE_LIMIT);latest=data.toString();
        append(directory,latest+"\n");Log.i("XrRuntimeHealth",latest);
    }catch(Exception error){Log.w("XrRuntimeHealth","Diagnostic sample unavailable",error);}}
    private static void append(File directory,String line)throws IOException{
        byte[] bytes=line.getBytes(StandardCharsets.UTF_8);File current=new File(directory,"health-0.jsonl");
        if(current.length()+bytes.length>FILE_LIMIT){
            File oldest=new File(directory,"health-2.jsonl");if(oldest.exists()&&!oldest.delete())throw new IOException("History cleanup failed");
            File previous=new File(directory,"health-1.jsonl");if(previous.exists()&&!previous.renameTo(oldest))throw new IOException("History rotation failed");
            if(current.exists()&&!current.renameTo(previous))throw new IOException("Current history rotation failed");
        }
        try(FileOutputStream out=new FileOutputStream(current,true)){out.write(bytes);}
    }
}
