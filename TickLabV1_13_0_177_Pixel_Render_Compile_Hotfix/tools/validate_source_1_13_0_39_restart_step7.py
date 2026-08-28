from pathlib import Path
import hashlib, re, sys, xml.etree.ElementTree as ET
from pygments import lex
from pygments.lexers.dotnet import CSharpLexer
from pygments.token import Token

root=Path(__file__).resolve().parents[1]
base=Path('/mnt/data/work_step7/TickLabV1_13_0_38_Restart_Step6A_Indicator_Context_Menu_Correction')
app=root/'src'/'TickLab.App'
passed=[]; failed=[]
def check(cond,label): (passed if cond else failed).append(label)
def read(rel): return (root/rel).read_text(encoding='utf-8-sig',errors='ignore')
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()

proj=read('src/TickLab.App/TickLab.App.csproj')
xaml=read('src/TickLab.App/MainWindow.xaml')
demo=read('src/TickLab.App/MainWindow.DemoTrading.cs')
settings=read('src/TickLab.App/Core/Settings/ChartSettings.cs')
settings_window=read('src/TickLab.App/Windows/ChartSettingsWindow.xaml.cs')
chart=read('src/TickLab.App/Controls/CandleChartControl.cs')
chart_contexts=read('src/TickLab.App/MainWindow.ChartContexts.cs')
management=read('src/TickLab.App/MainWindow.IndicatorManagement.cs')
builtin_plot=read('src/TickLab.App/Controls/BuiltInIndicatorPlotControl.cs')
script_plot=read('src/TickLab.App/Controls/TickScriptIndicatorPlotControl.cs')
detached=read('src/TickLab.App/Windows/DetachedChartWindow.xaml')
detached_cs=read('src/TickLab.App/Windows/DetachedChartWindow.xaml.cs')
surface=read('src/TickLab.App/Controls/WorkspaceSurfaceControl.cs')
workspaces=read('src/TickLab.App/MainWindow.Workspaces.cs')
trade_overlay=read('src/TickLab.App/Controls/CandleChartControl.DemoTrading.cs')

# Release identity.
for needle,label in [('<Version>1.13.0.39</Version>','version'),('<AssemblyVersion>1.13.0.39</AssemblyVersion>','assembly version'),('<FileVersion>1.13.0.39</FileVersion>','file version')]: check(needle in proj,label)
check((root/'TickLabV1_13_0_39.sln').exists(),'solution exists')
check((root/'VERSION.txt').read_text().strip()=='1.13.0.39','VERSION file')
check('TickLabV1_13_0_39.sln' in read('Clean-Restore-Build.cmd'),'clean build script targets Step 6 solution')
check('Restart Step 7 Demo Trading Reliability and History' in xaml,'window title')

# XML parse and unique names.
for p in app.rglob('*.xaml'):
    try: ET.parse(p); check(True,f'XAML parses {p.relative_to(app)}')
    except Exception as e: check(False,f'XAML parses {p.relative_to(app)}: {e}')
for p in app.rglob('*.xaml'):
    names=re.findall(r'x:Name="([^"]+)"',p.read_text(errors='ignore'))
    check(len(names)==len(set(names)),f'unique x:Name {p.relative_to(app)}')

# XAML handlers resolve.
all_main='\n'.join(p.read_text(errors='ignore') for p in app.glob('MainWindow*.cs'))
handler_pattern=r'\b(?:Click|Checked|Unchecked|TextChanged|SelectionChanged|SelectedDateChanged|PreviewMouse\w+|PreviewKey\w+|Mouse\w+|Key\w+|Drag\w+|Drop|Loaded|Closing)="([A-Za-z_]\w*)"'
for p in app.rglob('*.xaml'):
    handlers=set(re.findall(handler_pattern,p.read_text(errors='ignore')))
    code=all_main if p.name=='MainWindow.xaml' else (p.with_suffix('.xaml.cs').read_text(errors='ignore') if p.with_suffix('.xaml.cs').exists() else '')
    for handler in handlers: check(re.search(r'\b'+re.escape(handler)+r'\s*\(',code) is not None,f'handler {p.name}:{handler}')

# C# punctuation balance using lexer (ignores strings/comments).
for p in app.rglob('*.cs'):
    stack=[]; pairs={')':'(',']':'[','}':'{'}; ok=True
    for typ,value in lex(p.read_text(encoding='utf-8-sig',errors='ignore'),CSharpLexer()):
        if typ in Token.Punctuation:
            for ch in value:
                if ch in '([{': stack.append(ch)
                elif ch in ')]}':
                    if not stack or stack.pop()!=pairs[ch]: ok=False; break
        if not ok: break
    check(ok and not stack,f'C# balanced {p.relative_to(app)}')

