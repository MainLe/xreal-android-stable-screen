"""Offline SNPE/HTP inventory and evidence audit; never connects to the phone."""
import hashlib,io,json,re,sys,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parent
PROJECT=ROOT.parent
sys.path.insert(0,str(ROOT/'tools/pydeps'))
from elftools.elf.elffile import ELFFile
from capstone import Cs,CS_ARCH_ARM64,CS_MODE_ARM

apk=PROJECT/'Build/Android/XREAL-HandFix-v1.0.2.apk'
aar=PROJECT/'package/Runtime/Plugins/Android/nr_common.aar'
names=['libSNPE.so','libSnpeHtpPrepare.so','libSnpeHtpV79Stub.so','libSnpeHtpV79Skel.so','libQnnHtp.so']
libraries=[]
with zipfile.ZipFile(apk) as package,zipfile.ZipFile(aar) as original:
    for name in names:
        data=package.read('lib/arm64-v8a/'+name)
        assert data==original.read('jni/arm64-v8a/'+name),name
        elf=ELFFile(io.BytesIO(data))
        ids=sorted({m.decode() for m in re.findall(rb'v2\.36\.0\.250627101419_123260(?:\.de9c79e9a9)?',data)})
        assert 'v2.36.0.250627101419_123260' in ids,name
        libraries.append({'name':name,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest(),'machine':elf['e_machine'],'needed':[t.needed for t in elf.get_section_by_name('.dynamic').iter_tags() if t.entry.d_tag=='DT_NEEDED'],'buildStrings':ids,'identicalToOfficialSDK':True})

log=(ROOT/'evidence/hands-current.log').read_text(encoding='utf8',errors='replace').splitlines()
patterns=[r'GetAdspLibraryPath success',r'Created user PD.*Unsigned:Y',r'Successfully opened file .*libSnpeHtpV79Skel',r'remote_handle64_open: opened.*libSnpeHtpV79Skel',r'Skel lib id mismatch',r'QNN_COMMON_ERROR_INCOMPATIBLE_BINARIES',r'dsp_support=false']
evidence=[]
for pattern in patterns:
    found=next(({'line':i+1,'text':line} for i,line in enumerate(log) if '19415' in line and re.search(pattern,line)),None)
    assert found,pattern
    evidence.append(found)

with zipfile.ZipFile(PROJECT/'ControlGlasses-3.1.0.20251118115716-release.apk') as z:
    vendor=z.read('lib/arm64-v8a/libnr_hand_tracking.so')
assert hashlib.sha256(vendor).hexdigest()=='925bc761ae0a9d9fbd06a80799e6342c95a48d356cc38ac4a31576df90b74c4f'
elf=ELFFile(io.BytesIO(vendor))
def read_va(address,size):
    for seg in elf.iter_segments():
        if seg['p_type']=='PT_LOAD' and seg['p_vaddr']<=address and address+size<=seg['p_vaddr']+seg['p_filesz']:
            offset=address-seg['p_vaddr']+seg['p_offset'];return vendor[offset:offset+size]
    raise ValueError(hex(address))
assert read_va(0xb2e86,18)==b'ADSP_LIBRARY_PATH\x00'
ins=list(Cs(CS_ARCH_ARM64,CS_MODE_ARM).disasm(read_va(0x23e56d4,16),0x23e56d4))
assert ins[1].mnemonic=='mov' and ins[1].op_str=='w2, wzr'
assert ins[3].mnemonic=='bl' and ins[3].op_str=='#0x2a18050'
# .rela.plt plus the standard 32-byte AArch64 PLT header/16-byte entries identifies the callee.
plt=elf.get_section_by_name('.plt');relocs=elf.get_section_by_name('.rela.plt');symbols=elf.get_section(relocs['sh_link'])
index=(0x2a18050-plt['sh_addr']-32)//16
assert symbols.get_symbol(relocs.get_relocation(index)['r_info_sym']).name=='setenv'

result={'status':'passed offline','sourceAPK':str(apk),'sourceAPKsha256':hashlib.sha256(apk.read_bytes()).hexdigest(),'libraries':libraries,'log':'evidence/hands-current.log','evidence':evidence,'officialEnvironmentSetter':{'address':'0x23e56d4','environmentVariable':'ADSP_LIBRARY_PATH','setenvOverwrite':0,'instructions':[f'{i.address:#x}: {i.mnemonic} {i.op_str}' for i in ins]},'observedFailure':'V79 Skel opened from /odm while APK carries its own matching 2.36 host/stub/skel set; version verification returned detected empty and error 2008','candidate':'Prepend applicationInfo.nativeLibraryDir before runtime initialization and hands requests; keep SDK availability checks and CPU fallback','notProven':['Actual system Skel build ID (detected was empty)','Whether QcSoCServiceUtils.dll lookup failures are causal','That app-local DSP libraries are accessible on device','That DSP inference and hand tracking will succeed']}
(ROOT/'evidence/dsp-runtime-audit.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps({'status':result['status'],'librariesAudited':len(libraries),'officialSetterOverwritesExistingPath':False,'phoneConnectionUsed':False,'report':str(ROOT/'evidence/dsp-runtime-audit.json')},ensure_ascii=False,indent=2))
