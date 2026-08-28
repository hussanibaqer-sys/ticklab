from pathlib import Path
import hashlib
import re
import sys
import xml.etree.ElementTree as ET
from pygments import lex
from pygments.lexers.dotnet import CSharpLexer
from pygments.token import Token

root = Path(__file__).resolve().parents[1]
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
    ('<Version>1.13.0.45</Version>', 'version'),
    ('<AssemblyVersion>1.13.0.45</AssemblyVersion>', 'assembly version'),
    ('<FileVersion>1.13.0.45</FileVersion>', 'file version')]:
    check(needle in proj, label)
check((root / 'TickLabV1_13_0_45.sln').exists(), 'v45 solution exists')
check(not (root / 'TickLabV1_13_0_44.sln').exists(), 'old v44 solution name removed')
check(read(root / 'VERSION.txt').strip() == '1.13.0.45', 'VERSION file')
check('TickLabV1_13_0_45.sln' in read(root / 'Clean-Restore-Build.cmd'), 'build script targets v45')
check('Demo History Startup Null Hotfix' in xaml, 'window title')

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


# v1.13.0.45 startup crash and persistence hotfix.
check('private bool _demoTradingInitialized;' in demo, 'demo initialization gate exists')
check('if (!_demoTradingInitialized || !IsInitialized || DemoShowHistoryOnChartCheckBox is null)' in demo,
      'history checkbox ignores InitializeComponent events')
check('if (!_demoTradingInitialized)\n            return;' in demo,
      'demo save is blocked before account load completes')
check('_demoTradingInitialized = true;' in demo, 'demo initialization gate opens after UI restore')
check('if (DemoTradeStatusText is not null)' in demo and 'if (StatusText is not null)' in demo,
      'status controls are null-safe during startup and teardown')
check('TryReadDemoAccountDocument(DemoTradingPath + ".bak")' in demo and
      'TryReadDemoAccountDocument(DemoTradingPath + ".tmp")' in demo,
      'demo account backup and temporary fallback loading')
check('File.Copy(DemoTradingPath, backup, overwrite: true);' in demo,
      'demo account backup created before replacement')

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

# Protected MT5 and FileBridge sources remain at their approved hashes.
protected_hashes = {
    'MT5/TickLabCandleMarkerExchange_V109.mq5': 'f8db1d0d23fad1a96412cbaff7b9a4206344fb9ad67e60fe2c9e77cf2e003bd4',
    'MT5/TickLabHistoryBridge_V305.mq5': '1d17ee364e0425b26c4cff2c69f333c20bd748b9e7ba000816dd915005695ccf',
    'MT5/TickLabLiveBridge_V300.mq5': 'a3cf40709e7bf6ea68baa9cfbb40a84b6fb906488b6e24f2a2577bf62a9eec3e',
    'src/TickLab.App/Gateway/FileBridge/CanonicalTickArchiveStore.cs': 'a8b5ae11512387a28a7b4eb77d6df73b2db994ccc70f774cdb838bbef6b8cdf5',
    'src/TickLab.App/Gateway/FileBridge/CsvLineParser.cs': 'c1ab404b7acda4274a218f7ae9ca86bac017a028e4ed83a065434eafd0cf9859',
    'src/TickLab.App/Gateway/FileBridge/ExternalHistoryStore.cs': 'b09afd7deaa0c54ec8a507f76a16f9996213494d52ee8a3fc74e614f56bc5a1d',
    'src/TickLab.App/Gateway/FileBridge/HistoryVisibilityStore.cs': '28121e07e0edbdece025e4e423b9f09d1a86f11ae05fce01e989199945af803b',
    'src/TickLab.App/Gateway/FileBridge/LocalCandleHistoryCache.cs': 'aa3ad534d91d80be0868b13561770cf7e0d0d703f64cc45ba3c7bf7caad2253e',
    'src/TickLab.App/Gateway/FileBridge/MarkerExchangeService.cs': 'c76f9c132e6a096a6c71ce7d3f4cf30c59ba25be3e9b6eae32b8aa0ea3cc374d',
    'src/TickLab.App/Gateway/FileBridge/Mt5ConnectorSummary.cs': 'ddc50ea487380fe2499fabca666d4d38a2612090ad5452c346b8aca4fe36c6ad',
    'src/TickLab.App/Gateway/FileBridge/Mt5FileBridgeClient.cs': 'f21af284b6b80cb0c0ec4465b604dad7ce992bcc512a3e83dbda5d87876b36e7',
    'src/TickLab.App/Gateway/FileBridge/Mt5HistoryStatus.cs': '529090aa9ebc30e81f41dd30c62e9582caa1f53593d6f0728486fe86793c3cd6',
    'src/TickLab.App/Gateway/FileBridge/Mt5Paths.cs': '08f3b7f62b7f9feeff7f34359f9eef81dbd65d95cc5899a924e44ddce6759ec8',
    'src/TickLab.App/Gateway/FileBridge/Mt5ProtocolModels.cs': '1bcc108c6a8ca36a79c703bdfc914879d3661b3bf404e05327dcdc90c69d5a54',
    'src/TickLab.App/Gateway/FileBridge/Mt5SymbolInfo.cs': '9bd478f890b154acf6f5d054de45317345a5ea6c36a189a403551bff8c855c3f',
    'src/TickLab.App/Gateway/FileBridge/NativeCandleArchiveStore.cs': 'c699c8c82c7f46557ac0d717ead9a8238a3f549e136bcc4a81cb3bdbfec94724',
    'src/TickLab.App/Gateway/FileBridge/PersistentHistoryStore.cs': '0624ee64b417d02c7fc0d5814b2f5f692b53f97de445afd0b03d40ad4f34bd3b',
    'src/TickLab.App/Gateway/FileBridge/TemporaryHistoryStore.cs': '96cc81123b2ac158910686d2f3f5c3b0e97b3a9c9586343600bac9c9a9ce9b7f',
    'src/TickLab.App/Gateway/FileBridge/TickArchiveCandleCache.cs': '408cddfce9d50d66934533f3bffea99b9954da08c28d40a6689b80439fa75ca1',
}
for relative, expected_hash in protected_hashes.items():
    path = root / relative
    check(path.exists() and sha(path) == expected_hash, f'protected unchanged {relative}')

report = root / 'VALIDATION_REPORT_1_13_0_45.txt'
report.write_text(
    'TickLab v1.13.0.45 static validation\n\n'
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
