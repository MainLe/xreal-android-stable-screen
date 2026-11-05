import hashlib,io,json,re,subprocess,sys,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parent;PROJECT=ROOT.parent
sys.path.insert(0,str(ROOT/'tools/pydeps'))
from elftools.elf.elffile import ELFFile
BT=Path(r'D:/FilesLibrary/SDK/build-tools/35.0.0');JAVA=Path(r'C:/Program Files/Java/jdk-17/bin/java.exe')
apk=PROJECT/'Build/Android/XREAL-HandFix-v1.0.2.apk'
signature=subprocess.check_output([str(JAVA),'-jar',str(BT/'lib/apksigner.jar'),'verify','--print-certs',str(apk)],text=True)
assert '26676203e2103a95b89c4adf5c482b81b97561484e2d4390c6bc707dcea93fbb' in signature
subprocess.run([str(BT/'zipalign.exe'),'-c','4',str(apk)],check=True)
badging=subprocess.check_output([str(BT/'aapt2.exe'),'dump','badging',str(apk)],text=True,encoding='utf8')
assert "name='dev.xreal.server.features'" in badging and "versionCode='12'" in badging and "versionName='1.0.2-handfix'" in badging
assert 'application-debuggable' not in badging
build=json.loads((ROOT/'evidence/hand-compat-build.json').read_text())
with zipfile.ZipFile(apk) as z:
    assert z.testzip() is None
    shim=z.read('lib/arm64-v8a/libXrealHandCompat.so');assert hashlib.sha256(shim).hexdigest()==build['shimSha256']
    elf=ELFFile(io.BytesIO(shim));symbols={s.name for s in elf.get_section_by_name('.dynsym').iter_symbols()}
    for name in ['XrealHandCompatStart','XrealHandCompatState','XrealHandCompatImports','XrealHandCompatRewrites']:assert name in symbols
    assert 'XrealHandCompatOfflineTest' not in symbols and 'XrealHandCompatOfflineInstall' not in symbols
    assert all(s['p_align']>=16384 for s in elf.iter_segments() if s['p_type']=='PT_LOAD')
    assert not any('hand_heap_reader' in n or 'lldb' in n or 'Test.so' in n for n in z.namelist())
    needs=[t.needed for t in elf.get_section_by_name('.dynamic').iter_tags() if t.entry.d_tag=='DT_NEEDED']
    assert 'libc++_shared.so' not in needs
    rebuilt=[]
    with zipfile.ZipFile(PROJECT/'Build/Android/XREAL-FeatureDemo.apk') as previous:
        for n in previous.namelist():
            if n.startswith('lib/') and n.endswith('.so') and previous.read(n)!=z.read(n):
                assert Path(n).name in {'libil2cpp.so','libunity.so','lib_burst_generated.so'},('Changed vendor client native library',n)
                rebuilt.append(n)
settings=(PROJECT/'Assets/XR/Settings/XREALSettings.asset').read_text()
assert 'InitialTrackingType: 0' in settings and 'InitialInputSource: 1' in settings and 'SupportMultiResume: 1' in settings
assert 'EnableHandCompatibility: 1' in (PROJECT/'Assets/ServerDiagnostics/Spatial/SpatialFeatures.unity').read_text()
log=(ROOT/'evidence/unity-handfix-build.log').read_text(encoding='utf8',errors='replace')
assert '[SpatialDemoBuild] APK built:' in log and not re.search(r'error CS\d+',log)
assert json.loads((ROOT/'evidence/hand-compat-native-test.json').read_text())['status']=='passed'
result={'status':'passed','apk':str(apk),'bytes':apk.stat().st_size,'sha256':hashlib.sha256(apk.read_bytes()).hexdigest(),'package':'dev.xreal.server.features','versionCode':12,'signatureMatchesPreviousDemo':True,'zipAlignmentVerified':True,'shimSha256':build['shimSha256'],'vendorClientNativeLibrariesUnchanged':True,'rebuiltUnityLibraries':rebuilt,'serverUpdateRequired':False,'nativeGuardAndImportTests':'passed offline on actual ARM64 routines','phoneTesting':'pending; user disconnected device before this APK was built'}
(ROOT/'evidence/handfix-apk-verification.json').write_text(json.dumps(result,indent=2));(PROJECT/'Build/Android/handfix-build-info.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
