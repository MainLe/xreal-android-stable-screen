using UnityEditor.Build;
using UnityEngine;

public static class HandInteractionChecks
{
    static void Require(bool value,string message){if(!value)throw new BuildFailedException(message);}
    public static void Run()
    {
        Require(HandInteractionMath.PhoneUv(new Vector2(.25f,1),true)==new Vector2(.25f,0),"Top image row mapping failed");
        Require(HandInteractionMath.PhoneUv(new Vector2(.8f,0),false)==new Vector2(.8f,0),"Unflipped image mapping failed");
        Vector3 v=new Vector3(2,1,-1),original=v;Vector3 full=HandInteractionMath.DampedStep(ref v,2,1);
        Vector3 splitV=original,split=Vector3.zero;for(int i=0;i<100;i++)split+=HandInteractionMath.DampedStep(ref splitV,2,.01f);
        Require(Vector3.Distance(full,split)<.0001f&&Vector3.Distance(v,splitV)<.0001f,"Damping depends on render rate");
        Vector3 stopped=Vector3.zero;Require(HandInteractionMath.DampedStep(ref stopped,2,100)==Vector3.zero,"Zero velocity gained gravity");
        Require(full.y>0&&v.magnitude<original.magnitude,"Damping accelerated or inverted motion");
        Quaternion q=Quaternion.Euler(0,5,0);Vector3 spin=HandInteractionMath.AngularVelocity(Quaternion.identity,q,.05f);
        Vector3 sign=HandInteractionMath.AngularVelocity(Quaternion.identity,new Quaternion(-q.x,-q.y,-q.z,-q.w),.05f);
        Require(Vector3.Distance(spin,sign)<.0001f&&Mathf.Abs(spin.y-100*Mathf.Deg2Rad)<.001f,"Rotation derivative sign/unit regression");
        Require(HandInteractionMath.AngularVelocity(Quaternion.identity,q,1)==Vector3.zero,"Stale samples created a throw");
        Debug.Log("[HandInteractionChecks] UV orientation, frame-rate-independent damping, no gravity, quaternion velocity and stale-sample checks passed.");
    }
}
