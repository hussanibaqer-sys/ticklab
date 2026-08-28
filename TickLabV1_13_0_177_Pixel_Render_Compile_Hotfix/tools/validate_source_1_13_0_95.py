from pathlib import Path
import hashlib, re, sys, xml.etree.ElementTree as ET
from pygments import lex
from pygments.lexers.dotnet import CSharpLexer
from pygments.token import Token

root=Path(__file__).resolve().parents[1]
app=root/'src'/'TickLab.App'
base=Path('/mnt/data/ticklab_work/v95/TickLabV1_13_0_94_Fib_Reading_Font_Restore')
passed=[]; failed=[]
def check(ok,label): (passed if ok else failed).append(label)
def read(p): return p.read_text(encoding='utf-8-sig', errors='ignore')

# identity / launch
proj=read(app/'TickLab.App.csproj')
for marker in ('<Version>1.13.0.95</Version>','<AssemblyVersion>1.13.0.95</AssemblyVersion>','<FileVersion>1.13.0.95</FileVersion>'):
    check(marker in proj, marker)
check((root/'TickLabV1_13_0_95.sln').exists(),'v95 solution exists')
check(not (root/'TickLabV1_13_0_90.sln').exists(),'old v90 solution renamed')
check(read(root/'VERSION.txt').strip()=='1.13.0.95','VERSION.txt v95')
check('TickLabV1_13_0_95.sln' in read(root/'Clean-Restore-Build.cmd'),'build command targets v95')
check('TickLab v1.13.0.95 — Gann Geometry / Theme Contrast Fix' in read(app/'MainWindow.xaml'),'window title v95')

# all xaml + events
all_main='\n'.join(read(p) for p in app.glob('MainWindow*.cs'))
handler_pattern=r'\b(?:Click|Checked|Unchecked|TextChanged|SelectionChanged|SelectedDateChanged|PreviewMouse\w+|PreviewKey\w+|Mouse\w+|Key\w+|Drag\w+|Drop|Loaded|Closing|ValueChanged)="([A-Za-z_]\w*)"'
for x in app.rglob('*.xaml'):
    try:
        ET.parse(x); check(True,f'XAML parses {x.relative_to(app)}')
    except Exception as exc:
        check(False,f'XAML parse {x.relative_to(app)}: {exc}')
    text=read(x)
    names=re.findall(r'x:Name="([^"]+)"',text)
    check(len(names)==len(set(names)),f'unique x:Name {x.relative_to(app)}')
    code=all_main if x.name=='MainWindow.xaml' else (read(x.with_suffix('.xaml.cs')) if x.with_suffix('.xaml.cs').exists() else '')
    for handler in sorted(set(re.findall(handler_pattern,text))):
        check(re.search(r'\b'+re.escape(handler)+r'\s*\(',code) is not None,f'handler {x.name}:{handler}')

# lexical C# structure
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

main=read(app/'MainWindow.xaml.cs')
mainx=read(app/'MainWindow.xaml')
drawing=read(app/'Controls/CandleChartControl.Drawing.cs')
parity=read(app/'Controls/CandleChartControl.DrawingParity.cs')
defaults=read(app/'Core/Drawing/DrawingParityDefaults.cs')
appx=read(app/'App.xaml')
theme=read(app/'Settings/ApplicationThemeManager.cs')
linex=read(app/'Windows/TradingViewLineSettingsWindow.xaml')
linecs=read(app/'Windows/TradingViewLineSettingsWindow.xaml.cs')
genericx=read(app/'Windows/DrawingSettingsWindow.xaml')
genericcs=read(app/'Windows/DrawingSettingsWindow.xaml.cs')
fibx=read(app/'Windows/TradingViewFibGannSettingsWindow.xaml')

