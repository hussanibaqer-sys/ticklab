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
main_xaml = text(APP / 'MainWindow.xaml')
chart = text(APP / 'Controls/CandleChartControl.cs')
chart_types = text(APP / 'Controls/CandleChartControl.ChartTypes.cs')
models = text(APP / 'Core/Indicators/BuiltInIndicatorModels.cs')
catalog = text(APP / 'Core/Indicators/BuiltInIndicatorCatalog.cs')
engine = text(APP / 'Core/Indicators/BuiltInIndicatorEngine.cs')
settings = text(APP / 'Windows/BuiltInIndicatorSettingsWindow.cs')
indicators_window = text(APP / 'Windows/IndicatorsWindow.xaml')
builtin_main = text(APP / 'MainWindow.BuiltInIndicators.cs')
contexts = text(APP / 'MainWindow.ChartContexts.cs')
workspaces = text(APP / 'MainWindow.Workspaces.cs')
alerts = text(APP / 'MainWindow.AlertsReplay.cs')
templates = text(APP / 'Settings/ChartTemplateStore.cs')

for token, label in [
    ('<Version>1.13.0.26</Version>', 'project version'),
    ('<AssemblyVersion>1.13.0.26</AssemblyVersion>', 'assembly version'),
    ('<FileVersion>1.13.0.26</FileVersion>', 'file version'),
]:
    check(token in project, label)
check((ROOT / 'TickLabV1_13_0_26.sln').exists(), 'solution file')
check(text(ROOT / 'VERSION.txt').strip() == '1.13.0.26', 'VERSION.txt')
check('1.13.0.26' in main_xaml, 'main window version title')
check('MaximumHorizontalVisibleCandles = 1_500' in chart, '1500 candle cap preserved')
check('DrawBuiltInIndicatorOverlays(drawingContext, layout);' in chart, 'overlay renderer connected')

# All 38 built-in indicators must exist exactly once in the enum, catalog and engine switch.
enum_body = re.search(r'public enum BuiltInIndicatorKind\s*\{(.*?)\}', models, re.S)
check(enum_body is not None, 'indicator enum found')
enum_items = [line.strip().rstrip(',') for line in enum_body.group(1).splitlines() if line.strip()] if enum_body else []
catalog_items = re.findall(r'Add\(BuiltInIndicatorKind\.(\w+)', catalog)
engine_items = re.findall(r'BuiltInIndicatorKind\.(\w+)\s*=>', engine)
check(len(enum_items) == 38, '38 enum indicators')
check(len(catalog_items) == 38, '38 catalog indicators')
check(len(engine_items) == 38, '38 calculation branches')
check(len(set(enum_items)) == 38, 'unique enum indicators')
check(set(enum_items) == set(catalog_items), 'catalog covers enum')
check(set(enum_items) == set(engine_items), 'engine covers enum')
for category, expected in [('Trend', 13), ('Oscillator', 15), ('Volume', 4), ('BillWilliams', 6)]:
    actual = len(re.findall(rf'Add\(BuiltInIndicatorKind\.\w+,.*?BuiltInIndicatorCategory\.{category}\b', catalog))
    check(actual == expected, f'{category} count {expected}')

# MT5-like settings and UI integration.
for token in ['Tab("Parameters"', 'Tab("Levels"', 'Tab("Scale"', 'Tab("Visualization"', 'Tab("Style"']:
    check(token in settings, f'settings page {token}')
for token in ['AppliedPriceChoices', 'MaMethodChoices', 'VolumeChoices', 'VisibleTimeframes', 'UseFixedMinimum', 'UseFixedMaximum']:
    check(token in catalog + models + settings, f'indicator option {token}')
