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
multi_live = read(app / 'MainWindow.MultiChartLive.cs')
alerts = read(app / 'MainWindow.AlertsReplay.cs')
chart = read(app / 'Controls' / 'CandleChartControl.cs')
symbol_xaml = read(app / 'Windows' / 'SymbolPickerWindow.xaml')
symbol_code = read(app / 'Windows' / 'SymbolPickerWindow.xaml.cs')
atomic_history = read(app / 'MainWindow.AtomicHistoryLive.cs')

# Release identity.
for needle, label in [
    ('<Version>1.13.0.66</Version>', 'version'),
    ('<AssemblyVersion>1.13.0.66</AssemblyVersion>', 'assembly version'),
    ('<FileVersion>1.13.0.66</FileVersion>', 'file version')]:
    check(needle in proj, label)
check((root / 'TickLabV1_13_0_66.sln').exists(), 'v55 solution exists')
check(not (root / 'TickLabV1_13_0_54.sln').exists(), 'old v54 solution name removed')
check(read(root / 'VERSION.txt').strip() == '1.13.0.66', 'VERSION file')
check('TickLabV1_13_0_66.sln' in read(root / 'Clean-Restore-Build.cmd'), 'build script targets v55')
check('Recorder Performance + Playback Fix' in xaml, 'window title')

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
    (demo, 'PromptAndApplyDemoPositionLevel', r'void'),
    (chart_demo, 'DrawDemoTradeHistoryPaths', r'void'),
    (chart_demo, 'DrawDemoHistoryArrow', r'void'),
    (multi_live, 'RefreshAllChartContextsLiveAsync', r'async\s+Task'),
    (multi_live, 'ProjectBridgeCandleIntoContext', r'int'),
    (alerts, 'RemoveAlertRule', r'void'),
    (alerts, 'NotifyAlert', r'void')]:
    declarations = len(re.findall(r'(?m)^\s*(?:public|private)\s+(?:static\s+)?' + return_pattern + r'\s+' + re.escape(method_name) + r'\s*\(', source))
    check(declarations == 1, f'unique method {method_name}')

# Shared rail remains intact.
check('x:Name="RightHandleStripColumn" Width="19" MinWidth="19" MaxWidth="19"' in xaml,
      '19 px right handle strip preserved')
for name in ['CodeEditorSlideButton', 'DemoTradeSlideButton', 'RightWorkspaceToggleButton']:
    check(re.search(rf'x:Name="{name}"[^>]*Width="24"', xaml, re.S) is not None,
          f'{name} remains 24 px wide')
check('x:Name="CodeEditorSlideButton" Grid.Row="1" Width="24" Height="130" MinHeight="130"' in xaml,
      'Code Editor handle remains unclipped')

# History on chart: persistence plus corrected broker-server timeline.
check('x:Name="DemoShowHistoryOnChartCheckBox"' in xaml, 'history on chart checkbox present')
check('Content="History on chart"' in xaml, 'history toggle label')
check('public bool ShowHistoryOnChart { get; set; } = true;' in demo,
      'history visibility persisted')
check('if (_demoAccount.ShowHistoryOnChart)' in demo,
      'historical overlays obey toggle')
check('OpenedServerUnix = Mt5ServerClock.ServerNowUnix(GetDemoServerUtcOffsetMinutes())' in demo,
      'new positions store broker-server opening time')
check('ClosedServerUnix = Mt5ServerClock.ServerNowUnix(GetDemoServerUtcOffsetMinutes())' in demo,
      'closed trades store broker-server closing time')
check('ResolveDemoTradeOpenedServerUnix(trade)' in demo and
      'ResolveDemoTradeClosedServerUnix(trade)' in demo,
      'older history timestamps migrate for rendering')
check('Mt5ServerClock.UtcToServerUnix' in demo,
      'UTC-only history converts into MT5 server clock domain')
check('_demoTradeHistory.Where(item => DemoSymbolsMatch(item.Symbol, context.Symbol))' in demo,
      'history remains symbol scoped across charts/timeframes')
check('DemoHistoryLineId(trade.Id, "entry")' in demo and
      'DemoHistoryLineId(trade.Id, "exit")' in demo,
      'history entry and exit overlays created')
check('DrawDemoHistoryArrow' in chart_demo and 'DrawDemoHistoryCaption' in chart_demo,
      'MT5-style history arrows and captions render')
check('DashStyles.Dash' in chart_demo,
      'entry-to-exit history connector renders')
check('DemoHistoryOverlayOverlapsCandleRange' in chart_demo,
      'visible history participates in auto-scale')
check('item.IsHistorical && DemoHistoryOverlayOverlapsCandleRange' in read(app / 'Controls' / 'CandleChartControl.cs'),
      'chart auto-scale invokes history overlap check')

# Startup-account protection remains.
check('private bool _demoTradingInitialized;' in demo, 'demo initialization gate exists')
check('if (!_demoTradingInitialized || !IsInitialized || DemoShowHistoryOnChartCheckBox is null)' in demo,
      'history checkbox ignores construction events')
check('TryReadDemoAccountDocument(DemoTradingPath + ".bak")' in demo,
      'demo account backup fallback retained')

# SL/TP editor: exact price only, 50-point click, five-times-faster hold.
check('Enter points' not in demo, 'Enter points right-click option removed')
check('modify price ▲ / ▼' in demo, 'single exact-price SL/TP menu')
check('double arrowStep = Math.Max(point, point * 50.0);' in demo,
      'one arrow click moves 50 symbol points')
check('double initial = currentLevel > 0 ? currentLevel : position.EntryPrice;' in demo,
      'missing level starts from entry price')
check('RepeatButton' in prompt, 'holdable arrow buttons retained')
check('Delay = 320' in prompt and 'Interval = 11' in prompt,
      'held arrows repeat five times faster')
check('CreateArrowButton("▲", 1' in prompt and 'CreateArrowButton("▼", -1' in prompt,
      'both price directions available')
check('One ▲ / ▼ click moves 50 symbol points' in demo and 'holding an arrow repeats five times faster' in demo,
      'editor explains click and hold behavior')

# Entry line keeps its compact BUY/SELL, lot and live P/L label while restoring drag-to-place SL/TP.
entry_overlay_pattern = re.compile(r'DemoLineId\(position\.Id, "entry"\).*?true,\s*isBuy\)\);', re.S)
check(entry_overlay_pattern.search(demo) is not None, 'entry line is draggable')
check('position.Direction' in demo, 'entry line direction retained')
check('position.Volume:0.00} lot' in demo and 'FormatDemoUsdAmount(position.FloatingProfit)' in demo,
      'entry line shows lot size and live USD P/L')
check('if (kind == "entry")' in demo and 'createsStopLoss' in demo and 'created from entry drag' in demo,
      'entry drag creates SL or TP by trade direction')
