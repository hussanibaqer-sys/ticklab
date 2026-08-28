#!/usr/bin/env python3
from __future__ import annotations
import hashlib, math, re, subprocess, sys
from pathlib import Path
from lxml import etree

ROOT = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[1]
APP = ROOT / 'src' / 'TickLab.App'
checks = 0
failures: list[str] = []

def check(ok: bool, msg: str) -> None:
    global checks
    checks += 1
    if not ok:
        failures.append(msg)

def text(path: Path) -> str:
    return path.read_text(encoding='utf-8-sig')

project = text(APP / 'TickLab.App.csproj')
chart = text(APP / 'Controls/CandleChartControl.cs')
types = text(APP / 'Controls/CandleChartControl.ChartTypes.cs')
main = text(APP / 'MainWindow.xaml')

for token, label in [
    ('<Version>1.13.0.20</Version>', 'project version'),
    ('<AssemblyVersion>1.13.0.20</AssemblyVersion>', 'assembly version'),
    ('<FileVersion>1.13.0.20</FileVersion>', 'file version'),
]:
    check(token in project, label)
check((ROOT / 'TickLabV1_13_0_20.sln').exists(), 'solution file')
check(text(ROOT / 'VERSION.txt').strip() == '1.13.0.20', 'VERSION.txt')
check('Stable Candle Range Zoom Hotfix' in main, 'window title')
check('MaximumHorizontalVisibleCandles = 1_500' in chart, '1500 cap')
check('if (minimumSlotWidthPixels < 3)' in types, 'full candle tier through 3px slots')
check('viewportLeftPixels,\n            viewportRightPixels' in types, 'full viewport candle grid')
check('_detailedCandlePitchPixels' not in chart + types, 'old partial-width lattice removed')
check('oldCount *\n            anchorRatio' in chart, 'full-slot old zoom anchor')
check('newCount *\n                anchorRatio' in chart, 'full-slot new zoom anchor')
check('(oldCount - 1)' not in chart[chart.index('public void ZoomHorizontal'):chart.index('public void ZoomVertical')], 'old centre-only anchor removed')
check('int bodyLeftPixels = slot.Left + freePixels / 2;' in types, 'body uses distributed slot gap')
check('double bodyCenterPixels = (bodyLeftPixels + bodyRightPixels) / 2.0;' in types, 'wick tied to body centre')
check('return new Rect(LeftMargin, TopMargin, availableWidth, height);' in chart, 'fixed plot rectangle')

# Parse every XAML file and perform basic C# brace checks.
for path in ROOT.rglob('*.xaml'):
    try:
        etree.parse(str(path))
        check(True, f'XAML {path.relative_to(ROOT)}')
    except Exception as exc:
        check(False, f'XAML {path.relative_to(ROOT)}: {exc}')
for path in ROOT.rglob('*.cs'):
    source = text(path)
    check(source.count('{') == source.count('}'), f'brace balance {path.relative_to(ROOT)}')

# Duplicate method signature regression across CandleChartControl partial files.
pattern = re.compile(r'\b(?:public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?[\w<>,?\[\].]+\s+(\w+)\s*\(([^)]*)\)', re.M)
signatures: dict[tuple[str, tuple[str, ...]], list[str]] = {}
for path in (APP / 'Controls').glob('CandleChartControl*.cs'):
    for name, args in pattern.findall(text(path)):
        types_only: list[str] = []
        for arg in [value.strip() for value in args.split(',') if value.strip()]:
            arg = re.sub(r'\b(?:ref|out|in|params|this)\s+', '', arg)
            parts = arg.split('=')[0].strip().split()
            types_only.append(' '.join(parts[:-1]) if len(parts) >= 2 else (parts[0] if parts else ''))
        signatures.setdefault((name, tuple(types_only)), []).append(path.name)
for signature, files in signatures.items():
    check(len(files) == 1, f'unique method {signature}: {files}')

# Physical pixel geometry model.
def round_away(value: float) -> int:
    return math.floor(value + 0.5) if value >= 0 else math.ceil(value - 0.5)

def body_width(minimum_slot_width: int, target_gap: int = 3) -> int:
    if minimum_slot_width <= 4:
        return 3
    maximum = max(3, minimum_slot_width - 2)
    preferred = max(3, min(minimum_slot_width - target_gap, maximum))
    if preferred % 2 == 0:
        preferred = preferred + 1 if preferred + 1 <= maximum else preferred - 1
    return max(3, preferred)

def spans(available: int, count: int):
    raw = available / max(1, count)
    result = []
    for index in range(count):
        left = round_away(index * raw)
        right = round_away((index + 1) * raw)
        if right <= left:
            right = left + 1
        result.append((left, right))
    return raw, result

