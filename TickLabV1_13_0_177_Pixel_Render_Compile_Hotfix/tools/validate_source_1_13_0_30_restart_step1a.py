from __future__ import annotations

from pathlib import Path
import hashlib
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
passed = 0
failed: list[str] = []


def check(name: str, condition: bool) -> None:
    global passed
    if condition:
        passed += 1
    else:
        failed.append(name)


def text(rel: str) -> str:
    return (ROOT / rel).read_text(encoding="utf-8")

# Version identity.
csproj = text("src/TickLab.App/TickLab.App.csproj")
main_xaml = text("src/TickLab.App/MainWindow.xaml")
check("version metadata", "<Version>1.13.0.30</Version>" in csproj)
check("assembly metadata", "<AssemblyVersion>1.13.0.30</AssemblyVersion>" in csproj)
check("file metadata", "<FileVersion>1.13.0.30</FileVersion>" in csproj)
check("window title", "TickLab v1.13.0.30 — Restart Step 1A" in main_xaml)

# Exact scope: vertical/free-drag sync from source chart to independent indicator panes.
chart = text("src/TickLab.App/Controls/CandleChartControl.cs")
chart_contexts = text("src/TickLab.App/MainWindow.ChartContexts.cs")
independent = text("src/TickLab.App/MainWindow.IndependentIndicators.cs")
stack = text("src/TickLab.App/Controls/IndicatorPaneStackControl.cs")
custom_plot = text("src/TickLab.App/Controls/TickScriptIndicatorPlotControl.cs")
builtin_plot = text("src/TickLab.App/Controls/BuiltInIndicatorPlotControl.cs")

check("vertical sync action event", "event Action<ChartVerticalSyncAction>? VerticalSyncAction" in chart)
check("vertical zoom action type", "ChartVerticalSyncActionKind.Zoom" in chart)
check("vertical pan action type", "ChartVerticalSyncActionKind.Pan" in chart)
check("vertical reset action type", "ChartVerticalSyncActionKind.Reset" in chart)
check("wheel both-axis zoom publishes vertical gesture", "ChartVerticalSyncAction.Zoom(factor, verticalAnchor)" in chart)
check("vertical-only zoom publishes gesture", "ChartVerticalSyncAction.Zoom(factor, anchorRatio)" in chart)
check("free plot drag publishes relative vertical pan", "ChartVerticalSyncAction.Pan(shiftRatio)" in chart)
check("price scale drag publishes relative vertical zoom", "ChartVerticalSyncAction.Zoom(factor, 0.5)" in chart)
check("fit vertical publishes reset", "VerticalSyncAction?.Invoke(ChartVerticalSyncAction.Reset())" in chart)
check("chart event wired once", chart_contexts.count("chart.VerticalSyncAction += action =>") == 1)
check("vertical sync routed only to independent panes", "SyncIndependentIndicatorWorkspacesVertical(context, action)" in chart_contexts)
check("connected source filter", "context.ConnectedPricePaneId == source.PaneId && context.SyncWithPriceChart" in independent)
check("stack receives linked vertical action", "context.Stack.ApplyLinkedVerticalAction(action)" in independent)
check("stack ignores vertical link when sync off", "if (!_syncWithPriceChart)\n            return;" in stack)
check("TickScript vertical action", "public void ApplyLinkedVerticalAction(ChartVerticalSyncAction action)" in custom_plot)
check("built-in vertical action", "public void ApplyLinkedVerticalAction(ChartVerticalSyncAction action)" in builtin_plot)
check("indicator values keep own range", "double anchorValue = _manualMaximum - anchorRatio * span" in custom_plot and "double anchorValue = _manualMaximum - anchorRatio * span" in builtin_plot)
check("fixed-range override default remains off", "public bool AllowManualFixedRangeOverride { get; set; }" in builtin_plot)
check("fixed-range override enabled only for independent stack", "AllowManualFixedRangeOverride = _independentWorkspaceMode" in stack)
check("existing horizontal viewport sync retained", "SyncIndependentIndicatorWorkspacesViewport(context, snapshot)" in chart_contexts)
check("existing horizontal wheel route retained", "source.Chart.ApplyLinkedHorizontalWheel(delta, ratio)" in independent)
check("existing horizontal pan route retained", "source.Chart.PanHorizontalBySlots(slots)" in independent)
check("Sync OFF still local wheel", "else\n            ApplyLocalWheel" in stack)
check("Sync OFF still local pan", "else\n            ApplyLocalPan" in stack)
check("connection not cleared by sync", "ConnectedPricePaneId = null" not in stack)

