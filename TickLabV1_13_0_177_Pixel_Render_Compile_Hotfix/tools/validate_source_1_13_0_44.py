from pathlib import Path
import hashlib
import re
import sys
import xml.etree.ElementTree as ET
from pygments import lex
from pygments.lexers.dotnet import CSharpLexer
from pygments.token import Token

root = Path(__file__).resolve().parents[1]
base = root.parent / 'TickLabV1_13_0_43_Shared_Rail_Symbol_Trade_Sync'
app = root / 'src' / 'TickLab.App'
passed: list[str] = []
failed: list[str] = []

def check(condition: bool, label: str) -> None:
    (passed if condition else failed).append(label)

def read(path: Path) -> str:
    return path.read_text(encoding='utf-8-sig', errors='ignore')

def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()

proj = read(app / 'TickLab.App.csproj')
xaml = read(app / 'MainWindow.xaml')
main = read(app / 'MainWindow.xaml.cs')
demo = read(app / 'MainWindow.DemoTrading.cs')
chart_demo = read(app / 'Controls' / 'CandleChartControl.DemoTrading.cs')
prompt = read(app / 'Windows' / 'DemoTradeValuePromptWindow.cs')
detached = read(app / 'Windows' / 'DetachedChartWindow.xaml.cs')
contexts = read(app / 'MainWindow.ChartContexts.cs')
workspaces = read(app / 'MainWindow.Workspaces.cs')

# Release identity.
for needle, label in [
    ('<Version>1.13.0.44</Version>', 'version'),
    ('<AssemblyVersion>1.13.0.44</AssemblyVersion>', 'assembly version'),
    ('<FileVersion>1.13.0.44</FileVersion>', 'file version')]:
    check(needle in proj, label)
check((root / 'TickLabV1_13_0_44.sln').exists(), 'v44 solution exists')
check(not (root / 'TickLabV1_13_0_43.sln').exists(), 'old v43 solution name removed')
check(read(root / 'VERSION.txt').strip() == '1.13.0.44', 'VERSION file')
check('TickLabV1_13_0_44.sln' in read(root / 'Clean-Restore-Build.cmd'), 'build script targets v44')
check('Trade History Toggle and Direct SL/TP Controls' in xaml, 'window title')

# XAML parsing, unique names and referenced event handlers.
all_main = '\n'.join(read(path) for path in app.glob('MainWindow*.cs'))
handler_pattern = r'\b(?:Click|Checked|Unchecked|TextChanged|SelectionChanged|SelectedDateChanged|PreviewMouse\w+|PreviewKey\w+|Mouse\w+|Key\w+|Drag\w+|Drop|Loaded|Closing)="([A-Za-z_]\w*)"'
for path in app.rglob('*.xaml'):
    try:
        ET.parse(path)
        check(True, f'XAML parses {path.relative_to(app)}')
    except Exception as exc:
        check(False, f'XAML parses {path.relative_to(app)}: {exc}')
    text = read(path)
    names = re.findall(r'x:Name="([^"]+)"', text)
    check(len(names) == len(set(names)), f'unique x:Name {path.relative_to(app)}')
    handlers = set(re.findall(handler_pattern, text))
    code_text = all_main if path.name == 'MainWindow.xaml' else (
        read(path.with_suffix('.xaml.cs')) if path.with_suffix('.xaml.cs').exists() else '')
    for handler in handlers:
        check(re.search(r'\b' + re.escape(handler) + r'\s*\(', code_text) is not None,
              f'handler {path.name}:{handler}')

# C# punctuation/string/comment structural balance.
for path in app.rglob('*.cs'):
    stack: list[str] = []
    pairs = {')': '(', ']': '[', '}': '{'}
    ok = True
    for token_type, value in lex(read(path), CSharpLexer()):
        if token_type in Token.Punctuation:
            for character in value:
                if character in '([{':
                    stack.append(character)
                elif character in ')]}':
                    if not stack or stack.pop() != pairs[character]:
                        ok = False
                        break
        if not ok:
            break
    check(ok and not stack, f'C# balanced {path.relative_to(app)}')