check('ResolveEntryDragPreviewKind' in chart_demo and 'NEW SL' in chart_demo and 'NEW TP' in chart_demo,
      'entry-drag creation preview restored')
check('sourceLine?.IsDraggable == true' in chart_demo,
      'only draggable demo lines complete a drag')
check('DemoLineId(position.Id, "sl")' in demo and 'true,' in demo,
      'SL line remains draggable')
check('DemoLineId(position.Id, "tp")' in demo,
      'TP line remains present and draggable')

# P/L width expansion.
check('private const double DemoPreferredPanelWidth = 647.0;' in demo,
      'demo panel expands about 0.7 cm')
check('<ColumnDefinition Width="1.55*" MinWidth="108"/>' in xaml,
      'open-position P/L content width expanded')
check('Header="P/L"' in xaml and 'Width="106" MinWidth="106"' in xaml,
      'history P/L column expanded')

# Independent simultaneous live data for every same-symbol chart context.
check((app / 'MainWindow.MultiChartLive.cs').exists(), 'multi-chart live projection source exists')
check('await RefreshAllChartContextsLiveAsync();' in main,
      'live timer updates all contexts after active chart')
check('if (!IsReplayChart(_activePricePaneId))' in main,
      'replay freezes only active replay pane')
check('context.PaneId != _activePricePaneId' in multi_live and '!IsReplayChart(context.PaneId)' in multi_live,
      'nonactive non-replay contexts selected independently')
check('context.AllNewerHistoryLoaded' in multi_live,
      'historical paging window is not forced to jump live')
check('EnsureContextLiveLists(context)' in multi_live,
      'each chart owns initialized live lists')
check('ProjectBridgeCandleIntoContext' in multi_live,
      'bridge candle projects into every matching context')
check('context.SourceCandles' in multi_live and 'context.DisplayCandles' in multi_live,
      'per-context source and display tails update')
check('context.Chart.RefreshData' in multi_live and 'context.Chart.ReplaceDataKeepingViewport' in multi_live,
      'each chart repaints without stealing active state')
check('ReadLiveSecondCandle' in multi_live and 'ReadClosedSecondCandle' in multi_live,
      'one-second bridge stream drives simultaneous charts')
check('ReadLiveCandle' in multi_live and 'ReadClosedCandle' in multi_live,
      'native live fallback retained')
check('StringComparison.OrdinalIgnoreCase' in multi_live,
      'live projection is exact same-symbol scoped')
check('_lastMultiChartLiveSecondWriteUtc = DateTime.MinValue;' in main,
      'multi-chart cursor resets on chart identity change')
check('IsReplayChart(_activePricePaneId))\n            return;' not in main,
      'active replay no longer stops every live chart')

# Accidental duplicate local introduced during edit must remain absent.
check(demo.count('double sl = kind == "sl" ? price : position.StopLoss;') == 1,
      'single SL local declaration')

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


# Demo handle rail regression repair.
check('x:Name="DemoTradeColumn" Width="0" MinWidth="0" MaxWidth="647"' in xaml,
      'demo grid column matches widened panel')
check('private const double DemoPreferredPanelWidth = 647.0;' in demo,
      'demo preferred width matches grid maximum')
check('FinishDemoTradeHandleInteraction' in demo and 'Mouse.Capture(handle, CaptureMode.Element)' in demo,
      'Demo Trading handle uses shared drag interaction pattern')
check('_demoSlideStartPanelWidth = DemoTradePanel.Visibility == Visibility.Visible' in demo,
      'Demo Trading drag starts from actual open or closed width')

# Alert line, triggering and notification repair.
check('candle.Low + sourceOffset' in alerts and 'candle.High + sourceOffset' in alerts,
      'price-touch alert uses full live candle range')
check('candleRangeTouched' in alerts and 'closeCrossed' in alerts,
      'fast touches and crosses both trigger')
check('AlertBellPlayer.PlayFor(TimeSpan.FromSeconds(5));' in alerts and alerts.index('AlertBellPlayer.PlayFor(TimeSpan.FromSeconds(5));') < alerts.index('if (!rule.ShowDesktopPopup'),
      'five-second bell starts before popup')
check('AlertLineEditRequested' in chart and 'AlertLineRemoveRequested' in chart,
      'alert line exposes edit and remove actions')
check('Header = "Remove alert"' in chart and 'Header = "Edit alert…"' in chart,
      'alert line right-click menu has normal edit/remove actions')
check('RemoveAlertById' in contexts and 'EditAlertById' in contexts,
      'chart alert menu routes to saved rule')
check('RemoveAlertRule(rule);' in alerts and 'RefreshAlertLines();' in alerts,
      'removing alert also removes its horizontal line')
check('EvaluateLiveAlerts(context);' in multi_live,
      'alerts continue evaluating on nonactive live charts')

# Trade history lifecycle colours and compact captions.
check('Color entryColor = Color.FromRgb(47, 128, 237);' in chart_demo,
      'history entry is blue')
check('Color exitColor = Color.FromRgb(224, 75, 90);' in chart_demo,
      'history exit is red')
check('CreateText(caption, 6.75, Brushes.White)' in chart_demo,
      'history caption text is fifty percent larger')

# Symbol favourites.
check('FavouriteStar_Click' in symbol_xaml and 'Content="{Binding StarGlyph}"' in symbol_xaml,
      'star control appears beside every symbol')
check('symbol-favourites.json' in symbol_code and 'SaveFavourites()' in symbol_code,
      'symbol favourites persist locally')
check('.OrderBy(item => _favouriteSymbols.Contains(item.Name) ? 0 : 1)' in symbol_code,
      'favourite symbols sort to top')
check('StarGlyph => IsFavourite ? "★" : "☆"' in symbol_code,
      'filled and empty star states render')

