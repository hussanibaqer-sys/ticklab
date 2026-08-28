from __future__ import annotations

from pathlib import Path
import hashlib
import re
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
check("version metadata", "<Version>1.13.0.29</Version>" in csproj)
check("assembly metadata", "<AssemblyVersion>1.13.0.29</AssemblyVersion>" in csproj)
check("window title", "TickLab v1.13.0.29 — Restart Step 1" in main_xaml)

# One simple indicator action.
indicators_xaml = text("src/TickLab.App/Windows/IndicatorsWindow.xaml")
indicators_cs = text("src/TickLab.App/Windows/IndicatorsWindow.xaml.cs")
check("one Properties & Apply action", "Properties &amp; Apply" in indicators_xaml)
check("old workspace button removed", "PlaceInWorkspaceButton" not in indicators_xaml + indicators_cs)
check("old workspace placement events removed", "WorkspacePlacementRequested" not in indicators_cs)

# Placement properties.
placement = text("src/TickLab.App/Windows/IndicatorPlacementModels.cs")
check("Place Address field", 'FormRow("Place Address"' in placement)
check("Connect Address field", 'FormRow("Connect Address"' in placement)
check("Sync field", 'Content = "Sync with Price Chart"' in placement)
check("unconnected option", 'new(null, "Not connected")' in text("src/TickLab.App/MainWindow.IndependentIndicators.cs"))
check("chart placement locks source", "_connectAddress.IsEnabled = false" in placement)
check("workspace connection stays optional", "_connectAddress.IsEnabled = true" in placement)

# Last-click target and exact routing.
independent = text("src/TickLab.App/MainWindow.IndependentIndicators.cs")
workspaces = text("src/TickLab.App/MainWindow.Workspaces.cs")
surface = text("src/TickLab.App/Controls/WorkspaceSurfaceControl.cs")
main_cs = text("src/TickLab.App/MainWindow.xaml.cs")
check("last click target field", "_lastIndicatorPlaceAddress" in independent)
check("empty workspace remembered", "Empty workspace" in independent)
check("chart target remembered", "RememberIndicatorPlacementTarget(request.WorkspaceId" in workspaces)
check("single click empty selection", "contentHost.Content is null && e.ClickCount == 1" in surface)
check("occupied chart click not intercepted", "if (contentHost.Content is null" in surface)
check("destination-aware TickScript handler", "ApplyTickScriptIndicatorFromSelection" in main_cs)
check("destination-aware built-in handler", "ApplyBuiltInIndicatorFromSelection" in main_cs)
check("direct chart path preserved", "ApplyIndicatorToContext(chart, entry, settings.Result)" in independent)
check("built-in chart path preserved", "AddOrReplaceBuiltInIndicator(chart, settings.Result" in independent)
check("workspace independent path", "PlaceConfiguredTickScriptIndicatorInWorkspace" in independent)
check("optional source path", "ConnectConfiguredIndicatorWorkspace" in independent)

# Properties window integration is optional, so existing edit constructors stay valid.
builtin_settings = text("src/TickLab.App/Windows/BuiltInIndicatorSettingsWindow.cs")
tickscript_settings = text("src/TickLab.App/Windows/TickScriptIndicatorSettingsWindow.cs")
check("built-in optional placement tab", "IndicatorPlacementOptions? placementOptions = null" in builtin_settings)
check("built-in placement result", "PlacementResult" in builtin_settings)
check("TickScript optional placement tab", "IndicatorPlacementOptions? placementOptions = null" in tickscript_settings)
check("TickScript placement result", "PlacementResult" in tickscript_settings)

# Sync semantics: connected data is separate from navigation sync.
stack = text("src/TickLab.App/Controls/IndicatorPaneStackControl.cs")
check("visible Sync with Price Chart label", 'Content = "Sync with Price Chart"' in stack)
check("sync off uses local wheel", "else\n            ApplyLocalWheel" in stack)
check("sync off uses local pan", "else\n            ApplyLocalPan" in stack)
check("source remains connected when sync changes", "ConnectedPricePaneId = null" not in stack)
check("right-click/header Connect remains", "ConnectSourceRequested" in stack)
check("time scale remains visible in independent mode", "_timeScale.Visibility = value ? Visibility.Visible" in stack)

# Protected source hashes: every non-step source file must match v1.13.0.28 exactly.
manifest = ROOT / "BASELINE_PROTECTED_SOURCE_1_13_0_28_STEP1.sha256.txt"
for line in manifest.read_text(encoding="utf-8").splitlines():
    if not line.strip():
        continue
    expected, rel = line.split("  ", 1)
    path = ROOT / rel.removeprefix("./")
    actual = hashlib.sha256(path.read_bytes()).hexdigest() if path.exists() else "MISSING"
    check(f"protected source {rel}", actual == expected)

# Parse all XAML and project XML.
for path in sorted(list((ROOT / "src").rglob("*.xaml")) + list((ROOT / "src").rglob("*.csproj"))):
    try:
        ET.parse(path)
        check(f"XML parse {path.relative_to(ROOT)}", True)
    except Exception:
        check(f"XML parse {path.relative_to(ROOT)}", False)

# Lightweight delimiter check for every C# file.
def stripped(source: str) -> str:
    out: list[str] = []
    i = 0
    state = "code"
    quote = ""
    while i < len(source):
        c = source[i]
        d = source[i + 1] if i + 1 < len(source) else ""
        if state == "code":
            if c == "/" and d == "/":
                state = "line"; out.extend("  "); i += 2; continue
            if c == "/" and d == "*":
                state = "block"; out.extend("  "); i += 2; continue
            if c == "@" and d == '"':
                state = "verbatim"; out.extend("  "); i += 2; continue
            if c in ('"', "'"):
                state = "string"; quote = c; out.append(" "); i += 1; continue
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

print(f"V1.13.0.29 RESTART STEP 1 CHECKS PASSED: {passed}")
print(f"V1.13.0.29 RESTART STEP 1 CHECKS FAILED: {len(failed)}")
for item in failed:
    print(f"FAIL: {item}")
sys.exit(1 if failed else 0)
