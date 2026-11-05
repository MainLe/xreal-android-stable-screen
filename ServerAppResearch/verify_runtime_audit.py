"""Verify the runtime-audit demo, including the opt-in managed SDK poll probe. No device access."""
import hashlib,json,re,struct,subprocess,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parent;PROJECT=ROOT.parent
BT=Path(r'D:/FilesLibrary/SDK/build-tools/35.0.0');JAVA=Path(r'C:/Program Files/Java/jdk-17/bin/java.exe')
apk=PROJECT/'Build/Android/XREAL-RuntimeAudit-v1.0.4.apk'
baseline=PROJECT/'Build/Android/XREAL-DspProbe-v1.0.3.apk'
signature=subprocess.check_output([str(JAVA),'-jar',str(BT/'lib/apksigner.jar'),'verify','--print-certs',str(apk)],text=True)
assert '26676203e2103a95b89c4adf5c482b81b97561484e2d4390c6bc707dcea93fbb' in signature
subprocess.run([str(BT/'zipalign.exe'),'-c','4',str(apk)],check=True)
badging=subprocess.check_output([str(BT/'aapt2.exe'),'dump','badging',str(apk)],text=True,encoding='utf8')
for expected in ["name='dev.xreal.server.features'","versionCode='14'","versionName='1.0.4-runtime-audit'"]:assert expected in badging
assert 'application-debuggable' not in badging
with zipfile.ZipFile(apk) as z,zipfile.ZipFile(baseline) as old:
    assert z.testzip() is None
    names={n for n in z.namelist() if n.startswith('lib/') and n.endswith('.so')}
    assert names=={n for n in old.namelist() if n.startswith('lib/') and n.endswith('.so')}
    rebuilt=[]
    for name in sorted(names):
        if z.read(name)!=old.read(name):
            assert Path(name).name in {'libil2cpp.so','libunity.so','lib_burst_generated.so'},('Changed native runtime or compatibility shim',name)
            rebuilt.append(name)
    metadata=z.read('assets/bin/Data/Managed/Metadata/global-metadata.dat')
    for name in [b'FeatureHeadPose\x00',b'XrHeadPoseSample\x00',b'XrRuntimeHealth\x00',b'PollingDiagnostics\x00',b'ReuseBeforeRender\x00',b'NativeReadReturnedFalse\x00',b'handPollingAuditAvailable\x00']:
        assert name in metadata,('Diagnostic build define or managed code missing',name)
    assert any(b'Ldev/xreal/common/RuntimeHealthBridge;' in z.read(n) for n in z.namelist() if re.fullmatch(r'classes\d*\.dex',n))
    assert not any('lldb' in n or 'hand_heap_reader' in n for n in z.namelist())
settings=(PROJECT/'Assets/XR/Settings/XREALSettings.asset').read_text()
assert 'InitialTrackingType: 0' in settings and 'InitialInputSource: 1' in settings and 'SupportMultiResume: 1' in settings
assert 'enableFrameTimingStats: 0' in (PROJECT/'ProjectSettings/ProjectSettings.asset').read_text()
log=(ROOT/'evidence/unity-runtime-audit-build.log').read_text(encoding='utf8',errors='replace')
assert '[SpatialDemoBuild] APK built:' in log and '[XrRuntimeChecks] Sub-millisecond OFF passthrough and strict tracking validity checks passed.' in log
assert not re.search(r'error CS\d+|FAILURE: Build failed|Crash!!!',log)
sdk=PROJECT/'package/Runtime/Scripts/Android/XREALHandSubsystem.cs'
original=ROOT/'build/runtime-audit/XREALHandSubsystem.original.cs'
result={'status':'passed offline','apk':str(apk),'version':'1.0.4-runtime-audit','versionCode':14,'package':'dev.xreal.server.features','bytes':apk.stat().st_size,'sha256':hashlib.sha256(apk.read_bytes()).hexdigest(),'signatureVerified':True,'zipAlignmentVerified':True,'nativeRuntimeAndHandCompatibilityShimUnchanged':True,'rebuiltUnityLibraries':rebuilt,'managedSdkPollProbe':{'source':str(sdk),'originalSha256':hashlib.sha256(original.read_bytes()).hexdigest(),'patchedSha256':hashlib.sha256(sdk.read_bytes()).hexdigest(),'define':'XREAL_HAND_RUNTIME_AUDIT','default':'Two-phase SDK reads; reuse disabled','experiment':'Reuse the current Dynamic sample at BeforeRender; native inference frequency unchanged'},'longRunHistory':'5-second samples; up to three 2-MiB files in external app files/runtime-health','serverUpdateRequired':False,'phoneConnectionUsed':False,'hardwareValidation':'pending; reported intermittent degradation after enabling hands has not been reproduced on this build'}
(ROOT/'evidence/runtime-audit-apk-verification.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
(PROJECT/'Build/Android/runtime-audit-build-info.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(result,ensure_ascii=False,indent=2))
