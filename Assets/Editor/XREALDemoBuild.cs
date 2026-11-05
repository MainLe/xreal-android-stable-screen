using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using Unity.XR.XREAL;

public static class XREALDemoBuild
{
    private const string ScenePath = "Assets/Scenes/HelloMR.unity";
    private const string OutputPath = "Build/Android/XREAL-HelloMR.apk";
    private const string ApplicationId = "com.xreal.hellomr.demo";
    private const string XRSettingsKey = "com.unity.xr.management.loader_settings";
    private const string XRSettingsAssetPath = "Assets/XR/Settings/XRGeneralSettingsPerBuildTarget.asset";

    [MenuItem("XREAL/Build Android APK")]
    public static void BuildAndroidApk()
    {
        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            throw new InvalidOperationException("Unity could not switch the active target to Android.");
        var xrealSettings = AssetDatabase.LoadAssetAtPath<XREALSettings>("Assets/XR/Settings/XREALSettings.asset");
        if (xrealSettings == null)
            throw new InvalidOperationException("XREAL settings asset is missing.");
        EditorBuildSettings.AddConfigObject(XREALSettings.k_SettingsKey, xrealSettings, true);


        ConfigureXRLoader();
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, ApplicationId);
        PlayerSettings.productName = "XREAL HelloMR";
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
        EditorUserBuildSettings.buildAppBundle = false;
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = OutputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException($"Android build failed: {report.summary.result}. See the Unity Editor log for details.");

        Debug.Log($"XREAL APK created: {report.summary.outputPath} ({report.summary.totalSize} bytes)");
    }

    internal static void ConfigureXRLoader()
    {
        if (!EditorBuildSettings.TryGetConfigObject(XRSettingsKey, out XRGeneralSettingsPerBuildTarget perBuildTarget))
        {
            perBuildTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XRSettingsAssetPath);
            if (perBuildTarget == null)
            {
                perBuildTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perBuildTarget, XRSettingsAssetPath);
            }
            EditorBuildSettings.AddConfigObject(XRSettingsKey, perBuildTarget, true);
        }

        if (perBuildTarget.SettingsForBuildTarget(BuildTargetGroup.Android) == null)
            perBuildTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
        if (!perBuildTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

        XRGeneralSettings generalSettings = perBuildTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        if (generalSettings == null || generalSettings.AssignedSettings == null)
            throw new InvalidOperationException("XR Plug-in Management could not create Android settings.");

        if (!XRPackageMetadataStore.AssignLoader(generalSettings.AssignedSettings, typeof(XREALXRLoader).FullName, BuildTargetGroup.Android))
            throw new InvalidOperationException("Could not assign the XREAL XR Loader. Check that the XREAL XR Plugin package is installed and resolved.");
        generalSettings.InitManagerOnStart = true;
        AssetDatabase.SaveAssets();
        EditorUtility.SetDirty(generalSettings);
        EditorUtility.SetDirty(generalSettings.AssignedSettings);
    }
}
