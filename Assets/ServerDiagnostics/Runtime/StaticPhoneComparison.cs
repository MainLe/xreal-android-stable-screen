using System.Collections;
using UnityEngine;

/// <summary>A static screenshot and the existing cube share one world transform and viewing depth.</summary>
public sealed class StaticPhoneComparison : MonoBehaviour
{
    public Camera Head;
    public FeatureHeadPose HeadPose;
    public Transform Cube;
    public MeshRenderer Screenshot;
    public Texture2D SourceImage;
    public bool FreezeCube=true;
    public bool ImageVisible=true;
    public const float Distance=.65f;
    public const float ScreenshotHeight=.28f;
    Coroutine pending;
    bool placed;
    IEnumerator Start()
    {
        Screenshot.enabled=false;
        while(!HeadPose.RotationTracked||!HeadPose.PositionTracked)yield return null;
        Place();
    }
    public void Place()
    {
        if(!HeadPose.RotationTracked||!HeadPose.PositionTracked)return;
        var interaction=Cube.GetComponent<InertialCubeInteraction>();if(interaction)interaction.StopMotion();
        transform.SetPositionAndRotation(Head.transform.position+Head.transform.forward*Distance,Head.transform.rotation);
        Cube.localPosition=new Vector3(-.11f,0,0);Cube.localRotation=Quaternion.identity;
        Screenshot.transform.localPosition=new Vector3(.13f,0,0);Screenshot.transform.localRotation=Quaternion.identity;
        Screenshot.transform.localScale=new Vector3(ScreenshotHeight*SourceImage.width/SourceImage.height,ScreenshotHeight,1);
        placed=true;Screenshot.enabled=ImageVisible;
        Debug.Log("[ScreenshotCompare] Static cube and screenshot placed at the same depth: "+Distance+" m; texture="+SourceImage.width+"x"+SourceImage.height+"; no capture or pose compensation.");
    }
    public void CenterAfterDelay(){if(pending!=null)StopCoroutine(pending);pending=StartCoroutine(Center());}
    IEnumerator Center(){yield return new WaitForSecondsRealtime(1);Place();pending=null;}
    public void ToggleImage(){ImageVisible=!ImageVisible;Screenshot.enabled=placed&&ImageVisible;}
}
