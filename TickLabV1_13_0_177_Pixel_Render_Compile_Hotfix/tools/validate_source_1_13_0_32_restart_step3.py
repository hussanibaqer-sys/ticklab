from pathlib import Path
import hashlib, re, sys, xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
base = Path('/mnt/data/ticklab_step3_baseline/TickLabV1_13_0_31_Restart_Step2_Indicator_Properties_Controls_Contrast')
passed = 0
failed = []

def check(name, condition):
    global passed
    if condition:
        passed += 1
    else:
        failed.append(name)

def text(rel):
    return (root / rel).read_text(encoding='utf-8')

csproj = text('src/TickLab.App/TickLab.App.csproj')
main_xaml = text('src/TickLab.App/MainWindow.xaml')
management = text('src/TickLab.App/MainWindow.IndicatorManagement.cs')
chart = text('src/TickLab.App/Controls/CandleChartControl.cs')
script_plot = text('src/TickLab.App/Controls/TickScriptIndicatorPlotControl.cs')
builtin_plot = text('src/TickLab.App/Controls/BuiltInIndicatorPlotControl.cs')
stack = text('src/TickLab.App/Controls/IndicatorPaneStackControl.cs')
symbol_xaml = text('src/TickLab.App/Windows/SymbolPickerWindow.xaml')
symbol_cs = text('src/TickLab.App/Windows/SymbolPickerWindow.xaml.cs')
color_helper = text('src/TickLab.App/Settings/ColorDisplayHelper.cs')
chart_settings = text('src/TickLab.App/Windows/ChartSettingsWindow.xaml.cs')
drawing_xaml = text('src/TickLab.App/Windows/DrawingSettingsWindow.xaml')
drawing_cs = text('src/TickLab.App/Windows/DrawingSettingsWindow.xaml.cs')
picker_xaml = text('src/TickLab.App/Windows/DrawingColorPickerWindow.xaml')
picker_cs = text('src/TickLab.App/Windows/DrawingColorPickerWindow.xaml.cs')
builtin_settings = text('src/TickLab.App/Windows/BuiltInIndicatorSettingsWindow.cs')
script_settings = text('src/TickLab.App/Windows/TickScriptIndicatorSettingsWindow.cs')

check('version metadata', '<Version>1.13.0.32</Version>' in csproj)
check('assembly metadata', '<AssemblyVersion>1.13.0.32</AssemblyVersion>' in csproj)
check('file metadata', '<FileVersion>1.13.0.32</FileVersion>' in csproj)
check('window title', 'TickLab v1.13.0.32 — Restart Step 3' in main_xaml)
check('solution name', (root / 'TickLabV1_13_0_32.sln').exists())

# Indicator right-click and exact address.
check('chart address formatter', 'FormatChartIndicatorAddress' in management)
check('workspace address formatter', 'FormatIndicatorWorkspaceAddress' in management)
check('chart menu address', 'entry.DisplayName} — {entry.Placement}' in chart)
check('chart remove selected', 'Remove selected indicator' in chart)
check('script plot address provider', 'PlacementAddressProvider' in script_plot)
check('built-in plot address provider', 'PlacementAddressProvider' in builtin_plot)
check('script right-click address', 'TickScript indicator"} — {address}' in script_plot)
check('built-in right-click address', 'Built-in indicator"} — {address}' in builtin_plot)
check('script remove selected', 'Remove selected indicator' in script_plot)
check('built-in remove selected', 'Remove selected indicator' in builtin_plot)
check('stack provider forwarding', stack.count('PlacementAddressProvider = () => PlacementAddressProvider?.Invoke()') >= 2)

