// Compatibility for one audited XREAL 3.1 hand graph. No device/system changes.
#include <atomic>
#include <thread>
#include <chrono>
#include <cstring>
#include <cstdlib>
#include <dlfcn.h>
#include <link.h>
#include <elf.h>
#include <sys/mman.h>
#include <unistd.h>
#include <android/log.h>
#include "graph_payload.h"

namespace {
using Copy=void*(*)(void*,const void*,size_t);
Copy originalCopy=nullptr,originalMove=nullptr;
std::atomic<int> state{-1},imports{0},rewrites{0};
std::atomic<bool> started{false};
bool Match(const void* src,size_t n){return src&&(n==kGraphSize||(n==kGraphSize+1&&((const char*)src)[kGraphSize]==0))&&std::memcmp(src,kOriginalGraph,kGraphSize)==0;}
void* CopyGraph(void* dst,const void* src,size_t n,Copy real,bool record){
 if(Match(src,n)){
  void* result=real(dst,kRepairedGraph,n);
  if(record){int count=++rewrites;if(count<=3)__android_log_print(ANDROID_LOG_INFO,"XrealHandCompat","Repaired audited flora CPU graph, copy=%d",count);}
  return result;
 }
 return real(dst,src,n);
}
void* PatchedCopy(void*d,const void*s,size_t n){return CopyGraph(d,s,n,originalCopy,true);}
void* PatchedMove(void*d,const void*s,size_t n){return CopyGraph(d,s,n,originalMove,true);}
bool SelfTest(){
 char dest[kGraphSize+2]{};
 if(!Match(kOriginalGraph,kGraphSize)||!Match(kOriginalGraph,kGraphSize+1))return false;
 CopyGraph(dest,kOriginalGraph,kGraphSize+1,originalCopy,false);
 if(std::memcmp(dest,kRepairedGraph,kGraphSize+1))return false;
 char changed[kGraphSize+1];originalCopy(changed,kOriginalGraph,kGraphSize+1);changed[kGraphSize/2]^=1;
 if(Match(changed,kGraphSize)||Match(changed,kGraphSize-1)||Match(kOriginalGraph,kGraphSize-1))return false;
 CopyGraph(dest,changed,kGraphSize+1,originalCopy,false);
 return std::memcmp(dest,changed,kGraphSize+1)==0;
}
bool BuildMatches(const dl_phdr_info* info){
 for(size_t i=0;i<info->dlpi_phnum;i++){
  const auto&ph=info->dlpi_phdr[i];if(ph.p_type!=PT_NOTE)continue;
  const unsigned char*p=(const unsigned char*)(info->dlpi_addr+ph.p_vaddr);size_t pos=0;
  while(pos+sizeof(ElfW(Nhdr))<=ph.p_memsz){
   auto*n=(const ElfW(Nhdr)*)(p+pos);pos+=sizeof(*n);
   size_t name=(n->n_namesz+3)&~size_t(3),desc=(n->n_descsz+3)&~size_t(3);
   if(name>ph.p_memsz-pos||desc>ph.p_memsz-pos-name)break;
   if(n->n_type==NT_GNU_BUILD_ID&&n->n_namesz==4&&!std::memcmp(p+pos,"GNU",4)&&n->n_descsz==sizeof(kVendorBuildId))return !std::memcmp(p+pos+name,kVendorBuildId,sizeof(kVendorBuildId));
   pos+=name+desc;
  }
 }
 return false;
}
int Install(dl_phdr_info* info,size_t,void*){
 const char* name=std::strrchr(info->dlpi_name,'/');name=name?name+1:info->dlpi_name;
 if(std::strcmp(name,"libnr_hand_tracking.so"))return 0;
 if(!BuildMatches(info)){state=2;return 1;}
 const ElfW(Dyn)*dynamic=nullptr;
 for(size_t i=0;i<info->dlpi_phnum;i++)if(info->dlpi_phdr[i].p_type==PT_DYNAMIC)dynamic=(const ElfW(Dyn)*)(info->dlpi_addr+info->dlpi_phdr[i].p_vaddr);
 if(!dynamic){state=2;return 1;}
 const char* strings=nullptr;const ElfW(Sym)*symbols=nullptr;const ElfW(Rela)*relocations=nullptr;size_t count=0;bool rela=true;
 auto ptr=[&](uintptr_t p){return p<info->dlpi_addr?p+info->dlpi_addr:p;};
 for(auto*d=dynamic;d->d_tag!=DT_NULL;d++)switch(d->d_tag){
  case DT_STRTAB:strings=(const char*)ptr(d->d_un.d_ptr);break;
  case DT_SYMTAB:symbols=(const ElfW(Sym)*)ptr(d->d_un.d_ptr);break;
  case DT_JMPREL:relocations=(const ElfW(Rela)*)ptr(d->d_un.d_ptr);break;
  case DT_PLTRELSZ:count=d->d_un.d_val/sizeof(ElfW(Rela));break;
  case DT_PLTREL:rela=d->d_un.d_val==DT_RELA;break;
 }
 if(!strings||!symbols||!relocations||!rela){state=2;return 1;}
 int installed=0;long pageSize=sysconf(_SC_PAGESIZE);
 for(size_t i=0;i<count;i++){
  const auto&r=relocations[i];if(ELF64_R_TYPE(r.r_info)!=R_AARCH64_JUMP_SLOT)continue;
  const char*s=strings+symbols[ELF64_R_SYM(r.r_info)].st_name;void* replacement=nullptr;
  if(!std::strcmp(s,"memcpy"))replacement=(void*)&PatchedCopy;
  else if(!std::strcmp(s,"memmove"))replacement=(void*)&PatchedMove;
  else continue;
  auto*slot=(uintptr_t*)(info->dlpi_addr+r.r_offset);
  if(__atomic_load_n(slot,__ATOMIC_ACQUIRE)!=(uintptr_t)replacement){
   uintptr_t page=(uintptr_t)slot&~(uintptr_t(pageSize)-1);
   // Restore the original segment protection, including RELRO, after changing this data slot.
   bool writable=false;
   for(size_t j=0;j<info->dlpi_phnum;j++){
    auto&ph=info->dlpi_phdr[j];uintptr_t a=info->dlpi_addr+ph.p_vaddr,b=a+ph.p_memsz;
    if(ph.p_type==PT_LOAD&&(uintptr_t)slot>=a&&(uintptr_t)slot<b)writable=(ph.p_flags&PF_W)!=0;
   }
   for(size_t j=0;j<info->dlpi_phnum;j++){
    auto&ph=info->dlpi_phdr[j];uintptr_t a=(info->dlpi_addr+ph.p_vaddr)&~(uintptr_t(pageSize)-1),b=(info->dlpi_addr+ph.p_vaddr+ph.p_memsz+pageSize-1)&~(uintptr_t(pageSize)-1);
    if(ph.p_type==PT_GNU_RELRO&&(uintptr_t)slot>=a&&(uintptr_t)slot<b)writable=false;
   }
   if(mprotect((void*)page,pageSize,PROT_READ|PROT_WRITE)){state=3;return 1;}
   __atomic_store_n(slot,(uintptr_t)replacement,__ATOMIC_RELEASE);
   if(!writable&&mprotect((void*)page,pageSize,PROT_READ)){state=3;return 1;}
  }
  installed++;
 }
 imports=installed;state=installed>0?1:2;return 1;
}
}
extern "C" __attribute__((visibility("default"))) int XrealHandCompatStart(){
 if(started.exchange(true))return state.load();
 originalCopy=(Copy)dlsym(RTLD_DEFAULT,"memcpy");originalMove=(Copy)dlsym(RTLD_DEFAULT,"memmove");
 if(!originalCopy||!originalMove||!SelfTest()){state=4;return 4;}
 state=0;
 std::thread([]{for(;;){if(!dl_iterate_phdr(Install,nullptr)){state=0;imports=0;}std::this_thread::sleep_for(std::chrono::milliseconds(state.load()==0?5:250));}}).detach();
 return 0;
}
extern "C" __attribute__((visibility("default"))) int XrealHandCompatState(){return state.load();}
extern "C" __attribute__((visibility("default"))) int XrealHandCompatImports(){return imports.load();}
extern "C" __attribute__((visibility("default"))) int XrealHandCompatRewrites(){return rewrites.load();}
#ifdef XREAL_HAND_COMPAT_OFFLINE_TEST
extern "C" __attribute__((visibility("default"))) int XrealHandCompatOfflineTest(Copy copy,Copy move){originalCopy=copy;originalMove=move;return SelfTest()?1:0;}
extern "C" __attribute__((visibility("default"))) int XrealHandCompatOfflineInstall(dl_phdr_info* info){return Install(info,0,nullptr);}
#endif
