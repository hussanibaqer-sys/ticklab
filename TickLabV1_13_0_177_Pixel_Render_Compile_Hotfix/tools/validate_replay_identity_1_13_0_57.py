from pathlib import Path
import re, sys
root=Path(__file__).resolve().parents[1]
app=root/'src'/'TickLab.App'
text=(app/'MainWindow.AlertsReplay.cs').read_text(encoding='utf-8-sig')
proj=(app/'TickLab.App.csproj').read_text(encoding='utf-8-sig')
xaml=(app/'MainWindow.xaml').read_text(encoding='utf-8-sig')
checks=[]
def check(c,n): checks.append((bool(c),n))
check('<Version>1.13.0.57</Version>' in proj,'project version')
check('<AssemblyVersion>1.13.0.57</AssemblyVersion>' in proj,'assembly version')
check('<FileVersion>1.13.0.57</FileVersion>' in proj,'file version')
check((root/'TickLabV1_13_0_57.sln').exists(),'solution renamed')
check((root/'VERSION.txt').read_text().strip()=='1.13.0.57','VERSION.txt')
check('Replay Identity Fix' in xaml,'window title')
load=text[text.index('private async Task LoadReplayAsync'):text.index('private async void StartOrToggleReplay')]
reset=load.index('ResetContextHistoryPaging(context);')
capture=load.index('int identityGeneration = context.IdentityGeneration;')
awaitread=load.index('CanonicalTickReadResult read = await ReadReplayTicksImmediatelyAsync')
compare=load.index('context.IdentityGeneration != identityGeneration')
check(reset < capture < awaitread < compare,'identity guard captured after replay reset and before async read')
check(load.count('int identityGeneration = context.IdentityGeneration;')==1,'single replay identity capture')
check('RenderReplayChart(forceFit: false);' in load,'immediate replay render preserved')
check('ReadReplayTicksImmediatelyAsync(' in load,'instant tick reader preserved')
check('HiddenLiveSourceCandles' in text and 'HiddenLiveDisplayCandles' in text,'hidden live state preserved')
failed=[n for ok,n in checks if not ok]
for ok,n in checks: print(('PASS' if ok else 'FAIL'),n)
print(f'Validation: {len(checks)-len(failed)} passed, {len(failed)} failed')
sys.exit(1 if failed else 0)