# Targeted declaration uniqueness.
for source, method_name, return_pattern in [
    (demo, 'DemoShowHistoryOnChartCheckBox_Changed', r'void'),
    (demo, 'RefreshDemoTradeLines', r'void'),
    (demo, 'MoveDemoTradeLine', r'void'),
    (chart_demo, 'DrawDemoTradeHistoryPaths', r'void'),
    (chart_demo, 'DrawDemoHistoryArrow', r'void'),
    (chart_demo, 'ResolveEntryDragPreviewKind', r'DemoTradeLineKind'),
    (detached, 'UpdateDemoTradeLines', r'void')]:
    declarations = len(re.findall(r'(?m)^\s*(?:public|private)\s+(?:static\s+)?' + return_pattern + r'\s+' + re.escape(method_name) + r'\s*\(', source))
    check(declarations == 1, f'unique method {method_name}')

# Shared rail and clipped text correction.
check('x:Name="RightHandleStripColumn" Width="19" MinWidth="19" MaxWidth="19"' in xaml,
      '19 px right handle strip preserved')
for name in ['CodeEditorSlideButton', 'DemoTradeSlideButton', 'RightWorkspaceToggleButton']:
    check(re.search(rf'x:Name="{name}"[^>]*Width="24"', xaml, re.S) is not None,
          f'{name} remains 24 px wide')
check('x:Name="CodeEditorSlideButton" Grid.Row="1" Width="24" Height="130" MinHeight="130"' in xaml,
      'Code Editor handle height prevents final R clipping')
check('LineHeight="9.2"' in xaml and 'ClipToBounds="False"' in xaml,
      'Code Editor handle text has safe line height and unclipped content')

# History on-chart toggle and persistence.
check('x:Name="DemoShowHistoryOnChartCheckBox"' in xaml, 'history on chart checkbox present')
check('Content="History on chart"' in xaml, 'history toggle label')
check('Checked="DemoShowHistoryOnChartCheckBox_Changed"' in xaml and
      'Unchecked="DemoShowHistoryOnChartCheckBox_Changed"' in xaml,
      'history toggle handles both states')
check('public bool ShowHistoryOnChart { get; set; } = true;' in demo,
      'history visibility persisted in account document')
check('DemoShowHistoryOnChartCheckBox.IsChecked = _demoAccount.ShowHistoryOnChart;' in demo,
      'history toggle restored at startup')
check('if (_demoAccount.ShowHistoryOnChart)' in demo,
      'historical overlays obey toggle')
check('_demoAccount.ShowHistoryOnChart = DemoShowHistoryOnChartCheckBox.IsChecked == true;' in demo,
      'toggle updates persisted setting')
check('_demoTradeHistory.Where(item => DemoSymbolsMatch(item.Symbol, context.Symbol))' in demo,
      'history remains symbol scoped across timeframes and charts')
check('chartWindow.UpdateDemoTradeLines(CandleChart.DemoTradeLines);' in demo,
      'detached mirror history/levels refresh without full data reload')
check('DetachedChart.DemoTradeLines = demoTradeLines ?? Array.Empty<DemoTradeLineOverlay>();' in detached,
      'detached chart overlay-only update')

# MT5-style visible history markers.
check('DrawDemoHistoryArrow' in chart_demo, 'entry/exit arrow renderer present')
check('DrawDemoHistoryCaption' in chart_demo, 'BUY/SELL/EXIT captions present')
check('"BUY" : "SELL"' in chart_demo and '"EXIT"' in chart_demo,
      'history marker captions are explicit')
check('DashStyles.Dash' in chart_demo and 'new Pen(connectorBrush, 2.0)' in chart_demo,
      'history connector made visible')
check('UpdateDemoTradeHistoryHover' in chart_demo,
      'history tooltip hover retained')

# Right-click arrow adjustment window.
check('RepeatButton' in prompt, 'holdable arrow buttons use RepeatButton')
check('Delay = 320' in prompt and 'Interval = 55' in prompt,
      'arrow buttons repeat while held')
check('CreateArrowButton("▲", 1' in prompt and 'CreateArrowButton("▼", -1' in prompt,
      'both up and down arrows present')
check('Background = new SolidColorBrush(Color.FromRgb(16, 27, 43))' in prompt and
      'Background = Brushes.White' in prompt and 'Foreground = Brushes.Black' in prompt,
      'new window and input maintain readable contrast')
