using System;
using UnityEngine;
using Unity.XR.XREAL;

/// <summary>Screen-normal index projection, pinch or physical contact, and depth feedback.</summary>
[DefaultExecutionOrder(100)]
public sealed class HandPhoneControl : MonoBehaviour
{
    public StabilizedScreen Screen;
    public FeatureHandRig Hands;
    public Material CursorMaterial;
    Material cursorVisual,cursorBaseVisual;
    Transform cursor,cursorBase,cursorTop,buttons;
    bool enabledByUser,lastRequested,inputRequested,awaitRelease=true,wasPinching,contact;
    int side=1,navPressed,touchMode=1,geometryVersion=-1;
    float nextSettings,nextReport,nextMove,touchDepth,touchMargin;
    readonly DirectScreenTouch directTouch=new DirectScreenTouch();
    float lastNormalDistance;
    bool nativeError;
    string status="手势操作未开启";
#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaClass bridge;
#endif
    void Start()
    {
        cursor=new GameObject("Index normal projection / distance cursor").transform;
        cursorVisual=new Material(CursorMaterial);cursorBaseVisual=new Material(CursorMaterial);cursorBaseVisual.color=new Color(.55f,.65f,.7f);
        cursorBase=CreateDot("Fixed reference disc",cursorBaseVisual);cursorTop=CreateDot("Variable distance dot",cursorVisual);cursor.gameObject.SetActive(false);
        buttons=new GameObject("Hand navigation buttons").transform;buttons.SetParent(Screen.ScreenAnchor,false);buttons.gameObject.SetActive(false);
        string[] labels={"BACK","HOME","APPS"};
        for(int i=0;i<3;i++){var text=new GameObject(labels[i]).AddComponent<TextMesh>();text.transform.SetParent(buttons,false);text.fontSize=40;text.characterSize=.0018f;text.anchor=TextAnchor.MiddleCenter;text.color=Color.cyan;text.text=labels[i];}
        XREALCallbackHandler.OnXREALError+=OnNativeError;
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge=new AndroidJavaClass("dev.xreal.stablescreen.ScreenBridge");
#endif
    }
    Transform CreateDot(string name,Material visual)
    {
        var dot=GameObject.CreatePrimitive(PrimitiveType.Sphere);Destroy(dot.GetComponent<Collider>());dot.name=name;dot.transform.SetParent(cursor,false);dot.GetComponent<Renderer>().sharedMaterial=visual;return dot.transform;
    }
    void DrawCursor(Vector2 uv,float normalMetres,Color color)
    {
        Transform image=Screen.Image.transform;Vector2 size=new Vector2(image.lossyScale.x,image.lossyScale.y);
        float diameter=.009f*Screen.ScreenDistance;cursor.SetPositionAndRotation(image.position+image.right*((uv.x-.5f)*size.x)+image.up*((uv.y-.5f)*size.y)-image.forward*(diameter*.4f),image.rotation);
        cursorBase.localScale=new Vector3(diameter,diameter,diameter*.10f);
        float top=diameter*HandScreenGeometry.DistanceRatio(normalMetres);cursorTop.localScale=new Vector3(top,top,diameter*.14f);cursorTop.localPosition=new Vector3(0,0,-diameter*.12f);
        cursorVisual.color=color;cursor.gameObject.SetActive(true);
    }
    void OnNativeError(XREALErrorCode code,string message){if(message!=null&&message.IndexOf("Hand",StringComparison.OrdinalIgnoreCase)>=0)nativeError=true;}
    void SetHands(bool requested)
    {
        Cancel();enabledByUser=false;Hands.ShowHands=false;
        try{
            if(requested){if(!XREALPlugin.IsHandTrackingSupported()){status="设备未报告手势支持";return;}HandDspCompatibility.Prepare("phone-hands");HandGraphCompatibility.Start();}
            bool accepted=XREALPlugin.SetInputSource(requested?InputSource.Hands:InputSource.None);
            enabledByUser=accepted&&requested;Hands.ShowHands=enabledByUser;status=accepted?(requested?"等待真实手部关节":"手势操作已停止"):"SDK 未接受手势切换";
        }catch(Exception e){status=e.Message;}
    }
    void Cancel(){
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge?.CallStatic("phonePointer",3,0f,0f);
#endif
        directTouch.Reset();contact=false;navPressed=0;wasPinching=false;awaitRelease=true;if(cursor)cursor.gameObject.SetActive(false);
    }
    void LateUpdate()
    {
        float now=Time.unscaledTime;
#if UNITY_ANDROID && !UNITY_EDITOR
        if(bridge==null)return;
        if(now>=nextSettings){
            nextSettings=now+.25f;bool phoneRequested=bridge.CallStatic<bool>("handControlRequested");
            bool requested=phoneRequested||bridge.CallStatic<bool>("leftHandScreenRequested")||(Screen.PlanePin&&Screen.PlanePin.NeedsHands);
            if(phoneRequested!=inputRequested){Cancel();inputRequested=phoneRequested;}
            Hands.VisualizeJoints=bridge.CallStatic<bool>("handSkeletonVisible");
            int requestedSide=bridge.CallStatic<int>("handControlSide"),requestedMode=bridge.CallStatic<int>("handTouchMode");float depth=bridge.CallStatic<float>("directTouchDepth"),margin=bridge.CallStatic<float>("directTouchMargin");
            if(requestedSide!=side||requestedMode!=touchMode||depth!=touchDepth||margin!=touchMargin){Cancel();side=requestedSide;touchMode=requestedMode;touchDepth=depth;touchMargin=margin;}
            if(requested!=lastRequested){lastRequested=requested;SetHands(requested);}
        }
#endif
        if(nativeError){nativeError=false;SetHands(false);status="手势原生错误，已停止操作";
#if UNITY_ANDROID && !UNITY_EDITOR
            bridge.CallStatic("setHandControl",false);bridge.CallStatic("setLeftHandScreen",false);
#endif
        }
        if(geometryVersion!=Screen.InputGeometryVersion){Cancel();geometryVersion=Screen.InputGeometryVersion;}
        if(touchMode==1){UpdateDirectTouch(now);return;}
        bool valid=inputRequested&&enabledByUser&&Screen.PhoneInputReady;
        bool held=false;Vector3 point=default;
        if(valid)valid=Hands.TryGetIndexPointer(side,out point,out held);
        if(now>=nextReport){nextReport=now+.2f;
#if UNITY_ANDROID && !UNITY_EDITOR
            bridge.CallStatic("updateHandControl",valid,status);
#endif
        }
        buttons.gameObject.SetActive(inputRequested&&enabledByUser&&Screen.PhoneInputReady);
        Vector2 size=new Vector2(Screen.Image.transform.lossyScale.x,Screen.Image.transform.lossyScale.y);
        float uiScale=Screen.ScreenDistance/2;
        for(int i=0;i<3;i++){buttons.GetChild(i).localScale=Vector3.one*uiScale;buttons.GetChild(i).localPosition=new Vector3((i-1)*size.x*.32f,-size.y*.5f-.055f*uiScale,-.012f*uiScale);}
        if(!valid){if(contact||navPressed!=0)Cancel();awaitRelease=true;cursor.gameObject.SetActive(false);status=inputRequested?"等待投屏、定位或手部跟踪":"手机操控未开启";return;}
        if(awaitRelease){if(held)return;awaitRelease=false;}
        Transform image=Screen.Image.transform;Vector3 local=HandScreenGeometry.Project(new Pose(image.position,image.rotation),point);
        Vector2 uv=HandScreenGeometry.Uv(local,size);
        bool inImage=uv.x>=0&&uv.x<=1&&uv.y>=0&&uv.y<=1;
        int nav=uv.x>=0&&uv.x<=1&&local.y< -size.y*.5f-.015f*uiScale&&local.y> -size.y*.5f-.10f*uiScale?Mathf.Clamp((int)(uv.x*3),0,2)+1:0;
        if(inImage||nav>0)DrawCursor(uv,local.z,held?Color.green:Color.cyan);else cursor.gameObject.SetActive(false);
        if(held&&!wasPinching){
#if UNITY_ANDROID && !UNITY_EDITOR
            bridge.CallStatic("updateHandControl",true,status);
#endif
            if(inImage){contact=true;Send(0,uv);}
            else navPressed=nav;
        }
        if(held&&contact&&now>=nextMove){nextMove=now+.04f;if(inImage)Send(1,uv);else{Cancel();awaitRelease=true;}}
        if(!held&&wasPinching){if(contact)Send(2,uv);
#if UNITY_ANDROID && !UNITY_EDITOR
            else if(navPressed>0&&navPressed==nav)bridge.CallStatic("phoneNavigation",navPressed);
#endif
            contact=false;navPressed=0;
        }
        wasPinching=held;status=contact?"食指投影：移动食指拖动，张开即松手":inImage?"食指垂直投影 · 捏合点击":"把食指投影移到画面或下方导航";
    }
    void ReportDirect(bool valid,float now,bool force=false)
    {
        if(!force&&now<nextReport)return;nextReport=now+.2f;
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge?.CallStatic("updateHandControl",valid,status);
#endif
    }
    void UpdateDirectTouch(float now)
    {
        buttons.gameObject.SetActive(false);
        if(!inputRequested||!enabledByUser||!Screen.PhoneInputReady)
        {bool ending=contact;if(ending)Cancel();directTouch.Reset();cursor.gameObject.SetActive(false);status=inputRequested?"直接触屏：等待投屏与真实定位":"手机操控未开启";ReportDirect(false,now,ending);return;}
        bool tracked=Hands.TryGetIndexTip(side,out Vector3 tip);Transform image=Screen.Image.transform;
        Vector3 local=HandScreenGeometry.Project(new Pose(image.position,image.rotation),tip);
        Vector2 size=new Vector2(image.lossyScale.x,image.lossyScale.y);
        DirectScreenTouch.Change change=directTouch.Sample(tracked,local,size,touchDepth,now,touchMargin);
        contact=directTouch.Down;
        status=directTouch.HoldingMissingSample?"直接触屏：短暂漏检，保持原触点":contact?"直接触屏：连续滑动 / 保持长按":directTouch.Armed?"直接触屏：食指触入屏幕开始操作":"直接触屏：先将食指退回屏幕前方";
        ReportDirect(tracked||directTouch.HoldingMissingSample,now,change==DirectScreenTouch.Change.Begin||change==DirectScreenTouch.Change.End);
        if(change==DirectScreenTouch.Change.Begin)Send(0,directTouch.Uv);
        else if(change==DirectScreenTouch.Change.Move&&now>=nextMove){nextMove=now+.025f;Send(1,directTouch.Uv);}
        else if(change==DirectScreenTouch.Change.End)Send(2,directTouch.Uv);
        bool hover=tracked&&Mathf.Abs(local.x)<=size.x*.5f+.04f&&Mathf.Abs(local.y)<=size.y*.5f+.04f&&local.z>=-3&&local.z<=touchDepth+.3f;
        cursor.gameObject.SetActive(hover||directTouch.HoldingMissingSample);
        if(tracked)lastNormalDistance=local.z;
        Vector2 uv=directTouch.HoldingMissingSample?directTouch.Uv:HandScreenGeometry.Uv(local,size);
        if(hover||directTouch.HoldingMissingSample)DrawCursor(uv,lastNormalDistance,directTouch.HoldingMissingSample?Color.yellow:contact?Color.green:Color.cyan);
    }
    void Send(int phase,Vector2 uv){Vector2 phone=HandInteractionMath.PhoneUv(uv,Screen.FlipVertical);
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge?.CallStatic("phonePointer",phase,phone.x,phone.y);
#endif
    }
    void OnApplicationPause(bool paused){if(paused)Cancel();}
    void OnDestroy(){Cancel();XREALCallbackHandler.OnXREALError-=OnNativeError;if(enabledByUser)try{XREALPlugin.SetInputSource(InputSource.None);}catch(Exception){}
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge?.Dispose();
#endif
        if(cursor)Destroy(cursor.gameObject);
        if(cursorVisual)Destroy(cursorVisual);
        if(cursorBaseVisual)Destroy(cursorBaseVisual);
    }
}
