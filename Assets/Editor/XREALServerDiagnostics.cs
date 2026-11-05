using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.XR.XREAL;

public static class XREALServerDiagnostics
{
    [MenuItem("XREAL/Build Server 3DoF Diagnostic APK")]
    public static void Build()
    {
        const string scenePath = "Assets/ServerDiagnostics/Server3DoF.unity";
        const string output = "Build/Android/XREAL-Server3DoF.apk";
        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            throw new InvalidOperationException("Could not select Android.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (Resources.Load<TMPro.TMP_Settings>("TMP Settings") == null)
            throw new InvalidOperationException("TMP essential resources have not been imported.");
        System.IO.Directory.CreateDirectory("Assets/ServerDiagnostics/Materials");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cameraObject = new GameObject("3DoF Head Camera");
        var camera = cameraObject.AddComponent<Camera>();
        cameraObject.tag = "MainCamera";
        camera.backgroundColor = new Color(0.015f,0.025f,0.04f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.nearClipPlane = 0.05f;
        var telemetry = cameraObject.AddComponent<ServerPoseTelemetry>();
        var light = new GameObject("Directional Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(35,-30,0);
        var shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) throw new InvalidOperationException("No supported unlit shader available.");
        for (int i=0;i<7;i++)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = i==3 ? "Rotating center cube" : "Stationary marker " + i;
            cube.transform.position = new Vector3((i-3)*0.45f,0,2.5f);
            cube.transform.localScale = Vector3.one*(i==3?0.3f:0.12f);
            string materialPath = "Assets/ServerDiagnostics/Materials/Marker" + i + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) {material = new Material(shader); AssetDatabase.CreateAsset(material,materialPath);}
            material.color = Color.HSVToRGB(i/7f,0.8f,1f);
            cube.GetComponent<Renderer>().sharedMaterial = material;
            EditorUtility.SetDirty(material);
            if(i==3)telemetry.Marker = cube.transform;
        }
        EditorSceneManager.SaveScene(scene,scenePath);
        var settings = AssetDatabase.LoadAssetAtPath<XREALSettings>("Assets/XR/Settings/XREALSettings.asset");
        if(settings == null)throw new InvalidOperationException("XREAL settings are missing.");
        string before = EditorJsonUtility.ToJson(settings);
        var previousId=PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
        var previousName=PlayerSettings.productName;
        try
        {
            settings.InitialTrackingType = TrackingType.MODE_3DOF;
            settings.InitialInputSource = InputSource.None;
            settings.SupportMultiResume = false;
            EditorBuildSettings.AddConfigObject(XREALSettings.k_SettingsKey,settings,true);
            XREALDemoBuild.ConfigureXRLoader();
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android,"dev.xreal.server.posecheck");
            PlayerSettings.productName = "XREAL Server 3DoF Check";
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android,ScriptingImplementation.IL2CPP);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[]{GraphicsDeviceType.OpenGLES3});
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{scenePath}, locationPathName=output,target=BuildTarget.Android,options=BuildOptions.None});
            if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Diagnostic build failed: " + report.summary.result);
            Debug.Log("[ServerPoseCheck] APK built: " + report.summary.outputPath);
        }
        finally
        {
            EditorJsonUtility.FromJsonOverwrite(before,settings);
            EditorUtility.SetDirty(settings);
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android,previousId);
            PlayerSettings.productName=previousName;
            AssetDatabase.SaveAssets();
        }
    }
}
