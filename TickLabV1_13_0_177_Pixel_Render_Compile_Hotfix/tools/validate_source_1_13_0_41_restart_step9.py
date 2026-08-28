from pathlib import Path
import hashlib, re, sys, xml.etree.ElementTree as ET
from pygments import lex
from pygments.lexers.dotnet import CSharpLexer
from pygments.token import Token

root=Path(__file__).resolve().parents[1]
base=Path('/mnt/data/work_step9/TickLabV1_13_0_40_Restart_Step8_Trade_Level_Labels_UI_Contrast')
app=root/'src'/'TickLab.App'
passed=[]; failed=[]
def check(cond,label): (passed if cond else failed).append(label)
def read(rel): return (root/rel).read_text(encoding='utf-8-sig',errors='ignore')
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()

proj=read('src/TickLab.App/TickLab.App.csproj')
xaml=read('src/TickLab.App/MainWindow.xaml')
demo=read('src/TickLab.App/MainWindow.DemoTrading.cs')
overlay=read('src/TickLab.App/Controls/CandleChartControl.DemoTrading.cs')
chart=read('src/TickLab.App/Controls/CandleChartControl.cs')
colors=read('src/TickLab.App/Settings/ColorDisplayHelper.cs')
picker=read('src/TickLab.App/Windows/DrawingColorPickerWindow.xaml.cs')

# Release identity.
for needle,label in [('<Version>1.13.0.41</Version>','version'),('<AssemblyVersion>1.13.0.41</AssemblyVersion>','assembly version'),('<FileVersion>1.13.0.41</FileVersion>','file version')]: check(needle in proj,label)
check((root/'TickLabV1_13_0_41.sln').exists(),'solution exists')
check((root/'VERSION.txt').read_text().strip()=='1.13.0.41','VERSION file')
check('TickLabV1_13_0_41.sln' in read('Clean-Restore-Build.cmd'),'clean build script targets Step 9 solution')
check('Restart Step 9 Trade Labels, History and Contrast' in xaml,'window title')

# XML, unique names and C# punctuation.
for p in app.rglob('*.xaml'):
    try: ET.parse(p); check(True,f'XAML parses {p.relative_to(app)}')
    except Exception as e: check(False,f'XAML parses {p.relative_to(app)}: {e}')
    names=re.findall(r'x:Name="([^"]+)"',p.read_text(errors='ignore'))
    check(len(names)==len(set(names)),f'unique x:Name {p.relative_to(app)}')
all_main='\n'.join(p.read_text(errors='ignore') for p in app.glob('MainWindow*.cs'))
handler_pattern=r'\b(?:Click|Checked|Unchecked|TextChanged|SelectionChanged|SelectedDateChanged|PreviewMouse\w+|PreviewKey\w+|Mouse\w+|Key\w+|Drag\w+|Drop|Loaded|Closing)="([A-Za-z_]\w*)"'
for p in app.rglob('*.xaml'):
    handlers=set(re.findall(handler_pattern,p.read_text(errors='ignore')))
    code=all_main if p.name=='MainWindow.xaml' else (p.with_suffix('.xaml.cs').read_text(errors='ignore') if p.with_suffix('.xaml.cs').exists() else '')
    for handler in handlers: check(re.search(r'\b'+re.escape(handler)+r'\s*\(',code) is not None,f'handler {p.name}:{handler}')
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

# Near Black palette.
check('Add(result, "Near Black", "#080808")' in colors,'Near Black named colour')
check('P("Near Black", "#080808")' in picker,'Near Black selectable palette swatch')
check('P("Black", "#000000"), P("Near Black", "#080808"), P("Charcoal"' in picker,'Near Black placed with neutral colours')

# Preset and pending selector contrast.
for needle,label in [
    ('settings:ThemeColorScope.PreserveExactColors="True"','demo panel exact-colour scope'),
    ('Color.FromRgb(23, 63, 112)','selected preset dark-blue background'),
    ('button.Foreground = isSelected ? Brushes.White : Brushes.Black','preset readable foreground'),
    ('button.FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal','selected preset pressed emphasis'),
    ('DemoLightComboBoxItemStyle','pending dropdown item style'),
    ('<Setter Property="Background" Value="White"/>','light selector background'),
    ('<Setter Property="Foreground" Value="Black"/>','dark selector text'),
    ('<Setter Property="TextElement.Foreground" Value="Black"/>','closed pending selector text'),
    ('<Setter Property="Background" Value="#2B5685"/>','selected dropdown state'),
    ('<Setter Property="Foreground" Value="White"/>','selected dropdown text')]: check(needle in xaml+demo,label)
check('x:Name="DemoOrderTypeCombo"' in xaml and 'Style="{StaticResource DemoLightComboBoxStyle}"' in xaml,'pending Type uses readable style')
check('x:Name="DemoExpirationModeCombo"' in xaml and xaml.count('Style="{StaticResource DemoLightComboBoxStyle}"')>=2,'pending Expiration uses readable style')

