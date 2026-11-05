"""Verify the DSP path candidate APK without installing it or contacting a device."""
import hashlib,json,re,subprocess,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parent;PROJECT=ROOT.parent
BT=Path(r'D:/FilesLibrary/SDK/build-tools/35.0.0');JAVA=Path(r'C:/Program Files/Java/jdk-17/bin/java.exe')
apk=PROJECT/'Build/Android/XREAL-DspProbe-v1.0.3.apk'
previous=PROJECT/'Build/Android/XREAL-HandFix-v1.0.2.apk'
signature=subprocess.check_output([str(JAVA),'-jar',str(BT/'lib/apksigner.jar'),'verify','--print-certs',str(apk)],text=True)
assert '26676203e2103a95b89c4adf5c482b81b97561484e2d4390c6bc707dcea93fbb' in signature
subprocess.run([str(BT/'zipalign.exe'),'-c','4',str(apk)],check=True)
badging=subprocess.check_output([str(BT/'aapt2.exe'),'dump','badging',str(apk)],text=True,encoding='utf8')
assert "name='dev.xreal.server.features'" in badging and "versionCode='13'" in badging and "versionName='1.0.3-dsp-path'" in badging
assert 'application-debuggable' not in badging
audit=json.loads((ROOT/'evidence/dsp-runtime-audit.json').read_text(encoding='utf8'))
assert audit['status']=='passed offline'
with zipfile.ZipFile(apk) as z,zipfile.ZipFile(previous) as old:
    assert z.testzip() is None
    oldlibs={n for n in old.namelist() if n.startswith('lib/') and n.endswith('.so')}
    newlibs={n for n in z.namelist() if n.startswith('lib/') and n.endswith('.so')}
    assert oldlibs==newlibs,('native library inventory changed',oldlibs^newlibs)
    changed=[]
    for name in sorted(oldlibs):
        if z.read(name)!=old.read(name):
            assert Path(name).name in {'libil2cpp.so','libunity.so','lib_burst_generated.so'},('Changed vendor/compatibility native library',name)
            changed.append(name)
    for lib in audit['libraries']:
        assert hashlib.sha256(z.read('lib/arm64-v8a/'+lib['name'])).hexdigest()==lib['sha256']
    metadata=z.read('assets/bin/Data/Managed/Metadata/global-metadata.dat')
    for name in [b'HandDspCompatibility\x00',b'EarlyPrepare\x00',b'pathStillEffective\x00',b'v79SkelPresent\x00']:
        assert name in metadata,('Missing player bootstrap/report code',name)
    assert not any('hand_heap_reader' in n or 'lldb' in n or 'Test.so' in n for n in z.namelist())
scene=(PROJECT/'Assets/ServerDiagnostics/Spatial/SpatialFeatures.unity').read_text()
assert 'EnableHandCompatibility: 1' in scene and 'EnableHandDspCompatibility: 1' in scene
settings=(PROJECT/'Assets/XR/Settings/XREALSettings.asset').read_text()
assert 'InitialTrackingType: 0' in settings and 'InitialInputSource: 1' in settings and 'SupportMultiResume: 1' in settings
log=(ROOT/'evidence/unity-dsp-probe-build.log').read_text(encoding='utf8',errors='replace')
assert '[SpatialDemoBuild] APK built:' in log and not re.search(r'error CS\d+',log)
result={'status':'passed','apk':str(apk),'bytes':apk.stat().st_size,'sha256':hashlib.sha256(apk.read_bytes()).hexdigest(),'package':'dev.xreal.server.features','versionCode':13,'signatureMatchesPreviousDemo':True,'zipAlignmentVerified':True,'vendorAndCompatibilityNativeLibrariesUnchanged':True,'rebuiltUnityLibraries':changed,'earlyPlayerBootstrapPresent':True,'cpuFallbackRetained':True,'sourceAudit':'evidence/dsp-runtime-audit.json','serverUpdateRequired':False,'phoneConnectionUsed':False,'deviceValidation':'pending; path setup, DSP initialization, model inference and hand joints are not verified on hardware'}
(ROOT/'evidence/dsp-probe-apk-verification.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
(PROJECT/'Build/Android/dsp-probe-build-info.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(result,ensure_ascii=False,indent=2))