# Chart colour picker root fix.
for needle,label in [
    ('Uid = key','chart colour key stored separately'),
    ('string.IsNullOrWhiteSpace(swatch.Uid)','colour click reads Uid'),
    ('_colorBoxes.TryGetValue(swatch.Uid','colour setting lookup works'),
    ('ColorDisplayHelper.ApplyToButton(swatch, box.Text)','colour swatch and tooltip retained'),
    ('Visibility = Visibility.Collapsed','raw colour code remains hidden')]: check(needle in settings_window,label)
add_color = settings_window[settings_window.index('private void AddColor'):settings_window.index('private void AddSlider')]
check('Uid = key' in add_color and 'Tag = string.Empty' in add_color,'colour swatch keeps setting key separate from colour value')

# Ask line configuration/rendering.
for needle in ['ShowAskPriceLine','AskPriceLineColor','AskPriceLineTextColor','AskPriceLineStyle','AskPriceLineThickness']:
    check(needle in settings, f'Ask setting {needle}')
    check(needle in settings_window, f'Ask settings UI/read {needle}')
for needle in ['DrawAskPriceLine(drawingContext, layout)','private void DrawAskPriceLine','ASK {askPrice','live.Close + Math.Max(0, live.Spread) * point']:
    check(needle in chart,f'Ask line renderer {needle}')

# Demo slide control.
for needle in ['Width="22"','Text="D&#x0a;E&#x0a;M&#x0a;O','Cursor="SizeWE"','VerticalAlignment="Center"','DemoTradeSlideButton_PreviewMouseLeftButtonDown','DemoTradeSlideButton_PreviewMouseMove','DemoTradeSlideButton_PreviewMouseLeftButtonUp']:
    check(needle in xaml,f'demo handle {needle}')
for needle in ['SetDemoPanelOpen','_demoSlideStartPanelWidth - deltaX','DemoTradePanel.Visibility = Visibility.Visible','CaptureMouse','ReleaseMouseCapture','DemoTradeSlideButton_PreviewKeyDown']:
    check(needle in demo,f'demo slide behavior {needle}')

# MT5-style market execution and valuation.
for needle,label in [
    ('double entry = direction == "BUY" ? market.Ask : market.Bid','Buy Ask / Sell Bid entry'),
    ('double mark = position.Direction == "BUY" ? market.Bid : market.Ask','Buy Bid / Sell Ask valuation'),
    ('market.Bid <= position.StopLoss','Buy SL uses Bid'),
    ('market.Ask >= position.StopLoss','Sell SL uses Ask'),
    ('market.Bid >= position.TakeProfit','Buy TP uses Bid'),
    ('market.Ask <= position.TakeProfit','Sell TP uses Ask'),
    ('double exit = position.Direction == "BUY" ? market.Bid : market.Ask','manual close correct side'),
    ('Interval = TimeSpan.FromMilliseconds(100)','100ms live refresh timer')]: check(needle in demo,label)

# Market order input/preset behavior.
for needle in ['DemoSlPresetPanel','DemoTpPresetPanel','DemoSelectedSlText','DemoSelectedTpText','DemoOrderPreviewText','BUY MARKET (DEMO)','SELL MARKET (DEMO)','Tag="NONE"']:
    check(needle in xaml,f'order UI {needle}')
for needle in ['int? _demoSlPresetPoints','int? _demoTpPresetPoints','UpdateDemoPresetVisuals','TryResolveDemoLevels','stopLoss = 0','takeProfit = 0']:
    check(needle in demo,f'optional SL/TP {needle}')

# Running trade controls and chart levels.
for needle in ['DemoApplyPositionLevelsButton_Click','DemoRemovePositionSlButton_Click','DemoRemovePositionTpButton_Click','CloseDemoPositionAtMarket','DemoClosePositionButton_Click','DemoCloseSelectedButton_Click']:
    check(needle in demo,f'running trade handler {needle}')
for needle in ['Apply SL/TP','Remove SL','Remove TP','Close selected now','Close all now','CurrentBid','CurrentAsk','LIVE P/L']:
    check(needle in xaml,f'running trade UI {needle}')
for needle in ['position.StopLoss > 0','position.TakeProfit > 0','DemoTradeLineKind.StopLoss','DemoTradeLineKind.TakeProfit','DemoTradeLineMoved']:
    check(needle in demo+trade_overlay+chart_contexts,f'trade level {needle}')
check('IsDraggable' in trade_overlay and 'DemoTradeLineMoved?.Invoke' in trade_overlay,'SL/TP chart drag retained')

