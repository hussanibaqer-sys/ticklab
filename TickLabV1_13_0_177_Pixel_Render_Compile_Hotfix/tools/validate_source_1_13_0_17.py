#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import math
import re
import subprocess
import sys
from pathlib import Path
from lxml import etree

ROOT = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[1]
APP = ROOT / 'src' / 'TickLab.App'
checks = 0
failures: list[str] = []


def check(condition: bool, message: str) -> None:
    global checks
    checks += 1
    if not condition:
        failures.append(message)


def text(path: Path) -> str:
    return path.read_text(encoding='utf-8-sig')

project = text(APP / 'TickLab.App.csproj')
chart = text(APP / 'Controls' / 'CandleChartControl.cs')
chart_types = text(APP / 'Controls' / 'CandleChartControl.ChartTypes.cs')
main_xaml = text(APP / 'MainWindow.xaml')

check('<Version>1.13.0.17</Version>' in project, 'Project version is 1.13.0.17')
check('<AssemblyVersion>1.13.0.17</AssemblyVersion>' in project, 'Assembly version is 1.13.0.17')
check('<FileVersion>1.13.0.17</FileVersion>' in project, 'File version is 1.13.0.17')
check((ROOT / 'TickLabV1_13_0_17.sln').exists(), 'v1.13.0.17 solution exists')
check(text(ROOT / 'VERSION.txt').strip() == '1.13.0.17', 'VERSION.txt is updated')
check('Fixed-Pitch Candle Zoom Repair' in main_xaml, 'Window title identifies the repair')
check('private const double CandleGapPixels = 3.0;' in chart, 'Compact 3-pixel candle gap remains')
check('MaximumHorizontalVisibleCandles = 1_500' in chart, '1,500-candle cap remains')

for token, label in [
    ('_detailedCandlePitchPixels', 'locked detailed pitch field'),
    ('_detailedCandlePitchSlotCount', 'locked pitch slot-count field'),
    ('QuantizeDetailedVisibleCount', 'visible-count quantizer'),
    ('CreateCrispPlotRect', 'crisp plot rectangle'),
    ('commonBodyWidthPixels', 'shared body width'),
    ('GetUniformCandleBodyWidthPixels', 'uniform body-width helper'),
    ('SnapToPixelCenter', 'physical centre snap'),
    ('MakeOddPixelWidth', 'odd wick-width normalization'),
    ('compressedPitchPixels = 2', 'two-pixel compressed lattice'),
    ('compressedWidthPixels = 1', 'one-pixel compressed candle width'),
]:
    check(token in chart + chart_types, f'Contains {label}')

check('int slotWidthPixels = Math.Max(1, slot.Right - slot.Left);' not in chart_types,
      'Detailed renderer no longer sizes every body from its individual slot width')
check('bodyCenterPixels = (bodyLeftPixels + bodyRightPixels) / 2.0;' in chart_types,
      'Wick centre is recomputed from final body bounds')
check('DrawVerticalPixelBar(\n                drawingContext,\n                wickBrush,\n                bodyCenterPixels' in chart_types,
      'Wick uses the final body centre')
check('DrawVerticalPixelBar(\n                drawingContext,\n                brush,\n                centerPixels' in chart,
      'Compressed wick and body share one centre')

# Structural checks for all C# and XAML source files.
for path in ROOT.rglob('*.cs'):
    source = text(path)
    check(source.count('{') == source.count('}'), f'Balanced braces: {path.relative_to(ROOT)}')

for path in ROOT.rglob('*.xaml'):
    try:
        etree.parse(str(path))
        check(True, f'XAML parses: {path.relative_to(ROOT)}')
    except Exception as exc:
        check(False, f'XAML parses: {path.relative_to(ROOT)} ({exc})')

# Method signature duplicate regression check inside CandleChartControl partials.
method_pattern = re.compile(
    r'\b(?:public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?[\w<>,?\[\].]+\s+(\w+)\s*\(([^)]*)\)',
    re.MULTILINE,
)
signatures: dict[tuple[str, tuple[str, ...]], list[str]] = {}
for path in (APP / 'Controls').glob('CandleChartControl*.cs'):
    source = text(path)
    for name, args in method_pattern.findall(source):
        types = []
        for arg in [a.strip() for a in args.split(',') if a.strip()]:
            arg = re.sub(r'\b(?:ref|out|in|params|this)\s+', '', arg)
            parts = arg.split('=')[0].strip().split()
            if len(parts) >= 2:
                types.append(' '.join(parts[:-1]))
            elif parts:
                types.append(parts[0])
        signatures.setdefault((name, tuple(types)), []).append(path.name)
for signature, files in signatures.items():
    # Partial overloads are fine; exact signature duplicated across partials is not.
    check(len(files) == 1, f'Unique CandleChartControl member {signature}: {files}')