# Protected MT5 and FileBridge sources remain at their approved hashes.
protected_hashes = {
    'MT5/TickLabCandleMarkerExchange_V109.mq5': 'f8db1d0d23fad1a96412cbaff7b9a4206344fb9ad67e60fe2c9e77cf2e003bd4',
    'MT5/TickLabHistoryBridge_V305.mq5': '1d17ee364e0425b26c4cff2c69f333c20bd748b9e7ba000816dd915005695ccf',
    'MT5/TickLabLiveBridge_V300.mq5': 'a3cf40709e7bf6ea68baa9cfbb40a84b6fb906488b6e24f2a2577bf62a9eec3e',
    'src/TickLab.App/Gateway/FileBridge/CsvLineParser.cs': 'c1ab404b7acda4274a218f7ae9ca86bac017a028e4ed83a065434eafd0cf9859',
    'src/TickLab.App/Gateway/FileBridge/ExternalHistoryStore.cs': 'b09afd7deaa0c54ec8a507f76a16f9996213494d52ee8a3fc74e614f56bc5a1d',
    'src/TickLab.App/Gateway/FileBridge/HistoryVisibilityStore.cs': '28121e07e0edbdece025e4e423b9f09d1a86f11ae05fce01e989199945af803b',
    'src/TickLab.App/Gateway/FileBridge/LocalCandleHistoryCache.cs': 'aa3ad534d91d80be0868b13561770cf7e0d0d703f64cc45ba3c7bf7caad2253e',
    'src/TickLab.App/Gateway/FileBridge/MarkerExchangeService.cs': 'c76f9c132e6a096a6c71ce7d3f4cf30c59ba25be3e9b6eae32b8aa0ea3cc374d',
    'src/TickLab.App/Gateway/FileBridge/Mt5ConnectorSummary.cs': 'ddc50ea487380fe2499fabca666d4d38a2612090ad5452c346b8aca4fe36c6ad',
    'src/TickLab.App/Gateway/FileBridge/Mt5HistoryStatus.cs': '529090aa9ebc30e81f41dd30c62e9582caa1f53593d6f0728486fe86793c3cd6',
    'src/TickLab.App/Gateway/FileBridge/Mt5Paths.cs': '08f3b7f62b7f9feeff7f34359f9eef81dbd65d95cc5899a924e44ddce6759ec8',
    'src/TickLab.App/Gateway/FileBridge/Mt5ProtocolModels.cs': '1bcc108c6a8ca36a79c703bdfc914879d3661b3bf404e05327dcdc90c69d5a54',
    'src/TickLab.App/Gateway/FileBridge/Mt5SymbolInfo.cs': '9bd478f890b154acf6f5d054de45317345a5ea6c36a189a403551bff8c855c3f',
    'src/TickLab.App/Gateway/FileBridge/NativeCandleArchiveStore.cs': 'c699c8c82c7f46557ac0d717ead9a8238a3f549e136bcc4a81cb3bdbfec94724',
    'src/TickLab.App/Gateway/FileBridge/TemporaryHistoryStore.cs': '96cc81123b2ac158910686d2f3f5c3b0e97b3a9c9586343600bac9c9a9ce9b7f',
    'src/TickLab.App/Gateway/FileBridge/TickArchiveCandleCache.cs': '408cddfce9d50d66934533f3bffea99b9954da08c28d40a6689b80439fa75ca1',
}
for relative, expected_hash in protected_hashes.items():
    path = root / relative
    check(path.exists() and sha(path) == expected_hash, f'protected unchanged {relative}')

# v1.13.0.66 targeted regression and integrity checks
data_integrity = read(app / 'MainWindow.DataIntegrity.cs')
code_editor = read(app / 'MainWindow.CodeEditor.cs')
chart_contexts = read(app / 'MainWindow.ChartContexts.cs')
bell_player = read(app / 'Core' / 'Alerts' / 'AlertBellPlayer.cs')
alert_triggered = read(app / 'Windows' / 'AlertTriggeredWindow.cs')
alert_toast = read(app / 'Windows' / 'AlertToastWindow.cs')

check('RepairAllChartContextsFromRollingSecondsAsync' in data_integrity and
      'ReadRecentSecondCandles' in data_integrity and
      'ReadRecentM1Candles' in data_integrity and
      'BuildValidatedCandleSnapshot' in data_integrity,
      'rolling-window catch-up and candle validation present')
check('TryNormalizeCandle' in data_integrity and 'double.IsFinite' in data_integrity and
      'SortedDictionary<long, Candle>' in data_integrity,
      'invalid, duplicate and out-of-order candles normalized')
check('TryCreateSafeIndicatorSnapshot' in data_integrity and
      'CandleRevision' in data_integrity and
      'CandleRevision' in chart_contexts,
      'indicator snapshots are revision guarded')
check('await RepairAllChartContextsFromRollingSecondsAsync();' in multi_live,
      'all-chart live loop runs integrity catch-up')
check('AlertBellPlayer.PlayFor(TimeSpan.FromSeconds(5));' in alerts and
      'PlayLooping();' in bell_player and
      'duration < TimeSpan.FromSeconds(5)' in bell_player,
      'alert bell is guaranteed for at least five seconds')
check('context.Chart.Candles' in alerts and 'candleRangeTouched' in alerts and
      'observedLow' in alerts and 'observedHigh' in alerts,
      'alerts evaluate visible live candle high-low range')
notify_slice = alerts[alerts.find('private void NotifyAlert'):alerts.find('private void ReplayButton_Click')]
check('ShowDialog()' not in notify_slice, 'alert notification does not block live dispatcher')
check('Click="DemoTradeSlideButton_Click"' in xaml and
      'ReferenceEquals(sender, DemoTradeSlideButton)' in demo and
      'CancelOtherRightHandleInteractions(DemoTradeSlideButton)' in demo,
      'demo handle click and drag are isolated')
check('ReferenceEquals(sender, CodeEditorSlideButton)' in code_editor and
      'ReferenceEquals(sender, RightWorkspaceToggleButton)' in main,
      'code editor and panel handles are isolated')
check('CreateText(caption, 6.75, Brushes.White)' in chart_demo and
      'placeAbove: !entry.IsBuy' in chart_demo and
      'placeAbove: entry.IsBuy' in chart_demo,
      'history labels are enlarged and placed on arrow base side')

# v1.13.0.66 narrow hotfix checks.
check('if (display.Count == 0)\n            return false;' in data_integrity,
      'rolling integrity window cannot initialize an empty chart')
check('long loadedHistoryStart = display[0].StartUnix;' in data_integrity and
      'firstEligibleProjected = LowerBoundByStart(projected, loadedHistoryStart)' in data_integrity,
      'rolling repair preserves the indexed history prefix')
check('(context.DisplayCandles.Count > 0 || context.Chart.Candles.Count > 0)' in multi_live,
      'latest-only live stream cannot seed an empty restored chart')
check('EnsureActivatedChartHistoryLoadedAsync' in chart_contexts and
      'await SafeSelectChartAsync(symbol, timeframe);' in chart_contexts and
      'context.InitialHistoryLoadRunning' in chart_contexts,
      'empty restored chart loads its complete indexed local history on activation')
check('SizeToContent = SizeToContent.Height;' in alert_triggered and
      'Padding = new Thickness(12, 16, 12, 16)' in alert_triggered and
      'LineHeight = 21' in alert_triggered and
      'TextTrimming = TextTrimming.None' in alert_triggered,
      'main alert popup auto-sizes with full vertical text padding')
check('SizeToContent = SizeToContent.Height;' in alert_toast and
      'Padding = new Thickness(16, 18, 16, 18)' in alert_toast and
      'LineHeight = 20' in alert_toast and
      'ActualHeight' in alert_toast,
      'secondary alert toast auto-sizes without top-bottom clipping')

