from pathlib import Path
import hashlib
import re
import sys
import xml.etree.ElementTree as ET
from pygments import lex
from pygments.lexers.dotnet import CSharpLexer
from pygments.token import Token

root = Path(__file__).resolve().parents[1]
base = Path('/mnt/data/ticklab_42_baseline/TickLabV1_13_0_42_Restart_Step10_Right_Panels_Contrast_Default_Background')
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
code = read(app / 'MainWindow.CodeEditor.cs')
demo = read(app / 'MainWindow.DemoTrading.cs')
contexts = read(app / 'MainWindow.ChartContexts.cs')
workspaces = read(app / 'MainWindow.Workspaces.cs')
detached = read(app / 'Windows' / 'DetachedChartWindow.xaml.cs')

# Release identity.
for needle, label in [
    ('<Version>1.13.0.43</Version>', 'version'),
    ('<AssemblyVersion>1.13.0.43</AssemblyVersion>', 'assembly version'),
    ('<FileVersion>1.13.0.43</FileVersion>', 'file version')]:
    check(needle in proj, label)
check((root / 'TickLabV1_13_0_43.sln').exists(), 'solution exists')
check(not (root / 'TickLabV1_13_0_42.sln').exists(), 'old solution name removed')
check(read(root / 'VERSION.txt').strip() == '1.13.0.43', 'VERSION file')
check('TickLabV1_13_0_43.sln' in read(root / 'Clean-Restore-Build.cmd'), 'build script targets v43')
check('Shared Right Rail and Symbol-Wide Demo Trade Markings' in xaml, 'window title')

# XAML parsing, unique names, event handlers.
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

# Targeted duplicate-risk checks for the methods changed in this release.
for method_name in [
    'SetDemoPanelWidth', 'TryGetDemoMarket', 'DemoSymbolsMatch',
    'RefreshDemoTradeLines', 'MoveDemoTradeLine']:
    expected = 2 if method_name == 'TryGetDemoMarket' else 1
    count = len(re.findall(r'\b' + re.escape(method_name) + r'\s*\(', demo))
    # TryGetDemoMarket has three overloads plus call sites, so declarations are checked separately below.
    if method_name == 'TryGetDemoMarket':
        declarations = len(re.findall(r'(?m)^\s*private\s+(?:static\s+)?bool\s+TryGetDemoMarket\s*\(', demo))
        check(declarations == 3, 'three intentional TryGetDemoMarket overloads')
    else:
        declarations = len(re.findall(r'(?m)^\s*private\s+(?:static\s+)?(?:void|bool)\s+' + re.escape(method_name) + r'\s*\(', demo))
        check(declarations == 1, f'unique changed method {method_name}')

# Shared synchronized right rail requirements.
check('x:Name="RightHandleStripColumn" Width="19" MinWidth="19" MaxWidth="19"' in xaml,
      '19 px dedicated right handle strip')
rail_match = re.search(r'<Grid x:Name="RightHandleRail".*?</Grid>\s*</Grid>\s*<!-- Slim persistent', xaml, re.S)
check(rail_match is not None, 'shared rail block present')
rail = rail_match.group(0) if rail_match else ''
check(rail.count('<RowDefinition Height="38"/>') == 2, 'exact two 38 px (~1 cm) gaps')
for name in ['CodeEditorSlideButton', 'DemoTradeSlideButton', 'RightWorkspaceToggleButton']:
    check(re.search(rf'x:Name="{name}"[^>]*Width="24"', rail, re.S) is not None,
          f'{name} no wider than 24 px')
check(rail.find('x:Name="CodeEditorSlideButton"') < rail.find('x:Name="DemoTradeSlideButton"') < rail.find('x:Name="RightWorkspaceToggleButton"'),
      'vertical order Code Editor, Demo Trading, Panel')
check('<RowDefinition Height="*"/>' in rail and rail.count('<RowDefinition Height="*"/>') == 2,
      'equal flexible free space above and below')
check('Fixed top-right panel handles' not in xaml, 'old separate fixed rail removed')
check(xaml.count('x:Name="CodeEditorSlideButton"') == 1, 'one Code Editor handle')
check(xaml.count('x:Name="DemoTradeSlideButton"') == 1, 'one Demo Trading handle')
check(xaml.count('x:Name="RightWorkspaceToggleButton"') == 1, 'one Panel handle')
check('x:Name="DemoTradeColumn" Width="0" MinWidth="0" MaxWidth="620"' in xaml,
      'dedicated demo panel column')
