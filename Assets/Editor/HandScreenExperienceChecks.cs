using UnityEditor.Build;
using UnityEngine;

public static class HandScreenExperienceChecks
{
    static void Require(bool value,string message){if(!value)throw new BuildFailedException(message);}
    public static void Run()
    {
        var plane=new Pose(new Vector3(1,2,3),Quaternion.Euler(20,65,-15));Vector2 size=new Vector2(.4f,.6f);
        foreach(float depth in new[]{-.8f,-.2f,0,.08f,.3f})
        {
            Vector3 point=plane.position+plane.rotation*new Vector3(.1f,-.15f,depth);
            Vector3 local=HandScreenGeometry.Project(plane,point);Vector2 uv=HandScreenGeometry.Uv(local,size);
            Require(Vector2.Distance(uv,new Vector2(.75f,.25f))<.00001f,"Screen-normal projection shifted with depth or rotation");
            Require(Mathf.Abs(local.z-depth)<.00001f,"Normal distance sign/unit mismatch");
        }
        Require(HandScreenGeometry.DistanceRatio(0)<HandScreenGeometry.DistanceRatio(.1f)&&HandScreenGeometry.DistanceRatio(.1f)<HandScreenGeometry.DistanceRatio(.2f),"Distance cue is not monotonic");
        Require(HandScreenGeometry.DistanceRatio(-.1f)==HandScreenGeometry.DistanceRatio(.1f)&&HandScreenGeometry.DistanceRatio(10)<1,"Distance dot hides its fixed reference or flips sign");
        // Sign regression for this app's XREAL joint stream: index side +X,
        // wrist-to-fingers +Y, expected palmar side +Z. 0.7.1 chose -Z instead.
        // Expected side is explicit so the test cannot reuse the old sign assumption.
        Vector3 wrist=new Vector3(0,-.07f,.45f),index=new Vector3(.035f,0,.45f),little=new Vector3(-.035f,0,.45f),middle=new Vector3(0,.01f,.45f),centre=new Vector3(0,-.03f,.45f);
        Require(PalmSurfaceFrame.TryCreate(wrist,index,little,middle,centre,true,out Pose palm),"Valid left palm frame rejected");
        Vector3 expectedPalmar=Vector3.forward;
        Require(Vector3.Dot(-(palm.rotation*Vector3.forward),expectedPalmar)>.99999f&&Vector3.Dot(palm.rotation*Vector3.up,Vector3.up)>.99999f,"XREAL palmar polarity or finger direction inverted");
        Require(PalmSurfaceFrame.TryCreate(wrist,little,index,middle,centre,false,out Pose right)&&Quaternion.Angle(right.rotation,palm.rotation)<.02f,"Right-hand chirality flipped palmar side");
        Pose world=new Pose(new Vector3(1,2,3),Quaternion.Euler(25,70,15));
        Require(PalmSurfaceFrame.TryCreate(Point(world,wrist),Point(world,index),Point(world,little),Point(world,middle),Point(world,centre),true,out Pose transformed),"Rotated palm frame rejected");
        Require(Vector3.Distance(transformed.position,Point(world,centre))<.00001f&&Quaternion.Angle(transformed.rotation,world.rotation*palm.rotation)<.02f,"Anatomical frame depends on world axes");
        Require(!PalmSurfaceFrame.TryCreate(wrist,index,index,middle,centre,true,out _)&&!PalmSurfaceFrame.TryCreate(wrist,index,little,wrist,centre,true,out _),"Degenerate palm geometry accepted");
        var panel=new LeftHandPanelAnchor();Pose head=new Pose(new Vector3(0,0,.9f),Quaternion.Euler(0,180,0));
        Require(!panel.Step(false,palm,head)&&!panel.Bound&&!panel.Visible,"Invalid palm bound or displayed the screen");
        Require(panel.Step(true,palm,head)&&panel.Visible&&panel.Bound,"Facing palm did not display");Pose initial=panel.Pose;
        Require(Vector3.Distance(initial.position,palm.position-palm.rotation*Vector3.forward*LeftHandPanelAnchor.PalmOffset)<.00001f,"Screen is not on the palmar side");
        Require(Vector3.Dot(initial.position-palm.position,expectedPalmar)>.0299f&&Vector3.Dot(-(initial.rotation*Vector3.forward),(head.position-initial.position).normalized)>.99f,"Placement or displayed face still uses the old dorsal-side sign");
        Require(Quaternion.Angle(initial.rotation,palm.rotation)<.02f&&Mathf.Abs(Vector3.Dot(initial.rotation*Vector3.right,middle-wrist))<.00001f,"Screen not parallel to palm or horizontal edge not perpendicular to fingers");
        Vector3 shift=new Vector3(.2f,.1f,.3f);var moved=new Pose(palm.position+shift,palm.rotation);panel.Step(true,moved,head);
        Require(Vector3.Distance(panel.Pose.position,initial.position+shift)<.00001f,"Palm translation did not carry panel");
        Quaternion turn=Quaternion.Euler(10,40,20);var rotated=new Pose(palm.position,turn*palm.rotation);panel.Step(true,rotated,head);
        Require(Vector3.Distance(panel.Pose.position,palm.position+turn*(initial.position-palm.position))<.00001f&&Quaternion.Angle(panel.Pose.rotation,turn*initial.rotation)<.02f,"Palm rotation changed the phone-like offset");
        panel.Step(true,palm,new Pose(new Vector3(.4f,.1f,.9f),Quaternion.Euler(0,80,0)));
        Require(Quaternion.Angle(panel.Pose.rotation,palm.rotation)<.02f&&panel.Pose.position==initial.position,"Head pose recalibrated the palm attachment");
        var back=new Pose(palm.position,Quaternion.Euler(0,180,0)*palm.rotation);panel.Step(true,back,head);Require(!panel.Visible&&panel.Tracked,"Back of hand displayed the screen");
        var sideways=new Pose(palm.position,Quaternion.Euler(0,90,0)*palm.rotation);panel.Step(true,sideways,head);Require(!panel.Visible,"Edge-on palm displayed the screen");
        panel.Step(true,palm,EyeAtDot(palm,1));Require(panel.Visible,"Front palm did not reappear");
        panel.Step(true,palm,EyeAtDot(palm,.12f));Require(panel.Visible,"Visibility hysteresis hid too early");
        panel.Step(true,palm,EyeAtDot(palm,.05f));Require(!panel.Visible,"Near-edge palm did not hide");
        panel.Step(true,palm,EyeAtDot(palm,.12f));Require(!panel.Visible,"Visibility hysteresis revealed too early");
        panel.Step(true,palm,EyeAtDot(palm,.25f));Require(panel.Visible,"Facing palm did not restore visibility");
        Pose held=panel.Pose;Require(!panel.Step(false,default,head)&&!panel.Tracked&&!panel.Visible&&panel.Bound&&panel.Pose.position==held.position,"Tracking loss did not hide without discarding pose");
        panel.Step(true,palm,head);Require(panel.Visible&&panel.Pose.position==initial.position,"Tracking recovery changed palm attachment");
        float fitted=panel.SizeDistance;
        panel.Step(true,palm,head,90);Pose thumbPose=panel.Pose;
        Vector3 thumbSide=(index-little).normalized;
        Require(Vector3.Dot(thumbPose.rotation*Vector3.up,thumbSide)>.9999f,"Screen top rotated away from thumb side");
        Require(thumbPose.position==initial.position&&panel.Visible&&panel.SizeDistance==fitted&&Vector3.Dot(-(thumbPose.rotation*Vector3.forward),expectedPalmar)>.9999f,"Thumb heading changed palm side, placement, size or visibility");
        panel.Step(true,palm,head,45);
        Require(Mathf.Abs(Vector3.Dot(panel.Pose.rotation*Vector3.up,Vector3.up)-Mathf.Sqrt(.5f))<.0001f&&Mathf.Abs(Vector3.Dot(panel.Pose.rotation*Vector3.up,thumbSide)-Mathf.Sqrt(.5f))<.0001f,"Intermediate thumb heading incorrect");
        panel.Step(true,rotated,head,90);
        Require(Quaternion.Angle(panel.Pose.rotation,turn*thumbPose.rotation)<.02f,"Thumb-oriented phone did not rotate rigidly with palm");
        panel.Step(true,back,head,90);Require(!panel.Visible,"Thumb heading exposed dorsal-side phone");
        Debug.Log("[ThumbHeadingChecks] Thumb-side top, intermediate heading, preserved palm normal/offset/size and rigid following passed.");
        panel.Reset();panel.Step(true,back,head);Require(!panel.Visible&&!panel.Bound,"Hidden initial palm calibrated screen size");panel.Step(true,palm,head);Require(panel.Bound&&panel.Visible,"First facing palm did not calibrate");
        Require(!panel.Step(true,new Pose(new Vector3(float.NaN,0,0),Quaternion.identity),head)&&!panel.Visible,"Nonfinite palm accepted");
        Debug.Log("[HandScreenChecks] Normal projection, distance reference, corrected XREAL palmar polarity, anatomical frame, rigid following, front-only visibility/hysteresis and tracking-loss checks passed.");
    }
    static Vector3 Point(Pose pose,Vector3 point)=>pose.position+pose.rotation*point;
    static Pose EyeAtDot(Pose palm,float dot)
    {
        Vector3 panel=palm.position-palm.rotation*Vector3.forward*LeftHandPanelAnchor.PalmOffset;
        return new Pose(panel+palm.rotation*new Vector3(Mathf.Sqrt(1-dot*dot),0,-dot)*.6f,Quaternion.identity);
    }
}