# v1.13.0.66 atomic history/live merge checks.
check('CommitLoadedHistoryToActiveContext();' in main,
      'successful indexed history load commits source and display lists to runtime context')
check('ReconcileActiveHistoryBeforeLiveMerge(candle);' in main,
      'active live candle reconciles authoritative history before merge')
check('ReconcileChartContextBeforeLiveMerge(context);' in multi_live,
      'inactive live chart reconciles authoritative history before merge')
check('ReconcileActiveHistoryBeforeLiveMerge(sourceWindow[^1]);' in data_integrity and
      'ReconcileChartContextBeforeLiveMerge(context);' in data_integrity,
      'rolling integrity repair cannot promote a stale one-candle context')
check('SelectAuthoritativeDisplayHistory' in atomic_history and
      'validated.Count > best.Count' in atomic_history and
      'context.Chart.Candles.Count < _displayCandles.Count' in atomic_history,
      'largest valid same-symbol history snapshot remains authoritative')
check('context.SourceCandles = _sourceCandles.ToList();' in atomic_history and
      'context.DisplayCandles = _displayCandles.ToList();' in atomic_history and
      'context.CandleRevision++;' in atomic_history,
      'loaded history owners synchronize atomically with indicator revision')
check('SyntheticChartBuilder.IsSynthetic(context.Settings.ChartType)' in atomic_history,
      'rendered synthetic candles are not mistaken for authoritative source history')
check('BuildValidatedCandleSnapshot' in atomic_history and
      'IsSameChartHistory' in atomic_history,
      'authoritative history candidates are identity checked and normalized')


# v1.13.0.66 source/display aliasing regression checks.
check('_sourceCandles = result.Source.ToList();' in main and
      '_displayCandles = result.Display.ToList();' in main,
      'history-load result owners are cloned independently')
check('seconds.ToList(),\n                seconds.ToList(),' in main and
      'exactPage.ToList(),\n                exactPage.ToList(),' in main,
      'fast history paths return independent source/display lists')
check('_displayCandles = _sourceCandles;' not in main and
      '_sourceCandles = _displayCandles;' not in main,
      'active source/display lists are never directly aliased')
check('EnsureDistinctActiveCandleLists();' in main and
      'EnsureDistinctActiveCandleLists' in atomic_history,
      'active live mutations enforce distinct candle owners')
check('EnsureDistinctContextCandleLists(context);' in multi_live and
      'EnsureDistinctContextCandleLists(context);' in data_integrity,
      'multi-chart and integrity paths enforce distinct candle owners')
check('ReferenceEquals(_sourceCandles, _displayCandles)' in atomic_history and
      '_sourceCandles = _displayCandles.ToList();' in atomic_history,
      'active alias guard copies before mutation')
check('ReferenceEquals(context.SourceCandles, context.DisplayCandles)' in atomic_history and
      'context.SourceCandles = context.DisplayCandles.ToList();' in atomic_history,
      'context alias guard copies before mutation')
check('List<Candle> nativeSourceReplacement = display.ToList();' in data_integrity and
      'source.Clear();\n            source.AddRange(nativeSourceReplacement);' in data_integrity,
      'native integrity replacement copies display before clearing source')
check('if (updateSecondSource)\n            _displayCandles = _sourceCandles.ToList();' in main,
      'one-second source/display owners remain independent')


# v1.13.0.66 tick replay and vertical-line regression checks.
replay_engine = read(app / 'Core' / 'Replay' / 'MarketReplayEngine.cs')
replay_window = read(app / 'Windows' / 'MarketReplayWindow.cs')
independent_indicators = read(app / 'MainWindow.IndependentIndicators.cs')

check('public event Action<CandleMarker>? InteractiveMarkerRemoveRequested;' in chart and
      'HitTestInteractiveSelectionMarker' in chart and
      'Remove replay line' in chart,
      'replay vertical line is selectable and right-click removable')
check('bool canBeginMarkerDrag = !replayLine ||' in chart and
      'HitTestInteractiveSelectionMarker(layout, mouse.X) is not null' in chart and
      '_dragMode =' in chart,
      'replay line only captures a click near itself and normal chart dragging remains available')
check('Cursor = Cursors.SizeWE;' in chart and
      'HitTestReplayInteractiveMarker(layout, mouse.X) is not null' in chart,
      'replay selectors expose drawing-style horizontal drag cursor')
check('chart.InteractiveMarkerRemoveRequested += HandleInteractiveMarkerRemoveRequested;' in contexts,
      'replay-line removal is routed to the owning chart runtime')
check('private int? _replaySetupChartId;' in alerts and
      '_replay?.ChartId ?? _replayMarkerChartId ?? _replaySetupChartId' in alerts and
      'context.IdentityGeneration != identityGeneration' in alerts,
      'replay remains bound to its selected chart and rejects stale async loads')
check('CurrentCandleStartUnix' in replay_engine and
      'targetBucketStart ??= nextBucketStart;' in alerts and
      'runtime.Engine.CompleteCurrentCandle();' in alerts,
      'step-candle completes exactly the selected candle bucket')
check('ProcessNextReplayTick(runtime);' in alerts and
      'RenderReplayChart(forceFit: false);' in alerts,
      'first saved raw tick is rendered immediately after replay load')
check('ReadTicksForReplay' in alerts and
      'runtime.Engine.Process(tick);' in alerts and
      'tick.TimeMilliseconds' in alerts,
      'replay consumes saved raw ticks in timestamp order')
check('LastTickClosedCandle' in replay_engine and
      'ReplayCandleBuilder' in replay_engine and
      '_tickVolume++' in replay_engine and
      'tick.Ask - tick.Bid' in replay_engine,
      'raw ticks form candle body, wick, tick volume and spread')
check('runtime.Context.SourceCandles = visible.ToList();' in alerts and
      'runtime.Context.DisplayCandles = visible.ToList();' in alerts,
      'replay source and display data remain independently owned')
check('int appendedCount = visible.Count > previousCount' in alerts and
      'ReplaceDataKeepingViewport' in alerts,
      'replay updates the active candle without false viewport shifts')
check('Candle[] candles = source.Chart.Candles.ToArray();' in independent_indicators and
      'OriginalDisplayCandles.ToArray()' not in independent_indicators,
      'replay indicators cannot inspect future candles')
check('if (IsReplayChart(context.PaneId))' in main and
      'Replay chart is isolated. End replay before loading older candles.' in main and
      'Replay chart is isolated. End replay before loading newer candles.' in main,
      'replay chart cannot page future history into its candle set')
check('End replay before refreshing this chart.' in contexts and
      'End replay before opening the earliest chart window.' in main and
      'End replay before returning this chart to the latest window.' in main,
      'refresh Home and End cannot replace replay data')
