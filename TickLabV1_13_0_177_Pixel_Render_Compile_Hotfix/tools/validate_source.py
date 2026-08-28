#!/usr/bin/env python3
from __future__ import annotations
from pathlib import Path
import hashlib, re, sys, xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / 'src' / 'TickLab.App'
MT5 = ROOT / 'MT5'
passed=[]; failed=[]

def check(cond,label): (passed if cond else failed).append(label)
def read(p): return p.read_text(encoding='utf-8-sig')
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()

def balanced(path: Path) -> bool:
    s=read(path); pairs={')':'(',']':'[','}':'{'}; stack=[]; state='code'; verb=False; raw=0; i=0
    while i<len(s):
        ch=s[i]; nx=s[i+1] if i+1<len(s) else ''
        if state=='line':
            if ch=='\n': state='code'
        elif state=='block':
            if ch=='*' and nx=='/': state='code'; i+=1
        elif state=='char':
            if ch=='\\': i+=1
            elif ch=="'": state='code'
        elif state=='string':
            if raw:
                if s.startswith('"'*raw,i): i+=raw-1; state='code'; raw=0
            elif verb:
                if ch=='"' and nx=='"': i+=1
                elif ch=='"': state='code'; verb=False
            else:
                if ch=='\\': i+=1
                elif ch=='"': state='code'
        else:
            if ch=='/' and nx=='/': state='line'; i+=1
            elif ch=='/' and nx=='*': state='block'; i+=1
            elif ch=="'": state='char'
            elif s.startswith('"""',i): state='string'; raw=3; i+=2
            elif ch=='@' and nx=='"': state='string'; verb=True; i+=1
            elif ch=='$' and nx=='"': state='string'; i+=1
            elif ch=='"': state='string'
            elif ch in '([{': stack.append(ch)
            elif ch in ')]}':
                if not stack or stack.pop()!=pairs[ch]: return False
        i+=1
    return state not in {'block','char','string'} and not stack

required=['TickLabV1_13_0_12.sln','RELEASE_NOTES_1_13_0_12.txt','BUILD_FIX_README_1_13_0_12.txt','FIRST_TEST_CHECKLIST_1_13_0_12.txt','VERSION.txt','README.txt','DRAWING_TOOLS_GUIDE.txt','BRIDGE_SETUP.txt','FIRST_TEST_CHECKLIST.txt','LIVE_COMPATIBILITY_AUDIT.txt','MT5_SOURCE_SHA256.txt','Clean-Restore-Build.cmd']
for n in required: check((ROOT/n).is_file(),f'package file exists: {n}')
project=read(APP/'TickLab.App.csproj'); solution=read(ROOT/'TickLabV1_13_0_12.sln'); cmd=read(ROOT/'Clean-Restore-Build.cmd')
check('<Version>1.13.0.12</Version>' in project,'project version is 1.13.0.12')
check('<AssemblyVersion>1.13.0.12</AssemblyVersion>' in project,'assembly version is 1.13.0.12')
check('<FileVersion>1.13.0.12</FileVersion>' in project,'file version is 1.13.0.12')
check('src\\TickLab.App\\TickLab.App.csproj' in solution,'solution references TickLab.App')
check('TickLabV1_13_0_12.sln' in cmd,'build script targets v1.13.0.12')
check('<UseWindowsForms>true</UseWindowsForms>' not in project,'no WinForms dependency introduced')
props=read(ROOT/'Directory.Build.props')
check('CS4014' in props,'audited intentional fire-and-forget warning is contained')


# Protected sources
hashes={'TickLabLiveBridge_V300.mq5':'a3cf40709e7bf6ea68baa9cfbb40a84b6fb906488b6e24f2a2577bf62a9eec3e','TickLabHistoryBridge_V305.mq5':'1d17ee364e0425b26c4cff2c69f333c20bd748b9e7ba000816dd915005695ccf','TickLabCandleMarkerExchange_V109.mq5':'f8db1d0d23fad1a96412cbaff7b9a4206344fb9ad67e60fe2c9e77cf2e003bd4'}
for n,h in hashes.items():
    p=MT5/n; check(p.is_file(),f'protected source exists: {n}'); check(p.is_file() and sha(p)==h,f'protected source unchanged: {n}')

# Parse XAML and resolve handlers
all_cs='\n'.join(read(p) for p in APP.rglob('*.cs'))
events='Click|Loaded|Closing|TextChanged|SelectionChanged|MouseLeftButtonDown|MouseLeftButtonUp|MouseRightButtonDown|MouseRightButtonUp|PreviewMouseRightButtonUp|PreviewMouseLeftButtonDown|PreviewMouseLeftButtonUp|PreviewMouseMove|PreviewMouseWheel|PreviewKeyDown|Drop|Deactivated|SizeChanged|Checked|Unchecked|ValueChanged|LostKeyboardFocus|DragOver'
for x in APP.rglob('*.xaml'):
    try: ET.parse(x); check(True,f'valid XAML: {x.relative_to(ROOT)}')
    except ET.ParseError as e: check(False,f'valid XAML: {x.relative_to(ROOT)}: {e}')
    text=read(x)
    names=re.findall(r'\bx:Name="([^"]+)"',text)
    check(len(names)==len(set(names)),f'unique x:Name values: {x.relative_to(ROOT)}')
    for unsupported in ('CharacterSpacing=', 'PlaceholderText=', 'StackPanel Spacing='):
        check(unsupported not in text,f'WPF-compatible XAML token absent ({unsupported}): {x.relative_to(ROOT)}')
    for h in re.findall(rf'\b(?:{events})="([A-Za-z_]\w*)"',text):
        check(re.search(r'\b'+re.escape(h)+r'\s*\(',all_cs) is not None,f'XAML handler exists: {h}')