markers=[
# Gann comparison geometry
('DrawParityGannSquare(dc, layout, drawing, rect, active, pen, reverse, useOneColor, bandOpacity);' in parity,'Gann box/square family routes to one reference geometry'),
('DrawParityGannBox' not in parity,'old mismatched Gann Box renderer removed'),
('new DrawingLevel(0.2, "0.2"' in defaults and 'new DrawingLevel(0.8, "0.8"' in defaults,'Gann box/square 5 equal subdivisions defaults'),
('CreateGannQuarterEllipseArc' in parity and 'rect.Width * radius' in parity and 'rect.Height * radius' in parity,'Gann arcs use independent X/Y ellipse radii'),
('Math.Sqrt(ax * ax + ay * ay) / 5.0' in parity,'Gann reference arc radius formula'),
('(2, 1, "#00BCD4")' in parity and '(1, 1, "#4CAF50")' in parity and '(1, 2, "#089981")' in parity,'Gann reference fan rays 2x1 1x1 1x2'),
('CreateGannQuarterEllipseBand' in parity,'Gann coloured quarter-annular background bands'),
('if (drawing.ToolId == "gann-square-fixed")' in parity and 'Math.Max(24, Math.Max(Math.Abs(p2.X - p1.X), Math.Abs(p2.Y - p1.Y)))' in parity,'fixed Gann diagonal square constraint'),
('if (drawing.ToolId == "gann-square")' not in drawing[drawing.index('private static Point GetGannDisplaySecondPoint'):drawing.index('private Rect GetDrawingBounds')],'regular Gann no forced screen-square constraint'),
('fixedRect.TopLeft, fixedRect.TopRight, fixedRect.BottomRight, fixedRect.BottomLeft' in drawing,'fixed Gann displays all four corner handles'),
('drawing.ToolId == "gann-square-fixed" && _dragStartAnchors.Count >= 2' in drawing,'fixed Gann four-corner drag handling'),
('DistanceToGannQuarterEllipse' in drawing,'Gann arc hit testing matches ellipse geometry'),
('ParityReadingText(drawing, level, YToPrice(y, layout))' in parity,'Gann price/readings rendering retained'),
# Fib controls regression guards
('Content="Level readings"' in fibx,'Fib master Level readings control preserved'),
('Content="Prices on levels"' in fibx,'Fib master Prices on levels control preserved'),
('Content="Add reading"' in fibx and 'Content="Remove reading"' in fibx,'Fib add/remove reading controls preserved'),
('Header="Price"' in fibx and 'Header="Level reading"' in fibx,'Fib per-level price/reading controls preserved'),
# global contrast/theme
('x:Key="ControlBrush"' in appx and 'x:Key="SelectionTextBrush"' in appx and 'x:Key="MenuBrush"' in appx,'theme-safe interactive resources exist'),
('Value="{DynamicResource TextBrush}"' in appx and 'Value="{DynamicResource ControlBrush}"' in appx,'global control text/background dynamic'),
('TargetType="ComboBoxItem"' in appx and 'TargetType="ListBoxItem"' in appx,'popup/list selection styles theme-safe'),
('TargetType="ContextMenu"' in appx and 'TargetType="MenuItem"' in appx,'context menu theme styles exist'),
('Value="{DynamicResource SelectionBrush}"' in appx and 'Value="{DynamicResource SelectionTextBrush}"' in appx,'selection backgrounds always have contrast text'),
('UpdateResource(app, "SelectionBrush"' in theme and 'UpdateResource(app, "SelectionTextBrush"' in theme,'light/dark selection resources update live'),
('UpdateResource(app, "ControlBrush"' in theme and 'UpdateResource(app, "MenuBrush"' in theme,'light/dark control/menu resources update live'),
('case TabItem tabItem:' in theme and 'case ComboBoxItem comboItem:' in theme and 'case MenuItem menuItem:' in theme,'runtime theme recursion covers edit/selection popup states'),
('Background="{DynamicResource WindowBrush}" Foreground="{DynamicResource TextBrush}"' in linex,'line settings no forced white/light text mismatch'),
('ApplicationThemeManager.ApplyToWindow(this);' in linecs,'line settings actively themed'),
('ApplicationThemeManager.ApplyToWindow(this);' in genericcs,'generic tool settings actively themed'),
('{DynamicResource WindowBrush}' in genericx and '{DynamicResource TextBrush}' in genericx,'generic edit window uses dynamic theme'),
('{DynamicResource WindowBrush}' in fibx and '{DynamicResource TextBrush}' in fibx,'Fib/Gann edit window uses dynamic theme'),
('DrawingCategoryPaletteBorder.Background = Brushes.White' not in main,'tool selection flyout no forced white background'),
('row.Background = dark ?' not in main and 'row.Background = active ? new SolidColorBrush(Color.FromRgb(238, 241, 247)) : Brushes.White' not in main,'reference toolbox rows no white/dark hardcoding'),
('Brush selectedText = DrawingUiBrush("SelectionTextBrush"' in main,'toolbox selected rows use contrast text resource'),
('menu.Background = panel;' in main and 'Brush foreground = DrawingUiBrush("TextBrush"' in main,'quick/context menus use active theme'),
('QuickLineColorButton.Background = Brushes.White' not in main,'quick edit line-color control no forced white surface'),
('DrawingUiBrush("SelectionBrush"' in main and 'InlineDrawingObjectTreePanel' in main,'inline edit/object tab selected state follows theme'),
('RightWorkspaceBorder" Grid.Column="5"' in mainx and 'Background="{DynamicResource WindowBrush}"' in mainx and 'InlineInspectorTitleText' in mainx and 'Foreground="{DynamicResource TextBrush}"' in mainx,'drawing edit/objects workspace follows active theme'),
('ToolPartitionBorder' in mainx and 'Background="{DynamicResource PanelBrush}"' in mainx,'tool panel surface follows active theme'),
# draggable quick edit bar
('Cursor="SizeAll" ToolTip="Drag toolbar"' in mainx,'quick edit grip visibly draggable'),
('QuickGripText_PreviewMouseLeftButtonDown' in mainx and 'QuickGripText_PreviewMouseMove' in mainx and 'QuickGripText_PreviewMouseLeftButtonUp' in mainx,'quick edit drag events wired'),
('_quickEditBarManualPosition' in main and 'Math.Clamp(_quickEditBarDragStartMargin.Left + delta.X' in main,'quick edit drag clamps inside TickLab frame'),
('QuickGripText.Visibility = Visibility.Visible;' in main,'drag grip available for every selected drawing tool'),
# protected prior fixes
('tool.Id == "disjoint-channel"' in drawing and 'StartUnix = _workingDrawing.Anchors[1].StartUnix' in drawing,'Disjoint vertical lock preserved'),
('new Typeface("Segoe UI"), 10, brush, 1.0' in drawing and 'dc.DrawText(formatted, point);' in drawing,'clean drawing reading font renderer remains'),
]
for ok,label in markers: check(ok,label)

# protected source hash: no MT5/bridge/history/replay implementation changed vs v94
if base.exists():
    baseapp=base/'src'/'TickLab.App'
    changed=[]
    rels={p.relative_to(app) for p in app.rglob('*') if p.is_file()} | {p.relative_to(baseapp) for p in baseapp.rglob('*') if p.is_file()}
    for rel in rels:
        a=app/rel; b=baseapp/rel
        if not a.exists() or not b.exists() or a.read_bytes()!=b.read_bytes(): changed.append(str(rel))
    protected=[x for x in changed if any(tok in x.lower() for tok in ('bridge','history','replay','mt5','gateway','connector'))]
    check(not protected,'no MT5/bridge/history/replay/gateway/connector source changed')
    check(len(changed)==12,f'intended app source scope is 12 files ({len(changed)})')
else:
    changed=[]; check(False,'v94 base exists for source-scope comparison')

print(f'passed={len(passed)} failed={len(failed)}')
for item in failed: print('FAIL',item)
if changed:
    print('SOURCE_DIFF')
    for item in sorted(changed): print(item)
if failed: sys.exit(1)
