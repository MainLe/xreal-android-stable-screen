using UnityEngine;

/// <summary>Real-hand grabbing, wrist/two-hand rotation, palm pushes and damped zero-gravity throws.</summary>
public sealed class InertialCubeInteraction : MonoBehaviour
{
    public FeatureHandRig Hands;
    public FeatureHeadPose HeadPose;
    public bool InteractionEnabled=true;
    public bool IdleSpin;
    public string State { get; private set; }="ready";
    public Vector3 Velocity { get; private set; }
    public Vector3 Spin { get; private set; }
    readonly bool[] previousPinch=new bool[2],valid=new bool[2],pinch=new bool[2],previousValid=new bool[2];
    readonly Pose[] wrist=new Pose[2];
    readonly Vector3[] points=new Vector3[2],lastPalm=new Vector3[2];
    readonly float[] pushAfter=new float[2];
    int owner=-1;
    bool twoHands;
    Vector3 offset,lastPosition;
    Quaternion gripRotation,lastRotation;
    float lastTime,lastMotion;
    public bool Held=>owner>=0;
    public void StopMotion(){Velocity=Spin=Vector3.zero;owner=-1;twoHands=false;lastTime=0;State="stopped";for(int i=0;i<2;i++)previousPinch[i]=true;}
    bool Frame(out Pose pose)
    {
        Vector3 right=points[1]-points[0];Vector3 up=(wrist[0].rotation*Vector3.up+wrist[1].rotation*Vector3.up).normalized;
        Vector3 forward=Vector3.Cross(right,up);if(right.magnitude<.06f||forward.sqrMagnitude<.001f){pose=default;return false;}
        pose=new Pose((points[0]+points[1])*.5f,Quaternion.LookRotation(forward.normalized,Vector3.Cross(forward,right).normalized));return true;
    }
    void Bind(Pose grip){offset=Quaternion.Inverse(grip.rotation)*(transform.position-grip.position);gripRotation=Quaternion.Inverse(grip.rotation)*transform.rotation;lastPosition=transform.position;lastRotation=transform.rotation;lastTime=Time.unscaledTime;lastMotion=lastTime;Velocity=Spin=Vector3.zero;}
    void LateUpdate()
    {
        float now=Time.unscaledTime,dt=Time.unscaledDeltaTime;
        if(!InteractionEnabled||!Hands||!HeadPose||!HeadPose.PositionTracked||!HeadPose.RotationTracked){StopMotion();return;}
        for(int h=0;h<2;h++)valid[h]=Hands.TryGetInteractionPose(h,out wrist[h],out points[h],out pinch[h]);
        if(owner>=0)
        {
            if(!valid[owner]){StopMotion();State="tracking lost; no throw";}
            else if(!pinch[owner])
            {
                if(twoHands&&valid[1-owner]&&pinch[1-owner]){owner=1-owner;twoHands=false;Bind(new Pose(points[owner],wrist[owner].rotation));}
                else{owner=-1;twoHands=false;if(now-lastMotion>.12f)Velocity=Spin=Vector3.zero;State="released with damping";}
            }
            else
            {
                bool dual=valid[1-owner]&&pinch[1-owner]&&(twoHands||Vector3.Distance(points[1-owner],transform.position)<.24f);
                Pose grip=new Pose(points[owner],wrist[owner].rotation);
                if(dual){if(Frame(out Pose pair))grip=pair;else dual=false;}
                if(dual!=twoHands){twoHands=dual;Bind(grip);}
                Vector3 target=grip.position+grip.rotation*offset;Quaternion rotation=grip.rotation*gripRotation;
                float step=now-lastTime;
                if(step>.0001f&&step<.1f&&Vector3.Distance(target,lastPosition)<.25f)
                {
                    Vector3 v=Vector3.ClampMagnitude((target-lastPosition)/step,3);
                    Vector3 w=Vector3.ClampMagnitude(HandInteractionMath.AngularVelocity(lastRotation,rotation,step),6);
                    float blend=1-Mathf.Exp(-step*20);Velocity=Vector3.Lerp(Velocity,v,blend);Spin=Vector3.Lerp(Spin,w,blend);
                    lastMotion=now;
                }
                else Velocity=Spin=Vector3.zero;
                transform.SetPositionAndRotation(target,rotation);lastPosition=target;lastRotation=rotation;lastTime=now;
                State=twoHands?"two-hand rotation":"pinch + wrist rotation";
            }
        }
        if(owner<0)
        {
            for(int h=0;h<2;h++)if(valid[h]&&pinch[h]&&!previousPinch[h]&&Vector3.Distance(points[h],transform.position)<.18f){owner=h;Bind(new Pose(points[h],wrist[h].rotation));State="grabbed";break;}
            if(owner<0)
            {
                for(int h=0;h<2;h++)
                {
                    if(valid[h]&&!pinch[h]&&Hands.IsOpenPalm(h)&&previousValid[h]&&dt>.001f&&dt<.1f&&now>=pushAfter[h])
                    {
                        Vector3 palmVelocity=(wrist[h].position-lastPalm[h])/dt;Vector3 toward=transform.position-wrist[h].position;
                        if(toward.magnitude<.13f&&palmVelocity.magnitude<4&&Vector3.Dot(palmVelocity,toward.normalized)>.12f)
                        {Velocity=Vector3.ClampMagnitude(Velocity+palmVelocity*.8f,3);pushAfter[h]=now+.3f;State="palm push";}
                    }
                }
                Vector3 velocity=Velocity,spin=Spin;transform.position+=HandInteractionMath.DampedStep(ref velocity,2.2f,dt);
                Vector3 rotationStep=HandInteractionMath.DampedStep(ref spin,2.8f,dt);
                if(rotationStep.sqrMagnitude>0)transform.rotation=Quaternion.AngleAxis(rotationStep.magnitude*Mathf.Rad2Deg,rotationStep.normalized)*transform.rotation;
                Velocity=velocity.magnitude<.005f?Vector3.zero:velocity;Spin=spin.magnitude<.01f?Vector3.zero:spin;
                if(IdleSpin&&Velocity==Vector3.zero&&Spin==Vector3.zero)transform.Rotate(0,25*dt,0,Space.World);
            }
        }
        for(int h=0;h<2;h++){previousPinch[h]=!valid[h]||pinch[h];previousValid[h]=valid[h];lastPalm[h]=wrist[h].position;}
    }
    void OnApplicationPause(bool pause){if(pause)StopMotion();}
    void OnDisable(){StopMotion();}
}