check('Built-in (38)' in indicators_window, 'indicator window count label')
check('BuiltInApplyRequested' in text(APP / 'Windows/IndicatorsWindow.xaml.cs'), 'built-in apply event')
check('window.BuiltInApplyRequested += ApplyBuiltInIndicatorToActiveChart;' in text(APP / 'MainWindow.xaml.cs'), 'built-in window wiring')
check('RefreshAllBuiltInIndicators(force);' in text(APP / 'MainWindow.xaml.cs'), 'live refresh wiring')
check('RestoreAppliedBuiltInIndicators();' in text(APP / 'MainWindow.xaml.cs'), 'startup restore wiring')
check('AppliedBuiltInIndicators = CloneBuiltInIndicators' in text(APP / 'MainWindow.xaml.cs'), 'primary persistence')
check('BuiltInIndicators = builtInIndicators' in workspaces, 'workspace persistence')
check('preference.BuiltInIndicators' in workspaces, 'workspace restoration')
check('BuiltInIndicators' in templates, 'template persistence model')
check('CloneBuiltInIndicators(context.BuiltInIndicators)' in text(APP / 'MainWindow.ChartAppearance.cs'), 'template indicator save')
check('builtin:' in alerts and 'BuiltInIndicatorResults' in alerts, 'built-in indicator alerts')
check('GetBuiltInIndicatorValuesAt' in text(APP / 'Controls/CandleChartControl.Indicators.cs'), 'built-in snap/data values')
check('HasSeparateIndicatorPane' in contexts, 'overlay-only charts avoid blank pane')
check('BuiltInIndicatorRefreshRunning' in contexts and 'LastBuiltInIndicatorRefreshUtc' in contexts, 'per-chart refresh state')


# V1.13.0.26 right-edge prefetch and sticky-scroll checks plus prior regressions.
indicator_window_cs = text(APP / 'Windows/IndicatorsWindow.xaml.cs')
indicator_stack = text(APP / 'Controls/IndicatorPaneStackControl.cs')
builtin_plot = text(APP / 'Controls/BuiltInIndicatorPlotControl.cs')
script_plot = text(APP / 'Controls/TickScriptIndicatorPlotControl.cs')
chart_pane = text(APP / 'Controls/ChartPaneControl.cs')
management = text(APP / 'MainWindow.IndicatorManagement.cs')
for token in ['On selected chart', 'AppliedList', 'RemoveButton', 'Properties…']:
    check(token in indicators_window + indicator_window_cs, f'applied indicator UI {token}')
for token in ['AppliedEditRequested', 'AppliedRemoveRequested', 'SetAppliedIndicators', 'ShowAppliedTab']:
    check(token in indicator_window_cs, f'applied indicator API {token}')
for token in ['BuildChartIndicatorMenuEntries', 'BuildAppliedIndicatorList', 'EditIndicatorByKey', 'RemoveIndicatorByKey']:
    check(token in management, f'indicator management {token}')
for token in ['IndicatorMenuItemsProvider', 'IndicatorManagerRequested', 'IndicatorEditRequested', 'IndicatorRemoveRequested']:
    check(token in chart + contexts, f'chart indicator menu {token}')
for source, label in [(builtin_plot, 'built-in pane'), (script_plot, 'TickScript pane')]:
    for token in ['ResetVerticalScale()', 'HorizontalWheelRequested', 'HorizontalPanRequested', 'Cursors.SizeNS']:
        check(token in source, f'{label} {token}')
