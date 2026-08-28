from pathlib import Path
import re, sys, xml.etree.ElementTree as ET
from pygments import lex
from pygments.lexers.dotnet import CSharpLexer
from pygments.token import Token

root=Path(__file__).resolve().parents[1]
passed=0; failed=[]
def check(name, condition):
 global passed
 if condition: passed+=1
 else: failed.append(name)
def text(rel): return (root/rel).read_text(encoding='utf-8')
app='src/TickLab.App/'
proj=text(app+'TickLab.App.csproj')
xaml=text(app+'MainWindow.xaml')
demo=text(app+'MainWindow.DemoTrading.cs')
chart=text(app+'Controls/CandleChartControl.cs')
trade_overlay=text(app+'Controls/CandleChartControl.DemoTrading.cs')
contexts=text(app+'MainWindow.ChartContexts.cs')
management=text(app+'MainWindow.IndicatorManagement.cs')
indwin=text(app+'Windows/IndicatorsWindow.xaml.cs')
detached=text(app+'Windows/DetachedChartWindow.xaml')
work=text(app+'MainWindow.Workspaces.cs')
symbol=text(app+'Windows/SymbolPickerWindow.xaml')
main=text(app+'MainWindow.xaml.cs')

for name,needle in [
 ('version','<Version>1.13.0.35</Version>'),('assembly','<AssemblyVersion>1.13.0.35</AssemblyVersion>'),('file','<FileVersion>1.13.0.35</FileVersion>')]: check(name,needle in proj)
check('solution',(root/'TickLabV1_13_0_35.sln').exists())
check('title','TickLab v1.13.0.35 — Restart Step 5 Demo Trading Levels' in xaml)
check('version file',(root/'VERSION.txt').read_text().strip()=='1.13.0.35')

# Prior Step 4 requirements remain.
for needle in ['ShowEmptyPartitionAddMenu','AddChartToExactPartitionAsync','Header = "Add"','Header = "Chart"','Header = "Indicator"']:
 check('workspace retained '+needle,needle in work)
for needle in ['ScrollViewer.VerticalScrollBarVisibility="Visible"','PART_Track','Orientation="Vertical"']:
 check('symbol scroll retained '+needle,needle in symbol)
for needle in ['DEMO / FAKE TRADING','Simulation only','Reset to $1,000,000','BUY (DEMO)','SELL (DEMO)','Breakeven +10','Trade history']:
 check('demo retained '+needle,needle in xaml)

# Price-chart Add Indicator.
check('chart add indicator menu','Header = "Add Indicator…"' in chart)
check('chart add indicator event','IndicatorAddRequested' in chart and 'IndicatorAddRequested +=' in contexts)
check('exact chart activation','OpenIndicatorManager(chart, showApplied: false)' in contexts and 'ActivateChartControl(chart)' in management)
check('indicator library tab','ShowLibraryTab' in indwin and 'IndicatorTabs.SelectedIndex = 0' in indwin and 'ShowLibraryTab();' in management)

# Centred trade slide button.
check('slide centre','x:Name="DemoTradeSlideButton"' in xaml and 'VerticalAlignment="Center"' in xaml)
check('slide no top margin','x:Name="DemoTradeSlideButton"' in xaml and 'Margin="0"' in xaml)

# Open positions and live P/L.
for needle in ['DemoFloatingText','DemoOpenPositionCountText','LIVE P/L','DemoClosePositionButton_Click','Close only this demo position','ListBox x:Name="DemoOpenPositionsGrid"']:
 check('position ui '+needle,needle in xaml+demo)
check('live price update','position.CurrentPrice = RoundPrice(mark, position.Digits)' in demo)
check('live profit update','position.RecalculateProfit();' in demo and 'FloatingProfit' in demo)
check('floating account total','DemoFloatingText.Text = floating.ToString' in demo)
check('position x exact close','Tag: DemoPosition position' in demo and 'CloseDemoPosition(position, position.CurrentPrice, "Position × close")' in demo)

# Chart trade levels.
for needle in ['DemoTradeLineKind','DemoTradeLineOverlay','DrawDemoTradeLines','HitTestDemoTradeLine','BeginDemoTradeLineDrag','CompleteDemoTradeLineDrag','DemoTradeLineMoved']:
 check('trade overlay '+needle,needle in trade_overlay+chart+contexts)
