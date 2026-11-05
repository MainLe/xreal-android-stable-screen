package dev.xreal.stablescreen;
import android.content.*;
public final class ControlReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context,Intent intent){ScreenBridge.init(context);String action=intent.getAction();if("stable.stop".equals(action))ScreenBridge.stopCapture();else if("stable.center".equals(action))ScreenBridge.command("recenter");}
}
