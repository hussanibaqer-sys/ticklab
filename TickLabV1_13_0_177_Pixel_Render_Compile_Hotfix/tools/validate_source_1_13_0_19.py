#!/usr/bin/env python3
from __future__ import annotations
import hashlib, math, re, subprocess, sys
from pathlib import Path
from lxml import etree

ROOT = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[1]
APP = ROOT / 'src' / 'TickLab.App'
checks = 0
failures=[]
def check(ok,msg):
    global checks
    checks += 1
    if not ok: failures.append(msg)
def text(p): return p.read_text(encoding='utf-8-sig')

project=text(APP/'TickLab.App.csproj')
chart=text(APP/'Controls/CandleChartControl.cs')
types=text(APP/'Controls/CandleChartControl.ChartTypes.cs')
main=text(APP/'MainWindow.xaml')
for token,label in [
('<Version>1.13.0.19</Version>','project version'),
('<AssemblyVersion>1.13.0.19</AssemblyVersion>','assembly version'),
('<FileVersion>1.13.0.19</FileVersion>','file version'),
]: check(token in project,label)
check((ROOT/'TickLabV1_13_0_19.sln').exists(),'solution file')
check(text(ROOT/'VERSION.txt').strip()=='1.13.0.19','VERSION.txt')
check('Stable Viewport Wall Hotfix' in main,'window title')
check('MaximumHorizontalVisibleCandles = 1_500' in chart,'1500 cap')
check('if (minimumSlotWidthPixels < 3)' in types,'compressed transition below 3px')
check('if (requestedPitchPixels < 3.0)' in chart,'layout transition below 3px')
check('CreateStablePlotRect' in chart and 'CreateCrispPlotRect' not in chart,'fixed plot rectangle')
check('if (minimumSlotWidthPixels <= 4)' in types and 'return 3;' in types,'3px micro body')
check('minimumSlotWidthPixels < 7\n            ? 1' in types,'micro wick is 1px')
check('(int)Math.Floor(requestedPitchPixels)' in chart,'integer pitch floors to fit requested bars')
check('_detailedCandlePitchSlotCount = requested;' in chart,'requested visible count retained')

# XAML and basic C# structure.
for p in ROOT.rglob('*.xaml'):
    try: etree.parse(str(p)); check(True,f'XAML {p.relative_to(ROOT)}')
    except Exception as e: check(False,f'XAML {p.relative_to(ROOT)}: {e}')
for p in ROOT.rglob('*.cs'):
    s=text(p); check(s.count('{')==s.count('}'),f'brace balance {p.relative_to(ROOT)}')

# Exact duplicate method signature regression in CandleChartControl partials.
pat=re.compile(r'\b(?:public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?[\w<>,?\[\].]+\s+(\w+)\s*\(([^)]*)\)',re.M)
sigs={}
for p in (APP/'Controls').glob('CandleChartControl*.cs'):
    for name,args in pat.findall(text(p)):
        ts=[]
        for arg in [a.strip() for a in args.split(',') if a.strip()]:
            arg=re.sub(r'\b(?:ref|out|in|params|this)\s+','',arg)
            parts=arg.split('=')[0].strip().split()
            ts.append(' '.join(parts[:-1]) if len(parts)>=2 else (parts[0] if parts else ''))
        sigs.setdefault((name,tuple(ts)),[]).append(p.name)
for sig,files in sigs.items(): check(len(files)==1,f'unique method {sig}: {files}')

# Geometry model retained from v1.13.0.18 plus stable viewport-wall checks.
def pitch(available,count):
    raw=available/max(1,count)
    return 0 if raw < 3.0 else max(3,math.floor(raw))
def body_width(p):
    if p<=4:return 3
    maximum=max(3,p-2)
    preferred=max(3,min(p-3,maximum))
    if preferred%2==0:
        preferred=preferred+1 if preferred+1<=maximum else preferred-1
    return max(3,preferred)