# Symbol picker.
check('editable symbol search', 'IsReadOnly="False"' in symbol_xaml)
check('search white background', '<TextBox x:Name="SearchBox"' in symbol_xaml and 'Background="White"' in symbol_xaml)
check('search black text', 'Foreground="Black"' in symbol_xaml)
check('visible symbol scrollbar', 'ScrollViewer.VerticalScrollBarVisibility="Visible"' in symbol_xaml)
check('exact symbol priority', 'string.Equals(item.Name, search, StringComparison.OrdinalIgnoreCase) ? 0 : 1' in symbol_cs)
check('exact symbol selection', 'Mt5SymbolInfo? exact' in symbol_cs)
check('symbol names black', 'Text="{Binding Name}"' in symbol_xaml and 'Foreground="Black"' in symbol_xaml)

# Colour-code hiding and names.
check('colour helper exists', 'public static class ColorDisplayHelper' in color_helper)
check('one second tooltip', 'SetInitialShowDelay' in color_helper and '1000' in color_helper)
check('custom colour fallback', 'return "Custom colour"' in color_helper)
check('swatch-only helper', 'button.Content = null;' in color_helper)
check('chart colour boxes hidden', 'Visibility = Visibility.Collapsed' in chart_settings)
check('chart swatches named', 'ColorDisplayHelper.ApplyToButton(swatch, box.Text)' in chart_settings)
check('built-in swatches named', 'ColorDisplayHelper.ApplyToButton(button, color)' in builtin_settings)
check('TickScript swatches named', 'ColorDisplayHelper.ApplyToButton(button, value)' in script_settings)
check('drawing colour boxes hidden', drawing_xaml.count('Visibility="Collapsed"') >= 4)
check('drawing level swatches', 'ColorValueToBrushConverter' in drawing_xaml and 'ColorValueToNameConverter' in drawing_xaml)
check('drawing buttons named', drawing_cs.count('ColorDisplayHelper.ApplyToButton') >= 4)
check('picker no hexadecimal label', 'Hexadecimal' not in picker_xaml)
check('picker no visible RGB boxes', 'x:Name="RedBox" Visibility="Collapsed"' in picker_xaml and 'x:Name="GreenBox" Visibility="Collapsed"' in picker_xaml and 'x:Name="BlueBox" Visibility="Collapsed"' in picker_xaml)
check('picker custom sliders', 'x:Name="RedSlider"' in picker_xaml and 'ComponentSlider_ValueChanged' in picker_cs)
check('picker names only', 'SelectedColourText.Text = matchingEntry?.Name ?? ColorDisplayHelper.GetName(normalized);' in picker_cs)
check('no code in palette tooltip', '{entry.Hex}' not in picker_cs)
check('no colour content assignment', not re.search(r'button\.Content\s*=\s*(?:color|value|initialColor)', builtin_settings + script_settings, re.I))

# XML/XAML/project parsing.
for path in list(root.rglob('*.xaml')) + list(root.rglob('*.csproj')):
    try:
        ET.parse(path)
        check('xml ' + str(path.relative_to(root)), True)
    except Exception:
        check('xml ' + str(path.relative_to(root)), False)

# Lightweight C# delimiter check after masking comments and strings.
def mask(code):
    out=[]; i=0; n=len(code); state='normal'
    while i<n:
        c=code[i]; nxt=code[i+1] if i+1<n else ''
        if state=='normal':
            if c=='/' and nxt=='/': out.extend('  '); i+=2; state='line'; continue
            if c=='/' and nxt=='*': out.extend('  '); i+=2; state='block'; continue
            if c=='@' and nxt=='"': out.extend('  '); i+=2; state='verbatim'; continue
            if c=='"': out.append(' '); i+=1; state='string'; continue
            if c=="'": out.append(' '); i+=1; state='char'; continue
            out.append(c); i+=1; continue
        if state=='line':
            if c=='\n': out.append('\n'); state='normal'
            else: out.append(' ')
            i+=1; continue
        if state=='block':
            if c=='*' and nxt=='/': out.extend('  '); i+=2; state='normal'
            else: out.append('\n' if c=='\n' else ' '); i+=1
            continue
        if state=='string':
            if c=='\\': out.extend('  ' if i+1<n else ' '); i+=2; continue
            if c=='"': out.append(' '); i+=1; state='normal'; continue
            out.append('\n' if c=='\n' else ' '); i+=1; continue
        if state=='verbatim':
            if c=='"' and nxt=='"': out.extend('  '); i+=2; continue
            if c=='"': out.append(' '); i+=1; state='normal'; continue
            out.append('\n' if c=='\n' else ' '); i+=1; continue
        if state=='char':
            if c=='\\': out.extend('  ' if i+1<n else ' '); i+=2; continue
            if c=="'": out.append(' '); i+=1; state='normal'; continue
            out.append(' '); i+=1
    return ''.join(out)

