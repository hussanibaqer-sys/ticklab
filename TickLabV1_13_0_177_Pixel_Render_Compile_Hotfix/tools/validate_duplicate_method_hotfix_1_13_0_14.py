from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1]
controls = root / 'src' / 'TickLab.App' / 'Controls'
files = sorted(controls.glob('CandleChartControl*.cs'))
checks = []

def check(condition, label):
    checks.append((bool(condition), label))

combined = '\n'.join(path.read_text(encoding='utf-8-sig', errors='ignore') for path in files)
declarations = re.findall(
    r'^\s*private\s+int\s+FindNearestCandleIndex\s*\(\s*long\s+[A-Za-z_]\w*\s*\)',
    combined,
    flags=re.MULTILINE,
)
check(len(declarations) == 1, 'Exactly one FindNearestCandleIndex(long) declaration')

main = (controls / 'CandleChartControl.cs').read_text(encoding='utf-8-sig', errors='ignore')
drawing = (controls / 'CandleChartControl.Drawing.cs').read_text(encoding='utf-8-sig', errors='ignore')
check('private int FindNearestCandleIndex(long startUnix)' in main,
      'Viewport-safe nearest-candle implementation retained')
check('private int FindNearestCandleIndex(long timestamp)' not in drawing,
      'Older drawing-partial duplicate removed')
check(main.count('private int FindNearestCandleIndex(') == 1,
      'Main chart file contains one nearest-candle method')
check(drawing.count('private int FindNearestCandleIndex(') == 0,
      'Drawing partial contains no duplicate nearest-candle method')

# Ensure important consumers still resolve to the shared method.
for marker in [
    'ReplaceVisualViewportFromAnchor',
    'FindNearestCandleIndex(anchor.StartUnix)',
]:
    check(marker in main, f'Main consumer retained: {marker}')
for marker in [
    'FindNearestCandleIndex(drawing.Anchors[0].StartUnix)',
    'FindNearestCandleIndex(drawing.Anchors[1].StartUnix)',
]:
    check(marker in drawing, f'Drawing consumer retained: {marker}')

passed = sum(ok for ok, _ in checks)
failed = [label for ok, label in checks if not ok]
print(f'DUPLICATE-METHOD CHECKS PASSED: {passed}')
print(f'DUPLICATE-METHOD CHECKS FAILED: {len(failed)}')
for label in failed:
    print('FAIL:', label)
if failed:
    sys.exit(1)
print('CandleChartControl duplicate-method compile hotfix validation passed.')
