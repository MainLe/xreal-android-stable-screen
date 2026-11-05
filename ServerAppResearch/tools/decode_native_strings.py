"""Offline ARM64 emulation of the supplied runtime's string decoder (no Android process writes)."""
import sys,struct,re,json
from pathlib import Path
sys.path.insert(0,str(Path(__file__).parent/'pydeps'))
from elftools.elf.elffile import ELFFile
from unicorn import Uc,UC_ARCH_ARM64,UC_MODE_ARM,UC_HOOK_CODE
from unicorn.arm64_const import *

def nibble(s):
    out=[]
    for i in range(0,len(s)-1,2):
        a=s[i]-(s[i-1] if i else 1);b=s[i+1]-s[i]
        if a<0:a+=14
        if b<0:b+=14
        out.append(((a<<4)|(b if i==0 else b&15))&255)
    return bytes(out)

class Decoder:
    def __init__(self,path,func):
        self.func=func;self.u=Uc(UC_ARCH_ARM64,UC_MODE_ARM)
        with open(path,'rb') as f:
            elf=ELFFile(f)
            for seg in elf.iter_segments():
                if seg['p_type']!='PT_LOAD':continue
                start=seg['p_vaddr']&~4095;size=(seg['p_vaddr']+seg['p_memsz']+4095&~4095)-start
                self.u.mem_map(start,size);self.u.mem_write(seg['p_vaddr'],seg.data())
            self.plt={};sym=elf.get_section_by_name('.dynsym');rel=elf.get_section_by_name('.rela.plt');plt=elf.get_section_by_name('.plt')
            for i,r in enumerate(rel.iter_relocations()):self.plt[plt['sh_addr']+32+i*16]=sym.get_symbol(r['r_info_sym']).name
        self.u.mem_map(0x60000000,0x200000);self.u.mem_map(0x70000000,0x200000)
        self.u.hook_add(UC_HOOK_CODE,self.hook,begin=plt['sh_addr'],end=plt['sh_addr']+plt['sh_size'])
    def hook(self,u,addr,size,data):
        name=self.plt.get(addr)
        if not name:return
        a=u.reg_read(UC_ARM64_REG_X0);b=u.reg_read(UC_ARM64_REG_X1);n=u.reg_read(UC_ARM64_REG_X2)
        if name in ('memcpy','memmove'):u.mem_write(a,bytes(u.mem_read(b,n)))
        elif name=='memset':u.mem_write(a,bytes([b&255])*n)
        else:raise RuntimeError('Unexpected import '+name)
        u.reg_write(UC_ARM64_REG_PC,u.reg_read(UC_ARM64_REG_LR))
    def decode(self,s):
        raw=nibble(s)
        if len(raw)<5:return b''
        u=self.u;u.mem_write(0x60000000,raw);u.mem_write(0x60100000,b'\0'*(len(raw)+32))
        for reg,val in [(UC_ARM64_REG_X0,struct.unpack_from('<I',raw)[0]),(UC_ARM64_REG_X1,0x60000004),(UC_ARM64_REG_X2,len(raw)-4),(UC_ARM64_REG_X3,0x60100000),(UC_ARM64_REG_X4,len(raw)),(UC_ARM64_REG_SP,0x701ff000),(UC_ARM64_REG_LR,0x70200000)]:u.reg_write(reg,val)
        u.emu_start(self.func,0x70200000,count=1000000)
        return bytes(u.mem_read(0x60100000,len(raw)-4))

if __name__=='__main__':
    decoder=Decoder(sys.argv[1],int(sys.argv[2],0));d=Path(sys.argv[3] if len(sys.argv)>3 else sys.argv[1]).read_bytes()
    result=[]
    for m in re.finditer(rb'[\x20-\x7e]{20,}\x00',d[:0x900000]):
        s=m[0][:-1]
        if len(s)%2:continue
        try:
            value=decoder.decode(s).rstrip(b'\x00')
            if value and all(c in (9,10,13) or 32<=c<127 for c in value):result.append({'offset':hex(m.start()),'text':value.decode()})
        except Exception:pass
    Path('ServerAppResearch/evidence/decoded-hand-strings.json').write_text(json.dumps(result,indent=2),encoding='utf8')
    print('Decoded',len(result))
    for r in result:
        if any(t in r['text'] for t in ('graph_flora','HEADPOSE','GR_KPT2D','calculator:')):print(r)