for p in APP.rglob('*.cs'): check(balanced(p),f'balanced C#: {p.relative_to(ROOT)}')

# Duplicate method-signature guard for CandleChartControl partial files.
method_pattern=re.compile(r'^\s*(?:public|private|protected|internal)\s+(?:static\s+|async\s+|virtual\s+|override\s+|sealed\s+|new\s+|partial\s+|unsafe\s+)*(?:[\w<>,?\[\].]+)\s+(\w+)\s*\(([^)]*)\)\s*(?:=>|\{|$)')
method_signatures={}
for source_path in sorted((APP/'Controls').glob('CandleChartControl*.cs')):
    for line_no,line in enumerate(read(source_path).splitlines(),1):
        match=method_pattern.match(line)
        if not match: continue
        name, raw_params=match.groups(); param_types=[]
        if raw_params.strip():
            for raw_param in raw_params.split(','):
                tokens=raw_param.strip().split('=')[0].strip().split()
                modifiers=[]
                while tokens and tokens[0] in {'ref','out','in','params','this'}:
                    modifiers.append(tokens.pop(0))
                param_types.append(' '.join(modifiers + (tokens[:-1] if len(tokens)>=2 else tokens)))
        signature=f"{name}({','.join(param_types)})"
        method_signatures.setdefault(signature,[]).append(f"{source_path.name}:{line_no}")
duplicate_methods={signature:locations for signature,locations in method_signatures.items() if len(locations)>1}
check(not duplicate_methods,'no duplicate CandleChartControl method signatures')


app=read(APP/'App.xaml'); mainx=read(APP/'MainWindow.xaml'); main=read(APP/'MainWindow.xaml.cs')
chart=read(APP/'Controls'/'CandleChartControl.cs'); drawing=read(APP/'Controls'/'CandleChartControl.Drawing.cs')
models=read(APP/'Core'/'Drawing'/'DrawingModels.cs'); catalog=read(APP/'Core'/'Drawing'/'DrawingToolCatalog.cs')
settingsx=read(APP/'Windows'/'DrawingSettingsWindow.xaml'); settings=read(APP/'Windows'/'DrawingSettingsWindow.xaml.cs')
favx=read(APP/'Windows'/'DrawingFavoritesWindow.xaml'); fav=read(APP/'Windows'/'DrawingFavoritesWindow.xaml.cs')
prefs=read(APP/'Settings'/'UserPreferences.cs'); store=read(APP/'Settings'/'SettingsStore.cs')
chart_settings_model=read(APP/'Core'/'Settings'/'ChartSettings.cs')
chart_settings_x=read(APP/'Windows'/'ChartSettingsWindow.xaml')
chart_settings_code=read(APP/'Windows'/'ChartSettingsWindow.xaml.cs')
template_store=read(APP/'Settings'/'ChartTemplateStore.cs')
template_dialogs=read(APP/'Windows'/'ChartTemplateDialogs.cs')
theme_manager=read(APP/'Settings'/'ApplicationThemeManager.cs')
chart_appearance=read(APP/'MainWindow.ChartAppearance.cs')
chart_contexts=read(APP/'MainWindow.ChartContexts.cs')
workspace_surface=read(APP/'Controls'/'WorkspaceSurfaceControl.cs')
media=read(APP/'Core'/'Drawing'/'DrawingMediaCatalog.cs')
pickerx=read(APP/'Windows'/'DrawingMediaPickerWindow.xaml')
picker=read(APP/'Windows'/'DrawingMediaPickerWindow.xaml.cs')
archive=read(APP/'Gateway'/'FileBridge'/'CanonicalTickArchiveStore.cs')
persist=read(APP/'Gateway'/'FileBridge'/'PersistentHistoryStore.cs')
bridge_client=read(APP/'Gateway'/'FileBridge'/'Mt5FileBridgeClient.cs')
check('out double levelFillOpacity' in settings and 'Math.Clamp(levelFillOpacity, 0, 1)' in settings,'level fill opacity local does not shadow drawing fill opacity')
check('out TickScriptIndicatorResult? result' in main and 'result is null' in main,'indicator dictionary lookup is null-safe')
check('out NativeBoundaryEntry? entry' in persist and 'entry is not null' in persist,'native boundary lookup is null-safe')
check(bridge_client.count('out Mt5ConnectorSummary? cached') >= 2 and bridge_client.count('cached is null') >= 2,'cached connector lookups are null-safe')


