//+------------------------------------------------------------------+
//| TickLab Live Data Bridge 3.0.0                             |
//| Live raw ticks plus transient chart projection for TickLab       |
//+------------------------------------------------------------------+
#property strict
#property version   "3.00"
#property description "Independent live raw-tick channel and transient chart projection for TickLab"

enum ENUM_TICKLAB_BRIDGE_ROLE
{
   TICKLAB_LIVE_CHANNEL = 0,
   TICKLAB_HISTORY_WORKER = 1
};

input ENUM_TICKLAB_BRIDGE_ROLE InpBridgeRole = TICKLAB_LIVE_CHANNEL; // keep LIVE CHANNEL
input string InpConnectorName = "";
input int    InpTimerMilliseconds = 50;
input int    InpHeartbeatMilliseconds = 2000;
input int    InpLiveCandleExportMilliseconds = 50;
input int    InpHistoryCheckMilliseconds = 5000;
input int    InpHistoryChunkSize = 1000;
input int    InpMaximumRequestSeconds = 0; // 0 = unlimited selected tick range

input bool   InpCaptureAllLiveTicks = true;
input bool   InpBackfillAllHistoricalTicks = true; // runs only after TickLab Import/Refresh
input bool   InpCaptureMarketBook = false;
input bool   InpCaptureTradeTransactions = false;
input bool   InpCaptureSnapshots = false;
input int    InpLiveTickBatchSize = 4096;
input int    InpHistoricalTickChunkMinutes = 30;
input int    InpSnapshotMilliseconds = 1000;
input int    InpCaptureStatusMilliseconds = 2000;
input int    InpSymbolListMilliseconds = 15000;
input int    InpHistoryLoadBlockBars = 1000;
input int    InpBootstrapBars = 2000; // exact native bars sent once at attach/timeframe change
input int    InpM1IntegrityMilliseconds = 5000;
input int    InpRecentTickSnapshotMilliseconds = 30000;
input int    InpRecentSecondWindow = 300;
input int    InpRecentSecondSnapshotMilliseconds = 1000;
input int    InpRecentTickRepairSliceSeconds = 120;

const int ProtocolVersion = 2;
string BridgeVersion = "3.0.0-live-ticks-channel";
string ConnectorId = "";
string ConnectorFolder = "";
bool ConnectorFoldersReady = false;
string LastProcessedRequestId = "";
string LastChartRequestId = "";
string LastSymbolsRequestId = "";
string LastHistoryRequestId = "";
string ActiveHistoryRequestId = "";
bool HistoryImportRequested = false;
bool HistoryRequestIncludesTicks = false;
string LastChartSymbol = "";
ENUM_TIMEFRAMES LastChartTimeframe = PERIOD_CURRENT;
ENUM_TIMEFRAMES DataTimeframe = PERIOD_CURRENT;
string HistorySymbol = "";
ENUM_TIMEFRAMES HistoryTimeframe = PERIOD_M1;

bool CandleHistoryLoadComplete = false;
bool CandleHistoryLimitedByMaxBars = false;
int CandleHistoryLoadFailCount = 0;
datetime CandleHistoryTargetFirst = 0;
datetime CandleHistoryCurrentFirst = 0;
datetime LastLiveBarTime = 0;
ulong LastOnTickLiveWrite = 0;

ulong LastHeartbeatTick = 0;
ulong LastLiveCandleExportTick = 0;
ulong LastHistoryCheckTick = 0;
ulong LastSnapshotTick = 0;
ulong LastCaptureStatusTick = 0;
ulong LastSymbolListTick = 0;
ulong LastM1IntegrityTick = 0;
ulong LastRecentTickSnapshotTick = 0;
ulong LastRecentSecondSnapshotTick = 0;
ulong LastTickStateSaveTick = 0;
ulong LastLiveTickFlushTick = 0;
int LiveTickFileHandle = INVALID_HANDLE;
string LiveTickFilePath = "";

bool HistoryExportPending = true;
bool HistoryExportInProgress = false;
bool LiveTickCapturePending = true;
bool BookSubscribed = false;

int LastExportedBarCount = -1;
datetime LastExportedFirstDate = 0;
datetime LastExportedLatestBarTime = 0;

string CaptureSymbol = "";
long BridgeStartMsc = 0;
long LiveCursorMsc = 0;
int LiveCursorSeenCount = 0;
long LiveTickSequence = 0;
long LiveTicksArchived = 0;

long HistoricalCursorMsc = 0;
long HistoricalEndMsc = 0;
bool HistoricalTickBackfillComplete = false;
long HistoricalTicksArchived = 0;

long BookEventSequence = 0;
long TradeEventSequence = 0;
string LastCaptureMessage = "starting";

struct TickLabSecondBar
{
   long start_unix;
   double open;
   double high;
   double low;
   double close;
   long tick_volume;
   int spread;
   double real_volume;
   bool is_closed;
};

TickLabSecondBar RecentSecondBars[];
bool RecentSecondsDirty = false;
bool PrimingRecentSeconds = false;
long RecentTickRepairCursorMsc = 0;

//+------------------------------------------------------------------+
//| Initialization                                                   |
//+------------------------------------------------------------------+
int OnInit()
{
   if(InpTimerMilliseconds < 50 ||
      InpHeartbeatMilliseconds < 500 ||
      InpLiveCandleExportMilliseconds < 50 ||
      InpHistoryCheckMilliseconds < 1000 ||
      InpHistoryChunkSize < 1000 ||
      InpMaximumRequestSeconds < 0 ||
      InpLiveTickBatchSize < 256 ||
      InpHistoricalTickChunkMinutes < 1 ||
      InpSnapshotMilliseconds < 250 ||
      InpCaptureStatusMilliseconds < 500 ||
      InpSymbolListMilliseconds < 1000 ||
      InpHistoryLoadBlockBars < 100 ||
      InpBootstrapBars < 100 ||
      InpBootstrapBars > 10000 ||
      InpM1IntegrityMilliseconds < 250 ||
      InpRecentTickSnapshotMilliseconds < 1000 ||
      InpRecentSecondWindow < 60 ||
      InpRecentSecondWindow > 3600 ||
      InpRecentSecondSnapshotMilliseconds < 250 ||
      InpRecentTickRepairSliceSeconds < 30 ||
      InpRecentTickRepairSliceSeconds > 600)
   {
      Print("TickLab: Invalid bridge input settings.");
      return(INIT_PARAMETERS_INCORRECT);
   }

   ConnectorId = CreateConnectorId();
   ConnectorFolder = "TickLab\\Connections\\" + ConnectorId;

   if(!EnsureConnectorFolders())
   {
      Print(
         "TickLab: Could not create connector folder ",
         ConnectorFolder,
         " | Error: ",
         GetLastError());
      return(INIT_FAILED);
   }

   LastChartSymbol = _Symbol;
   LastChartTimeframe = _Period;
   DataTimeframe = _Period;
   CaptureSymbol = _Symbol;
   HistorySymbol = CaptureSymbol;
   HistoryTimeframe = PERIOD_M1;
   ResetCandleHistoryLoader();

   if(!EventSetMillisecondTimer(InpTimerMilliseconds))
   {
      Print("TickLab: Could not start timer. Error: ", GetLastError());
      return(INIT_FAILED);
   }

   ulong now = GetTickCount64();
   LastHeartbeatTick = now;
   LastLiveCandleExportTick = now;
   LastHistoryCheckTick = now;
   LastSnapshotTick = now;
   LastCaptureStatusTick = now;
   LastSymbolListTick = now;
   LastM1IntegrityTick = now;
   LastRecentTickSnapshotTick = now;
   LastRecentSecondSnapshotTick = now;
   LastTickStateSaveTick = now;

   CandleHistoryLoadComplete = true;
   HistoricalTickBackfillComplete = true;
   HistoryExportPending = false;
   HistoryImportRequested = false;
   HistoryRequestIncludesTicks = false;

   if(IsHistoryWorker())
   {
      // The worker owns only request-driven old-history operations. It never
      // publishes live files and therefore can block on MT5 synchronization
      // without delaying the independent live-channel EA.
      WriteHistoryWorkerHeartbeat(true,"waiting");
      WriteHistoryStatus(0,0,0,true,"waiting_for_import");
      Comment(
         "TickLab HISTORY WORKER V3.0.0 ONLINE\n",
         "Connector: ",ConnectorId);
      Print(
         "TickLab history worker v3.0.0 online | ID: ",
         ConnectorId);
      return(INIT_SUCCEEDED);
   }

   // V300 owns a dedicated live heartbeat file. Older bridge instances may
   // still write heartbeat.json in the same stable connector folder, so the
   // desktop intentionally ignores that legacy file once V300 is present.
   FileDelete(ConnectorFolder + "\\heartbeat.json", FILE_COMMON);
   FileDelete(ConnectorFolder + "\\heartbeat.json.tmp", FILE_COMMON);

   CleanCurrentChartFiles();
   WriteConnectionFile();
   WriteHeartbeatFile(true);
   WriteSymbolsFile();
   WriteRuntimeState("starting");

   InitializeUniversalCaptureForSymbol();
   PrimeRecentSecondBars();

   if(InpCaptureMarketBook)
      BookSubscribed = MarketBookAdd(CaptureSymbol);

   WriteCapabilitiesFile();
   WriteUniversalSnapshots();
   WriteLiveCandleFile();
   WriteChartBootstrapFile();
   WriteM1RecentFile();
   WriteRecentTickSnapshot();
   WriteRecentSecondBarsFile();
   WriteLiveSecondBarFile();
   WriteCaptureStatus("online");
   MaintainBridgeHeartbeat(true);

   Print(
      "TickLab live channel v3.0.0 online | ID: ",
      ConnectorId,
      " | Symbol: ",
      CaptureSymbol,
      " | Timeframe: ",
      EnumToString(DataTimeframe));

   Comment(
      "TickLab LIVE CHANNEL V3.0.0 ONLINE\n",
      "Connector: ",ConnectorId,"\n",
      "Instrument: ",CaptureSymbol);

   return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
//| Deinitialization                                                 |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   EventKillTimer();
   Comment("");

   if(IsHistoryWorker())
   {
      WriteHistoryWorkerHeartbeat(false,"stopped");
      Print("TickLab history worker stopped | ID: ",ConnectorId);
      return;
   }

   CaptureAllLiveTicks();
   CloseLiveTickArchiveFile(true);
   SaveTickArchiveState();

   if(BookSubscribed)
   {
      MarketBookRelease(CaptureSymbol);
      BookSubscribed = false;
   }

   WriteCaptureStatus("stopped");
   WriteHeartbeatFile(false);
   Print("TickLab live channel stopped | ID: ",ConnectorId);
}

//+------------------------------------------------------------------+
//| New market-state notification                                   |
//| Keep this handler tiny. CopyTicks recovers every stored tick.    |
//+------------------------------------------------------------------+
void OnTick()
{
   // One publisher only: OnTick merely signals new work. The 50 ms timer
   // copies every stored tick and writes the exact native live candle.
   LiveTickCapturePending = true;
}

//+------------------------------------------------------------------+
//| Depth-of-Market event                                            |
//+------------------------------------------------------------------+
void OnBookEvent(const string &symbol)
{
   if(!InpCaptureMarketBook ||
      !BookSubscribed ||
      symbol != CaptureSymbol)
   {
      return;
   }

   AppendMarketBookSnapshot(symbol);
}

//+------------------------------------------------------------------+
//| Account trade transaction                                       |
//+------------------------------------------------------------------+
void OnTradeTransaction(
   const MqlTradeTransaction &trans,
   const MqlTradeRequest &request,
   const MqlTradeResult &result)
{
   if(InpCaptureTradeTransactions)
      AppendTradeTransaction(trans, request, result);
}

//+------------------------------------------------------------------+
//| Timer                                                            |
//+------------------------------------------------------------------+
void OnTimer()
{
   ulong now = GetTickCount64();
   MaintainBridgeHeartbeat(false);

   if(IsHistoryWorker())
   {
      CheckForHistoryRequest();

      if(HistoryImportRequested)
      {
         if(!CandleHistoryLoadComplete)
         {
            int loadResult = LoadCandleHistoryStep();
            if(loadResult > 0)
            {
               CandleHistoryLoadComplete = true;
               HistoryExportPending = true;
            }
         }
         else if(HistoryExportPending && !HistoryExportInProgress)
         {
            HistoryExportInProgress = true;
            WriteHistoryWorkerHeartbeat(true,"exporting_candles");
            bool exported = RefreshFullHistory(true);
            HistoryExportPending = !exported;
            HistoryExportInProgress = false;
         }
         else if(InpBackfillAllHistoricalTicks &&
                 !HistoricalTickBackfillComplete)
         {
            WriteHistoryWorkerHeartbeat(true,"exporting_ticks");
            ProcessHistoricalTickBackfillStep();
         }
         else
         {
            HistoryImportRequested = false;
            HistoryRequestIncludesTicks = false;
            WriteHistoryStatus(
               LastExportedBarCount,
               LastExportedFirstDate,
               LastExportedLatestBarTime,
               true,
               "ready");
            ActiveHistoryRequestId = "";
            WriteRuntimeState("waiting");
            WriteHistoryWorkerHeartbeat(true,"waiting");
         }
      }
      return;
   }

   CheckForChartSelectionRequest();
   CheckForSymbolsRequest();

   if(now - LastSymbolListTick >= (ulong)InpSymbolListMilliseconds)
   {
      WriteSymbolsFile();
      LastSymbolListTick = now;
   }

   if(InpCaptureAllLiveTicks)
      CaptureAllLiveTicks();

   if(now - LastRecentSecondSnapshotTick >=
      (ulong)InpRecentSecondSnapshotMilliseconds)
   {
      WriteRecentSecondBarsFile();
      LastRecentSecondSnapshotTick = now;
   }

   if(now - LastM1IntegrityTick >= (ulong)InpM1IntegrityMilliseconds)
   {
      WriteM1RecentFile();
      LastM1IntegrityTick = now;
   }

   if(now - LastRecentTickSnapshotTick >=
      (ulong)InpRecentTickSnapshotMilliseconds)
   {
      WriteRecentTickSnapshot();
      LastRecentTickSnapshotTick = now;
   }

   if(now - LastLiveCandleExportTick >=
      (ulong)InpLiveCandleExportMilliseconds)
   {
      WriteLiveCandleFile();
      LastLiveCandleExportTick = now;
   }

   CheckForTickRequest();

   if(InpCaptureSnapshots &&
      now - LastSnapshotTick >= (ulong)InpSnapshotMilliseconds)
   {
      WriteUniversalSnapshots();
      LastSnapshotTick = now;
   }

   if(now - LastCaptureStatusTick >=
      (ulong)InpCaptureStatusMilliseconds)
   {
      WriteCaptureStatus("online");
      WriteRuntimeState("live");
      LastCaptureStatusTick = now;
   }
}