# Reproduce the user's frame: old 7px threshold near 206 => about 1442px plot.
available=1442
check(pitch(available,206)>=7,'206 bars remain normal detailed candles')
for count in (207,250,300,350,400,450,480):
    p=pitch(available,count)
    check(p>=3,f'{count} bars remain body-candle tier')
    w=body_width(p)
    check(w>=3,f'{count} bars have body width >=3')
    check(w%2==1,f'{count} bars body has exact centre column')
    # identical integer slots and body/wick centres for a sample frame
    previous=None
    for slot in range(min(count,64)):
        left=slot*p; right=left+p
        center=math.floor((left+right)/2.0)+0.5
        bl=math.floor(center-w/2.0+0.5); br=bl+w
        if bl<left: br+=left-bl; bl=left
        if br>right: bl-=br-right; br=right
        final=(bl+br)/2.0
        check(br-bl==w,f'uniform width count={count} slot={slot}')
        check(abs(final-(math.floor(final)+0.5))<1e-9,f'pixel centre count={count} slot={slot}')
        if previous is not None: check(bl-previous==p-w,f'uniform gap count={count} slot={slot}')
        previous=br
check(pitch(available,481)==0,'compact renderer begins only beyond 480 bars on test frame')

# Wider/narrower DPI-scaled plots: detailed mode must always guarantee 3px bodies.
for available in range(900,2401,73):
    max_detail=available//3
    for count in (200,300,400,min(1500,max_detail)):
        if count<=max_detail:
            p=pitch(available,count); check(p>=3,f'detail pitch {available}/{count}')
            check(body_width(p)>=3,f'body visible {available}/{count}')
    if max_detail+1<=1500: check(pitch(available,max_detail+1)==0,f'clean transition {available}')


# Viewport wall must remain fixed for every visible count and DPI. The candle
# lattice may leave a small left remainder, but it must not resize layout.Plot.
check('return new Rect(LeftMargin, TopMargin, availableWidth, height);' in chart,
      'stable plot uses full available width')
check('Rect plot = CreateStablePlotRect(width, height);' in chart,
      'layout always uses stable plot')
check('latticeLeftPixels = latticeRightPixels - usedPixels;' in types,
      'integer candle lattice remains internal')
check('viewportLeftPixels' in types and 'viewportRightPixels' in types,
      'renderer separates viewport from lattice')
for width_dip in (640.0, 960.0, 1442.0, 1920.0):
    for scale in (1.0, 1.25, 1.5, 2.0):
        plot_left = 12.0
        plot_right = plot_left + width_dip
        for count in (70, 110, 206, 300, 400, 450, 800, 1500):
            check(plot_left == 12.0, f'fixed plot left {width_dip}/{scale}/{count}')
            check(plot_right == 12.0 + width_dip, f'fixed plot right {width_dip}/{scale}/{count}')
            available_px = round(width_dip * scale)
            p = pitch(available_px, count)
            if p >= 3:
                used = p * count
                lattice_right = round(plot_right * scale)
                lattice_left = lattice_right - used
                check(lattice_right == round(plot_right * scale),
                      f'lattice right anchor {width_dip}/{scale}/{count}')
                check(lattice_left >= round(plot_left * scale),
                      f'lattice stays inside viewport {width_dip}/{scale}/{count}')

# Protected MT5 sources.
for line in text(ROOT/'MT5_SOURCE_SHA256.txt').splitlines():
    if not line.strip(): continue
    expected,rel=line.split(maxsplit=1)
    check(hashlib.sha256((ROOT/rel).read_bytes()).hexdigest()==expected,f'MT5 hash {rel}')

for validator in ['validate_duplicate_method_hotfix_1_13_0_14.py','validate_bridge_write_access_hotfix_1_13_0_14.py']:
    r=subprocess.run([sys.executable,str(ROOT/'tools'/validator),str(ROOT)],capture_output=True,text=True)
    check(r.returncode==0,f'{validator}: {r.stdout}{r.stderr}')

print(f'V1.13.0.19 CHECKS PASSED: {checks-len(failures)}')
print(f'V1.13.0.19 CHECKS FAILED: {len(failures)}')
if failures:
    for f in failures[:100]: print('FAIL:',f)
    raise SystemExit(1)
print('TickLab v1.13.0.19 stable viewport-wall validation passed.')
