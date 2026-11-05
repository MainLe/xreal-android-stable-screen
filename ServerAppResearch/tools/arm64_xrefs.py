import struct,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).parent/'pydeps'))
from elftools.elf.elffile import ELFFile
def refs(path,targets):
    with open(path,'rb') as f:
        e=ELFFile(f);section=e.get_section_by_name('.text');data=section.data();base=section['sh_addr']
    pages={t&~4095 for t in targets}
    for offset in range(0,len(data)-32,4):
        w=struct.unpack_from('<I',data,offset)[0]
        if w&0x9f000000!=0x90000000:continue
        imm=((w>>29)&3)|(((w>>5)&0x7ffff)<<2)
        if imm&(1<<20):imm-=1<<21
        address=((base+offset)&~4095)+(imm<<12)
        if address not in pages:continue
        for p in range(offset+4,offset+32,4):
            a=struct.unpack_from('<I',data,p)[0]
            if a&0xffc00000==0x91000000 and (a>>5)&31==w&31 and address+((a>>10)&4095) in targets:
                print(hex(address+((a>>10)&4095)),hex(base+offset),hex(base+p))
if __name__=='__main__':refs(sys.argv[1],{int(t,0) for t in sys.argv[2:]})
