"""Summarize saved, read-only Stable Screen / spatial Demo display evidence.

SurfaceFlinger latency rows describe queued buffers. They do not measure the
optical refresh of the SDK's single-buffer native eye output.
"""
import json
import re
import statistics
from pathlib import Path

ROOT = Path(__file__).resolve().parent
EVIDENCE = ROOT / "evidence"


def intervals(values):
    dt = [(b - a) / 1e6 for a, b in zip(values, values[1:])]
    if not dt:
        return None
    return {
        "intervals": len(dt),
        "meanMs": statistics.mean(dt),
        "minMs": min(dt),
        "maxMs": max(dt),
        "over25Ms": sum(x > 25 for x in dt),
        "zeroIntervals": sum(x == 0 for x in dt),
        "negativeIntervals": sum(x < 0 for x in dt),
    }


def surface(path):
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    rows = []
    for line in lines[1:]:
        fields = line.split()
        if len(fields) == 3 and all(x.isdigit() for x in fields):
            row = [int(x) for x in fields]
            if all(0 < x < 2**63 - 1 for x in row):
                rows.append(row)
    return {
        "file": str(path.relative_to(ROOT)),
        "reportedRefreshPeriodNs": int(lines[0]) if lines else None,
        "validBufferRecords": len(rows),
        **{
            name: intervals([r[col] for r in rows])
            for col, name in enumerate(("desiredPresent", "actualPresent", "frameReady"))
        },
    }


def log(path):
    text = path.read_text(encoding="utf-8-sig")
    lines = text.splitlines()
    errors = [s for s in lines if "[ErrorCheck] NativeRendering FrameWait" in s]
    health = []
    for line in lines:
        if "XrRuntimeHealth: " in line:
            health.append(json.loads(line.split("XrRuntimeHealth: ", 1)[1]))
    metrics = re.findall(r"\[NRMetrics\] ([^\r\n]+)", text)
    return {
        "file": str(path.relative_to(ROOT)),
        "frameWaitFailureLines": len(errors),
        "firstFailure": errors[0] if errors else None,
        "lastFailure": errors[-1] if errors else None,
        "healthWindows": len(health),
        "meanUpdateFps": statistics.mean(d["updateFps"] for d in health) if health else None,
        "lastHealth": health[-1] if health else None,
        "firstNativeMetrics": metrics[0] if metrics else None,
        "lastNativeMetrics": metrics[-1] if metrics else None,
        "singleBufferConfiguration": next((s for s in lines if "single_buffer:" in s), None),
    }


result = {
    "device": "Xiaomi 15 / Android 17 API 37 / Air 2 Ultra stereo 3840x1080 60 Hz",
    "stableVersion": "0.3.2",
    "demoVersion": "1.0.5-screen-compare",
    "stableGridSurfaces": [surface(p) for p in sorted((EVIDENCE / "stable-grid-cadence").glob("surface-*.txt"))],
    "demoEyeSurfaces": [surface(p) for p in sorted((EVIDENCE / "demo-cadence").glob("surface-*.txt"))],
    "demoPhoneSurfaces": [surface(p) for p in sorted((EVIDENCE / "demo-cadence").glob("phone-surface*.txt"))],
    "logs": [log(EVIDENCE / name) for name in (
        "stable-grid-cadence-baseline.log", "demo-cadence-baseline.log",
        "demo-cadence-current.log", "demo-cadence-tail.log")],
    "limitations": [
        "Snapshot windows overlap slightly and must not be pooled as independent frames.",
        "Demo eye layer has no usable SF latency buffer records; this is not evidence of a frozen eye image.",
        "Demo native startup explicitly reports single_buffer:1. Its optical presentation cadence is not measured here.",
        "Demo phone Unity mirror is not the eye output and must not substitute for its missing latency records.",
        "Native FramePresent and DroppedFrame fields are provider-defined; their values are not treated as cumulative frame counts or measured FPS.",
        "The head-pose source and default filter policy match, but the scene geometry and complete rendering paths are not controlled-identical.",
        "Changing to the native output path remains a hypothesis to test, not a verified jitter fix.",
    ],
}
output = EVIDENCE / "display-cadence-comparison.json"
output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(f"Saved {output}")
for d in result["logs"]:
    print(d["file"], "FrameWait failures:", d["frameWaitFailureLines"], "mean Update FPS:", d["meanUpdateFps"])
for label in ("stableGridSurfaces", "demoEyeSurfaces", "demoPhoneSurfaces"):
    print(label, [(d["validBufferRecords"], d["actualPresent"]) for d in result[label]])