# Unified multi-window shell
features={
 'Unified workspace and timeframe bar':'single compact top workspace bar',
 '<RowDefinition Height="52"/>':'compact unified top bar',
 '<RowDefinition Height="38"/>':'slim chart/window bottom dock',
 'NewChartButton':'new detachable chart command',
 'TimeframeScrollViewer':'timeframes moved to the top bar',
 'ChartWindowTabsPanel':'bottom chart/window selector',
 'MT5 Disconnected':'connection status moved to bottom-right',
 'DrawingToolbarColumn" Width="56"':'full-height centered icon drawing rail',
 'RightWorkspaceColumn" Width="310"':'resizable right workspace',
 'DrawingCategoryPaletteBorder':'adjacent scrollable tool window',
 'InlineInspectorTitleText':'live drawing inspector',
 'InlineInspectorLevelsPanel':'live fib-level inspector',
 'InlineDrawingFavoritesPanel" Visibility="Collapsed"':'old fixed favorites strip removed',
}
for f,l in features.items(): check(f in mainx,f'v1.13.0.12 shell: {l}')
for f,l in {
 'DrawingToolbarScrollUpButton':'drawing rail scroll-up control',
 'DrawingToolbarScrollDownButton':'drawing rail scroll-down control',
 'DrawingPaletteSplitter':'resizable adjacent tool window',
 'RightWorkspaceSplitter':'draggable right workspace divider',
 'RightWorkspaceToggleButton':'collapsible right workspace',
}.items(): check(f in mainx,f'v1.13.0.9 layout: {l}')
for f,l in {'WindowBrush':'unified window palette','DrawingRailButton':'compact rail controls','DarkToolbarButton':'compact top actions','PART_Thumb':'custom slim scrollbars','TextRenderingMode':'high quality text rendering'}.items(): check(f in app or f in mainx,f'premium theme: {l}')
check('RefreshInlineDrawingInspector' in main,'dynamic inspector refresh connected')
check('OpenDrawingCategoryPalette' in main and 'DrawingCategoryPaletteRowsPanel' in mainx,'category rail opens integrated tool list')
check('Width = 40' in main and 'Height = 40' in main,'category buttons use compact 40px geometry')
check('CreateCategoryIcon(category, 22' in main,'large vector category icons used')
check('DrawingFavoritesWindow' in main and 'DragDrop.DoDragDrop' in fav,'free draggable favorites bar retained')
check('Width="650" Height="500"' in settingsx,'advanced settings remains compact')

# Detachable chart-window system
detachedx=read(APP/'Windows'/'DetachedChartWindow.xaml')
detached=read(APP/'Windows'/'DetachedChartWindow.xaml.cs')
for token,label in {
 'WindowStyle="None"':'detached chart has its own compact frame',
 'ChartNumberText':'number appears in detached chart top-left',
 'MinimizeButton_Click':'chart-only minimize control',
 'MaximizeButton_Click':'chart-only maximize/restore control',
 'CloseButton_Click':'chart-only close control',
 'controls:CandleChartControl':'price chart includes internal price/time scales',
 'HostedContent':'generic price/indicator/tool/EA host',
}.items(): check(token in detachedx or token in detached,f'detached chart: {label}')
for token,label in {
 'CreateWorkspaceChart':'new chart creates a dockable or floating pane',
 'SyncDetachedChartWindows':'floating and docked charts receive live updates',
 'RefreshWorkspaceTabs':'bottom dock switches workspaces',
 'AllocateLowestPaneId':'closed chart numbers are reused safely',
}.items(): check(token in main,f'multi-window manager: {label}')
check('EnsureVisible' in fav and 'VirtualScreen' in fav,'favorites toolbar is clamped to all connected screens')
check('Favorites hidden. Press Favorites again to restore it' in main,'favorites button toggles and restores lost toolbar')

# Persistent multi-workspace architecture
workspace_surface=read(APP/'Controls'/'WorkspaceSurfaceControl.cs')
workspace_main=read(APP/'MainWindow.Workspaces.cs')
workspace_prefs=read(APP/'Settings'/'WorkspacePreferences.cs')
detached_workspace_x=read(APP/'Windows'/'DetachedWorkspaceWindow.xaml')
detached_workspace=read(APP/'Windows'/'DetachedWorkspaceWindow.xaml.cs')
decisions=read(APP/'Windows'/'WorkspaceDecisionDialog.cs')
chart_pane=read(APP/'Controls'/'ChartPaneControl.cs')
for token,label in {
 'WorkspaceTabsPanel':'bottom workspace page tabs',
 'AddWorkspaceButton':'add workspace command',
 'DivideWorkspaceButton':'workspace division command',
 'WorkspacePageHost':'central workspace host',
 'DrawingBrushButton':'normal brush rail button',
}.items(): check(token in mainx,f'workspace shell: {label}')
for token,label in {
 'layoutCount is 1 or 2 or 3 or 4 or 6':'supported partition layouts',
 'Text = partitionId.ToString':'empty partition number',
 'BorderThickness = new Thickness(0.6)':'thin solid partition border',
 'e.ClickCount == 2':'double-click partition selection',
 'PaneDragFormat':'magnetic pane drag format',
 'ToggleMaximize':'partition maximize and restore',
 'WorkspaceDetachRequested':'hover workspace detach handle',
 'WorkspaceMinimizeRequested':'docked workspace minimize control',
 'WorkspaceMaximizeRequested':'docked workspace maximize control',
 'WorkspaceCloseRequested':'docked workspace confirmed close control',
 'Opacity = 0':'workspace handle hidden by default',
}.items(): check(token in workspace_surface,f'workspace surface: {label}')
for token,label in {
 'AllocateLowestPaneId':'lowest available pane ID reuse',
 'AllocateLowestWorkspaceId':'lowest available workspace ID reuse',
 'AttachPaneToTarget':'partition attach/swap/replace flow',
 'ConvertFloatingPaneToWorkspace':'floating chart to workspace conversion',
 'BuildAttachTargets':'right-click workspace/partition attach choices',
 'DetachWorkspace':'whole-workspace detach',
 'MinimizeWorkspaceInTickLab':'docked workspace minimizes without closing',
 'MaximizeWorkspaceInTickLab':'docked workspace fills TickLab frame',
 'CaptureWorkspacePagePreferences':'workspace page persistence',
 'CaptureFloatingPanePreferences':'floating pane persistence',
 'CloseWorkspaceWindowsForApplicationExit':'controlled detached workspace shutdown',
}.items(): check(token in workspace_main,f'workspace manager: {label}')
for token,label in {
 'WorkspaceStateInitialized':'workspace migration gate',
 'PreferredWorkspaceLayout':'selected layout persistence',
 'Workspaces':'workspace collection persistence',
 'FloatingPanes':'floating pane persistence',
}.items(): check(token in prefs,f'workspace preferences: {label}')
check('IsMinimized' in workspace_prefs,'workspace preferences: workspace minimized-state persistence')
check('FileOptions.WriteThrough' in store and 'stream.Flush(true)' in store,'power-cut-safe write-through settings save')
check('File.Replace' in store and '_backupSettingsPath' in store,'atomic settings replacement with backup')
check('ReadPreferences(_backupSettingsPath)' in store,'backup settings recovery')
check('WorkspaceCloseDecision.DetachItems' in decisions and 'WorkspaceCloseDecision.CloseAll' in decisions,'workspace close confirmation supports detach or close all')
check('Attach to TickLab' in detached_workspace and 'WindowState.Maximized' in detached_workspace,'detached workspace attach and full-monitor maximize')
check('Chart = new CandleChartControl' in chart_pane,'independent chart pane host exists')
check('private const double RightMargin = 48' in chart,'thin right price scale')
check('DrawingFavoritesButton.BorderBrush = Brushes.Transparent' in main,'favorites extra marker removed')
check('new[] { "pen", "brush", "highlighter" }' in workspace_main,'Brush palette exposes Pen, Brush and Highlighter')