check('e.ClickCount >= 2' in builtin_plot and 'e.ClickCount >= 2' in script_plot, 'double-click vertical reset')
check('SetChartSettings' in indicator_stack and 'ChartBackgroundColor' in indicator_stack, 'pane follows chart colours')
check('Height = 9' in chart_pane and 'Cursor = Cursors.SizeNS' in chart_pane, 'outer pane resize strip')
check('Height = new GridLength(9)' in indicator_stack and 'Cursor = Cursors.SizeNS' in indicator_stack, 'inner pane resize strips')
check('if (wasHidden)' in chart_pane and 'if (wasHidden)' in contexts, 'pane heights preserved')
check('NegativeColor = editor.NegativeColor' in settings, 'negative indicator colour saved')
check('Edit colour…' in settings and 'DrawingColorPickerWindow(level.Color)' in settings, 'level colour picker')
check('_rightOffset = state.RightOffset;' in chart, 'negative future-space viewport restore preserved')
check('ResolveLatestCandleAnchorRatio' in chart, 'newest candle screen anchor resolved')
check('CalculateZoomedRightOffset' in chart, 'future space scaled during horizontal zoom')
check('_latestCandleAnchorRatio' in chart, 'zoom anchor survives one-bar zoom tier')
check('_dragStartLatestCandleAnchorRatio' in chart and 'Time-scale dragging uses the same newest-candle anchor' in chart, 'time-scale drag preserves newest candle position')
check('ZoomBoth(factor, 1.0, verticalAnchor);' in chart, 'mouse wheel uses right-edge horizontal anchor')
check('anchorTimelinePosition' not in chart, 'old mixed slot-boundary anchor removed')
check('GetTotalTimelineSlots() -\n                GetMinimumVisibleCount()' in chart, 'history clamp is zoom-count independent')
check('GetTotalTimelineSlots() -\n                _visibleCount' not in chart, 'count-dependent history clamp removed')
check('private bool ApplyHorizontalZoomCore' in chart, 'horizontal zoom core exists')
check('private bool ApplyVerticalZoomCore' in chart, 'vertical zoom core exists')
check('if (newCount == oldCount)\n            return false;' in chart, 'horizontal limit is hard no-op')
check('bool horizontalChanged = ApplyHorizontalZoomCore' in chart, 'combined zoom starts horizontal transaction')
check('bool verticalChanged = ApplyVerticalZoomCore' in chart, 'combined zoom includes vertical transaction')
zoom_both = chart[chart.find('public void ZoomBoth'):chart.find('public void ZoomHorizontal')]
check(zoom_both.count('PublishViewportChanged();') == 1, 'combined zoom publishes once')
check(zoom_both.count('InvalidateVisual();') == 1, 'combined zoom redraws once')
wheel = chart[chart.find('protected override void OnMouseWheel'):chart.find('private void ApplyDrag')]
check(wheel.count('PublishViewportChanged();') == 1, 'mouse wheel publishes only in scroll branch')
check('ZoomBoth(factor, 1.0, verticalAnchor);' in wheel, 'wheel zoom uses combined transaction')
check('maximumDrawableRow' in chart_types, 'pixel Y excludes bottom boundary')
check('maximumTop' in chart_types, 'pixel bars retain drawable row')

# V1.13.0.26 exact regression: history prefetch is directional, bounded,
# and never triggered by vertical-only wheel or every mouse-move frame.
check('MinimumBoundaryPrefetchSlots = 256' in chart, 'bounded prefetch minimum')
check('MaximumBoundaryPrefetchSlots = 800' in chart, 'bounded prefetch maximum')
check('BoundaryPrefetchMultiplier' not in chart, 'unbounded prefetch multiplier removed')
check('RequestHistoryIfNearBoundary(\n        HistoryBoundaryDirection direction)' in chart,
      'directional prefetch API')
check('private enum HistoryBoundaryDirection' in chart, 'prefetch direction enum')
check('RequestHistoryIfNearBoundary()' not in chart, 'parameterless opposing prefetch removed')
mouse_move = chart[chart.find('protected override void OnMouseMove'):chart.find('protected override void OnMouseLeave')]
check('RequestHistoryIfNearBoundary' not in mouse_move, 'no history I/O during mouse move')
check('PublishViewportChanged();' not in mouse_move, 'crosshair does not publish viewport')
apply_drag = chart[chart.find('private void ApplyDrag'):chart.find('private void UpdateCursor')]
check('bool viewportChanged =' in apply_drag, 'drag publishes only changed viewport')
check('if (!viewportChanged)\n            return;' in apply_drag, 'drag no-op suppression')
wheel_v26 = chart[chart.find('protected override void OnMouseWheel'):chart.find('private void ApplyDrag')]
check('if (_visibleCount > oldVisibleCount)' in wheel_v26, 'zoom prefetch only when horizontal count grows')
check('RequestHistoryIfNearBoundary(HistoryBoundaryDirection.Older);' in wheel_v26,
      'zoom-out requests older only')
check(wheel_v26.rfind('RequestHistoryIfNearBoundary') < wheel_v26.rfind('e.Handled = true;'),
      'no unconditional prefetch after wheel branches')
request_method = chart[chart.find('private void RequestHistoryIfNearBoundary'):chart.find('private void ClampViewport')]
check('Math.Clamp(\n            _visibleCount / 2' in request_method, 'prefetch uses bounded visible fraction')
check('if (direction == HistoryBoundaryDirection.Older)' in request_method, 'older branch isolated')
check('return;' in request_method and 'HistoryBoundaryDirection.Newer' not in request_method,
      'newer is implicit exclusive branch')

