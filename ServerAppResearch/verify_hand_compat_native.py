"""Execute the actual ARM64 copy guard self-test offline, without a phone."""
import sys,json,struct
from pathlib import Path
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT/'tools'));sys.path.insert(0,str(ROOT/'tools/pydeps'))
from decode_native_strings import Decoder
from elftools.elf.elffile import ELFFile
from unicorn.arm64_const import *
class TestVM(Decoder):
    def hook(self,u,addr,size,data):
        name=self.plt.get(addr)
        def text(a):return bytes(u.mem_read(a,512)).split(b'\0',1)[0]
        result=None
        if name=='sysconf':result=4096
        elif name in ('mprotect','__android_log_print'):result=0
        elif name=='strcmp':
            left=text(u.reg_read(UC_ARM64_REG_X0));right=text(u.reg_read(UC_ARM64_REG_X1));result=((left>right)-(left<right))&0xffffffffffffffff
        elif name=='strrchr':
            a=u.reg_read(UC_ARM64_REG_X0);needle=u.reg_read(UC_ARM64_REG_X1)&255;offset=text(a).rfind(bytes([needle]));result=a+offset if offset>=0 else 0
        if result is not None:
            u.reg_write(UC_ARM64_REG_X0,result);u.reg_write(UC_ARM64_REG_PC,u.reg_read(UC_ARM64_REG_LR));return
        if name in ('memcmp','bcmp'):
            a=u.reg_read(UC_ARM64_REG_X0);b=u.reg_read(UC_ARM64_REG_X1);n=u.reg_read(UC_ARM64_REG_X2)
            left=bytes(u.mem_read(a,n));right=bytes(u.mem_read(b,n));value=(left>right)-(left<right)
            u.reg_write(UC_ARM64_REG_X0,value&0xffffffffffffffff);u.reg_write(UC_ARM64_REG_PC,u.reg_read(UC_ARM64_REG_LR));return
        return super().hook(u,addr,size,data)
path=ROOT/'build/libXrealHandCompatTest.so'
with path.open('rb') as f:
    elf=ELFFile(f);exports={s.name:s['st_value'] for s in elf.get_section_by_name('.dynsym').iter_symbols() if s['st_value']};entry=exports['XrealHandCompatOfflineTest']
vm=TestVM(path,entry);addresses={v:k for k,v in vm.plt.items()}
u=vm.u
for reg,val in [(UC_ARM64_REG_X0,addresses['memcpy']),(UC_ARM64_REG_X1,addresses['memmove']),(UC_ARM64_REG_SP,0x701ff000),(UC_ARM64_REG_LR,0x70200000),(UC_ARM64_REG_TPIDR_EL0,0x60000000)]:u.reg_write(reg,val)
u.emu_start(entry,0x70200000,count=2000000)
assert u.reg_read(UC_ARM64_REG_X0)==1,'Native copy guard self-test failed'
def call(address,*args):
    u.reg_write(UC_ARM64_REG_SP,0x701ff000);u.reg_write(UC_ARM64_REG_LR,0x70200000)
    for reg,value in zip((UC_ARM64_REG_X0,UC_ARM64_REG_X1,UC_ARM64_REG_X2),args):u.reg_write(reg,value)
    u.emu_start(address,0x70200000,count=3000000);return u.reg_read(UC_ARM64_REG_X0)
base=0x10000000;u.mem_map(base,0x3000000)
vendor=ROOT/'build/libnr_hand_tracking.so'
with vendor.open('rb') as f:
    e=ELFFile(f)
    for segment in e.iter_segments():
        if segment['p_type']=='PT_LOAD':u.mem_write(base+segment['p_vaddr'],segment.data())
    phdr=base+e['e_phoff'];phnum=e['e_phnum'];note=e.get_section_by_name('.note.gnu.build-id')['sh_addr']
    symbols=e.get_section_by_name('.dynsym');slots={}
    for r in e.get_section_by_name('.rela.plt').iter_relocations():
        name=symbols.get_symbol(r['r_info_sym']).name
        if name in ('memcpy','memmove'):slots[name]=base+r['r_offset']
info=0x60001000;name=0x60002000;u.mem_write(name,b'/local/libnr_hand_tracking.so\0')
u.mem_write(info,struct.pack('<QQQH',base,name,phdr,phnum)+bytes(46))
assert call(exports['XrealHandCompatOfflineInstall'],info)==1
assert call(exports['XrealHandCompatState'])==1
assert call(exports['XrealHandCompatImports'])==len(slots)>0
# Exercise the real function installed into the audited module's import slot.
original=(ROOT/'hand-compat/flora_cpu.original.txt').read_bytes();patched=(ROOT/'hand-compat/flora_cpu.repaired.txt').read_bytes()
src=0x60010000;dst=0x60020000;u.mem_write(src,original+b'\0')
replacement=struct.unpack('<Q',bytes(u.mem_read(slots['memcpy'],8)))[0]
call(replacement,dst,src,len(original)+1)
assert bytes(u.mem_read(dst,len(original)+1))==patched+b'\0'
assert call(exports['XrealHandCompatRewrites'])==1
# A different build ID is rejected before any import is changed.
before={n:bytes(u.mem_read(a,8)) for n,a in slots.items()}
u.mem_write(base+note+16,b'\xff')
assert call(exports['XrealHandCompatOfflineInstall'],info)==1
assert call(exports['XrealHandCompatState'])==2
assert all(bytes(u.mem_read(a,8))==before[n] for n,a in slots.items())
result={'status':'passed','execution':'Actual ARM64 native routines under Unicorn, with the original vendor ELF loaded','checks':['exact audited graph replacement','same-size NUL-terminated replacement','different buffer rejected','truncated buffer rejected','nonmatching data copied unchanged','original ELF build ID and import binding','copy through installed import repairs only the expected graph','different build ID rejected without writes'],'hardwareValidation':'pending'}
(ROOT/'evidence/hand-compat-native-test.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
