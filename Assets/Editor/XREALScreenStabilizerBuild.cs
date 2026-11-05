using System;
using System.IO;
using System.Linq;
using System.Xml;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.XR.XREAL;
using Unity.XR.CoreUtils;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public static class XREALScreenStabilizerBuild
{
    public const string Id="dev.xreal.stablescreen";
    const string Folder="Assets/ScreenStabilizer";
    const string Library=Folder+"/Plugins/ScreenMirror.androidlib";
    [MenuItem("XREAL/Build Stable Phone Screen APK")]
    public static void Build()
    {
        if(!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android,BuildTarget.Android))throw new BuildFailedException("Android unavailable");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);ValidatePixelsAndGeometry();
        if(!File.Exists("ServerAppResearch/build/screen-stabilizer/GlassesDisplayPlugEvent-2.4.2.aar"))throw new BuildFailedException("Run prepare_screen_stabilizer.py first.");
        var plugin=(PluginImporter)AssetImporter.GetAtPath(Library);if(plugin==null)throw new BuildFailedException("Missing capture Android library importer");
        bool pluginBefore=plugin.GetCompatibleWithPlatform(BuildTarget.Android);
        var settings=AssetDatabase.LoadAssetAtPath<XREALSettings>("Assets/XR/Settings/XREALSettings.asset");
        string settingsBefore=EditorJsonUtility.ToJson(settings),idBefore=PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android),nameBefore=PlayerSettings.productName,versionBefore=PlayerSettings.bundleVersion;
        int codeBefore=PlayerSettings.Android.bundleVersionCode;
        var minBefore=PlayerSettings.Android.minSdkVersion;var targetBefore=PlayerSettings.Android.targetSdkVersion;var archBefore=PlayerSettings.Android.targetArchitectures;
        var backendBefore=PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android);var gfxBefore=PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);bool defaultGfxBefore=PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android);
        var orientationBefore=PlayerSettings.defaultInterfaceOrientation;bool backgroundBefore=PlayerSettings.runInBackground,bundleBefore=EditorUserBuildSettings.buildAppBundle;
        string il2CppArgsBefore=PlayerSettings.GetAdditionalIl2CppArgs();bool timingBefore=PlayerSettings.enableFrameTimingStats;
        var shaderFailures=new System.Collections.Concurrent.ConcurrentQueue<string>();
        Application.LogCallback shaderLog=(condition,stack,type)=>{if(condition.IndexOf("Shader error in",StringComparison.OrdinalIgnoreCase)>=0)shaderFailures.Enqueue(condition);};
        Application.logMessageReceivedThreaded+=shaderLog;
        try
        {
            plugin.SetCompatibleWithAnyPlatform(false);plugin.SetCompatibleWithEditor(false);plugin.SetCompatibleWithPlatform(BuildTarget.Android,true);plugin.SaveAndReimport();
            settings.InitialTrackingType=TrackingType.MODE_3DOF;settings.InitialInputSource=InputSource.None;settings.SupportMultiResume=false;settings.EnableAutoLogcat=false;
            EditorBuildSettings.AddConfigObject(XREALSettings.k_SettingsKey,settings,true);XREALDemoBuild.ConfigureXRLoader();
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android,Id);PlayerSettings.productName="XREAL 稳屏";PlayerSettings.bundleVersion="0.8.1";PlayerSettings.Android.bundleVersionCode=17;
            PlayerSettings.enableFrameTimingStats=true;
            PlayerSettings.Android.minSdkVersion=(AndroidSdkVersions)32;PlayerSettings.Android.targetSdkVersion=(AndroidSdkVersions)35;PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android,ScriptingImplementation.IL2CPP);PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[]{GraphicsDeviceType.OpenGLES3});
            PlayerSettings.SetAdditionalIl2CppArgs((il2CppArgsBefore+" --jobs=2").Trim());
            PlayerSettings.defaultInterfaceOrientation=UIOrientation.Portrait;PlayerSettings.runInBackground=true;EditorUserBuildSettings.buildAppBundle=false;
            string scene=CreateScene();EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();Directory.CreateDirectory("Build/Android");
            StableScreenMotionChecks.Run();
            HandInteractionChecks.Run();
            DirectScreenTouchChecks.Run();
            HandScreenExperienceChecks.Run();
            PlanePinChecks.Run();
            PlaneAnchorLifecycleChecks.Run();
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene},locationPathName="Build/Android/XREAL-StableScreen-v0.8.1.apk",target=BuildTarget.Android,options=BuildOptions.None,extraScriptingDefines=new[]{"XREAL_DSP_PATH_FIX"}});
            if(result.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Stable Screen build failed: "+result.summary.result);
            foreach(string path in new[]{Folder+"/Shaders/PhoneScreen.shader",Folder+"/Shaders/CalibrationGrid.shader",Folder+"/Shaders/HandOverlay.shader"})
                foreach(var message in ShaderUtil.GetShaderMessages(AssetDatabase.LoadAssetAtPath<Shader>(path)))
                    if(message.severity.ToString()=="Error")shaderFailures.Enqueue(path+": "+message.message);
            if(!shaderFailures.IsEmpty)throw new BuildFailedException("Stable Screen shader compilation failed: "+string.Join("\n",shaderFailures));
            Debug.Log("[StableScreenBuild] Phone and calibration GLES shaders compiled without errors.");
            Debug.Log("[HandScreenBuild] Transparent hand overlay GLES shader compiled without errors.");
            Debug.Log("[StableScreenBuild] APK built: "+result.summary.outputPath+" bytes="+result.summary.totalSize);
        }
        finally
        {
            Application.logMessageReceivedThreaded-=shaderLog;
            EditorJsonUtility.FromJsonOverwrite(settingsBefore,settings);EditorUtility.SetDirty(settings);
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android,idBefore);PlayerSettings.productName=nameBefore;PlayerSettings.bundleVersion=versionBefore;PlayerSettings.Android.bundleVersionCode=codeBefore;
            PlayerSettings.Android.minSdkVersion=minBefore;PlayerSettings.Android.targetSdkVersion=targetBefore;PlayerSettings.Android.targetArchitectures=archBefore;PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android,backendBefore);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,gfxBefore);PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,defaultGfxBefore);PlayerSettings.defaultInterfaceOrientation=orientationBefore;PlayerSettings.runInBackground=backgroundBefore;EditorUserBuildSettings.buildAppBundle=bundleBefore;
            PlayerSettings.SetAdditionalIl2CppArgs(il2CppArgsBefore);
            PlayerSettings.enableFrameTimingStats=timingBefore;
            plugin.SetCompatibleWithPlatform(BuildTarget.Android,pluginBefore);plugin.SaveAndReimport();AssetDatabase.SaveAssets();
        }
    }
    static Material GetMaterial(string name,Shader shader,Color color)
    {
        string path=Folder+"/Materials/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}if(material.HasProperty("_Color"))material.color=color;EditorUtility.SetDirty(material);return material;
    }
    static string CreateScene()
    {
        Directory.CreateDirectory(Folder+"/Materials");Directory.CreateDirectory(Folder+"/Scenes");AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var session=new GameObject("AR tracking session");session.AddComponent<UnityEngine.XR.ARFoundation.ARSession>();session.AddComponent<UnityEngine.XR.ARFoundation.ARInputManager>();
        var originObject=new GameObject("Stable XR Origin");var origin=originObject.AddComponent<XROrigin>();origin.CameraYOffset=0;origin.RequestedTrackingOriginMode=XROrigin.TrackingOriginMode.Device;
        var cameraOffset=new GameObject("Stable camera offset");cameraOffset.transform.SetParent(originObject.transform,false);origin.CameraFloorOffsetObject=cameraOffset;
        var cameraObject=new GameObject("3DoF Head Camera");cameraObject.transform.SetParent(cameraOffset.transform,false);cameraObject.tag="MainCamera";var camera=cameraObject.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.nearClipPlane=.05f;camera.farClipPlane=10;origin.Camera=camera;
        var planes=originObject.AddComponent<ARPlaneManager>();planes.enabled=false;planes.requestedDetectionMode=PlaneDetectionMode.None;
        var anchors=originObject.AddComponent<ARAnchorManager>();anchors.enabled=false;
        var controller=new GameObject("Stabilizer").AddComponent<StabilizedScreen>();controller.Head=camera;
        controller.ScreenAnchor=new GameObject("World fixed phone screen").transform;controller.ScreenAnchor.position=new Vector3(0,0,2);
        Shader screenShader=Shader.Find("XREAL/Stable Phone Screen"),colorShader=Shader.Find("Unlit/Color");if(!screenShader||!colorShader)throw new BuildFailedException("Screen shaders unavailable");
        var screen=GameObject.CreatePrimitive(PrimitiveType.Quad);screen.name="Phone image";UnityEngine.Object.DestroyImmediate(screen.GetComponent<Collider>());screen.transform.SetParent(controller.ScreenAnchor,false);controller.Image=screen.GetComponent<MeshRenderer>();controller.Image.sharedMaterial=GetMaterial("PhoneScreen",screenShader,Color.white);
        Shader gridShader=Shader.Find("XREAL/Stable Calibration Grid");if(!gridShader)throw new BuildFailedException("Calibration shader unavailable");controller.ReferenceMaterial=GetMaterial("CalibrationGrid",gridShader,Color.white);
        var bezel=GameObject.CreatePrimitive(PrimitiveType.Quad);bezel.name="Screen bezel";UnityEngine.Object.DestroyImmediate(bezel.GetComponent<Collider>());bezel.transform.SetParent(controller.ScreenAnchor,false);bezel.transform.localPosition=new Vector3(0,0,.01f);controller.Bezel=bezel.GetComponent<MeshRenderer>();controller.Bezel.sharedMaterial=GetMaterial("Bezel",colorShader,new Color(.045f,.08f,.1f));
        var message=new GameObject("Status message").AddComponent<TextMesh>();message.transform.SetParent(cameraObject.transform,false);message.transform.localPosition=new Vector3(0,0,1.8f);message.fontSize=36;message.characterSize=.004f;message.anchor=TextAnchor.MiddleCenter;message.alignment=TextAlignment.Center;message.color=new Color(.45f,1,.8f);message.text="XREAL STABLE SCREEN";controller.Message=message;
        var handOrigin=cameraOffset;
        var hands=new GameObject("Phone gesture hands").AddComponent<FeatureHandRig>();hands.TrackingOrigin=handOrigin.transform;hands.VisualizeJoints=true;hands.VisualizeJointMarkers=false;hands.BoneWidth=.0013f;
        Shader handShader=Shader.Find("XREAL/Stable Hand Overlay");if(!handShader)throw new BuildFailedException("Transparent hand shader unavailable");
        hands.LeftMaterial=GetMaterial("PhoneLeftHand",handShader,new Color(.35f,.75f,1,.16f));hands.RightMaterial=GetMaterial("PhoneRightHand",handShader,new Color(.4f,1,.75f,.16f));controller.Hands=hands;
        var phoneHands=controller.gameObject.AddComponent<HandPhoneControl>();phoneHands.Screen=controller;phoneHands.Hands=hands;phoneHands.CursorMaterial=GetMaterial("PhoneHandCursor",colorShader,Color.cyan);
        var pin=controller.gameObject.AddComponent<PlaneScreenPin>();pin.Screen=controller;pin.Planes=planes;pin.Anchors=anchors;pin.Origin=origin;pin.BoundaryMaterial=GetMaterial("DetectedPlane",colorShader,new Color(.2f,.8f,1));pin.SelectedMaterial=GetMaterial("SelectedPlane",colorShader,new Color(1,.8f,.2f));controller.PlanePin=pin;
        string path=Folder+"/Scenes/StableScreen.unity";EditorSceneManager.SaveScene(scene,path);AssetDatabase.SaveAssets();
        if(controller.ScreenAnchor.parent!=null||screen.transform.parent==cameraObject.transform)throw new BuildFailedException("Screen must stay in world space");
        if(origin.Camera!=camera||origin.transform.position!=Vector3.zero||cameraOffset.transform.localPosition!=Vector3.zero||planes.enabled||anchors.enabled||hands.TrackingOrigin!=cameraOffset.transform||pin.Origin!=origin)throw new BuildFailedException("Plane pin origin/idle-module wiring invalid");
        Debug.Log("[PlanePinBuild] Identity XR origin, shared hand/camera space and idle plane/anchor managers configured.");return path;
    }
    static void ValidatePixelsAndGeometry()
    {
        using(var src=new NativeArray<byte>(Enumerable.Range(0,28).Select(i=>(byte)i).ToArray(),Allocator.Temp))using(var dst=new NativeArray<byte>(24,Allocator.Temp))
        {
            if(!MirrorFrameCopy.Copy(src,28,3,2,16,4,dst))throw new BuildFailedException("Padded RGBA copy rejected");
            for(int i=0;i<24;i++)if(dst[i]!=(byte)(i<12?i:i+4))throw new BuildFailedException("Row padding leaked into texture");
            if(MirrorFrameCopy.Copy(src,27,3,2,16,4,dst)||MirrorFrameCopy.Copy(src,28,3,2,16,3,dst))throw new BuildFailedException("Invalid image layout accepted");
        }
        foreach(float ratio in new[]{.45f,1f,2.2f}){var size=StabilizedScreen.FitSize(ratio,1);if(Mathf.Abs(size.x/size.y-ratio)>.0001f||size.x>1.456f||size.y>.778f)throw new BuildFailedException("Aspect / viewing bounds invalid");}
        var yaw=Quaternion.Euler(0,90,0);if(Vector3.Distance(yaw*(Vector3.forward*2),new Vector3(2,0,0))>.0001f)throw new BuildFailedException("Recenter orientation invalid");
        Debug.Log("[StableScreenBuild] Padded / truncated frame, aspect and recenter checks passed.");
    }
}

