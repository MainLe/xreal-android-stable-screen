using System;
using System.Runtime.InteropServices;

/// <summary>Opt-in compatibility for the audited XREAL 3.1 flora CPU graph.</summary>
public static class HandGraphCompatibility
{
    static string error;
    static bool enabled;
#if UNITY_ANDROID && !UNITY_EDITOR
    [DllImport("XrealHandCompat")] static extern int XrealHandCompatStart();
    [DllImport("XrealHandCompat")] static extern int XrealHandCompatState();
    [DllImport("XrealHandCompat")] static extern int XrealHandCompatImports();
    [DllImport("XrealHandCompat")] static extern int XrealHandCompatRewrites();
#endif
    public static void Start()
    {
        enabled=true;
#if UNITY_ANDROID && !UNITY_EDITOR
        try{XrealHandCompatStart();}catch(Exception e){error=e.Message;}
#endif
    }
    public static string Status
    {
        get
        {
            if(!enabled)return "disabled";
            if(error!=null)return "load failure: "+error;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                int state=XrealHandCompatState();
                string label=state==0?"waiting for module":state==1?"installed":state==2?"unsupported binary":state==3?"data-slot permission failure":state==4?"self-test failed":"not started";
                return label+" / imports="+XrealHandCompatImports()+" / graph copies repaired="+XrealHandCompatRewrites();
            }
            catch(Exception e){return "status failure: "+e.Message;}
#else
            return "Android device required";
#endif
        }
    }
}
