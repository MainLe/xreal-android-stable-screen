using UnityEngine;

/// <summary>An anatomical palm frame from joints: -Z is palmar, +Y points towards finger roots.</summary>
public static class PalmSurfaceFrame
{
    public static bool TryCreate(Vector3 wrist,Vector3 indexRoot,Vector3 littleRoot,Vector3 fingerRoot,Vector3 centre,bool left,out Pose frame)
    {
        frame=default;
        if(!Finite(wrist)||!Finite(indexRoot)||!Finite(littleRoot)||!Finite(fingerRoot)||!Finite(centre))return false;
        Vector3 fingers=fingerRoot-wrist,across=indexRoot-littleRoot;
        if(fingers.sqrMagnitude<.0004f||across.sqrMagnitude<.000225f)return false;
        // Air 2 Ultra device feedback showed the 0.7.1 sign selected the dorsal side.
        // Keep one corrected normal for placement, quad facing, and visibility.
        Vector3 palmar=Vector3.Cross(fingers.normalized,across.normalized)*(left?-1:1);
        if(palmar.sqrMagnitude<.04f)return false;
        // Screen quads show their -Z face, so +Z points through the back of the hand.
        frame=new Pose(centre,Quaternion.LookRotation(-palmar.normalized,fingers.normalized));return true;
    }
    static bool Finite(Vector3 point){float s=point.sqrMagnitude;return !float.IsNaN(s)&&!float.IsInfinity(s);}
}