//+------------------------------------------------------------------+
//| Apply a TickLab or attached-chart data selection                 |
//+------------------------------------------------------------------+
void AdoptAttachedChartState()
{
   CaptureAllLiveTicks();
   CloseLiveTickArchiveFile(true);
   SaveTickArchiveState();

   if(BookSubscribed)
   {
      MarketBookRelease(CaptureSymbol);
      BookSubscribed = false;
   }

   CaptureSymbol = _Symbol;
   DataTimeframe = _Period;
   LastChartSymbol = _Symbol;
   LastChartTimeframe = _Period;
   LastExportedBarCount = -1;
   LastExportedFirstDate = 0;
   LastExportedLatestBarTime = 0;
   LastLiveBarTime = 0;

   ResetCandleHistoryLoader();
   CleanCurrentChartFiles();
   InitializeUniversalCaptureForSymbol();
   CandleHistoryLoadComplete = true;
   HistoricalTickBackfillComplete = true;
   HistoryExportPending = false;
   HistoryImportRequested = false;
   HistoryRequestIncludesTicks = false;

   if(InpCaptureMarketBook)
      BookSubscribed = MarketBookAdd(CaptureSymbol);

   WriteConnectionFile();
   WriteHeartbeatFile(true);
   WriteSymbolsFile();
   WriteCapabilitiesFile();
   WriteUniversalSnapshots();
   WriteLiveCandleFile();
   WriteChartBootstrapFile();
   WriteCaptureStatus("online");
   WriteRuntimeState("chart_changed");
   MaintainBridgeHeartbeat(true);
}

bool QueueAttachedChartSelection(
   const string symbol,
   const ENUM_TIMEFRAMES timeframe)
{
   if(StringLen(symbol) == 0 ||
      timeframe == PERIOD_CURRENT)
      return false;

   ResetLastError();
   if(!SymbolSelect(symbol,true))
      return false;

   bool symbolChanged = symbol != CaptureSymbol;
   bool timeframeChanged = timeframe != DataTimeframe;

   if(!symbolChanged && !timeframeChanged)
      return true;

   CaptureAllLiveTicks();
   CloseLiveTickArchiveFile(true);
   SaveTickArchiveState();

   if(symbolChanged && BookSubscribed)
   {
      MarketBookRelease(CaptureSymbol);
      BookSubscribed = false;
   }

   CaptureSymbol = symbol;
   DataTimeframe = timeframe;
   LastChartSymbol = symbol;
   LastChartTimeframe = timeframe;
   LastLiveBarTime = 0;

   if(symbolChanged)
   {
      ArrayResize(RecentSecondBars,0);
      RecentSecondsDirty = false;
      InitializeUniversalCaptureForSymbol();
      PrimeRecentSecondBars();

      if(InpCaptureMarketBook)
         BookSubscribed = MarketBookAdd(CaptureSymbol);
   }

   // V300 owns a dedicated live heartbeat file. Older bridge instances may
   // still write heartbeat.json in the same stable connector folder, so the
   // desktop intentionally ignores that legacy file once V300 is present.
   FileDelete(ConnectorFolder + "\\heartbeat.json", FILE_COMMON);
   FileDelete(ConnectorFolder + "\\heartbeat.json.tmp", FILE_COMMON);

   CleanCurrentChartFiles();
   WriteConnectionFile();
   WriteHeartbeatFile(true);
   WriteCapabilitiesFile();
   WriteUniversalSnapshots();
   WriteLiveCandleFile();
   WriteChartBootstrapFile();
   WriteM1RecentFile();
   WriteRecentSecondBarsFile();
   WriteRuntimeState("projection_changed");
   MaintainBridgeHeartbeat(true);
   return true;
}

//+------------------------------------------------------------------+
//| Read chart_request.json written by TickLab                       |
//+------------------------------------------------------------------+
void CheckForChartSelectionRequest()
{
   string requestPath = ConnectorFolder + "\\chart_request.json";

   if(!FileIsExist(requestPath,FILE_COMMON))
      return;

   string json = ReadCommonTextFile(requestPath);

   if(StringLen(json) == 0)
      return;

   string requestId = "";
   string connectorId = "";
   string symbol = "";
   string timeframeText = "";
   long protocol = 0;
   long requestedUnix = 0;

   bool valid =
      JsonGetLong(json,"protocol_version",protocol) &&
      JsonGetString(json,"request_id",requestId) &&
      JsonGetString(json,"connector_id",connectorId) &&
      JsonGetString(json,"symbol",symbol) &&
      JsonGetString(json,"timeframe",timeframeText) &&
      JsonGetLong(json,"requested_unix",requestedUnix);

   if(!valid || StringLen(requestId) == 0)
   {
      FileDelete(requestPath,FILE_COMMON);
      return;
   }

   if(requestId == LastChartRequestId)
   {
      FileDelete(requestPath,FILE_COMMON);
      return;
   }

   LastChartRequestId = requestId;
   long age = (long)TimeGMT() - requestedUnix;

   if(protocol != ProtocolVersion ||
      connectorId != ConnectorId ||
      age < -30 || age > 120)
   {
      WriteChartSelectionResponse(requestId,symbol,timeframeText,false,
         "Invalid or expired chart request.");
      FileDelete(requestPath,FILE_COMMON);
      return;
   }

   ENUM_TIMEFRAMES requestedTimeframe = ParseTimeframe(timeframeText);

   if(requestedTimeframe == PERIOD_CURRENT)
   {
      WriteChartSelectionResponse(requestId,symbol,timeframeText,false,
         "Unsupported timeframe.");
      FileDelete(requestPath,FILE_COMMON);
      return;
   }

   bool queued = QueueAttachedChartSelection(symbol,requestedTimeframe);

   WriteChartSelectionResponse(requestId,symbol,timeframeText,queued,
      queued ? "MT5 chart change queued." : "MT5 chart change failed.");

   FileDelete(requestPath,FILE_COMMON);
}

//+------------------------------------------------------------------+
//| chart_selection.json acknowledgement                             |
//+------------------------------------------------------------------+
bool WriteChartSelectionResponse(
   const string requestId,
   const string symbol,
   const string timeframeText,
   const bool success,
   const string message)
{
   string json =
      "{\r\n" +
      "  \"protocol_version\": " + IntegerToString(ProtocolVersion) + ",\r\n" +
      "  \"request_id\": \"" + EscapeJson(requestId) + "\",\r\n" +
      "  \"connector_id\": \"" + EscapeJson(ConnectorId) + "\",\r\n" +
      "  \"symbol\": \"" + EscapeJson(symbol) + "\",\r\n" +
      "  \"timeframe\": \"" + EscapeJson(timeframeText) + "\",\r\n" +
      "  \"success\": " + BoolToJson(success) + ",\r\n" +
      "  \"message\": \"" + EscapeJson(message) + "\",\r\n" +
      "  \"completed_unix\": " + IntegerToString((long)TimeGMT()) + "\r\n" +
      "}\r\n";

   return WriteTextAtomic(
      ConnectorFolder +
      "\\chart_selection.json",
      json);
}

//+------------------------------------------------------------------+
//| Convert TickLab timeframe text to an MT5 timeframe               |
//+------------------------------------------------------------------+
ENUM_TIMEFRAMES ParseTimeframe(
   const string value)
{
   if(value == "PERIOD_M1")  return PERIOD_M1;
   if(value == "PERIOD_M2")  return PERIOD_M2;
   if(value == "PERIOD_M3")  return PERIOD_M3;
   if(value == "PERIOD_M4")  return PERIOD_M4;
   if(value == "PERIOD_M5")  return PERIOD_M5;
   if(value == "PERIOD_M6")  return PERIOD_M6;
   if(value == "PERIOD_M10") return PERIOD_M10;
   if(value == "PERIOD_M12") return PERIOD_M12;
   if(value == "PERIOD_M15") return PERIOD_M15;
   if(value == "PERIOD_M20") return PERIOD_M20;
   if(value == "PERIOD_M30") return PERIOD_M30;
   if(value == "PERIOD_H1")  return PERIOD_H1;
   if(value == "PERIOD_H2")  return PERIOD_H2;
   if(value == "PERIOD_H3")  return PERIOD_H3;
   if(value == "PERIOD_H4")  return PERIOD_H4;
   if(value == "PERIOD_H6")  return PERIOD_H6;
   if(value == "PERIOD_H8")  return PERIOD_H8;
   if(value == "PERIOD_H12") return PERIOD_H12;
   if(value == "PERIOD_D1")  return PERIOD_D1;
   if(value == "PERIOD_W1")  return PERIOD_W1;
   if(value == "PERIOD_MN1") return PERIOD_MN1;

   return PERIOD_CURRENT;
}

//+------------------------------------------------------------------+
//| TickLab may request an immediate symbols.psv refresh             |
//+------------------------------------------------------------------+
void CheckForSymbolsRequest()
{
   string requestPath =
      ConnectorFolder +
      "\\symbols_request.json";

   if(!FileIsExist(
         requestPath,
         FILE_COMMON))
   {
      return;
   }

   string json =
      ReadCommonTextFile(
         requestPath);

   if(StringLen(json) == 0)
      return;

   string requestId = "";
   string connectorId = "";
   long protocol = 0;

   bool valid =
      JsonGetLong(
         json,
         "protocol_version",
         protocol) &&
      JsonGetString(
         json,
         "request_id",
         requestId) &&
      JsonGetString(
         json,
         "connector_id",
         connectorId);

   if(!valid ||
      StringLen(requestId) == 0 ||
      requestId == LastSymbolsRequestId)
   {
      return;
   }

   LastSymbolsRequestId =
      requestId;

   if(protocol == ProtocolVersion &&
      connectorId == ConnectorId)
   {
      WriteSymbolsFile();
      MaintainBridgeHeartbeat(true);
   }

   FileDelete(requestPath,FILE_COMMON);
}

//+------------------------------------------------------------------+
//| Read explicit Import/Refresh request from TickLab                 |
//+------------------------------------------------------------------+
void CheckForHistoryRequest()
{
   string requestPath = ConnectorFolder + "\\history_request.json";

   if(!FileIsExist(requestPath,FILE_COMMON))
      return;

   string json = ReadCommonTextFile(requestPath);
   if(StringLen(json) == 0)
      return;

   string requestId = "";
   string connectorId = "";
   string action = "";
   string symbol = "";
   string timeframeText = "";
   long protocol = 0;
   long includeTicks = 0;
   long minimumTickMsc = 0;
   long requestedUnix = 0;

   bool valid =
      JsonGetLong(json,"protocol_version",protocol) &&
      JsonGetString(json,"request_id",requestId) &&
      JsonGetString(json,"connector_id",connectorId) &&
      JsonGetString(json,"action",action) &&
      JsonGetString(json,"symbol",symbol) &&
      JsonGetString(json,"timeframe",timeframeText) &&
      JsonGetLong(json,"include_ticks",includeTicks) &&
      JsonGetLong(json,"requested_unix",requestedUnix);

   // Optional in protocol v2 for backward compatibility. A positive value
   // prevents current-quarter and 60-day repair requests from rereading the
   // broker's complete tick archive.
   JsonGetLong(json,"minimum_tick_msc",minimumTickMsc);

   if(!valid || StringLen(requestId) == 0)
   {
      FileDelete(requestPath,FILE_COMMON);
      return;
   }

   if(requestId == LastHistoryRequestId)
   {
      FileDelete(requestPath,FILE_COMMON);
      return;
   }

   LastHistoryRequestId = requestId;
   long age = (long)TimeGMT() - requestedUnix;
   ENUM_TIMEFRAMES requestedTimeframe = ParseTimeframe(timeframeText);
   bool identityMatches =
      protocol == ProtocolVersion &&
      connectorId == ConnectorId &&
      age >= -30 && age <= 120;

   bool supportedAction =
      action == "import" ||
      action == "refresh";

   bool sourceAvailable =
      StringLen(symbol) > 0 &&
      requestedTimeframe != PERIOD_CURRENT &&
      SymbolSelect(symbol,true);

   if(!identityMatches || !supportedAction || !sourceAvailable)
   {
      WriteHistoryRequestResponse(
         requestId,
         false,
         "The requested MT5 symbol or timeframe is unavailable.");
      FileDelete(requestPath,FILE_COMMON);
      return;
   }

   HistorySymbol = symbol;
   HistoryTimeframe = requestedTimeframe;
   ActiveHistoryRequestId = requestId;
   // Rebuild the exact native MT5 candle snapshot once. Historical ticks
   // resume from their saved cursor during Import. Refresh restarts the
   // tick backfill so missing/corrupt segments are repaired atomically.
   LastExportedBarCount = -1;
   LastExportedFirstDate = 0;
   LastExportedLatestBarTime = 0;
   ResetCandleHistoryLoader();
   HistoryImportRequested = true;
   HistoryRequestIncludesTicks = includeTicks > 0;

   if(includeTicks > 0 && InpBackfillAllHistoricalTicks)
   {
      MqlTick historyNowTick;
      long historyEnd = (long)TimeCurrent() * 1000 - 1;
      if(SymbolInfoTick(HistorySymbol,historyNowTick) && historyNowTick.time_msc > 0)
         historyEnd = historyNowTick.time_msc - 1;

      HistoricalCursorMsc = minimumTickMsc > 0
         ? minimumTickMsc
         : 0;
      HistoricalEndMsc = historyEnd;
      HistoricalTickBackfillComplete = false;

      // Historical cursor state is request-owned and must never be written
      // into the independent live symbol cursor file.
   }
   else
   {
      // Native timeframe imports after the first one must not repeat the
      // expensive raw-tick backfill for the same instrument.
      HistoricalTickBackfillComplete = true;
   }

   WriteHistoryStatus(0,0,0,false,"requested");
   WriteRuntimeState("import_requested");
   WriteHistoryRequestResponse(
      requestId,
      true,
      action == "refresh"
         ? "MT5 history refresh started."
         : "MT5 history import started.");

   FileDelete(requestPath,FILE_COMMON);
}

bool WriteHistoryRequestResponse(
   const string requestId,
   const bool success,
   const string message)
{
   string json =
      "{\r\n" +
      "  \"protocol_version\": " + IntegerToString(ProtocolVersion) + ",\r\n" +
      "  \"request_id\": \"" + EscapeJson(requestId) + "\",\r\n" +
      "  \"connector_id\": \"" + EscapeJson(ConnectorId) + "\",\r\n" +
      "  \"symbol\": \"" + EscapeJson(HistorySymbol) + "\",\r\n" +
      "  \"timeframe\": \"" + EscapeJson(EnumToString(HistoryTimeframe)) + "\",\r\n" +
      "  \"success\": " + BoolToJson(success) + ",\r\n" +
      "  \"message\": \"" + EscapeJson(message) + "\",\r\n" +
      "  \"completed_unix\": " + IntegerToString((long)TimeGMT()) + "\r\n" +
      "}\r\n";

   return WriteTextAtomic(
      ConnectorFolder + "\\history_response.json",
      json);
}

//+------------------------------------------------------------------+
//| Load older candle chunks until MT5 terminal history is complete  |
//+------------------------------------------------------------------+
//+------------------------------------------------------------------+
//| Close an unfinished complete-history build                       |
//+------------------------------------------------------------------+
void ResetCandleHistoryLoader()
{
   CandleHistoryLoadComplete = false;
   CandleHistoryLimitedByMaxBars = false;
   CandleHistoryLoadFailCount = 0;
   CandleHistoryTargetFirst = 0;
   CandleHistoryCurrentFirst = 0;
   HistoryExportPending = false;
   HistoryExportInProgress = false;
}

