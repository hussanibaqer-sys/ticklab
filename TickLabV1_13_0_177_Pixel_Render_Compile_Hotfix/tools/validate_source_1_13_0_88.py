from pathlib import Path
import re, sys, xml.etree.ElementTree as ET
from pygments import lex
from pygments.lexers.dotnet import CSharpLexer
from pygments.token import Token

root=Path(__file__).resolve().parents[1]
app=root/'src'/'TickLab.App'
base=Path('/mnt/data/ticklab_work/v87/TickLabV1_13_0_87_Folder1_Interaction_Parity')
passed=[]; failed=[]
def check(c,l): (passed if c else failed).append(l)
def read(p): return p.read_text(encoding='utf-8-sig',errors='ignore')

proj=read(app/'TickLab.App.csproj')
for n in ['<Version>1.13.0.88</Version>','<AssemblyVersion>1.13.0.88</AssemblyVersion>','<FileVersion>1.13.0.88</FileVersion>']:
    check(n in proj,n)
check((root/'TickLabV1_13_0_88.sln').exists(),'v88 solution exists')
check(not (root/'TickLabV1_13_0_87.sln').exists(),'old v87 solution name removed')
check(read(root/'VERSION.txt').strip()=='1.13.0.88','VERSION.txt')
check('TickLabV1_13_0_88.sln' in read(root/'Clean-Restore-Build.cmd'),'build command targets v88')
check('TickLab v1.13.0.88 — Pitchfork / Settings Stability' in read(app/'MainWindow.xaml'),'window title v88')

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

main=read(app/'MainWindow.xaml.cs')
drawing=read(app/'Controls/CandleChartControl.Drawing.cs')
parity=read(app/'Controls/CandleChartControl.DrawingParity.cs')
defaults=read(app/'Core/Drawing/DrawingParityDefaults.cs')
settings=read(app/'Windows/TradingViewLineSettingsWindow.xaml')
settings_code=read(app/'Windows/TradingViewLineSettingsWindow.xaml.cs')
generic_settings=read(app/'Windows/DrawingSettingsWindow.xaml.cs')
open_settings=main[main.index('private void OpenDrawingSettings'):main.index('private void OpenDrawingObjectTree')]

# v88 guarantees
markers=[
    ('tool.Id == "disjoint-channel"' in drawing and 'Price = _workingDrawing.Anchors[1].Price' in drawing,'disjoint point3 horizontal-only construction constraint'),
    ('Vector opposite = new(first.X, -first.Y)' in parity,'disjoint mirrored opposite slope'),
    ('dc.DrawLine(new Pen(CreateDrawingBrush("#F23645"' in parity,'pitchfork A-B red helper retained'),
    ('if (drawing.ToolId == "inside-pitchfork")' in parity and 'origin = Mid(a, b);' in parity and 'direction = c - origin;' in parity and 'levelCenter = Mid(b, c);' in parity and 'halfWidth = (b - c) * 0.5;' in parity,'inside pitchfork reference construction formula'),
    ('dc.DrawLine(new Pen(CreateDrawingBrush("#F23645", drawing.Style.Opacity), Math.Max(1.0, pen.Thickness)), origin, c);' in parity,'inside O-C construction leg retained'),
    ('DrawRay(dc, plot, levelCenter, levelCenter + direction, pen, false);' in parity,'inside median starts at B-C midpoint parallel to O-C'),
    ('new DrawingLevel(0.25' in defaults and 'new DrawingLevel(2.0' in defaults,'pitchfork full 0.25 through 2 level stack'),
    ('#FF9800' in defaults and '#4CAF50' in defaults and '#089981' in defaults and '#26A69A' in defaults and '#26C6DA' in defaults and '#2962FF' in defaults and '#7E57C2' in defaults and '#AB47BC' in defaults and '#EC407A' in defaults,'TradingView pitchfork per-level palette'),
    ('LineColor = pitchfork ? "#F23645"' in catalog,'pitchfork median red default'),
    ('PitchforkUseOneColorBox' in settings and 'Use one color' in settings,'pitchfork Use one color control'),
    ('PitchforkLevelColorButton_Click' in settings and 'PitchforkLevelColorButton_Click' in settings_code,'pitchfork clickable level swatches'),
    ('Content="Extend lines"' in settings,'pitchfork Extend lines label'),
    ('numeric["UseOneColor"]' in settings_code and 'ParityFlag(drawing, "UseOneColor", false)' in parity,'Use one color live render path'),
    ('_activeDrawingSettingsWindow' in main and 'lineWindow.Show();' in open_settings and 'window.Show();' in open_settings,'drawing settings modeless windows'),
    ('lineWindow.ShowDialog' not in open_settings and 'window.ShowDialog' not in open_settings,'drawing settings no owner-blocking ShowDialog'),
    ('public bool WasAccepted' in settings_code and 'public bool WasAccepted' in generic_settings,'modeless settings accepted-state contract'),
    ('WasAccepted = true;' in settings_code and 'WasAccepted = true;' in generic_settings,'OK/Apply acceptance'),
    ('WasAccepted = false;' in settings_code and 'WasAccepted = false;' in generic_settings,'Cancel restores original contract'),
    ('targetChart.PreviewDrawing(original);' in open_settings,'modeless settings restore original before cancel/apply'),
]
for c,l in markers: check(c,l)

# Source scope: only intended app files differ from v87.
expected={
'Controls/CandleChartControl.Drawing.cs','Controls/CandleChartControl.DrawingParity.cs',
'Core/Drawing/DrawingParityDefaults.cs','Core/Drawing/DrawingToolCatalog.cs','MainWindow.xaml','MainWindow.xaml.cs','TickLab.App.csproj',
'Windows/DrawingSettingsWindow.xaml.cs','Windows/TradingViewLineSettingsWindow.xaml','Windows/TradingViewLineSettingsWindow.xaml.cs'}
actual=set(); baseapp=base/'src'/'TickLab.App'
for p in app.rglob('*'):
    if p.is_file():
        rel=p.relative_to(app); q=baseapp/rel
        if not q.exists() or p.read_bytes()!=q.read_bytes(): actual.add(str(rel))
for q in baseapp.rglob('*'):
    if q.is_file():
        rel=q.relative_to(baseapp)
        if not (app/rel).exists(): actual.add(str(rel))
check(actual==expected,f'exact intended app source diff ({len(actual)} files)')
if actual!=expected: failed.append('diff actual='+repr(sorted(actual))+' expected='+repr(sorted(expected)))

# Guard protected systems: v88 app diff must not touch bridge/history/replay implementation files.
protected_tokens=('Bridge','History','Replay','Mt5','MT5','Gateway','Connector')
changed_protected=[x for x in actual if any(tok in x for tok in protected_tokens)]
check(not changed_protected,'no bridge/history/replay/MT5 source touched')

print(f'passed={len(passed)} failed={len(failed)}')
for f in failed: print('FAIL',f)
if failed: sys.exit(1)
