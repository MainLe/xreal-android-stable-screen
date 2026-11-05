"""Verify the built whole-phone 3/6DoF app, without installation or a phone connection."""
import hashlib,json,os,re,struct,subprocess,zipfile
from collections import Counter
from pathlib import Path
ROOT=Path(__file__).resolve().parent;PROJECT=ROOT.parent
BT=Path(r'D:/FilesLibrary/SDK/build-tools/35.0.0');JAVA=Path(r'C:/Program Files/Java/jdk-17/bin/java.exe')
VERSION='0.8.1';CODE=17
SUFFIX=VERSION.replace('.', '')
apk=PROJECT/f'Build/Android/XREAL-StableScreen-v{VERSION}.apk'
def classes(data):
    count,offset=struct.unpack_from('<II',data,56);strings=[]
    for i in range(count):
        p=struct.unpack_from('<I',data,offset+4*i)[0]
        while data[p]&128:p+=1
        p+=1;strings.append(data[p:data.index(b'\0',p)].decode('utf8','replace'))
    count,offset=struct.unpack_from('<II',data,64);types=[strings[struct.unpack_from('<I',data,offset+i*4)[0]] for i in range(count)]
    count,offset=struct.unpack_from('<II',data,96)
    return [types[struct.unpack_from('<I',data,offset+i*32)[0]] for i in range(count)]
signature=subprocess.check_output([str(JAVA),'-jar',str(BT/'lib/apksigner.jar'),'verify','--print-certs',str(apk)],text=True)
assert '26676203e2103a95b89c4adf5c482b81b97561484e2d4390c6bc707dcea93fbb' in signature
subprocess.run([str(BT/'zipalign.exe'),'-c','4',str(apk)],check=True)
badging=subprocess.check_output([str(BT/'aapt2.exe'),'dump','badging',str(apk)],text=True,encoding='utf8')
assert "name='dev.xreal.stablescreen'" in badging and f"versionCode='{CODE}'" in badging and f"versionName='{VERSION}'" in badging
assert re.search(r"(?:sdkVersion|minSdkVersion):'32'",badging) and "targetSdkVersion:'35'" in badging and 'application-debuggable' not in badging
assert re.findall(r"launchable-activity: name='([^']+)'",badging)==['dev.xreal.stablescreen.ControlActivity']
manifest=subprocess.check_output([str(BT/'aapt2.exe'),'dump','xmltree',str(apk),'--file','AndroidManifest.xml'],text=True,encoding='utf8')
for value in ['android.permission.FOREGROUND_SERVICE_MEDIA_PROJECTION','dev.xreal.stablescreen.ProjectionService','dev.xreal.stablescreen.ProjectionConsentActivity','dev.xreal.stablescreen.ScreenPlayerActivity','dev.xreal.stablescreen.CaptureBenchmarkActivity','dev.xreal.stablescreen.PhoneInputTestActivity','ai.nreal.nrsdk.network.type.server','dev.xreal.stablescreen.PhoneGestureService','android.permission.BIND_ACCESSIBILITY_SERVICE']:
    assert value in manifest,('Missing manifest capability',value)
projection_service=next(block for block in re.split(r'(?m)^          E: ',manifest) if '"dev.xreal.stablescreen.ProjectionService"' in block)
assert re.search(r'foregroundServiceType.*=0x0*20\b',projection_service), 'MediaProjection service type missing'
assert 'android.permission.RECORD_AUDIO' not in manifest
assert 'android.permission.WRITE_SECURE_SETTINGS' not in manifest
assert 'ai.nreal.activitylife.NRFakeActivity' not in manifest
assert '"dev.xreal.stablescreen.ScreenApplication"' in manifest,'Early Application diagnostics missing'
resources=subprocess.check_output([str(BT/'aapt2.exe'),'dump','resources',str(apk)],text=True,encoding='utf8')
accessibility_path=re.search(r'xml/phone_gesture_service\s+\(\) \(file\) (\S+)',resources)
assert accessibility_path,'Accessibility config missing from resource table'
accessibility_xml=subprocess.check_output([str(BT/'aapt2.exe'),'dump','xmltree',str(apk),'--file',accessibility_path.group(1)],text=True,encoding='utf8')
assert re.search(r'canPerformGestures[^\n]*=(?:true|\(type 0x12\)0xffffffff)',accessibility_xml),accessibility_xml
assert re.search(r'canRetrieveWindowContent[^\n]*=(?:false|\(type 0x12\)0x0)',accessibility_xml),accessibility_xml
affinities={}
for name,suffix in [('dev.xreal.stablescreen.ControlActivity','phone'),('dev.xreal.stablescreen.ScreenPlayerActivity','engine')]:
    block=next(block for block in re.split(r'(?m)^          E: ',manifest) if '"'+name+'"' in block)
    affinity=re.search(r'taskAffinity[^=]*="([^"]+)"',block)
    assert affinity and affinity.group(1)=='dev.xreal.stablescreen.'+suffix,('Wrong display task affinity',name,block)
    affinities[name]=affinity.group(1)
