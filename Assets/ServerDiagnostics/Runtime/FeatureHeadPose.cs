using UnityEngine;
using UnityEngine.XR;
using Unity.XR.XREAL;

/// <summary>Reads only real XR poses. Invalid samples retain the last pose and are reported separately.</summary>
public sealed class FeatureHeadPose : MonoBehaviour
{
    public bool Tracked { get; private set; }
    public bool PositionTracked { get; private set; }
    public bool RotationTracked { get; private set; }
    public InputTrackingState TrackingState { get; private set; }
    void OnEnable() { Application.onBeforeRender += Apply; }
    void OnDisable() { Application.onBeforeRender -= Apply; }
    void Update() { Apply(); }
    void Apply()
    {
        var sample=XrHeadPoseSample.Read();TrackingState=sample.Flags;
        RotationTracked=sample.RotationTracked;PositionTracked=sample.PositionTracked;Tracked=RotationTracked;
        if(RotationTracked)transform.localRotation=sample.Pose.rotation;
        int mode=XREALPlugin.GetTrackingType()==TrackingType.MODE_6DOF?6:3;
        if(mode==6&&PositionTracked)transform.localPosition=sample.Pose.position;
        else if(mode==3)transform.localPosition=Vector3.zero;
        XrRuntimeHealth.RotationTracked=RotationTracked;XrRuntimeHealth.PositionTracked=PositionTracked;XrRuntimeHealth.TrackingMode=mode;
    }
}
