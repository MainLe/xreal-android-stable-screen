"""Read-only APK inventory, ELF load alignment and JNI/loader string audit."""
import hashlib, json, re, struct, sys, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APK = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT.parent / 'ControlGlasses-3.1.0.20251118115716-release.apk'
OUT = ROOT / 'evidence'
OUT.mkdir(parents=True, exist_ok=True)

def cstr(data, off):
    return data[off:data.find(b'\0', off)].decode('utf-8', 'replace')

def elf(data):
    assert data[:5] == b'\x7fELF\x02', 'Expected ELF64'
    phoff, shoff = struct.unpack_from('<QQ', data, 32)
    phsize, phnum, shsize, shnum = struct.unpack_from('<HHHH', data, 54)
    loads=[]
    for i in range(phnum):
        p=struct.unpack_from('<IIQQQQQQ', data, phoff+i*phsize)
        if p[0]==1: loads.append(dict(offset=p[2],vaddr=p[3],file_size=p[5],mem_size=p[6],alignment=p[7]))
    sections=[struct.unpack_from('<IIQQQQIIQQ',data,shoff+i*shsize) for i in range(shnum)]
    exports=[]; imports=[]; needed=[]
    for sec in sections:
        if sec[1] == 11 and sec[9]:
            strs=sections[sec[6]]; strdata=data[strs[4]:strs[4]+strs[5]]
            for o in range(sec[4],sec[4]+sec[5],sec[9]):
                name,info,other,idx,value,size=struct.unpack_from('<IBBHQQ',data,o)
                if name:
                    n=cstr(strdata,name)
                    (imports if idx==0 else exports).append(n)
        if sec[1] == 6:
            strs=sections[sec[6]]; strdata=data[strs[4]:strs[4]+strs[5]]
            for o in range(sec[4],sec[4]+sec[5],16):
                tag,val=struct.unpack_from('<QQ',data,o)
                if tag==1:needed.append(cstr(strdata,val))
    strings=[x.decode('ascii') for x in re.findall(rb'[ -~]{7,}',data)]
    relevant=[s for s in strings if re.search(r'JNI|Android|android/|/system/|/dev/|SDK_INT|sdk\.version|sdk_version|page.size|PAGE_SIZE|dlopen|dlsym|libbinder|permission|UsbDevice|usbfs|hidraw|AHardwareBuffer|libgui|libutils|libcutils|ro\.build|\.so$|start.*service|signature|SurfaceFlinger',s,re.I)]
    return dict(load_segments=loads,supports_16k_elf_alignment=all(x['alignment']>=16384 and (x['offset']-x['vaddr'])%16384==0 for x in loads),needed=needed,imports=imports,exports=exports,relevant_strings=relevant)

report=dict(apk=str(APK),bytes=APK.stat().st_size,sha256=hashlib.file_digest(APK.open('rb'),'sha256').hexdigest(),native={})
with zipfile.ZipFile(APK) as z:
    report['entries']=[dict(name=x.filename,size=x.file_size,compression=x.compress_type) for x in z.infolist()]
    for x in z.infolist():
        if x.filename.startswith('lib/') and x.filename.endswith('.so'):
            print(x.filename,flush=True)
            report['native'][x.filename]=elf(z.read(x))
    report['dex_strings']={}
    for n in z.namelist():
        if re.fullmatch(r'classes\d*\.dex',n):
            data=z.read(n); count,off=struct.unpack_from('<II',data,56); strings=[]
            for i in range(count):
                p=struct.unpack_from('<I',data,off+4*i)[0]
                while data[p]&128:p+=1
                p+=1; strings.append(cstr(data,p))
            report['dex_strings'][n]=[s for s in strings if re.search(r'nreal|xreal|nrservice|sdk init|sdk failed|start.*sdk|无法|启动|unsupported|not support|android.os.Build|Landroid/hardware/usb|Landroid/view/SurfaceControl|Landroid/os/ServiceManager',s,re.I)]
(OUT/'apk-audit.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:{'16k':v['supports_16k_elf_alignment'],'needed':v['needed']} for k,v in report['native'].items()},indent=2))