# Independent chart contexts and code editor
contexts=read(APP/'MainWindow.ChartContexts.cs')
code_panel=read(APP/'MainWindow.CodeEditor.cs')
for token,label in {
 'private CandleChartControl CandleChart => ActiveChartContext.Chart':'active chart routing',
 'SelectTimeframeForActiveChartAsync':'selected-chart timeframe switching',
 'HandleChartDrawingWorkspaceChanged':'same-symbol drawing synchronization',
 'SetDrawingToolForAllCharts':'drawing tool available on every chart',
 'ShowIndicatorsForActiveChart':'indicator attached to selected chart',
 'ActivateWorkspacePane':'chart activation by pane selection',
}.items(): check(token in contexts,f'chart contexts: {label}')
for token,label in {
 'CodeEditorPanelBorder':'separate right-side code panel',
 'CodeEditorColumn':'slideable code editor column',
 'InlineCodeEditorBox':'embedded TickScript source editor',
 'InlineCodeCompileButton_Click':'compile and save from side panel',
 'OpenFullCodeEditorButton_Click':'full editor remains available',
}.items(): check(token in mainx or token in code_panel,f'code panel: {label}')
check('PaneActivated' in workspace_surface and 'ActivateWorkspacePane(request.Pane.Id)' in workspace_main,'clicking a pane selects its chart context')
check('Width = 24' in workspace_surface and 'CompactCaptionButton' in detachedx and 'CompactCaptionButton' in detached_workspace_x,'modern compact chart/workspace controls')
check('PrimaryCandleChart' in mainx and 'x:Name="CandleChart"' not in mainx,'primary chart routed through active context property')
check('SyncWorkspaceChartPanes' in workspace_main and 'foreach (WorkspacePaneHandle pane' not in workspace_main[workspace_main.find('private void SyncWorkspaceChartPanes'):workspace_main.find('private bool AttachIndicatorPaneToSelectedPartition')],'chart synchronization no longer broadcasts timeframe data to every pane')

# Premium chart rendering
for token,label in {'CandleGapPixels = 3.0':'three-pixel candle gap','MaximumHorizontalVisibleCandles = 1_500':'1,500-candle zoom-out cap','Resolve appearance once per frame':'per-frame candle material reuse','SnapStrokeCoordinate':'crisp candle pixel alignment','slotWidth < CandleGapPixels + 1.0':'compressed-renderer transition'}.items(): check(token in chart,f'chart renderer: {label}')
check('UseLayoutRounding="True"' in mainx and 'SnapsToDevicePixels="True"' in mainx,'high-DPI layout rounding enabled')

# Drawing catalog completeness
ids=re.findall(r'Add\("([^"]+)"',catalog)
check(len(ids)>=95,'at least 95 drawing/media tools registered')
check(len(ids)==len(set(ids)),'drawing tool IDs unique')
for t in ['trend-line','arrow','ray','extended-line','horizontal-line','vertical-line','parallel-channel','fib-retracement','trend-fib-extension','pitchfork','gann-box','rectangle','circle','brush','highlighter','text','price-label','xabcd-pattern','elliott-impulse','long-position','short-position','date-price-range','icons','stickers','emojis']:
    check(f'Add("{t}"' in catalog,f'tool registered: {t}')

# Full editability / level customization
for token,label in {'string FillColor = ""':'independent level fill colour','double FillOpacity = -1':'independent level fill opacity','LevelFillColorButton_Click':'zone colour picker','FillOpacityText':'zone opacity editor','AddLevelButton_Click':'add levels','RemoveLevelButton_Click':'remove levels','MoveLevelUpButton_Click':'reorder levels','MoveLevelDownButton_Click':'reorder levels','ResetLevelsButton_Click':'restore levels','SetDefaultTemplateButton_Click':'default template','DeleteTemplateButton_Click':'delete template','PreviewChanged':'live settings preview'}.items():
    check(token in models or token in settings or token in settingsx,f'drawing settings: {label}')