check('double fallback = usePoints ? 1.0 : position.EntryPrice;' in demo,
      'missing SL/TP starts from entry price')
check('position.Point > 0' in demo and 'usePoints ? 0 : position.Digits' in demo,
      'price arrows use symbol point and digits')
check('price + hold ▲ / ▼' in demo,
      'right-click menu advertises hold arrows')

# Direct entry-line drag to create levels.
check('drag from entry to create SL/TP' in demo, 'entry line explains direct drag')
entry_overlay_pattern = re.compile(r'DemoLineId\(position\.Id, "entry"\).*?true,\s*isBuy\)\);', re.S)
check(entry_overlay_pattern.search(demo) is not None, 'entry line is draggable')
check('if (kind == "entry")' in demo, 'entry-line drag handled')
check('bool createsStopLoss = position.Direction == "BUY"' in demo,
      'entry drag is direction-aware')
check('ResolveEntryDragPreviewKind' in chart_demo and '"NEW SL"' in chart_demo and '"NEW TP"' in chart_demo,
      'entry drag shows live SL/TP preview')
check('CommitPositionLevelChange(position,' in demo and 'created from entry drag' in demo,
      'entry drag persists created level')
check('TryApplyPositionLevels(position, createdSl, createdTp, market' in demo,
      'entry drag retains market-side validation')

# Existing symbol-wide and safe demo behavior preserved.
check('_demoOpenPositions.Where(item => DemoSymbolsMatch(item.Symbol, context.Symbol))' in demo,
      'active positions remain symbol-wide')
check(contexts.count('RefreshDemoTradeLines();') >= 4,
      'chart lifecycle reprojects markings')
check('RefreshDemoTradeLines();' in workspaces,
      'new/floating charts receive markings')
for needle, label in [
    ('double entry = direction == "BUY" ? market.Ask : market.Bid', 'Buy Ask / Sell Bid entry'),
    ('double mark = position.Direction == "BUY" ? market.Bid : market.Ask', 'live mark side'),
    ('market.Bid <= position.StopLoss', 'Buy SL Bid'),
    ('market.Ask >= position.StopLoss', 'Sell SL Ask'),
    ('market.Bid >= position.TakeProfit', 'Buy TP Bid'),
    ('market.Ask <= position.TakeProfit', 'Sell TP Ask')]:
    check(needle in demo, label)
for forbidden in ['OrderSend', 'MqlTradeRequest', 'MqlTradeResult', 'CTrade', 'trade.Buy', 'trade.Sell', 'OrderSendAsync']:
    check(forbidden.lower() not in demo.lower(), f'no real order API {forbidden}')

# Protected MT5 and FileBridge sources remain byte-for-byte unchanged from v43.
check(base.exists(), 'v43 baseline available')
if base.exists():
    protected = [path for path in base.rglob('*') if path.is_file() and (
        path.relative_to(base).as_posix().startswith('MT5/') or
        path.relative_to(base).as_posix().startswith('src/TickLab.App/Gateway/FileBridge/'))]
    check(len(protected) > 0, 'protected source set found')
    for baseline_path in protected:
        relative = baseline_path.relative_to(base)
        current_path = root / relative
        check(current_path.exists() and sha(baseline_path) == sha(current_path),
              f'protected unchanged {relative.as_posix()}')

report = root / 'VALIDATION_REPORT_1_13_0_44.txt'
report.write_text(
    'TickLab v1.13.0.44 static validation\n\n'
    f'Passed: {len(passed)}\nFailed: {len(failed)}\n\n' +
    '\n'.join('PASS  ' + item for item in passed) +
    (('\n\n' + '\n'.join('FAIL  ' + item for item in failed)) if failed else '') + '\n\n'
    'Compiler limitation: the generation environment has no dotnet/Windows WPF toolchain.\n'
    'Run Clean-Restore-Build.cmd in Visual Studio 2022 / Windows for the final compiler and display test.\n',
    encoding='utf-8')
print(f'passed={len(passed)} failed={len(failed)}')
for item in failed:
    print('FAIL', item)
sys.exit(1 if failed else 0)
