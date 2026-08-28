#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[1]
bridge = (root / 'src/TickLab.App/Gateway/FileBridge/Mt5FileBridgeClient.cs').read_text(encoding='utf-8')
main = (root / 'src/TickLab.App/MainWindow.xaml.cs').read_text(encoding='utf-8')

checks = {
    'concurrent dictionary import': 'using System.Collections.Concurrent;' in bridge,
    'per-target writer locks': 'AtomicWriteLocks' in bridge and 'GetOrAdd(' in bridge,
    'unique temporary path': 'Environment.ProcessId' in bridge and 'Guid.NewGuid():N' in bridge,
    'locked helper': 'WriteAtomicTextLocked(' in bridge,
    '12 bounded attempts': 'attempt < 12' in bridge,
    'move overwrite': 'File.Move(temporaryPath, targetPath, true);' in bridge,
    'direct write fallback': 'WriteCompleteFile(targetPath, bytes);' in bridge,
    'write-through stream': 'options: FileOptions.WriteThrough' in bridge,
    'flush to disk': 'stream.Flush(flushToDisk: true);' in bridge,
    'io and access classification': 'exception is IOException or UnauthorizedAccessException' in bridge,
    'read-only handling': 'ClearReadOnlyAttribute(targetPath);' in bridge,
    'temporary cleanup': 'TryDeleteTemporaryBridgeFile(temporaryPath);' in bridge,
    'symbol IO suppression': 'catch (IOException)' in main,
    'symbol access suppression': 'catch (UnauthorizedAccessException)' in main,
    'protocol filename unchanged': '"symbols_request.json"' in bridge,
}

failed = [name for name, ok in checks.items() if not ok]
print(f'BRIDGE-WRITE CHECKS PASSED: {len(checks)-len(failed)}')
print(f'BRIDGE-WRITE CHECKS FAILED: {len(failed)}')
for name in failed:
    print(f'FAILED: {name}')
if failed:
    raise SystemExit(1)
print('TickLab v1.13.0.14 MT5 bridge write-access hotfix validation passed.')