int LoadCandleHistoryStep()
{
   if(!SymbolInfoInteger(HistorySymbol,SYMBOL_SELECT))
   {
      if(!SymbolSelect(HistorySymbol,true))
         return 0;
   }

   datetime targetFirst = 0;
   // SERVER_FIRSTDATE is the oldest history the broker says is available.
   // Using TERMINAL_FIRSTDATE here made the worker stop at the short range
   // already loaded in MT5 and falsely report that history was complete.
   SeriesInfoInteger(HistorySymbol,HistoryTimeframe,
      SERIES_SERVER_FIRSTDATE,targetFirst);

   if(targetFirst <= 0)
      SeriesInfoInteger(HistorySymbol,HistoryTimeframe,
         SERIES_TERMINAL_FIRSTDATE,targetFirst);

   if(targetFirst <= 0)
   {
      WriteHistoryStatus(0,0,0,false,"waiting_for_server_history_date");
      return 0;
   }

   CandleHistoryTargetFirst = targetFirst;

   datetime firstDate = 0;
   SeriesInfoInteger(HistorySymbol,HistoryTimeframe,
      SERIES_FIRSTDATE,firstDate);
   CandleHistoryCurrentFirst = firstDate;

   int periodSeconds = PeriodSeconds(HistoryTimeframe);
   if(periodSeconds <= 0) periodSeconds = 60;

   if(firstDate > 0 && firstDate <= targetFirst + periodSeconds)
      return 1;

   datetime edgeTimes[];
   CopyTime(HistorySymbol,HistoryTimeframe,
      targetFirst + periodSeconds,1,edgeTimes);

   SeriesInfoInteger(HistorySymbol,HistoryTimeframe,
      SERIES_FIRSTDATE,firstDate);
   CandleHistoryCurrentFirst = firstDate;

   if(firstDate > 0 && firstDate <= targetFirst + periodSeconds)
      return 2;

   if(!SeriesInfoInteger(HistorySymbol,HistoryTimeframe,SERIES_SYNCHRONIZED))
   {
      // The independent Live Bridge remains responsive while this worker
      // waits for MT5 to synchronize the requested historical series.
      MaintainBridgeHeartbeat(true);
      return 0;
   }

   int bars = Bars(HistorySymbol,HistoryTimeframe);
   int maximumBars = (int)TerminalInfoInteger(TERMINAL_MAXBARS);

   if(bars <= 0) return 0;

   if(maximumBars > 0 && bars >= maximumBars)
   {
      CandleHistoryLimitedByMaxBars = true;
      WriteHistoryStatus(bars,firstDate,
         (datetime)SeriesInfoInteger(HistorySymbol,HistoryTimeframe,SERIES_LASTBAR_DATE),
         true,"maximum_bars_reached");
      return 3;
   }

   int blockBars = InpHistoryLoadBlockBars;
   if(blockBars < 100) blockBars = 100;

   datetime olderTimes[];
   int copied = CopyTime(HistorySymbol,HistoryTimeframe,bars,blockBars,olderTimes);

   if(copied > 0)
   {
      CandleHistoryLoadFailCount = 0;
      if(olderTimes[0] <= targetFirst + periodSeconds)
         return 4;
   }
   else
   {
      CandleHistoryLoadFailCount++;
   }

   SeriesInfoInteger(HistorySymbol,HistoryTimeframe,
      SERIES_FIRSTDATE,firstDate);
   CandleHistoryCurrentFirst = firstDate;

   WriteHistoryStatus(bars,firstDate,
      (datetime)SeriesInfoInteger(HistorySymbol,HistoryTimeframe,SERIES_LASTBAR_DATE),
      false,copied > 0 ? "loading_older_bars" : "waiting_for_older_bars");

   MaintainBridgeHeartbeat(true);
   return 0;
}

//+------------------------------------------------------------------+
//| Export every broker instrument available in this MT5 terminal    |
//+------------------------------------------------------------------+
bool WriteSymbolsFile()
{
   string targetPath =
      ConnectorFolder +
      "\\symbols.psv";

   string temporaryPath =
      targetPath +
      ".tmp";

   ResetLastError();

   // FILE_TXT + FileWriteString avoids the quoting rules of FILE_CSV.
   // TickLab therefore receives exact symbol names and reliable booleans.
   int handle =
      FileOpen(
         temporaryPath,
         FILE_WRITE |
         FILE_TXT |
         FILE_ANSI |
         FILE_COMMON |
         FILE_SHARE_READ,
         0,
         CP_UTF8);

   if(handle == INVALID_HANDLE)
      return false;

   FileWriteString(
      handle,
      "name|description|path|selected|visible|custom|digits\r\n");

   int total =
      SymbolsTotal(false);

   for(int index = 0;
       index < total;
       index++)
   {
      string symbol =
         SymbolName(
            index,
            false);

      if(StringLen(symbol) == 0)
         continue;

      string description =
         CleanSymbolListText(
            SymbolInfoString(
               symbol,
               SYMBOL_DESCRIPTION));

      string path =
         CleanSymbolListText(
            SymbolInfoString(
               symbol,
               SYMBOL_PATH));

      string line =
         CleanSymbolListText(symbol) +
         "|" +
         description +
         "|" +
         path +
         "|" +
         BoolToJson(
            SymbolInfoInteger(
               symbol,
               SYMBOL_SELECT) != 0) +
         "|" +
         BoolToJson(
            SymbolInfoInteger(
               symbol,
               SYMBOL_VISIBLE) != 0) +
         "|" +
         BoolToJson(
            SymbolInfoInteger(
               symbol,
               SYMBOL_CUSTOM) != 0) +
         "|" +
         IntegerToString(
            SymbolInfoInteger(
               symbol,
               SYMBOL_DIGITS)) +
         "\r\n";

      FileWriteString(
         handle,
         line);
   }

   FileFlush(handle);
   FileClose(handle);

   bool replaced =
      ReplaceCommonFile(
         temporaryPath,
         targetPath);

   MaintainBridgeHeartbeat(true);
   return replaced;
}

string CleanSymbolListText(
   const string value)
{
   string cleaned = value;
   StringReplace(cleaned, "|", " ");
   StringReplace(cleaned, "\r", " ");
   StringReplace(cleaned, "\n", " ");
   StringReplace(cleaned, "\t", " ");
   return cleaned;
}

//+------------------------------------------------------------------+
//| Ensure the Common Files folder tree exists                       |
//+------------------------------------------------------------------+
bool EnsureConnectorFolders()
{
   if(ConnectorFoldersReady)
      return true;

   // FolderCreate uses a path relative to
   // Terminal\Common\Files when FILE_COMMON is supplied.
   ResetLastError();
   FolderCreate("TickLab",FILE_COMMON);

   ResetLastError();
   FolderCreate("TickLab\\Connections",FILE_COMMON);

   ResetLastError();
   FolderCreate(ConnectorFolder,FILE_COMMON);

   // Validate the final folder by creating a tiny probe file. This also
   // handles the normal case where FolderCreate returned false because
   // the directory already existed.
   string probePath = ConnectorFolder + "\\.__ticklab_folder_test.tmp";

   ResetLastError();
   int probeHandle =
      FileOpen(
         probePath,
         FILE_WRITE |
         FILE_TXT |
         FILE_ANSI |
         FILE_COMMON |
         FILE_SHARE_READ |
         FILE_SHARE_WRITE,
         0,
         CP_UTF8);

   if(probeHandle == INVALID_HANDLE)
   {
      Print(
         "TickLab: Connector folder validation failed: ",
         ConnectorFolder,
         " | Error: ",
         GetLastError());

      return false;
   }

   FileWriteString(probeHandle,"ok");
   FileFlush(probeHandle);
   FileClose(probeHandle);
   FileDelete(probePath,FILE_COMMON);
   ConnectorFoldersReady = true;

   return true;
}

void CleanCurrentChartFiles()
{
   // Only remove live projection files. Historical request/response files
   // belong to the independent history channel and must survive a chart
   // symbol or timeframe change.
   string filesToDelete[] =
   {
      "candle_live.csv",
      "candle_closed.csv",
      "chart_bootstrap.csv",
      "second_live.csv",
      "second_closed.csv",
      "seconds_recent.csv",
      "chart_selection.json",
      "runtime_state.json"
   };

   for(int index=0; index<ArraySize(filesToDelete); index++)
      FileDelete(ConnectorFolder + "\\" + filesToDelete[index],FILE_COMMON);
}

bool WriteRuntimeState(const string state)
{
   datetime firstDate = (datetime)SeriesInfoInteger(
      CaptureSymbol,DataTimeframe,SERIES_FIRSTDATE);
   datetime terminalFirst = (datetime)SeriesInfoInteger(
      CaptureSymbol,PERIOD_M1,SERIES_TERMINAL_FIRSTDATE);
   datetime latestDate = (datetime)SeriesInfoInteger(
      CaptureSymbol,DataTimeframe,SERIES_LASTBAR_DATE);

   MqlTick tick;
   long tickMsc=0;
   double bid=0;
   double ask=0;
   if(SymbolInfoTick(CaptureSymbol,tick))
   {
      tickMsc=tick.time_msc; bid=tick.bid; ask=tick.ask;
   }

   int digits=(int)SymbolInfoInteger(CaptureSymbol,SYMBOL_DIGITS);
   string json=
      "{\r\n" +
      "  \"bridge_version\": \"" + EscapeJson(BridgeVersion) + "\",\r\n" +
      "  \"state\": \"" + EscapeJson(state) + "\",\r\n" +
      "  \"chart_id\": " + IntegerToString((long)ChartID()) + ",\r\n" +
      "  \"symbol\": \"" + EscapeJson(CaptureSymbol) + "\",\r\n" +
      "  \"timeframe\": \"" + EscapeJson(EnumToString(DataTimeframe)) + "\",\r\n" +
      "  \"bars\": " + IntegerToString(Bars(CaptureSymbol,DataTimeframe)) + ",\r\n" +
      "  \"series_first_unix\": " + IntegerToString((long)firstDate) + ",\r\n" +
      "  \"terminal_first_unix\": " + IntegerToString((long)terminalFirst) + ",\r\n" +
      "  \"latest_bar_unix\": " + IntegerToString((long)latestDate) + ",\r\n" +
      "  \"history_load_complete\": " + BoolToJson(CandleHistoryLoadComplete) + ",\r\n" +
      "  \"limited_by_max_bars\": " + BoolToJson(CandleHistoryLimitedByMaxBars) + ",\r\n" +
      "  \"last_tick_msc\": " + IntegerToString(tickMsc) + ",\r\n" +
      "  \"bid\": " + DoubleToString(bid,digits) + ",\r\n" +
      "  \"ask\": " + DoubleToString(ask,digits) + ",\r\n" +
      "  \"updated_unix\": " + IntegerToString((long)TimeGMT()) + "\r\n" +
      "}\r\n";

   string runtimeFile = IsHistoryWorker()
      ? "history_runtime_state.json"
      : "runtime_state.json";
   return WriteTextAtomic(ConnectorFolder + "\\" + runtimeFile,json);
}

//+------------------------------------------------------------------+
//| Keep the bridge visible while full history is being exported     |
//+------------------------------------------------------------------+
bool IsHistoryWorker()
{
   return InpBridgeRole == TICKLAB_HISTORY_WORKER;
}

bool WriteHistoryWorkerHeartbeat(
   const bool online,
   const string state)
{
   string json =
      "{\r\n" +
      "  \"protocol_version\": " + IntegerToString(ProtocolVersion) + ",\r\n" +
      "  \"connector_id\": \"" + EscapeJson(ConnectorId) + "\",\r\n" +
      "  \"bridge_version\": \"" + EscapeJson(BridgeVersion) + "\",\r\n" +
      "  \"role\": \"history_worker\",\r\n" +
      "  \"online\": " + BoolToJson(online) + ",\r\n" +
      "  \"state\": \"" + EscapeJson(state) + "\",\r\n" +
      "  \"updated_unix\": " + IntegerToString((long)TimeGMT()) + "\r\n" +
      "}\r\n";

   return WriteTextAtomic(
      ConnectorFolder + "\\history_worker_heartbeat.json",
      json);
}

void MaintainBridgeHeartbeat(const bool force)
{
   ulong now = GetTickCount64();

   if(!force &&
      now - LastHeartbeatTick < (ulong)InpHeartbeatMilliseconds)
      return;

   if(IsHistoryWorker())
      WriteHistoryWorkerHeartbeat(true,
         HistoryImportRequested ? "working" : "waiting");
   else
   {
      WriteConnectionFile();
      WriteHeartbeatFile(true);
   }

   LastHeartbeatTick = now;
}

//+------------------------------------------------------------------+
//| Stable connector ID for this MT5 installation                    |
//+------------------------------------------------------------------+
string CreateConnectorId()
{
   string identity =
      TerminalInfoString(TERMINAL_DATA_PATH) + "|" +
      AccountInfoString(ACCOUNT_SERVER) + "|" +
      IntegerToString(AccountInfoInteger(ACCOUNT_LOGIN));

   uint hash = 2166136261;
   int length = StringLen(identity);
   for(int index = 0; index < length; index++)
   {
      hash ^= (uint)StringGetCharacter(identity,index);
      hash *= 16777619;
   }

   // Stable across charts so the Live Channel and History Worker share one
   // folder, but isolated across terminals/accounts.
   return "TL3-" + StringFormat("%08X",hash);
}

//+------------------------------------------------------------------+
//| Connector display name                                           |
//+------------------------------------------------------------------+
string GetConnectorName()
{
   if(StringLen(InpConnectorName) > 0)
      return InpConnectorName;

   string terminalName =
      TerminalInfoString(TERMINAL_NAME);

   string broker =
      AccountInfoString(ACCOUNT_COMPANY);

   if(StringLen(broker) > 0)
      return terminalName + " - " + broker;

   if(StringLen(terminalName) > 0)
      return terminalName;

   return ConnectorId;
}

//+------------------------------------------------------------------+
//| connection.json                                                  |
//+------------------------------------------------------------------+
bool WriteConnectionFile()
{
   string broker = AccountInfoString(ACCOUNT_COMPANY);
   string server = AccountInfoString(ACCOUNT_SERVER);
   long login = AccountInfoInteger(ACCOUNT_LOGIN);
   int build = (int)TerminalInfoInteger(TERMINAL_BUILD);

   string json =
      "{\r\n" +
      "  \"protocol_version\": " + IntegerToString(ProtocolVersion) + ",\r\n" +
      "  \"connector_id\": \"" + EscapeJson(ConnectorId) + "\",\r\n" +
      "  \"connector_name\": \"" + EscapeJson(GetConnectorName()) + "\",\r\n" +
      "  \"bridge_version\": \"" + EscapeJson(BridgeVersion) + "\",\r\n" +
      "  \"bridge_build\": \"3.0.0-live-ticks-channel\",\r\n" +
      "  \"broker\": \"" + EscapeJson(broker) + "\",\r\n" +
      "  \"server\": \"" + EscapeJson(server) + "\",\r\n" +
      "  \"terminal_build\": " + IntegerToString(build) + ",\r\n" +
      "  \"account_login\": " + IntegerToString(login) + ",\r\n" +
      "  \"data_mode\": \"isolated_ticklab_projection\",\r\n" +
      "  \"bridge_file\": \"TickLabLiveBridge_V300.mq5\",\r\n" +
      "  \"chart_id\": " + IntegerToString((long)ChartID()) + ",\r\n" +
      "  \"attached_chart_projection\": false,\r\n" +
      "  \"symbols_file\": \"symbols.psv\",\r\n" +
      "  \"chart_request_file\": \"chart_request.json\",\r\n" +
      "  \"active_symbol\": \"" + EscapeJson(CaptureSymbol) + "\",\r\n" +
      "  \"active_timeframe\": \"" + EscapeJson(EnumToString(DataTimeframe)) + "\",\r\n" +
      "  \"capabilities_file\": \"capabilities.json\"\r\n" +
      "}\r\n";

   return WriteTextAtomic(
      ConnectorFolder + "\\connection.json",
      json);
}

