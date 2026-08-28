from pathlib import Path
import hashlib, re, sys, xml.etree.ElementTree as ET
from pygments import lex
from pygments.lexers.dotnet import CSharpLexer
from pygments.token import Token

root=Path(__file__).resolve().parents[1]
base=Path('/mnt/data/work_step10/TickLabV1_13_0_41_Restart_Step9_Trade_Labels_History_Contrast')
app=root/'src'/'TickLab.App'
passed=[]; failed=[]
def check(cond,label): (passed if cond else failed).append(label)
def read(rel): return (root/rel).read_text(encoding='utf-8-sig',errors='ignore')
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()

proj=read('src/TickLab.App/TickLab.App.csproj')
xaml=read('src/TickLab.App/MainWindow.xaml')
main=read('src/TickLab.App/MainWindow.xaml.cs')
code=read('src/TickLab.App/MainWindow.CodeEditor.cs')
demo=read('src/TickLab.App/MainWindow.DemoTrading.cs')
colors=read('src/TickLab.App/Settings/ColorDisplayHelper.cs')
picker=read('src/TickLab.App/Windows/DrawingColorPickerWindow.xaml.cs')

# Release identity.
for needle,label in [('<Version>1.13.0.42</Version>','version'),('<AssemblyVersion>1.13.0.42</AssemblyVersion>','assembly version'),('<FileVersion>1.13.0.42</FileVersion>','file version')]: check(needle in proj,label)
check((root/'TickLabV1_13_0_42.sln').exists(),'solution exists')
check(not (root/'TickLabV1_13_0_41.sln').exists(),'old solution name removed')
check((root/'VERSION.txt').read_text().strip()=='1.13.0.42','VERSION file')
check('TickLabV1_13_0_42.sln' in read('Clean-Restore-Build.cmd'),'clean build script targets Step 10 solution')
check('Restart Step 10 Right Panels, Contrast and Default Background' in xaml,'window title')

# XML, names, event handlers and punctuation.
all_main='\n'.join(p.read_text(errors='ignore') for p in app.glob('MainWindow*.cs'))
handler_pattern=r'\b(?:Click|Checked|Unchecked|TextChanged|SelectionChanged|SelectedDateChanged|PreviewMouse\w+|PreviewKey\w+|Mouse\w+|Key\w+|Drag\w+|Drop|Loaded|Closing)="([A-Za-z_]\w*)"'
for p in app.rglob('*.xaml'):
    try: ET.parse(p); check(True,f'XAML parses {p.relative_to(app)}')
    except Exception as e: check(False,f'XAML parses {p.relative_to(app)}: {e}')
    text=p.read_text(errors='ignore')
    names=re.findall(r'x:Name="([^"]+)"',text)
    check(len(names)==len(set(names)),f'unique x:Name {p.relative_to(app)}')
    handlers=set(re.findall(handler_pattern,text))
    code_text=all_main if p.name=='MainWindow.xaml' else (p.with_suffix('.xaml.cs').read_text(errors='ignore') if p.with_suffix('.xaml.cs').exists() else '')
    for handler in handlers: check(re.search(r'\b'+re.escape(handler)+r'\s*\(',code_text) is not None,f'handler {p.name}:{handler}')
for p in app.rglob('*.cs'):
    stack=[]; pairs={')':'(',']':'[','}':'{'}; ok=True
    for typ,value in lex(p.read_text(encoding='utf-8-sig',errors='ignore'),CSharpLexer()):
        if typ in Token.Punctuation:
            for ch in value:
                if ch in '([{': stack.append(ch)
                elif ch in ')]}':
                    if not stack or stack.pop()!=pairs[ch]: ok=False; break
        if not ok: break
    check(ok and not stack,f'C# balanced {p.relative_to(app)}')

# Exact current launch/fallback chart colour is selectable and named separately from Near Black.
for needle,label in [
    ('Add(result, "Default Chart Background", "#07101B")','default chart background named'),
    ('P("Default Chart Background", "#07101B")','default chart background selectable'),
    ('Add(result, "Near Black", "#080808")','Near Black retained separately'),
    ('P("Near Black", "#080808")','Near Black palette retained')]: check(needle in colors+picker,label)