check('zoneColor' in drawing and 'zoneOpacity' in drawing,'fib zones render independent colour and opacity')
check('DefaultFibonacciLevels' in catalog and '#F05261' in catalog and '#22C97A' in catalog and '#846EF6' in catalog,'professional multicolour fib defaults')

# Pen/highlighter smoothing
for token,label in {
 'SimplifyFreehandAnchors':'freehand point simplification',
 'DistanceToSegment':'RDP simplification distance',
 'SmoothFreehandAnchors':'multi-pass point smoothing',
 'DrawSmoothFreehand':'smooth Bezier freehand',
 'QuadraticBezierTo':'stable midpoint quadratic curves',
 'PenLineJoin.Round':'rounded pen joins',
 'PenLineCap.Round':'rounded pen caps',
 'highlighter ? 0.62 : 0.42':'natural freehand simplification tolerance',
 'highlighter ? 5 : 4':'extra highlighter smoothing pass',
 'freehandTool?.Geometry == DrawingGeometryKind.Highlighter ? 0.24 : 0.34':'live pointer low-pass filtering',
 'Distance(_freehandLastAcceptedPoint.Value, finalPoint) >= 0.5':'freehand reaches the released pointer endpoint',
}.items(): check(token in drawing,f'freehand renderer: {label}')


# v1.13.0.12 natural interaction and media checks
for token,label in {
 'MainWindow_PreviewMouseLeftButtonDown':'outside-click category close handler',
 'IsWithinVisualTree':'inside/outside visual-tree detection',
 'DrawingCategoryPaletteBorder.Visibility = Visibility.Collapsed':'category window closes after tool selection',
 'RightWorkspaceHandle_PreviewMouseLeftButtonDown':'right workspace drag begins',
 'RightWorkspaceHandle_PreviewMouseMove':'right workspace follows pointer',
 'RightWorkspaceHandle_PreviewMouseLeftButtonUp':'right workspace drag completes',
 'SetRightWorkspaceDragWidth':'right workspace drag width clamp',
}.items(): check(token in main,f'natural workspace: {label}')
check('PreviewMouseLeftButtonDown="MainWindow_PreviewMouseLeftButtonDown"' in mainx,'main window receives outside-click preview')
check('IsWithinVisualTree(source, DrawingToolbarPanel)' not in main,'toolbar clicks also dismiss an unselected category flyout')
check('Cursor="SizeWE"' in mainx and 'RightWorkspaceHandle_PreviewMouseMove' in mainx,'arrow handle is pointer-draggable')
for token,label in {
 'Point A placed. Release Shift if desired, then click point B.':'measure A then B instruction',
 'Measurement complete. Click elsewhere to clear it':'temporary measure clear instruction',
 'bool measurementComplete':'completed measure clear state',
 'if (_measureDragging && _measureStartAnchor is not null)':'measure remains active after Shift release',
 'DrawingTimestampToTimelineSlot(end.StartUnix)':'continuous future-space measure bars',
}.items(): check(token in drawing,f'measurement: {label}')
for token,label in {
 'DrawingTimelineSlotToTimestamp':'future timeline to timestamp conversion',
 'DrawingTimestampToTimelineSlot':'future timestamp to timeline conversion',
 'GetDrawingSlotSeconds':'native interval extrapolation',
 'SafeTimestampOffset':'overflow-safe future timestamp',
 'ConstrainPointToPlot':'drawing remains inside visible chart plot',
}.items(): check(token in drawing,f'future-space drawing: {label}')
icon_keys=re.findall(r'Icon\("([^"]+)"',media); sticker_keys=re.findall(r'Sticker\("([^"]+)"',media); emoji_keys=re.findall(r'Emoji\("([^"]+)"',media)
icon_count=len(icon_keys); sticker_count=len(sticker_keys); emoji_count=len(emoji_keys)
check(len(icon_keys)==len(set(icon_keys)),'coloured media: icon keys unique')
check(len(sticker_keys)==len(set(sticker_keys)),'coloured media: sticker keys unique')
check(len(emoji_keys)==len(set(emoji_keys)),'coloured media: emoji keys unique')
check(all(re.fullmatch(r'#[0-9A-Fa-f]{6}',value) for value in re.findall(r'"(#[0-9A-Fa-f]+)"',media)),'coloured media: all palette values are six-digit hex colours')
check(icon_count >= 50,'coloured media: at least 50 original icons')
check(sticker_count >= 48,'coloured media: at least 48 original stickers')
check(emoji_count >= 60,'coloured media: at least 60 original emoji')
check(icon_count + sticker_count + emoji_count >= 158,'coloured media: at least 158 total items')
check('TLME' in media and 'TryDecode' in media,'coloured media: persistent scalable token format')
for token,label in {
 'Background="{StaticResource WindowBrush}"':'picker uses TickLab theme',
 'MediaSearchBox':'picker search field',
 'VerticalScrollBarVisibility="Auto"':'scrollable media folders',
 'Original TickLab artwork':'original artwork disclosure',
}.items(): check(token in pickerx,f'coloured media picker: {label}')
check('CreateStickerPreview' in picker and 'CreateBadgePreview' in picker,'coloured media picker builds native coloured previews')
check('DrawTickLabMedia' in drawing and 'DrawCartoonFace' in drawing,'chart renders coloured vector media and cartoon faces')
check('v1.13.0.5' not in '\n'.join(read(p) for p in APP.rglob('*') if p.is_file() and p.suffix in {'.cs','.xaml','.csproj'}),'current source contains no stale visible v1.13.0.5 labels')

