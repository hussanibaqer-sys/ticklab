TickLab v1.13.0.14 — MT5 Bridge Write-Access Hotfix

Fixes TL-CORE-MAINT / connector_integrity_or_flush errors caused when Windows,
MT5, OneDrive, antivirus, or Controlled Folder Access temporarily prevents an
atomic rename over an existing fixed-name bridge request file.

Changes:
- Serializes writes per bridge request path.
- Uses a unique same-folder temporary file for atomic replacement.
- Retries atomic replacement with bounded backoff.
- Falls back to a flushed direct write when MT5 allows writing but not rename/delete sharing.
- Clears an accidental read-only attribute when possible.
- Removes temporary files after success or failure.
- Treats symbol refresh as opportunistic so one transient access lock does not
  disconnect TickLab or raise a maintenance error.
- Does not change any MT5 bridge filename, payload, schema, or MQL5 source.

Build:
1. Close TickLab and Visual Studio.
2. Extract outside OneDrive, preferably C:\TickLab\TickLabV1_13_0_14.
3. Delete src\TickLab.App\bin and src\TickLab.App\obj.
4. Open TickLabV1_13_0_14.sln.
5. Clean Solution, then Rebuild Solution.
