from pathlib import Path
import hashlib, re, sys, xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
passed=0; failed=[]
def check(name, condition):
 global passed
 if condition: passed+=1
 else: failed.append(name)
def text(rel): return (root/rel).read_text(encoding='utf-8')
app='src/TickLab.App/'
proj=text(app+'TickLab.App.csproj'); xaml=text(app+'MainWindow.xaml'); demo=text(app+'MainWindow.DemoTrading.cs')
work=text(app+'MainWindow.Workspaces.cs'); surface=text(app+'Controls/WorkspaceSurfaceControl.cs')
symbol=text(app+'Windows/SymbolPickerWindow.xaml'); main=text(app+'MainWindow.xaml.cs')
for name,needle in [
 ('version','<Version>1.13.0.34</Version>'),('assembly','<AssemblyVersion>1.13.0.34</AssemblyVersion>'),('file','<FileVersion>1.13.0.34</FileVersion>')]: check(name,needle in proj)
check('solution',(root/'TickLabV1_13_0_34.sln').exists())
check('title','TickLab v1.13.0.34 — Restart Step 4 Demo Trading' in xaml)
# workspace menu
for needle in ['EmptyPartitionContextRequested','ShowEmptyPartitionAddMenu','AddChartToExactPartitionAsync','Header = "Add"','Header = "Chart"','Header = "Indicator"']:
 check('workspace '+needle, needle in surface+work)
check('exact partition chart', 'page.Surface.AttachPane(partitionId, pane)' in work)
check('indicator existing flow','IndicatorsButton_Click(this, new RoutedEventArgs())' in work)
check('symbol selection helper','ShowSymbolPickerForSelectionAsync' in main and 'ShowSymbolPickerForSelectionAsync' in work)
# scrollbar
for needle in ['ScrollViewer.VerticalScrollBarVisibility="Visible"','Width" Value="17"','PART_Track','Orientation="Vertical"','ScrollBar.LineUpCommand','ScrollBar.LineDownCommand','Background="#666666"']:
 check('scroll '+needle,needle in symbol)
# demo UI and engine
for needle in ['DEMO / FAKE TRADING','Simulation only','DemoTradePanel','Reset to $1,000,000','BUY (DEMO)','SELL (DEMO)','Breakeven +10','Open positions','Trade history']:
 check('demo xaml '+needle,needle in xaml)
for needle in ['DemoInitialBalance = 1_000_000.0','OpenDemoPosition','CloseDemoPosition','Reset Demo Account','_demoOpenPositions','_demoTradeHistory','SaveDemoTradingState','LoadDemoTradingState','ResolveDemoContractSize','Stop loss','Take profit','Manual close','DemoManualSlBox','DemoManualTpBox']:
 check('demo code '+needle,needle in demo+xaml)
for forbidden in ['OrderSend','MqlTradeRequest','CTrade','PositionOpen','trade.Buy','trade.Sell','WebRequest']:
 check('no real order '+forbidden,forbidden.lower() not in demo.lower())
check('persistence local app data','Environment.SpecialFolder.LocalApplicationData' in demo)
check('atomic state save','File.Move(temporary, DemoTradingPath, overwrite: true)' in demo)
check('mq5 reference',(root/'reference/ScalpTradePanel.mq5').exists())
# init/shutdown hooks
check('demo init hook','InitializeDemoTrading();' in main)
check('demo shutdown hook','ShutdownDemoTrading();' in main)
# XAML and project parsing
for p in list(root.rglob('*.xaml'))+list(root.rglob('*.csproj')):
 try: ET.parse(p); check('xml '+str(p.relative_to(root)),True)
 except Exception: check('xml '+str(p.relative_to(root)),False)
# xaml handler existence
cs='\n'.join(p.read_text(encoding='utf-8',errors='ignore') for p in root.rglob('*.cs'))
attrs=['Click','Loaded','Closing','PreviewKeyDown','PreviewMouseLeftButtonDown','MouseLeftButtonUp','MouseDoubleClick','SelectionChanged','TextChanged','KeyDown','MouseRightButtonDown','MouseRightButtonUp','Checked','Unchecked','ValueChanged']
for xp in root.rglob('*.xaml'):
 value=xp.read_text(encoding='utf-8')
 for attr in attrs:
  for handler in re.findall(rf'\b{attr}="([A-Za-z_][A-Za-z0-9_]*)"',value):
   check('handler '+handler, bool(re.search(rf'\b{re.escape(handler)}\s*\(',cs)))
# delimiter validation
from pygments import lex
from pygments.lexers.dotnet import CSharpLexer
from pygments.token import Token
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
print(f'V1.13.0.34 RESTART STEP 4 CHECKS PASSED: {passed}')
print(f'V1.13.0.34 RESTART STEP 4 CHECKS FAILED: {len(failed)}')
for item in failed: print('FAIL:',item)
sys.exit(1 if failed else 0)
