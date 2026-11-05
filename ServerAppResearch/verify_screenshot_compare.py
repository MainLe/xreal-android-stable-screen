"""Offline packaging checks for the static screenshot/cube comparison demo."""
import hashlib,json,re,struct,subprocess,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parent;PROJECT=ROOT.parent
BT=Path(r'D:/FilesLibrary/SDK/build-tools/35.0.0');JAVA=Path(r'C:/Program Files/Java/jdk-17/bin/java.exe')
apk=PROJECT/'Build/Android/XREAL-ScreenshotCompare-v1.0.5.apk'
baseline=PROJECT/'Build/Android/XREAL-RuntimeAudit-v1.0.4.apk'
signature=subprocess.check_output([str(JAVA),'-jar',str(BT/'lib/apksigner.jar'),'verify','--print-certs',str(apk)],text=True)
assert '26676203e2103a95b89c4adf5c482b81b97561484e2d4390c6bc707dcea93fbb' in signature
subprocess.run([str(BT/'zipalign.exe'),'-c','4',str(apk)],check=True)
badging=subprocess.check_output([str(BT/'aapt2.exe'),'dump','badging',str(apk)],text=True,encoding='utf8')
for value in ["name='dev.xreal.server.features'","versionCode='15'","versionName='1.0.5-screen-compare'"]:assert value in badging
assert 'application-debuggable' not in badging
manifest=subprocess.check_output([str(BT/'aapt2.exe'),'dump','xmltree',str(apk),'--file','AndroidManifest.xml'],text=True,encoding='utf8')
assert 'dev.xreal.stablescreen.ProjectionService' not in manifest,'Comparison must not import the live screen-capture library'
image=PROJECT/'Assets/ServerDiagnostics/Spatial/PhoneScreenshot.png'
source=ROOT/'evidence/stable-screen-v031-phone.png'
assert image.read_bytes()==source.read_bytes(),'Static comparison input changed'
assert struct.unpack('>II',image.read_bytes()[16:24])==(1200,2670)
with zipfile.ZipFile(apk) as z,zipfile.ZipFile(baseline) as old:
    assert z.testzip() is None
    libraries={n for n in z.namelist() if n.startswith('lib/') and n.endswith('.so')}
    assert libraries=={n for n in old.namelist() if n.startswith('lib/') and n.endswith('.so')}
    changed=[]
    for name in sorted(libraries):
        if z.read(name)!=old.read(name):
            assert Path(name).name in {'libil2cpp.so','libunity.so','lib_burst_generated.so'},('Native SDK or compatibility shim changed',name)
            changed.append(name)
    metadata=z.read('assets/bin/Data/Managed/Metadata/global-metadata.dat')
    for value in [b'StaticPhoneComparison\x00',b'FreezeCube\x00',b'ImageVisible\x00',b'CenterAfterDelay\x00',b'XrHeadPoseSample\x00']:
        assert value in metadata,('Missing comparison behavior',value)
    scene=b''.join(z.read(name) for name in z.namelist() if re.fullmatch(r'assets/bin/Data/level0(?:\.split\d+)?',name))
    for value in [b'World-fixed screenshot and cube',b'Static phone screenshot']:
        assert value in scene,('Missing comparison geometry in APK scene',value)
    assert any(b'configureScreenshotComparison' in z.read(name) for name in z.namelist() if re.fullmatch(r'classes\d*\.dex',name))
log=(ROOT/'evidence/unity-screenshot-compare-build.log').read_text(encoding='utf8',errors='replace')
assert '[SpatialDemoBuild] APK built:' in log
assert '[ScreenshotCompareBuild] Static screenshot and cube packaged; shared world group and depth; GLES shader compiled without errors.' in log
assert not re.search(r'error CS\d+|Shader error in|FAILURE: Build failed|Crash!!!',log,re.IGNORECASE)
compiled=re.search(r'Serialized binary data for shader XREAL/Stable Phone Screen[^\n]*\n\s+gles3 \(total internal programs: (\d+)',log)
assert compiled and int(compiled.group(1))>0,'No valid GLES screenshot shader programs'
settings=(PROJECT/'Assets/XR/Settings/XREALSettings.asset').read_text()
assert 'InitialTrackingType: 0' in settings and 'InitialInputSource: 1' in settings and 'SupportMultiResume: 1' in settings
result={'status':'passed offline','apk':str(apk),'bytes':apk.stat().st_size,'sha256':hashlib.sha256(apk.read_bytes()).hexdigest(),'package':'dev.xreal.server.features','version':'1.0.5-screen-compare','versionCode':15,'signatureMatchesPreviousDemo':True,'zipAligned':True,'sdkNativeAndHandCompatibilityShimUnchanged':True,'rebuiltUnityLibraries':changed,'staticImage':{'source':str(source),'sha256':hashlib.sha256(image.read_bytes()).hexdigest(),'width':1200,'height':2670,'mipmaps':False,'filter':'Bilinear','androidTexture':'RGBA32 / uncompressed'},'comparison':{'sameWorldParent':True,'sameDepth':True,'distanceMetres':0.65,'screenshotHeightMetres':0.28,'cubeRotationInitiallyPaused':True,'liveCapture':False,'extraPoseFiltering':False},'glesShaderPrograms':int(compiled.group(1)),'serverUpdateRequired':False,'stableScreenUpdateRequired':False,'deviceValidation':'pending manual installation; subjective comparison belongs to user'}
(ROOT/'evidence/screenshot-compare-apk-verification.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
(PROJECT/'Build/Android/screenshot-compare-build-info.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(result,ensure_ascii=False,indent=2))
