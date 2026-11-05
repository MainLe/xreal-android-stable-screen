using Unity.Collections;
using UnityEngine;

/// <summary>Hit only the detected polygon and place the screen's visible -Z face towards the viewer.</summary>
public static class PlanePinMath
{
    public const float SurfaceOffset=.01f,MaximumDistance=8;
    public static bool Contains(NativeArray<Vector2> polygon,Vector2 point)
    {
        if(!polygon.IsCreated||polygon.Length<3||!Finite(point.sqrMagnitude))return false;
        float area=0;
        for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
        {if(!Finite(polygon[i].sqrMagnitude)||!Finite(polygon[j].sqrMagnitude))return false;area+=polygon[j].x*polygon[i].y-polygon[i].x*polygon[j].y;}
        if(!Finite(area)||Mathf.Abs(area)<.000001f)return false;
        bool inside=false;const float edge=.0005f;
        for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
        {
            Vector2 a=polygon[j],b=polygon[i];
            Vector2 ab=b-a;float length=ab.sqrMagnitude;
            if(length>0&&Vector2.Distance(point,a+ab*Mathf.Clamp01(Vector2.Dot(point-a,ab)/length))<=edge)return true;
            if((a.y>point.y)!=(b.y>point.y)&&point.x<(b.x-a.x)*(point.y-a.y)/(b.y-a.y)+a.x)inside=!inside;
        }
        return inside;
    }
    public static bool Hit(Ray ray,Pose plane,NativeArray<Vector2> boundary,out Vector3 point,out float distance)
    {
        point=default;distance=0;
        if(!Finite(ray.origin.sqrMagnitude)||!Finite(ray.direction.sqrMagnitude)||ray.direction.sqrMagnitude<.0001f||!Valid(plane))return false;
        Vector3 direction=ray.direction.normalized,normal=(plane.rotation*Vector3.up).normalized;
        float denominator=Vector3.Dot(normal,direction);if(Mathf.Abs(denominator)<.0001f)return false;
        distance=Vector3.Dot(normal,plane.position-ray.origin)/denominator;
        if(distance<.05f||distance>MaximumDistance)return false;
        point=ray.origin+direction*distance;Vector3 local=Quaternion.Inverse(plane.rotation)*(point-plane.position);
        return Contains(boundary,new Vector2(local.x,local.z));
    }
    public static bool Placement(Vector3 hit,Vector3 planeNormal,Pose viewer,out Pose screen)
    {
        screen=default;
        if(!Finite(hit.sqrMagnitude)||!Finite(planeNormal.sqrMagnitude)||planeNormal.sqrMagnitude<.0001f||!Valid(viewer))return false;
        Vector3 normal=planeNormal.normalized;if(Vector3.Dot(normal,viewer.position-hit)<0)normal=-normal;
        Vector3 up=Vector3.ProjectOnPlane(Vector3.up,normal);
        if(up.sqrMagnitude<.001f)up=Vector3.ProjectOnPlane(viewer.rotation*Vector3.forward,normal);
        if(up.sqrMagnitude<.001f)up=Vector3.ProjectOnPlane(viewer.rotation*Vector3.up,normal);
        if(up.sqrMagnitude<.001f)up=Vector3.ProjectOnPlane(Vector3.forward,normal);
        if(up.sqrMagnitude<.001f)up=Vector3.ProjectOnPlane(Vector3.right,normal);
        screen=new Pose(hit+normal*SurfaceOffset,Quaternion.LookRotation(-normal,up.normalized));return true;
    }
    static bool Valid(Pose p)=>Finite(p.position.sqrMagnitude)&&Finite(Quaternion.Dot(p.rotation,p.rotation))&&Quaternion.Dot(p.rotation,p.rotation)>.5f;
    static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
}

/// <summary>Selection requires a visible open hand first; holding or tracking recovery cannot auto-pin.</summary>
public sealed class PlanePinGesture
{
    bool armed,held;
    public void Reset(){armed=held=false;}
    public bool Sample(bool tracked,bool pinching)
    {
        if(!tracked){Reset();return false;}
        bool confirm=armed&&pinching&&!held;
        if(!pinching)armed=true;else if(confirm)armed=false;
        held=pinching;return confirm;
    }
}
