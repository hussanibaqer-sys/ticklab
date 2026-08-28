from pathlib import Path
import re, sys, xml.etree.ElementTree as ET
from pygments import lex
from pygments.lexers.dotnet import CSharpLexer
from pygments.token import Token

root=Path(__file__).resolve().parents[1]
app=root/'src'/'TickLab.App'
passed=[]; failed=[]
def check(c,l): (passed if c else failed).append(l)
def read(p): return p.read_text(encoding='utf-8-sig',errors='ignore')

proj=read(app/'TickLab.App.csproj')
for n in ['<Version>1.13.0.89</Version>','<AssemblyVersion>1.13.0.89</AssemblyVersion>','<FileVersion>1.13.0.89</FileVersion>']:
    check(n in proj,n)
check((root/'TickLabV1_13_0_89.sln').exists(),'v89 solution exists')
check(not (root/'TickLabV1_13_0_88.sln').exists(),'old v88 solution name removed')
check(read(root/'VERSION.txt').strip()=='1.13.0.89','VERSION.txt')
check('TickLabV1_13_0_89.sln' in read(root/'Clean-Restore-Build.cmd'),'build command targets v89')
check('TickLab v1.13.0.89 — Disjoint Vertical Lock / Fib-Gann Parity' in read(app/'MainWindow.xaml'),'window title v89')

# Every XAML parses, names are unique, and every declared event handler exists.
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

# Lexical punctuation balance for all C# files.
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
fibx=read(app/'Windows/TradingViewFibGannSettingsWindow.xaml')
fibcs=read(app/'Windows/TradingViewFibGannSettingsWindow.xaml.cs')
generic=read(app/'Windows/DrawingSettingsWindow.xaml.cs')
open_settings=main[main.index('private void OpenDrawingSettings'):main.index('private void OpenDrawingObjectTree')]

markers=[
    ('tool.Id == "disjoint-channel"' in drawing and 'StartUnix = _workingDrawing.Anchors[1].StartUnix' in drawing,'disjoint point3 vertical construction lock'),
    ('Point c = new(b.X, points[2].Y);' in parity and 'Vector opposite = new(first.X, -first.Y);' in parity,'disjoint mirrored second rail with vertical point3'),
    ('new Point(points[1].X, points[2].Y)' in drawing,'disjoint display/edit handle vertically constrained'),
    ('category is DrawingToolCategory.TrendLine or DrawingToolCategory.FibonacciGann' in main,'Folder2 uses light reference category button'),
    ('DrawingToolCategory.FibonacciGann ? 292.0' in main,'Folder2 reference flyout width'),
    ('_openDrawingCategory is DrawingToolCategory.TrendLine or DrawingToolCategory.FibonacciGann' in main,'Folder2 reference rows/sections'),
    ('tool?.Category is DrawingToolCategory.TrendLine or DrawingToolCategory.FibonacciGann' in main,'Folder2 white selected-object mini toolbar'),
    ('definition?.Category == DrawingToolCategory.FibonacciGann' in open_settings and 'new TradingViewFibGannSettingsWindow' in open_settings,'Folder2 dedicated settings route'),
    ('fibWindow.Show();' in open_settings and 'fibWindow.ShowDialog' not in open_settings,'Folder2 settings are modeless'),
    ('public bool WasAccepted' in fibcs and 'WasAccepted = true;' in fibcs and 'WasAccepted = false;' in fibcs,'Folder2 settings OK/Cancel state'),
    ('ReverseBox' in fibx and 'UseOneColorBox' in fibx and 'Place as background' in fibx,'Folder2 reference/live style controls'),
    ('LevelLineColorButton_Click' in fibx and 'LevelFillSwatchButton_Click' in fibx and 'ColorPreviewChanged' in fibcs,'Folder2 clickable live per-level colours'),
    ('Transparent → solid' in fibx and 'transparent → solid' in fibx,'Folder2 transparency direction is transparent to solid'),
    ('DrawParityFibRetracement' in parity and 'ParityLevelPen(drawing, level)' in parity,'Fib retracement level renderer'),
    ('DrawParityFibChannel' in parity and 'DrawParityFibTimeZone' in parity and 'DrawParityFibExtension' in parity,'Fib channel/time/extension renderers'),
    ('CreateSemiAnnulusGeometry' in parity and 'Fib Speed Resistance Arcs are half-circle bands' in parity,'Fib speed arcs semicircle band geometry'),
    ('CreateSectorBandGeometry' in parity and 'Fib Wedge is a nested coloured circular sector' in parity,'Fib wedge sector geometry'),
    ('The reference fan has translucent coloured zones' in parity and 'ExtendRayPoint' in parity,'Fib/Gann fan coloured zones'),
    ('DistanceToFibonacciGannDrawing' in drawing and 'DistanceToEllipseStroke' in drawing and 'PointWithinSmallSector' in drawing,'Folder2 visible-geometry reselection'),
    ('GetGannDisplaySecondPoint' in drawing,'Gann square displayed handle/bounds parity'),
    ('if (id is "gann-box" or "gann-square" or "gann-square-fixed")' in defaults,'Gann multicolor level stack'),
    ('#F23645' in defaults and '#FF9800' in defaults and '#FDD835' in defaults and '#089981' in defaults and '#00BCD4' in defaults and '#2962FF' in defaults,'Folder2 multicolor TradingView-style palette'),
    ('fib-retracement' in defaults and 'trend-fib-extension' in defaults and 'fib-time-zone' in defaults and 'fib-speed-fan' in defaults,'major Fib default level families'),
    ('targetChart.PreviewDrawing(original);' in open_settings,'modeless settings cancel restore path'),
    ('window.Show();' in open_settings and 'window.ShowDialog' not in open_settings,'generic drawing settings remain non-blocking'),
    ('public bool WasAccepted' in generic,'generic settings accepted-state preserved'),
]
for c,l in markers: check(c,l)

