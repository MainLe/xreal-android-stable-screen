param(
    [string]$UnityEditor = 'D:\Program Files\Unity\2022.3.62f3c1\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'
$projectPath = (Resolve-Path $PSScriptRoot).Path
$logPath = Join-Path $projectPath 'Build\Android\unity-build.log'
$outputPath = Join-Path $projectPath 'Build\Android\XREAL-HelloMR.apk'
New-Item -ItemType Directory -Force -Path (Split-Path $logPath) | Out-Null

$unityArgs = @(
    '-batchmode',
    '-nographics',
    '-projectPath', $projectPath,
    '-executeMethod', 'XREALDemoBuild.BuildAndroidApk',
    '-logFile', $logPath,
    '-quit'
)
$unity = Start-Process -FilePath $UnityEditor -ArgumentList $unityArgs -WorkingDirectory $projectPath -Wait -PassThru
if ($unity.ExitCode -ne 0) {
    throw "Unity build failed with exit code $($unity.ExitCode). See $logPath"
}
if (-not (Test-Path $outputPath)) {
    throw "Unity reported success but APK is missing: $outputPath"
}
Write-Output "Built $outputPath"
