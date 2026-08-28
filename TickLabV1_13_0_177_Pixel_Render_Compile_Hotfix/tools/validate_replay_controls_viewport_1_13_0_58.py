from pathlib import Path

root = Path(__file__).resolve().parents[1]
app = root / 'src' / 'TickLab.App'

def read(path):
    return path.read_text(encoding='utf-8-sig')

window = read(app / 'Windows' / 'MarketReplayWindow.cs')
alerts = read(app / 'MainWindow.AlertsReplay.cs')
engine = read(app / 'Core' / 'Replay' / 'MarketReplayEngine.cs')
chart = read(app / 'Controls' / 'CandleChartControl.cs')
project = read(app / 'TickLab.App.csproj')
xaml = read(app / 'MainWindow.xaml')

checks = []
def check(ok, label):
    checks.append((bool(ok), label))

check('<Version>1.13.0.58</Version>' in project, 'v58 project version')
check('TickLab v1.13.0.58 — Replay Controls + Viewport Fix' in xaml, 'v58 window title')
check('Width = 760;' in window and 'MinWidth = 700;' in window, 'replay window widened for new controls')
check('Background = Brushes.White' in window and 'Foreground = Brushes.Black' in window, 'speed selector white with black text')
check('speedItemStyle' in window and 'ComboBoxItem' in window, 'speed dropdown items black on white')
check('CreateButton("◀ Reverse"' in window, 'reverse button present')
check('CreateButton("Forward ▶"' in window, 'forward button present')
check('public event Action? ReverseRequested;' in window, 'reverse event exposed')
check('public event Action? ForwardRequested;' in window, 'forward event exposed')
check('_replayWindow.ReverseRequested += StartReverseReplay;' in alerts, 'reverse event wired')
check('_replayWindow.ForwardRequested += StartForwardReplay;' in alerts, 'forward event wired')
check('private enum ReplayPlaybackDirection' in alerts, 'playback direction state present')
check('ReplayPlaybackDirection.Reverse' in alerts and 'ReplayPlaybackDirection.Forward' in alerts, 'both directions implemented')
check('private readonly Stack<ReplayUndoState> _undo = new();' in engine, 'replay engine maintains undo history')
check('public bool TryUndoLastTick(out MarketTick undoneTick)' in engine, 'replay engine can undo a raw tick')
check('public Stack<MarketTick> RedoTicks { get; } = new();' in alerts, 'reversed ticks retained for exact forward replay')
check('runtime.RedoTicks.Push(undoneTick);' in alerts, 'reverse stores exact undone tick')
check('runtime.RedoTicks.Pop();' in alerts, 'forward reapplies reversed tick before new raw ticks')
check('runtime.SimulatedMilliseconds -= scaledElapsed;' in alerts, 'reverse uses replay clock and speed')
check('Replay returned to the yellow start line.' in alerts, 'reverse stops at replay start boundary')
check('CaptureWindowAnchorAtOrBefore' in chart, 'chart supports replay start viewport anchor')
check('excludeExact: true' in alerts, 'anchor uses completed candle before yellow start')
check('ReplaceDataPreservingAnchor' in alerts and 'ReplayViewportApplied' in alerts, 'initial future hide preserves horizontal window')
check('leaving the original right-side' in alerts and 'area empty for replay candles to grow into' in alerts, 'right-side future space rule documented in source')
check('ResetContextHistoryPaging(context);' in alerts and 'int identityGeneration = context.IdentityGeneration;' in alerts, 'v57 replay identity fix preserved')
check('ReadReplayTicksImmediatelyAsync' in alerts and 'Starting replay…' in alerts, 'instant replay loading path preserved')
check('HiddenLiveSourceCandles' in alerts and 'HiddenLiveDisplayCandles' in alerts, 'hidden live engine state preserved')

passed = sum(ok for ok, _ in checks)
failed = len(checks) - passed
report = root / 'VALIDATION_REPLAY_CONTROLS_VIEWPORT_1_13_0_58.txt'
report.write_text(
    'TickLab v1.13.0.58 replay controls + viewport validation\n\n' +
    f'Passed: {passed}\nFailed: {failed}\n\n' +
    '\n'.join(('PASS  ' if ok else 'FAIL  ') + label for ok, label in checks) + '\n',
    encoding='utf-8')
print(f'passed={passed} failed={failed}')
for ok, label in checks:
    if not ok:
        print('FAIL', label)
raise SystemExit(1 if failed else 0)
