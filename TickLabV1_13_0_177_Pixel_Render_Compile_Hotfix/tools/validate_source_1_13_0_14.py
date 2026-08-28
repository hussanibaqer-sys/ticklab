from pathlib import Path
import hashlib
import re
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
src = root / 'src' / 'TickLab.App'
checks = []

def check(condition, label):
    checks.append((bool(condition), label))

# Every XAML file must parse.
for path in src.rglob('*.xaml'):
    try:
        ET.parse(path)
        check(True, f'XAML parses: {path.relative_to(root)}')
    except Exception as exc:
        check(False, f'XAML parses: {path.relative_to(root)}: {exc}')

all_cs = '\n'.join(path.read_text(errors='ignore') for path in src.rglob('*.cs'))
for path in src.rglob('*.xaml'):
    text = path.read_text(errors='ignore')
    handlers = re.findall(
        r'\b(?:Click|Loaded|Closing|PreviewKeyDown|PreviewMouseLeftButtonDown|MouseLeftButtonUp|MouseRightButtonUp|TextChanged|PreviewMouseWheel)="([A-Za-z_][A-Za-z0-9_]*)"',
        text)
    for handler in handlers:
        check(re.search(r'\b' + re.escape(handler) + r'\s*\(', all_cs) is not None,
              f'XAML handler exists: {handler}')

# Gross source integrity.
for path in src.rglob('*.cs'):
    text = path.read_text(errors='ignore')
    check(text.count('{') == text.count('}'), f'Brace balance: {path.relative_to(root)}')
    check('\x00' not in text, f'No NUL bytes: {path.relative_to(root)}')

settings = (src / 'Core/Settings/ChartSettings.cs').read_text()
builder = (src / 'Core/Market/SyntheticChartBuilder.cs').read_text()
chart = (src / 'Controls/CandleChartControl.cs').read_text()
chart_types = (src / 'Controls/CandleChartControl.ChartTypes.cs').read_text()
main_types = (src / 'MainWindow.ChartTypes.cs').read_text()
main_alerts = (src / 'MainWindow.AlertsReplay.cs').read_text()
popup = (src / 'Windows/AlertTriggeredWindow.cs').read_text()
bell = (src / 'Core/Alerts/AlertBellPlayer.cs').read_text()
editor = (src / 'Windows/AlertEditorWindow.cs').read_text()
main_code = (src / 'MainWindow.xaml.cs').read_text()
project = (src / 'TickLab.App.csproj').read_text()
main_xaml = (src / 'MainWindow.xaml').read_text()

check('<Version>1.13.0.14</Version>' in project, 'Project version 1.13.0.14')
check('<AssemblyVersion>1.13.0.14</AssemblyVersion>' in project, 'Assembly version 1.13.0.14')
check('<FileVersion>1.13.0.14</FileVersion>' in project, 'File version 1.13.0.14')
check('TickLab v1.13.0.14' in main_xaml, 'Main window version title')
check((root / 'TickLabV1_13_0_14.sln').exists(), 'Renamed solution exists')

standard = ['Candles','HollowCandles','Bars','VolumeCandles','Line','LineWithMarkers','StepLine','Area','HlcArea','Baseline','Columns','HighLow']
synthetic = ['HeikinAshi','Renko','LineBreak','Kagi','PointAndFigure','Range']
for name in standard + synthetic:
    check(re.search(r'\b' + name + r'\b', settings) is not None, f'Chart enum: {name}')
    check(f'ChartVisualType.{name}' in main_types, f'Chart menu wiring: {name}')
    check(name == 'Candles' or f'ChartVisualType.{name}' in chart, f'Chart renderer switch: {name}')

for method in ['BuildHeikinAshi','BuildRenko','BuildLineBreak','BuildKagi','BuildPointAndFigure','BuildRange']:
    check(f'private static IReadOnlyList<Candle> {method}' in builder, f'Synthetic algorithm: {method}')

