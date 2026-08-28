from pathlib import Path
import re, sys, hashlib, xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
src=root/'src'/'TickLab.App'
checks=[]
def check(cond,label): checks.append((bool(cond),label))

# XML parse
for p in src.rglob('*.xaml'):
    try: ET.parse(p); check(True,f'XAML parses: {p.name}')
    except Exception as e: check(False,f'XAML parses: {p.name}: {e}')

settings=(src/'Core/Settings/ChartSettings.cs').read_text()
chart=(src/'Controls/CandleChartControl.cs').read_text()
charttypes=(src/'Controls/CandleChartControl.ChartTypes.cs').read_text()
alerts=(src/'Controls/CandleChartControl.Alerts.cs').read_text()
mainalerts=(src/'MainWindow.AlertsReplay.cs').read_text()
maintypes=(src/'MainWindow.ChartTypes.cs').read_text()
mainx=(src/'MainWindow.xaml').read_text()
models=(src/'Core/Alerts/AlertModels.cs').read_text()
popup=(src/'Windows/AlertTriggeredWindow.cs').read_text()
contexts=(src/'MainWindow.ChartContexts.cs').read_text()

check('1.13.0.13' in mainx and '<Version>1.13.0.13</Version>' in (src/'TickLab.App.csproj').read_text(),'version references')
check('ChartTypeButton' in mainx and 'ChartTypeButton_Click' in maintypes,'top chart-type selector')
expected=['Candles','HollowCandles','Bars','VolumeCandles','Line','LineWithMarkers','StepLine','Area','HlcArea','Baseline','Columns','HighLow']
for name in expected:
    check(name in settings,f'chart enum {name}')
    check(f'ChartVisualType.{name}' in chart or f'ChartVisualType.{name}' in maintypes,f'chart type wired {name}')
for method in ['DrawBodyCandles','DrawOhlcBars','DrawCloseLine','DrawAreaChart','DrawHlcAreaChart','DrawBaselineChart','DrawColumnsChart','DrawHighLowChart']:
    check(method in charttypes and method in chart,f'renderer {method}')
check('AlertLines' in chart and 'DrawAlertLines' in chart and 'HitTestAlertLine' in alerts,'draggable alert overlay')
check('AlertLineMoved' in chart and 'MoveAlertLine' in mainalerts and 'chart.AlertLineMoved' in contexts,'alert line persistence event')
check('PriceTouches' in models and 'Market touched' in mainalerts,'touch alert condition')
check('DispatcherTimer' in popup and 'SystemSounds.Exclamation.Play()' in popup and 'ok.Click' in popup,'repeating bell acknowledgement')
check('ClearReplayMarker' in mainalerts and 'Replay ended. Replay line removed.' in mainalerts,'end replay removes line')
check('clearMarker: false' in mainalerts,'replay load preserves start line')
check('ChartType { get; init; }' in settings,'chart type persists in settings')
check('context.Settings = context.Settings with { ChartType = type }' in maintypes,'selected chart only chart type')
check('MaximumHorizontalVisibleCandles = 1_500' in chart and 'CandleGapPixels = 3.0' in chart,'zoom and candle gap retained')

# XAML click handlers exist somewhere in partial sources.
allcs='\n'.join(p.read_text(errors='ignore') for p in src.rglob('*.cs'))
for p in src.rglob('*.xaml'):
    text=p.read_text()
    for handler in re.findall(r'\b(?:Click|Loaded|Closing|PreviewKeyDown|PreviewMouseLeftButtonDown|MouseLeftButtonUp|MouseRightButtonUp|TextChanged|PreviewMouseWheel)="([A-Za-z_][A-Za-z0-9_]*)"',text):
        check(re.search(r'\b'+re.escape(handler)+r'\s*\(',allcs) is not None,f'handler exists: {handler}')

# Gross brace balance
for p in src.rglob('*.cs'):
    text=p.read_text()
    check(text.count('{')==text.count('}'),f'brace balance: {p.name}')

passed=sum(ok for ok,_ in checks); failed=[label for ok,label in checks if not ok]
print(f'STATIC CHECKS PASSED: {passed}')
print(f'STATIC CHECKS FAILED: {len(failed)}')
for label in failed: print('FAIL:',label)
if failed: sys.exit(1)
print('All TickLab v1.13.0.13 alert, replay-line and standard chart-type checks passed.')