# Geometry simulation matching the new renderer and layout rules.
def round_away(value: float) -> int:
    return math.floor(value + 0.5) if value >= 0 else math.ceil(value - 0.5)


def quantize(available: int, requested: int, stored_pitch: int = 0, stored_count: int = 0) -> tuple[int, int, int]:
    requested = max(1, min(1500, requested))
    if stored_pitch >= 7 and stored_count == requested:
        used = stored_pitch * requested
        remainder = available - used
        if used > 0 and used <= available and remainder <= max(16, stored_pitch * 2):
            return requested, stored_pitch, stored_count
    raw = available / max(1, requested)
    if raw < 7.0:
        return requested, 0, 0
    pitch = max(7, round_away(raw))
    count = max(1, min(1500, available // pitch))
    pitch = max(7, available // max(1, count))
    return count, pitch, count


def body_width(pitch: int, gap: int) -> int:
    maximum = max(1, pitch - 2)
    preferred = max(1, min(pitch - gap, maximum))
    if preferred % 2 == 0:
        preferred = preferred + 1 if preferred + 1 <= maximum else preferred - 1
    return max(1, preferred)

for available in range(320, 2561, 41):
    for requested in range(1, 1501, 13):
        count, pitch, stored_count = quantize(available, requested)
        check(1 <= count <= 1500, f'Count bounds P={available} N={requested}')
        if pitch >= 7:
            used = pitch * count
            check(used <= available, f'Detailed lattice fits P={available} N={requested}')
            check(available - used <= max(16, pitch * 2), f'Detailed leftover is bounded P={available} N={requested}')
            count2, pitch2, stored2 = quantize(available, count, pitch, stored_count)
            check((count2, pitch2, stored2) == (count, pitch, stored_count),
                  f'Detailed lattice is stable P={available} N={requested}')
            width = body_width(pitch, 3)
            check(width % 2 == 1, f'Body width is odd P={available} N={requested}')
            check(width <= pitch - 2, f'Body leaves separation P={available} N={requested}')
            previous_right = None
            for slot in range(min(count, 48)):
                left = slot * pitch
                right = left + pitch
                center = math.floor((left + right) / 2.0) + 0.5
                body_left = round_away(center - width / 2.0)
                body_right = body_left + width
                if body_left < left:
                    shift = left - body_left
                    body_left += shift
                    body_right += shift
                if body_right > right:
                    shift = body_right - right
                    body_left -= shift
                    body_right -= shift
                final_center = (body_left + body_right) / 2.0
                check(body_right - body_left == width, f'Uniform body width P={available} N={requested} S={slot}')
                check(abs(final_center - (math.floor(final_center) + 0.5)) < 1e-9,
                      f'Body has a pixel centre P={available} N={requested} S={slot}')
                wick_left = round_away(final_center - 0.5)
                wick_right = wick_left + 1
                check(abs((wick_left + wick_right) / 2.0 - final_center) < 1e-9,
                      f'Wick is centred P={available} N={requested} S={slot}')
                if previous_right is not None:
                    check(body_left - previous_right == pitch - width,
                          f'Uniform candle gap P={available} N={requested} S={slot}')
                previous_right = body_right
        else:
            # Compressed columns are always one pixel wide on a two-pixel pitch.
            for bucket in range(min(available // 2, 64)):
                left = bucket * 2
                right = left + 1
                check(right - left == 1, f'Compressed width bucket={bucket}')
                if bucket:
                    check(left - ((bucket - 1) * 2 + 1) == 1, f'Compressed gap bucket={bucket}')

# Protected MT5 source files must match the retained hashes.
for line in text(ROOT / 'MT5_SOURCE_SHA256.txt').splitlines():
    if not line.strip():
        continue
    expected, relative = line.split(maxsplit=1)
    data = (ROOT / relative).read_bytes()
    actual = hashlib.sha256(data).hexdigest()
    check(actual == expected, f'Protected MT5 hash: {relative}')

# Reuse the focused legacy checks that remain version-independent.
for validator in [
    'validate_duplicate_method_hotfix_1_13_0_14.py',
    'validate_bridge_write_access_hotfix_1_13_0_14.py',
]:
    result = subprocess.run(
        [sys.executable, str(ROOT / 'tools' / validator), str(ROOT)],
        capture_output=True,
        text=True,
    )
    check(result.returncode == 0, f'{validator} passes: {result.stdout}{result.stderr}')

print(f'V1.13.0.17 CHECKS PASSED: {checks - len(failures)}')
print(f'V1.13.0.17 CHECKS FAILED: {len(failures)}')
if failures:
    for failure in failures[:100]:
        print(f'FAIL: {failure}')
    sys.exit(1)
print('TickLab v1.13.0.17 fixed-pitch candle geometry validation passed.')
