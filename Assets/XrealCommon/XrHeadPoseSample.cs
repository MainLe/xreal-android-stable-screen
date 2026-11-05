using UnityEngine;
using UnityEngine.XR;

/// <summary>One validity policy for the spatial demo and stabilized screen. No prediction or filtering.</summary>
public struct XrHeadPoseSample
{
    public Pose Pose;
    public InputTrackingState Flags;
    public bool RotationTracked,PositionTracked;
    public static bool ValidRotation(bool tracked,InputTrackingState flags,Quaternion rotation)
    {
        float norm=Quaternion.Dot(rotation,rotation);
        return tracked&&(flags&InputTrackingState.Rotation)!=0&&norm>.5f&&norm<1.5f;
    }
    public static bool ValidPosition(bool tracked,InputTrackingState flags,Vector3 position)=>tracked&&(flags&InputTrackingState.Position)!=0&&position.sqrMagnitude<1000000;
    public static XrHeadPoseSample Read()
    {
        var device=InputDevices.GetDeviceAtXRNode(XRNode.Head);
        bool valid=device.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked)&&tracked;
        device.TryGetFeatureValue(CommonUsages.trackingState,out var flags);
        bool rotation=device.TryGetFeatureValue(CommonUsages.deviceRotation,out var q);
        bool position=device.TryGetFeatureValue(CommonUsages.devicePosition,out var p);
        return new XrHeadPoseSample{Pose=new Pose(p,q),Flags=flags,RotationTracked=rotation&&ValidRotation(valid,flags,q),PositionTracked=position&&ValidPosition(valid,flags,p)};
    }
}
