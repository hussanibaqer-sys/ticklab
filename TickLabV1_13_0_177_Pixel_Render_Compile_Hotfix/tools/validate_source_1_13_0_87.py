from pathlib import Path
import re, sys, xml.etree.ElementTree as ET
from pygments import lex
from pygments.lexers.dotnet import CSharpLexer
from pygments.token import Token

root=Path(__file__).resolve().parents[1]
app=root/'src'/'TickLab.App'
base=Path('/mnt/data/ticklab_work/v86/TickLabV1_13_0_86_Drawing_Selection_Construction_Background_Fix')
passed=[]; failed=[]
def check(c,l): (passed if c else failed).append(l)
def read(p): return p.read_text(encoding='utf-8-sig',errors='ignore')

proj=read(app/'TickLab.App.csproj')
for n in ['<Version>1.13.0.87</Version>','<AssemblyVersion>1.13.0.87</AssemblyVersion>','<FileVersion>1.13.0.87</FileVersion>']:
    check(n in proj,n)
check((root/'TickLabV1_13_0_87.sln').exists(),'v87 solution exists')
check(read(root/'VERSION.txt').strip()=='1.13.0.87','VERSION.txt')
check('TickLabV1_13_0_87.sln' in read(root/'Clean-Restore-Build.cmd'),'build command targets v87')
check('TickLab v1.13.0.87 — Folder 1 Interaction Parity' in read(app/'MainWindow.xaml'),'window title v87')

# XAML + unique names + event handlers
all_main='\n'.join(read(p) for p in app.glob('MainWindow*.cs'))
handler_pattern=r'\b(?:Click|Checked|Unchecked|TextChanged|SelectionChanged|SelectedDateChanged|PreviewMouse\w+|PreviewKey\w+|Mouse\w+|Key\w+|Drag\w+|Drop|Loaded|Closing|ValueChanged)="([A-Za-z_]\w*)"'
for x in app.rglob('*.xaml'):
    try: ET.parse(x); check(True,f'XAML parses {x.relative_to(app)}')
    except Exception as e: check(False,f'XAML parse {x.relative_to(app)}: {e}')
    t=read(x); names=re.findall(r'x:Name="([^"]+)"',t)
    check(len(names)==len(set(names)),f'unique x:Name {x.relative_to(app)}')
    code=all_main if x.name=='MainWindow.xaml' else (read(x.with_suffix('.xaml.cs')) if x.with_suffix('.xaml.cs').exists() else '')
    for h in set(re.findall(handler_pattern,t)):
        check(re.search(r'\b'+re.escape(h)+r'\s*\(',code) is not None,f'handler {x.name}:{h}')

# C# lexical punctuation balance
for p in app.rglob('*.cs'):
    stack=[]; ok=True; pairs={')':'(',']':'[','}':'{'}
    for typ,val in lex(read(p),CSharpLexer()):
        if typ in Token.Punctuation:
            for ch in val:
                if ch in '([{': stack.append(ch)
                elif ch in ')]}':
                    if not stack or stack[-1]!=pairs[ch]: ok=False; break
                    stack.pop()
        if not ok: break
    check(ok and not stack,f'C# structure {p.relative_to(app)}')

catalog=read(app/'Core/Drawing/DrawingToolCatalog.cs')
ids=re.findall(r'Add\("([^"]+)"',catalog)
check(len(ids)==len(set(ids)),f'drawing tool IDs unique ({len(ids)})')

# New parity guarantees
chart=read(app/'Controls/CandleChartControl.cs')
drawing=read(app/'Controls/CandleChartControl.Drawing.cs')
parity=read(app/'Controls/CandleChartControl.DrawingParity.cs')
defaults=read(app/'Core/Drawing/DrawingParityDefaults.cs')
settings=read(app/'Windows/TradingViewLineSettingsWindow.xaml')
settings_code=read(app/'Windows/TradingViewLineSettingsWindow.xaml.cs')
for c,l in [
    ('drawingObjectUnderPointer' in chart,'existing drawing wins left-click reselection'),
    ('priorityDrawingContextLayout' in chart,'existing drawing wins right-click context'),
    ('DrawingPointerInputHasPriority' in chart and 'DrawingPointerInputHasPriority' in drawing,'armed tool owns first construction click'),
    ('NormalizeConstructionAnchor' in drawing and 'parallel-channel' in drawing,'parallel point3 price-offset normalization'),
    ('committedFill = Brushes.White' in drawing,'hollow construction anchors'),
    ('TryGetParityRegressionGeometry' in parity and 'TryGetParityRegressionGeometry' in drawing,'regression render/hit geometry shared'),
    ('Vector offset = new(0, points[2].Y - b.Y);' in parity,'parallel channel ignores point3 time'),
    ('DrawingParityDefaults.LevelsForTool(drawing.ToolId)' in parity,'channel/pitchfork level rendering'),
    ('"schiff-pitchfork" => new Point(a.X, (a.Y + b.Y) / 2.0)' in parity,'Schiff pitchfork variant geometry'),
    ('"modified-schiff-pitchfork" => Mid(a, b)' in parity,'Modified Schiff variant geometry'),
    ('ChannelLevelsGrid' in settings and 'ChannelLevelsGrid.ItemsSource = _levels' in settings_code,'channel levels settings'),
    ('PitchforkStyleBox' in settings and 'modified-schiff-pitchfork' in settings_code,'pitchfork style switch'),
    ('#2962FF' in defaults and '#2962FF' in catalog,'Folder1 reference blue defaults'),
]: check(c,l)

# Source scope: only intended app files differ from v86.
expected={
'Controls/CandleChartControl.Drawing.cs','Controls/CandleChartControl.DrawingParity.cs','Controls/CandleChartControl.cs',
'Core/Drawing/DrawingParityDefaults.cs','Core/Drawing/DrawingToolCatalog.cs','MainWindow.xaml','TickLab.App.csproj',
'Windows/TradingViewLineSettingsWindow.xaml','Windows/TradingViewLineSettingsWindow.xaml.cs'}
actual=set()
baseapp=base/'src'/'TickLab.App'
for p in app.rglob('*'):
    if p.is_file():
        rel=p.relative_to(app)
        q=baseapp/rel
        if not q.exists() or p.read_bytes()!=q.read_bytes(): actual.add(str(rel))
for q in baseapp.rglob('*'):
    if q.is_file():
        rel=q.relative_to(baseapp)
        if not (app/rel).exists(): actual.add(str(rel))
check(actual==expected,f'exact intended source diff ({len(actual)} files)')
if actual!=expected:
    failed.append('diff actual='+repr(sorted(actual))+' expected='+repr(sorted(expected)))

print(f'passed={len(passed)} failed={len(failed)}')
for f in failed: print('FAIL',f)
if failed: sys.exit(1)