# Verify requested Folder2 catalog tools all exist.
folder2=['fib-retracement','trend-fib-extension','fib-channel','fib-time-zone','fib-speed-fan','trend-fib-time','fib-circles','fib-spiral','fib-speed-arcs','fib-wedge','pitchfan','gann-box','gann-square-fixed','gann-square','gann-fan']
for tool in folder2: check(f'"{tool}"' in catalog,f'Folder2 tool exists: {tool}')

# Compare against the clean v88 source when available in this validation environment.
base=Path('/mnt/data/ticklab_work/v89_baseline/TickLabV1_13_0_88_Pitchfork_Settings_Stability/src/TickLab.App')
if base.exists():
    actual=set()
    rels={p.relative_to(app) for p in app.rglob('*') if p.is_file()}|{p.relative_to(base) for p in base.rglob('*') if p.is_file()}
    for rel in rels:
        a=app/rel; b=base/rel
        if not a.exists() or not b.exists() or a.read_bytes()!=b.read_bytes(): actual.add(str(rel))
    expected={
        'Controls/CandleChartControl.Drawing.cs','Controls/CandleChartControl.DrawingParity.cs',
        'Core/Drawing/DrawingParityDefaults.cs','Core/Drawing/DrawingToolCatalog.cs',
        'MainWindow.xaml','MainWindow.xaml.cs','TickLab.App.csproj',
        'Windows/TradingViewFibGannSettingsWindow.xaml','Windows/TradingViewFibGannSettingsWindow.xaml.cs'}
    check(actual==expected,f'exact intended app source diff ({len(actual)} files)')
    protected_tokens=('Bridge','History','Replay','Mt5','MT5','Gateway','Connector')
    changed_protected=[x for x in actual if any(tok in x for tok in protected_tokens)]
    check(not changed_protected,'no bridge/history/replay/MT5 source touched')

print(f'passed={len(passed)} failed={len(failed)}')
for f in failed: print('FAIL',f)
if failed: sys.exit(1)