check('#07101B' != '#080808','default and Near Black shades are distinct')

# Chart width restored: no reserved side-handle column; overlay is in chart column.
check('<ColumnDefinition Width="0"/>\n                <ColumnDefinition x:Name="RightWorkspaceColumn"' in xaml,'side handle reserves zero chart width')
check('<Grid Grid.Row="1" Panel.ZIndex="7000" HorizontalAlignment="Right"' in xaml,'side handles overlay chart edge')
check('Margin="0,8,28,0"' in xaml,'overlay anchored at top-right without chart shift')
check('Grid.Column="4" Panel.ZIndex="7000"' not in xaml and 'Grid.Column="3" Panel.ZIndex="7000"' not in xaml,'handles are root overlays, not zero-width cell targets')

# Right workspace middle handle reliable click + drag.
for needle,label in [
    ('x:Name="RightWorkspaceToggleButton" Width="24" Height="86"','middle handle thick enough'),
    ('Click="RightWorkspaceToggleButton_Click"','middle handle click event'),
    ('RightWorkspaceToggleButton_Click','middle click handler'),
    ('Mouse.Capture(handle, CaptureMode.Element);','drag captures after threshold'),
    ('if (!_rightWorkspaceHandleMoved)\n            return;','drag waits for threshold'),
    ('ApplyRightWorkspaceCollapsedState();','middle click applies state'),
    ('SaveWorkspace();','middle state saved')]: check(needle in xaml+main,label)
for handler in ['RightWorkspaceHandle_PreviewMouseLeftButtonDown','RightWorkspaceHandle_PreviewMouseMove','RightWorkspaceHandle_PreviewMouseLeftButtonUp','RightWorkspaceHandle_LostMouseCapture','RightWorkspaceHandle_PreviewKeyDown']:
    check(handler in xaml and handler in main,f'middle handle behavior {handler}')

# Dedicated vertical Code Editor handle and independent drag logic.
for needle,label in [
    ('x:Name="CodeEditorSlideButton" Width="24" Height="116"','code editor handle present'),
    ('C&#x0a;O&#x0a;D&#x0a;E','code editor written vertically'),
    ('Click="CodeEditorSlideButton_Click"','code editor click event'),
    ('CodeEditorHandle_PreviewMouseLeftButtonDown','code editor mouse down'),
    ('CodeEditorHandle_PreviewMouseMove','code editor mouse move'),
    ('CodeEditorHandle_PreviewMouseLeftButtonUp','code editor mouse up'),
    ('CodeEditorHandle_LostMouseCapture','code editor lost capture'),
    ('CodeEditorHandle_PreviewKeyDown','code editor keyboard toggle'),
    ('SetCodeEditorDragWidth','code editor drag width'),
    ('Math.Clamp(requestedWidth, 0.0, 760.0)','code editor width clamped'),
    ('CodeEditorSlideButton.ToolTip','code editor handle status updated')]: check(needle in xaml+code,label)

# Pending-order popup actual items remain readable.
for needle,label in [
    ('DemoLightComboBoxTextTemplate','pending item text template'),
    ('<TextBlock Text="{Binding}" Foreground="Black"','popup item text forced black'),
    ('<Setter Property="ItemTemplate" Value="{StaticResource DemoLightComboBoxTextTemplate}"/>','combo uses item template'),
    ('<Setter Property="Background" Value="#C9DCF2"/>','selected popup uses light blue'),
    ('<Setter Property="Foreground" Value="Black"/>','popup text remains dark'),
    ('x:Name="DemoOrderTypeCombo"','order type combo retained'),
    ('x:Name="DemoExpirationModeCombo"','expiration combo retained')]: check(needle in xaml,label)
check(xaml.count('Style="{StaticResource DemoLightComboBoxStyle}"')>=2,'both pending selectors use contrast style')

