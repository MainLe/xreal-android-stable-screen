using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.ARSubsystems;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;
using Unity.XR.XREAL;

public static class XREALFeatureDemoBuild
{
    const string Folder="Assets/ServerDiagnostics/Spatial";
    const string ScenePath=Folder+"/SpatialFeatures.unity";
    const string Output="Build/Android/XREAL-FeatureDemo.apk";
    [MenuItem("XREAL/Build Full Spatial Feature Demo APK")]
    public static void Build() { BuildInternal(false); }
    [MenuItem("XREAL/Build Hands Diagnostic APK")]
    public static void BuildHandsDiagnostic() { BuildInternal(true); }
    [MenuItem("XREAL/Build Hands Compatibility APK")]
    public static void BuildHandFix() { BuildInternal(false,true); }
    [MenuItem("XREAL/Build DSP Search Path Candidate APK")]
    public static void BuildDspProbe() { BuildInternal(false,true,true); }
    [MenuItem("XREAL/Build Runtime Stability Diagnostic APK")]
    public static void BuildRuntimeAudit() { BuildInternal(false,true,true,true); }
    [MenuItem("XREAL/Build Static Screenshot Comparison APK")]
    public static void BuildScreenshotComparison() { BuildInternal(false,true,true,true,true); }
    [MenuItem("XREAL/Build Inertial Cube Interaction APK")]
    public static void BuildInteraction() { BuildInternal(false,true,true,true,true,true); }
    static void BuildInternal(bool handsDiagnostic,bool handFix=false,bool dspProbe=false,bool runtimeAudit=false,bool screenshotComparison=false,bool interaction=false)
    {
        if(!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android,BuildTarget.Android))throw new BuildFailedException("Android target unavailable.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ValidateGestures();CreateScene(handFix,dspProbe,screenshotComparison);
        if(interaction){var demo=UnityEngine.Object.FindObjectOfType<SpatialFeatureDemo>();var cube=demo.Marker.gameObject.AddComponent<InertialCubeInteraction>();cube.Hands=demo.Hands;cube.HeadPose=demo.Pose;demo.CubeInteraction=cube;EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath);HandInteractionChecks.Run();}
        if(handFix)
        {
            const string nativePath="Assets/Plugins/Android/arm64-v8a/libXrealHandCompat.so";
            if(!File.Exists(nativePath))throw new BuildFailedException("Run ServerAppResearch/build_hand_compat.py first.");
            var importer=(PluginImporter)AssetImporter.GetAtPath(nativePath);importer.SetCompatibleWithAnyPlatform(false);importer.SetCompatibleWithEditor(false);importer.SetCompatibleWithPlatform(BuildTarget.Android,true);importer.SetPlatformData(BuildTarget.Android,"CPU","ARM64");importer.SaveAndReimport();
        }
        var settings=AssetDatabase.LoadAssetAtPath<XREALSettings>("Assets/XR/Settings/XREALSettings.asset");
        if(!settings)throw new BuildFailedException("Missing XREAL settings.");
        if(Resources.Load<TMPro.TMP_Settings>("TMP Settings")==null)throw new BuildFailedException("Missing TMP resources required by SDK.");
        string before=EditorJsonUtility.ToJson(settings),id=PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android),product=PlayerSettings.productName,version=PlayerSettings.bundleVersion;
        int versionCode=PlayerSettings.Android.bundleVersionCode;
        bool timingBefore=PlayerSettings.enableFrameTimingStats;string il2cppBefore=PlayerSettings.GetAdditionalIl2CppArgs();
        var shaderFailures=new System.Collections.Concurrent.ConcurrentQueue<string>();
        Application.LogCallback shaderLog=(condition,stack,type)=>{if(condition.IndexOf("Shader error in",StringComparison.OrdinalIgnoreCase)>=0)shaderFailures.Enqueue(condition);};
        Application.logMessageReceivedThreaded+=shaderLog;
        try
        {
            settings.InitialTrackingType=TrackingType.MODE_6DOF;settings.InitialInputSource=InputSource.None;settings.SupportMultiResume=false;
            settings.EnableAutoLogcat=false;PlayerSettings.enableFrameTimingStats=true;PlayerSettings.SetAdditionalIl2CppArgs((il2cppBefore+" --jobs=2").Trim());
            EditorBuildSettings.AddConfigObject(XREALSettings.k_SettingsKey,settings,true);XREALDemoBuild.ConfigureXRLoader();
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android,"dev.xreal.server.features");PlayerSettings.productName="XREAL Spatial Debug";
            PlayerSettings.bundleVersion=interaction?"1.1.0-interaction":screenshotComparison?"1.0.5-screen-compare":runtimeAudit?"1.0.4-runtime-audit":dspProbe?"1.0.3-dsp-path":handFix?"1.0.2-handfix":handsDiagnostic?"1.0.1-hands-debug":"1.0.0";PlayerSettings.Android.bundleVersionCode=interaction?16:screenshotComparison?15:runtimeAudit?14:dspProbe?13:handFix?12:handsDiagnostic?11:10;
            PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel29;PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android,ScriptingImplementation.IL2CPP);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[]{GraphicsDeviceType.OpenGLES3});
            EditorUserBuildSettings.buildAppBundle=false;EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Build/Android");
            File.Copy(Folder+"/ImageTarget.png","Build/Android/XREAL-ImageTarget.png",true);
            if(runtimeAudit)StableScreenMotionChecks.Run();
            var defines=runtimeAudit?new[]{"XREAL_DSP_PATH_FIX","XREAL_HAND_RUNTIME_AUDIT"}:dspProbe?new[]{"XREAL_DSP_PATH_FIX"}:Array.Empty<string>();
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=interaction?"Build/Android/XREAL-CubeInteraction-v1.1.0.apk":screenshotComparison?"Build/Android/XREAL-ScreenshotCompare-v1.0.5.apk":runtimeAudit?"Build/Android/XREAL-RuntimeAudit-v1.0.4.apk":dspProbe?"Build/Android/XREAL-DspProbe-v1.0.3.apk":handFix?"Build/Android/XREAL-HandFix-v1.0.2.apk":handsDiagnostic?"Build/Android/XREAL-HandsDebug.apk":Output,target=BuildTarget.Android,options=handsDiagnostic?BuildOptions.Development:BuildOptions.None,extraScriptingDefines=defines});
            if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Spatial demo build failed: "+report.summary.result);
            if(screenshotComparison)
            {
                foreach(var message in ShaderUtil.GetShaderMessages(Shader.Find("XREAL/Stable Phone Screen")))if(message.severity.ToString()=="Error")shaderFailures.Enqueue(message.message);
                if(!shaderFailures.IsEmpty)throw new BuildFailedException("Screenshot comparison shader failed: "+string.Join("\n",shaderFailures));
                Debug.Log("[ScreenshotCompareBuild] Static screenshot and cube packaged; shared world group and depth; GLES shader compiled without errors.");
            }
            Debug.Log("[SpatialDemoBuild] APK built: "+report.summary.outputPath+" bytes="+report.summary.totalSize);
        }
        finally
        {
            Application.logMessageReceivedThreaded-=shaderLog;
            EditorJsonUtility.FromJsonOverwrite(before,settings);EditorUtility.SetDirty(settings);
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android,id);PlayerSettings.productName=product;
            PlayerSettings.bundleVersion=version;PlayerSettings.Android.bundleVersionCode=versionCode;AssetDatabase.SaveAssets();
            PlayerSettings.enableFrameTimingStats=timingBefore;PlayerSettings.SetAdditionalIl2CppArgs(il2cppBefore);AssetDatabase.SaveAssets();
        }
    }
    static Material Material(string name,Color color)
    {
        string path=Folder+"/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){var shader=Shader.Find("Unlit/Color")??Shader.Find("Universal Render Pipeline/Unlit");if(!shader)throw new BuildFailedException("Missing unlit shader.");material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
        material.color=color;EditorUtility.SetDirty(material);return material;
    }
    static void CreateScene(bool handFix,bool dspProbe,bool screenshotComparison)
    {
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var session=new GameObject("AR Session");session.AddComponent<ARSession>();session.AddComponent<ARInputManager>();
        var originObject=new GameObject("XR Origin");var origin=originObject.AddComponent<XROrigin>();origin.CameraYOffset=0;
        origin.RequestedTrackingOriginMode=XROrigin.TrackingOriginMode.Device;
        var offset=new GameObject("Camera Offset");offset.transform.SetParent(originObject.transform,false);origin.CameraFloorOffsetObject=offset;
        var head=new GameObject("Head Camera");head.transform.SetParent(offset.transform,false);head.tag="MainCamera";
        var camera=head.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0.008f,0.012f,0.018f);camera.nearClipPlane=0.05f;camera.farClipPlane=50;
        origin.Camera=camera;var pose=head.AddComponent<FeatureHeadPose>();
        var anchors=originObject.AddComponent<ARAnchorManager>();var planes=originObject.AddComponent<ARPlaneManager>();planes.requestedDetectionMode=PlaneDetectionMode.Horizontal|PlaneDetectionMode.Vertical;planes.enabled=false;
        var meshObject=new GameObject("Environment meshes");meshObject.transform.SetParent(originObject.transform,false);
        var meshes=meshObject.AddComponent<ARMeshManager>();meshes.enabled=false;meshes.density=0.35f;
        var meshTemplate=new GameObject("Environment mesh");var filter=meshTemplate.AddComponent<MeshFilter>();
        meshTemplate.AddComponent<MeshRenderer>().sharedMaterial=Material("Mesh",new Color(0.12f,0.35f,0.4f));
        var meshPrefab=PrefabUtility.SaveAsPrefabAsset(meshTemplate,Folder+"/EnvironmentMesh.prefab");UnityEngine.Object.DestroyImmediate(meshTemplate);meshes.meshPrefab=meshPrefab.GetComponent<MeshFilter>();
        var images=originObject.AddComponent<ARTrackedImageManager>();images.enabled=false;images.referenceLibrary=CreateImageLibrary();images.requestedMaxNumberOfMovingImages=1;
        var cameraFeed=head.AddComponent<ARCameraManager>();cameraFeed.enabled=false;
        var demo=new GameObject("FeatureDemo").AddComponent<SpatialFeatureDemo>();demo.EnableHandCompatibility=handFix;demo.EnableHandDspCompatibility=dspProbe;demo.Origin=origin;demo.Head=camera;demo.Pose=pose;demo.Anchors=anchors;demo.Planes=planes;demo.Meshes=meshes;demo.Images=images;demo.CameraFeed=cameraFeed;
        demo.MarkerMaterial=Material("Marker",Color.cyan);demo.AnchorMaterial=Material("Anchor",Color.yellow);demo.PlaneMaterial=Material("Plane",Color.green);demo.ImageMaterial=Material("Image",Color.magenta);
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name="Pinch interaction cube";UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());cube.transform.position=new Vector3(0,-0.15f,0.65f);cube.transform.localScale=Vector3.one*0.1f;cube.GetComponent<Renderer>().sharedMaterial=demo.MarkerMaterial;demo.Marker=cube.transform;
        if(screenshotComparison)CreateScreenshotComparison(demo,camera,pose,cube.transform);
        var hands=new GameObject("Hand visualization").AddComponent<FeatureHandRig>();hands.TrackingOrigin=offset.transform;hands.LeftMaterial=Material("LeftHand",new Color(0.2f,1,0.7f));hands.RightMaterial=Material("RightHand",new Color(1,0.4f,0.7f));demo.Hands=hands;
        var hud=new GameObject("Head status").AddComponent<TextMesh>();hud.transform.SetParent(head.transform,false);hud.transform.localPosition=new Vector3(-0.44f,0.3f,1.5f);
        hud.characterSize=0.0038f;hud.fontSize=36;hud.anchor=TextAnchor.UpperLeft;hud.color=Color.white;hud.text="XREAL Spatial Debug";demo.Hud=hud;
        EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
        if(anchors.transform!=origin.transform||camera.transform.parent!=offset.transform||meshes.meshPrefab==null||images.referenceLibrary.count!=1)throw new BuildFailedException("Spatial scene validation failed.");
    }
    static void CreateScreenshotComparison(SpatialFeatureDemo demo,Camera head,FeatureHeadPose pose,Transform cube)
    {
        const string screenshotPath=Folder+"/PhoneScreenshot.png";
        if(!File.Exists(screenshotPath))throw new BuildFailedException("Missing static phone screenshot");
        var importer=(TextureImporter)AssetImporter.GetAtPath(screenshotPath);
        importer.textureType=TextureImporterType.Default;importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=4096;
        importer.sRGBTexture=true;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.isReadable=false;importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;
        var android=importer.GetPlatformTextureSettings("Android");android.overridden=true;android.maxTextureSize=4096;android.format=TextureImporterFormat.RGBA32;importer.SetPlatformTextureSettings(android);importer.SaveAndReimport();
        var image=AssetDatabase.LoadAssetAtPath<Texture2D>(screenshotPath);Shader shader=Shader.Find("XREAL/Stable Phone Screen");if(!image||!shader)throw new BuildFailedException("Screenshot texture or stereo shader unavailable");
        const string materialPath=Folder+"/ScreenshotComparison.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,materialPath);}material.shader=shader;material.SetTexture("_MainTex",image);material.SetTextureScale("_MainTex",Vector2.one);material.SetTextureOffset("_MainTex",Vector2.zero);EditorUtility.SetDirty(material);
        var root=new GameObject("World-fixed screenshot and cube");root.transform.position=new Vector3(0,0,StaticPhoneComparison.Distance);
        cube.SetParent(root.transform,true);cube.localPosition=new Vector3(-.11f,0,0);
        var panel=GameObject.CreatePrimitive(PrimitiveType.Quad);panel.name="Static phone screenshot";UnityEngine.Object.DestroyImmediate(panel.GetComponent<Collider>());panel.transform.SetParent(root.transform,false);panel.transform.localPosition=new Vector3(.13f,0,0);panel.transform.localScale=new Vector3(StaticPhoneComparison.ScreenshotHeight*image.width/image.height,StaticPhoneComparison.ScreenshotHeight,1);panel.GetComponent<MeshRenderer>().sharedMaterial=material;
        var compare=root.AddComponent<StaticPhoneComparison>();compare.Head=head;compare.HeadPose=pose;compare.Cube=cube;compare.Screenshot=panel.GetComponent<MeshRenderer>();compare.SourceImage=image;demo.ScreenshotComparison=compare;
        if(cube.parent!=panel.transform.parent||Mathf.Abs(cube.localPosition.z-panel.transform.localPosition.z)>.00001f)throw new BuildFailedException("Comparison targets must share a world parent and depth");
    }
    static XRReferenceImageLibrary CreateImageLibrary()
    {
        string imagePath=Folder+"/ImageTarget.png";
        if(!File.Exists(imagePath))
        {
            // Asymmetric high contrast calibration card, deterministic and free of external assets.
            const int size=768;var texture=new Texture2D(size,size,TextureFormat.RGB24,false);var pixels=new Color32[size*size];
            for(int i=0;i<pixels.Length;i++)pixels[i]=Color.white;
            var random=new System.Random(72931);
            for(int n=0;n<380;n++)
            {
                int x=random.Next(24,size-80),y=random.Next(24,size-80),w=random.Next(8,70),h=random.Next(8,70);
                var color=new Color32((byte)random.Next(10,140),(byte)random.Next(10,140),(byte)random.Next(10,140),255);
                for(int yy=y;yy<y+h;yy++)for(int xx=x;xx<x+w;xx++)if(n%3!=0||(xx-x)*(xx-x)+(yy-y)*(yy-y)<w*h/2)pixels[yy*size+xx]=color;
            }
            for(int y=8;y<size-8;y++)for(int x=8;x<size-8;x++)if(x<14||y<14||x>=size-14||y>=size-14)pixels[y*size+x]=Color.black;
            texture.SetPixels32(pixels);texture.Apply();File.WriteAllBytes(imagePath,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(imagePath,ImportAssetOptions.ForceSynchronousImport);
        }
        var importer=(TextureImporter)AssetImporter.GetAtPath(imagePath);importer.isReadable=true;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=false;importer.SaveAndReimport();
        string path=Folder+"/ImageLibrary.asset";var library=AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(path);
        if(!library){library=ScriptableObject.CreateInstance<XRReferenceImageLibrary>();AssetDatabase.CreateAsset(library,path);}
        if(library.count==0)library.Add();library.SetName(0,"XREAL Debug Target 20cm");library.SetTexture(0,AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath),true);library.SetSpecifySize(0,true);library.SetSize(0,new Vector2(0.2f,0.2f));EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();return library;
    }
    static void ValidateGestures()
    {
        if(FeatureHandRig.PinchState(false,0.03f)||!FeatureHandRig.PinchState(true,0.03f)||!FeatureHandRig.PinchState(false,0.02f)||FeatureHandRig.PinchState(true,0.04f)||FeatureHandRig.PinchState(true,float.NaN)||FeatureHandRig.PinchState(false,-1))
            throw new BuildFailedException("Pinch hysteresis / invalid-sample validation failed.");
        Debug.Log("[SpatialDemoBuild] Pinch hysteresis and invalid-sample checks passed.");
    }
}