# v1.13.0.12 per-chart appearance, themes and templates
for token,label in {
 'ChartBackgroundColor':'chart background colour',
 'ChartTextColor':'general chart text colour',
 'PriceScaleBackgroundColor':'price-scale background colour',
 'PriceScaleTextColor':'price-scale text colour',
 'TimeScaleBackgroundColor':'time-scale background colour',
 'TimeScaleTextColor':'time-scale text colour',
 'GridOpacity':'grid transparency',
 'GridThickness':'grid pixel thickness',
 'UpBodyColor':'bull candle body colour',
 'UpBorderColor':'bull candle border colour',
 'UpWickColor':'bull candle wick colour',
 'DownBodyColor':'bear candle body colour',
 'DownBorderColor':'bear candle border colour',
 'DownWickColor':'bear candle wick colour',
 'PriceLineStyle':'price-line style',
 'SpreadLineStyle':'spread-line style',
 'ShowSpreadFill':'spread fill toggle',
 'ShowCandleCountdown':'candle countdown toggle',
 'CrosshairLineStyle':'crosshair style',
 'CrosshairLabelBackgroundColor':'crosshair label background',
 'SelectedCandleColor':'selected-candle colour',
 'HistoryBoundaryColor':'history-boundary colour',
 'LatestButtonColor':'go-live button colour',
}.items(): check(token in chart_settings_model,f'per-chart appearance model: {label}')
for token,label in {
 'Application theme':'theme chooser',
 'Live preview · OK saves · Cancel restores':'preview contract',
 'Use confirmed settings as the default for future charts':'future-chart defaults',
 'Save preset':'preset saving',
 'Load preset':'preset loading',
 'Import':'preset import',
 'Export':'preset export',
}.items(): check(token in chart_settings_x,f'chart settings UI: {label}')
for token,label in {
 'PreviewChanged':'chart-only live preview event',
 'ThemePreviewChanged':'theme live preview event',
 'CopyTargetChartIds':'explicit copy targets',
 'ChartTemplateStore':'preset storage',
 'DrawingColorPickerWindow':'colour picker',
 'LoadControls(ChartSettings.Default)':'reset chart defaults',
}.items(): check(token in chart_settings_code,f'chart settings behavior: {label}')
for token,label in {
 'File.Replace':'atomic template replacement',
 'chart-templates.json':'persistent template store',
 'Import':'template import',
 'Export':'template export',
}.items(): check(token in template_store,f'template store: {label}')
for token,label in {
 'Save Template…':'right-click save template',
 'Load Template…':'right-click load template',
 'Delete Template…':'right-click delete template',
 'ChartSettingsRequested':'right-click chart settings',
}.items(): check(token in chart,f'chart context menu: {label}')
check('Are you sure you want to delete the template' in chart_appearance,'template deletion requires explicit confirmation')
check('ApplicationThemeManager.Apply' in main and 'ApplicationTheme' in prefs,'application theme persists')
check('UpdateResource("WindowBrush", light ? "#F4F4F4" : "#000000")' in theme_manager,'dark theme is complete black and light theme is high contrast')
check('element is CandleChartControl or TickChartControl' in theme_manager,'application theme never overwrites chart colours')
check('UpdatePaneIdentity' in workspace_surface and 'IdentityText' in workspace_surface,'chart badge supports ID, symbol and timeframe')
check('UpdateWorkspacePaneIdentity' in chart_contexts,'chart identity updates after symbol/timeframe changes')
check('root.PreviewMouseLeftButtonDown' in workspace_surface and 'PaneActivated?.Invoke' in workspace_surface,'single click silently activates a chart')
check('if (e.ClickCount >= 2)' in chart and chart.find('if (e.ClickCount >= 2)') < chart.find('HandleDrawingMouseLeftDown'),'scale double-click priority precedes drawing selection')
check('FitVertical();' in chart and 'FitHorizontal();' in chart,'price and time scale reset commands retained')
check('Mt5ServerClock.ServerNowUnix(ServerUtcOffsetMinutes)' in chart,'countdown uses MT5 broker-server time')
check('context.Chart.Settings = settings' in main,'live chart preview is chart-local')
check('context.Settings = window.Settings' in main and 'SaveWorkspace();' in main,'only confirmed chart settings persist')
check('context.Chart.Settings = original' in main,'Cancel/X restores original chart appearance')
check('v1.13.0.8' not in '\n'.join(read(p) for p in APP.rglob('*') if p.is_file() and p.suffix in {'.cs','.xaml','.csproj'}),'current source contains no stale visible v1.13.0.8 labels')

# v1.13.0.12 visual stability regression checks
picker_code=read(APP/'Windows'/'DrawingColorPickerWindow.xaml.cs')
picker_xaml=read(APP/'Windows'/'DrawingColorPickerWindow.xaml')
for token,label in {
 'BasicColours':'paint-style basic colour palette',
 'RedBox':'RGB red input',
 'GreenBox':'RGB green input',
 'BlueBox':'RGB blue input',
 'CurrentColorBorder':'current colour preview',
 'NewColorBorder':'new colour preview',
 'PaletteSwatchButton':'swatches preserve their real colour',
}.items(): check(token in picker_code or token in picker_xaml,f'professional colour picker: {label}')
check('if (themeChanged)' in chart_settings_code,'chart colour preview does not re-theme every window')
check('contentHost.Content is null && e.ClickCount == 2' in workspace_surface,'occupied chart double-click is not intercepted by partition selection')
check('if (_activePricePaneId == paneId)' in chart_contexts,'already-active chart click avoids workspace rebuild')
check('VisibleCount = Math.Clamp(viewport.VisibleCount, 1, 1_500)' in store,'saved viewport respects 1,500-candle limit')
check('TargetName="ButtonBorder" Property="RenderTransform"' in app and '<TranslateTransform Y="1"/>' in app,'buttons use subtle pressed impression')
check('#171717' in app and '#3B3B3B' in app,'button hover uses neutral professional tones')