check('OriginalSourceCandles' in alerts and
      'OriginalDisplayCandles' in alerts and
      'RestoreViewport(runtime.OriginalViewport)' in alerts,
      'ending replay restores the original chart data and viewport')
check('private bool IsReplayChart(int paneId) => _replay?.ChartId == paneId;' in alerts,
      'live-update isolation remains scoped to one replay chart')
check('Moving the lines never starts replay; press Play' in replay_window and
      'currently visible on the chart' in replay_window,
      'replay controls explain visible-window placement and Play-only start')

# v1.13.0.66 replay selector and range workflow.
check('private readonly CheckBox _replayLineCheckBox;' in replay_window and
      'private readonly CheckBox _compactReplayLineCheckBox;' in replay_window and
      'CreateReplayCheckBox("Replay line"' in replay_window and
      'HandleReplayLineUiChange(true)' in replay_window and
      'HandleReplayLineUiChange(false)' in replay_window and
      '_compactReplayLineCheckBox.IsChecked = enabled;' in replay_window,
      'Replay window keeps synchronized full/compact replay-line controls')
check('private readonly CheckBox _replayRangeCheckBox;' in replay_window and
      'private readonly CheckBox _compactReplayRangeCheckBox;' in replay_window and
      'CreateReplayCheckBox("Replay range"' in replay_window and
      'HandleReplayRangeUiChange(true)' in replay_window and
      'HandleReplayRangeUiChange(false)' in replay_window,
      'Replay window has synchronized full/compact replay-range option')
check('SetReplayLineChecked(bool enabled)' in replay_window and
      'SetReplayRangeChecked(bool enabled)' in replay_window and
      '_synchronizingReplayLineCheckBox' in replay_window and
      '_synchronizingReplayRangeCheckBox' in replay_window,
      'programmatic replay checkbox synchronization cannot retrigger user events')
check('GetReplayVisibleRange(context, availableCandles.Count)' in alerts and
      '(visibleCount - 1) / 2' in alerts and
      '(visibleCount - 1) * 0.35' in alerts and
      '(visibleCount - 1) * 0.65' in alerts,
      'selectors are placed inside the current visible candle window, not on the live candle')
check('"Replay start"' in alerts and '"TickLabReplay"' in alerts and
      '"Replay end"' in alerts and '"TickLabReplayEnd"' in alerts,
      'yellow start and red end replay marker identities are distinct')
check('Color.FromRgb(250, 204, 21)' in chart and
      'Color.FromRgb(239, 68, 68)' in chart and
      'InteractiveReplayEndMarker' in chart,
      'chart renders yellow start and red end selectors')
check('HitTestReplayInteractiveMarker' in chart and
      '_interactiveReplayEndDragging' in chart and
      'MoveInteractiveReplayEndMarkerTo' in chart,
      'both replay selectors drag independently without stealing normal chart pan')
check('InteractiveMarkerPlacementCompleted?.Invoke(completedMarker);' in chart and
      'chart.InteractiveMarkerPlacementCompleted += CandleChart_InteractiveMarkerPlacementCompleted;' in contexts,
      'selector drag completion is routed once to the owning chart')
check('await LoadReplayAsync(serverTime' not in alerts[alerts.find('private void HandleReplayMarkerPlacementCompleted'):alerts.find('private bool IsReplayChart')],
      'dragging or releasing replay selectors never auto-loads ticks')
check('private async void StartOrToggleReplay()' in alerts and
      'await LoadReplayAsync(serverTime, startPlaying: true);' in alerts and
      '_replayWindow.PlayPauseRequested += StartOrToggleReplay;' in alerts,
      'Play is the action that loads and starts replay from the selected line')
check('endBucketStart <= startUnix' in alerts and
      'The red end line must be to the right of the yellow start line.' in alerts,
      'range validates red end is after yellow start')
check('EndUnixExclusive' in alerts and
      'preparationEndUnix = GetReplayPreparationEndUnix' in alerts and
      'runtime.EndMillisecondsExclusive' in alerts,
      'raw tick reads are bounded to the selected replay range')
check('tick.TimeMilliseconds >= endMillisecondsExclusive' in alerts and
      'runtime.RangeCompleted = true;' in alerts and
      'Replay range complete at the red end line.' in alerts,
      'replay automatically stops after the final tick of the red end candle')
check('context.Chart.InteractiveReplayEndMarker = _replayRangeMode' in alerts and
      '? replayEndMarker' in alerts and
      'retainedReplayEndMarker' in alerts and
      'runtime.Context.Chart.InteractiveReplayEndMarker = retainedReplayEndMarker;' in alerts,
      'range selectors survive replay chart restoration when requested')
check('ClearReplayMarker(bool syncCheckBox = true)' in alerts and
      'InteractiveReplayEndMarker = null;' in alerts and
      '_replayWindow?.SetReplayLineChecked(false);' in alerts,
      'unticking or right-click removing replay clears both range selectors')
check('Moving replay selectors never loads or starts replay.' in alerts and
      'Drag either line, then press Play.' in alerts,
      'status text matches Play-only replay workflow')

# v1.13.0.66 instant replay performance and hidden-live checks.
canonical = read(app / 'Gateway' / 'FileBridge' / 'CanonicalTickArchiveStore.cs')
persistent = read(app / 'Gateway' / 'FileBridge' / 'PersistentHistoryStore.cs')
check('replay_source_index.json' in canonical and
      'ResolveReplayHistoricalSources' in canonical and
      'TryGetHistoricalSourceRangeMilliseconds' in canonical,
      'persistent replay source-range index maps selected timestamps to small raw files')
check('Building this map reads filenames and file metadata only' in canonical and
      'TryParseTick' in canonical,
      'replay index creation does not parse an entire quarter of tick rows')
check('private readonly object _replayIndexSync = new();' in canonical and
      'lock (_replayIndexSync)' in canonical,
      'replay source index is independent of canonical merge lock')
check('ReadBridgeTicksForReplayFast' in persistent and
      'GetTickArchiveFolder(connectorId, symbol)' in persistent,
      'fast replay reader uses persistent per-symbol replay index location')
check('Primary instant Play path: TickLab\'s permanent ticks.tlt archive' in alerts and
      alerts.find('_historyStore.ReadTicksForReplay(') < alerts.find('_historyStore.ReadBridgeTicksForReplayFast('),
      'Play seeks permanent ticks.tlt before bridge CSV fallback')
load_start = alerts.find('private async Task LoadReplayAsync')
load_end = alerts.find('private async void StartOrToggleReplay', load_start)
load_body = alerts[load_start:load_end]
check(load_body.find('RenderReplayChart(forceFit: false);') < load_body.find('await ReadReplayTicksImmediatelyAsync'),
      'yellow start candle and future candles hide before disk reads begin')
