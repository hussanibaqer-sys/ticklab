from pathlib import Path
import hashlib, re, sys, xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
base = Path('/mnt/data/ticklab_step2_work/TickLabV1_13_0_30_Restart_Step1_Sync_Gesture_Fix')
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
placement = text('src/TickLab.App/Windows/IndicatorPlacementModels.cs')
independent = text('src/TickLab.App/MainWindow.IndependentIndicators.cs')
chart_script = text('src/TickLab.App/MainWindow.xaml.cs')
builtin = text('src/TickLab.App/MainWindow.BuiltInIndicators.cs')
workspace = text('src/TickLab.App/Controls/WorkspaceSurfaceControl.cs')
route = text('src/TickLab.App/Windows/IndicatorRouteWindow.cs')
picker = text('src/TickLab.App/Windows/WorkspacePartitionPickerWindow.cs')
builtin_settings = text('src/TickLab.App/Windows/BuiltInIndicatorSettingsWindow.cs')

check('version metadata', '<Version>1.13.0.31</Version>' in csproj)
check('assembly metadata', '<AssemblyVersion>1.13.0.31</AssemblyVersion>' in csproj)
check('file metadata', '<FileVersion>1.13.0.31</FileVersion>' in csproj)
check('window title', 'TickLab v1.13.0.31 — Restart Step 2' in main_xaml)
check('solution name', (root / 'TickLabV1_13_0_31.sln').exists())

check('current workspace place id model', 'IndicatorWorkspacePaneId' in placement)
check('place address selector', 'Place Address' in placement)
check('connect address selector', 'Connect Address' in placement)
check('sync selector', 'Sync with Price Chart' in placement)
check('white selector background', 'combo.Background = White;' in placement)
check('black selector text', 'combo.Foreground = Black;' in placement)
check('hover selector state', 'UIElement.IsMouseOverProperty' in placement)
check('selected selector state', 'ComboBoxItem.IsSelectedProperty' in placement)
check('route selector styled', 'IndicatorAddressSelectorStyle.Apply(_targets);' in route)
check('workspace picker styled', 'IndicatorAddressSelectorStyle.Apply(_targets);' in picker)

check('placement options current pane', 'currentPlacePaneId' in independent)
check('placement options current source', 'currentConnectedPricePaneId' in independent)
check('current indicator workspace listed', 'Current indicator workspace' in independent)
check('workspace TickScript properties addresses', 'new TickScriptIndicatorSettingsWindow(entry, current, options)' in independent)
check('workspace built-in properties addresses', 'new BuiltInIndicatorSettingsWindow(current, options)' in independent)
check('chart TickScript properties addresses', 'new TickScriptIndicatorSettingsWindow(entry, current, options)' in chart_script)
check('chart built-in properties addresses', 'new BuiltInIndicatorSettingsWindow(current, options)' in builtin)
check('chart to workspace TickScript move', 'TryCreateTickScriptIndicatorWorkspace(entry, moved, placement' in chart_script)
check('chart to workspace built-in move', 'TryCreateBuiltInIndicatorWorkspace(moved, placement' in builtin)
check('workspace to chart TickScript move', 'Moved {entry.Name} to Chart' in independent)
check('workspace to chart built-in move', 'Moved {updated.DisplayName} to Chart' in independent)
check('workspace source change', 'SetIndicatorWorkspaceSource(context, source, placement.SyncWithPriceChart)' in independent)
check('workspace disconnect option', 'DisconnectIndicatorWorkspaceSource(context)' in independent)
check('built-in reset retains placement', 'PlacementResult = replacement.PlacementResult;' in builtin_settings)

check('workspace controls bottom', 'VerticalAlignment = VerticalAlignment.Bottom' in workspace)
check('workspace controls bottom margin', 'Margin = new Thickness(0, 0, 3, 3)' in workspace)
check('bottom hover region', 'point.Y >= Math.Max(0, ActualHeight - 32)' in workspace)

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

# Only intended source files may differ from Step 1A.
expected = {
    'src/TickLab.App/Controls/WorkspaceSurfaceControl.cs',
    'src/TickLab.App/MainWindow.BuiltInIndicators.cs',
    'src/TickLab.App/MainWindow.IndependentIndicators.cs',
    'src/TickLab.App/MainWindow.xaml.cs',
    'src/TickLab.App/Windows/BuiltInIndicatorSettingsWindow.cs',
    'src/TickLab.App/Windows/IndicatorPlacementModels.cs',
    'src/TickLab.App/Windows/IndicatorRouteWindow.cs',
    'src/TickLab.App/Windows/WorkspacePartitionPickerWindow.cs',
    'src/TickLab.App/MainWindow.xaml',
    'src/TickLab.App/TickLab.App.csproj',
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

# MT5 and FileBridge protection.
protected=[]
for pattern in ['src/TickLab.App/Gateway/FileBridge/**/*.cs','mt5/**/*','MQL5/**/*']:
    protected.extend(p for p in base.glob(pattern) if p.is_file())
for bp in protected:
    rel=bp.relative_to(base)
    np=root/rel
    same=np.exists() and hashlib.sha256(bp.read_bytes()).digest()==hashlib.sha256(np.read_bytes()).digest()
    check('protected ' + rel.as_posix(), same)

print(f'V1.13.0.31 RESTART STEP 2 CHECKS PASSED: {passed}')
print(f'V1.13.0.31 RESTART STEP 2 CHECKS FAILED: {len(failed)}')
for item in failed:
    print('FAIL:', item)
sys.exit(1 if failed else 0)
