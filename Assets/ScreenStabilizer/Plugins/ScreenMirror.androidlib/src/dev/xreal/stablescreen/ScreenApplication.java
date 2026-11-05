package dev.xreal.stablescreen;

import android.app.*;
import android.os.*;
import android.os.Process;
import android.util.Log;

/** Observes our own activities before SDK initialization; does not replace vendor lifecycle handling. */
public final class ScreenApplication extends Application {
    @Override public void onCreate(){
        super.onCreate();ScreenBridge.init(this);
        ScreenBridge.event("process-created pid="+Process.myPid());
        Thread.UncaughtExceptionHandler previous=Thread.getDefaultUncaughtExceptionHandler();
        Thread.setDefaultUncaughtExceptionHandler((thread,error)->{
            ScreenBridge.recordFailure("Java "+thread.getName(),Log.getStackTraceString(error));
            if(previous!=null)previous.uncaughtException(thread,error);
            else{Process.killProcess(Process.myPid());System.exit(1);}
        });
        registerActivityLifecycleCallbacks(new ActivityLifecycleCallbacks(){
            private void record(Activity a,String phase){
                String name=a.getClass().getName();
                int display=-1;try{if(Build.VERSION.SDK_INT>=30&&a.getDisplay()!=null)display=a.getDisplay().getDisplayId();}catch(RuntimeException ignored){}
                ScreenBridge.activityEvent(name,phase,display,a.getTaskId());
            }
            public void onActivityCreated(Activity a,Bundle b){record(a,"created");}
            public void onActivityStarted(Activity a){record(a,"started");}
            public void onActivityResumed(Activity a){record(a,"resumed");}
            public void onActivityPaused(Activity a){record(a,"paused");}
            public void onActivityStopped(Activity a){record(a,"stopped");}
            public void onActivityDestroyed(Activity a){record(a,"destroyed");}
            public void onActivitySaveInstanceState(Activity a,Bundle b){}
        });
    }
}
