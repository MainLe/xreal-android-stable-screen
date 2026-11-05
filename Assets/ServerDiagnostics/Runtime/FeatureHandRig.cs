using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

/// <summary>XR Hands joint visualization and explicitly geometric gestures (not vendor gesture labels).</summary>
public sealed class FeatureHandRig : MonoBehaviour
{
    public Transform TrackingOrigin;
    public Material LeftMaterial, RightMaterial;
    public bool ShowHands;
    public bool VisualizeJoints=true;
    public bool VisualizeJointMarkers=true;
    public float JointDiameter=.009f,BoneWidth=.003f;
    public string LeftStatus { get; private set; } = "disabled";
    public string RightStatus { get; private set; } = "disabled";
    public bool Running => subsystem != null && subsystem.running;
    public event Action<int, Vector3> PinchStarted;
    public event Action<int, Vector3> PinchMoved;
    public event Action<int> PinchEnded;
    XRHandSubsystem subsystem;
    readonly List<XRHandSubsystem> subsystems = new List<XRHandSubsystem>();
    readonly HandView[] views = { new HandView(), new HandView() };
    float nextDiscovery;
    sealed class HandView
    {
        public GameObject root;
        public readonly Transform[] joints = new Transform[26];
        public readonly Vector3[] positions = new Vector3[26];
        public readonly bool[] valid = new bool[26];
        public readonly List<LineRenderer> bones = new List<LineRenderer>();
        public bool pinch;
        public bool interactionValid;
        public bool open;
        public bool tracked;
        public Pose wrist;
        public Pose palm;
        public Vector3 pinchPoint;
    }
    static readonly XRHandJointID[][] fingers = {
        new[]{XRHandJointID.Wrist,XRHandJointID.ThumbMetacarpal,XRHandJointID.ThumbProximal,XRHandJointID.ThumbDistal,XRHandJointID.ThumbTip},
        new[]{XRHandJointID.Wrist,XRHandJointID.IndexMetacarpal,XRHandJointID.IndexProximal,XRHandJointID.IndexIntermediate,XRHandJointID.IndexDistal,XRHandJointID.IndexTip},
        new[]{XRHandJointID.Wrist,XRHandJointID.MiddleMetacarpal,XRHandJointID.MiddleProximal,XRHandJointID.MiddleIntermediate,XRHandJointID.MiddleDistal,XRHandJointID.MiddleTip},
        new[]{XRHandJointID.Wrist,XRHandJointID.RingMetacarpal,XRHandJointID.RingProximal,XRHandJointID.RingIntermediate,XRHandJointID.RingDistal,XRHandJointID.RingTip},
        new[]{XRHandJointID.Wrist,XRHandJointID.LittleMetacarpal,XRHandJointID.LittleProximal,XRHandJointID.LittleIntermediate,XRHandJointID.LittleDistal,XRHandJointID.LittleTip}
    };
    void Start()
    {
        for (int h=0;h<2;h++)
        {
            var view=views[h];view.root=new GameObject(h==0?"Left hand joints":"Right hand joints");
            view.root.transform.SetParent(transform,false);
            var material=h==0?LeftMaterial:RightMaterial;
            for(int i=0;i<26;i++)
            {
                var joint=GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(joint.GetComponent<Collider>());joint.transform.SetParent(view.root.transform,false);
                joint.transform.localScale=Vector3.one*JointDiameter;joint.GetComponent<Renderer>().sharedMaterial=material;
                view.joints[i]=joint.transform;
            }
            foreach(var finger in fingers) for(int i=1;i<finger.Length;i++)
            {
                var line=new GameObject("Bone").AddComponent<LineRenderer>();line.transform.SetParent(view.root.transform,false);
                line.sharedMaterial=material;line.startWidth=line.endWidth=BoneWidth;line.positionCount=2;view.bones.Add(line);
            }
            view.root.SetActive(false);
        }
    }
    void Update()
    {
        if((subsystem==null||!subsystem.running)&&Time.unscaledTime>=nextDiscovery)
        {
            nextDiscovery=Time.unscaledTime+1;subsystems.Clear();SubsystemManager.GetSubsystems(subsystems);
            subsystem=subsystems.Find(s=>s.running);
        }
        LeftStatus=UpdateHand(0,subsystem==null?default:subsystem.leftHand);
        RightStatus=UpdateHand(1,subsystem==null?default:subsystem.rightHand);
    }
    string UpdateHand(int side,XRHand hand)
    {
        var v=views[side];bool active=ShowHands&&Running&&hand.isTracked;
        v.root.SetActive(active&&VisualizeJoints);v.interactionValid=false;v.open=false;v.tracked=active;
        if(!active){EndPinch(side,v);return !ShowHands?"disabled":!Running?"subsystem not running":"not tracked";}
        int count=0;
        for(int id=(int)XRHandJointID.BeginMarker;id<(int)XRHandJointID.EndMarker;id++)
        {
            var jointID=(XRHandJointID)id;int i=jointID.ToIndex();
            v.valid[i]=hand.GetJoint(jointID).TryGetPose(out var pose)&&pose.position.sqrMagnitude<10000&&Quaternion.Dot(pose.rotation,pose.rotation)>.5f;v.joints[i].gameObject.SetActive(v.valid[i]&&VisualizeJointMarkers);
            if(!v.valid[i])continue;count++;v.positions[i]=TrackingOrigin.TransformPoint(pose.position);
            v.joints[i].SetPositionAndRotation(v.positions[i],TrackingOrigin.rotation*pose.rotation);
            if(jointID==XRHandJointID.Wrist)v.wrist=new Pose(v.positions[i],TrackingOrigin.rotation*pose.rotation);
            if(jointID==XRHandJointID.Palm)v.palm=new Pose(v.positions[i],TrackingOrigin.rotation*pose.rotation);
        }
        int bone=0;foreach(var finger in fingers)for(int i=1;i<finger.Length;i++)
        {
            int a=finger[i-1].ToIndex(),b=finger[i].ToIndex();var line=v.bones[bone++];line.enabled=v.valid[a]&&v.valid[b];
            if(line.enabled){line.SetPosition(0,v.positions[a]);line.SetPosition(1,v.positions[b]);}
        }
        int thumb=XRHandJointID.ThumbTip.ToIndex(),index=XRHandJointID.IndexTip.ToIndex();
        float distance=float.NaN;
        if(v.valid[thumb]&&v.valid[index])
        {
            distance=Vector3.Distance(v.positions[thumb],v.positions[index]);
            bool pinch=PinchState(v.pinch,distance);Vector3 point=(v.positions[thumb]+v.positions[index])*0.5f;
            v.pinchPoint=point;v.interactionValid=v.valid[XRHandJointID.Wrist.ToIndex()];
            if(pinch&&!v.pinch)PinchStarted?.Invoke(side,point);
            if(pinch)PinchMoved?.Invoke(side,point);
            if(!pinch&&v.pinch)PinchEnded?.Invoke(side);v.pinch=pinch;
        }
        else EndPinch(side,v);
        string shape=Classify(v);v.open=shape=="OPEN PALM (geometry)";
        return $"{count}/26 joints | {(v.pinch?"PINCH":shape)} | pinch {distance*1000:F0} mm";
    }
    public static bool PinchState(bool wasPinching,float metres)
    {
        return !float.IsNaN(metres)&&metres>=0&&metres<(wasPinching?0.035f:0.025f);
    }
    public bool TryGetInteractionPose(int side,out Pose wrist,out Vector3 point,out bool pinching)
    {
        var v=views[Mathf.Clamp(side,0,1)];wrist=v.wrist;point=v.pinchPoint;pinching=v.pinch;
        return ShowHands&&Running&&v.interactionValid;
    }
    public bool IsOpenPalm(int side)=>ShowHands&&Running&&views[Mathf.Clamp(side,0,1)].interactionValid&&views[Mathf.Clamp(side,0,1)].open;
    public bool TryGetPhoneRayPose(int side,out Vector3 palm,out bool pinching)
    {
        var v=views[Mathf.Clamp(side,0,1)];int centre=XRHandJointID.Palm.ToIndex();
        // Fingertips close by several centimetres while pinching. Use the palm
        // as the phone pointer's geometric aim, leaving the cube's pinch pose intact.
        palm=v.valid[centre]?v.positions[centre]:v.wrist.position;pinching=v.pinch;
        return ShowHands&&Running&&v.interactionValid;
    }
    public bool TryGetIndexTip(int side,out Vector3 point)
    {
        var v=views[Mathf.Clamp(side,0,1)];int index=XRHandJointID.IndexTip.ToIndex();point=v.positions[index];
        return ShowHands&&Running&&v.tracked&&v.valid[index];
    }
    public bool TryGetPalmPose(int side,out Pose palm)
    {
        var v=views[Mathf.Clamp(side,0,1)];bool validPalm=v.valid[XRHandJointID.Palm.ToIndex()];palm=validPalm?v.palm:v.wrist;
        return ShowHands&&Running&&v.tracked&&(validPalm||v.valid[XRHandJointID.Wrist.ToIndex()]);
    }
    public bool TryGetPalmSurfacePose(int side,out Pose palm)
    {
        palm=default;side=Mathf.Clamp(side,0,1);var v=views[side];
        int wrist=XRHandJointID.Wrist.ToIndex(),index=XRHandJointID.IndexProximal.ToIndex(),little=XRHandJointID.LittleProximal.ToIndex(),middle=XRHandJointID.MiddleProximal.ToIndex(),centre=XRHandJointID.Palm.ToIndex();
        if(!ShowHands||!Running||!v.tracked||!v.valid[wrist]||!v.valid[index]||!v.valid[little])return false;
        Vector3 roots=(v.positions[index]+v.positions[little])*.5f;
        Vector3 palmCentre=v.valid[centre]?v.positions[centre]:(v.positions[wrist]+roots)*.5f;
        return PalmSurfaceFrame.TryCreate(v.positions[wrist],v.positions[index],v.positions[little],v.valid[middle]?v.positions[middle]:roots,palmCentre,side==0,out palm);
    }
    public bool TryGetIndexPointer(int side,out Vector3 tip,out bool pinching)
    {
        var v=views[Mathf.Clamp(side,0,1)];pinching=v.pinch;
        return TryGetIndexTip(side,out tip)&&v.valid[XRHandJointID.ThumbTip.ToIndex()];
    }
    static string Classify(HandView v)
    {
        int wrist=XRHandJointID.Wrist.ToIndex();if(!v.valid[wrist])return "unknown";
        int open=0,closed=0;
        for(int f=1;f<5;f++)
        {
            int tip=fingers[f][5].ToIndex(),prox=fingers[f][2].ToIndex();
            if(!v.valid[tip]||!v.valid[prox])return "partial";
            float baseLength=Vector3.Distance(v.positions[prox],v.positions[wrist]);
            if(baseLength<0.02f)return "unknown";
            float ratio=Vector3.Distance(v.positions[tip],v.positions[wrist])/baseLength;
            if(ratio>1.65f)open++;if(ratio<1.25f)closed++;
        }
        return open==4?"OPEN PALM (geometry)":closed==4?"FIST (geometry)":"OTHER";
    }
    void EndPinch(int side,HandView v){if(v.pinch)PinchEnded?.Invoke(side);v.pinch=false;}
    void OnDisable(){for(int h=0;h<2;h++){views[h].interactionValid=false;views[h].tracked=false;EndPinch(h,views[h]);if(views[h].root)views[h].root.SetActive(false);}}
}
