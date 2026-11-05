using UnityEditor.Build;
using UnityEngine;

public static class DirectScreenTouchChecks
{
    static void Require(bool value,string message){if(!value)throw new BuildFailedException(message);}
    public static void Run()
    {
        var touch=new DirectScreenTouch();Vector2 size=new Vector2(.20f,.30f);float depth=.08f;
        Require(LegacySample(touch,true,new Vector3(0,0,.04f),size,depth,0)==DirectScreenTouch.Change.None,"Finger appearing behind screen generated a click");
        LegacySample(touch,true,new Vector3(0,0,-.06f),size,depth,.01f);
        Require(LegacySample(touch,true,Vector3.zero,size,depth,.02f)==DirectScreenTouch.Change.Begin,"Entering screen plane did not begin contact");
        Require(LegacySample(touch,true,new Vector3(.05f,.04f,.07f),size,depth,.04f)==DirectScreenTouch.Change.Move&&touch.Down,"Rear thickness did not preserve stroke");
        Require(Vector2.Distance(touch.Uv,new Vector2(.75f,.6333333f))<.0001f,"Direct fingertip UV mapping failed");
        Require(LegacySample(touch,true,new Vector3(.105f,0,-.01f),size,depth,.06f)==DirectScreenTouch.Change.Move&&touch.Uv.x==1,"Edge/front hysteresis broke continuous stroke");
        Require(LegacySample(touch,true,new Vector3(0,0,.10f),size,depth,.08f)==DirectScreenTouch.Change.Move,"Rear hysteresis released too soon");
        Require(LegacySample(touch,false,Vector3.zero,size,depth,.14f)==DirectScreenTouch.Change.Move&&touch.HoldingMissingSample,"Short tracking gap broke stroke");
        Require(LegacySample(touch,true,new Vector3(.02f,0,.06f),size,depth,.15f)==DirectScreenTouch.Change.Move&&!touch.HoldingMissingSample,"Tracking recovery restarted stroke");
        Require(LegacySample(touch,false,Vector3.zero,size,depth,.27f)==DirectScreenTouch.Change.End&&!touch.Down,"Stale tracking held contact indefinitely");
        Require(LegacySample(touch,true,new Vector3(0,0,.03f),size,depth,.28f)==DirectScreenTouch.Change.None,"Tracking recovery clicked without front withdrawal");
        LegacySample(touch,true,new Vector3(0,0,-.04f),size,depth,.30f);
        Require(LegacySample(touch,true,new Vector3(0,0,.01f),size,depth,.32f)==DirectScreenTouch.Change.Begin,"Front withdrawal did not rearm");
        Require(LegacySample(touch,true,new Vector3(0,0,.12f),size,depth,.34f)==DirectScreenTouch.Change.End,"Exited rear volume did not release");
        touch.Reset();LegacySample(touch,true,new Vector3(0,0,-.04f),size,depth,1);
        int starts=0,ends=0;
        for(int i=0;i<90;i++){
            float z=i==89?-.04f:(i%2==0?.01f:.079f);float x=Mathf.Lerp(-.08f,.08f,i/89f);
            var change=LegacySample(touch,true,new Vector3(x,0,z),size,depth,1.02f+i/60f);
            if(change==DirectScreenTouch.Change.Begin)starts++;if(change==DirectScreenTouch.Change.End)ends++;
        }
        Require(starts==1&&ends==1,"Rear-depth sliding split into multiple gestures");
        // New zero/minimal range behavior: no implicit centimetres of hold expansion.
        var strict=new DirectScreenTouch();
        Require(strict.Sample(true,new Vector3(0,0,.004f),size,0,2)==DirectScreenTouch.Change.None,"Initial point behind zero plane clicked");
        strict.Sample(true,new Vector3(0,0,-.006f),size,0,2.02f);
        Require(strict.Sample(true,Vector3.zero,size,0,2.04f)==DirectScreenTouch.Change.Begin,"Exact zero plane cannot be touched");
        Require(strict.Sample(true,new Vector3(.03f,.04f,0),size,0,2.06f)==DirectScreenTouch.Change.Move,"Zero plane cannot slide along surface");
        Require(strict.Sample(true,new Vector3(.03f,.04f,.0001f),size,0,2.08f)==DirectScreenTouch.Change.End,"Zero range retained hidden rear tolerance");
        strict.Reset();strict.Sample(true,new Vector3(-.01f,0,-.01f),size,0,3);
        Require(strict.Sample(true,new Vector3(.01f,0,.01f),size,0,3.02f)==DirectScreenTouch.Change.Begin&&Vector2.Distance(strict.Uv,new Vector2(.5f,.5f))<.00001f,"Sampled plane crossing missed or used wrong crossing UV");
        Require(strict.Sample(false,Vector3.zero,size,0,3.04f)==DirectScreenTouch.Change.End&&!strict.Down,"Zero-thickness pulse stayed down during tracking loss");
        Require(strict.Sample(true,new Vector3(0,0,.01f),size,0,3.06f)==DirectScreenTouch.Change.None,"Plane pulse rearmed behind screen");
        strict.Reset();strict.Sample(true,new Vector3(0,0,-.006f),size,0,3.2f,.002f);
        Require(strict.Sample(true,new Vector3(0,0,.001f),size,0,3.22f,.002f)==DirectScreenTouch.Change.Begin,"Zero plane with configured margin missed crossing");
        Require(strict.Sample(true,new Vector3(.03f,0,.001f),size,0,3.24f,.002f)==DirectScreenTouch.Change.Move,"Configured margin forced a brief pulse instead of continuous sliding");
        Require(strict.Sample(true,new Vector3(.03f,0,.0021f),size,0,3.26f,.002f)==DirectScreenTouch.Change.End,"Zero plane margin did not enforce its configured limit");
        strict.Reset();strict.Sample(true,new Vector3(0,0,-.006f),size,.01f,4);
        strict.Sample(true,new Vector3(0,0,.005f),size,.01f,4.02f);
        Require(strict.Sample(true,new Vector3(.1001f,0,.005f),size,.01f,4.04f)==DirectScreenTouch.Change.End,"Zero margin retained hidden edge tolerance");
        strict.Reset();strict.Sample(true,new Vector3(0,0,-.006f),size,.01f,5);
        strict.Sample(true,new Vector3(0,0,.005f),size,.01f,5.02f);
        Require(strict.Sample(true,new Vector3(0,0,-.0001f),size,.01f,5.04f)==DirectScreenTouch.Change.End,"Zero margin retained hidden front tolerance");
        var tight=new DirectScreenTouch();tight.Sample(true,new Vector3(0,0,-.006f),size,.01f,6,.002f);
        tight.Sample(true,new Vector3(0,0,.003f),size,.01f,6.02f,.002f);
        Require(tight.Sample(true,new Vector3(.101f,0,-.001f),size,.01f,6.04f,.002f)==DirectScreenTouch.Change.Move,"Configured millimetre margin not applied");
        Require(tight.Sample(true,new Vector3(0,0,.0119f),size,.01f,6.06f,.002f)==DirectScreenTouch.Change.Move,"Rear margin released too early");
        Require(tight.Sample(true,new Vector3(0,0,.0121f),size,.01f,6.08f,.002f)==DirectScreenTouch.Change.End,"Configured rear limit exceeded");
        strict.Reset();strict.Sample(true,new Vector3(0,0,-.006f),size,0,7);
        Require(strict.Sample(true,new Vector3(0,0,.3f),size,0,7.02f)==DirectScreenTouch.Change.None,"Teleport triggered swept touch");
        strict.Reset();strict.Sample(true,new Vector3(0,0,-.006f),size,0,8);
        Require(strict.Sample(true,new Vector3(0,0,.003f),size,0,8.3f)==DirectScreenTouch.Change.None,"Stale sample triggered swept touch");
        strict.Reset();strict.Sample(true,new Vector3(0,0,-.006f),size,0,9);strict.Sample(true,Vector3.zero,size,0,9.02f);
        Require(strict.Sample(true,Vector3.zero,size,0,9.04f,float.NaN)==DirectScreenTouch.Change.End,"Invalid tolerance kept contact alive");
        Debug.Log("[TouchRangeChecks] Zero plane/swept pulse, exact front/rear/edge limits, configurable millimetre margin, rearm and stale/teleport rejection passed.");
        foreach(float distance in new[]{.25f,.45f,.60f,2f,5f}){
            var near=StabilizedScreen.FitSize(9f/20,1,distance);var baseline=StabilizedScreen.FitSize(9f/20,1,2);
            Require(Vector2.Distance(near/distance,baseline/2)<.00001f,"Distance adjustment changed viewing angle");
        }
        var anchor=new Pose(new Vector3(2,3,4),Quaternion.Euler(0,90,0));var moved=StabilizedScreen.ShiftDistance(anchor,2,.45f);
        Require(Vector3.Distance(moved.position,new Vector3(.45f,3,4))<.0001f&&Mathf.Abs(Quaternion.Dot(moved.rotation,anchor.rotation))>.99999f,"Distance shift changed orientation or wrong axis");
        Debug.Log("[DirectTouchChecks] Rear-volume continuous stroke, hysteresis, tracking grace/rearm, UV, distance angle and rotated-plane checks passed.");
    }
    static DirectScreenTouch.Change LegacySample(DirectScreenTouch touch,bool tracked,Vector3 local,Vector2 size,float depth,float now)=>touch.Sample(tracked,local,size,depth,now,.03f);
}