# v1.13.0.12 exact colour mapping and theme isolation
theme_scope=read(APP/'Settings'/'ThemeColorScope.cs')
check('PreserveExactColorsProperty' in theme_scope and 'FrameworkPropertyMetadataOptions.Inherits' in theme_scope,'exact-colour scope inherits through swatch visual trees')
check('ThemeColorScope.GetPreserveExactColors(element)' in theme_manager,'theme engine skips exact-colour subtrees')
check('Tag = entry' in picker_code and 'Background = new SolidColorBrush(entry.Color)' in picker_code,'palette display and click value share one immutable entry')
check('sender is Button { Tag: PaletteColour entry }' in picker_code,'palette click reads the displayed palette entry directly')
check('RestoreExactPaletteVisuals' in picker_code,'post-load swatch restoration guard exists')
check('settings:ThemeColorScope.PreserveExactColors="True"' in picker_xaml,'current/new colour previews are theme protected')
check(picker_code.count('P("') >= 70,'Paint-style palette contains at least 70 exact colours')
check('ThemeColorScope.SetPreserveExactColors(swatch, true)' in chart_settings_code,'chart-settings swatches are theme protected')

# v1.13.0.12 persistent alerts and canonical tick replay
alerts_replay=read(APP/'MainWindow.AlertsReplay.cs')
alert_models=read(APP/'Core'/'Alerts'/'AlertModels.cs')
alert_store=read(APP/'Settings'/'AlertStore.cs')
alert_editor=read(APP/'Windows'/'AlertEditorWindow.cs')
alert_manager=read(APP/'Windows'/'AlertManagerWindow.cs')
alert_toast=read(APP/'Windows'/'AlertToastWindow.cs')
replay_engine=read(APP/'Core'/'Replay'/'MarketReplayEngine.cs')
replay_window=read(APP/'Windows'/'MarketReplayWindow.cs')
persistent_history=read(APP/'Gateway'/'FileBridge'/'PersistentHistoryStore.cs')
for path,label in {
 APP/'Core'/'Alerts'/'AlertModels.cs':'alert models',
 APP/'Settings'/'AlertStore.cs':'atomic alert store',
 APP/'Windows'/'AlertEditorWindow.cs':'alert editor',
 APP/'Windows'/'AlertManagerWindow.cs':'alert manager',
 APP/'Windows'/'AlertToastWindow.cs':'alert toast',
 APP/'Core'/'Replay'/'MarketReplayEngine.cs':'tick replay engine',
 APP/'Windows'/'MarketReplayWindow.cs':'replay controls',
 APP/'MainWindow.AlertsReplay.cs':'alert/replay integration',
}.items(): check(path.is_file(),f'v1.13.0.12 file exists: {label}')
for token,label in {
 'PriceCrossesUp':'price cross-up alert',
 'PriceCrossesDown':'price cross-down alert',
 'SpreadAbove':'spread alert',
 'CandleOpened':'candle-open alert',
 'CandleClosed':'candle-close alert',
 'DrawingCross':'drawing-cross alert',
 'IndicatorCrossesUp':'indicator cross-up alert',
 'OncePerCandleClose':'once-per-close frequency',
}.items(): check(token in alert_models,f'alert model: {label}')
for token,label in {
 'FileOptions.WriteThrough':'write-through alert save',
 'File.Replace':'atomic alert replacement',
 'alerts.json':'persistent alert document',
 '.bak':'alert backup recovery',
}.items(): check(token in alert_store,f'alert persistence: {label}')
for token,label in {
 'Alert log':'alert log tab',
 'Enable / disable':'alert enable control',
 'DeleteRequested':'alert delete action',
 'ClearLogRequested':'clear alert log action',
}.items(): check(token in alert_manager,f'alert manager: {label}')
for token,label in {
 'Add alert…':'drawing context alert command',
 'DrawingAlertRequested':'drawing alert event',
}.items(): check(token in drawing,f'drawing alert integration: {label}')
for token,label in {
 'AlertsButton_Click':'working Alerts top-bar handler',
 'ReplayButton_Click':'working Replay top-bar handler',
}.items(): check(token in mainx and token in all_cs,f'top bar integration: {label}')
for token,label in {
 '0.25×':'quarter-speed replay',
 '50×':'fifty-times replay',
 'Step tick':'single tick stepping',
 'Step candle':'single candle stepping',
 'Use / drag chart line':'draggable marker control',
}.items(): check(token in replay_window,f'replay window: {label}')
for token,label in {
 'MarketReplayEngine':'canonical replay engine integration',
 'ReadTicksForReplay':'raw canonical tick reading',
 'GetTickCoverageForReplay':'strict archive coverage check',
 'Replay unavailable — no tick data exists for the selected period.':'agreed no-tick-data error',
 'TickLabReplay':'solid replay marker source',
 'LoadNextReplayBatchAsync':'bounded replay batching',
 'RefreshAllAppliedIndicators(force: true)':'indicator recalculation from revealed replay data',
 'StopReplay(restoreChart: true)':'live chart restoration',
}.items(): check(token in alerts_replay,f'replay integration: {label}')
check('CanonicalTickCoverage' in archive and 'GetCoverage' in archive,'canonical tick archive exposes exact coverage')
check('ReadTicksForReplay' in persistent_history and 'GetTickCoverageForReplay' in persistent_history,'persistent history exposes replay APIs')
check('IsReplayChart(_activePricePaneId)' in main,'live chart writer is isolated during replay')
check('CloseAlertsAndReplayForShutdown' in main,'alert/replay windows close safely on shutdown')
check('EvaluateLiveAlerts(ActiveChartContext)' in main,'live chart updates evaluate alerts')
check('v1.13.0.12 — Persistent Alerts + Tick Replay' in mainx,'main window title identifies alert/replay release')
check('v1.13.0.10' not in '\n'.join(read(p) for p in APP.rglob('*') if p.is_file() and p.suffix in {'.cs','.xaml','.csproj'}),'current source contains no stale visible v1.13.0.10 labels')
check('Additional chart types are not part of this release.' in read(ROOT/'README.txt'),'release scope excludes unfinished chart types')