check('entry locked','DemoTradeLineKind.Entry' in demo and 'false,\n                    isBuy' in demo)
check('sl draggable','DemoTradeLineKind.StopLoss' in demo and '$"SL #{position.Id}"' in demo)
check('tp draggable','DemoTradeLineKind.TakeProfit' in demo and '$"TP #{position.Id}"' in demo)
check('line update exact position','position.ChartPaneId != context.PaneId' in demo and 'item.Id == positionId' in demo)
check('safe sl validation','price < market.Bid' in demo and 'price > market.Ask' in demo)
check('safe tp validation','price > market.Bid' in demo and 'price < market.Ask' in demo)
check('levels persisted','MoveDemoTradeLine' in demo and 'SaveDemoTradingState();' in demo)
check('levels refreshed after open close reset','RefreshDemoTradeLines();' in demo)
check('levels auto fit','foreach (DemoTradeLineOverlay tradeLine in _demoTradeLines)' in chart)
_line_drag = chart.find('BeginDemoTradeLineDrag(layout, mouse)')
_next_chart_drag = chart.find('_dragMode =', _line_drag)
check('line interaction before chart drag', _line_drag >= 0 and _next_chart_drag > _line_drag)

# Floating chart drag handle.
for needle in ['x:Name="ChartDragGrip"','Width="66"','⠿  DRAG','Cursor="SizeAll"','ChartNumberBadge_PreviewMouseMove']:
 check('drag handle '+needle,needle in detached)

# Strict demo safety.
for forbidden in ['OrderSend','MqlTradeRequest','CTrade','PositionOpen','trade.Buy','trade.Sell','WebRequest']:
 check('no real order '+forbidden,forbidden.lower() not in demo.lower())
check('local persistence','Environment.SpecialFolder.LocalApplicationData' in demo)
check('atomic state save','File.Move(temporary, DemoTradingPath, overwrite: true)' in demo)
check('mq5 reference',(root/'reference/ScalpTradePanel.mq5').exists())
check('demo init hook','InitializeDemoTrading();' in main)
check('demo shutdown hook','ShutdownDemoTrading();' in main)

# XAML/project parsing.
for p in list(root.rglob('*.xaml'))+list(root.rglob('*.csproj')):
 try: ET.parse(p); check('xml '+str(p.relative_to(root)),True)
 except Exception: check('xml '+str(p.relative_to(root)),False)

# XAML event handlers must exist.
cs='\n'.join(p.read_text(encoding='utf-8',errors='ignore') for p in root.rglob('*.cs'))
attrs=['Click','Loaded','Closing','PreviewKeyDown','PreviewMouseLeftButtonDown','PreviewMouseMove','PreviewMouseLeftButtonUp','MouseLeftButtonUp','MouseDoubleClick','SelectionChanged','TextChanged','KeyDown','MouseRightButtonDown','MouseRightButtonUp','Checked','Unchecked','ValueChanged']
for xp in root.rglob('*.xaml'):
 value=xp.read_text(encoding='utf-8')
 for attr in attrs:
  for handler in re.findall(rf'\b{attr}="([A-Za-z_][A-Za-z0-9_]*)"',value):
   check('handler '+handler, bool(re.search(rf'\b{re.escape(handler)}\s*\(',cs)))

# C# delimiter validation ignoring comments and strings.
for p in root.rglob('*.cs'):
 stack=[]; ok=True
 for typ,val in lex(p.read_text(encoding='utf-8',errors='ignore'),CSharpLexer()):
  if typ in Token.Comment or typ in Token.Literal.String or typ in Token.Literal.Char: continue
  for ch in val:
   if ch in '{[(': stack.append(ch)
   elif ch in '}])':
    if not stack or {'}':'{',']':'[',')':'('}[ch]!=stack.pop(): ok=False; break
  if not ok: break
 if stack: ok=False
 check('delimiters '+str(p.relative_to(root)),ok)

print(f'V1.13.0.35 RESTART STEP 5 CHECKS PASSED: {passed}')
print(f'V1.13.0.35 RESTART STEP 5 CHECKS FAILED: {len(failed)}')
for item in failed: print('FAIL:',item)
sys.exit(1 if failed else 0)