assert len(set(affinities.values()))==2
host=next(block for block in re.split(r'(?m)^          E: ',manifest) if '"dev.xreal.stablescreen.ScreenPlayerActivity"' in block)
assert re.search(r'exported[^\n]*=(?:false|\(type 0x12\)0x0)\b',host),host
assert 'unityplayer.UnityActivity' in host
with zipfile.ZipFile(apk) as z:
    assert z.testzip() is None
    definitions=[]
    for name in z.namelist():
        if re.fullmatch(r'classes\d*\.dex',name):definitions.extend(classes(z.read(name)))
    counts=Counter(definitions)
    for name in ['ControlActivity','ProjectionConsentActivity','ProjectionService','ControlReceiver','ScreenBridge','FrameMailbox','CaptureSizing','ScreenApplication','ScreenPlayerActivity','CaptureBenchmarkActivity','PhoneInputTestActivity','SdkRefreshDriver','PhoneGestureService']:
        assert counts['Ldev/xreal/stablescreen/'+name+';']==1
    assert counts['Lcom/xreal/glassesdisplayplugevent/display/DisplayModel;']==1
    assert counts['Ldev/xreal/common/RuntimeHealthBridge;']==1
    assert counts['Lai/nreal/activitylife/UnityPlayerActivity;']==0
    assert counts['Lai/nreal/activitylife/NRFakeActivity;']==0
    metadata=z.read('assets/bin/Data/Managed/Metadata/global-metadata.dat')
    for name in [b'StabilizedScreen\x00',b'MirrorFrameCopy\x00',b'StablePoseFilter\x00',b'SwitchMode\x00',b'Compensate\x00']:
        assert name in metadata,('Missing managed screen/tracking implementation',name)
    for name in [b'XrHeadPoseSample\x00',b'XrRuntimeHealth\x00',b'BeforeRender\x00']:
        assert name in metadata,('Missing shared raw-pose / runtime diagnostics',name)
    assert b'PollingDiagnostics\x00' not in metadata,'Hand provider audit experiment must be excluded from Stable Screen'
    assert b'ReferenceMaterial\x00' in metadata,'Separate calibration material reference missing'
    assert b'updateSdkOutput' in metadata,'SDK output readiness probe missing from managed player'
    for name in [b'HandPhoneControl\x00',b'HandScreenGeometry\x00',b'LeftHandPanelAnchor\x00',b'PalmSurfaceFrame\x00',b'TryGetPalmSurfacePose\x00',b'TryGetIndexPointer\x00',b'PhoneUv\x00',b'HandGraphCompatibility\x00',b'DirectScreenTouch\x00',b'TryGetIndexTip\x00',b'ShiftDistance\x00',b'RefreshPresentation\x00']:
        assert name in metadata,('Missing hand phone control',name)
    for name in [b'handScreenHeadingDegrees',b'directTouchMargin']:
        assert name in metadata,('Missing runtime setting read',name)
    for name in [b'PlaneScreenPin\x00',b'PlanePinMath\x00',b'PlanePinGesture\x00',b'ARPlaneManager\x00',b'ARAnchorManager\x00',b'ARAnchor\x00',b'ApplyPlanePose\x00',b'planeAimMode',b'updatePlanePin']:
        assert name in metadata,('Missing plane selection or spatial anchoring',name)
    for name in [b'PlaneAnchorCreation\x00',b'PlaneNativeAnchorFactory\x00',b'updatePlaneAnchorDiagnostics']:
        assert name in metadata,('Missing anchor lifecycle repair',name)
    for name in [b'PlaneAnchorTestProvider\x00',b'PlaneAnchorTestSubsystem\x00',b'PlaneAnchorLifecycleChecks\x00']:
        assert name not in metadata,('Editor test provider leaked into player',name)
    for name in [b'BootstrapDiagnostics\x00',b'BeforeSplash\x00',b'ReportUnityError\x00']:assert name in metadata,('Missing early managed startup report',name)
    with zipfile.ZipFile(PROJECT/'Build/Android/XREAL-HandFix-v1.0.2.apk') as old:
        native_names=[n for n in z.namelist() if n.startswith('lib/') and n.endswith('.so') and Path(n).name not in {'libil2cpp.so','libunity.so','lib_burst_generated.so'}]
        for name in native_names:assert z.read(name)==old.read(name),('Unexpected native vendor change',name)
    assert not any('lldb' in n or 'hand_heap_reader' in n for n in z.namelist())