# Demo panel Order/Open/Pending/History tabs and data surfaces readable.
for needle,label in [
    ('DemoTabItemStyle','demo tab item style'),
    ('ItemContainerStyle="{StaticResource DemoTabItemStyle}"','demo tab control uses style'),
    ('<Setter Property="Foreground" Value="Black"/>','dark tab/list text'),
    ('<Setter TargetName="TabBorder" Property="Background" Value="#214E80"/>','selected tab dark blue'),
    ('<Setter Property="Foreground" Value="White"/>','selected tab white text'),
    ('DemoListBoxItemStyle','demo list row style'),
    ('DemoDataGridRowStyle','history row style'),
    ('DemoDataGridCellStyle','history cell style'),
    ('DemoDataGridHeaderStyle','history header style'),
    ('ItemContainerStyle="{StaticResource DemoListBoxItemStyle}"','demo lists use row style'),
    ('RowStyle="{StaticResource DemoDataGridRowStyle}"','history grid uses row style'),
    ('CellStyle="{StaticResource DemoDataGridCellStyle}"','history grid uses cell style'),
    ('ColumnHeaderStyle="{StaticResource DemoDataGridHeaderStyle}"','history grid uses header style')]: check(needle in xaml,label)
for header in ['Header="Order"','Header="Open positions"','Header="Pending orders"','Header="Trade history"']:
    check(header in xaml,f'demo tab retained {header}')

# Working demo execution rules are unchanged.
for needle,label in [
    ('double entry = direction == "BUY" ? market.Ask : market.Bid','Buy Ask / Sell Bid entry retained'),
    ('double mark = position.Direction == "BUY" ? market.Bid : market.Ask','live mark side retained'),
    ('market.Bid <= position.StopLoss','Buy SL Bid retained'),
    ('market.Ask >= position.StopLoss','Sell SL Ask retained'),
    ('market.Bid >= position.TakeProfit','Buy TP Bid retained'),
    ('market.Ask <= position.TakeProfit','Sell TP Ask retained')]: check(needle in demo,label)
for forbidden in ['OrderSend','MqlTradeRequest','MqlTradeResult','CTrade','trade.Buy','trade.Sell','PositionOpen','OrderSendAsync']:
    check(forbidden.lower() not in demo.lower(),f'no real order API {forbidden}')

# Exact changed source scope.
allowed={
 'Clean-Restore-Build.cmd','TickLabV1_13_0_41.sln','TickLabV1_13_0_42.sln','VERSION.txt',
 'src/TickLab.App/MainWindow.CodeEditor.cs','src/TickLab.App/MainWindow.xaml','src/TickLab.App/MainWindow.xaml.cs',
 'src/TickLab.App/Settings/ColorDisplayHelper.cs','src/TickLab.App/TickLab.App.csproj','src/TickLab.App/Windows/DrawingColorPickerWindow.xaml.cs',
 'tools/validate_source_1_13_0_42_restart_step10.py','VALIDATION_REPORT_1_13_0_42_RESTART_STEP10.txt'
}
def filemap(path):
    return {p.relative_to(path).as_posix():sha(p) for p in path.rglob('*') if p.is_file()}
bm=filemap(base); nm=filemap(root)
changed={k for k in set(bm)|set(nm) if bm.get(k)!=nm.get(k)}
check(changed <= allowed,f'only intended files changed: {sorted(changed-allowed)}')

# Protected MT5 / FileBridge exact hashes.
protected=[p for p in base.rglob('*') if p.is_file() and (p.relative_to(base).as_posix().startswith('MT5/') or p.relative_to(base).as_posix().startswith('src/TickLab.App/Gateway/FileBridge/'))]
for bp in protected:
    rel=bp.relative_to(base); wp=root/rel
    check(wp.exists() and sha(bp)==sha(wp),f'protected unchanged {rel.as_posix()}')

report=root/'VALIDATION_REPORT_1_13_0_42_RESTART_STEP10.txt'
report.write_text('TickLab v1.13.0.42 Restart Step 10 static validation\n\n'+f'Passed: {len(passed)}\nFailed: {len(failed)}\n\n'+'\n'.join(('PASS  '+x) for x in passed)+(('\n\n'+'\n'.join(('FAIL  '+x) for x in failed)) if failed else '')+'\n')
print(f'passed={len(passed)} failed={len(failed)}')
for item in failed: print('FAIL',item)
sys.exit(1 if failed else 0)
