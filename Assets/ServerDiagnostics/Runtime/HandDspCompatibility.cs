using System;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Opt-in DSP resource search path setup. Runtime availability remains the SDK's decision.</summary>
public static class HandDspCompatibility
{
    const string Variable="ADSP_LIBRARY_PATH";
    const string Defaults="/odm/lib/rfsa/adsp;/vendor/lib/rfsa/adsp;/system/lib/rfsa/adsp;/system/vendor/lib/rfsa/adsp;/dsp";
    static bool requested,configured,capturedBefore;
    static string originalPath="",configuredPath="",nativeDirectory="",error="",lastStage="";
    static int attempts;

#if XREAL_DSP_PATH_FIX && UNITY_ANDROID && !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void EarlyPrepare(){Prepare("subsystem-registration");}
#endif

    public static void Prepare(string stage)
    {
        requested=true;lastStage=stage;attempts++;
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using(var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using(var activity=player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                if(activity==null)throw new InvalidOperationException("Activity unavailable; setup will be retried before hands start.");
                using(var info=activity.Call<AndroidJavaObject>("getApplicationInfo"))nativeDirectory=info.Get<string>("nativeLibraryDir");
            }
            if(string.IsNullOrEmpty(nativeDirectory)||!nativeDirectory.StartsWith("/",StringComparison.Ordinal)||nativeDirectory.Contains(";"))
                throw new InvalidOperationException("Invalid application native library directory.");
            using(var os=new AndroidJavaClass("android.system.Os"))
            {
                string before=os.CallStatic<string>("getenv",Variable)??"";
                if(!capturedBefore){originalPath=before;capturedBefore=true;}
                string tail=string.IsNullOrWhiteSpace(before)?Defaults:before;
                configuredPath=string.Join(";",new[]{nativeDirectory}.Concat(tail.Split(';')).Select(p=>p.Trim()).Where(p=>p.Length>0).Distinct(StringComparer.Ordinal));
                os.CallStatic("setenv",Variable,configuredPath,true);
                configured=string.Equals(configuredPath,os.CallStatic<string>("getenv",Variable),StringComparison.Ordinal);
                error=configured?"":"Environment read-back mismatch.";
            }
            Debug.Log("[HandDspCompatibility] stage="+stage+" configured="+configured+" path="+configuredPath+"; DSP availability and inference remain unverified.");
        }
        catch(Exception e){configured=false;error=e.Message;Debug.LogWarning("[HandDspCompatibility] "+stage+": "+error);}
#else
        error="Android device required";
#endif
    }

    static bool Has(string name)=>!string.IsNullOrEmpty(nativeDirectory)&&File.Exists(Path.Combine(nativeDirectory,name));
    public static string Status=>!requested?"disabled":(configured?"path configured":"setup failed")+" / V79 Skel="+Has("libSnpeHtpV79Skel.so")+" / DSP use unverified"+(error.Length==0?"":" / "+error);

    [Serializable] public sealed class Snapshot
    {
        public bool requested,pathConfigured,pathStillEffective,snpePresent,v79StubPresent,v79SkelPresent;
        public string stage,originalSearchPath,configuredSearchPath,currentSearchPath,nativeLibraryDirectory,error;
        public int attempts;
        public string dspInference="unverified; consult native runtime logs and real hand joints";
    }
    public static Snapshot Report()
    {
        string current="",readError=error;
#if UNITY_ANDROID && !UNITY_EDITOR
        if(requested)try{using(var os=new AndroidJavaClass("android.system.Os"))current=os.CallStatic<string>("getenv",Variable)??"";}catch(Exception e){readError=e.Message;}
#endif
        return new Snapshot{requested=requested,pathConfigured=configured,pathStillEffective=configured&&current==configuredPath,snpePresent=Has("libSNPE.so"),v79StubPresent=Has("libSnpeHtpV79Stub.so"),v79SkelPresent=Has("libSnpeHtpV79Skel.so"),stage=lastStage,originalSearchPath=originalPath,configuredSearchPath=configuredPath,currentSearchPath=current,nativeLibraryDirectory=nativeDirectory,error=readError,attempts=attempts};
    }
}