for path in root.rglob('*.cs'):
    code=mask(path.read_text(encoding='utf-8'))
    ok=True
    for open_c, close_c in [('(',')'),('[',']'),('{','}')]:
        depth=0
        for c in code:
            if c==open_c: depth+=1
            elif c==close_c:
                depth-=1
                if depth<0: ok=False; break
        if depth!=0: ok=False
    check('csharp delimiters ' + str(path.relative_to(root)), ok)

expected = {
    'src/TickLab.App/Controls/BuiltInIndicatorPlotControl.cs',
    'src/TickLab.App/Controls/CandleChartControl.cs',
    'src/TickLab.App/Controls/IndicatorPaneStackControl.cs',
    'src/TickLab.App/Controls/TickScriptIndicatorPlotControl.cs',
    'src/TickLab.App/MainWindow.ChartContexts.cs',
    'src/TickLab.App/MainWindow.IndependentIndicators.cs',
    'src/TickLab.App/MainWindow.IndicatorManagement.cs',
    'src/TickLab.App/MainWindow.xaml',
    'src/TickLab.App/Settings/ColorDisplayHelper.cs',
    'src/TickLab.App/TickLab.App.csproj',
    'src/TickLab.App/Windows/BuiltInIndicatorSettingsWindow.cs',
    'src/TickLab.App/Windows/ChartSettingsWindow.xaml.cs',
    'src/TickLab.App/Windows/DrawingColorPickerWindow.xaml',
    'src/TickLab.App/Windows/DrawingColorPickerWindow.xaml.cs',
    'src/TickLab.App/Windows/DrawingSettingsWindow.xaml',
    'src/TickLab.App/Windows/DrawingSettingsWindow.xaml.cs',
    'src/TickLab.App/Windows/SymbolPickerWindow.xaml',
    'src/TickLab.App/Windows/SymbolPickerWindow.xaml.cs',
    'src/TickLab.App/Windows/TickScriptIndicatorSettingsWindow.cs',
}
changed=set()
base_files={p.relative_to(base).as_posix():p for p in (base/'src').rglob('*') if p.is_file()}
new_files={p.relative_to(root).as_posix():p for p in (root/'src').rglob('*') if p.is_file()}
for rel in sorted(set(base_files)|set(new_files)):
    bp=base_files.get(rel); np=new_files.get(rel)
    if bp is None or np is None or hashlib.sha256(bp.read_bytes()).digest()!=hashlib.sha256(np.read_bytes()).digest():
        changed.add(rel)
check('only intended source changes', changed == expected)
if changed != expected:
    print('Changed source files:', *sorted(changed), sep='\n  ')

# Protect MT5, bridge, history and market-data source areas.
for rel, bp in base_files.items():
    lower=rel.lower()
    if ('filebridge' in lower or '/gateway/' in lower or '/core/history/' in lower or rel.startswith('MT5/') or rel.startswith('MQL5/')):
        np=root/rel
        check('protected ' + rel, np.exists() and hashlib.sha256(bp.read_bytes()).digest()==hashlib.sha256(np.read_bytes()).digest())

print(f'V1.13.0.32 RESTART STEP 3 CHECKS PASSED: {passed}')
print(f'V1.13.0.32 RESTART STEP 3 CHECKS FAILED: {len(failed)}')
for item in failed:
    print('FAIL:', item)
sys.exit(1 if failed else 0)
