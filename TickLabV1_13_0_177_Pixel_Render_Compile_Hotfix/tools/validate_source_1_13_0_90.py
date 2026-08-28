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
base = Path('/mnt/data/ticklab_work/v90/base89_backup/src/TickLab.App')
passed=[]; failed=[]

def check(condition, label):
    (passed if condition else failed).append(label)

def read(path):
    return path.read_text(encoding='utf-8-sig', errors='ignore')

# Identity / launcher
proj = read(app/'TickLab.App.csproj')
for marker in ('<Version>1.13.0.90</Version>', '<AssemblyVersion>1.13.0.90</AssemblyVersion>', '<FileVersion>1.13.0.90</FileVersion>'):
    check(marker in proj, marker)
check((root/'TickLabV1_13_0_90.sln').exists(), 'v90 solution exists')
check(not (root/'TickLabV1_13_0_89.sln').exists(), 'old v89 solution name removed')
check(read(root/'VERSION.txt').strip() == '1.13.0.90', 'VERSION.txt v90')
check('TickLabV1_13_0_90.sln' in read(root/'Clean-Restore-Build.cmd'), 'build command targets v90')
check('TickLab v1.13.0.90 — Folder 2 Fib / Gann Rebuild' in read(app/'MainWindow.xaml'), 'window title v90')

# XAML parse/name/handler integrity.
all_main='\n'.join(read(p) for p in app.glob('MainWindow*.cs'))
handler_pattern=r'\b(?:Click|Checked|Unchecked|TextChanged|SelectionChanged|SelectedDateChanged|PreviewMouse\w+|PreviewKey\w+|Mouse\w+|Key\w+|Drag\w+|Drop|Loaded|Closing|ValueChanged)="([A-Za-z_]\w*)"'
for x in app.rglob('*.xaml'):
    try:
        ET.parse(x)
        check(True, f'XAML parses {x.relative_to(app)}')
    except Exception as exc:
        check(False, f'XAML parse {x.relative_to(app)}: {exc}')
    text=read(x)
    names=re.findall(r'x:Name="([^"]+)"', text)
    check(len(names)==len(set(names)), f'unique x:Name {x.relative_to(app)}')
    code=all_main if x.name=='MainWindow.xaml' else (read(x.with_suffix('.xaml.cs')) if x.with_suffix('.xaml.cs').exists() else '')
    for handler in sorted(set(re.findall(handler_pattern,text))):
        check(re.search(r'\b'+re.escape(handler)+r'\s*\(',code) is not None, f'handler {x.name}:{handler}')

# C# lexical bracket structure. Strings/comments are ignored by lexer.
for p in app.rglob('*.cs'):
    stack=[]; ok=True; pairs={')':'(',']':'[','}':'{'}
    for typ,val in lex(read(p), CSharpLexer()):
        if typ in Token.Punctuation:
            for ch in val:
                if ch in '([{': stack.append(ch)
                elif ch in ')]}':
                    if not stack or stack[-1] != pairs[ch]:
                        ok=False; break
                    stack.pop()
        if not ok: break
    check(ok and not stack, f'C# structure {p.relative_to(app)}')

# Duplicate simple case labels inside DrawingParityDefaults' option switch are a common patching failure.
defaults=read(app/'Core/Drawing/DrawingParityDefaults.cs')
option_switch=defaults[defaults.index('switch (id)'):defaults.index('return values;', defaults.index('switch (id)'))]
case_labels=re.findall(r'case\s+"([^"]+)"\s*:', option_switch)
check(len(case_labels)==len(set(case_labels)), 'NumericOptions switch has no duplicate string case labels')

catalog=read(app/'Core/Drawing/DrawingToolCatalog.cs')
ids=re.findall(r'Add\("([^"]+)"', catalog)
check(len(ids)==len(set(ids)), f'drawing tool IDs unique ({len(ids)})')

main=read(app/'MainWindow.xaml.cs')
drawing=read(app/'Controls/CandleChartControl.Drawing.cs')
parity=read(app/'Controls/CandleChartControl.DrawingParity.cs')
icons=read(app/'Core/Drawing/DrawingToolIconFactory.cs')
palette_x=read(app/'Windows/DrawingToolPaletteWindow.xaml')
palette_cs=read(app/'Windows/DrawingToolPaletteWindow.xaml.cs')
fibx=read(app/'Windows/TradingViewFibGannSettingsWindow.xaml')
fibcs=read(app/'Windows/TradingViewFibGannSettingsWindow.xaml.cs')
generic=read(app/'Windows/DrawingSettingsWindow.xaml.cs')
open_settings=main[main.index('private void OpenDrawingSettings'):main.index('private void OpenDrawingObjectTree')]

