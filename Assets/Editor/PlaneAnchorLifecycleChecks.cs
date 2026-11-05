using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.XR.CoreUtils;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>Real AR Foundation managed factory plus an Editor-only controlled anchor provider.</summary>
public static class PlaneAnchorLifecycleChecks
{
    static void Require(bool value,string message){if(!value)throw new BuildFailedException(message);}
    public static void Run()
    {
        var creation=new PlaneAnchorCreation();creation.Begin(0);
        Require(creation.Step(0,false,false,false,false,TrackingState.None)==PlaneAnchorCreation.Decision.Wait&&creation.Attempts==0,"Creation attempted before service ready");
        Require(creation.Step(1,true,false,false,false,TrackingState.None)==PlaneAnchorCreation.Decision.Attempt,"First native registration attempt missing");
        Require(creation.Step(1.1f,true,false,false,false,TrackingState.None)==PlaneAnchorCreation.Decision.Wait,"Transient registration retried every frame");
        Require(creation.Step(1.75f,true,false,false,false,TrackingState.None)==PlaneAnchorCreation.Decision.Attempt&&creation.Attempts==2,"Transient refusal was not retried");
        Require(creation.Step(1.8f,true,true,true,true,TrackingState.Tracking)==PlaneAnchorCreation.Decision.Ready,"Tracking native anchor rejected solely for pending flag");
        creation.Begin(0);creation.Step(0,true,false,false,false,TrackingState.None);
        Require(creation.Step(1,true,true,true,true,TrackingState.Limited)==PlaneAnchorCreation.Decision.Wait&&creation.Step(5,true,true,true,false,TrackingState.None)==PlaneAnchorCreation.Decision.Wait&&creation.Attempts==1,"Registered anchor duplicated or untracked anchor accepted");
        Require(creation.Step(15,true,true,true,true,TrackingState.Limited)==PlaneAnchorCreation.Decision.TimedOut,"Untracked native anchor never timed out");
        Require(creation.Step(16,true,true,true,false,TrackingState.Tracking)==PlaneAnchorCreation.Decision.Wait,"Timed-out request resurrected");
        creation.Begin(0);for(int i=0;i<145;i++)creation.Step(i*.1f,true,false,false,false,TrackingState.None);
        Require(creation.Attempts==PlaneAnchorCreation.MaximumAttempts&&creation.Step(15,true,false,false,false,TrackingState.None)==PlaneAnchorCreation.Decision.TimedOut,"Retries exceeded finite budget");
        creation.Begin(0);Require(creation.Step(1,true,true,false,false,TrackingState.Tracking)==PlaneAnchorCreation.Decision.Wait,"Invalid native ID accepted");creation.Cancel();
        Require(creation.Step(2,true,false,false,false,TrackingState.None)==PlaneAnchorCreation.Decision.Wait,"Cancelled creation retried");
        CheckManagedFactory();
        Debug.Log("[PlaneAnchorLifecycleChecks] Actual AR Foundation factory transient refusal and Tracking+pending, trackables parenting, paced retries, no duplicates, timeout and cancellation passed.");
    }
    static void CheckManagedFactory()
    {
        const string id="Stable Screen Editor Anchor Check";
        XRAnchorSubsystemDescriptor.Create(new XRAnchorSubsystemDescriptor.Cinfo{id=id,providerType=typeof(PlaneAnchorTestProvider),subsystemTypeOverride=typeof(PlaneAnchorTestSubsystem),supportsTrackableAttachments=false});
        var descriptors=new List<XRAnchorSubsystemDescriptor>();SubsystemManager.GetSubsystemDescriptors(descriptors);
        XRAnchorSubsystem subsystem=descriptors.Find(d=>d.id==id).Create();subsystem.Start();PlaneAnchorTestProvider.Attempts=0;
        GameObject root=null;ARAnchorManager manager=null;ARAnchor anchor=null;
        var lifecycle=typeof(SubsystemLifecycleManager<XRAnchorSubsystem,XRAnchorSubsystemDescriptor,XRAnchorSubsystem.Provider>);
        var subsystemProperty=lifecycle.GetProperty("subsystem",BindingFlags.Public|BindingFlags.Instance);
        try
        {
            root=new GameObject("Editor anchor lifecycle fixture");root.SetActive(false);var origin=root.AddComponent<XROrigin>();
            var trackables=new GameObject("Editor trackables").transform;trackables.SetParent(root.transform,false);trackables.position=new Vector3(1,0,0);
            typeof(XROrigin).GetProperty("TrackablesParent").SetValue(origin,trackables);
            manager=root.AddComponent<ARAnchorManager>();manager.enabled=true;
            typeof(ARAnchorManager).BaseType.GetProperty("origin",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(manager,origin);
            subsystemProperty.SetValue(manager,subsystem);
            Pose pose=new Pose(new Vector3(2,3,4),Quaternion.Euler(0,30,0));
            Require(!PlaneNativeAnchorFactory.TryCreate(manager,pose)&&PlaneAnchorTestProvider.Attempts==1,"Factory did not expose transient provider refusal");
            anchor=PlaneNativeAnchorFactory.TryCreate(manager,pose);
            Require(anchor&&anchor.pending&&anchor.trackingState==TrackingState.Tracking&&!anchor.trackableId.Equals(TrackableId.invalidId),"Managed factory did not reproduce real Tracking+pending state");
            Require(anchor.transform.parent==trackables&&Vector3.Distance(anchor.transform.position,pose.position)<.0001f&&PlaneAnchorTestProvider.Attempts==2,"Factory lost origin parenting/world pose or registered twice");
            var request=new PlaneAnchorCreation();request.Begin(0);
            Require(request.Step(0,true,true,true,anchor.pending,anchor.trackingState)==PlaneAnchorCreation.Decision.Ready,"Real managed pending anchor still treated as failure");
        }
        finally
        {
            if(manager)subsystemProperty.SetValue(manager,null);
            if(root)UnityEngine.Object.DestroyImmediate(root);
            subsystem.Stop();subsystem.Destroy();
        }
    }
}

public sealed class PlaneAnchorTestSubsystem : XRAnchorSubsystem { }
public sealed class PlaneAnchorTestProvider : XRAnchorSubsystem.Provider
{
    public static int Attempts;
    public override void Start(){}public override void Stop(){}public override void Destroy(){}
    public override TrackableChanges<XRAnchor> GetChanges(XRAnchor value,Allocator allocator)=>new TrackableChanges<XRAnchor>(0,0,0,allocator,value);
    public override bool TryAddAnchor(Pose pose,out XRAnchor anchor)
    {
        Attempts++;anchor=default;if(Attempts==1)return false;
        anchor=new XRAnchor(new TrackableId(1,2),pose,TrackingState.Tracking,IntPtr.Zero);return true;
    }
    public override bool TryRemoveAnchor(TrackableId id)=>true;
}