//+------------------------------------------------------------------+
//| heartbeat.json                                                   |
//+------------------------------------------------------------------+
bool WriteHeartbeatFile(const bool bridgeRunning)
{
   bool terminalConnected =
      bridgeRunning &&
      TerminalInfoInteger(TERMINAL_CONNECTED) != 0;

   long login = AccountInfoInteger(ACCOUNT_LOGIN);

   bool accountConnected =
      login > 0;

   int digits =
      (int)SymbolInfoInteger(
         CaptureSymbol,
         SYMBOL_DIGITS);

   double point =
      SymbolInfoDouble(
         CaptureSymbol,
         SYMBOL_POINT);

   long updatedUnix = (long)TimeGMT();

   MqlTick heartbeatTick;
   long lastTickMsc = 0;
   if(SymbolInfoTick(CaptureSymbol,heartbeatTick))
      lastTickMsc = heartbeatTick.time_msc;

   string json =
      "{\r\n" +
      "  \"protocol_version\": " + IntegerToString(ProtocolVersion) + ",\r\n" +
      "  \"connector_id\": \"" + EscapeJson(ConnectorId) + "\",\r\n" +
      "  \"bridge_version\": \"" + EscapeJson(BridgeVersion) + "\",\r\n" +
      "  \"symbol\": \"" + EscapeJson(CaptureSymbol) + "\",\r\n" +
      "  \"timeframe\": \"" + EscapeJson(EnumToString(DataTimeframe)) + "\",\r\n" +
      "  \"digits\": " + IntegerToString(digits) + ",\r\n" +
      "  \"point\": " + DoubleToString(point, digits) + ",\r\n" +
      "  \"tick_size\": " + DoubleToString(SymbolInfoDouble(CaptureSymbol,SYMBOL_TRADE_TICK_SIZE), 10) + ",\r\n" +
      "  \"server_utc_offset_minutes\": " + IntegerToString((long)((TimeTradeServer()-TimeGMT())/60)) + ",\r\n" +
      "  \"terminal_connected\": " + BoolToJson(terminalConnected) + ",\r\n" +
      "  \"account_connected\": " + BoolToJson(accountConnected) + ",\r\n" +
      "  \"universal_data\": true,\r\n" +
      "  \"live_ticks_archived\": " + IntegerToString(LiveTicksArchived) + ",\r\n" +
      "  \"historical_tick_backfill_complete\": " + BoolToJson(HistoricalTickBackfillComplete) + ",\r\n" +
      "  \"last_tick_msc\": " + IntegerToString(lastTickMsc) + ",\r\n" +
      "  \"updated_unix\": " + IntegerToString(updatedUnix) + "\r\n" +
      "}\r\n";

   return WriteTextAtomic(
      ConnectorFolder + "\\live_channel_heartbeat.json",
      json);
}

//+------------------------------------------------------------------+
//| Initialize persistent tick archive for the current symbol        |
//+------------------------------------------------------------------+
void InitializeUniversalCaptureForSymbol()
{
   MqlTick currentTick;

   if(SymbolInfoTick(CaptureSymbol, currentTick) &&
      currentTick.time_msc > 0)
   {
      BridgeStartMsc = currentTick.time_msc;
   }
   else
   {
      BridgeStartMsc = (long)TimeTradeServer() * 1000;
   }

   LiveCursorMsc = 0;
   LiveCursorSeenCount = 0;
   LiveTickSequence = 0;
   HistoricalCursorMsc = 0;
   HistoricalEndMsc = 0;
   HistoricalTickBackfillComplete = false;

   LoadTickArchiveState();

   if(LiveCursorMsc <= 0)
   {
      LiveCursorMsc = BridgeStartMsc;
      LiveCursorSeenCount = 0;
   }

   if(HistoricalEndMsc <= 0)
   {
      HistoricalEndMsc = BridgeStartMsc - 1;
      HistoricalCursorMsc = 0;
      HistoricalTickBackfillComplete = false;
   }

   LastCaptureMessage = "capture initialized";
   SaveTickArchiveState();
}

//+------------------------------------------------------------------+
//| Persistent archive-state path                                   |
//+------------------------------------------------------------------+
string GetTickArchiveStatePath()
{
   return ConnectorFolder +
      "\\tick_archive_state_" +
      SanitizeFilePart(CaptureSymbol) +
      ".json";
}

//+------------------------------------------------------------------+
//| Load persistent live/history cursors                            |
//+------------------------------------------------------------------+
void LoadTickArchiveState()
{
   string json =
      ReadCommonTextFile(
         GetTickArchiveStatePath());

   if(StringLen(json) == 0)
      return;

   long value = 0;

   if(JsonGetLong(json, "live_cursor_msc", value))
      LiveCursorMsc = value;

   if(JsonGetLong(json, "live_same_msc_count", value))
      LiveCursorSeenCount = (int)value;

   if(JsonGetLong(json, "live_sequence", value))
      LiveTickSequence = value;

   if(JsonGetLong(json, "historical_cursor_msc", value))
      HistoricalCursorMsc = value;

   if(JsonGetLong(json, "historical_end_msc", value))
      HistoricalEndMsc = value;

   if(JsonGetLong(json, "historical_complete", value))
      HistoricalTickBackfillComplete = value != 0;

   if(JsonGetLong(json, "live_ticks_archived", value))
      LiveTicksArchived = value;

   if(JsonGetLong(json, "historical_ticks_archived", value))
      HistoricalTicksArchived = value;
}

//+------------------------------------------------------------------+
//| Save persistent live/history cursors                            |
//+------------------------------------------------------------------+
bool SaveTickArchiveState()
{
   string json =
      "{\r\n" +
      "  \"protocol_version\": " + IntegerToString(ProtocolVersion) + ",\r\n" +
      "  \"symbol\": \"" + EscapeJson(CaptureSymbol) + "\",\r\n" +
      "  \"timeframe\": \"" + EscapeJson(EnumToString(DataTimeframe)) + "\",\r\n" +
      "  \"live_cursor_msc\": " + IntegerToString(LiveCursorMsc) + ",\r\n" +
      "  \"live_same_msc_count\": " + IntegerToString(LiveCursorSeenCount) + ",\r\n" +
      "  \"live_sequence\": " + IntegerToString(LiveTickSequence) + ",\r\n" +
      "  \"historical_cursor_msc\": " + IntegerToString(HistoricalCursorMsc) + ",\r\n" +
      "  \"historical_end_msc\": " + IntegerToString(HistoricalEndMsc) + ",\r\n" +
      "  \"historical_complete\": " + IntegerToString(HistoricalTickBackfillComplete ? 1 : 0) + ",\r\n" +
      "  \"live_ticks_archived\": " + IntegerToString(LiveTicksArchived) + ",\r\n" +
      "  \"historical_ticks_archived\": " + IntegerToString(HistoricalTicksArchived) + ",\r\n" +
      "  \"updated_unix\": " + IntegerToString((long)TimeGMT()) + "\r\n" +
      "}\r\n";

   return WriteTextAtomic(
      GetTickArchiveStatePath(),
      json);
}

//+------------------------------------------------------------------+
//| Capture every tick stored by MT5 since the previous cursor       |
//+------------------------------------------------------------------+
void CaptureAllLiveTicks()

{
   if(IsHistoryWorker())
      return;
   if(!InpCaptureAllLiveTicks || StringLen(CaptureSymbol) == 0)
      return;

   int loops = 0;
   bool wroteAny = false;

   while(loops < 8)
   {
      loops++;

      MqlTick ticks[];
      ResetLastError();

      int copied =
         CopyTicks(
            CaptureSymbol,
            ticks,
            COPY_TICKS_ALL,
            (ulong)MathMax((long)1, LiveCursorMsc),
            (uint)InpLiveTickBatchSize);

      int copyError = GetLastError();

      if(copied <= 0)
      {
         if(copied < 0)
            LastCaptureMessage =
               "live CopyTicks error " + IntegerToString(copyError);
         break;
      }

      long currentTimestamp = LiveCursorMsc;
      int encounteredAtTimestamp = 0;
      int alreadyArchivedAtTimestamp = LiveCursorSeenCount;
      int appended = 0;

      for(int index = 0; index < copied; index++)
      {
         long tickMsc = ticks[index].time_msc;

         if(tickMsc < currentTimestamp)
            continue;

         if(tickMsc == currentTimestamp)
         {
            encounteredAtTimestamp++;
            if(encounteredAtTimestamp <= alreadyArchivedAtTimestamp)
               continue;
         }
         else
         {
            currentTimestamp = tickMsc;
            encounteredAtTimestamp = 1;
            alreadyArchivedAtTimestamp = 0;
         }

         string path =
            ConnectorFolder +
            "\\ticks_live_" +
            SanitizeFilePart(CaptureSymbol) +
            "_" +
            DateKeyFromMilliseconds(tickMsc) +
            ".csv";

         if(!EnsureLiveTickArchiveFile(path))
         {
            LastCaptureMessage = "could not open live tick archive";
            break;
         }

         LiveTickSequence++;
         WriteTickArchiveRow(
            LiveTickFileHandle,
            "live",
            LiveTickSequence,
            ticks[index]);

         UpdateRecentSecondBar(ticks[index]);
         appended++;
      }

      if(currentTimestamp >= LiveCursorMsc)
      {
         LiveCursorMsc = currentTimestamp;
         LiveCursorSeenCount = encounteredAtTimestamp;
      }

      if(appended > 0)
      {
         wroteAny = true;
         LiveTicksArchived += appended;
         LastCaptureMessage = "live ticks archived";
      }

      ArrayFree(ticks);

      if(copied < InpLiveTickBatchSize || appended == 0)
         break;
   }

   if(wroteAny)
   {
      ulong now = GetTickCount64();
      if(LiveTickFileHandle != INVALID_HANDLE &&
         now - LastLiveTickFlushTick >= 250)
      {
         FileFlush(LiveTickFileHandle);
         LastLiveTickFlushTick = now;
      }

      if(now - LastTickStateSaveTick >= 2000)
      {
         if(LiveTickFileHandle != INVALID_HANDLE)
            FileFlush(LiveTickFileHandle);
         SaveTickArchiveState();
         LastTickStateSaveTick = now;
      }

      WriteLiveSecondBarFile();
   }

   LiveTickCapturePending = false;
}

//+------------------------------------------------------------------+
//| Build a small exact rolling 1-second stream for the live chart   |
//+------------------------------------------------------------------+
void PrimeRecentSecondBars()
{
   ArrayResize(RecentSecondBars,0);
   RecentSecondsDirty = false;

   if(StringLen(CaptureSymbol) == 0)
      return;

   long nowMsc = (long)TimeCurrent() * 1000;
   long fromMsc = nowMsc - (long)InpRecentSecondWindow * 1000;
   if(fromMsc < 0) fromMsc = 0;

   MqlTick ticks[];
   int copied = CopyTicksRange(
      CaptureSymbol,
      ticks,
      COPY_TICKS_ALL,
      (ulong)fromMsc,
      (ulong)nowMsc);

   PrimingRecentSeconds = true;
   if(copied > 0)
   {
      for(int index=0; index<copied; index++)
         UpdateRecentSecondBar(ticks[index]);
   }
   PrimingRecentSeconds = false;

   ArrayFree(ticks);
   WriteRecentSecondBarsFile();
   WriteLiveSecondBarFile();
}

void UpdateRecentSecondBar(const MqlTick &tick)
{
   long startUnix = (long)tick.time_msc / 1000;
   if(startUnix <= 0)
      startUnix = (long)tick.time;

   double price = tick.bid > 0.0
      ? tick.bid
      : tick.last > 0.0
         ? tick.last
         : tick.ask;

   if(startUnix <= 0 || price <= 0.0)
      return;

   double point = SymbolInfoDouble(CaptureSymbol,SYMBOL_POINT);
   int spread = 0;
   if(point > 0.0 && tick.bid > 0.0 && tick.ask > 0.0)
      spread = (int)MathRound((tick.ask-tick.bid)/point);

   int count = ArraySize(RecentSecondBars);
   if(count == 0 || startUnix > RecentSecondBars[count-1].start_unix)
   {
      if(count > 0)
      {
         RecentSecondBars[count-1].is_closed = true;
         if(!PrimingRecentSeconds)
            WriteSecondBarFile(
               "second_closed.csv",
               RecentSecondBars[count-1],
               true);
      }

      ArrayResize(RecentSecondBars,count+1);
      RecentSecondBars[count].start_unix = startUnix;
      RecentSecondBars[count].open = price;
      RecentSecondBars[count].high = price;
      RecentSecondBars[count].low = price;
      RecentSecondBars[count].close = price;
      RecentSecondBars[count].tick_volume = 1;
      RecentSecondBars[count].spread = spread;
      RecentSecondBars[count].real_volume = MathMax(0.0,tick.volume_real);
      RecentSecondBars[count].is_closed = false;
   }
   else if(startUnix == RecentSecondBars[count-1].start_unix)
   {
      RecentSecondBars[count-1].high = MathMax(RecentSecondBars[count-1].high,price);
      RecentSecondBars[count-1].low = MathMin(RecentSecondBars[count-1].low,price);
      RecentSecondBars[count-1].close = price;
      RecentSecondBars[count-1].tick_volume++;
      RecentSecondBars[count-1].spread = spread;
      RecentSecondBars[count-1].real_volume += MathMax(0.0,tick.volume_real);
   }
   else
   {
      return;
   }

   count = ArraySize(RecentSecondBars);
   int maximum = InpRecentSecondWindow < 60 ? 60 : InpRecentSecondWindow;
   if(count > maximum)
   {
      int remove = count-maximum;
      for(int index=remove; index<count; index++)
         RecentSecondBars[index-remove] = RecentSecondBars[index];
      ArrayResize(RecentSecondBars,maximum);
   }

   RecentSecondsDirty = true;
}

bool WriteSecondBarFile(
   const string fileName,
   const TickLabSecondBar &bar,
   const bool isClosed)
{
   if(StringLen(CaptureSymbol) == 0 || bar.start_unix <= 0)
      return false;

   string targetPath = ConnectorFolder + "\\" + fileName;
   string temporaryPath = targetPath + ".tmp";
   int handle = FileOpen(
      temporaryPath,
      FILE_WRITE|FILE_CSV|FILE_ANSI|FILE_COMMON|FILE_SHARE_READ,
      ',',
      CP_UTF8);

   if(handle == INVALID_HANDLE)
      return false;

   WriteCandleHeader(handle);
   int digits = (int)SymbolInfoInteger(CaptureSymbol,SYMBOL_DIGITS);
   double point = SymbolInfoDouble(CaptureSymbol,SYMBOL_POINT);

   FileWrite(
      handle,
      CaptureSymbol,
      "1s",
      IntegerToString(digits),
      DoubleToString(point,digits),
      IntegerToString(bar.start_unix),
      IntegerToString(bar.start_unix+1),
      TimeToString((datetime)bar.start_unix,TIME_DATE|TIME_SECONDS),
      DoubleToString(bar.open,digits),
      DoubleToString(bar.high,digits),
      DoubleToString(bar.low,digits),
      DoubleToString(bar.close,digits),
      IntegerToString(bar.tick_volume),
      IntegerToString(bar.spread),
      IntegerToString((long)MathRound(bar.real_volume)),
      BoolToJson(isClosed));

   FileFlush(handle);
   FileClose(handle);
   return ReplaceCommonFile(temporaryPath,targetPath);
}

bool WriteLiveSecondBarFile()

{
   if(IsHistoryWorker())
      return false;
   int count = ArraySize(RecentSecondBars);
   if(count <= 0)
      return false;

   return WriteSecondBarFile(
      "second_live.csv",
      RecentSecondBars[count-1],
      false);
}

bool WriteRecentSecondBarsFile()

