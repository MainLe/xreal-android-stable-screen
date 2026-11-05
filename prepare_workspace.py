
from pathlib import Path
root = Path(__file__).resolve().parent
for name in ['Build/Android', 'ServerAppResearch/build', 'ServerAppResearch/evidence']:
    (root / name).mkdir(parents=True, exist_ok=True)
print('Workspace directories prepared. Provide the local SDK dependencies described in README.md.')
