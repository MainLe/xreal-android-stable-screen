using UnityEngine;

/// <summary>Phone-like panel parallel to the palm, on its palmar side, visible only towards the viewer.</summary>
public sealed class LeftHandPanelAnchor
{
    public bool Bound { get; private set; }
    public bool Tracked { get; private set; }
    public bool Visible { get; private set; }
    public float FacingDot { get; private set; }
    public const float PalmOffset=.03f,ShowFacingDot=.17364818f,HideFacingDot=.08715574f;
    public float SizeDistance { get; private set; }=.5f;
    public Pose Pose { get; private set; }
    public void Reset(){Bound=Tracked=Visible=false;FacingDot=0;}
    public bool Step(bool valid,Pose palm,Pose head,float headingDegrees=0)
    {
        Tracked=valid&&Finite(palm)&&Finite(head)&&!float.IsNaN(headingDegrees)&&!float.IsInfinity(headingDegrees);
        if(!Tracked){Visible=false;FacingDot=0;return false;}
        Vector3 normal=-(palm.rotation*Vector3.forward);
        Vector3 position=palm.position+normal*PalmOffset;
        // In the validated left-palm frame, +90 degrees around local +Z turns
        // screen-up towards the index/thumb side without changing its palmar normal.
        Pose=new Pose(position,palm.rotation*Quaternion.AngleAxis(Mathf.Clamp(headingDegrees,0,90),Vector3.forward));
        Vector3 toEye=head.position-position;
        FacingDot=toEye.sqrMagnitude>.0001f?Vector3.Dot(normal,toEye.normalized):-1;
        // Both thresholds stay strictly on the palmar side; the band prevents edge-on flicker.
        Visible=FacingDot>(Visible?HideFacingDot:ShowFacingDot);
        if(!Bound&&Visible){SizeDistance=Mathf.Clamp(toEye.magnitude,.25f,1.2f);Bound=true;}
        return true;
    }
    static bool Finite(Pose pose)
    {
        float p=pose.position.sqrMagnitude,q=Quaternion.Dot(pose.rotation,pose.rotation);
        return !float.IsNaN(p)&&!float.IsInfinity(p)&&!float.IsNaN(q)&&!float.IsInfinity(q)&&q>.5f;
    }
}