{
   if(IsHistoryWorker())
      return false;
   if(!RecentSecondsDirty && FileIsExist(ConnectorFolder+"\\seconds_recent.csv",FILE_COMMON))
      return true;

   string targetPath = ConnectorFolder+"\\seconds_recent.csv";
   string temporaryPath = targetPath+".tmp";
   int handle = FileOpen(
      temporaryPath,
      FILE_WRITE|FILE_CSV|FILE_ANSI|FILE_COMMON|FILE_SHARE_READ,
      ',',
      CP_UTF8);

   if(handle == INVALID_HANDLE)
      return false;

   WriteCandleHeader(handle);
   int digits = (int)SymbolInfoInteger(CaptureSymbol,SYMBOL_DIGITS);
   double point = SymbolInfoDouble(CaptureSymbol,SYMBOL_POINT);
   int count = ArraySize(RecentSecondBars);

   for(int index=0; index<count; index++)
   {
      TickLabSecondBar bar = RecentSecondBars[index];
      FileWrite(
         handle,
         CaptureSymbol,
         "1s",
         IntegerToString(digits),
         DoubleToString(point,digits),
         IntegerToString(bar.start_unix),
         IntegerToString(bar.start_unix+1),
         TimeToString((datetime)bar.start_unix,TIME_DATE|TIME_SECONDS),
         DoubleToString(bar.open,digits),
         DoubleToString(bar.high,digits),
         DoubleToString(bar.low,digits),
         DoubleToString(bar.close,digits),
         IntegerToString(bar.tick_volume),
         IntegerToString(bar.spread),
         IntegerToString((long)MathRound(bar.real_volume)),
         BoolToJson(bar.is_closed));
   }

   FileFlush(handle);
   FileClose(handle);
   bool replaced = ReplaceCommonFile(temporaryPath,targetPath);
   if(replaced) RecentSecondsDirty = false;
   return replaced;
}

//+------------------------------------------------------------------+
//| Discover the first tick MT5/broker currently makes available     |
//+------------------------------------------------------------------+
long DiscoverFirstAvailableTickMsc()
{
   datetime terminalFirst = 0;
   datetime serverFirst = 0;

   SeriesInfoInteger(
      HistorySymbol,
      PERIOD_M1,
      SERIES_TERMINAL_FIRSTDATE,
      terminalFirst);

   SeriesInfoInteger(
      HistorySymbol,
      PERIOD_M1,
      SERIES_SERVER_FIRSTDATE,
      serverFirst);

   datetime oldest = 0;
   if(terminalFirst > 0)
      oldest = terminalFirst;
   if(serverFirst > 0 && (oldest <= 0 || serverFirst < oldest))
      oldest = serverFirst;

   if(oldest > 0)
      return (long)oldest * 1000;

   MqlTick firstTick[];
   ResetLastError();

   int copied =
      CopyTicks(
         HistorySymbol,
         firstTick,
         COPY_TICKS_ALL,
         1,
         1);

   if(copied == 1)
      return firstTick[0].time_msc;

   return 0;
}

//+------------------------------------------------------------------+
//| Backfill one historical tick time segment per timer event        |
//+------------------------------------------------------------------+
void ProcessHistoricalTickBackfillStep()
{
   if(!InpBackfillAllHistoricalTicks ||
      HistoricalTickBackfillComplete ||
      StringLen(HistorySymbol) == 0)
   {
      return;
   }

   // Live ticks belong to CaptureSymbol and remain completely independent
   // from the HistorySymbol range being copied below.
   CaptureAllLiveTicks();

   if(HistoricalCursorMsc <= 0)
   {
      HistoricalCursorMsc = DiscoverFirstAvailableTickMsc();

      if(HistoricalCursorMsc <= 0)
      {
         LastCaptureMessage =
            "waiting for MT5 tick-history synchronization";
         return;
      }
   }

   if(HistoricalCursorMsc > HistoricalEndMsc)
   {
      HistoricalTickBackfillComplete = true;
      LastCaptureMessage = "historical tick backfill complete";
      CaptureAllLiveTicks();
      WriteLiveCandleFile();
      return;
   }

   long chunkMilliseconds =
      (long)InpHistoricalTickChunkMinutes * 60 * 1000;

   long segmentStart = HistoricalCursorMsc;
   long segmentEnd =
      MathMin(
         HistoricalEndMsc,
         segmentStart + chunkMilliseconds - 1);

   MqlTick ticks[];
   ResetLastError();

   int copied =
      CopyTicksRange(
         HistorySymbol,
         ticks,
         COPY_TICKS_ALL,
         (ulong)segmentStart,
         (ulong)segmentEnd);

   int copyError = GetLastError();

   CaptureAllLiveTicks();
   WriteLiveCandleFile();

   if(copied < 0)
   {
      LastCaptureMessage =
         "historical CopyTicksRange error " +
         IntegerToString(copyError);
      return;
   }

   if(!WriteHistoricalTickSegment(
         segmentStart,
         segmentEnd,
         ticks,
         copied))
   {
      LastCaptureMessage =
         "could not write historical tick segment";
      CaptureAllLiveTicks();
      return;
   }

   CaptureAllLiveTicks();
   WriteLiveCandleFile();

   if(copyError != 0)
   {
      LastCaptureMessage =
         "historical ticks synchronizing; segment will retry";
      ArrayFree(ticks);
      return;
   }

   HistoricalTicksArchived += copied;
   HistoricalCursorMsc = segmentEnd + 1;

   if(HistoricalCursorMsc > HistoricalEndMsc)
      HistoricalTickBackfillComplete = true;

   LastCaptureMessage =
      HistoricalTickBackfillComplete
         ? "historical tick backfill complete"
         : "historical tick segment archived";

   ArrayFree(ticks);
}

//+------------------------------------------------------------------+
//| Write one atomic historical tick segment                         |
//+------------------------------------------------------------------+
bool WriteHistoricalTickSegment(
   const long segmentStart,
   const long segmentEnd,
   MqlTick &ticks[],
   const int count)
{
   string targetPath =
      ConnectorFolder +
      "\\ticks_history_" +
      SanitizeFilePart(HistorySymbol) +
      "_" +
      IntegerToString(segmentStart) +
      "_" +
      IntegerToString(segmentEnd) +
      ".csv";

   string temporaryPath = targetPath + ".tmp";

   int handle =
      FileOpen(
         temporaryPath,
         FILE_WRITE |
         FILE_CSV |
         FILE_ANSI |
         FILE_COMMON |
         FILE_SHARE_READ,
         ',',
         CP_UTF8);

   if(handle == INVALID_HANDLE)
      return false;

   WriteTickArchiveHeader(handle);

   for(int index = 0; index < count; index++)
   {
      WriteTickArchiveRowForSymbol(
         handle,
         "history",
         index + 1,
         HistorySymbol,
         ticks[index]);

      if(index % 500 == 0)
         MaintainBridgeHeartbeat(false);
   }

   FileFlush(handle);
   FileClose(handle);

   return ReplaceCommonFile(temporaryPath,targetPath);
}

//+------------------------------------------------------------------+
//| Open a daily append-only live tick file                          |
//+------------------------------------------------------------------+
void CloseLiveTickArchiveFile(const bool flush)
{
   if(LiveTickFileHandle == INVALID_HANDLE)
      return;

   if(flush)
      FileFlush(LiveTickFileHandle);
   FileClose(LiveTickFileHandle);
   LiveTickFileHandle = INVALID_HANDLE;
   LiveTickFilePath = "";
}

bool EnsureLiveTickArchiveFile(const string path)
{
   if(LiveTickFileHandle != INVALID_HANDLE && path == LiveTickFilePath)
      return true;

   CloseLiveTickArchiveFile(true);
   LiveTickFileHandle = OpenTickArchiveFile(path);
   if(LiveTickFileHandle == INVALID_HANDLE)
      return false;

   LiveTickFilePath = path;
   LastLiveTickFlushTick = GetTickCount64();
   return true;
}

int OpenTickArchiveFile(const string path)
{
   int handle =
      FileOpen(
         path,
         FILE_READ |
         FILE_WRITE |
         FILE_CSV |
         FILE_ANSI |
         FILE_COMMON |
         FILE_SHARE_READ |
         FILE_SHARE_WRITE,
         ',',
         CP_UTF8);

   if(handle == INVALID_HANDLE)
      return INVALID_HANDLE;

   if(FileSize(handle) == 0)
      WriteTickArchiveHeader(handle);

   FileSeek(handle, 0, SEEK_END);
   return handle;
}

//+------------------------------------------------------------------+
//| Tick archive CSV header                                          |
//+------------------------------------------------------------------+
void WriteTickArchiveHeader(const int handle)
{
   FileWrite(
      handle,
      "source",
      "sequence",
      "symbol",
      "time_msc",
      "time",
      "bid",
      "ask",
      "last",
      "volume",
      "flags",
      "volume_real",
      "bid_changed",
      "ask_changed",
      "last_changed",
      "volume_changed",
      "buy_tick",
      "sell_tick",
      "received_monotonic_us");
}

//+------------------------------------------------------------------+
//| Complete MqlTick archive row                                     |
//+------------------------------------------------------------------+
void WriteTickArchiveRowForSymbol(
   const int handle,
   const string source,
   const long sequence,
   const string symbol,
   const MqlTick &tick)
{
   int digits =
      (int)SymbolInfoInteger(
         symbol,
         SYMBOL_DIGITS);

   FileWrite(
      handle,
      source,
      IntegerToString(sequence),
      symbol,
      IntegerToString(tick.time_msc),
      IntegerToString((long)tick.time),
      DoubleToString(tick.bid, digits),
      DoubleToString(tick.ask, digits),
      DoubleToString(tick.last, digits),
      IntegerToString((long)tick.volume),
      IntegerToString((long)tick.flags),
      DoubleToString(tick.volume_real, 8),
      BoolToJson((tick.flags & TICK_FLAG_BID) != 0),
      BoolToJson((tick.flags & TICK_FLAG_ASK) != 0),
      BoolToJson((tick.flags & TICK_FLAG_LAST) != 0),
      BoolToJson((tick.flags & TICK_FLAG_VOLUME) != 0),
      BoolToJson((tick.flags & TICK_FLAG_BUY) != 0),
      BoolToJson((tick.flags & TICK_FLAG_SELL) != 0),
      StringFormat("%I64u", GetMicrosecondCount()));
}

void WriteTickArchiveRow(
   const int handle,
   const string source,
   const long sequence,
   const MqlTick &tick)
{
   WriteTickArchiveRowForSymbol(
      handle,
      source,
      sequence,
      CaptureSymbol,
      tick);
}

//+------------------------------------------------------------------+
//| Subscribe/capture one complete DOM snapshot per BookEvent        |
//+------------------------------------------------------------------+
void AppendMarketBookSnapshot(const string symbol)
{
   MqlBookInfo book[];

   if(!MarketBookGet(symbol, book))
      return;

   MqlTick marketTick;
   SymbolInfoTick(symbol, marketTick);

   long eventMsc =
      marketTick.time_msc > 0
         ? marketTick.time_msc
         : (long)TimeGMT() * 1000;

   string path =
      ConnectorFolder +
      "\\market_book_" +
      SanitizeFilePart(symbol) +
      "_" +
      DateKeyFromMilliseconds(eventMsc) +
      ".csv";

   int handle = OpenAppendCsvFile(path);

   if(handle == INVALID_HANDLE)
      return;

   if(FileTell(handle) == 0)
   {
      FileWrite(
         handle,
         "event_sequence",
         "symbol",
         "market_tick_msc",
         "received_unix",
         "received_monotonic_us",
         "level",
         "type",
         "price",
         "volume",
         "volume_real");
   }

   BookEventSequence++;
   int digits =
      (int)SymbolInfoInteger(symbol, SYMBOL_DIGITS);
   string receivedUs =
      StringFormat("%I64u", GetMicrosecondCount());
   long receivedUnix = (long)TimeGMT();

   int levels = ArraySize(book);

   for(int level = 0; level < levels; level++)
   {
      FileWrite(
         handle,
         IntegerToString(BookEventSequence),
         symbol,
         IntegerToString(eventMsc),
         IntegerToString(receivedUnix),
         receivedUs,
         IntegerToString(level),
         EnumToString(book[level].type),
         DoubleToString(book[level].price, digits),
         IntegerToString(book[level].volume),
         DoubleToString(book[level].volume_real, 8));
   }

   FileFlush(handle);
   FileClose(handle);
}

//+------------------------------------------------------------------+
//| Capture account trade-server transactions                        |
//+------------------------------------------------------------------+
void AppendTradeTransaction(
   const MqlTradeTransaction &trans,
   const MqlTradeRequest &request,
   const MqlTradeResult &result)
{
   long eventMsc = (long)TimeGMT() * 1000;
   MqlTick tick;

   if(StringLen(trans.symbol) > 0 &&
      SymbolInfoTick(trans.symbol, tick) &&
      tick.time_msc > 0)
   {
      eventMsc = tick.time_msc;
   }

   string path =
      ConnectorFolder +
      "\\trade_transactions_" +
      DateKeyFromMilliseconds(eventMsc) +
      ".csv";

   int handle = OpenAppendCsvFile(path);

   if(handle == INVALID_HANDLE)
      return;

   if(FileTell(handle) == 0)
   {
      FileWrite(
         handle,
         "sequence",
         "received_unix",
         "received_monotonic_us",
         "transaction_type",
         "deal",
         "order",
         "symbol",
         "order_type",
         "order_state",
         "deal_type",
         "time_type",
         "time_expiration",
         "price",
         "price_trigger",
         "price_sl",
         "price_tp",
         "volume",
         "position",
         "position_by",
         "request_action",
         "request_magic",
         "request_comment",
         "result_retcode",
         "result_deal",
         "result_order",
         "result_volume",
         "result_price",
         "result_bid",
         "result_ask",
         "result_comment",
         "result_request_id");
   }

   TradeEventSequence++;

   FileWrite(
      handle,
      IntegerToString(TradeEventSequence),
      IntegerToString((long)TimeGMT()),
      StringFormat("%I64u", GetMicrosecondCount()),
      EnumToString(trans.type),
      StringFormat("%I64u", trans.deal),
      StringFormat("%I64u", trans.order),
      trans.symbol,
      EnumToString(trans.order_type),
      EnumToString(trans.order_state),
      EnumToString(trans.deal_type),
      EnumToString(trans.time_type),
      IntegerToString((long)trans.time_expiration),
      DoubleToString(trans.price, 10),
      DoubleToString(trans.price_trigger, 10),
      DoubleToString(trans.price_sl, 10),
      DoubleToString(trans.price_tp, 10),
      DoubleToString(trans.volume, 8),
      StringFormat("%I64u", trans.position),
      StringFormat("%I64u", trans.position_by),
      EnumToString(request.action),
      StringFormat("%I64u", request.magic),
      request.comment,
      IntegerToString((long)result.retcode),
      StringFormat("%I64u", result.deal),
      StringFormat("%I64u", result.order),
      DoubleToString(result.volume, 8),
      DoubleToString(result.price, 10),
      DoubleToString(result.bid, 10),
      DoubleToString(result.ask, 10),
      result.comment,
      IntegerToString((long)result.request_id));

   FileFlush(handle);
   FileClose(handle);
}

//+------------------------------------------------------------------+
//| Generic append-only CSV opener                                   |
//+------------------------------------------------------------------+
int OpenAppendCsvFile(const string path)
{
   int handle =
      FileOpen(
         path,
         FILE_READ |
         FILE_WRITE |
         FILE_CSV |
         FILE_ANSI |
         FILE_COMMON |
         FILE_SHARE_READ |
         FILE_SHARE_WRITE,
         ',',
         CP_UTF8);

   if(handle == INVALID_HANDLE)
      return INVALID_HANDLE;

   FileSeek(handle, 0, SEEK_END);
   return handle;
}

