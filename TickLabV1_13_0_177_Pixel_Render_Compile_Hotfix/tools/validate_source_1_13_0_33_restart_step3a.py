from pathlib import Path
import hashlib, re, sys, xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
base = Path('/mnt/data/ticklab_step3_hotfix_work/TickLabV1_13_0_32_Restart_Step3_Indicator_Remove_Symbol_Search_Colour_Names')
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

app='src/TickLab.App/'
csproj=text(app+'TickLab.App.csproj')
main_xaml=text(app+'MainWindow.xaml')
drawing=text(app+'Windows/DrawingSettingsWindow.xaml.cs')
catalog=text(app+'Core/Indicators/BuiltInIndicatorCatalog.cs')
workspaces=text(app+'MainWindow.Workspaces.cs')
mainwindow=text(app+'MainWindow.xaml.cs')
theme=text(app+'Settings/ApplicationThemeManager.cs')
color=text(app+'Settings/ColorDisplayHelper.cs')

check('version', '<Version>1.13.0.33</Version>' in csproj)
check('assembly version', '<AssemblyVersion>1.13.0.33</AssemblyVersion>' in csproj)
check('file version', '<FileVersion>1.13.0.33</FileVersion>' in csproj)
check('title', 'TickLab v1.13.0.33 — Restart Step 3A Compile Hotfix' in main_xaml)
check('solution', (root/'TickLabV1_13_0_33.sln').exists())
check('drawing helper namespace', 'using TickLab.Desktop.Settings;' in drawing)
check('drawing helper calls retained', drawing.count('ColorDisplayHelper.ApplyToButton') == 4)
check('unused Bool removed', 'IndicatorParameterDefinition Bool(' not in catalog)
check('unused workspace field removed', '_indicatorContentAssigned' not in workspaces)
check('unused refresh fields removed', '_indicatorRefreshRunning' not in mainwindow and '_indicatorRefreshPending' not in mainwindow)
check('application flow safe', 'Application? app = Application.Current;' in theme and 'foreach (Window window in app.Windows)' in theme)
check('resource flow safe', 'UpdateResource(Application app' in theme and 'app.Resources[key]' in theme)
check('no unsafe theme current dereference', 'Application.Current.Windows' not in theme and 'Application.Current.Resources' not in theme)
check('colour lookup nonnullable', 'out string name' in color and 'out string? name' not in color)

# Known nullable-dialog patterns must be gone.
nullable_files=[
    'MainWindow.AlertsReplay.cs','MainWindow.ChartAppearance.cs','MainWindow.ChartTypes.cs',
    'MainWindow.IndependentIndicators.cs','MainWindow.IndicatorRouting.cs',
    'Windows/ChartSettingsWindow.xaml.cs','Windows/HistoryManagementWindow.xaml.cs'
]
for rel in nullable_files:
    value=text(app+rel)
    check('no combined nullable dialog pattern '+rel, not re.search(r'ShowDialog\([^)]*\)?\s*!=\s*true\s*\|\|[^\n]*\sis\snull', value))

# Step 3 behavior remains present.
management=text(app+'MainWindow.IndicatorManagement.cs')
chart=text(app+'Controls/CandleChartControl.cs')
symbol_xaml=text(app+'Windows/SymbolPickerWindow.xaml')
symbol_cs=text(app+'Windows/SymbolPickerWindow.xaml.cs')
check('indicator address retained', 'FormatChartIndicatorAddress' in management and 'FormatIndicatorWorkspaceAddress' in management)
check('indicator remove retained', 'Remove selected indicator' in chart)
check('symbol editable retained', 'IsReadOnly="False"' in symbol_xaml)
check('symbol scrollbar retained', 'ScrollViewer.VerticalScrollBarVisibility="Visible"' in symbol_xaml)
check('symbol exact priority retained', 'Mt5SymbolInfo? exact' in symbol_cs)
check('colour helper retained', 'public static class ColorDisplayHelper' in color and 'SetInitialShowDelay' in color)

# XML/XAML/project parse.
for path in list(root.rglob('*.xaml')) + list(root.rglob('*.csproj')):
    try:
        ET.parse(path)
        check('xml '+str(path.relative_to(root)), True)
    except Exception:
        check('xml '+str(path.relative_to(root)), False)

# Lightweight delimiter validation.
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
    for a,b in [('(',')'),('[',']'),('{','}')]:
        depth=0
        for c in code:
            if c==a: depth+=1
            elif c==b:
                depth-=1
                if depth<0: ok=False; break
        if depth!=0: ok=False
    check('csharp delimiters '+str(path.relative_to(root)), ok)

expected={
    app+'Core/Indicators/BuiltInIndicatorCatalog.cs',
    app+'MainWindow.AlertsReplay.cs',
    app+'MainWindow.ChartAppearance.cs',
    app+'MainWindow.ChartTypes.cs',
    app+'MainWindow.IndependentIndicators.cs',
    app+'MainWindow.IndicatorRouting.cs',
    app+'MainWindow.Workspaces.cs',
    app+'MainWindow.xaml',
    app+'MainWindow.xaml.cs',
    app+'Settings/ApplicationThemeManager.cs',
    app+'Settings/ColorDisplayHelper.cs',
    app+'TickLab.App.csproj',
    app+'Windows/ChartSettingsWindow.xaml.cs',
    app+'Windows/DrawingSettingsWindow.xaml.cs',
    app+'Windows/HistoryManagementWindow.xaml.cs',
}
base_files={p.relative_to(base).as_posix():p for p in (base/'src').rglob('*') if p.is_file()}
new_files={p.relative_to(root).as_posix():p for p in (root/'src').rglob('*') if p.is_file()}
changed=set()
for rel in sorted(set(base_files)|set(new_files)):
    bp=base_files.get(rel); np=new_files.get(rel)
    if bp is None or np is None or hashlib.sha256(bp.read_bytes()).digest()!=hashlib.sha256(np.read_bytes()).digest():
        changed.add(rel)
check('only hotfix source changes', changed==expected)
if changed!=expected:
    print('Changed source files:', *sorted(changed), sep='\n  ')

# Protected bridge/history/market-data sources unchanged.
for rel,bp in base_files.items():
    low=rel.lower()
    if ('filebridge' in low or '/gateway/' in low or '/core/history/' in low or rel.startswith('MT5/') or rel.startswith('MQL5/')):
        np=root/rel
        check('protected '+rel, np.exists() and hashlib.sha256(bp.read_bytes()).digest()==hashlib.sha256(np.read_bytes()).digest())

print(f'V1.13.0.33 RESTART STEP 3A CHECKS PASSED: {passed}')
print(f'V1.13.0.33 RESTART STEP 3A CHECKS FAILED: {len(failed)}')
for item in failed: print('FAIL:',item)
sys.exit(1 if failed else 0)