check('Future candles are hidden. Opening the saved raw ticks at the yellow start line' in alerts,
      'Replay window reports immediate visible switch before raw tick open')
check('Very recent ticks may not have reached ticks.tlt yet.' in alerts and
      'ScheduleReplayCanonicalWarmup' in alerts and
      'Task background = Task.Run(() => _historyStore.SyncTickArchives' in alerts,
      'bridge fallback warms permanent archive only after a permanent miss')
check('maximumEndUnix' in canonical and 'maximumEndUnix' in persistent,
      'background canonical warmup is bounded to the nearby replay slice')
check('HiddenLiveSourceCandles' in alerts and 'HiddenLiveDisplayCandles' in alerts and
      'UpdateReplayHiddenLiveState(snapshot, identity, serverOffset);' in multi_live,
      'live bridge keeps replay chart updated invisibly in hidden live lists')
check('runtime.HiddenLiveSourceCandles.ToList()' in alerts and
      'runtime.HiddenLiveDisplayCandles.ToList()' in alerts,
      'End Replay reveals continuously updated hidden live chart')
check('Width = 760;' in replay_window and 'Height = 420;' in replay_window and
      'MinHeight = 74' in replay_window and
      '_fullRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });' in replay_window,
      'replay full window and status area remain enlarged to prevent bottom text clipping')
check('Preparing selected replay period' not in alerts,
      'old quarter-scan wait message and awaited preparation path removed')
check('Thread.Yield();' in canonical and 'one source' in canonical,
      'background canonical conversion yields between source files')

# v1.13.0.66 compact Replay controls and visible speed checks.
check('public void SetCompactMode(bool compact)' in replay_window and
      '_compactRoot.Visibility = Visibility.Visible;' in replay_window and
      '_fullRoot.Visibility = Visibility.Collapsed;' in replay_window,
      'Replay supports compact/full window modes')
check('var compactButton = CreateButton("▁ Compact"' in replay_window and
      'var expandButton = CreateButton("⛶"' in replay_window,
      'Replay full and compact modes expose collapse/expand controls')
check('_compactPlayButton' in replay_window and '_compactReverseButton' in replay_window and
      '_compactForwardButton' in replay_window and '_compactSpeedButton' in replay_window,
      'compact Replay tab contains requested playback controls')
check('Background = Brushes.White' in replay_window and
      'Foreground = Brushes.Black' in replay_window and
      'string text = $"Speed {FormatSpeed(speed)}";' in replay_window,
      'speed button is white/black and always shows selected speed')
check('if (startPlaying && runtime.IsPlaying)' in alerts and
      '_replayWindow?.SetCompactMode(true);' in alerts,
      'successful Play automatically collapses Replay to compact tab')
check(alerts.count('_replayWindow?.SetCompactMode(true);') == 2,
      'manual reverse/forward do not force an expanded Replay window compact')

# v1.13.0.66 replay speed, compact default, selector sharpness/colour and alert colour.
check('string caption = IsReplayEndMarker(marker) ? "END" : "START";' in chart and
      'drawingContext.DrawText(label, new Point(left, top));' in chart and
      'drawingContext.DrawText(label, new Point(left, bottom));' in chart and
      'CreateText(caption, 9, markerBrush)' in chart,
      'yellow/red replay selectors show tiny START/END labels at top and bottom')
check('var compactEndButton = CreateButton("End Replay", 88);' in replay_window and
      'compactEndButton.Click += (_, _) => StopRequested?.Invoke();' in replay_window and
      'compactControls.Children.Add(compactEndButton);' in replay_window,
      'compact Replay tab exposes End Replay')
check('ProcessPreviousReplayTick(ReplayRuntime runtime, bool synchronizeClock = true)' in alerts and
      alerts.count('ProcessPreviousReplayTick(runtime, synchronizeClock: false)') >= 3 and
      'double firstQuantum = _replayTimer.Interval.TotalMilliseconds * _replaySpeed;' in alerts,
      'Reverse timed loops preserve simulated clock and obey selected speed')
check('_replayWindow.SpeedChanged += ApplyReplaySpeed;' in alerts and
      'runtime.LastPlaybackUtc = DateTime.UtcNow;' in alerts,
      'changing replay speed resynchronizes playback clock')
check('_replayWindow.SetCompactMode(true);' in alerts and 'if (_replay is null)' in alerts,
      'main Replay button opens compact mode by default')
check('ReplayStartLineColor' in chart and 'ReplayEndLineColor' in chart and
      'SnapStrokeCoordinate(x, pen.Thickness)' in chart and
      'double lineTop = Math.Min' in chart and 'double lineBottom = Math.Max' in chart,
      'replay selectors use configurable colours, pixel snapping and label gaps')
check('Start colour' in replay_window and 'End colour' in replay_window and
      'ChooseReplayLineColor(isStart: true)' in replay_window and
      'StartLineColorChanged' in replay_window and 'EndLineColorChanged' in replay_window,
      'expanded Replay window exposes start/end colour selectors')
alert_models = read(app / 'Core' / 'Alerts' / 'AlertModels.cs')
alert_manager = read(app / 'Windows' / 'AlertManagerWindow.cs')
alert_draw = read(app / 'Controls' / 'CandleChartControl.Alerts.cs')
check('public string LineColor { get; init; } = "#F5B83E";' in alert_models and
      'LineColorRequested' in alert_manager and 'Line colour' in alert_manager and
      'rule.LineColor' in alerts and 'line.Color' in alert_draw,
      'Alerts window supports persistent per-alert line colour')

# v1.13.0.66 immediate range completion, selector pixels and alert bulk management.
chart_settings = read(app / 'Core' / 'Settings' / 'ChartSettings.cs')
alert_editor = read(app / 'Windows' / 'AlertEditorWindow.cs')
alert_overlay = read(app / 'Core' / 'Alerts' / 'AlertLineOverlay.cs')
check('CompleteReplayRangeImmediately' in alerts and
      'runtime.IsPlaying = false;' in alerts and
      '_replayTimer.Stop();' in alerts and
      'UpdateReplayWindow("Replay range complete at the red end line.");' in alerts and
      'DispatcherPriority.Background' in alerts,
      'range completion flips Pause to Play before deferred final replay redraw')
check('bool initialSliceReachesRangeEnd' in alerts and
      'runtime.HasMore = read.HasMore || !initialSliceReachesRangeEnd;' in alerts and
      'bool sliceReachesRangeEnd' in alerts and
      '(read.HasMore || !sliceReachesRangeEnd)' in alerts,
      'range reader knows when loaded slice already reaches red boundary and avoids delayed extra disk read')