//+------------------------------------------------------------------+
//| Bridge capabilities                                             |
//+------------------------------------------------------------------+
bool WriteCapabilitiesFile()
{
   string json =
      "{\r\n" +
      "  \"protocol_version\": " + IntegerToString(ProtocolVersion) + ",\r\n" +
      "  \"bridge_version\": \"" + EscapeJson(BridgeVersion) + "\",\r\n" +
      "  \"permanent_bridge_name\": \"TickLabLiveBridge_V300\",\r\n" +
      "  \"read_only\": true,\r\n" +
      "  \"candle_history\": true,\r\n" +
      "  \"live_candle\": true,\r\n" +
      "  \"all_terminal_symbols\": true,\r\n" +
      "  \"independent_chart_symbol_selection\": false,\r\n" +
      "  \"attached_chart_projection\": false,\r\n" +
      "  \"ticklab_can_change_attached_chart\": true,\r\n" +
      "  \"symbols_file\": \"symbols.psv\",\r\n" +
      "  \"selected_tick_ranges\": true,\r\n" +
      "  \"continuous_live_tick_archive\": " + BoolToJson(InpCaptureAllLiveTicks) + ",\r\n" +
      "  \"automatic_historical_tick_backfill\": " + BoolToJson(InpBackfillAllHistoricalTicks) + ",\r\n" +
      "  \"tick_fields\": \"time,time_msc,bid,ask,last,volume,flags,volume_real\",\r\n" +
      "  \"broker_tick_timestamp_precision\": \"milliseconds\",\r\n" +
      "  \"local_receive_counter_precision\": \"microseconds\",\r\n" +
      "  \"nanosecond_broker_timestamps\": false,\r\n" +
      "  \"market_book_requested\": " + BoolToJson(InpCaptureMarketBook) + ",\r\n" +
      "  \"market_book_subscribed\": " + BoolToJson(BookSubscribed) + ",\r\n" +
      "  \"trade_transactions\": " + BoolToJson(InpCaptureTradeTransactions) + ",\r\n" +
      "  \"symbol_account_terminal_snapshots\": " + BoolToJson(InpCaptureSnapshots) + ",\r\n" +
      "  \"updated_unix\": " + IntegerToString((long)TimeGMT()) + "\r\n" +
      "}\r\n";

   return WriteTextAtomic(
      ConnectorFolder + "\\capabilities.json",
      json);
}

//+------------------------------------------------------------------+
//| Current symbol/account/terminal snapshots                        |
//+------------------------------------------------------------------+
void WriteUniversalSnapshots()
{
   if(!InpCaptureSnapshots)
      return;

   WriteSymbolSnapshot();
   WriteAccountSnapshot();
   WriteTerminalSnapshot();
}

bool WriteSymbolSnapshot()
{
   MqlTick tick;
   SymbolInfoTick(CaptureSymbol, tick);

   int digits =
      (int)SymbolInfoInteger(
         CaptureSymbol,
         SYMBOL_DIGITS);

   string json =
      "{\r\n" +
      "  \"symbol\": \"" + EscapeJson(CaptureSymbol) + "\",\r\n" +
      "  \"description\": \"" + EscapeJson(SymbolInfoString(CaptureSymbol, SYMBOL_DESCRIPTION)) + "\",\r\n" +
      "  \"currency_base\": \"" + EscapeJson(SymbolInfoString(CaptureSymbol, SYMBOL_CURRENCY_BASE)) + "\",\r\n" +
      "  \"currency_profit\": \"" + EscapeJson(SymbolInfoString(CaptureSymbol, SYMBOL_CURRENCY_PROFIT)) + "\",\r\n" +
      "  \"currency_margin\": \"" + EscapeJson(SymbolInfoString(CaptureSymbol, SYMBOL_CURRENCY_MARGIN)) + "\",\r\n" +
      "  \"digits\": " + IntegerToString(digits) + ",\r\n" +
      "  \"point\": " + DoubleToString(SymbolInfoDouble(CaptureSymbol, SYMBOL_POINT), digits) + ",\r\n" +
      "  \"tick_size\": " + DoubleToString(SymbolInfoDouble(CaptureSymbol, SYMBOL_TRADE_TICK_SIZE), 10) + ",\r\n" +
      "  \"tick_value\": " + DoubleToString(SymbolInfoDouble(CaptureSymbol, SYMBOL_TRADE_TICK_VALUE), 10) + ",\r\n" +
      "  \"contract_size\": " + DoubleToString(SymbolInfoDouble(CaptureSymbol, SYMBOL_TRADE_CONTRACT_SIZE), 8) + ",\r\n" +
      "  \"volume_min\": " + DoubleToString(SymbolInfoDouble(CaptureSymbol, SYMBOL_VOLUME_MIN), 8) + ",\r\n" +
      "  \"volume_max\": " + DoubleToString(SymbolInfoDouble(CaptureSymbol, SYMBOL_VOLUME_MAX), 8) + ",\r\n" +
      "  \"volume_step\": " + DoubleToString(SymbolInfoDouble(CaptureSymbol, SYMBOL_VOLUME_STEP), 8) + ",\r\n" +
      "  \"spread_points\": " + IntegerToString(SymbolInfoInteger(CaptureSymbol, SYMBOL_SPREAD)) + ",\r\n" +
      "  \"spread_floating\": " + BoolToJson(SymbolInfoInteger(CaptureSymbol, SYMBOL_SPREAD_FLOAT) != 0) + ",\r\n" +
      "  \"book_depth\": " + IntegerToString(SymbolInfoInteger(CaptureSymbol, SYMBOL_TICKS_BOOKDEPTH)) + ",\r\n" +
      "  \"trade_mode\": " + IntegerToString(SymbolInfoInteger(CaptureSymbol, SYMBOL_TRADE_MODE)) + ",\r\n" +
      "  \"stops_level\": " + IntegerToString(SymbolInfoInteger(CaptureSymbol, SYMBOL_TRADE_STOPS_LEVEL)) + ",\r\n" +
      "  \"freeze_level\": " + IntegerToString(SymbolInfoInteger(CaptureSymbol, SYMBOL_TRADE_FREEZE_LEVEL)) + ",\r\n" +
      "  \"time\": " + IntegerToString((long)tick.time) + ",\r\n" +
      "  \"time_msc\": " + IntegerToString(tick.time_msc) + ",\r\n" +
      "  \"bid\": " + DoubleToString(tick.bid, digits) + ",\r\n" +
      "  \"ask\": " + DoubleToString(tick.ask, digits) + ",\r\n" +
      "  \"last\": " + DoubleToString(tick.last, digits) + ",\r\n" +
      "  \"volume\": " + IntegerToString((long)tick.volume) + ",\r\n" +
      "  \"volume_real\": " + DoubleToString(tick.volume_real, 8) + ",\r\n" +
      "  \"flags\": " + IntegerToString((long)tick.flags) + ",\r\n" +
      "  \"updated_unix\": " + IntegerToString((long)TimeGMT()) + "\r\n" +
      "}\r\n";

   return WriteTextAtomic(
      ConnectorFolder + "\\symbol_snapshot.json",
      json);
}

bool WriteAccountSnapshot()
{
   string json =
      "{\r\n" +
      "  \"login\": " + IntegerToString(AccountInfoInteger(ACCOUNT_LOGIN)) + ",\r\n" +
      "  \"name\": \"" + EscapeJson(AccountInfoString(ACCOUNT_NAME)) + "\",\r\n" +
      "  \"company\": \"" + EscapeJson(AccountInfoString(ACCOUNT_COMPANY)) + "\",\r\n" +
      "  \"server\": \"" + EscapeJson(AccountInfoString(ACCOUNT_SERVER)) + "\",\r\n" +
      "  \"currency\": \"" + EscapeJson(AccountInfoString(ACCOUNT_CURRENCY)) + "\",\r\n" +
      "  \"leverage\": " + IntegerToString(AccountInfoInteger(ACCOUNT_LEVERAGE)) + ",\r\n" +
      "  \"trade_mode\": " + IntegerToString(AccountInfoInteger(ACCOUNT_TRADE_MODE)) + ",\r\n" +
      "  \"trade_allowed\": " + BoolToJson(AccountInfoInteger(ACCOUNT_TRADE_ALLOWED) != 0) + ",\r\n" +
      "  \"trade_expert\": " + BoolToJson(AccountInfoInteger(ACCOUNT_TRADE_EXPERT) != 0) + ",\r\n" +
      "  \"balance\": " + DoubleToString(AccountInfoDouble(ACCOUNT_BALANCE), 8) + ",\r\n" +
      "  \"credit\": " + DoubleToString(AccountInfoDouble(ACCOUNT_CREDIT), 8) + ",\r\n" +
      "  \"profit\": " + DoubleToString(AccountInfoDouble(ACCOUNT_PROFIT), 8) + ",\r\n" +
      "  \"equity\": " + DoubleToString(AccountInfoDouble(ACCOUNT_EQUITY), 8) + ",\r\n" +
      "  \"margin\": " + DoubleToString(AccountInfoDouble(ACCOUNT_MARGIN), 8) + ",\r\n" +
      "  \"margin_free\": " + DoubleToString(AccountInfoDouble(ACCOUNT_MARGIN_FREE), 8) + ",\r\n" +
      "  \"margin_level\": " + DoubleToString(AccountInfoDouble(ACCOUNT_MARGIN_LEVEL), 8) + ",\r\n" +
      "  \"updated_unix\": " + IntegerToString((long)TimeGMT()) + "\r\n" +
      "}\r\n";

   return WriteTextAtomic(
      ConnectorFolder + "\\account_snapshot.json",
      json);
}

bool WriteTerminalSnapshot()
{
   string json =
      "{\r\n" +
      "  \"name\": \"" + EscapeJson(TerminalInfoString(TERMINAL_NAME)) + "\",\r\n" +
      "  \"company\": \"" + EscapeJson(TerminalInfoString(TERMINAL_COMPANY)) + "\",\r\n" +
      "  \"path\": \"" + EscapeJson(TerminalInfoString(TERMINAL_PATH)) + "\",\r\n" +
      "  \"data_path\": \"" + EscapeJson(TerminalInfoString(TERMINAL_DATA_PATH)) + "\",\r\n" +
      "  \"commondata_path\": \"" + EscapeJson(TerminalInfoString(TERMINAL_COMMONDATA_PATH)) + "\",\r\n" +
      "  \"build\": " + IntegerToString(TerminalInfoInteger(TERMINAL_BUILD)) + ",\r\n" +
      "  \"connected\": " + BoolToJson(TerminalInfoInteger(TERMINAL_CONNECTED) != 0) + ",\r\n" +
      "  \"trade_allowed\": " + BoolToJson(TerminalInfoInteger(TERMINAL_TRADE_ALLOWED) != 0) + ",\r\n" +
      "  \"max_bars\": " + IntegerToString(TerminalInfoInteger(TERMINAL_MAXBARS)) + ",\r\n" +
      "  \"x64\": " + BoolToJson(TerminalInfoInteger(TERMINAL_X64) != 0) + ",\r\n" +
      "  \"updated_unix\": " + IntegerToString((long)TimeGMT()) + "\r\n" +
      "}\r\n";

   return WriteTextAtomic(
      ConnectorFolder + "\\terminal_snapshot.json",
      json);
}

//+------------------------------------------------------------------+
//| Capture progress/status                                          |
//+------------------------------------------------------------------+
bool WriteCaptureStatus(const string bridgeStatus)
{
   string json =
      "{\r\n" +
      "  \"bridge_status\": \"" + EscapeJson(bridgeStatus) + "\",\r\n" +
      "  \"symbol\": \"" + EscapeJson(CaptureSymbol) + "\",\r\n" +
      "  \"live_cursor_msc\": " + IntegerToString(LiveCursorMsc) + ",\r\n" +
      "  \"live_ticks_archived\": " + IntegerToString(LiveTicksArchived) + ",\r\n" +
      "  \"historical_cursor_msc\": " + IntegerToString(HistoricalCursorMsc) + ",\r\n" +
      "  \"historical_end_msc\": " + IntegerToString(HistoricalEndMsc) + ",\r\n" +
      "  \"historical_complete\": " + BoolToJson(HistoricalTickBackfillComplete) + ",\r\n" +
      "  \"historical_ticks_archived\": " + IntegerToString(HistoricalTicksArchived) + ",\r\n" +
      "  \"book_subscribed\": " + BoolToJson(BookSubscribed) + ",\r\n" +
      "  \"book_event_sequence\": " + IntegerToString(BookEventSequence) + ",\r\n" +
      "  \"trade_event_sequence\": " + IntegerToString(TradeEventSequence) + ",\r\n" +
      "  \"message\": \"" + EscapeJson(LastCaptureMessage) + "\",\r\n" +
      "  \"updated_unix\": " + IntegerToString((long)TimeGMT()) + "\r\n" +
      "}\r\n";

   return WriteTextAtomic(
      ConnectorFolder + "\\capture_status.json",
      json);
}

//+------------------------------------------------------------------+
//| Safe filename component                                          |
//+------------------------------------------------------------------+
string SanitizeFilePart(string value)
{
   StringReplace(value, "\\", "_");
   StringReplace(value, "/", "_");
   StringReplace(value, ":", "_");
   StringReplace(value, "*", "_");
   StringReplace(value, "?", "_");
   StringReplace(value, "\"", "_");
   StringReplace(value, "<", "_");
   StringReplace(value, ">", "_");
   StringReplace(value, "|", "_");
   return value;
}

//+------------------------------------------------------------------+
//| YYYYMMDD key from a broker tick timestamp                        |
//+------------------------------------------------------------------+
string DateKeyFromMilliseconds(const long timeMsc)
{
   datetime seconds =
      (datetime)(timeMsc / 1000);

   MqlDateTime parts;
   TimeToStruct(seconds, parts);

   return StringFormat(
      "%04d%02d%02d",
      parts.year,
      parts.mon,
      parts.day);
}

//+------------------------------------------------------------------+
//| Ask MT5 to continue synchronizing the oldest server history      |
//+------------------------------------------------------------------+
void RequestOldestAvailableHistory()
{
   datetime serverFirst =
      (datetime)SeriesInfoInteger(
         HistorySymbol,
         HistoryTimeframe,
         SERIES_SERVER_FIRSTDATE);

   if(serverFirst <= 0)
      return;

   int periodSeconds = PeriodSeconds(HistoryTimeframe);

   if(periodSeconds <= 0)
      periodSeconds = 60;

   MqlRates probe[];
   ArraySetAsSeries(probe, true);

   ResetLastError();

   // Start history synchronization without changing the user's MT5 chart
   // or the independent TickLab live projection source.
   CopyRates(
      HistorySymbol,
      HistoryTimeframe,
      serverFirst + periodSeconds * 2,
      1,
      probe);
}