# Middle slide handle thickness; behavior remains present.
check('<ColumnDefinition Width="24"/>' in xaml,'middle handle column widened')
check('x:Name="RightWorkspaceToggleButton" Width="22" Height="52"' in xaml,'middle handle thicker')
for needle in ['RightWorkspaceHandle_PreviewMouseLeftButtonDown','RightWorkspaceHandle_PreviewMouseMove','RightWorkspaceHandle_PreviewMouseLeftButtonUp','RightWorkspaceHandle_LostMouseCapture','RightWorkspaceHandle_PreviewKeyDown']:
    check(needle in xaml and needle in read('src/TickLab.App/MainWindow.xaml.cs'),f'middle handle behavior {needle}')

# Compact active trade labels and separate price-scale tickets.
check('$"{position.Direction} {position.Volume:0.00} lot · {FormatDemoUsdAmount(position.FloatingProfit)}"' in demo,'entry label contains side lot and running P/L only')
check('$"SL · {FormatDemoUsdAmount' in demo,'compact SL projected USD label')
check('$"TP · {FormatDemoUsdAmount' in demo,'compact TP projected USD label')
check('DrawDemoPriceScaleTicket' in overlay,'active price-scale ticket renderer')
check('new Rect(layout.Plot.Right + 2' in overlay,'trade price placed on price scale')
check('CreateText($"{line.Label}{dragHint}"' in overlay,'plot ticket excludes duplicate exact price')
check('Math.Max(54, text.Width + 12)' in overlay and 'layout.Plot.Width * 0.42' in overlay,'trade labels narrowed')

# MT5-style history path and hover information.
for needle,label in [
    ('DrawDemoTradeHistoryPaths','history point-to-point renderer'),
    ('drawingContext.DrawLine(pen, start, end)','entry-to-exit path'),
    ('drawingContext.DrawEllipse','entry and exit markers'),
    ('UpdateDemoTradeHistoryHover','history hover detector'),
    ('DistanceToDemoHistorySegment','history path hit test'),
    ('Placement = PlacementMode.Mouse','tooltip beside pointer'),
    ('Background = Brushes.White','tooltip readable background'),
    ('Foreground = Brushes.Black','tooltip readable text'),
    ('Realized P/L:','tooltip realized P/L'),
    ('Close reason:','tooltip close reason'),
    ('Historical SL:','tooltip historical SL'),
    ('Historical TP:','tooltip historical TP'),
    ('Entry: {trade.EntryPrice:G10}','tooltip entry price/time'),
    ('Exit: {trade.ExitPrice:G10}','tooltip exit price/time'),
    ('CloseDemoTradeHistoryToolTip','tooltip cleanup')]: check(needle in demo+overlay+chart,label)
check('if (line.Kind is DemoTradeLineKind.HistoryEntry or DemoTradeLineKind.HistoryExit)\n                continue;' in overlay,'old horizontal entry/exit tickets suppressed')
check('RefreshDemoTradeLines();' in demo and 'LoadDemoTradingState();' in demo,'history remains restored from persisted demo state')

# Working execution rules retained.
for needle,label in [
    ('double entry = direction == "BUY" ? market.Ask : market.Bid','Buy Ask / Sell Bid entry retained'),
    ('double mark = position.Direction == "BUY" ? market.Bid : market.Ask','live mark side retained'),
    ('market.Bid <= position.StopLoss','Buy SL Bid retained'),
    ('market.Ask >= position.StopLoss','Sell SL Ask retained'),
    ('market.Bid >= position.TakeProfit','Buy TP Bid retained'),
    ('market.Ask <= position.TakeProfit','Sell TP Ask retained')]: check(needle in demo,label)
for forbidden in ['OrderSend','MqlTradeRequest','MqlTradeResult','CTrade','trade.Buy','trade.Sell','PositionOpen','OrderSendAsync']:
    check(forbidden.lower() not in demo.lower(),f'no real order API {forbidden}')

# No duplicate DistanceToSegment signature introduced.
all_chart='\n'.join(p.read_text(errors='ignore') for p in (app/'Controls').glob('CandleChartControl*.cs'))
check(all_chart.count('private static double DistanceToDemoHistorySegment(')==1,'unique demo history distance helper')

# Protected MT5 / FileBridge exact hashes.
protected=[p for p in base.rglob('*') if p.is_file() and (p.relative_to(base).as_posix().startswith('MT5/') or p.relative_to(base).as_posix().startswith('src/TickLab.App/Gateway/FileBridge/'))]
for bp in protected:
    rel=bp.relative_to(base); wp=root/rel
    check(wp.exists() and sha(bp)==sha(wp),f'protected unchanged {rel.as_posix()}')

report=root/'VALIDATION_REPORT_1_13_0_41_RESTART_STEP9.txt'
report.write_text('TickLab v1.13.0.41 Restart Step 9 static validation\n\n'+f'Passed: {len(passed)}\nFailed: {len(failed)}\n\n'+'\n'.join(('PASS  '+x) for x in passed)+('\n\n'+'\n'.join(('FAIL  '+x) for x in failed) if failed else '')+'\n')
print(f'passed={len(passed)} failed={len(failed)}')
for item in failed: print('FAIL',item)
sys.exit(1 if failed else 0)
