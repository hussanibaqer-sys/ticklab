from pathlib import Path
import hashlib, re, sys, xml.etree.ElementTree as ET
from pygments import lex
from pygments.lexers.dotnet import CSharpLexer
from pygments.token import Token

root=Path(__file__).resolve().parents[1]
base=Path('/mnt/data/ticklab_step5_fix/TickLabV1_13_0_35_Restart_Step5_Demo_Trade_Levels_Chart_Actions')
passed=0; failed=[]
def check(name, condition):
    global passed
    if condition: passed += 1
    else: failed.append(name)
def text(rel): return (root/rel).read_text(encoding='utf-8')
app='src/TickLab.App/'
proj=text(app+'TickLab.App.csproj')
xaml=text(app+'MainWindow.xaml')
demo=text(app+'MainWindow.DemoTrading.cs')
trade=text(app+'Controls/CandleChartControl.DemoTrading.cs')
independent=text(app+'MainWindow.IndependentIndicators.cs')
management=text(app+'MainWindow.IndicatorManagement.cs')

for name,needle in [
 ('version','<Version>1.13.0.36</Version>'),
 ('assembly','<AssemblyVersion>1.13.0.36</AssemblyVersion>'),
 ('file','<FileVersion>1.13.0.36</FileVersion>')]: check(name,needle in proj)
check('solution',(root/'TickLabV1_13_0_36.sln').exists())
check('title','TickLab v1.13.0.36 — Restart Step 5A Compile Hotfix' in xaml)
check('version file',(root/'VERSION.txt').read_text().strip()=='1.13.0.36')

# Exact reported diagnostic fixes.
check('NotNullWhen import','using System.Diagnostics.CodeAnalysis;' in independent)
check('NotNullWhen source contract','[NotNullWhen(true)] out ChartRuntimeContext? source' in independent)
check('all source call sites retained',independent.count('TryGetIndicatorWorkspaceSource(context, out ChartRuntimeContext? source)') >= 5)
check('two explicit result guards',management.count('result is not null &&') == 2)
check('no unsafe result overlay form','&& result.Overlay;' not in management)
check('active market initialized','private bool TryGetActiveDemoMarket' in demo and 'market = default;\n        return TryGetDemoMarket(ActiveChartContext, out market);' in demo)
check('position market initialized',re.search(r'private bool TryGetDemoMarket\(DemoPosition position, out DemoMarketSnapshot market\)\s*\{\s*market = default;',demo) is not None)
check('exact context guard','exact is not null &&\n            TryGetDemoMarket(exact, out market)' in demo)
check('symbol market initialized',re.search(r'private bool TryGetDemoMarket\(string symbol, string timeframe, out DemoMarketSnapshot market\)\s*\{\s*market = default;',demo) is not None)
check('no short circuit out return','return context is not null && TryGetDemoMarket(context, out market);' not in demo)
check('explicit context false path','if (context is null)\n            return false;\n        return TryGetDemoMarket(context, out market);' in demo)
check('drag id local snapshot','string? pendingLineId = _draggingDemoTradeLineId;' in trade and 'string lineId = pendingLineId;' in trade)
check('unsafe field conversion removed','string lineId = _draggingDemoTradeLineId;' not in trade)

# Step 5 behaviour retained.
for needle in ['Header = "Add Indicator…"','IndicatorAddRequested','DrawDemoTradeLines','BeginDemoTradeLineDrag','CompleteDemoTradeLineDrag']:
    check('step5 chart '+needle,needle in text(app+'Controls/CandleChartControl.cs')+trade)
for needle in ['DemoClosePositionButton_Click','DemoFloatingText.Text','RefreshDemoTradeLines','MoveDemoTradeLine','SaveDemoTradingState();']:
    check('step5 demo '+needle,needle in demo)
for needle in ['DEMO / FAKE TRADING','ListBox x:Name="DemoOpenPositionsGrid"','x:Name="DemoTradeSlideButton"']:
    check('step5 xaml '+needle,needle in xaml)
check('floating drag handle','⠿  DRAG' in text(app+'Windows/DetachedChartWindow.xaml'))
check('symbol scroll retained','ScrollViewer.VerticalScrollBarVisibility="Visible"' in text(app+'Windows/SymbolPickerWindow.xaml'))

# Strict demo safety.
for forbidden in ['OrderSend','MqlTradeRequest','CTrade','PositionOpen','trade.Buy','trade.Sell','WebRequest']:
    check('no real order '+forbidden,forbidden.lower() not in demo.lower())

# XML/XAML/project parsing.
for p in list(root.rglob('*.xaml'))+list(root.rglob('*.csproj')):
    try: ET.parse(p); check('xml '+str(p.relative_to(root)),True)
    except Exception: check('xml '+str(p.relative_to(root)),False)

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

# Only intended source files may differ from Step 5.
expected={
 app+'Controls/CandleChartControl.DemoTrading.cs',
 app+'MainWindow.DemoTrading.cs',
 app+'MainWindow.IndependentIndicators.cs',
 app+'MainWindow.IndicatorManagement.cs',
 app+'MainWindow.xaml',
 app+'TickLab.App.csproj',
}
base_files={p.relative_to(base).as_posix():p for p in (base/'src').rglob('*') if p.is_file()}
new_files={p.relative_to(root).as_posix():p for p in (root/'src').rglob('*') if p.is_file()}
changed=set()
for rel in sorted(set(base_files)|set(new_files)):
    bp=base_files.get(rel); np=new_files.get(rel)
    if bp is None or np is None or hashlib.sha256(bp.read_bytes()).digest()!=hashlib.sha256(np.read_bytes()).digest():
        changed.add(rel)
check('only intended hotfix source changes',changed==expected)
if changed!=expected:
    print('Changed source files:',*sorted(changed),sep='\n  ')

# Protected bridge/history/market-data source remains byte-for-byte identical.
protected=0
for rel,bp in base_files.items():
    low=rel.lower()
    if ('filebridge' in low or '/gateway/' in low or '/core/history/' in low or rel.startswith('MT5/') or rel.startswith('MQL5/')):
        np=root/rel
        protected += 1
        check('protected '+rel,np.exists() and hashlib.sha256(bp.read_bytes()).digest()==hashlib.sha256(np.read_bytes()).digest())
check('protected files found',protected>0)

# No build artefacts packaged.
check('no bin obj vs',not any(p.name.lower() in {'bin','obj','.vs'} for p in root.rglob('*') if p.is_dir()))

print(f'V1.13.0.36 RESTART STEP 5A CHECKS PASSED: {passed}')
print(f'V1.13.0.36 RESTART STEP 5A CHECKS FAILED: {len(failed)}')
for item in failed: print('FAIL:',item)
sys.exit(1 if failed else 0)