markers=[
    # Folder 1 regression guard
    ('tool.Id == "disjoint-channel"' in drawing and 'StartUnix = _workingDrawing.Anchors[1].StartUnix' in drawing, 'Disjoint point3 vertical time/X lock preserved'),
    ('Point c = new(b.X, points[2].Y);' in parity and 'Vector opposite = new(first.X, -first.Y);' in parity, 'Disjoint mirrored second rail preserved'),

    # Settings stability + theme
    ('new TradingViewFibGannSettingsWindow' in open_settings and 'fibWindow.Show();' in open_settings and 'fibWindow.ShowDialog' not in open_settings, 'Fib/Gann settings modeless/non-blocking'),
    ('window.Show();' in open_settings and 'window.ShowDialog' not in open_settings, 'generic drawing settings modeless/non-blocking'),
    ('targetChart.PreviewDrawing(original);' in open_settings, 'Cancel/X drawing restore path preserved'),
    ('ApplicationThemeManager.ApplyToWindow(this);' in fibcs and 'ApplyReferenceLightAppearance' not in fibcs, 'Fib/Gann settings follows active theme'),
    ('ApplicationThemeManager.ApplyToWindow(this);' in palette_cs, 'tool palette follows active theme'),
    ('{DynamicResource TextBrush}' in palette_x and '{DynamicResource BorderBrush}' in palette_x, 'palette XAML uses theme resources'),
    ('DrawingQuickEditBar.Background = Brushes.White' not in main, 'selected-object toolbar no forced white surface'),
    ('DrawingQuickEditBar.Background = DrawingUiBrush("PanelBrush"' in main and 'QuickLineColorButton.Foreground = DrawingUiBrush("TextBrush"' in main, 'selected-object toolbar uses theme resources'),

    # Crisp/pro vector icon work
    ('case "fib-retracement"' in icons and 'case "trend-fib-extension"' in icons, 'dedicated vector icons for core Fib tools'),
    ('case "gann-box"' in icons and 'case "gann-square"' in icons and 'case "gann-square-fixed"' in icons and 'case "gann-fan"' in icons, 'dedicated vector icons for Gann tools'),
    ('case "alert"' in icons and 'case "more"' in icons, 'vector contextual action icons'),
    ('SnapsToDevicePixels' in icons and 'EdgeMode.Aliased' in icons, 'icon factory crisp pixel alignment'),

    # Required settings controls
    ('ShowLevelReadingsBox' in fibx and 'BandsBox' in fibx and 'LabelsOutsideBox' in fibx, 'level readings/background/outside toggles exist'),
    ('LabelHorizontalBox' in fibx and 'LabelVerticalBox' in fibx, 'reading left-center-right and above-middle-below controls exist'),
    ('Header="Price"' in fibx and 'Header="Reading"' in fibx, 'per-level Price/Reading checkboxes exist'),
    ('LevelLineColorButton_Click' in fibx and 'ToolTip="Change this level colour; its band uses the same colour"' in fibx, 'per-level colour selector owns band colour'),
    ('Text="Background: transparent → solid"' in fibx and 'FillOpacitySlider" Minimum="0" Maximum="1"' in fibx, 'background gauge direction 0 transparent to 1 solid'),
    ('options["ShowLevelReadings"]' in fibcs and 'options["Bands"]' in fibcs and 'options["LabelsOutside"]' in fibcs, 'settings persist reading/band/outside options'),
    ('options["LabelHorizontal"]' in fibcs and 'options["LabelVertical"]' in fibcs, 'settings persist reading positions'),
    ('row.FillColor = row.Color;' in fibcs, 'per-level band colour follows line colour'),
    ('FillOpacityText = "-1"' in fibcs and 'FillOpacity = effectiveFillOpacity' in fibcs, 'per-level fill opacity defers to overall background gauge'),

    # Common Fib model/rendering
    ('private static double ParityBandOpacity' in parity and 'Math.Clamp(drawing.Style.FillOpacity, 0, 1)' in parity, 'one overall background opacity model'),
    ('private static bool ParityShowReadings' in parity and 'private static string ParityReadingText' in parity, 'common level reading model'),
    ('private static double CrispStroke' in parity, 'crisp line alignment helper'),
    ('CreateDrawingBrush(top.Level.Color, bandOpacity)' in parity, 'Fib retracement bands inherit top line colour'),
    ('ParityHorizontalReadingPoint(drawing, left, right, y)' in parity, 'Fib retracement/extension reading placement helper'),
    ('double naturalWidth = Math.Max(90, Math.Abs(points[1].X - points[0].X));' in parity, 'Trend-Based Fib Extension nonzero level width'),
    ('dc.DrawLine(basePen, points[0], points[1]);' in parity and 'dc.DrawLine(basePen, points[1], points[2]);' in parity, 'Trend-Based Fib Extension three-point construction retained'),
    ('CreateSemiAnnulusGeometry' in parity, 'Fib Speed Resistance Arcs use semicircular bands'),
    ('CreateSectorBandGeometry' in parity, 'Fib Wedge uses curved sector bands'),
    ('DrawParityFibSpiral' in parity and 'ParityBandOpacity(drawing)' in parity, 'Fib Spiral has colored background/ribbon opacity'),
    ('DrawParityFibCircles' in parity and 'Paint outer-to-inner' in parity, 'Fib Circles have nested level-color bands'),
    ('DrawParityFibFan' in parity and 'DrawParityPitchfan' in parity, 'Fib fan/pitchfan dedicated colored-zone geometry'),

    # Gann rebuild
    ('private static Point GannFanTarget' in parity, 'Gann Fan ratio geometry helper'),
    ('DrawParityGannFan' in parity and 'OrderBy(item => Math.Atan2' in parity, 'Gann Fan angle-ordered colored sectors'),
    ('drawing.ToolId == "gann-square-fixed"' in parity and 'rect.TopRight : rect.TopLeft' in parity, 'Gann Square Fixed top-corner origin'),
    ('rect.BottomRight : rect.BottomLeft' in parity, 'Gann Square bottom-corner origin'),
    ('CreateScreenArcGeometry' in parity and 'Reference Gann squares use quarter-circle arcs' in parity, 'Gann square quarter-arc geometry'),
    ('left/horizontal = 0, .382, .618, 1' in parity and 'right/vertical = .25, .5, .75' in parity, 'Gann Box separate reference axis stacks'),
    ('Nearly(level.Value, 0.382)' in parity and 'Nearly(level.Value, 0.618)' in parity and 'Nearly(level.Value, 0.75)' in parity, 'Gann Box reference ratio routing'),
    ('Add("Fan", 0);' in defaults and 'Add("TimeLevels", 3);' in defaults and 'Add("PriceLevels", 3);' in defaults, 'Gann Box no erroneous fan and 3 internal axis levels'),
    ('new DrawingLevel(0.382, "0.382"' in defaults and 'new DrawingLevel(0.618, "0.618"' in defaults and 'new DrawingLevel(0.75, "0.75"' in defaults, 'Gann Box reference level defaults'),
    ('DistanceToFibonacciGannDrawing' in drawing and 'Nearly(level.Value, 0.382)' in drawing, 'Gann Box visible grid hit testing follows displayed geometry'),
    ('GannFanTarget(origin, points[1], level.Value' in drawing, 'Gann Fan hit-testing uses rendered ratio geometry'),

    # Per-level TradingView-style colors and global options
    ('#F23645' in defaults and '#FF9800' in defaults and '#FDD835' in defaults and '#089981' in defaults and '#00BCD4' in defaults and '#2962FF' in defaults and '#7E57C2' in defaults, 'multicolor reference-style palette present'),
    ('Add("ShowLevelReadings", 1);' in defaults and 'Add("Bands", 1);' in defaults, 'Folder2 readings/bands default on'),
    ('Add("LabelHorizontal", 1);' in defaults and 'Add("LabelVertical", -1);' in defaults, 'Fib label position defaults'),
]
for condition,label in markers: check(condition,label)

