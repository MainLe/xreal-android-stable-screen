using System;
using UnityEngine;
using UnityEditor.Build;

public static class StableScreenMotionChecks
{
    static void Require(bool condition,string name){if(!condition)throw new BuildFailedException("Stable pose check: "+name);}
    public static void Run()
    {
        var filter=new StablePoseFilter();double rawEnergy=0,filteredEnergy=0;
        for(int i=0;i<240;i++)
        {
            var raw=new Pose(Vector3.zero,Quaternion.Euler(0,0,i%2==0?.08f:-.08f));
            Pose smooth=filter.Step(raw,i/60.0,2,false);
            if(i>60){rawEnergy+=Math.Pow((raw.rotation*Vector3.right).y,2);filteredEnergy+=Math.Pow((smooth.rotation*Vector3.right).y,2);}
        }
        Require(filteredEnergy<rawEnergy*.6,"stationary angular jitter attenuation");
        filter.Reset();
        for(int i=0;i<180;i++)
        {
            var raw=new Pose(new Vector3(i*.01f,0,0),Quaternion.Euler(0,i*1.5f,0));
            Pose smooth=filter.Step(raw,i/60.0,2,true);
            Require(StablePoseFilter.Angle(raw.rotation,smooth.rotation)<.5f,"fast-turn bounded lag");
            Require(Vector3.Distance(raw.position,smooth.position)<.0151f,"position bounded lag");
            Pose anchor=new Pose(new Vector3(.3f,.7f,2),Quaternion.Euler(10,25,-4));
            Pose content=StablePoseFilter.Compensate(raw,smooth,anchor);
            Vector3 visible=Quaternion.Inverse(raw.rotation)*(content.position-raw.position);
            Vector3 expected=Quaternion.Inverse(smooth.rotation)*(anchor.position-smooth.position);
            Require(Vector3.Distance(visible,expected)<.0001f,"6DoF content compensation matches filtered view");
        }
        var pose=new Pose(new Vector3(.2f,.1f,.3f),Quaternion.Euler(0,179.8f,0));
        filter.Reset();filter.Step(pose,0,2,true);
        var negated=pose;negated.rotation=new Quaternion(-pose.rotation.x,-pose.rotation.y,-pose.rotation.z,-pose.rotation.w);
        Require(Quaternion.Angle(filter.Step(negated,.02,2,true).rotation,pose.rotation)<.05f,"quaternion sign equivalence");
        Pose recovered=filter.Step(new Pose(Vector3.one,Quaternion.Euler(20,60,0)),2,2,true);
        Require(Vector3.Distance(recovered.position,Vector3.one)<.0001f,"tracking gap resets without trailing old coordinates");
        Pose direct=filter.Step(pose,2.02,0,true);
        Require(Vector3.Distance(direct.position,pose.position)<.0001f&&Quaternion.Angle(direct.rotation,pose.rotation)<.05f,"disabled filter is transparent");
        var beforeRender=new Pose(new Vector3(.201f,.1f,.3f),Quaternion.Euler(0,179.85f,0));
        Pose immediate=filter.Step(beforeRender,2.0201,0,true);
        Require(Vector3.Distance(immediate.position,beforeRender.position)<.00001f&&StablePoseFilter.Angle(immediate.rotation,beforeRender.rotation)<.0001f,"disabled filter must not reuse Update sample for sub-millisecond before-render");
        Require(!XrHeadPoseSample.ValidRotation(false,UnityEngine.XR.InputTrackingState.Rotation,Quaternion.identity),"isTracked false overrides stale rotation flags");
        Require(!XrHeadPoseSample.ValidPosition(false,UnityEngine.XR.InputTrackingState.Position,Vector3.zero),"isTracked false overrides stale position flags");
        Require(!XrHeadPoseSample.ValidRotation(true,UnityEngine.XR.InputTrackingState.Rotation,new Quaternion(float.NaN,0,0,1)),"invalid quaternion rejected");
        Pose fixedAnchor=new Pose(new Vector3(0,0,2),Quaternion.identity);
        Pose unchanged=StablePoseFilter.Compensate(pose,pose,fixedAnchor);
        Require(Vector3.Distance(unchanged.position,fixedAnchor.position)<.0001f,"world anchor remains fixed without filtering");
        Debug.Log("[StableScreenBuild] Adaptive filter jitter / lag / quaternion / 6DoF compensation checks passed.");
        Debug.Log("[XrRuntimeChecks] Sub-millisecond OFF passthrough and strict tracking validity checks passed.");
    }
}
