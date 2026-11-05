using Unity.Collections;
using UnityEditor.Build;
using UnityEngine;

public static class PlanePinChecks
{
    static void Require(bool value,string message){if(!value)throw new BuildFailedException(message);}
    public static void Run()
    {
        using(var rectangle=new NativeArray<Vector2>(new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)},Allocator.Temp))
        using(var concave=new NativeArray<Vector2>(new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,0),new Vector2(0,0),new Vector2(0,1),new Vector2(-1,1)},Allocator.Temp))
        using(var invalid=new NativeArray<Vector2>(new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(float.NaN,1)},Allocator.Temp))
        using(var degenerate=new NativeArray<Vector2>(new[]{Vector2.zero,Vector2.one,Vector2.one*2},Allocator.Temp))
        {
            Require(PlanePinMath.Contains(rectangle,Vector2.zero)&&PlanePinMath.Contains(rectangle,new Vector2(1,0))&&!PlanePinMath.Contains(rectangle,new Vector2(1.01f,0)),"Polygon interior/edge/outside mismatch");
            Require(PlanePinMath.Contains(concave,new Vector2(-.5f,.5f))&&!PlanePinMath.Contains(concave,new Vector2(.5f,.5f)),"Selection accepted only the plane's bounding rectangle");
            Require(!PlanePinMath.Contains(invalid,new Vector2(0,-1))&&!PlanePinMath.Contains(degenerate,Vector2.one),"Invalid or degenerate plane polygon accepted");
            Pose wall=new Pose(new Vector3(0,0,2),Quaternion.Euler(90,0,0));Ray forward=new Ray(Vector3.zero,Vector3.forward);
            Require(PlanePinMath.Hit(forward,wall,rectangle,out Vector3 wallHit,out float distance)&&Vector3.Distance(wallHit,new Vector3(0,0,2))<.0001f&&Mathf.Abs(distance-2)<.0001f,"Real plane ray intersection incorrect");
            Require(!PlanePinMath.Hit(new Ray(new Vector3(2,0,0),Vector3.forward),wall,rectangle,out _,out _),"Ray outside plane boundary accepted");
            Require(!PlanePinMath.Hit(new Ray(Vector3.zero,Vector3.right),wall,rectangle,out _,out _),"Parallel ray hit plane");
            Require(!PlanePinMath.Hit(forward,new Pose(new Vector3(0,0,-2),wall.rotation),rectangle,out _,out _)&&!PlanePinMath.Hit(forward,new Pose(new Vector3(0,0,9),wall.rotation),rectangle,out _,out _),"Behind or too-distant plane accepted");
            Pose world=new Pose(new Vector3(1,2,3),Quaternion.Euler(20,45,10));
            Require(PlanePinMath.Hit(new Ray(world.position,world.rotation*Vector3.forward),new Pose(world.position+world.rotation*wall.position,world.rotation*wall.rotation),rectangle,out Vector3 movedHit,out float movedDistance)&&Vector3.Distance(movedHit,world.position+world.rotation*wallHit)<.0001f&&Mathf.Abs(movedDistance-distance)<.0001f,"Ray/polygon selection depends on origin orientation");
            Pose viewer=new Pose(Vector3.zero,Quaternion.identity);
            Require(PlanePinMath.Placement(wallHit,wall.rotation*Vector3.up,viewer,out Pose screen),"Wall screen placement failed");
            Require(Vector3.Distance(screen.position,new Vector3(0,0,1.99f))<.0001f&&Vector3.Dot(-(screen.rotation*Vector3.forward),Vector3.back)>.999f&&Vector3.Dot(screen.rotation*Vector3.up,Vector3.up)>.999f,"Wall screen faces wrong side or is not flush/upright");
            Require(PlanePinMath.Placement(wallHit,-(wall.rotation*Vector3.up),viewer,out Pose reversed)&&Vector3.Distance(reversed.position,screen.position)<.0001f&&Quaternion.Angle(reversed.rotation,screen.rotation)<.02f,"Plane normal sign changed visible side");
            Pose floor=new Pose(new Vector3(0,-.5f,0),Quaternion.identity);Ray down=new Ray(Vector3.zero,new Vector3(0,-1,1));
            Require(PlanePinMath.Hit(down,floor,rectangle,out Vector3 floorHit,out _),"Horizontal plane hit failed");
            Require(PlanePinMath.Placement(floorHit,Vector3.up,new Pose(Vector3.zero,Quaternion.LookRotation(down.direction,Vector3.up)),out Pose table)&&Mathf.Abs(table.position.y+.49f)<.0001f&&Vector3.Dot(-(table.rotation*Vector3.forward),Vector3.up)>.999f&&Vector3.Dot(table.rotation*Vector3.up,Vector3.forward)>.999f,"Table screen orientation/offset failed");
            Require(PlanePinMath.Placement(Vector3.down,Vector3.up,new Pose(Vector3.zero,Quaternion.LookRotation(Vector3.down,Vector3.forward)),out Pose directDown)&&!float.IsNaN(directDown.rotation.w),"Looking straight down made a degenerate screen rotation");
            Require(!PlanePinMath.Placement(new Vector3(float.NaN,0,0),Vector3.up,viewer,out _)&&!PlanePinMath.Placement(Vector3.zero,Vector3.zero,viewer,out _),"Invalid plane pose accepted");
        }
        var pinch=new PlanePinGesture();Require(!pinch.Sample(true,true),"Initially held pinch auto-confirmed placement");
        Require(!pinch.Sample(true,false)&&pinch.Sample(true,true)&&!pinch.Sample(true,true),"Selection pinch not edge-triggered");
        Require(!pinch.Sample(false,false)&&!pinch.Sample(true,true),"Tracking recovery auto-confirmed held pinch");
        pinch.Sample(true,false);Require(pinch.Sample(true,true),"Reopened hand could not confirm");
        pinch.Reset();Require(!pinch.Sample(true,true),"Cancelled selection retained pinch permission");
        Debug.Log("[PlanePinChecks] Detected polygon/concavity, bounded rays, wall/table pose and visible side, invalid geometry, pinch release/recovery checks passed.");
    }
}
