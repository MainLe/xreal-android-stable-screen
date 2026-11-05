"""Check shipped APKs, JNI UI bridge, reference database, signing and display setup."""
import hashlib, json, re, runpy, struct, subprocess, zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parent
PROJECT=ROOT.parent
classes=runpy.run_path(str(ROOT/'verify_build.py'))['classes']
BT=Path(r'D:/FilesLibrary/SDK/build-tools/35.0.0')
JAVA=Path(r'C:/Program Files/Java/jdk-17/bin/java.exe')
deliveries={}
for name,package in [('XrealServer-v1.0.0.apk','dev.xreal.server'),('XREAL-FeatureDemo.apk','dev.xreal.server.features')]:
    path=PROJECT/'Build/Android'/name
    subprocess.run([str(JAVA),'-jar',str(BT/'lib/apksigner.jar'),'verify',str(path)],check=True)
    subprocess.run([str(BT/'zipalign.exe'),'-c','4',str(path)],check=True)
    badging=subprocess.check_output([str(BT/'aapt2.exe'),'dump','badging',str(path)],text=True,encoding='utf8')
    assert "name='"+package+"'" in badging
    manifest=subprocess.check_output([str(BT/'aapt2.exe'),'dump','xmltree',str(path),'--file','AndroidManifest.xml'],text=True,encoding='utf8')
    assert 'ai.nreal.nrsdk.network.type.server' in manifest
    with zipfile.ZipFile(path) as z:
        assert z.testzip() is None
        definitions=[]
        for entry in z.namelist():
            if re.fullmatch(r'classes\d*\.dex',entry):definitions.extend(classes(z.read(entry)))
        if package.endswith('.features'):
            assert 'Ldev/xreal/demo/FeatureDebugPanel;' in definitions,'Missing native phone controls'
            assert any(n.endswith('libXREALXRPlugin.so') for n in z.namelist()),'Missing XR plugin'
            # The image-library key followed by a nonempty byte array must exist in serialized player data.
            candidates=[]
            groups={}
            for n in z.namelist():
                match=re.fullmatch(r'(assets/bin/Data/[^/]+\.assets)(?:\.split(\d+))?',n)
                if match:groups.setdefault(match[1],[]).append((int(match[2] or 0),n))
            for n,parts in groups.items():
                    data=b''.join(z.read(part) for _,part in sorted(parts))
                    if b'ImageLibrary' in data and b'XREAL Debug Target 20cm' in data:
                        offset=data.find(b'com.xreal.xr',data.find(b'ImageLibrary'))
                        if offset>=0:
                            end=(offset+len(b'com.xreal.xr')+3)&~3
                            length=struct.unpack_from('<I',data,end)[0]
                            if length>1024 and end+4+length<=len(data):candidates.append({'asset':n,'databaseBytes':length})
            assert candidates,'Compiled reference database not found in player assets'
            deliveries['imageDatabase']=candidates
    deliveries[name]={'package':package,'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'signatureVerified':True,'zipAlignmentVerified':True}

log=(ROOT/'evidence/unity-spatial-build.log').read_text(encoding='utf8')
assert '[SpatialDemoBuild] APK built:' in log
assert '[SpatialDemoBuild] Pinch hysteresis and invalid-sample checks passed.' in log
assert not re.search(r'error CS\d+|FAILURE: Build failed',log)
settings=(PROJECT/'Assets/XR/Settings/XREALSettings.asset').read_text()
assert 'InitialTrackingType: 0' in settings and 'InitialInputSource: 1' in settings and 'SupportMultiResume: 1' in settings
deliveries['status']='build-and-package-checks-passed'
deliveries['hardwareValidation']='pending for newly added spatial features'
(ROOT/'evidence/spatial-build-verification.json').write_text(json.dumps(deliveries,indent=2),encoding='utf8')
(PROJECT/'Build/Android/spatial-build-info.json').write_text(json.dumps(deliveries,indent=2),encoding='utf8')
print(json.dumps(deliveries,indent=2))