preparation=json.loads((ROOT/'evidence/screen-stabilizer-preparation.json').read_text(encoding='utf8'))
assert preparation['status']=='passed offline'
source=PROJECT/'Assets/ScreenStabilizer/Plugins/ScreenMirror.androidlib/src/dev/xreal/stablescreen'
assert preparation['javaSourceHashes']=={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(source.glob('*.java'))},'Java source changed after preparation'
for name,digest in preparation['commonJavaSourceHashes'].items():assert hashlib.sha256((PROJECT/'Assets/Plugins/Android'/name).read_bytes()).hexdigest()==digest
original=PROJECT/'package/Runtime/Plugins/Android/GlassesDisplayPlugEvent-2.4.2.aar'
assert hashlib.sha256(original.read_bytes()).hexdigest()==preparation['originalDisplayAarSha256']
gradle=PROJECT/'Library/Bee/Android/Prj/IL2CPP/Gradle/unityLibrary'
assert hashlib.sha256((gradle/'libs'/original.name).read_bytes()).hexdigest()==preparation['preparedDisplayAarSha256']
settings=(PROJECT/'Assets/XR/Settings/XREALSettings.asset').read_text()
assert 'InitialTrackingType: 0' in settings and 'InitialInputSource: 1' in settings and 'SupportMultiResume: 1' in settings
plugin=(PROJECT/'Assets/ScreenStabilizer/Plugins/ScreenMirror.androidlib.meta').read_text()
assert re.search(r'Android: Android\s+second:\s+enabled: 0',plugin)
log=(ROOT/f'evidence/unity-stable-screen-build-v{VERSION.replace(".","")}.log').read_text(encoding='utf8',errors='replace')
for message in ['[StableScreenBuild] APK built:','[StableScreenBuild] Padded / truncated frame, aspect and recenter checks passed.','[StableScreenBuild] Adaptive filter jitter / lag / quaternion / 6DoF compensation checks passed.','[StableScreenBuild] Client display helper patched; phone control launcher configured.','[StableScreenBuild] SDK owns the sole eye Presentation; offscreen Unity primary sink; SDK Multi Resume disabled for this app only.']:
    assert message in log,message
assert not re.search(r'error CS\d+|FAILURE: Build failed|Crash!!!|Shader error in|Shader compilation failed',log,re.IGNORECASE)
assert '[StableScreenBuild] Phone and calibration GLES shaders compiled without errors.' in log
shader_programs={}
for shader in ['XREAL/Stable Phone Screen','XREAL/Stable Calibration Grid','XREAL/Stable Hand Overlay']:
    compiled=re.search(r'Serialized binary data for shader '+re.escape(shader)+r'[^\n]*\n\s+gles3 \(total internal programs: (\d+)',log)
    assert compiled and int(compiled.group(1))>0,('Missing valid GLES programs',shader)
    shader_programs[shader]=int(compiled.group(1))
