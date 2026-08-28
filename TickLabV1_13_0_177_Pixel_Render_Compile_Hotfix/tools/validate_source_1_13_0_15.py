from __future__ import annotations

import hashlib
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
checks: list[tuple[bool, str]] = []


def check(condition: bool, description: str) -> None:
    checks.append((bool(condition), description))


def text(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


def strip_csharp(source: str) -> str:
    result: list[str] = []
    index = 0
    state = "code"
    while index < len(source):
        current = source[index]
        following = source[index + 1] if index + 1 < len(source) else ""
        if state == "code":
            if current == "/" and following == "/":
                state = "line"
                result.extend("  ")
                index += 2
                continue
            if current == "/" and following == "*":
                state = "block"
                result.extend("  ")
                index += 2
                continue
            if current == "@" and following == '"':
                state = "verbatim"
                result.extend("  ")
                index += 2
                continue
            if current == '"':
                state = "string"
                result.append(" ")
                index += 1
                continue
            if current == "'":
                state = "char"
                result.append(" ")
                index += 1
                continue
            result.append(current)
            index += 1
            continue
        if state == "line":
            if current == "\n":
                state = "code"
                result.append("\n")
            else:
                result.append(" ")
            index += 1
            continue
        if state == "block":
            if current == "*" and following == "/":
                state = "code"
                result.extend("  ")
                index += 2
            else:
                result.append("\n" if current == "\n" else " ")
                index += 1
            continue
        if state == "string":
            if current == "\\":
                result.extend("  ")
                index += 2
            elif current == '"':
                state = "code"
                result.append(" ")
                index += 1
            else:
                result.append("\n" if current == "\n" else " ")
                index += 1
            continue
        if state == "verbatim":
            if current == '"' and following == '"':
                result.extend("  ")
                index += 2
            elif current == '"':
                state = "code"
                result.append(" ")
                index += 1
            else:
                result.append("\n" if current == "\n" else " ")
                index += 1
            continue
        if state == "char":
            if current == "\\":
                result.extend("  ")
                index += 2
            elif current == "'":
                state = "code"
                result.append(" ")
                index += 1
            else:
                result.append("\n" if current == "\n" else " ")
                index += 1
    return "".join(result)


project = text("src/TickLab.App/TickLab.App.csproj")
settings = text("src/TickLab.App/Core/Settings/ChartSettings.cs")
control = text("src/TickLab.App/Controls/CandleChartControl.cs")
renderers = text("src/TickLab.App/Controls/CandleChartControl.ChartTypes.cs")
main_types = text("src/TickLab.App/MainWindow.ChartTypes.cs")
builder = text("src/TickLab.App/Core/Market/OrderFlowProfileBuilder.cs")
models = text("src/TickLab.App/Core/Market/OrderFlowProfileModels.cs")
window = text("src/TickLab.App/Windows/OrderFlowSettingsWindow.cs")
contexts = text("src/TickLab.App/MainWindow.ChartContexts.cs")

check("<Version>1.13.0.15</Version>" in project, "Project version 1.13.0.15")
check("<AssemblyVersion>1.13.0.15</AssemblyVersion>" in project, "Assembly version 1.13.0.15")
check("<FileVersion>1.13.0.15</FileVersion>" in project, "File version 1.13.0.15")
check((ROOT / "TickLabV1_13_0_15.sln").exists(), "Renamed v1.13.0.15 solution exists")
check((ROOT / "VERSION.txt").read_text().strip() == "1.13.0.15", "VERSION.txt updated")

check("private const double CandleGapPixels = 6.0;" in control, "Candle gap doubled to 6 pixels")
check("SnapToDeviceStroke" in control, "Physical device-pixel stroke snapping")
check("SnapRectangleToDevicePixels" in control, "Physical device-pixel rectangle snapping")
check("PushGuidelineSet" in renderers, "WPF body-edge guidelines")
check("PenLineCap.Flat" in renderers, "Flat candle line caps")
check("PenLineJoin.Miter" in renderers, "Mitered candle border joins")
check("MaximumHorizontalVisibleCandles = 1_500" in control, "1,500-candle zoom cap retained")

for name in ("TimePriceOpportunity", "SessionVolumeProfile", "VolumeFootprint"):
    check(name in settings, f"ChartVisualType.{name} exists")
    check(f"ChartVisualType.{name}" in main_types, f"{name} appears in chart menu")
    check(f"case ChartVisualType.{name}:" in control, f"{name} rendering dispatch exists")

check("DrawTimePriceOpportunityChart" in renderers, "TPO renderer exists")
check("DrawSessionVolumeProfileChart" in renderers, "Session Volume Profile renderer exists")
check("DrawVolumeFootprintChart" in renderers, "Volume Footprint renderer exists")
check("TpoBracketMinutes" in settings and "MarketProfileRows" in settings, "TPO settings persist")
check("ProfileSessionStartHour" in settings, "Broker-time session start setting persists")
check("FootprintPriceStepPoints" in settings, "Footprint price-step setting persists")
check("VolumeProfileValueAreaPercent" in settings, "Value-area setting persists")
check("OrderFlowSettingsWindow" in main_types and "OrderFlowSettingsWindow" in window, "Order-flow settings window wired")

check("tick.VolumeReal" in builder, "Builder reads real traded volume")
check("double volume = tick.VolumeReal;" in builder, "No tick-volume substitution in builder")
check("TickFlagBuy = 32" in builder and "TickFlagSell = 64" in builder, "MT5 BUY/SELL flags supported")
check("PointOfControlPrice" in models and "ValueAreaLow" in models and "ValueAreaHigh" in models, "POC and value-area model")
check("BidVolume" in models and "AskVolume" in models and "Delta" in models, "Footprint bid/ask/delta model")
check("RequiresRealVolume(type)" in main_types, "Real-volume chart activation guard")
check("ChartVisualType.TimePriceOpportunity" not in re.search(r"private static bool RequiresRealVolume.*?;", main_types, re.S).group(0), "TPO does not require real volume")
check("maximumRecords: 2_000_000" in main_types, "Visible-range tick load is capped")
check("Task.Delay(250" in main_types, "Viewport refresh is debounced")
check("ScheduleOrderFlowRefresh(context);" in contexts, "Viewport refresh requests order-flow data")
check("This chart requires real trade volume" in main_types and "This chart requires real trade volume" in renderers, "Clear no-real-volume error")

# All XAML must remain well formed.
for xaml in ROOT.rglob("*.xaml"):
    try:
        ET.parse(xaml)
        check(True, f"XAML parses: {xaml.relative_to(ROOT)}")
    except Exception as error:
        check(False, f"XAML parses: {xaml.relative_to(ROOT)} ({error})")

# Lightweight lexical delimiter audit for all C# files.
for source_path in ROOT.joinpath("src").rglob("*.cs"):
    stripped = strip_csharp(source_path.read_text(encoding="utf-8"))
    balanced = (
        stripped.count("{") == stripped.count("}") and
        stripped.count("(") == stripped.count(")") and
        stripped.count("[") == stripped.count("]")
    )
    check(balanced, f"C# delimiters balanced: {source_path.relative_to(ROOT)}")

# Rough duplicate method-signature audit across CandleChartControl partial files.
method_pattern = re.compile(
    r"\b(?:public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?"
    r"[\w<>,?\[\].()]+\s+(\w+)\s*\(([^;{}]*)\)\s*(?:=>|\{)",
    re.S,
)
signatures: dict[tuple[str, tuple[str, ...]], list[str]] = {}
for source_path in ROOT.joinpath("src/TickLab.App/Controls").glob("CandleChartControl*.cs"):
    source = strip_csharp(source_path.read_text(encoding="utf-8"))
    for match in method_pattern.finditer(source):
        parameter_types: list[str] = []
        for parameter in match.group(2).split(","):
            parameter = re.sub(r"\b(ref|out|in|params|this)\b\s*", "", parameter.strip())
            if not parameter:
                continue
            tokens = parameter.split("=")[0].strip().split()
            parameter_types.append(" ".join(tokens[:-1]) if len(tokens) > 1 else tokens[0])
        signature = (match.group(1), tuple(parameter_types))
        signatures.setdefault(signature, []).append(source_path.name)
for signature, files in signatures.items():
    check(len(files) == 1, f"Unique CandleChartControl method signature {signature}: {files}")

# Protected MT5 bridge sources must match their retained hashes.
for line in text("MT5_SOURCE_SHA256.txt").splitlines():
    expected, relative = line.split(maxsplit=1)
    actual = hashlib.sha256((ROOT / relative).read_bytes()).hexdigest()
    check(actual == expected, f"Protected MT5 source unchanged: {relative}")

passed = sum(1 for result, _ in checks if result)
failed = [(description) for result, description in checks if not result]
print(f"STEP-5 CHECKS PASSED: {passed}")
print(f"STEP-5 CHECKS FAILED: {len(failed)}")
for description in failed:
    print(f"FAIL: {description}")
if failed:
    sys.exit(1)
print("TickLab v1.13.0.15 crisp-candle and Step 5 validation passed.")
