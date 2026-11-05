"""Import bundled TMP essential assets without overwriting user assets."""
from pathlib import Path
import tarfile

ROOT=Path(__file__).resolve().parents[2]
PACKAGE=ROOT/'Library/PackageCache/com.unity.textmeshpro@3.0.7/Package Resources/TMP Essential Resources.unitypackage'
allowed=(ROOT/'Assets/TextMesh Pro').resolve()
written=0
with tarfile.open(PACKAGE,'r:gz') as archive:
    members={m.name:m for m in archive.getmembers()}
    for n,m in members.items():
        if not n.endswith('/pathname'):continue
        pathname=archive.extractfile(m).read().decode('utf8').strip()
        destination=(ROOT/pathname).resolve()
        if not destination.is_relative_to(allowed):raise ValueError(pathname)
        folder=n.rsplit('/',1)[0]
        for suffix,target in [('asset',destination),('asset.meta',Path(str(destination)+'.meta'))]:
            item=members.get(folder+'/'+suffix)
            if item is None:continue
            data=archive.extractfile(item).read()
            if target.exists():
                if target.read_bytes()!=data:raise RuntimeError('Existing user asset differs: '+str(target))
                continue
            target.parent.mkdir(parents=True,exist_ok=True)
            target.write_bytes(data);written+=1
print('TMP essential files imported:',written)