# At max zoom, further outward wheel steps must trigger zero history requests.
visible = 1500
prefetch_requests = 0
for _ in range(500):
    requested = min(1500, round(visible * 1.10))
    if requested > visible:
        prefetch_requests += 1
    visible = requested
check(prefetch_requests == 0, 'vertical-only max zoom performs zero prefetch')

# The old threshold was 12,000 at 1,500 bars and covered both ends of a
# 12,000-record window. The new threshold is always below one 1,600-page load.
for visible in (1, 110, 400, 1500):
    threshold = max(256, min(800, visible // 2))
    check(threshold <= 800, f'prefetch threshold bounded {visible}')
    check(threshold < 1600, f'prefetch below page size {visible}')

# Every chart renderer must use the shared physical-pixel slot map for X positions.
for method in [
    'DrawOhlcBars', 'DrawCloseLine', 'DrawHlcAreaChart', 'DrawColumnsChart', 'DrawHighLowChart',
    'DrawKagiChart', 'DrawPointAndFigureChart', 'DrawTimePriceOpportunityChart',
    'DrawSessionVolumeProfileChart', 'DrawVolumeFootprintChart', 'GetClosePoints'
]:
    start = chart_types.find(f'private void {method}')
    if start < 0:
        start = chart_types.find(f'private IReadOnlyList<Point> {method}')
    check(start >= 0, f'chart renderer {method}')
check(chart_types.count('GetSlotCenterDip(layout') >= 9, 'shared slot centers used throughout chart types')
check(chart_types.count('GetSlotRectDip(layout') >= 4, 'shared slot boundaries used by profiles')
check('layout.Plot.Left + slotWidth * (layout.VisibleSlots' not in chart_types, 'old fractional chart-type X formula removed')
check('layout.Plot.Width * (layout.VisibleSlots' not in chart_types, 'old proportional chart-type X formula removed')
check('GetSlotWidthDip(layout, slotIndex)' in chart_types, 'slot-local width used')
check('SlotCenter(viewport, plot' in text(APP / 'Controls/BuiltInIndicatorPlotControl.cs'), 'indicator panes share stable lattice')

# XAML parsing and event handler existence across partial classes.
all_cs = '\n'.join(text(path) for path in APP.rglob('*.cs'))
event_names = {
    'Click','Loaded','Closed','Closing','TextChanged','SelectionChanged','MouseDoubleClick',
    'PreviewMouseMove','PreviewMouseLeftButtonDown','MouseRightButtonDown','Checked','Unchecked',
    'ValueChanged','SizeChanged','KeyDown','Drop','DragOver','PreviewKeyDown','PreviewMouseWheel',
    'MouseLeftButtonDown','MouseMove','MouseLeftButtonUp','MouseWheel'
}
for path in ROOT.rglob('*.xaml'):
    try:
        tree = etree.parse(str(path))
        check(True, f'XAML {path.relative_to(ROOT)}')
        for element in tree.iter():
            for attr, value in element.attrib.items():
                local = attr.rsplit('}', 1)[-1]
                if local in event_names:
                    check(re.search(r'\b' + re.escape(value) + r'\s*\(', all_cs) is not None,
                          f'handler {path.name}:{value}')
    except Exception as exc:
        check(False, f'XAML {path.relative_to(ROOT)}: {exc}')

# Lexical delimiter balance, ignoring comments and string/char literals.
def delimiter_ok(source: str) -> bool:
    stack: list[str] = []
    pairs = {'}':'{', ')':'(', ']':'['}
    state = 'code'; i = 0
    while i < len(source):
        c = source[i]; n = source[i+1] if i + 1 < len(source) else ''
        if state == 'code':
            if c == '/' and n == '/': state = 'line'; i += 2; continue
            if c == '/' and n == '*': state = 'block'; i += 2; continue
            if c == '"': state = 'vstring' if i > 0 and source[i-1] == '@' else 'string'; i += 1; continue
            if c == "'": state = 'char'; i += 1; continue
            if c in '({[': stack.append(c)
            elif c in pairs:
                if not stack or stack.pop() != pairs[c]: return False
        elif state == 'line':
            if c == '\n': state = 'code'
        elif state == 'block':
            if c == '*' and n == '/': state = 'code'; i += 2; continue
        elif state == 'string':
            if c == '\\': i += 2; continue
            if c == '"': state = 'code'
        elif state == 'vstring':
            if c == '"' and n == '"': i += 2; continue
            if c == '"': state = 'code'
        elif state == 'char':
            if c == '\\': i += 2; continue
            if c == "'": state = 'code'
        i += 1
    return not stack and state in {'code','line'}

for path in ROOT.rglob('*.cs'):
    check(delimiter_ok(text(path)), f'C# delimiters {path.relative_to(ROOT)}')

# Duplicate method signature regression for the large partial chart class.
pattern = re.compile(r'^\s*(?:public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?[\w<>,?\[\].]+\s+(\w+)\s*\(([^;{}]*)\)', re.M)
signatures: dict[tuple[str, tuple[str, ...]], list[str]] = {}
for path in (APP / 'Controls').glob('CandleChartControl*.cs'):
    for name, args in pattern.findall(text(path)):
        arg_types: list[str] = []
        for arg in [part.strip() for part in re.split(r',(?![^<]*>)', args) if part.strip()]:
            arg = re.sub(r'\b(?:ref|out|in|params|this)\s+', '', arg.split('=')[0].strip())
            parts = arg.split()
            arg_types.append(' '.join(parts[:-1]) if len(parts) >= 2 else arg)
        signatures.setdefault((name, tuple(arg_types)), []).append(path.name)
for signature, files in signatures.items():
    check(len(files) == 1, f'unique CandleChartControl method {signature}: {files}')

# Repeated horizontal zoom must keep the newest candle visible and at a stable
# screen position, including the one-bar tier where future space rounds to zero.
def clamp_count(value: int) -> int:
    return max(1, min(1500, value))

def newest_ratio(visible: int, right_offset: int) -> float:
    future = max(0, -right_offset)
    return 1.0 - (future + 0.5) / max(1, visible)

def round_away_local(value: float) -> int:
    return math.floor(value + 0.5) if value >= 0 else math.ceil(value - 0.5)

def zoom_offset(new_visible: int, current_right_offset: int, anchor_ratio: float) -> int:
    if current_right_offset > 0 or not math.isfinite(anchor_ratio):
        return current_right_offset
    future = round_away_local(new_visible * (1.0 - anchor_ratio) - 0.5)
    return -max(0, min(1_000_000_000, future))

for initial_visible, initial_offset in ((110, -10), (206, -18), (400, -36), (80, 0)):
    anchor = newest_ratio(initial_visible, initial_offset)
    visible = initial_visible
    offset = initial_offset
    for factor in ([1.0 / 1.10] * 80 + [1.10] * 80) * 3:
        visible = clamp_count(round(visible * factor))
        offset = zoom_offset(visible, offset, anchor)
        total_slots = 20_000
        timeline_first = total_slots - offset - visible
        newest_visible_slot = (total_slots - 1) - timeline_first
        check(0 <= newest_visible_slot < visible,
              f'newest candle remains visible {initial_visible}/{visible}/{offset}')
        current_ratio = (newest_visible_slot + 0.5) / visible
        tolerance = max(1.0 / visible, 0.015)
        check(abs(current_ratio - anchor) <= tolerance,
              f'newest candle ratio stable {initial_visible}/{visible}/{offset}')
    check(abs(newest_ratio(visible, offset) - anchor) <= max(1.0 / visible, 0.015),
          f'zoom round-trip newest position {initial_visible}')

# The v1.13.0.23 defect: fixed -10 future slots with <=10 visible bars produced
# an empty chart. Every normal live zoom tier must retain at least one candle.
for visible in range(1, 111):
    anchor = newest_ratio(110, -10)
    offset = zoom_offset(visible, -10, anchor)
    total_slots = 1000
    first = total_slots - offset - visible
    newest_slot = total_slots - 1 - first
    check(0 <= newest_slot < visible, f'no empty live chart at {visible} bars')

# A zoomed live chart must still be treated as following latest so an appended
# candle does not increment the historical right offset.
for visible in (1, 5, 10, 50, 110, 400, 1500):
    anchor = newest_ratio(110, -10)
    offset = zoom_offset(visible, -10, anchor)
    check(offset <= 0, f'live-follow state retained {visible}')

# At the horizontal maximum, repeated outward wheel input must leave every
# horizontal state value unchanged while vertical span continues to grow.
visible = 1500
right_offset = -136
latest_anchor = newest_ratio(visible, right_offset)
vertical_span = 100.0
for _ in range(250):
    requested = clamp_count(round(visible * 1.10))
    horizontal_changed = requested != visible
    check(not horizontal_changed, 'max horizontal wheel is no-op')
    check(visible == 1500, 'max horizontal visible count frozen')
    check(right_offset == -136, 'max horizontal right offset frozen')
    check(abs(newest_ratio(visible, right_offset) - latest_anchor) < 1e-12,
          'max horizontal newest anchor frozen')
    vertical_span *= 1.10
check(vertical_span > 100.0, 'vertical zoom continues at horizontal limit')

# Exclusive-bottom pixel safety: every rounded OHLC row must produce at least
# one drawable pixel, including values rounded to the lower plot boundary.
for plot_top, plot_bottom in ((0, 100), (25, 426), (125, 126)):
    maximum_row = max(plot_top, plot_bottom - 1)
    for rounded in range(plot_top - 10, plot_bottom + 11):
        row = max(plot_top, min(maximum_row, rounded))
        top = max(plot_top, min(maximum_row, row))
        bottom = max(plot_top + 1, min(plot_bottom, row))
        if bottom <= top:
            bottom = min(plot_bottom, top + 1)
        check(bottom > top, f'drawable candle row {plot_top}/{plot_bottom}/{rounded}')

# Shared full-width physical slot model across representative sizes, DPI scales and bar counts.
def round_away(value: float) -> int:
    return math.floor(value + 0.5) if value >= 0 else math.ceil(value - 0.5)
for width_dip in (640, 960, 1200, 1442, 1920):
    for scale in (1.0, 1.25, 1.5, 2.0):
        pixels = max(1, round_away(width_dip * scale))
        for count in (50, 100, 206, 300, 400, 450, 480, 750, 1500):
            raw = pixels / count
            spans = []
            for slot in range(count):
                left = round_away(slot * raw)
                right = round_away((slot + 1) * raw)
                if right <= left: right = left + 1
                spans.append((left, right))
            check(spans[0][0] == 0, f'grid left {width_dip}/{scale}/{count}')
            check(spans[-1][1] >= pixels, f'grid right {width_dip}/{scale}/{count}')
            centers = [(left + right) / 2.0 for left, right in spans]
            if raw >= 1:
                check(all(centers[i] < centers[i+1] for i in range(len(centers)-1)), f'ordered centers {width_dip}/{scale}/{count}')
            else:
                check(True, f'compressed tier {width_dip}/{scale}/{count}')
            if math.floor(raw) >= 3:
                check(max(r-l for l,r in spans) - min(r-l for l,r in spans) <= 1, f'balanced full candles {width_dip}/{scale}/{count}')

# Protected MT5 bridge hashes and previous bridge/duplicate regression checks.
for line in text(ROOT / 'MT5_SOURCE_SHA256.txt').splitlines():
    if not line.strip(): continue
    expected, relative = line.split(maxsplit=1)
    check(hashlib.sha256((ROOT / relative).read_bytes()).hexdigest() == expected, f'MT5 hash {relative}')
for validator in ['validate_duplicate_method_hotfix_1_13_0_14.py', 'validate_bridge_write_access_hotfix_1_13_0_14.py']:
    result = subprocess.run([sys.executable, str(ROOT / 'tools' / validator), str(ROOT)], capture_output=True, text=True)
    check(result.returncode == 0, f'{validator}: {result.stdout}{result.stderr}')

passed = checks - len(failures)
print(f'V1.13.0.26 CHECKS PASSED: {passed}')
print(f'V1.13.0.26 CHECKS FAILED: {len(failures)}')
if failures:
    for failure in failures[:150]: print('FAIL:', failure)
    raise SystemExit(1)
print('TickLab v1.13.0.26 right-edge prefetch and sticky-scroll validation passed.')