check('ReplayStartLineThickness' in chart_settings and 'ReplayEndLineThickness' in chart_settings and
      'StartLineThicknessChanged' in replay_window and 'EndLineThicknessChanged' in replay_window and
      'Start 1 px' in replay_window and 'End 1 px' in replay_window,
      'expanded Replay window persists independent START/END pixel thickness')
check('Settings.ReplayEndLineThickness' in chart and 'Settings.ReplayStartLineThickness' in chart and
      'Math.Clamp(selectorThickness, 1.0, 6.0)' in chart,
      'replay selector renderer uses configured pixel thickness')
check('public double LineThickness { get; init; } = 1.25;' in alert_models and
      'Line appearance' in alert_editor and 'LineColor = _selectedLineColor' in alert_editor and
      'LineThickness = _lineThicknessBox.SelectedItem is double pixels' in alert_editor and
      'Math.Clamp(line.Thickness, 0.5, 8.0)' in alert_draw,
      'alert settings page persists colour and pixel thickness and renderer uses it')
check('Delete selected' in alert_manager and 'IsChecked' in alert_manager and
      'DeleteSelectedRequested' in alert_manager and 'DeleteSelectedAlerts' in alerts and
      'HashSet<string> ids' in alerts,
      'active alerts support checkbox selection and one-click bulk delete')


# v1.13.0.66 raw Tick chart and alert-contrast checks.
timeframes = read(app / 'Core' / 'Market' / 'TimeframeDefinition.cs')
tick_chart = read(app / 'Controls' / 'TickChartControl.cs')
raw_tick = read(app / 'MainWindow.RawTickChart.cs')
chart_settings = read(app / 'Core' / 'Settings' / 'ChartSettings.cs')
chart_settings_window = read(app / 'Windows' / 'ChartSettingsWindow.xaml.cs')
alert_manager_63 = read(app / 'Windows' / 'AlertManagerWindow.cs')
alert_editor_63 = read(app / 'Windows' / 'AlertEditorWindow.cs')
bridge_reader = read(app / 'Gateway' / 'FileBridge' / 'Mt5FileBridgeClient.cs')
chart_pane = read(app / 'Controls' / 'ChartPaneControl.cs')
main_xaml_63 = read(app / 'MainWindow.xaml')

check('new TimeframeDefinition(1, TimeframeUnit.Tick, true, null)' in timeframes and
      timeframes.index('TimeframeUnit.Tick, true') < timeframes.index('TimeframeUnit.Second, true'),
      'Tick built-in appears before 1s')
check('TimeframeUnit.Tick => "Tick"' in timeframes and 'IsRawTickChart => Unit == TimeframeUnit.Tick' in timeframes,
      'Tick timeframe has dedicated raw identity')
check('PrimaryTickChart' in main_xaml_63 and 'SetRawTickMode' in chart_pane and 'TickChartControl' in chart_pane,
      'primary and workspace panes support dedicated raw Tick renderer')
check('GetTickCoverageForReplay' in raw_tick and 'ReadTicksForReplay' in raw_tick and
      'LoadOlderRawTicksAsync' in raw_tick and 'ReplaceTicksKeepingViewport' in raw_tick,
      'raw Tick chart pages permanent ticks.tlt history while preserving viewport')
check('ReadLiveRawTicksSince' in bridge_reader and 'ticks_live_' in bridge_reader and
      '.OrderBy(item => item.TimeMilliseconds)' in bridge_reader and '.TakeLast(maximumRecords)' in bridge_reader and
      '.ThenBy(item => item.Bid)' not in bridge_reader,
      'live raw Tick reader keeps newest records and stable same-millisecond source order')
check('public event EventHandler? OlderHistoryRequested' in tick_chart and
      'public void ZoomHorizontal' in tick_chart and 'public void ZoomVertical' in tick_chart and
      'GetMinimumVisibleCount' in tick_chart and 'availableCount > 0 ? 1 : 0' in tick_chart and
      'Key.Home' in tick_chart and 'Key.End' in tick_chart,
      'raw Tick chart supports individual-tick zoom, scales and full-history paging navigation')
check('DrawTimeScale' in tick_chart and 'DrawPriceScale' in tick_chart and
      'DrawSeries(drawingContext, layout, false' in tick_chart and
      'DrawSeries(drawingContext, layout, true' in tick_chart,
      'raw Tick chart renders time/price scales plus Bid and Ask series')
check('TickBidColor' in chart_settings and 'TickAskColor' in chart_settings and
      'TickBidThickness' in chart_settings and 'TickAskThickness' in chart_settings and
      'Bid tick colour' in chart_settings_window and 'Ask tick pixels' in chart_settings_window,
      'chart settings persist Bid/Ask tick colours and pixel thickness')
check('ApplicationThemeManager.ApplyToWindow(this)' in alert_manager_63 and
      'PanelBrush' in alert_manager_63 and 'TextBrush' in alert_manager_63 and
      'ApplicationThemeManager.ApplyToWindow(this)' in alert_editor_63,
      'alert manager/editor force current high-contrast theme instead of white-on-white')
check('context.Timeframe.IsRawTickChart' in multi_live and
      'context.Timeframe.IsRawTickChart' in read(app / 'MainWindow.DataIntegrity.cs'),
      'raw Tick contexts are isolated from one-second candle live/integrity projection')
check('ChartCountLabelText' in main_xaml_63 and '"TICKS "' in raw_tick,
      'main chart header labels raw events as ticks')
check(not (root / 'TickLabV1_13_0_62.sln').exists() and (root / 'TickLabV1_13_0_66.sln').exists(),
      'only current v63 solution name is shipped')
# MT5 EA sources remain byte-identical; only the desktop bridge reader was extended.
for relative in [
    'MT5/TickLabCandleMarkerExchange_V109.mq5',
    'MT5/TickLabHistoryBridge_V305.mq5',
    'MT5/TickLabLiveBridge_V300.mq5']:
    expected = protected_hashes[relative]
    check(sha(root / relative) == expected, f'v63 MT5 EA unchanged {relative}')

# v1.13.0.66 Tick chart-type, scale parity, replay layering/performance and turbo checks.
chart_types = read(app / 'MainWindow.ChartTypes.cs')
contexts_64 = read(app / 'MainWindow.ChartContexts.cs')
check('(ChartVisualType.Tick, "Tick")' in chart_types and
      chart_types.index('(ChartVisualType.Tick, "Tick")') < chart_types.index('(ChartVisualType.Candles, "Candles")'),
      'Tick appears as first standard chart type above Candles')
check('GetAllTimeframes().Where(item => !item.IsRawTickChart)' in main,
      'Tick is hidden from timeframe toolbar')
check('LastCandleTimeframe' in contexts_64 and 'LastCandleChartType' in contexts_64 and
      'context.LastCandleTimeframe = context.Timeframe;' in chart_types,
      'Tick chart type preserves previous candle timeframe and chart type')