assert '[XrRuntimeChecks] Sub-millisecond OFF passthrough and strict tracking validity checks passed.' in log
assert '[DirectTouchChecks] Rear-volume continuous stroke, hysteresis, tracking grace/rearm, UV, distance angle and rotated-plane checks passed.' in log
assert '[HandInteractionChecks] UV orientation, frame-rate-independent damping, no gravity, quaternion velocity and stale-sample checks passed.' in log
assert '[HandScreenChecks] Normal projection, distance reference, corrected XREAL palmar polarity, anatomical frame, rigid following, front-only visibility/hysteresis and tracking-loss checks passed.' in log
assert '[ThumbHeadingChecks] Thumb-side top, intermediate heading, preserved palm normal/offset/size and rigid following passed.' in log
assert '[TouchRangeChecks] Zero plane/swept pulse, exact front/rear/edge limits, configurable millimetre margin, rearm and stale/teleport rejection passed.' in log
assert '[PlanePinChecks] Detected polygon/concavity, bounded rays, wall/table pose and visible side, invalid geometry, pinch release/recovery checks passed.' in log
assert '[PlanePinBuild] Identity XR origin, shared hand/camera space and idle plane/anchor managers configured.' in log
assert '[PlaneAnchorLifecycleChecks] Actual AR Foundation factory transient refusal and Tracking+pending, trackables parenting, paced retries, no duplicates, timeout and cancellation passed.' in log
assert '[HandScreenBuild] Transparent hand overlay GLES shader compiled without errors.' in log
host_dex=ROOT/f'build/cadence-native/StableHost-v{SUFFIX}.dex'
host_java=ROOT/f'build/cadence-native/ScreenPlayerActivity-v{SUFFIX}.java'
jadx=ROOT/'tools/jadx/lib/jadx-1.5.6-all.jar'
subprocess.run([str(JAVA),'-Xmx256m','-cp',str(jadx),str(ROOT/'tools/ExtractDexClass.java'),str(apk),'dev.xreal.stablescreen.ScreenPlayerActivity',str(host_dex)],check=True,capture_output=True)
env=dict(os.environ,JADX_CONFIG_DIR=str(ROOT/'build/jadx-config'),JADX_CACHE_DIR=str(ROOT/'build/jadx-cache'))
subprocess.run([str(JAVA),'-Xmx192m','-Djava.awt.headless=true','-cp',str(jadx),'jadx.cli.JadxCLI','-j','1','--no-res','--single-class','dev.xreal.stablescreen.ScreenPlayerActivity','--single-class-output',str(host_java),str(host_dex)],check=True,env=env,capture_output=True)
host_code=host_java.read_text(encoding='utf8')
assert 'android.app.Presentation' not in host_code and 'new Presentation(' not in host_code,'Conflicting eye Presentation remains in packaged host'
assert re.search(r'ImageReader\.newInstance\(64,\s*64,',host_code),'Packaged retained primary target missing'
assert 'acquireLatestImage' in host_code and '.close()' in host_code,'Packaged primary target is not consumed and released'
assert 'displayChanged(0, this.primarySink.getSurface())' in host_code,'Unity primary must bind to offscreen reader, never another eye window'
driver_dex=ROOT/f'build/cadence-native/RefreshDriver-v{SUFFIX}.dex'
driver_java=ROOT/f'build/cadence-native/SdkRefreshDriver-v{SUFFIX}.java'
subprocess.run([str(JAVA),'-Xmx256m','-cp',str(jadx),str(ROOT/'tools/ExtractDexClass.java'),str(apk),'dev.xreal.stablescreen.SdkRefreshDriver',str(driver_dex)],check=True,capture_output=True)
subprocess.run([str(JAVA),'-Xmx192m','-Djava.awt.headless=true','-cp',str(jadx),'jadx.cli.JadxCLI','-j','1','--no-res','--single-class','dev.xreal.stablescreen.SdkRefreshDriver','--single-class-output',str(driver_java),str(driver_dex)],check=True,env=env,capture_output=True)
driver_code=driver_java.read_text(encoding='utf8')
for value in ['WindowInspector.getGlobalWindowViews()', 'postInvalidateOnAnimation()', 'canvas.drawRect(', 'continuousRefreshEnabled()', 'getDisplayId()']:
    assert value in driver_code,('Packaged continuous-refresh driver missing',value)