# Pending orders.
for order in ['Buy Limit','Sell Limit','Buy Stop','Sell Stop','Buy Stop Limit','Sell Stop Limit']:
    check(order in demo,f'pending type {order}')
for needle in ['DemoPendingOrdersGrid','DemoPendingOrderCountText','DemoPendingEntryBox','DemoStopLimitBox','DemoExpirationModeCombo','DemoExpirationDatePicker','DemoExpirationTimeBox','Place pending order']:
    check(needle in xaml,f'pending UI {needle}')
for needle in ['ProcessDemoPendingOrders','ValidatePendingPlacement','ResolveDemoExpiration','DemoEditPendingButton_Click','DemoCancelPendingButton_Click','IsStopLimitActivated','PendingOrders']:
    check(needle in demo,f'pending engine {needle}')

# Account/persistence/history.
for needle in ['DemoInitialBalance = 1_000_000.0','DemoBalanceText','DemoEquityText','DemoFloatingText','DemoRealizedText','DemoMarginText','SaveDemoTradingState','LoadDemoTradingState','File.Move(temporary, DemoTradingPath, overwrite: true)','DemoTradeHistoryGrid']:
    check(needle in demo+xaml,f'account/history {needle}')
for forbidden in ['OrderSend','MqlTradeRequest','MqlTradeResult','CTrade','trade.Buy','trade.Sell','PositionOpen','OrderSendAsync']:
    check(forbidden.lower() not in demo.lower(),f'no real order API {forbidden}')

# Indicator exact-instance context menus and routing.
pane_stack=read('src/TickLab.App/Controls/IndicatorPaneStackControl.cs')
independent=read('src/TickLab.App/MainWindow.IndependentIndicators.cs')
for needle in ['IndicatorRefreshRequested','IndicatorMoveToWindowRequested','IndicatorMoveToChartRequested']:
    check(needle in chart and needle in chart_contexts,f'chart indicator event {needle}')
for needle in ['HitTestBuiltInIndicatorOverlay','BuildExactIndicatorContextMenu','IndicatorContextAction']:
    check(needle in read('src/TickLab.App/Controls/CandleChartControl.Indicators.cs'),f'direct overlay menu {needle}')
for needle in ['Refresh','Properties…','Move to Window…','Move to Chart…','Remove']:
    check(needle in chart,f'fallback chart indicator menu {needle}')
for needle in ['RefreshIndicatorByKey','MoveIndicatorToWindowByKey','MoveIndicatorToChartByKey','RemoveIndicatorByKey']:
    check(needle in management,f'indicator management {needle}')
check('OpenIndicatorPlacementByKey' not in management,'obsolete Move-to-Window properties redirect removed')
check('EditBuiltInIndicator(context, instance)' in management,'built-in Properties uses exact chart context')
check('RemoveBuiltInIndicator(context, instance)' in management,'built-in Remove uses exact chart context')
for text,label in [(builtin_plot,'built-in plot'),(script_plot,'TickScript plot')]:
    for needle in ['Refresh','Properties…','Move to Window…','Move to Chart…','Remove']:
        check(needle in text,f'{label} exact menu {needle}')
    for needle in ['MoveToWindowRequested','MoveToChartRequested']:
        check(needle in text,f'{label} dedicated event {needle}')
    check('moveWindow.Click += (_, _) => EditRequested?.Invoke();' not in text,f'{label} Move to Window no longer opens Properties')
for needle in ['ConfigureTickScriptPaneContextMenu','ConfigureBuiltInPaneContextMenu','PreviewMouseRightButtonDown','BuildIndicatorContextMenu']:
    check(needle in pane_stack,f'indicator pane/header menu {needle}')
for needle in ['MoveIndicatorToWindowRequested','MoveIndicatorToChartRequested','MoveBuiltInIndicatorToWindowRequested','MoveBuiltInIndicatorToChartRequested']:
    check(needle in pane_stack,f'pane exact event {needle}')
for needle in ['SelectEmptyWorkspacePartition','MoveTickScriptIndicatorToWindow','MoveBuiltInIndicatorToWindow','MoveIndicatorWorkspaceTickScriptToWindow','MoveIndicatorWorkspaceBuiltInToWindow']:
    check(needle in independent,f'window move implementation {needle}')
check('.Where(partition => page.Surface.GetPane(partition) is null)' in independent,'Move to Window lists only empty workspace partitions')
for needle in ['RouteTickScriptIndicator(context, entry, IndicatorRouteAction.Move)','RouteBuiltInIndicator(context, instance, IndicatorRouteAction.Move)']:
    check(needle in chart_contexts,f'chart Move to Chart exact route {needle}')