check('DrawScaleBackgrounds(drawingContext, layout);' in tick_chart and
      'Settings.PriceScaleBackgroundColor' in tick_chart and 'Settings.TimeScaleBackgroundColor' in tick_chart and
      'Settings.PriceScaleTextColor' in tick_chart and 'Settings.TimeScaleTextColor' in tick_chart and
      'Settings.GridColor' in tick_chart and 'Settings.GridThickness' in tick_chart and 'Settings.GridOpacity' in tick_chart,
      'Tick scales and grid inherit candle chart appearance settings')
check('DrawReplayInteractiveMarkerLines(drawingContext, layout);' in chart and
      chart.find('DrawReplayInteractiveMarkerLines(drawingContext, layout);') < chart.find('DrawCandles(') and
      'The replay selector stroke is drawn in the below-candle layer.' in chart,
      'Replay START/END strokes render behind candle bodies and wicks')
check('100×' in replay_window and '250×' in replay_window and '500×' in replay_window and
      '750×' in replay_window and '1000×' in replay_window and '1250×' in replay_window and
      '1500×' in replay_window and '2000×' in replay_window and
      '5000×' in replay_window and '10000×' in replay_window and '15000×' in replay_window and
      '20000×' in replay_window and '25000×' in replay_window and '30000×' in replay_window and
      'Math.Clamp(speed, 0.01, 30000.0)' in alerts,
      'Replay exposes requested turbo speeds through 30000x')
check('GetReplayTickBudget' in alerts and '750_000' in alerts and
      'ShouldRenderReplayFrame' in alerts and 'TimeSpan.FromMilliseconds(100)' in alerts and
      'ProcessNextReplayTick(runtime, synchronizeClock: false)' in alerts and
      'ProcessPreviousReplayTick(runtime, synchronizeClock: false)' in alerts,
      'turbo replay processes raw ticks while throttling only visible redraws')
load_next = alerts[alerts.find('private async Task LoadNextReplayBatchAsync'):alerts.find('private void RefreshReplayIndicators')]
check(load_next.find('_historyStore.ReadTicksForReplay(') < load_next.find('_historyStore.ReadBridgeTicksForReplayFast('),
      'later replay batches also use ticks.tlt before bridge fallback')


# v1.13.0.66 recorder, right-space and ultra-speed checks.
recorder = read(app / 'Windows' / 'ScreenRecorderWindow.cs')
avi = read(app / 'Core' / 'Recording' / 'MjpegAviWriter.cs')
recorder_main = read(app / 'MainWindow.ScreenRecorder.cs')
check('x:Name="RecorderButton"' in xaml and 'Click="RecorderButton_Click"' in xaml,
      'top-right REC button exists')
check('StartRecording' in recorder and 'TogglePause' in recorder and 'StopRecordingCore' in recorder,
      'recorder exposes record pause resume stop workflow')
check('SaveScreenshot' in recorder and 'PngBitmapEncoder' in recorder,
      'recorder includes PNG screenshot capture')
check('Environment.SpecialFolder.MyVideos' in recorder and 'TickLab", "Recordings' in recorder,
      'recordings save under Videos TickLab Recordings')
check('RecordingDispositionWindow' in recorder and 'KeepFile' in recorder and 'Description' in recorder,
      'recording completion asks save/delete and description')
check('UseShellExecute = true' in recorder and 'PlaySelected' in recorder,
      'saved media opens through Windows default player')
check('MjpegAviWriter' in avi and 'MJPG' in avi and 'idx1' in avi,
      'dependency-free MJPEG AVI writer present')
check('CaptureTickLabWindowFrame' in recorder_main and 'RenderTargetBitmap' in recorder_main,
      'recorder captures TickLab WPF surface')
check('DefaultRightBlankSpace = 288.0' in tick_chart and 'DataRight' in tick_chart and 'DataWidth' in tick_chart,
      'Tick chart reserves about three inches to the right of newest tick')
check('DrawLatestPriceLines' in tick_chart and 'DrawLatestPriceLine' in tick_chart and '"Bid"' in tick_chart and '"Ask"' in tick_chart,
      'Tick chart latest Bid and Ask horizontal price lines are rendered')
check('5000×' in replay_window and '10000×' in replay_window and '15000×' in replay_window and
      '20000×' in replay_window and '25000×' in replay_window and '30000×' in replay_window,
      'ultra replay speed menu contains every requested speed')
check('Math.Clamp(speed, 0.01, 30000.0)' in alerts and '750_000' in alerts,
      'ultra replay accepts 30000x and expands processing budget')
check('ShutdownScreenRecorder();' in main,
      'recorder is finalized during TickLab shutdown')

# v1.13.0.66 recorder regression: chart-priority capture and fast-player AVI metadata.
check('DispatcherPriority.ApplicationIdle' in recorder,
      'recorder capture yields to chart/input/render dispatcher priorities')
check('Channel.CreateBounded<BitmapSource>' in recorder and 'FrameQueueCapacity = 2' in recorder and
      'TryWrite(frame)' in recorder and 'skipped to protect chart' in recorder,
      'recorder uses bounded frame queue and drops recorder frames instead of blocking chart')
check('Task.Run(() => EncodeQueuedFramesAsync(writer, reader))' in recorder and
      'JpegBitmapEncoder' in recorder and recorder.find('JpegBitmapEncoder') > recorder.find('EncodeQueuedFramesAsync'),
      'JPEG compression and AVI writes run through background encoder worker')
check('QualityLevel = 72' in recorder,
      'video JPEG quality reduced enough to lower disk/CPU pressure')
check('RecorderMaxPixelWidth = 1600' in recorder_main and 'RecorderMaxPixelHeight = 900' in recorder_main and
      'captureScale' in recorder_main and 'VisualBrush(root)' in recorder_main,
      'large recorder frames are bounded to 1600x900 while preserving the whole TickLab surface')
check('CaptureTickLabWindowScreenshot' in recorder_main and '_captureScreenshot' in recorder,
      'screenshots retain independent full-resolution capture path')
check('AviHasIndex | AviIsInterleaved | AviTrustChunkType' in avi and
      '_avihMaxBytesPerSecondPosition' in avi and '_avihSuggestedBufferPosition' in avi and
      '_strhSuggestedBufferPosition' in avi and '_largestFrameSize' in avi,
      'AVI finalization writes player-friendly index flags throughput and buffer metadata')
check('_writer.Flush();' in avi and '_stream.Flush();' in avi and '_stream.Flush(true)' not in avi,
      'AVI finalization avoids unnecessary forced physical-disk flush latency')

report = root / 'VALIDATION_REPORT_1_13_0_66.txt'
report.write_text(
    'TickLab v1.13.0.66 static validation\n\n'
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
