param(
    [string]$UnityEditor='D:\Program Files\Unity\2022.3.62f3c1\Editor\Unity.exe',
    [string]$ProjectPath=$PSScriptRoot
)
$ErrorActionPreference='Stop'
$releaseProject=(Resolve-Path -LiteralPath $ProjectPath).Path
$logPath=Join-Path $releaseProject 'Build\Releases\unity-release-build.log'
New-Item -ItemType Directory -Force -Path (Split-Path $logPath) | Out-Null
# Process-scoped limits keep compiler/IL postprocessor startup bounded on a
# machine with many logical CPUs. They do not change Windows configuration.
$limits=@{
    DOTNET_GCHeapHardLimit='20000000'
    COMPlus_GCHeapHardLimit='20000000'
    DOTNET_GCConserveMemory='9'
    COMPlus_gcServer='0'
    DOTNET_PROCESSOR_COUNT='2'
    JAVA_TOOL_OPTIONS='-Xmx256m -XX:+UseSerialGC'
}
$previous=@{}
try{
    foreach($key in $limits.Keys){
        $previous[$key]=[Environment]::GetEnvironmentVariable($key,'Process')
        [Environment]::SetEnvironmentVariable($key,$limits[$key],'Process')
    }
    $buildArguments=@('-batchmode','-nographics','-job-worker-count','2','-projectPath',('"{0}"' -f $releaseProject),'-executeMethod','XREALReleaseBuild.BuildStableScreenRelease','-logFile',('"{0}"' -f $logPath),'-quit')
    $buildProcess=Start-Process -FilePath $UnityEditor -ArgumentList $buildArguments -WindowStyle Hidden -PassThru
    $buildProcess.WaitForExit()
    if($buildProcess.ExitCode -ne 0){throw ('Unity Release build failed. See '+$logPath)}
    $artifact=Join-Path $releaseProject 'Build\Releases\xreal-android-stable-screen\XREAL-StableScreen-v0.8.1-release.apk'
    if(!(Test-Path -LiteralPath $artifact)){throw 'Unity exited without the expected Release APK.'}
    Get-FileHash -LiteralPath $artifact -Algorithm SHA256
}finally{
    foreach($key in $previous.Keys){[Environment]::SetEnvironmentVariable($key,$previous[$key],'Process')}
}