for needle in ['RouteIndicatorWorkspaceTickScript(context, entry, IndicatorRouteAction.Move)','RouteIndicatorWorkspaceBuiltIn(context, instance, IndicatorRouteAction.Move)']:
    check(needle in independent,f'workspace Move to Chart exact route {needle}')

# Floating chart transfer handle and destination highlighting.
for needle in ['MOVE TO WORKSPACE','Width="132"','PreviewMouseLeftButtonDown="ChartNumberBadge_PreviewMouseLeftButtonDown"']:
    check(needle in detached,f'floating handle {needle}')
for needle in ['new DataObject(WorkspaceSurfaceControl.PaneDragFormat, ChartNumber)','DragDrop.DoDragDrop']:
    check(needle in detached_cs,f'floating drag {needle}')
for needle in ['AllowDrop = true','DragEnter','DragOver','DropFrame.Visibility','PaneDropped']:
    check(needle in surface,f'workspace drop/highlight {needle}')
for needle in ['OpenFloatingPane(pane)','CreatePriceChartPane(paneId)','MOVE TO WORKSPACE handle']:
    check(needle in workspaces,f'new floating chart path {needle}')


# Step 7 demo panel reliability, live pricing, history and modification correction.
prompt=read('src/TickLab.App/Windows/DemoTradeValuePromptWindow.cs')
for needle,label in [
    ('SetDemoPanelWidth(0, updateOpenState: true)','panel starts closed but handle remains'),
    ('DemoTradePanel.Visibility = Visibility.Visible','panel is never collapsed away'),
    ('Math.Clamp(width, 0, maximum)','panel width clamped'),
    ('_demoSlideStartPanelWidth - deltaX','left/right drag changes width continuously'),
    ('SetDemoPanelOpen(DemoTradePanel.Width >= maximum * 0.35)','drag release snaps open/closed'),
    ('SetDemoPanelOpen(!_demoPanelOpen)','handle click toggles panel'),
    ('SizeChanged += (_, _) => ClampDemoPanelToWindow()','window resize reclamps panel'),
    ('DemoTradeSlideButton_LostMouseCapture','lost mouse capture safely snaps panel'),
]: check(needle in demo,label)
for needle,label in [
    ('x:Name="DemoTradeDock"','demo dock exists'),
    ('Panel.ZIndex="5000"','demo handle above chart'),
    ('Width="22"','thin demo handle'),
    ('VerticalAlignment="Center"','demo handle centred'),
    ('D&#x0a;E&#x0a;M&#x0a;O','vertical DEMO text'),
    ('T&#x0a;R&#x0a;A&#x0a;D&#x0a;I&#x0a;N&#x0a;G','vertical TRADING text'),
    ('<Style TargetType="Button">','demo scoped readable button style'),
    ('<Setter Property="Background" Value="White"/>','demo button white background'),
    ('<Setter Property="Foreground" Value="Black"/>','demo button black text'),
    ('<Setter Property="Background" Value="#E3EAF2"/>','demo readable hover state'),
]: check(needle in xaml,label)

# Correct visible-live-candle binding and Bid/Ask P/L fields.
for needle,label in [
    ('context.Chart.Candles.LastOrDefault() ?? context.DisplayCandles.LastOrDefault()','execution reads rendered live candle first'),
    ('double bid = candle.Close','live candle close is Bid'),
    ('double ask = bid + spreadPoints * point','Ask derived from live spread and point'),
    ('OpenBid = RoundPrice(market.Bid','position stores open Bid'),
    ('OpenAsk = RoundPrice(market.Ask','position stores open Ask'),
    ('CurrentBid = RoundPrice(market.Bid','position updates live Bid'),
    ('CurrentAsk = RoundPrice(market.Ask','position updates live Ask'),
    ('TickValuePerLot = market.TickValuePerLot','position stores tick value'),
    ('string.Equals(position.Symbol, exact.Symbol','position refuses a reused pane with another symbol'),
    ('ReferenceEquals(targetContext, context)','active/history levels target one exact chart'),
    ('difference / tickSize * tickValue * Volume','P/L uses ticks, tick value and lot size'),
    ('PointsMoved = point > 0 ? difference / point','points moved calculation'),
]: check(needle in demo,label)
for needle in ['EntryPrice','CurrentBid','CurrentAsk','PointsMoved','Volume','FloatingProfit']:
    check(needle in xaml,f'position card displays {needle}')