for property_name in ['SyntheticBoxSizePoints','RangeBarSizePoints','KagiReversalPoints','LineBreakCount','PointAndFigureReversalBoxes','RenkoReversalBoxes']:
    check(property_name in settings, f'Persistent synthetic setting: {property_name}')
    check(property_name in builder or property_name in main_types, f'Synthetic setting used: {property_name}')

check('SyntheticChartBuilder.Build(_sourceCandles' in chart, 'Original source candles kept separate from visual series')
check('SyntheticChartBuilder.IsSynthetic(_settings.ChartType)' in chart, 'Synthetic live refresh regeneration')
check('effectiveAppendedCount' in chart, 'Synthetic viewport append accounting')
check('DrawKagiChart' in chart and 'DrawKagiChart' in chart_types, 'Kagi renderer connected')
check('DrawPointAndFigureChart' in chart and 'DrawPointAndFigureChart' in chart_types, 'Point & Figure renderer connected')
check('SyntheticChartSettingsWindow' in main_types and (src/'Windows/SyntheticChartSettingsWindow.cs').exists(), 'Synthetic settings dialog connected')
check('RefreshAllAppliedIndicators(force: true)' in main_types, 'Indicators refresh after type/settings change')
check('CandleChart.Candles.ToArray()' in main_code, 'Indicators evaluate displayed chart series')

check('BuildBellWave' in bell and 'PlayLooping' in bell and 'SoundPlayer' in bell, 'Built-in WAV bell implementation')
check('SystemSounds.Exclamation' not in popup, 'Popup no longer depends on Windows Exclamation sound')
check('AlertBellPlayer' in popup and '_bellPlayer.PlayLooping()' in popup, 'Popup starts repeating built-in bell')
check('_bellPlayer?.Stop()' in popup and '_bellPlayer?.Dispose()' in popup, 'Popup stops bell on close/OK')
check('AlertBellPlayer.PlayOnce()' in main_alerts, 'Sound-only alerts use built-in bell')
check('Test bell' in editor and 'AlertBellPlayer.PlayOnce()' in editor, 'Alert editor Test bell button')
check('ok.Click += (_, _) => Close();' in popup, 'OK acknowledges alert popup')

check('MaximumHorizontalVisibleCandles = 1_500' in chart, '1,500-candle zoom cap retained')
check('CandleGapPixels = 3.0' in chart, '3-pixel candle gap retained')
check('ClearReplayMarker' in main_alerts, 'Replay line cleanup retained')
check('AlertLineMoved' in all_cs, 'Draggable alert line persistence retained')

expected_mt5 = {
    'TickLabCandleMarkerExchange_V109.mq5': 'f8db1d0d23fad1a96412cbaff7b9a4206344fb9ad67e60fe2c9e77cf2e003bd4',
    'TickLabHistoryBridge_V305.mq5': '1d17ee364e0425b26c4cff2c69f333c20bd748b9e7ba000816dd915005695ccf',
    'TickLabLiveBridge_V300.mq5': 'a3cf40709e7bf6ea68baa9cfbb40a84b6fb906488b6e24f2a2577bf62a9eec3e',
}
for name, expected in expected_mt5.items():
    path = root / 'MT5' / name
    actual = hashlib.sha256(path.read_bytes()).hexdigest() if path.exists() else ''
    check(actual == expected, f'Protected MT5 source unchanged: {name}')

check(not any(path.name in {'bin','obj'} for path in root.rglob('*') if path.is_dir()), 'No bin/obj build artifacts packaged')

passed = sum(ok for ok, _ in checks)
failed = [label for ok, label in checks if not ok]
print(f'STATIC CHECKS PASSED: {passed}')
print(f'STATIC CHECKS FAILED: {len(failed)}')
for label in failed:
    print('FAIL:', label)
if failed:
    sys.exit(1)
print('TickLab v1.13.0.14 reliable alert bell and Step 4 synthetic chart validation passed.')