assert 'new Presentation(' not in driver_code and 'WindowManager.addView' not in driver_code
assert 'new FrameLayout.LayoutParams(2, 2,' in driver_code,'Refresh pulse must stay a tiny child of the existing SDK window'
access_dex=ROOT/f'build/cadence-native/PhoneGesture-v{SUFFIX}.dex'
access_java=ROOT/f'build/cadence-native/PhoneGestureService-v{SUFFIX}.java'
subprocess.run([str(JAVA),'-Xmx256m','-cp',str(jadx),str(ROOT/'tools/ExtractDexClass.java'),str(apk),'dev.xreal.stablescreen.PhoneGestureService',str(access_dex)],check=True,capture_output=True)
subprocess.run([str(JAVA),'-Xmx192m','-Djava.awt.headless=true','-cp',str(jadx),'jadx.cli.JadxCLI','-j','1','--no-res','--single-class','dev.xreal.stablescreen.PhoneGestureService','--single-class-output',str(access_java),str(access_dex)],check=True,env=env,capture_output=True)
access_code=access_java.read_text(encoding='utf8')
for value in ['continueStroke(', 'dispatchGesture(', 'phoneInputAllowed()', '.setDisplayId(0)', 'geometryMatches()', '350', '15000']:
    assert value in access_code,('Missing packaged gesture injection or guard',value)
assert 'getRootInActiveWindow(' not in access_code and 'getWindows(' not in access_code,'Gesture service must not inspect window contents'
for value in ['postDelayed(', 'removeCallbacks(', 'Math.round(', 'StablePhoneInput']:
    assert value in access_code,('Missing stationary-contact repair or diagnostics',value)