//+------------------------------------------------------------------+
//| Export full MT5 terminal history when it changes                 |
//+------------------------------------------------------------------+
bool RefreshFullHistory(const bool force)
{
   bool synchronized =
      SeriesInfoInteger(
         HistorySymbol,
         HistoryTimeframe,
         SERIES_SYNCHRONIZED) != 0;

   int totalBars =
      Bars(
         HistorySymbol,
         HistoryTimeframe);

   datetime firstDate =
      (datetime)SeriesInfoInteger(
         HistorySymbol,
         HistoryTimeframe,
         SERIES_FIRSTDATE);

   datetime latestBarTime =
      (datetime)SeriesInfoInteger(
         HistorySymbol,
         HistoryTimeframe,
         SERIES_LASTBAR_DATE);

   if(totalBars <= 0)
   {
      WriteHistoryStatus(
         0,
         firstDate,
         latestBarTime,
         synchronized,
         "synchronizing");

      return false;
   }

   bool changed =
      force ||
      totalBars != LastExportedBarCount ||
      firstDate != LastExportedFirstDate ||
      latestBarTime != LastExportedLatestBarTime;

   if(!changed)
      return true;

   WriteHistoryStatus(
      LastExportedBarCount > 0
         ? LastExportedBarCount
         : 0,
      firstDate,
      latestBarTime,
      synchronized,
      "exporting");

   MaintainBridgeHeartbeat(true);

   if(!WriteFullCandlesFile(totalBars))
   {
      MaintainBridgeHeartbeat(true);
      return false;
   }

   LastExportedBarCount = totalBars;
   LastExportedFirstDate = firstDate;
   LastExportedLatestBarTime = latestBarTime;

   WriteHistoryStatus(
      totalBars,
      firstDate,
      latestBarTime,
      synchronized,
      HistoryRequestIncludesTicks &&
      !HistoricalTickBackfillComplete
         ? "candles_ready"
         : "ready");

   Print(
      "TickLab: exported all ",
      totalBars,
      " MT5 bars | ",
      HistorySymbol,
      " | ",
      EnumToString(HistoryTimeframe));

   return true;
}

//+------------------------------------------------------------------+
//| Exact native chart bootstrap - written only on source selection  |
//+------------------------------------------------------------------+
bool WriteChartBootstrapFile()
{
   if(IsHistoryWorker())
      return false;

   MqlRates rates[];
   ArraySetAsSeries(rates,true);

   int copied = CopyRates(
      CaptureSymbol,
      DataTimeframe,
      0,
      InpBootstrapBars,
      rates);

   if(copied <= 0)
      return false;

   string targetPath = ConnectorFolder + "\\chart_bootstrap.csv";
   string temporaryPath = targetPath + ".tmp";
   int handle = FileOpen(
      temporaryPath,
      FILE_WRITE | FILE_CSV | FILE_ANSI | FILE_COMMON | FILE_SHARE_READ,
      ',',
      CP_UTF8);

   if(handle == INVALID_HANDLE)
   {
      ArrayFree(rates);
      return false;
   }

   WriteCandleHeader(handle);
   for(int index = copied - 1; index >= 0; index--)
   {
      WriteCandleRowForTimeframe(
         handle,
         rates[index],
         index != 0,
         DataTimeframe);
   }

   FileFlush(handle);
   FileClose(handle);
   ArrayFree(rates);
   return ReplaceCommonFile(temporaryPath,targetPath);
}

//+------------------------------------------------------------------+
//| Native M1 integrity snapshot - independent of chart timeframe    |
//+------------------------------------------------------------------+
bool WriteM1RecentFile()

{
   if(IsHistoryWorker())
      return false;
   MqlRates rates[];
   ArraySetAsSeries(rates, true);

   int copied = CopyRates(CaptureSymbol, PERIOD_M1, 0, 31, rates);
   if(copied <= 0)
      return false;

   string targetPath = ConnectorFolder + "\\m1_recent.csv";
   string temporaryPath = targetPath + ".tmp";
   int handle = FileOpen(
      temporaryPath,
      FILE_WRITE | FILE_CSV | FILE_ANSI | FILE_COMMON | FILE_SHARE_READ,
      ',',
      CP_UTF8);

   if(handle == INVALID_HANDLE)
      return false;

   WriteCandleHeader(handle);
   for(int index = copied - 1; index >= 0; index--)
   {
      WriteCandleRowForTimeframe(
         handle,
         rates[index],
         index != 0,
         PERIOD_M1);
   }

   FileFlush(handle);
   FileClose(handle);
   ArrayFree(rates);
   return ReplaceCommonFile(temporaryPath, targetPath);
}

//+------------------------------------------------------------------+
//| Atomic recent raw-tick repair snapshot (moving last 30 minutes)  |
//+------------------------------------------------------------------+
bool WriteRecentTickSnapshot()

{
   if(IsHistoryWorker())
      return false;
   long nowMsc = (long)TimeCurrent() * 1000;
   long windowStart = nowMsc - 30 * 60 * 1000;
   if(windowStart < 0) windowStart = 0;

   if(RecentTickRepairCursorMsc < windowStart ||
      RecentTickRepairCursorMsc >= nowMsc)
   {
      RecentTickRepairCursorMsc = windowStart;
   }

   long fromMsc = RecentTickRepairCursorMsc;
   long toMsc = MathMin(
      nowMsc,
      fromMsc + (long)InpRecentTickRepairSliceSeconds * 1000 - 1);

   MqlTick ticks[];
   int copied = CopyTicksRange(
      CaptureSymbol,
      ticks,
      COPY_TICKS_ALL,
      (ulong)fromMsc,
      (ulong)toMsc);

   if(copied < 0)
      return false;

   string targetPath =
      ConnectorFolder +
      "\\ticks_recent_" +
      SanitizeFilePart(CaptureSymbol) +
      ".csv";
   string temporaryPath = targetPath + ".tmp";

   int handle = FileOpen(
      temporaryPath,
      FILE_WRITE | FILE_CSV | FILE_ANSI | FILE_COMMON | FILE_SHARE_READ,
      ',',
      CP_UTF8);
   if(handle == INVALID_HANDLE)
   {
      ArrayFree(ticks);
      return false;
   }

   WriteTickArchiveHeader(handle);
   for(int index = 0; index < copied; index++)
      WriteTickArchiveRow(handle, "recent", index + 1, ticks[index]);

   FileFlush(handle);
   FileClose(handle);
   ArrayFree(ticks);

   bool replaced = ReplaceCommonFile(temporaryPath, targetPath);
   if(replaced)
   {
      RecentTickRepairCursorMsc = toMsc + 1;
      if(RecentTickRepairCursorMsc >= nowMsc)
         RecentTickRepairCursorMsc = windowStart;
   }

   return replaced;
}

//+------------------------------------------------------------------+
//| candles.csv - complete available terminal history                |
//+------------------------------------------------------------------+
bool WriteFullCandlesFile(const int totalBars)
{
   string targetPath = ConnectorFolder + "\\candles.csv";
   string temporaryPath = targetPath + ".tmp";

   ResetLastError();

   int handle =
      FileOpen(
         temporaryPath,
         FILE_WRITE |
         FILE_CSV |
         FILE_ANSI |
         FILE_COMMON |
         FILE_SHARE_READ,
         ',',
         CP_UTF8);

   if(handle == INVALID_HANDLE)
   {
      Print(
         "TickLab: Cannot open full candle file. Error: ",
         GetLastError());
      return false;
   }

   WriteCandleHeader(handle);
   int chunkEnd = totalBars;

   while(chunkEnd > 0)
   {
      int requested = InpHistoryChunkSize;
      if(requested > chunkEnd)
         requested = chunkEnd;

      int startPosition = chunkEnd - requested;
      MqlRates rates[];
      ArraySetAsSeries(rates, true);

      MaintainBridgeHeartbeat(false);
      ResetLastError();

      int copied =
         CopyRates(
            HistorySymbol,
            HistoryTimeframe,
            startPosition,
            requested,
            rates);

      MaintainBridgeHeartbeat(false);

      if(copied != requested)
      {
         int copyError = GetLastError();
         FileClose(handle);
         FileDelete(temporaryPath,FILE_COMMON);

         Print(
            "TickLab: Full-history CopyRates incomplete. Requested ",
            requested,
            ", copied ",
            copied,
            ", error ",
            copyError,
            ". MT5 will continue synchronizing and retry.");
         return false;
      }

      for(int index = copied - 1; index >= 0; index--)
      {
         int globalPosition = startPosition + index;

         WriteCandleRowForSymbolTimeframe(
            handle,
            HistorySymbol,
            rates[index],
            globalPosition != 0,
            HistoryTimeframe);

         if(index % 500 == 0)
            MaintainBridgeHeartbeat(false);
      }

      ArrayFree(rates);
      chunkEnd = startPosition;

      // Keep only the independent history-worker heartbeat alive between blocks.
      MaintainBridgeHeartbeat(true);
   }

   FileFlush(handle);
   FileClose(handle);

   bool replaced = ReplaceCommonFile(temporaryPath,targetPath);
   MaintainBridgeHeartbeat(true);
   return replaced;
}

//+------------------------------------------------------------------+
//| candle_live.csv - current candle updated every second            |
//+------------------------------------------------------------------+
bool WriteLiveCandleFile()

{
   if(IsHistoryWorker())
      return false;
   MqlRates rates[];
   ArraySetAsSeries(rates, true);

   ResetLastError();

   int copied =
      CopyRates(
         CaptureSymbol,
         DataTimeframe,
         0,
         1,
         rates);

   if(copied != 1)
      return false;

   datetime previousLiveTime =
      LastLiveBarTime;

   string targetPath =
      ConnectorFolder + "\\candle_live.csv";

   string temporaryPath =
      targetPath + ".tmp";

   int handle =
      FileOpen(
         temporaryPath,
         FILE_WRITE |
         FILE_CSV |
         FILE_ANSI |
         FILE_COMMON |
         FILE_SHARE_READ,
         ',',
         CP_UTF8);

   if(handle == INVALID_HANDLE)
      return false;

   WriteCandleHeader(handle);
   WriteCandleRow(
      handle,
      rates[0],
      false);

   FileFlush(handle);
   FileClose(handle);

   if(!ReplaceCommonFile(
         temporaryPath,
         targetPath))
   {
      return false;
   }

   bool newBarStarted =
      previousLiveTime > 0 &&
      rates[0].time != previousLiveTime;

   if(newBarStarted)
      AppendClosedCandle();

   LastLiveBarTime = rates[0].time;
   return newBarStarted;
}

//+------------------------------------------------------------------+
//| Append the exact closed MT5 bar without rewriting full history   |
//+------------------------------------------------------------------+
bool AppendClosedCandle()
{
   MqlRates closed[];
   ArraySetAsSeries(closed,true);

   int copied = CopyRates(
      CaptureSymbol,
      DataTimeframe,
      1,
      1,
      closed);

   if(copied != 1)
      return false;

   string targetPath = ConnectorFolder + "\\candle_closed.csv";
   string temporaryPath = targetPath + ".tmp";
   int handle = FileOpen(
      temporaryPath,
      FILE_WRITE |
      FILE_CSV |
      FILE_ANSI |
      FILE_COMMON |
      FILE_SHARE_READ,
      ',',
      CP_UTF8);

   if(handle == INVALID_HANDLE)
      return false;

   WriteCandleHeader(handle);
   WriteCandleRow(handle,closed[0],true);
   FileFlush(handle);
   FileClose(handle);

   return ReplaceCommonFile(temporaryPath,targetPath);
}

//+------------------------------------------------------------------+
//| Shared candle CSV header                                         |
//+------------------------------------------------------------------+
void WriteCandleHeader(const int handle)
{
   FileWrite(
      handle,
      "symbol",
      "timeframe",
      "digits",
      "point",
      "start_unix",
      "end_unix",
      "start_text",
      "open",
      "high",
      "low",
      "close",
      "tick_volume",
      "spread",
      "real_volume",
      "is_closed");
}

//+------------------------------------------------------------------+
//| Exact expected end for native MT5 candle                         |
//+------------------------------------------------------------------+
long CalculateNativeCandleEndForTimeframe(
   const datetime startTime,
   const ENUM_TIMEFRAMES timeframe)
{
   if(timeframe == PERIOD_MN1)
   {
      MqlDateTime parts;
      TimeToStruct(startTime,parts);

      if(parts.mon >= 12)
      {
         parts.year++;
         parts.mon = 1;
      }
      else
      {
         parts.mon++;
      }

      parts.day = 1;
      return (long)StructToTime(parts);
   }

   int periodSeconds = PeriodSeconds(timeframe);
   if(periodSeconds <= 0)
      periodSeconds = 60;

   return (long)startTime + periodSeconds;
}

long CalculateNativeCandleEnd(const datetime startTime)
{
   return CalculateNativeCandleEndForTimeframe(startTime, DataTimeframe);
}

//+------------------------------------------------------------------+
//| Shared candle CSV row for an explicit native MT5 timeframe       |
//+------------------------------------------------------------------+
void WriteCandleRowForSymbolTimeframe(
   const int handle,
   const string symbol,
   const MqlRates &rate,
   const bool isClosed,
   const ENUM_TIMEFRAMES timeframe)
{
   int digits =
      (int)SymbolInfoInteger(
         symbol,
         SYMBOL_DIGITS);

   double point =
      SymbolInfoDouble(
         symbol,
         SYMBOL_POINT);

   long startUnix = (long)rate.time;
   long endUnix =
      CalculateNativeCandleEndForTimeframe(rate.time, timeframe);

   FileWrite(
      handle,
      symbol,
      EnumToString(timeframe),
      IntegerToString(digits),
      DoubleToString(point, digits),
      IntegerToString(startUnix),
      IntegerToString(endUnix),
      TimeToString(rate.time,TIME_DATE | TIME_SECONDS),
      DoubleToString(rate.open, digits),
      DoubleToString(rate.high, digits),
      DoubleToString(rate.low, digits),
      DoubleToString(rate.close, digits),
      IntegerToString((long)rate.tick_volume),
      IntegerToString(rate.spread),
      IntegerToString((long)rate.real_volume),
      BoolToJson(isClosed));
}

void WriteCandleRowForTimeframe(
   const int handle,
   const MqlRates &rate,
   const bool isClosed,
   const ENUM_TIMEFRAMES timeframe)
{
   WriteCandleRowForSymbolTimeframe(
      handle,
      CaptureSymbol,
      rate,
      isClosed,
      timeframe);
}

//+------------------------------------------------------------------+
//| Shared candle CSV row for the currently projected timeframe      |
//+------------------------------------------------------------------+
void WriteCandleRow(
   const int handle,
   const MqlRates &rate,
   const bool isClosed)
{
   WriteCandleRowForTimeframe(handle, rate, isClosed, DataTimeframe);
}

//+------------------------------------------------------------------+
//| history_status.json                                              |
//+------------------------------------------------------------------+
bool WriteHistoryStatus(
   const int exportedBars,
   const datetime firstDate,
   const datetime latestBarTime,
   const bool synchronized,
   const string status)
{
   long serverFirst =
      SeriesInfoInteger(
         HistorySymbol,
         HistoryTimeframe,
         SERIES_SERVER_FIRSTDATE);

   long terminalFirst =
      SeriesInfoInteger(
         HistorySymbol,
         HistoryTimeframe,
         SERIES_TERMINAL_FIRSTDATE);

   long maximumBars = TerminalInfoInteger(TERMINAL_MAXBARS);

   string json =
      "{\r\n" +
      "  \"protocol_version\": " + IntegerToString(ProtocolVersion) + ",\r\n" +
      "  \"connector_id\": \"" + EscapeJson(ConnectorId) + "\",\r\n" +
      "  \"request_id\": \"" + EscapeJson(ActiveHistoryRequestId) + "\",\r\n" +
      "  \"symbol\": \"" + EscapeJson(HistorySymbol) + "\",\r\n" +
      "  \"timeframe\": \"" + EscapeJson(EnumToString(HistoryTimeframe)) + "\",\r\n" +
      "  \"status\": \"" + EscapeJson(status) + "\",\r\n" +
      "  \"synchronized\": " + BoolToJson(synchronized) + ",\r\n" +
      "  \"exported_bars\": " + IntegerToString(exportedBars) + ",\r\n" +
      "  \"first_bar_unix\": " + IntegerToString((long)firstDate) + ",\r\n" +
      "  \"latest_bar_unix\": " + IntegerToString((long)latestBarTime) + ",\r\n" +
      "  \"server_first_unix\": " + IntegerToString(serverFirst) + ",\r\n" +
      "  \"terminal_first_unix\": " + IntegerToString(terminalFirst) + ",\r\n" +
      "  \"series_first_unix\": " + IntegerToString((long)CandleHistoryCurrentFirst) + ",\r\n" +
      "  \"target_first_unix\": " + IntegerToString((long)CandleHistoryTargetFirst) + ",\r\n" +
      "  \"history_sync_complete\": " + BoolToJson(CandleHistoryLoadComplete) + ",\r\n" +
      "  \"limited_by_max_bars\": " + BoolToJson(CandleHistoryLimitedByMaxBars) + ",\r\n" +
      "  \"terminal_max_bars\": " + IntegerToString(maximumBars) + ",\r\n" +
      "  \"updated_unix\": " + IntegerToString((long)TimeGMT()) + "\r\n" +
      "}\r\n";

   return WriteTextAtomic(ConnectorFolder + "\\history_status.json",json);
}

