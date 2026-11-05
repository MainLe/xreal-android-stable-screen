"""Prepare an app-local display fix and compile/test capture Java. No device or SDK source mutation."""
import hashlib,io,json,os,subprocess,tempfile,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parent;PROJECT=ROOT.parent
JDK=Path(os.environ.get('XREAL_JDK',r'C:/Program Files/Java/jdk-17'))/'bin';ANDROID=Path(os.environ.get('XREAL_ANDROID_SDK',r'D:/FilesLibrary/SDK'))/'platforms/android-36/android.jar'
SOURCE=PROJECT/'package/Runtime/Plugins/Android/GlassesDisplayPlugEvent-2.4.2.aar'
OUT=ROOT/'build/screen-stabilizer';OUT.mkdir(parents=True,exist_ok=True)
CLASSES=Path(tempfile.mkdtemp(prefix='display-',dir=OUT))
def run(*args):
    argv=[str(a) for a in args]
    limits=['-J-Xmx192m','-J-XX:+UseSerialGC'] if Path(argv[0]).name=='javac.exe' else ['-Xmx128m','-XX:+UseSerialGC']
    subprocess.run([argv[0],*limits,*argv[1:]],check=True,cwd=PROJECT)
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()=='17205da4e7004628ff86b2fc0a4fe8494d8f1638b6e2488e46c2755372522c26'
run(JDK/'javac.exe','-encoding','UTF-8','--release','8','-classpath',ANDROID,'-d',CLASSES,ROOT/'server-app/src/com/xreal/glassesdisplayplugevent/display/DisplayModel.java')
entry='com/xreal/glassesdisplayplugevent/display/DisplayModel.class'
with zipfile.ZipFile(SOURCE) as z:original_jar=z.read('classes.jar')
jar_out=io.BytesIO()
with zipfile.ZipFile(io.BytesIO(original_jar)) as old,zipfile.ZipFile(jar_out,'w') as updated:
    for member in old.infolist():updated.writestr(member,(CLASSES/entry).read_bytes() if member.filename==entry else old.read(member))
with zipfile.ZipFile(SOURCE) as old,zipfile.ZipFile(OUT/SOURCE.name,'w') as updated:
    for member in old.infolist():updated.writestr(member,jar_out.getvalue() if member.filename=='classes.jar' else old.read(member))
with zipfile.ZipFile(io.BytesIO(original_jar)) as old,zipfile.ZipFile(io.BytesIO(jar_out.getvalue())) as fixed:
    assert set(old.namelist())==set(fixed.namelist())
    assert [n for n in old.namelist() if old.read(n)!=fixed.read(n)]==[entry]
(OUT/'original-display.jar').write_bytes(original_jar)
capture=PROJECT/'Assets/ScreenStabilizer/Plugins/ScreenMirror.androidlib/src/dev/xreal/stablescreen'
test_classes=Path(tempfile.mkdtemp(prefix='tests-',dir=OUT))
run(JDK/'javac.exe','-encoding','UTF-8','--release','8','-d',test_classes,capture/'CaptureSizing.java',capture/'FrameMailbox.java',ROOT/'screen-stabilizer/FrameMailboxTest.java')
run(JDK/'java.exe','-cp',test_classes,'FrameMailboxTest')
unity=PROJECT/'Library/Bee/Android/Prj/IL2CPP/Gradle/unityLibrary/libs/unity-classes.jar'
if not unity.exists():unity=Path(r'D:/Program Files/Unity/2022.3.62f3c1/Editor/Data/PlaybackEngines/AndroidPlayer/Variations/il2cpp/Release/Classes/classes.jar')
assert unity.exists(),str(unity)
java_classes=Path(tempfile.mkdtemp(prefix='capture-',dir=OUT))
run(JDK/'javac.exe','-encoding','UTF-8','--release','8','-classpath',os.pathsep.join(map(str,[ANDROID,unity,OUT/'original-display.jar'])),'-d',java_classes,*sorted(capture.glob('*.java')))
common_sources=[PROJECT/'Assets/Plugins/Android/RuntimeHealthBridge.java',PROJECT/'Assets/Plugins/Android/FeatureDebugPanel.java']
run(JDK/'javac.exe','-encoding','UTF-8','--release','8','-classpath',os.pathsep.join(map(str,[ANDROID,unity])),'-d',java_classes,*common_sources)
result={'status':'passed offline','originalDisplayAarSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'preparedDisplayAarSha256':hashlib.sha256((OUT/SOURCE.name).read_bytes()).hexdigest(),'changedClass':entry,'officialDisplayAarUnmodified':True,'frameMailboxTests':'passed; 10000-frame producer/consumer stress','captureJavaCompile':'passed','javaFiles':len(list(capture.glob('*.java'))),'javaSourceHashes':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(capture.glob('*.java'))},'commonJavaSourceHashes':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in common_sources},'phoneConnectionUsed':False}
(ROOT/'evidence/screen-stabilizer-preparation.json').write_text(json.dumps(result,indent=2),encoding='utf8');print(json.dumps(result,indent=2))
