using UnityEngine;

/// <summary>A finger enters the plane from its front, then stays down in a finite rear volume.</summary>
public sealed class DirectScreenTouch
{
    public enum Change { None,Begin,Move,End }
    public bool Down { get; private set; }
    public bool Armed { get; private set; }
    public bool HoldingMissingSample { get; private set; }
    public Vector2 Uv { get; private set; }
    public const float RearmDistance=.005f,MissingSampleGrace=.10f,MaximumCrossingStep=.05f;
    float lastSample=float.NegativeInfinity;
    Vector3 previous;
    bool hasPrevious,pulseRelease;
    public void Reset(){Down=Armed=HoldingMissingSample=hasPrevious=pulseRelease=false;lastSample=float.NegativeInfinity;}
    public Change Sample(bool tracked,Vector3 localMetres,Vector2 size,float rearDepth,float now,float margin=0)
    {
        HoldingMissingSample=false;
        bool finite=Finite(localMetres.sqrMagnitude)&&Finite(size.sqrMagnitude)&&size.x>0&&size.y>0&&Finite(rearDepth)&&rearDepth>=0&&Finite(margin)&&margin>=0;
        if(!finite){bool ended=Down;Reset();return ended?Change.End:Change.None;}
        // A swept crossing entirely through a zero/thin layer is a single brief
        // contact, not a held finger outside the configured volume.
        if(pulseRelease){Reset();if(tracked&&finite){previous=localMetres;hasPrevious=true;lastSample=now;Armed=localMetres.z< -RearmDistance;}return Change.End;}
        if(!tracked)
        {
            if(Down&&now-lastSample<=MissingSampleGrace){HoldingMissingSample=true;return Change.Move;}
            bool ended=Down;Reset();return ended?Change.End:Change.None;
        }
        Vector3 from=previous;bool canSweep=hasPrevious&&now>lastSample&&now-lastSample<=MissingSampleGrace&&Vector3.Distance(from,localMetres)<=MaximumCrossingStep;
        previous=localMetres;hasPrevious=true;lastSample=now;
        Vector2 uv=new Vector2(localMetres.x/size.x+.5f,localMetres.y/size.y+.5f);
        bool inside=uv.x>=0&&uv.x<=1&&uv.y>=0&&uv.y<=1;
        if(Down)
        {
            bool retained=InHoldVolume(localMetres,size,rearDepth,margin);
            if(retained){Uv=new Vector2(Mathf.Clamp01(uv.x),Mathf.Clamp01(uv.y));return Change.Move;}
            Down=false;Armed=localMetres.z< -RearmDistance;return Change.End;
        }
        if(localMetres.z< -RearmDistance)Armed=true;
        if(Armed&&inside&&localMetres.z>=0&&localMetres.z<=rearDepth)
        {Down=true;Armed=false;Uv=uv;return Change.Begin;}
        if(Armed&&canSweep&&from.z<0&&localMetres.z>=0)
        {
            Vector3 crossing=Vector3.Lerp(from,localMetres,-from.z/(localMetres.z-from.z));
            Vector2 atPlane=new Vector2(crossing.x/size.x+.5f,crossing.y/size.y+.5f);
            if(atPlane.x>=0&&atPlane.x<=1&&atPlane.y>=0&&atPlane.y<=1)
            {Down=true;Armed=false;Uv=atPlane;pulseRelease=!InHoldVolume(localMetres,size,rearDepth,margin);return Change.Begin;}
        }
        // Appearing behind the volume must not arm a future accidental touch from its rear.
        if(localMetres.z>rearDepth+margin)Armed=false;
        return Change.None;
    }
    static bool InHoldVolume(Vector3 p,Vector2 size,float depth,float margin)=>Mathf.Abs(p.x)<=size.x*.5f+margin&&Mathf.Abs(p.y)<=size.y*.5f+margin&&p.z>=-margin&&p.z<=depth+margin;
    static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
}