result={
    'status':'passed offline','apk':str(apk),'bytes':apk.stat().st_size,
    'sha256':hashlib.sha256(apk.read_bytes()).hexdigest(),
    'package':'dev.xreal.stablescreen','version':VERSION,'versionCode':CODE,
    'minApi':32,'targetApi':35,'launchActivity':'dev.xreal.stablescreen.ControlActivity',
    'signatureVerified':True,'zipAlignmentVerified':True,'wholeScreenProjectionServiceDeclared':True,
    'displayPath':'SDK native Rendering owns sole eye Presentation; retained 64x64 offscreen Unity primary',
    'outputReadiness':'XR provider refresh rate and positive current-frame present count; not optical verification',
    'sdkMultiResumeDisabled':True,'secureSettingsPermissionRequired':False,
    'independentTaskAffinities':affinities,'earlyJavaAndManagedDiagnosticsPackaged':True,
    'clientDisplayHelperPatchedOnce':True,'officialDisplayAarUnchanged':True,
    'vendorNativeLibrariesUnchanged':len(native_names),'managedSdkHandPollProbeEnabled':False,
    'rgbaPaddingAndTruncationTests':'passed in Unity',
    'mailboxConcurrencyTests':preparation['frameMailboxTests'],
    'defaultPosePath':'raw camera; fixed world anchor; no filter changes in this output repair',
    'rotationBufferStrategy':'Fixed square capture surface; centered content crop',
    'phoneAndCalibrationShadersSeparated':True,'glesShaderPrograms':shader_programs,
    'projectSettingsAndPluginRestored':True,'phoneConnectionUsedForArtifactValidation':False,
    'packagedHostHasNoPresentation':True,'packagedOffscreenPrimaryHasDrainAndClose':True,
    'packagedRefreshDriverUsesExistingWindow':True,
    'continuousEyeRefresh':'Default-on 2x2 dark animated View in existing SDK eye window; public WindowInspector; user switch provided; no second window or SDK native changes',
    'handPhoneControl':'User-enabled accessibility service; index normal projection with pinch or direct contact volume; continuous drag/hold; no node tree access; session control defaults off',
    'screenDistance':{'defaultMetres':2,'minimumMetres':.25,'maximumMetres':5,'viewingAnglePreserved':True},
    'directTouch':{'defaultMode':True,'pinchRequired':False,'defaultRearMetres':0,'rearRangeMetres':[0,.20],'defaultHoldMarginMetres':0,'holdMarginRangeMetres':[0,.03],'holdMarginAppliesTo':'front/rear/XY edges','settingStepMetres':.001,'rearmFrontDistanceMetres':.005,'zeroMode':'Plane contact or swept crossing; brief pulse if sample leaves configured hold volume','maximumSweptStepMetres':.05,'maximumSweptAgeSeconds':.10,'missingSampleStationaryHoldSeconds':.10,'withdrawToFrontRequiredAfterReset':True},
    'gestureRepair':'0.6.1 stationary-continuation repair retained: integer coordinates, no empty continuing paths, independent 50ms watchdog and own-activity touch probe',
    'pointerExperience':{'projection':'Index tip orthogonal to current screen plane in both contact modes','distanceCue':'Fixed grey reference disc; smaller colored dot approaches plane; abs normal distance, saturated at 20cm','referenceDiameterPerScreenDistance':.009},
    'handSkeleton':{'defaultVisible':True,'alpha':.16,'lineWidthMetres':.0013,'jointMarkers':False,'depthTest':'Always; transparent overlay'},
    'leftHandScreen':{'sessionDefault':False,'alignment':'Parallel to anatomical left palm, adjustable roll towards thumb side; no viewer-based orientation calibration','defaultHeadingDegrees':90,'headingRangeDegrees':[0,90],'headingStepDegrees':5,'palmarOffsetMetres':.03,'followRotation':True,'frameSource':'Wrist, index/little proximal joints, middle proximal direction when available; palm position only, no vendor palm rotation assumption','visibility':'Only palm-facing viewer; show below 80 degrees, hide above 85 degrees; cancel contact on visibility change','trackingLoss':'Hide screen and pause phone input; retain internal pose','inputAuthorization':'Tracking for attachment independent of explicit phone input enablement'},
    'planePin':{'selection':'Tracked horizontal/vertical ARPlane polygon; nearest bounded hit within 8m; head-centre or index ray','confirmation':'Released-then-pinched selected hand, or phone button with 2-second timer','placement':'Viewer-facing plane-parallel screen, 1cm surface offset','tracking':'Real standalone ARAnchor; valid native ID plus Tracking; pending notification flag does not reject valid tracking','creation':'Configured map directory and trackables parent; synchronous AR Foundation 5.1.5 factory; at most 12 paced attempts, 750ms interval, 15-second deadline','diagnostics':'Provider/service/map state, attempts, native error, ID/pending/tracking and added/updated/removed counters','lossBehavior':'Hide and suspend input until true 6DoF and anchor tracking recover','scanAfterPin':'Plane detection stopped after anchor actually reaches Tracking; one registered anchor','lifetime':'Current run; no persistent map save','mutuallyExclusiveWithPalm':True},
    'hardwareValidation':'User confirms 0.8.0 detects planes but fails anchor creation. 0.8.1 repairs premature lifecycle rejection and adds bounded retries/diagnostics; actual AR Foundation managed factory tested with Editor-only controlled provider, no physical phone. Native creation still needs later device verification.'
}
phone_shader=(PROJECT/'Assets/ScreenStabilizer/Shaders/PhoneScreen.shader').read_text()
assert '_Calibration' not in phone_shader and 'fwidth' not in phone_shader and 'tex2D(_MainTex,i.uv)' in phone_shader
assert 'Shader "XREAL/Stable Calibration Grid"' in (PROJECT/'Assets/ScreenStabilizer/Shaders/CalibrationGrid.shader').read_text()
hand_shader=(PROJECT/'Assets/ScreenStabilizer/Shaders/HandOverlay.shader').read_text()
assert 'Blend SrcAlpha OneMinusSrcAlpha' in hand_shader and 'ZWrite Off' in hand_shader and 'UNITY_VERTEX_OUTPUT_STEREO' in hand_shader
device_result=ROOT/f'evidence/stable-screen-v{VERSION.replace(".","")}-device-validation.json'
if not device_result.exists():device_result=ROOT/'evidence/stable-screen-device-validation.json'
if device_result.exists():
    device=json.loads(device_result.read_text(encoding='utf8'))
    if device.get('sha256')==result['sha256']:
        result['hardwareValidation']=device.get('summary','Device evidence matches this exact APK; inspect evidence for tested scope and remaining items.')
        result['hardwareEvidence']=str(device_result)
        result['phoneConnectionUsedForDeviceValidation']=device.get('phoneConnectionUsedForDeviceValidation',True)
        result['validationSource']=device.get('validationSource','Recorded device evidence')
        if 'installedApkHashVerified' in device:result['installedApkHashVerified']=device['installedApkHashVerified']
(ROOT/'evidence/stable-screen-apk-verification.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
(PROJECT/'Build/Android/stable-screen-build-info.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(result,ensure_ascii=False,indent=2))
