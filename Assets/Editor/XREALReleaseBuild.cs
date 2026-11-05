using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Release packaging entry point. Keep the existing Android signing identity so
// this distribution can update installations signed by the same local key.
public static class XREALReleaseBuild
{
    public static void BuildStableScreenRelease()
    {
        var oldCompiler=PlayerSettings.GetIl2CppCompilerConfiguration(BuildTargetGroup.Android);
        bool oldDevelopment=EditorUserBuildSettings.development;
        bool oldDebugging=EditorUserBuildSettings.allowDebugging;
        bool oldProfiler=EditorUserBuildSettings.connectProfiler;
        try
        {
            PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Android,Il2CppCompilerConfiguration.Release);
            EditorUserBuildSettings.development=false;
            EditorUserBuildSettings.allowDebugging=false;
            EditorUserBuildSettings.connectProfiler=false;
            XREALScreenStabilizerBuild.Build();
            const string output="Build/Releases/xreal-android-stable-screen/XREAL-StableScreen-v0.8.1-release.apk";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.Copy("Build/Android/XREAL-StableScreen-v0.8.1.apk",output,true);
            Debug.Log("[ReleaseBuild] IL2CPP Release / BuildOptions.None: "+Path.GetFullPath(output));
        }
        finally
        {
            PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Android,oldCompiler);
            EditorUserBuildSettings.development=oldDevelopment;
            EditorUserBuildSettings.allowDebugging=oldDebugging;
            EditorUserBuildSettings.connectProfiler=oldProfiler;
            AssetDatabase.SaveAssets();
        }
    }
}