check('DemoTradeColumn.Width = new GridLength(clamped);' in demo, 'demo panel column drives shared rail')
check('DemoTradeDock.Width = clamped;' in demo, 'demo panel dock follows reserved column')
check('_demoSlideStartPanelWidth = DemoTradeColumn.ActualWidth;' in demo, 'demo drag starts from column width')
check('CodeEditorColumn.Width = new GridLength' in code, 'code editor retains resizable column')
check('RightWorkspaceColumn.Width = new GridLength' in main, 'panel retains resizable column')

# Symbol-wide active positions and saved history.
check('private static bool DemoSymbolsMatch' in demo, 'symbol identity helper')
check('_demoOpenPositions.Where(item => DemoSymbolsMatch(item.Symbol, context.Symbol))' in demo,
      'active positions projected to every same-symbol chart')
check('_demoTradeHistory.Where(item => DemoSymbolsMatch(item.Symbol, context.Symbol))' in demo,
      'saved history projected to every same-symbol chart')
check('string.Equals(position.Timeframe, recordedContext.Timeframe' not in demo,
      'active line rendering not restricted to original timeframe')
check('string.Equals(trade.Timeframe, recordedContext.Timeframe' not in demo,
      'history line rendering not restricted to original timeframe')
check('DemoSymbolsMatch(position.Symbol, exact.Symbol)' in demo,
      'running trade can use replacement same-symbol chart')
check('foreach (ChartRuntimeContext context in _chartContexts.Values.OrderBy(item => item.PaneId))' in demo,
      'running trade searches all same-symbol charts')
check('!DemoSymbolsMatch(position.Symbol, context.Symbol)' in demo,
      'SL/TP drag allowed from any same-symbol chart')
check(contexts.count('RefreshDemoTradeLines();') >= 4,
      'chart registration, identity, removal and timeframe refresh all reapply markings')
check(re.search(r'UpdateWorkspacePaneIdentity\(context\.PaneId, context\.Symbol, context\.Timeframe\.DisplayText\);\s*RefreshAlertLines\(\);\s*RefreshDemoTradeLines\(\);', contexts) is not None,
      'timeframe change immediately reprojects trade markings')
check('LoadDemoTradingState();' in demo and 'SaveDemoTradingState();' in demo,
      'demo account remains persisted independently of chart windows')
check('_demoAccount.History = _demoTradeHistory.ToList();' in demo,
      'saved history written to account document')
check('foreach (DemoTradeRecord trade in (_demoAccount.History' in demo,
      'saved history restored after restart')
check('RefreshIndicatorWorkspaceSourceLabels(floatingContext);\n            RefreshDemoTradeLines();' in workspaces,
      'new floating same-symbol chart immediately receives markings')
check('IReadOnlyList<DemoTradeLineOverlay> demoTradeLines' in detached and
      'DetachedChart.DemoTradeLines = demoTradeLines;' in detached,
      'legacy standalone detached chart receives demo overlays')
check('CandleChart.DemoTradeLines' in main,
      'detached chart synchronization forwards demo overlays')

# Existing safe demo execution rules preserved.
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

# Protected MT5 and FileBridge sources remain byte-for-byte unchanged.
protected = [path for path in base.rglob('*') if path.is_file() and (
    path.relative_to(base).as_posix().startswith('MT5/') or
    path.relative_to(base).as_posix().startswith('src/TickLab.App/Gateway/FileBridge/'))]
for baseline_path in protected:
    relative = baseline_path.relative_to(base)
    current_path = root / relative
    check(current_path.exists() and sha(baseline_path) == sha(current_path),
          f'protected unchanged {relative.as_posix()}')

report = root / 'VALIDATION_REPORT_1_13_0_43.txt'
report.write_text(
    'TickLab v1.13.0.43 static validation\n\n'
    f'Passed: {len(passed)}\nFailed: {len(failed)}\n\n' +
    '\n'.join('PASS  ' + item for item in passed) +
    (('\n\n' + '\n'.join('FAIL  ' + item for item in failed)) if failed else '') + '\n',
    encoding='utf-8')
print(f'passed={len(passed)} failed={len(failed)}')
for item in failed:
    print('FAIL', item)
sys.exit(1 if failed else 0)