folder2=['fib-retracement','trend-fib-extension','fib-channel','fib-time-zone','fib-speed-fan','trend-fib-time','fib-circles','fib-spiral','fib-speed-arcs','fib-wedge','pitchfan','gann-box','gann-square-fixed','gann-square','gann-fan']
for tool in folder2:
    check(f'"{tool}"' in catalog, f'Folder2 tool exists: {tool}')
    check(f'"{tool}"' in parity or tool in ('gann-square-fixed','gann-square'), f'Folder2 renderer routes tool: {tool}')

# Exact app source scope against clean v89.
actual=set()
if base.exists():
    rels={p.relative_to(app) for p in app.rglob('*') if p.is_file()} | {p.relative_to(base) for p in base.rglob('*') if p.is_file()}
    for rel in rels:
        a=app/rel; b=base/rel
        if not a.exists() or not b.exists() or a.read_bytes()!=b.read_bytes(): actual.add(str(rel))
    expected={
        'Controls/CandleChartControl.Drawing.cs',
        'Controls/CandleChartControl.DrawingParity.cs',
        'Core/Drawing/DrawingParityDefaults.cs',
        'Core/Drawing/DrawingToolIconFactory.cs',
        'MainWindow.xaml',
        'MainWindow.xaml.cs',
        'TickLab.App.csproj',
        'Windows/DrawingToolPaletteWindow.xaml',
        'Windows/DrawingToolPaletteWindow.xaml.cs',
        'Windows/TradingViewFibGannSettingsWindow.xaml',
        'Windows/TradingViewFibGannSettingsWindow.xaml.cs',
    }
    check(actual==expected, f'exact intended app source diff ({len(actual)} files)')
    protected_tokens=('Bridge','History','Replay','Mt5','MT5','Gateway','Connector')
    changed_protected=[x for x in actual if any(tok in x for tok in protected_tokens)]
    check(not changed_protected, 'no bridge/history/replay/MT5 source touched')
else:
    check(False, 'clean v89 comparison source available')

print(f'passed={len(passed)} failed={len(failed)}')
for item in failed:
    print('FAIL', item)
if actual:
    print('SOURCE_DIFF')
    for item in sorted(actual): print(item)
if failed: sys.exit(1)