# Existing interactions retained
for token,label in {'DrawingMeasureButton':'measure control','DrawMeasurementOverlay':'measure result overlay','CancelActiveDrawingToolOrMeasurement':'right-click/escape cancellation','TryGetDrawingMagnetSnap':'shared magnet snap','EffectiveMagnetMode':'Ctrl magnet inversion','DrawingQuickEditBar':'compact live edit bar','QuickFillOpacitySlider':'live fill opacity','BuildDrawingContextMenu':'full drawing menu','ExportDrawingWorkspaceJson':'drawing persistence','UndoDrawingChange':'drawing undo','RedoDrawingChange':'drawing redo'}.items(): check(token in mainx or token in main or token in drawing,f'interaction retained: {label}')
check('dc.DrawRectangle(null, selectionPen, bounds)' not in drawing,'blue selection rectangle remains removed')

# Settings/history stability retained
for token,label in {'JsonNumberHandling.AllowNamedFloatingPointLiterals':'non-finite JSON handling','UserPreferences safePreferences = Sanitize(preferences)':'pre-save sanitation','NormalizePosition':'window geometry sanitation','NormalizeSize':'window size sanitation'}.items(): check(token in store,f'workspace stability: {label}')
for token,label in {'HistoricalSourceOverlapsRequest':'historical tick range filtering','LiveSourceOverlapsRequest':'live tick range filtering','if (cancellationToken.IsCancellationRequested)':'cooperative tick cancellation','if (stateChanged)':'tick progress checkpoints'}.items(): check(token in archive,f'tick stability: {label}')
check('cancellationToken.ThrowIfCancellationRequested()' not in archive,'tick sync does not throw routine cancellation')
check('TimeSpan.FromSeconds(15)' in main,'startup foreground tick budget retained')
check('DrawingDocuments = new[] { CandleChart.ExportDrawingWorkspaceJson() }' in main,'drawing workspace saved')
check('DrawingFavoritesWindowVisible' in prefs and 'DrawingFavoritesWindowLeft' in prefs,'favorites geometry persists')
check('_ownedDrawingWindowsReady = true;' in main and 'PresentationSource.FromVisual(this) is null' in main,'owned window startup guard retained')

print(f'STATIC CHECKS PASSED: {len(passed)}')
print(f'STATIC CHECKS FAILED: {len(failed)}')
if failed:
    for x in failed: print('FAIL:',x)
    sys.exit(1)

# v1.13.0.12 history/replay/alert/floating-chart repair checks
history_window=read(APP/'Windows'/'HistoryImportProgressWindow.xaml.cs')
history_xaml=read(APP/'Windows'/'HistoryImportProgressWindow.xaml')
replay=read(APP/'MainWindow.AlertsReplay.cs')
chart_control=read(APP/'Controls'/'CandleChartControl.cs')
detached=read(APP/'Windows'/'DetachedChartWindow.xaml.cs')
detached_xaml=read(APP/'Windows'/'DetachedChartWindow.xaml')
maincs=read(APP/'MainWindow.xaml.cs')
chartcontexts=read(APP/'MainWindow.ChartContexts.cs')
check('CompletionButton' in history_xaml and 'CompletionAcknowledged' in history_window,'history success confirmation has OK acknowledgement')
check('TimeSpan.FromSeconds(12)' in maincs,'tick archive foreground finalization is bounded to 12 seconds')
check('ContinueTickArchiveIndexingInBackground' in maincs,'unfinished tick indexing resumes in background')
check('onlySegmentKey: replaySegmentKey' in replay and 'includeHistorical: true' in replay,'replay indexes selected imported tick quarter on demand')
check('PriceAlertRequested' in chart_control and 'Add Alert Here' in chart_control and 'CreatePriceAlert' in replay,'chart right-click price alert is wired')
check('HostContextMenuItemsProvider' in chart_control and 'BuildHostContextMenuItems' in detached,'floating chart right-click attach menu is wired')
check('ChartDragGrip' in detached_xaml and 'WindowMenuButton_Click' in detached_xaml,'floating chart has explicit drag grip and attach menu button')
check('chart.PriceAlertRequested +=' in chartcontexts,'all registered charts route price alerts')

print('All TickLab v1.13.0.12 history completion, alerts, replay and floating-chart checks passed.')