//+------------------------------------------------------------------+
//| Detect and process tick_request.json                             |
//+------------------------------------------------------------------+
void CheckForTickRequest()
{
   string requestPath = ConnectorFolder + "\\tick_request.json";

   if(!FileIsExist(requestPath, FILE_COMMON))
      return;

   string json = ReadCommonTextFile(requestPath);

   if(StringLen(json) == 0)
      return;

   string requestId = "";
   string connectorId = "";
   string symbol = "";
   string timeframe = "";
   long protocol = 0;
   long startMilliseconds = 0;
   long endMilliseconds = 0;

   bool valid =
      JsonGetLong(json, "protocol_version", protocol) &&
      JsonGetString(json, "request_id", requestId) &&
      JsonGetString(json, "connector_id", connectorId) &&
      JsonGetString(json, "symbol", symbol) &&
      JsonGetString(json, "timeframe", timeframe) &&
      JsonGetLong(json, "start_msc", startMilliseconds) &&
      JsonGetLong(json, "end_msc", endMilliseconds);

   if(!valid || StringLen(requestId) == 0)
      return;

   if(requestId == LastProcessedRequestId)
      return;

   if(protocol != ProtocolVersion)
   {
      WriteSelectionResponse(
         requestId,
         symbol,
         startMilliseconds,
         endMilliseconds,
         0,
         "error",
         1001,
         "Unsupported TickLab protocol version.");

      LastProcessedRequestId = requestId;
      return;
   }

   if(connectorId != ConnectorId)
   {
      WriteSelectionResponse(
         requestId,
         symbol,
         startMilliseconds,
         endMilliseconds,
         0,
         "error",
         1002,
         "The request belongs to another MT5 connector.");

      LastProcessedRequestId = requestId;
      return;
   }

   long rangeMilliseconds = endMilliseconds - startMilliseconds;

   if(StringLen(symbol) == 0 ||
      startMilliseconds <= 0 ||
      endMilliseconds < startMilliseconds ||
      (InpMaximumRequestSeconds > 0 &&
       rangeMilliseconds > (long)InpMaximumRequestSeconds * 1000))
   {
      WriteSelectionResponse(
         requestId,
         symbol,
         startMilliseconds,
         endMilliseconds,
         0,
         "error",
         1003,
         "The requested tick range is invalid.");

      LastProcessedRequestId = requestId;
      return;
   }

   ProcessTickRequest(
      requestId,
      symbol,
      startMilliseconds,
      endMilliseconds);
}

//+------------------------------------------------------------------+
//| Copy and export all available ticks                              |
//+------------------------------------------------------------------+
void ProcessTickRequest(
   const string requestId,
   const string symbol,
   const long startMilliseconds,
   const long endMilliseconds)
{
   ResetLastError();

   if(!SymbolSelect(symbol, true))
   {
      int selectError = GetLastError();

      WriteSelectionResponse(
         requestId,
         symbol,
         startMilliseconds,
         endMilliseconds,
         0,
         "error",
         selectError,
         "MT5 could not select the requested symbol.");

      LastProcessedRequestId = requestId;
      return;
   }

   MqlTick ticks[];
   int copied = -1;
   int copyError = 0;

   for(int attempt = 0; attempt < 3; attempt++)
   {
      ResetLastError();

      copied =
         CopyTicksRange(
            symbol,
            ticks,
            COPY_TICKS_ALL,
            (ulong)startMilliseconds,
            (ulong)endMilliseconds);

      copyError = GetLastError();

      if(copied >= 0)
         break;

      Sleep(100);
   }

   if(copied < 0)
   {
      WriteSelectionResponse(
         requestId,
         symbol,
         startMilliseconds,
         endMilliseconds,
         0,
         "error",
         copyError,
         "MT5 could not copy tick history for the selected candle.");

      LastProcessedRequestId = requestId;
      return;
   }

   if(!WriteTicksFile(requestId, symbol, ticks, copied))
   {
      int writeError = GetLastError();

      WriteSelectionResponse(
         requestId,
         symbol,
         startMilliseconds,
         endMilliseconds,
         0,
         "error",
         writeError,
         "MT5 could not write ticks.csv.");

      LastProcessedRequestId = requestId;
      return;
   }

   string message = copied == 0
      ? "No ticks were available for this candle range."
      : "Every available MT5 tick was exported successfully.";

   WriteSelectionResponse(
      requestId,
      symbol,
      startMilliseconds,
      endMilliseconds,
      copied,
      "ok",
      0,
      message);

   LastProcessedRequestId = requestId;

   Print(
      "TickLab: exported ",
      copied,
      " ticks for ",
      symbol,
      " | request ",
      requestId);
}

//+------------------------------------------------------------------+
//| ticks.csv                                                        |
//+------------------------------------------------------------------+
bool WriteTicksFile(
   const string requestId,
   const string symbol,
   MqlTick &ticks[],
   const int count)
{
   string targetPath = ConnectorFolder + "\\ticks.csv";
   string temporaryPath = targetPath + ".tmp";

   ResetLastError();

   int handle =
      FileOpen(
         temporaryPath,
         FILE_WRITE |
         FILE_CSV |
         FILE_ANSI |
         FILE_COMMON |
         FILE_SHARE_READ,
         ',',
         CP_UTF8);

   if(handle == INVALID_HANDLE)
      return false;

   FileWrite(
      handle,
      "request_id",
      "time_msc",
      "time",
      "bid",
      "ask",
      "last",
      "volume",
      "flags",
      "volume_real");

   int digits =
      (int)SymbolInfoInteger(symbol, SYMBOL_DIGITS);

   for(int index = 0; index < count; index++)
   {
      FileWrite(
         handle,
         requestId,
         IntegerToString((long)ticks[index].time_msc),
         IntegerToString((long)ticks[index].time),
         DoubleToString(ticks[index].bid, digits),
         DoubleToString(ticks[index].ask, digits),
         DoubleToString(ticks[index].last, digits),
         DoubleToString((double)ticks[index].volume, 2),
         IntegerToString((long)ticks[index].flags),
         DoubleToString(ticks[index].volume_real, 8));
   }

   FileFlush(handle);
   FileClose(handle);

   return ReplaceCommonFile(temporaryPath, targetPath);
}

//+------------------------------------------------------------------+
//| selection.json                                                   |
//+------------------------------------------------------------------+
bool WriteSelectionResponse(
   const string requestId,
   const string symbol,
   const long startMilliseconds,
   const long endMilliseconds,
   const int tickCount,
   const string status,
   const int errorCode,
   const string message)
{
   string json =
      "{\r\n" +
      "  \"protocol_version\": " + IntegerToString(ProtocolVersion) + ",\r\n" +
      "  \"request_id\": \"" + EscapeJson(requestId) + "\",\r\n" +
      "  \"connector_id\": \"" + EscapeJson(ConnectorId) + "\",\r\n" +
      "  \"symbol\": \"" + EscapeJson(symbol) + "\",\r\n" +
      "  \"start_msc\": " + IntegerToString(startMilliseconds) + ",\r\n" +
      "  \"end_msc\": " + IntegerToString(endMilliseconds) + ",\r\n" +
      "  \"tick_count\": " + IntegerToString(tickCount) + ",\r\n" +
      "  \"status\": \"" + EscapeJson(status) + "\",\r\n" +
      "  \"error_code\": " + IntegerToString(errorCode) + ",\r\n" +
      "  \"message\": \"" + EscapeJson(message) + "\",\r\n" +
      "  \"ticks_file\": \"ticks.csv\",\r\n" +
      "  \"completed_unix\": " + IntegerToString((long)TimeGMT()) + "\r\n" +
      "}\r\n";

   return WriteTextAtomic(
      ConnectorFolder + "\\selection.json",
      json);
}

//+------------------------------------------------------------------+
//| Atomic UTF-8 text write                                          |
//+------------------------------------------------------------------+
bool WriteTextAtomic(
   const string targetPath,
   const string contents)
{
   // Re-create the folder automatically if it was removed while MT5
   // was running. This keeps connection and heartbeat files recoverable.
   if(!EnsureConnectorFolders())
      return false;

   string temporaryPath = targetPath + ".tmp";

   ResetLastError();

   int handle =
      FileOpen(
         temporaryPath,
         FILE_WRITE |
         FILE_TXT |
         FILE_ANSI |
         FILE_COMMON |
         FILE_SHARE_READ,
         0,
         CP_UTF8);

   if(handle == INVALID_HANDLE)
   {
      // The user may have removed the folder while MT5 was running.
      // Recreate it once and retry the write.
      ConnectorFoldersReady = false;

      if(EnsureConnectorFolders())
      {
         ResetLastError();
         handle =
            FileOpen(
               temporaryPath,
               FILE_WRITE |
               FILE_TXT |
               FILE_ANSI |
               FILE_COMMON |
               FILE_SHARE_READ,
               0,
               CP_UTF8);
      }

      if(handle == INVALID_HANDLE)
      {
         Print("TickLab: Cannot open ", temporaryPath, ". Error: ", GetLastError());
         return false;
      }
   }

   uint written = FileWriteString(handle, contents);
   FileFlush(handle);
   FileClose(handle);

   if(written == 0)
      return false;

   return ReplaceCommonFile(temporaryPath, targetPath);
}

//+------------------------------------------------------------------+
//| Replace a shared file atomically                                 |
//+------------------------------------------------------------------+
bool ReplaceCommonFile(
   const string temporaryPath,
   const string targetPath)
{
   ResetLastError();

   bool moved =
      FileMove(
         temporaryPath,
         FILE_COMMON,
         targetPath,
         FILE_COMMON | FILE_REWRITE);

   if(!moved)
   {
      int moveError = GetLastError();

      // Some MT5 installations refuse the atomic FileMove operation in
      // Common Files even though normal writes are allowed. Fall back to a
      // direct replacement so heartbeat and connection files still update.
      string contents = ReadCommonTextFile(temporaryPath);

      ResetLastError();
      int directHandle =
         FileOpen(
            targetPath,
            FILE_WRITE |
            FILE_TXT |
            FILE_ANSI |
            FILE_COMMON |
            FILE_SHARE_READ |
            FILE_SHARE_WRITE,
            0,
            CP_UTF8);

      if(directHandle != INVALID_HANDLE)
      {
         uint directWritten = FileWriteString(directHandle,contents);
         FileFlush(directHandle);
         FileClose(directHandle);

         if(directWritten > 0)
         {
            FileDelete(temporaryPath,FILE_COMMON);
            return true;
         }
      }

      Print(
         "TickLab: File replacement failed: ",
         temporaryPath,
         " -> ",
         targetPath,
         " | Move error: ",
         moveError,
         " | Direct-write error: ",
         GetLastError());
   }

   return moved;
}

//+------------------------------------------------------------------+
//| Read a UTF-8 text file from Common Files                         |
//+------------------------------------------------------------------+
string ReadCommonTextFile(const string path)
{
   ResetLastError();

   int handle =
      FileOpen(
         path,
         FILE_READ |
         FILE_TXT |
         FILE_ANSI |
         FILE_COMMON |
         FILE_SHARE_READ |
         FILE_SHARE_WRITE,
         0,
         CP_UTF8);

   if(handle == INVALID_HANDLE)
      return "";

   string contents = "";

   while(!FileIsEnding(handle))
   {
      string line = FileReadString(handle);
      contents += line + "\n";
   }

   FileClose(handle);
   return contents;
}

//+------------------------------------------------------------------+
//| Minimal JSON string reader                                       |
//+------------------------------------------------------------------+
bool JsonGetString(
   const string json,
   const string key,
   string &value)
{
   string token = "\"" + key + "\"";
   int keyPosition = StringFind(json, token);

   if(keyPosition < 0)
      return false;

   int colonPosition = StringFind(json, ":", keyPosition + StringLen(token));

   if(colonPosition < 0)
      return false;

   int quoteStart = StringFind(json, "\"", colonPosition + 1);

   if(quoteStart < 0)
      return false;

   int quoteEnd = quoteStart + 1;
   bool escaped = false;

   while(quoteEnd < StringLen(json))
   {
      ushort character = StringGetCharacter(json, quoteEnd);

      if(character == '\\' && !escaped)
      {
         escaped = true;
         quoteEnd++;
         continue;
      }

      if(character == '"' && !escaped)
         break;

      escaped = false;
      quoteEnd++;
   }

   if(quoteEnd >= StringLen(json))
      return false;

   value = StringSubstr(json, quoteStart + 1, quoteEnd - quoteStart - 1);
   StringReplace(value, "\\\"", "\"");
   StringReplace(value, "\\\\", "\\");
   return true;
}

//+------------------------------------------------------------------+
//| Minimal JSON integer reader                                      |
//+------------------------------------------------------------------+
bool JsonGetLong(
   const string json,
   const string key,
   long &value)
{
   string token = "\"" + key + "\"";
   int keyPosition = StringFind(json, token);

   if(keyPosition < 0)
      return false;

   int colonPosition = StringFind(json, ":", keyPosition + StringLen(token));

   if(colonPosition < 0)
      return false;

   int index = colonPosition + 1;
   int length = StringLen(json);

   while(index < length && IsWhitespace(StringGetCharacter(json, index)))
      index++;

   int numberStart = index;

   if(index < length && StringGetCharacter(json, index) == '-')
      index++;

   while(index < length)
   {
      ushort character = StringGetCharacter(json, index);

      if(character < '0' || character > '9')
         break;

      index++;
   }

   if(index <= numberStart)
      return false;

   string numberText =
      StringSubstr(json, numberStart, index - numberStart);

   value = (long)StringToInteger(numberText);
   return true;
}

//+------------------------------------------------------------------+
//| Helpers                                                          |
//+------------------------------------------------------------------+
bool IsWhitespace(const ushort character)
{
   return character == ' ' ||
          character == '\t' ||
          character == '\r' ||
          character == '\n';
}

string BoolToJson(const bool value)
{
   return value ? "true" : "false";
}

string EscapeJson(const string value)
{
   string escaped = value;
   StringReplace(escaped, "\\", "\\\\");
   StringReplace(escaped, "\"", "\\\"");
   StringReplace(escaped, "\r", "\\r");
   StringReplace(escaped, "\n", "\\n");
   StringReplace(escaped, "\t", "\\t");
   return escaped;
}
//+------------------------------------------------------------------+
