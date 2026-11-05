using UnityEngine;

/// <summary>Adaptive quaternion/position low-pass with a small deadband and bounded correction.</summary>
public sealed class StablePoseFilter
{
    Pose filtered,previous;
    Vector3 angularVelocity,velocity;
    double lastTime;
    bool initialized;
    public float RotationCorrection { get; private set; }
    public float PositionCorrection { get; private set; }
    static float Alpha(float hz,float dt)=>1f-Mathf.Exp(-2*Mathf.PI*hz*dt);
    // acos(dot) and ToAngleAxis lose sub-degree detail when w rounds to 1 in float precision.
    static Vector3 RotationVector(Quaternion delta)
    {
        if(delta.w<0)delta=new Quaternion(-delta.x,-delta.y,-delta.z,-delta.w);
        var v=new Vector3(delta.x,delta.y,delta.z);float length=v.magnitude;
        return length<1e-9f?Vector3.zero:v*(2*Mathf.Atan2(length,delta.w)*Mathf.Rad2Deg/length);
    }
    public static float Angle(Quaternion a,Quaternion b)=>RotationVector(a*Quaternion.Inverse(b)).magnitude;
    public void Reset(){initialized=false;RotationCorrection=PositionCorrection=0;}
    public Pose Step(Pose raw,double time,int strength,bool position)
    {
        // Before-render can follow Update by less than 1 ms. OFF must still return the new raw sample.
        if(strength<=0){filtered=previous=raw;lastTime=time;initialized=true;angularVelocity=velocity=Vector3.zero;RotationCorrection=PositionCorrection=0;return raw;}
        if(!initialized||time-lastTime>.3||time<lastTime||Vector3.Distance(raw.position,previous.position)>1){filtered=previous=raw;lastTime=time;initialized=true;angularVelocity=velocity=Vector3.zero;RotationCorrection=PositionCorrection=0;return raw;}
        float dt=(float)(time-lastTime);if(dt<.001f)return filtered;
        strength=Mathf.Clamp(strength,1,3);
        Quaternion delta=raw.rotation*Quaternion.Inverse(previous.rotation);
        Vector3 angular=RotationVector(delta)/dt;
        angularVelocity=Vector3.Lerp(angularVelocity,angular,Alpha(1,dt));
        velocity=Vector3.Lerp(velocity,(raw.position-previous.position)/dt,Alpha(1,dt));
        float cutoff=strength==1?6:strength==2?2.5f:1.2f;
        float deadband=strength==1?.015f:strength==2?.035f:.065f;
        float maxAngle=strength==1?.25f:strength==2?.45f:.75f;
        if(Angle(filtered.rotation,raw.rotation)>deadband)
            filtered.rotation=Quaternion.Slerp(filtered.rotation,raw.rotation,Alpha(cutoff+.55f*angularVelocity.magnitude,dt));
        float lag=Angle(raw.rotation,filtered.rotation);
        if(lag>maxAngle)
            filtered.rotation=Quaternion.Slerp(raw.rotation,filtered.rotation,maxAngle/lag);
        if(position)
        {
            if(Vector3.Distance(filtered.position,raw.position)>.0005f)
                filtered.position=Vector3.Lerp(filtered.position,raw.position,Alpha(6+35*velocity.magnitude,dt));
            filtered.position=raw.position+Vector3.ClampMagnitude(filtered.position-raw.position,.015f);
        }
        else filtered.position=raw.position;
        previous=raw;lastTime=time;RotationCorrection=Angle(raw.rotation,filtered.rotation);PositionCorrection=Vector3.Distance(raw.position,filtered.position);return filtered;
    }
    /// <summary>Preserves the native XR camera/warp pose; applies the small filter correction to content.</summary>
    public static Pose Compensate(Pose raw,Pose smooth,Pose anchor)
    {
        Quaternion correction=raw.rotation*Quaternion.Inverse(smooth.rotation);
        return new Pose(raw.position+correction*(anchor.position-smooth.position),correction*anchor.rotation);
    }
}