for needle in ['DemoClosePositionButton_Click','DemoCloseSelectedButton_Click','DemoCloseAllButton_Click']:
    check(needle in demo and needle in xaml,f'position close control {needle}')

# SL/TP exact menu and drag editing.
for needle,label in [
    ('DemoTradeLineContextRequested','chart trade-line context event'),
    ('TryOpenDemoTradeLineContextMenu','right-click active trade line hit test'),
    ('OpenDemoTradeLineContextMenu','exact position line menu'),
    ('enter exact price…','exact-price modification menu'),
    ('enter points…','points modification menu'),
    ('Remove / cancel {shortName}','SL/TP removal menu'),
    ('PromptAndApplyDemoPositionLevel(position, isStopLoss, usePoints: false)','exact-price modification handler'),
    ('PromptAndApplyDemoPositionLevel(position, isStopLoss, usePoints: true)','points modification handler'),
    ('position.TakeProfit = 0','TP removal handler'),
    ('Close position now','line context manual close'),
    ('DemoTradeValuePromptWindow','numeric modification prompt'),
]: check(needle in demo+trade_overlay+chart_contexts+prompt,label)
check('DemoTradeLineMoved?.Invoke' in trade_overlay and 'MoveDemoTradeLine' in demo,'dragged SL/TP persists to exact position')
check('Background = Brushes.White, Foreground = Brushes.Black' in demo,'SL/TP menu readable white/black')

# Persistent historical chart markings and exact chart routing.
for kind in ['HistoryEntry','HistoryExit','HistoryStopLoss','HistoryTakeProfit']:
    check(kind in trade_overlay and kind in demo,f'history overlay kind {kind}')
for needle,label in [
    ('StartUnix','history overlay start time'),
    ('EndUnix','history overlay end time'),
    ('IncludeInAutoScale','history does not distort live chart scale'),
    ('GetUnixX','history timestamp to chart coordinate'),
    ('HISTORY {trade.Direction}','history entry label'),
    ('EXIT #{trade.Id}','history exit label'),
    ('Historical SL','history SL label'),
    ('Historical TP','history TP label'),
    ('ReferenceEquals(targetContext, context)','history drawn on only one exact matching chart'),
    ('trade.ChartPaneId = targetContext.PaneId','history chart address repaired and persisted'),
]: check(needle in demo+trade_overlay,label)
check('_demoTradeLines.Where(item => item.IncludeInAutoScale)' in chart,'history markings excluded from autoscale')

# History deletion and statistics.
for needle,label in [
    ('DemoDeleteSelectedHistoryButton_Click','delete selected history handler'),
    ('DemoDeleteAllHistoryButton_Click','delete all history handler'),
    ('_demoTradeHistory.Remove(trade)','selected history only removed'),
    ('_demoTradeHistory.Clear()','all completed history removed'),
    ('Open positions and pending orders were kept','history delete preserves live orders'),
    ('DemoHistoryTotalText','closed trade total'),
    ('DemoHistoryWinsText','profitable trade count'),
    ('DemoHistoryLossesText','losing trade count'),
    ('DemoHistoryBreakevenText','breakeven count'),
    ('DemoHistoryWinningTotalText','winning P/L total'),
    ('DemoHistoryLosingTotalText','losing P/L total'),
    ('DemoHistoryNetText','net P/L total'),
]: check(needle in demo+xaml,label)
for needle in ['Delete selected history + markings','Delete ALL demo history + chart markings','Profit trades','Loss trades','Breakeven','Winning total','Losing total','Net P/L']:
    check(needle in xaml,f'history UI {needle}')

# All trading remains simulated.
for forbidden in ['OrderSend','MqlTradeRequest','MqlTradeResult','CTrade','trade.Buy','trade.Sell','PositionOpen','OrderSendAsync']:
    check(forbidden.lower() not in demo.lower(),f'Step 7 no real order API {forbidden}')

# Protected MT5 and FileBridge hashes.
protected=[]
for folder in [Path('MT5'),Path('src/TickLab.App/Gateway/FileBridge')]:
    protected += [p.relative_to(root) for p in (root/folder).rglob('*') if p.is_file()]
check(len(protected)==19,'19 protected files present')
for rel in protected:
    bp=base/rel; np=root/rel
    check(bp.exists() and sha(bp)==sha(np),f'protected unchanged {rel}')

# No build products in release source.
check(not any(p.is_dir() and p.name in {'bin','obj'} for p in root.rglob('*')),'no bin or obj directories')

print(f'STEP7 CHECKS PASSED: {len(passed)}')
print(f'STEP7 CHECKS FAILED: {len(failed)}')
for label in failed: print('FAILED:',label)
if failed: raise SystemExit(1)
