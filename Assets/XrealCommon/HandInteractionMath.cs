using UnityEngine;

/// <summary>Pure coordinate and no-gravity inertia math shared by offline checks and runtime.</summary>
public static class HandInteractionMath
{
    public static Vector2 PhoneUv(Vector2 quadUv,bool flipVertical)=>new Vector2(quadUv.x,flipVertical?1-quadUv.y:quadUv.y);
    public static Vector3 AngularVelocity(Quaternion before,Quaternion after,float dt)
    {
        if(dt<=.0001f||dt>.1f)return Vector3.zero;
        Quaternion q=after*Quaternion.Inverse(before);if(q.w<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
        Vector3 axis=new Vector3(q.x,q.y,q.z);float length=axis.magnitude;
        return length<.000001f?Vector3.zero:axis*(2*Mathf.Atan2(length,Mathf.Clamp(q.w,-1,1))/(length*dt));
    }
    public static Vector3 DampedStep(ref Vector3 velocity,float damping,float dt)
    {
        if(dt<=0)return Vector3.zero;
        if(damping<=0)return velocity*dt;
        float decay=Mathf.Exp(-damping*dt);Vector3 step=velocity*((1-decay)/damping);velocity*=decay;return step;
    }
}
