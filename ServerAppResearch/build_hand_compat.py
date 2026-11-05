"""Build a narrowly guarded, process-local compatibility shim from audited local inputs."""
import hashlib,json,os,re,subprocess,sys,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parent;PROJECT=ROOT.parent
sys.path.insert(0,str(ROOT/'tools/pydeps'))
from elftools.elf.elffile import ELFFile
import io
SOURCE=ROOT/'evidence/hand-graph-bbb6f0d2.txt'
original=SOURCE.read_bytes()
assert len(original)==3785 and hashlib.sha256(original).hexdigest().startswith('bbb6f0d2')
text=original.decode('utf8')
# Compact whitespace/comments to keep the same allocation length after restoring the two edges.
patched='\n'.join(line.strip() for line in text.splitlines() if line.strip() and not line.lstrip().startswith('#'))+'\n'
assert patched.count('calculator: "HandLiftCalculator"')==1 and patched.count('calculator: "HandFilterCalculator"')==1
patched=patched.replace('calculator: "HandLiftCalculator"','calculator: "HandLiftCalculator"\ninput_stream: "HEADPOSE:head_pose_out"')
patched=patched.replace('calculator: "HandFilterCalculator"','calculator: "HandFilterCalculator"\ninput_stream: "GR_KPT2D_INPUT:kpt2d"')
assert len(patched.encode())<=len(original)
repaired=patched.encode().ljust(len(original),b' ')
# Verify producers and node-scoped connections instead of just testing text presence.
def nodes(data):
    result={};depth=0;start=None
    for line in data.decode().splitlines():
        if line.strip()=='node {':start=[];depth=0
        if start is not None:
            start.append(line);depth+=line.count('{')-line.count('}')
            if depth==0:
                body='\n'.join(start);name=re.search(r'calculator:\s*"([^"]+)"',body)[1];result[name]=body;start=None
    return result
n=nodes(repaired);old=nodes(original)
assert 'HEADPOSE:head_pose_out' not in old['HandLiftCalculator']
assert 'GR_KPT2D_INPUT:kpt2d' not in old['HandFilterCalculator']
assert 'HEADPOSE:head_pose_out' in n['HandLiftCalculator']
assert 'GR_KPT2D_INPUT:kpt2d' in n['HandFilterCalculator']
assert 'HEADPOSE_OUTPUT:head_pose_out' in n['HandDetTrackCalculator']
assert 'LANDMARK_OUTPUT:kpt2d' in n['HandLandmarkCalculator']
assert re.findall(r'model_name:\s*"[^"]+"',text)==re.findall(r'model_name:\s*"[^"]+"',patched)
with zipfile.ZipFile(PROJECT/'ControlGlasses-3.1.0.20251118115716-release.apk') as z:library=z.read('lib/arm64-v8a/libnr_hand_tracking.so')
assert hashlib.sha256(library).hexdigest()=='925bc761ae0a9d9fbd06a80799e6342c95a48d356cc38ac4a31576df90b74c4f'
elf=ELFFile(io.BytesIO(library));note=elf.get_section_by_name('.note.gnu.build-id');buildid=bytes.fromhex(next(note.iter_notes())['n_desc'])
WORK=ROOT/'hand-compat';OUT=PROJECT/'Assets/Plugins/Android/arm64-v8a';OUT.mkdir(parents=True,exist_ok=True)
def literal(data):return '{'+','.join(str(c) for c in data)+'}'
(WORK/'graph_payload.h').write_text('#pragma once\n#include <cstddef>\nstatic constexpr size_t kGraphSize='+str(len(original))+';\nstatic constexpr unsigned char kVendorBuildId[]='+literal(buildid)+';\nstatic constexpr unsigned char kOriginalGraph[]='+literal(original+b'\0')+';\nstatic constexpr unsigned char kRepairedGraph[]='+literal(repaired+b'\0')+';\nstatic_assert(sizeof(kOriginalGraph)==sizeof(kRepairedGraph),"Graph allocation changed");\n')
(WORK/'flora_cpu.original.txt').write_bytes(original);(WORK/'flora_cpu.repaired.txt').write_bytes(repaired)
CLANG=Path(os.environ.get('XREAL_ANDROID_SDK',r'D:/FilesLibrary/SDK'))/'ndk/25.1.8937393/toolchains/llvm/prebuilt/windows-x86_64/bin/clang++.exe'
env=dict(os.environ,TEMP=str(ROOT/'build'),TMP=str(ROOT/'build'))
common=[str(CLANG),'--target=aarch64-linux-android29','-std=c++17','-O2','-shared','-fPIC','-fvisibility=hidden','-ffunction-sections','-fdata-sections','-static-libstdc++','-Wl,--gc-sections','-Wl,--exclude-libs,ALL','-Wl,-z,relro,-z,now','-Wl,-z,max-page-size=16384','-Wl,-soname,libXrealHandCompat.so',str(WORK/'hand_compat.cpp'),'-ldl','-llog']
final=OUT/'libXrealHandCompat.so';test=ROOT/'build/libXrealHandCompatTest.so'
subprocess.run(common+['-o',str(final)],check=True,env=env)
subprocess.run(common+['-DXREAL_HAND_COMPAT_OFFLINE_TEST','-o',str(test)],check=True,env=env)
info={'vendorLibrarySha256':hashlib.sha256(library).hexdigest(),'vendorBuildId':buildid.hex(),'originalGraphSha256':hashlib.sha256(original).hexdigest(),'repairedGraphSha256':hashlib.sha256(repaired).hexdigest(),'graphBytes':len(original),'sameAllocationSize':len(original)==len(repaired),'restoredEdges':['HandLiftCalculator.HEADPOSE <- head_pose_out','HandFilterCalculator.GR_KPT2D_INPUT <- kpt2d'],'modelsUnchanged':True,'shimSha256':hashlib.sha256(final.read_bytes()).hexdigest(),'scope':'Only audited hand module imports and exact matching graph; process-local; original libraries unchanged'}
(ROOT/'evidence/hand-compat-build.json').write_text(json.dumps(info,indent=2));print(json.dumps(info,indent=2))
