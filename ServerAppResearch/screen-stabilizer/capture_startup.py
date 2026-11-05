"""Bounded, filtered wireless ADB startup trace for the authorized XREAL test."""
import re, subprocess, threading, time, argparse
from pathlib import Path

root = Path(__file__).resolve().parents[1]
adb = r'D:\FilesLibrary\SDK\platform-tools\adb.exe'
args=argparse.ArgumentParser();args.add_argument('--name',default='stable-screen-live-20261102');args.add_argument('--seconds',type=int,default=45);options=args.parse_args()
if not re.fullmatch(r'[a-zA-Z0-9_-]+',options.name):raise ValueError('Invalid evidence name')
pattern = re.compile(r'StableScreenBoot|NRXRApp|NRUnityPlayer|NRFakeActivity|ActivityTaskManager.*(dev.xreal|nreal|cannot host tasks)|XrealServer|SecondaryDisplayPolicy.*Force enable|AndroidRuntime.*(dev.xreal|nreal|FATAL)|XREAL.*(FATAL|ERROR)|Unity.*(Glasses Start|Start Failed|CreateSession)')
lines = []
process = subprocess.Popen([adb, 'logcat', '-v', 'threadtime', '-T', '1'], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding='utf8', errors='replace')
def read():
    for line in process.stdout:
        if pattern.search(line):
            lines.append(line)
            if len(lines) > 2500: del lines[:500]
reader = threading.Thread(target=read, daemon=True)
reader.start()
duration=max(1,min(options.seconds,55))
print(f'Capturing filtered XREAL startup for {duration} seconds', flush=True)
time.sleep(duration)
process.terminate()
reader.join(3)
output = root / ('evidence/'+options.name+'.log')
output.write_text(''.join(lines), encoding='utf8')
print(f'Saved {len(lines)} relevant lines: {output}', flush=True)
print(''.join(lines[-110:]))