# Reproduce the user's approximate 1,442-pixel plot.
available = 1442
for count in (70, 110, 206, 250, 300, 350, 400, 450, 480):
    raw, slots = spans(available, count)
    minimum = math.floor(raw)
    check(minimum >= 3, f'{count} bars remain full-candle tier')
    check(slots[0][0] == 0, f'{count} bars start at viewport left')
    check(slots[-1][1] == available, f'{count} bars finish at viewport right')
    widths = [right - left for left, right in slots]
    check(max(widths) - min(widths) <= 1, f'{count} slot widths differ by at most one pixel')
    common = body_width(minimum)
    body_widths: list[int] = []
    centres: list[float] = []
    gaps: list[int] = []
    previous_right = None
    for slot_index, (left, right) in enumerate(slots):
        slot_width = right - left
        width = min(common, slot_width)
        if width % 2 == 0 and width > 1:
            width -= 1
        free = max(0, slot_width - width)
        body_left = left + free // 2
        body_right = body_left + width
        centre = (body_left + body_right) / 2.0
        body_widths.append(width)
        centres.append(centre)
        check(body_left >= left and body_right <= right, f'body inside slot {count}/{slot_index}')
        check(abs(centre - (math.floor(centre) + 0.5)) < 1e-9, f'wick centre is physical column {count}/{slot_index}')
        if previous_right is not None:
            gaps.append(body_left - previous_right)
        previous_right = body_right
    check(len(set(body_widths)) == 1, f'{count} bars keep one common body width')
    check(min(gaps or [0]) >= 0, f'{count} bars never overlap')
    check(max(gaps or [0]) - min(gaps or [0]) <= 1, f'{count} distributed gaps vary by at most one pixel')

raw_481, _ = spans(available, 481)
check(math.floor(raw_481) < 3, 'compressed tier begins beyond 480 bars on test frame')

# Validate many widths and DPI scales: no blank strip, no body-width fluctuation,
# and the full plot is covered exactly by deterministic rounded boundaries.
for width_dip in (640.0, 960.0, 1200.0, 1442.0, 1920.0):
    for scale in (1.0, 1.25, 1.5, 2.0):
        available_px = max(1, round_away(width_dip * scale))
        for count in (70, 110, 206, 250, 300, 400, 450, min(480, max(1, available_px // 3))):
            if count <= 0:
                continue
            raw, slots = spans(available_px, count)
            check(slots[0][0] == 0, f'left coverage {width_dip}/{scale}/{count}')
            check(slots[-1][1] == available_px, f'right coverage {width_dip}/{scale}/{count}')
            widths = [r - l for l, r in slots]
            check(max(widths) - min(widths) <= 1, f'DDA width balance {width_dip}/{scale}/{count}')
            if math.floor(raw) >= 3:
                common = body_width(math.floor(raw), max(1, round_away(3 * scale)))
                actual = []
                for left, right in slots:
                    width = min(common, right - left)
                    if width % 2 == 0 and width > 1:
                        width -= 1
                    actual.append(width)
                check(len(set(actual)) == 1, f'common body width {width_dip}/{scale}/{count}')

# Zoom anchor round-trip. The full slot range formula should return to the same
# first slot within at most one integer slot after zoom-out then zoom-in.
for old_count in (70, 110, 206, 250, 300, 400, 450):
    for ratio in (0.0, 0.1, 0.25, 0.5, 0.75, 0.9, 1.0):
        first = 10_000
        new_count = round_away(old_count * 1.10)
        anchor = first + old_count * ratio
        new_first = round_away(anchor - new_count * ratio)
        restored_count = round_away(new_count / 1.10)
        restored_anchor = new_first + new_count * ratio
        restored_first = round_away(restored_anchor - restored_count * ratio)
        check(abs(restored_first - first) <= 1, f'zoom round trip {old_count}/{ratio}')

# Protected MT5 bridge sources.
for line in text(ROOT / 'MT5_SOURCE_SHA256.txt').splitlines():
    if not line.strip():
        continue
    expected, relative = line.split(maxsplit=1)
    check(hashlib.sha256((ROOT / relative).read_bytes()).hexdigest() == expected, f'MT5 hash {relative}')

for validator in [
    'validate_duplicate_method_hotfix_1_13_0_14.py',
    'validate_bridge_write_access_hotfix_1_13_0_14.py',
]:
    result = subprocess.run(
        [sys.executable, str(ROOT / 'tools' / validator), str(ROOT)],
        capture_output=True,
        text=True,
    )
    check(result.returncode == 0, f'{validator}: {result.stdout}{result.stderr}')

passed = checks - len(failures)
print(f'V1.13.0.20 CHECKS PASSED: {passed}')
print(f'V1.13.0.20 CHECKS FAILED: {len(failures)}')
if failures:
    for failure in failures[:100]:
        print('FAIL:', failure)
    raise SystemExit(1)
print('TickLab v1.13.0.20 stable candle-range zoom validation passed.')