public sealed class StableScreenGradleSetup : IPostGenerateGradleAndroidProject
{
    public int callbackOrder=>9999;
    public void OnPostGenerateGradleAndroidProject(string path)
    {
        if(PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android)!=XREALScreenStabilizerBuild.Id)return;
        string prepared=Path.GetFullPath("ServerAppResearch/build/screen-stabilizer/GlassesDisplayPlugEvent-2.4.2.aar");
        var copies=Directory.GetFiles(path,"GlassesDisplayPlugEvent-2.4.2.aar",SearchOption.AllDirectories);if(copies.Length==0)throw new BuildFailedException("Client display helper AAR not found");foreach(string target in copies)File.Copy(prepared,target,true);
        string compileOnly=Path.Combine(path,"ScreenMirror.androidlib/compileOnly");Directory.CreateDirectory(compileOnly);
        File.Copy(Path.GetFullPath("ServerAppResearch/build/screen-stabilizer/original-display.jar"),Path.Combine(compileOnly,"display-helper.jar"),true);
        File.Copy(Path.GetFullPath("Assets/ScreenStabilizer/Plugins/ScreenMirror.androidlib/build.gradle"),Path.Combine(path,"ScreenMirror.androidlib/build.gradle"),true);
        string manifest=Path.Combine(path,"src/main/AndroidManifest.xml");var xml=new XmlDocument();xml.Load(manifest);const string android="http://schemas.android.com/apk/res/android",tools="http://schemas.android.com/tools";
        var application=xml.DocumentElement.SelectSingleNode("application");
        foreach(XmlElement prior in application.SelectNodes("activity").Cast<XmlElement>().Where(a=>new[]{"ai.nreal.activitylife.NRXRActivity","dev.xreal.stablescreen.ControlActivity","ai.nreal.activitylife.UnityPlayerActivity","ai.nreal.activitylife.NRFakeActivity"}.Contains(a.GetAttribute("name",android))).ToArray())application.RemoveChild(prior);
        foreach(XmlElement activity in application.SelectNodes("activity"))
            foreach(XmlElement filter in activity.SelectNodes("intent-filter").Cast<XmlElement>().ToArray())
                if(filter.SelectNodes("action").Cast<XmlElement>().Any(a=>a.GetAttribute("name",android)=="android.intent.action.MAIN"))activity.RemoveChild(filter);
        var entry=xml.CreateElement("activity");entry.SetAttribute("name",android,"dev.xreal.stablescreen.ControlActivity");entry.SetAttribute("exported",android,"true");var intent=xml.CreateElement("intent-filter");
        var action=xml.CreateElement("action");action.SetAttribute("name",android,"android.intent.action.MAIN");var category=xml.CreateElement("category");category.SetAttribute("name",android,"android.intent.category.LAUNCHER");intent.AppendChild(action);intent.AppendChild(category);entry.AppendChild(intent);application.AppendChild(entry);
        xml.Save(manifest);Debug.Log("[StableScreenBuild] Client display helper patched; phone control launcher configured.");
        Debug.Log("[StableScreenBuild] SDK owns the sole eye Presentation; offscreen Unity primary sink; SDK Multi Resume disabled for this app only.");
        string properties=Path.GetFullPath(Path.Combine(path,"../gradle.properties"));
        if(File.Exists(properties))
        {
            string data=File.ReadAllText(properties);
            data=System.Text.RegularExpressions.Regex.Replace(data,@"(?m)^org\.gradle\.workers\.max=.*\r?$","");
            File.WriteAllText(properties,data+"\norg.gradle.workers.max=2\n");
        }
    }
    static string IdSuffix(string suffix)=>XREALScreenStabilizerBuild.Id+"."+suffix;
}