# Protected source files must remain byte-for-byte equal to Restart Step 1.
for manifest_name in [
    "BASELINE_PROTECTED_SOURCE_1_13_0_29_STEP1A.sha256.txt",
    "BASELINE_PROTECTED_MT5_1_13_0_29_STEP1A.sha256.txt",
]:
    manifest = ROOT / manifest_name
    for line in manifest.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        expected, rel = line.split("  ", 1)
        path = ROOT / rel.removeprefix("./")
        actual = hashlib.sha256(path.read_bytes()).hexdigest() if path.exists() else "MISSING"
        check(f"protected {rel}", actual == expected)

# Parse XAML/project XML.
for path in sorted(list((ROOT / "src").rglob("*.xaml")) + list((ROOT / "src").rglob("*.csproj"))):
    try:
        ET.parse(path)
        check(f"XML parse {path.relative_to(ROOT)}", True)
    except Exception:
        check(f"XML parse {path.relative_to(ROOT)}", False)

# Lightweight delimiter validation for all C# files.
def stripped(source: str) -> str:
    out: list[str] = []
    i = 0
    state = "code"
    quote = ""
    while i < len(source):
        c = source[i]
        d = source[i + 1] if i + 1 < len(source) else ""
        if state == "code":
            if c == "/" and d == "/": state = "line"; out.extend("  "); i += 2; continue
            if c == "/" and d == "*": state = "block"; out.extend("  "); i += 2; continue
            if c == "@" and d == '"': state = "verbatim"; out.extend("  "); i += 2; continue
            if c in ('"', "'"): state = "string"; quote = c; out.append(" "); i += 1; continue
            out.append(c); i += 1; continue
        if state == "line":
            if c == "\n": state = "code"; out.append("\n")
            else: out.append(" ")
            i += 1; continue
        if state == "block":
            if c == "*" and d == "/": state = "code"; out.extend("  "); i += 2
            else: out.append("\n" if c == "\n" else " "); i += 1
            continue
        if state == "string":
            if c == "\\": out.extend("  "); i += 2
            elif c == quote: state = "code"; out.append(" "); i += 1
            else: out.append("\n" if c == "\n" else " "); i += 1
            continue
        if state == "verbatim":
            if c == '"' and d == '"': out.extend("  "); i += 2
            elif c == '"': state = "code"; out.append(" "); i += 1
            else: out.append("\n" if c == "\n" else " "); i += 1
    return "".join(out)

for path in sorted((ROOT / "src").rglob("*.cs")):
    value = stripped(path.read_text(encoding="utf-8", errors="ignore"))
    check(f"brace balance {path.relative_to(ROOT)}", value.count("{") == value.count("}"))
    check(f"parenthesis balance {path.relative_to(ROOT)}", value.count("(") == value.count(")"))
    check(f"bracket balance {path.relative_to(ROOT)}", value.count("[") == value.count("]"))

print(f"V1.13.0.30 RESTART STEP 1A CHECKS PASSED: {passed}")
print(f"V1.13.0.30 RESTART STEP 1A CHECKS FAILED: {len(failed)}")
for item in failed:
    print(f"FAIL: {item}")
sys.exit(1 if failed else 0)
