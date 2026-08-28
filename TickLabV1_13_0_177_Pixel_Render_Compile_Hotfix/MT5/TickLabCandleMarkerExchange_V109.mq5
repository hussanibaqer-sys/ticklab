//+------------------------------------------------------------------+
//| TickLab Candle Marker Exchange V109                              |
//| Chart-safe marker panel with dynamic in-frame positioning.       |
//+------------------------------------------------------------------+
#property strict
#property indicator_chart_window
#property indicator_buffers 1
#property indicator_plots 1
#property indicator_label1  "TickLab Marker Engine"
#property indicator_type1   DRAW_NONE
#property indicator_applied_price PRICE_CLOSE
#property version   "1.09"

input bool ReceiveMarkersAtStartup = true;
input int  PanelLeft = 10;
input int  PanelBottom = 12;

string PREFIX = "TL_MARKX_";
string IN_FILE = "TickLab\\Markers\\ticklab_to_mt5.pipe";
string OUT_FILE = "TickLab\\Markers\\mt5_to_ticklab.pipe";
string RECEIVE_STATE = "TickLab\\Markers\\mt5_receive_v109.state";


double TickLabMarkerBuffer[];

bool panel_visible = false;
bool receive_enabled = false;
bool mark_mode = false;
string selected_marker_id = "";

int last_chart_width = 0;
int last_chart_height = 0;
int button_x = 10;
int button_y = 10;
int button_width = 72;
int button_height = 30;
int panel_x = 10;
int panel_y = 10;
int panel_width = 380;
int panel_height = 340;

string N(string suffix) { return PREFIX + suffix; }
string MarkerObject(string id) { return PREFIX + "LINE_" + id; }
string SelectionObject() { return PREFIX + "SELECTION_" + StringFormat("%I64d", ChartID()); }
string SafeFilePart(string value)
{
   StringReplace(value, "\\", "_");
   StringReplace(value, "/", "_");
   StringReplace(value, ":", "_");
   StringReplace(value, ".", "_");
   StringReplace(value, " ", "_");
   return(value);
}
string CursorFile()
{
   return("TickLab\\Markers\\mt5_in_" + SafeFilePart(_Symbol) + "_" +
          IntegerToString((int)_Period) + "_" + StringFormat("%I64d", ChartID()) + ".cursor");
}

int OnInit()
{
   SetIndexBuffer(0, TickLabMarkerBuffer, INDICATOR_DATA);
   ArraySetAsSeries(TickLabMarkerBuffer, true);
   PlotIndexSetDouble(0, PLOT_EMPTY_VALUE, EMPTY_VALUE);

   FolderCreate("TickLab", FILE_COMMON);
   FolderCreate("TickLab\\Markers", FILE_COMMON);
   receive_enabled = LoadReceiveState(ReceiveMarkersAtStartup);
   CalculateLayout();
   CreateMainButton();
   EventSetMillisecondTimer(250);
   if(receive_enabled)
      ProcessIncoming(true);
   ChartRedraw();
   return(INIT_SUCCEEDED);
}

void OnDeinit(const int reason)
{
   EventKillTimer();
   ObjectsDeleteAll(0, PREFIX);
   ChartRedraw();
}

int OnCalculate(const int rates_total,
                const int prev_calculated,
                const int begin,
                const double &price[])
{
   if(rates_total > 0)
      TickLabMarkerBuffer[0] = EMPTY_VALUE;
   return(rates_total);
}

void OnTimer()
{
   RefreshLayoutIfChartChanged();
   if(ObjectFind(0, N("OPEN")) < 0)
      CreateMainButton();
   if(receive_enabled)
      ProcessIncoming(false);
}

void OnChartEvent(const int id,
                  const long &lparam,
                  const double &dparam,
                  const string &sparam)
{
   if(id == CHARTEVENT_CHART_CHANGE)
   {
      RefreshLayoutIfChartChanged(false);
      return;
   }

   if(id == CHARTEVENT_OBJECT_CLICK)
   {
      ObjectSetInteger(0, sparam, OBJPROP_STATE, false);

      if(sparam == N("OPEN"))
      {
         if(panel_visible)
            ClosePanel();
         else
            OpenPanel();
         return;
      }
      if(sparam == N("CLOSE"))
      {
         ClosePanel();
         return;
      }
      if(sparam == N("RECEIVE"))
      {
         receive_enabled = !receive_enabled;
         SaveReceiveState();
         UpdateReceiveButton();
         if(receive_enabled)
            ProcessIncoming(true);
         return;
      }
      if(sparam == N("FIND"))
      {
         FindCurrentMarker();
         return;
      }
      if(sparam == N("MARK"))
      {
         ToggleMarkMode();
         return;
      }
      if(sparam == N("EXPORT"))
      {
         ExportCurrentMarker();
         return;
      }
      if(sparam == N("REMOVE"))
      {
         RemoveSelectedMarker();
         return;
      }
      if(sparam == N("CLEAR"))
      {
         ClearExportedMarkers();
         return;
      }
      if(sparam == SelectionObject())
      {
         UpdateSelectionFromLine();
         SetStatus("Yellow selection active. Drag it or press Export.");
         return;
      }
      if(StringFind(sparam, PREFIX + "LINE_") == 0)
      {
         selected_marker_id = StringSubstr(sparam, StringLen(PREFIX + "LINE_"));
         SetStatus("Marker selected. Press Remove selected.");
         ChartRedraw();
         return;
      }
   }

   if(id == CHARTEVENT_OBJECT_DRAG && sparam == SelectionObject())
   {
      UpdateSelectionFromLine();
      return;
   }

   if(id == CHARTEVENT_CLICK && panel_visible && mark_mode)
   {
      int click_x = (int)lparam;
      int click_y = (int)dparam;
      if(IsInsidePanel(click_x, click_y) || IsInsideOpenButton(click_x, click_y))
         return;

      int subwindow = 0;
      datetime clicked = 0;
      double price = 0.0;
      if(ChartXYToTimePrice(0, click_x, click_y, subwindow, clicked, price))
      {
         int shift = iBarShift(_Symbol, (ENUM_TIMEFRAMES)_Period, clicked, false);
         if(shift >= 0)
         {
            datetime bar_time = iTime(_Symbol, (ENUM_TIMEFRAMES)_Period, shift);
            if(bar_time > 0)
               SetSelectedCandle(bar_time, _Symbol, EnumToString((ENUM_TIMEFRAMES)_Period));
         }
      }
   }
}

void CalculateLayout()
{
   long width_value = 0;
   long height_value = 0;
   if(!ChartGetInteger(0, CHART_WIDTH_IN_PIXELS, 0, width_value))
      width_value = 800;
   if(!ChartGetInteger(0, CHART_HEIGHT_IN_PIXELS, 0, height_value))
      height_value = 600;

   int chart_width = (int)MathMax(240, width_value);
   int chart_height = (int)MathMax(220, height_value);
   int margin = 8;
   int gap = 8;

   last_chart_width = chart_width;
   last_chart_height = chart_height;

   button_width = 72;
   button_height = 30;
   button_x = MathMax(margin, MathMin(PanelLeft, chart_width - button_width - margin));
   button_y = MathMax(margin, chart_height - MathMax(PanelBottom, margin) - button_height);

   panel_width = MathMin(440, chart_width - (2 * margin));
   if(panel_width < 280)
      panel_width = MathMax(220, chart_width - (2 * margin));

   panel_height = MathMin(340, chart_height - (2 * margin));
   if(panel_height < 300)
      panel_height = MathMax(230, chart_height - (2 * margin));

   panel_x = button_x;
   if(panel_x + panel_width > chart_width - margin)
      panel_x = MathMax(margin, chart_width - margin - panel_width);

   panel_y = button_y - gap - panel_height;
   if(panel_y < margin)
      panel_y = margin;
}

void RefreshLayoutIfChartChanged(bool force = false)
{
   long width_value = 0;
   long height_value = 0;
   ChartGetInteger(0, CHART_WIDTH_IN_PIXELS, 0, width_value);
   ChartGetInteger(0, CHART_HEIGHT_IN_PIXELS, 0, height_value);
   int current_width = (int)width_value;
   int current_height = (int)height_value;

   if(!force && current_width == last_chart_width && current_height == last_chart_height)
      return;

   CalculateLayout();
   ApplyMainButtonLayout();
   if(panel_visible)
      ApplyPanelLayout();
   ChartRedraw();
}

bool IsInsidePanel(int x, int y)
{
   return(panel_visible && x >= panel_x && x <= panel_x + panel_width &&
          y >= panel_y && y <= panel_y + panel_height);
}

bool IsInsideOpenButton(int x, int y)
{
   return(x >= button_x && x <= button_x + button_width &&
          y >= button_y && y <= button_y + button_height);
}

void CreateMainButton()
{
   if(ObjectFind(0, N("OPEN")) >= 0)
      ObjectDelete(0, N("OPEN"));
   CreateButton(N("OPEN"), panel_visible ? "HIDE" : "MARK",
                button_x, button_y, button_width, button_height, 300);
}

void ApplyMainButtonLayout()
{
   if(ObjectFind(0, N("OPEN")) < 0)
   {
      CreateMainButton();
      return;
   }
   SetRect(N("OPEN"), button_x, button_y, button_width, button_height);
}

void OpenPanel()
{
   DeletePanelObjects();
   panel_visible = true;
   CalculateLayout();
   CreatePanelObjects();
   CreateMainButton();
   SyncPanelToCurrentChart();
   ChartRedraw();
}

void ClosePanel()
{
   DeletePanelObjects();
   mark_mode = false;
   ObjectDelete(0, SelectionObject());
   panel_visible = false;
   CreateMainButton();
   ChartRedraw();
}

void DeletePanelObjects()
{
   string names[] = {"PANEL","TITLE","RECEIVE","L_SYMBOL","SYMBOL","L_TF","TF",
                     "L_DATE","DATE","L_TIME","TIME","L_FIND","L_LABEL","LABEL",
                     "FIND","MARK","EXPORT","REMOVE","CLEAR","CLOSE","STATUS"};
   for(int i = 0; i < ArraySize(names); i++)
      ObjectDelete(0, N(names[i]));
}

void CreatePanelObjects()
{
   CreateRect(N("PANEL"), panel_x, panel_y, panel_width, panel_height, 200);
   CreateLabel(N("TITLE"), "Candle Marker / Export", panel_x, panel_y, 11, 230);
   CreateButton(N("RECEIVE"), "", panel_x, panel_y, 110, 27, 240);
   CreateButton(N("CLOSE"), "X", panel_x, panel_y, 30, 27, 240);

   CreateLabel(N("L_SYMBOL"), "Symbol", panel_x, panel_y, 9, 230);
   CreateEdit(N("SYMBOL"), _Symbol, panel_x, panel_y, 100, 27, 240);
   CreateLabel(N("L_TF"), "Timeframe", panel_x, panel_y, 9, 230);
   CreateEdit(N("TF"), EnumToString((ENUM_TIMEFRAMES)_Period), panel_x, panel_y, 100, 27, 240);

   CreateLabel(N("L_DATE"), "Server date", panel_x, panel_y, 9, 230);
   CreateEdit(N("DATE"), TimeToString(TimeCurrent(), TIME_DATE), panel_x, panel_y, 100, 27, 240);
   CreateLabel(N("L_TIME"), "Server time", panel_x, panel_y, 9, 230);
   CreateEdit(N("TIME"), TimeToString(TimeCurrent(), TIME_SECONDS), panel_x, panel_y, 100, 27, 240);

   CreateLabel(N("L_FIND"), "Manual Find Candle - local only, no import/export", panel_x, panel_y, 9, 230);
   CreateButton(N("FIND"), "Find candle", panel_x, panel_y, 100, 30, 240);

   CreateLabel(N("L_LABEL"), "Export label", panel_x, panel_y, 9, 230);
   CreateEdit(N("LABEL"), "MT5 marker", panel_x, panel_y, 200, 27, 240);

   CreateButton(N("MARK"), "Mark", panel_x, panel_y, 80, 30, 240);
   CreateButton(N("EXPORT"), "Export", panel_x, panel_y, 80, 30, 240);
   CreateButton(N("REMOVE"), "Remove", panel_x, panel_y, 80, 30, 240);
   CreateButton(N("CLEAR"), "Clear", panel_x, panel_y, 80, 30, 240);
   CreateLabel(N("STATUS"), "Click a candle or enter server date/time.", panel_x, panel_y, 8, 230);

   ApplyPanelLayout();
   UpdateReceiveButton();
   UpdateMarkButton();
}

int PanelY(int base_offset)
{
   double ratio = (double)panel_height / 340.0;
   return(panel_y + (int)MathRound(base_offset * ratio));
}

int ScaledHeight(int base_height, int minimum_height)
{
   double ratio = (double)panel_height / 340.0;
   return(MathMax(minimum_height, (int)MathRound(base_height * ratio)));
}

void ApplyPanelLayout()
{
   int pad = 12;
   int inner_width = MathMax(196, panel_width - (2 * pad));
   int column_gap = 10;
   int column_width = (inner_width - column_gap) / 2;
   int right_column_x = panel_x + pad + column_width + column_gap;
   int close_width = 30;
   int receive_width = MathMin(110, MathMax(86, panel_width / 3));
   int close_x = panel_x + panel_width - pad - close_width;
   int receive_x = close_x - 8 - receive_width;
   int button_gap = 8;
   int action_width = (inner_width - (3 * button_gap)) / 4;
   int header_height = ScaledHeight(28, 22);
   int edit_height = ScaledHeight(27, 20);
   int action_height = ScaledHeight(30, 22);

   SetRect(N("PANEL"), panel_x, panel_y, panel_width, panel_height);
   SetPoint(N("TITLE"), panel_x + pad, PanelY(12));
   SetRect(N("RECEIVE"), receive_x, PanelY(8), receive_width, header_height);
   SetRect(N("CLOSE"), close_x, PanelY(8), close_width, header_height);

   SetPoint(N("L_SYMBOL"), panel_x + pad, PanelY(48));
   SetRect(N("SYMBOL"), panel_x + pad, PanelY(64), column_width, edit_height);
   SetPoint(N("L_TF"), right_column_x, PanelY(48));
   SetRect(N("TF"), right_column_x, PanelY(64), column_width, edit_height);

   SetPoint(N("L_DATE"), panel_x + pad, PanelY(102));
   SetRect(N("DATE"), panel_x + pad, PanelY(118), column_width, edit_height);
   SetPoint(N("L_TIME"), right_column_x, PanelY(102));
   SetRect(N("TIME"), right_column_x, PanelY(118), column_width, edit_height);

   SetPoint(N("L_FIND"), panel_x + pad, PanelY(155));
   SetRect(N("FIND"), panel_x + pad, PanelY(172), inner_width, action_height);

   SetPoint(N("L_LABEL"), panel_x + pad, PanelY(210));
   SetRect(N("LABEL"), panel_x + pad, PanelY(226), inner_width, edit_height);

   SetRect(N("MARK"), panel_x + pad, PanelY(266), action_width, action_height);
   SetRect(N("EXPORT"), panel_x + pad + action_width + button_gap,
           PanelY(266), action_width, action_height);
   SetRect(N("REMOVE"), panel_x + pad + (2 * (action_width + button_gap)),
           PanelY(266), action_width, action_height);
   SetRect(N("CLEAR"), panel_x + pad + (3 * (action_width + button_gap)),
           PanelY(266), action_width, action_height);
   SetPoint(N("STATUS"), panel_x + pad, PanelY(314));
}

void SyncPanelToCurrentChart()
{
   if(!panel_visible)
      return;

   ObjectSetString(0, N("SYMBOL"), OBJPROP_TEXT, _Symbol);
   ObjectSetString(0, N("TF"), OBJPROP_TEXT, EnumToString((ENUM_TIMEFRAMES)_Period));

   datetime current_bar = iTime(_Symbol, (ENUM_TIMEFRAMES)_Period, 0);
   if(current_bar <= 0)
      current_bar = TimeCurrent();
   SetSelectedCandle(current_bar, _Symbol, EnumToString((ENUM_TIMEFRAMES)_Period));
}

void SetSelectedCandle(datetime bar_time, string symbol, string timeframe)
{
   if(!panel_visible)
      return;

   ObjectSetString(0, N("SYMBOL"), OBJPROP_TEXT, symbol);
   ObjectSetString(0, N("TF"), OBJPROP_TEXT, timeframe);
   ObjectSetString(0, N("DATE"), OBJPROP_TEXT, TimeToString(bar_time, TIME_DATE));
   ObjectSetString(0, N("TIME"), OBJPROP_TEXT, TimeToString(bar_time, TIME_SECONDS));
   SetStatus("Selected: " + TimeToString(bar_time, TIME_DATE|TIME_SECONDS));
   if(mark_mode)
      DrawSelection(bar_time);
   ChartRedraw();
}

void FindCurrentMarker()
{
   if(!panel_visible)
      return;

   string symbol = ObjectGetString(0, N("SYMBOL"), OBJPROP_TEXT);
   string timeframe_text = ObjectGetString(0, N("TF"), OBJPROP_TEXT);
   string date_text = ObjectGetString(0, N("DATE"), OBJPROP_TEXT);
   string time_text = ObjectGetString(0, N("TIME"), OBJPROP_TEXT);
   string label = ObjectGetString(0, N("LABEL"), OBJPROP_TEXT);
   datetime requested = StringToTime(date_text + " " + time_text);
   ENUM_TIMEFRAMES tf = PERIOD_CURRENT;

   StringTrimLeft(symbol);
   StringTrimRight(symbol);
   if(requested <= 0 || symbol == "" || !TryParseTimeframe(timeframe_text, tf))
   {
      SetStatus("Invalid symbol, timeframe or server date/time.");
      return;
   }
   if(symbol != _Symbol || tf != (ENUM_TIMEFRAMES)_Period)
   {
      ChartSetSymbolPeriod(0, symbol, tf);
      SetStatus("Chart switched. Press Find again after it loads.");
      return;
   }
   int shift = iBarShift(symbol, tf, requested, false);
   if(shift < 0)
   {
      SetStatus("No candle exists at that server date/time.");
      return;
   }
   datetime bar_time = iTime(symbol, tf, shift);
   if(bar_time <= 0)
   {
      SetStatus("The requested candle is unavailable.");
      return;
   }
   string id = StringFormat("FIND-%I64d-%d", GetMicrosecondCount(), MathRand());
   DrawMarker(id, bar_time, label, clrGold);
   selected_marker_id = id;
   ChartNavigate(0, CHART_END, -shift);
   SetSelectedCandle(bar_time, symbol, EnumToString(tf));
   SetStatus("Candle found, centered and marked locally.");
   ChartRedraw();
}

void ExportCurrentMarker()
{
   if(!panel_visible)
      return;

   if(!mark_mode || ObjectFind(0, SelectionObject()) < 0)
   {
      SetStatus("Click Mark and select a candle before Export.");
      return;
   }

   string symbol = ObjectGetString(0, N("SYMBOL"), OBJPROP_TEXT);
   string timeframe_text = ObjectGetString(0, N("TF"), OBJPROP_TEXT);
   string date_text = ObjectGetString(0, N("DATE"), OBJPROP_TEXT);
   string time_text = ObjectGetString(0, N("TIME"), OBJPROP_TEXT);
   string label = ObjectGetString(0, N("LABEL"), OBJPROP_TEXT);
   datetime requested = (datetime)ObjectGetInteger(0, SelectionObject(), OBJPROP_TIME, 0);
   ENUM_TIMEFRAMES tf = PERIOD_CURRENT;

   StringTrimLeft(symbol);
   StringTrimRight(symbol);
   if(requested <= 0 || symbol == "" || !TryParseTimeframe(timeframe_text, tf))
   {
      SetStatus("Invalid symbol, timeframe or server date/time.");
      return;
   }

   if(!SymbolSelect(symbol, true))
   {
      SetStatus("Symbol is unavailable in this MT5 terminal.");
      return;
   }

   int shift = iBarShift(symbol, tf, requested, false);
   if(shift < 0)
   {
      SetStatus("No candle exists at that server date/time.");
      return;
   }

   datetime bar_time = iTime(symbol, tf, shift);
   int period_seconds = PeriodSeconds(tf);
   if(bar_time <= 0 || requested < bar_time ||
      (period_seconds > 0 && requested >= bar_time + period_seconds))
   {
      SetStatus("The requested candle is outside available MT5 history.");
      return;
   }

   string timeframe = EnumToString(tf);
   string id = StringFormat("MT5-%I64d-%d", GetMicrosecondCount(), MathRand());
   string line = id + "|add|" + Clean(symbol) + "|" + Clean(timeframe) + "|" +
                 IntegerToString((long)bar_time) + "|MT5Exported|" +
                 IntegerToString((long)TimeGMT()) + "|" + Clean(label);
   if(AppendLine(OUT_FILE, line))
   {
      if(symbol == _Symbol && timeframe == EnumToString((ENUM_TIMEFRAMES)_Period))
         DrawMarker(id, bar_time, label, clrRed);
      SetSelectedCandle(bar_time, symbol, timeframe);
      SetStatus(symbol == _Symbol && timeframe == EnumToString((ENUM_TIMEFRAMES)_Period)
         ? "Exact candle marked and exported to TickLab."
         : "Exact candle exported. Open its matching MT5 chart.");
      selected_marker_id = id;
      mark_mode = false;
      ObjectDelete(0, SelectionObject());
      UpdateMarkButton();
   }
   else
   {
      SetStatus("Export failed. Check MT5 Common Files permissions.");
   }
   ChartRedraw();
}

void RemoveSelectedMarker()
{
   if(selected_marker_id == "")
   {
      SetStatus("Click a vertical marker line first.");
      return;
   }

   string object_name = MarkerObject(selected_marker_id);
   if(ObjectFind(0, object_name) < 0)
   {
      SetStatus("The selected marker is no longer on this chart.");
      selected_marker_id = "";
      return;
   }

   datetime when = (datetime)ObjectGetInteger(0, object_name, OBJPROP_TIME, 0);
   ObjectDelete(0, object_name);
   if(StringFind(selected_marker_id, "FIND-") != 0)
   {
      string line = selected_marker_id + "|remove|" + Clean(_Symbol) + "|" +
                    Clean(EnumToString((ENUM_TIMEFRAMES)_Period)) + "|" +
                    IntegerToString((long)when) + "|MT5|" +
                    IntegerToString((long)TimeGMT()) + "|removed";
      AppendLine(OUT_FILE, line);
      SetStatus("Marker removed and removal exported to TickLab.");
   }
   else
   {
      SetStatus("Local Find marker removed.");
   }
   selected_marker_id = "";
   ChartRedraw();
}

void ClearExportedMarkers()
{
   int removed = 0;
   string local_prefix = PREFIX + "LINE_MT5-";
   for(int index = ObjectsTotal(0, 0, -1) - 1; index >= 0; index--)
   {
      string name = ObjectName(0, index, 0, -1);
      if(StringFind(name, local_prefix) == 0)
      {
         string id = StringSubstr(name, StringLen(PREFIX + "LINE_"));
         datetime when = (datetime)ObjectGetInteger(0, name, OBJPROP_TIME, 0);
         string line = id + "|remove|" + Clean(_Symbol) + "|" +
                       Clean(EnumToString((ENUM_TIMEFRAMES)_Period)) + "|" +
                       IntegerToString((long)when) + "|MT5Exported|" +
                       IntegerToString((long)TimeGMT()) + "|cleared";
         AppendLine(OUT_FILE, line);
         ObjectDelete(0, name);
         removed++;
      }
   }
   selected_marker_id = "";
   SetStatus("Cleared " + IntegerToString(removed) + " locally exported marker(s).");
   ChartRedraw();
}

void ProcessIncoming(bool rebuild_all)
{
   int handle = FileOpen(IN_FILE, FILE_READ|FILE_TXT|FILE_ANSI|FILE_COMMON|FILE_SHARE_READ|FILE_SHARE_WRITE);
   if(handle == INVALID_HANDLE)
      return;

   int cursor = rebuild_all ? 0 : LoadCursor();
   int line_number = 0;
   while(!FileIsEnding(handle))
   {
      string line = FileReadString(handle);
      if(line_number >= cursor && StringLen(line) > 0)
         ApplyIncomingLine(line);
      line_number++;
   }
   FileClose(handle);
   SaveCursor(line_number);
}

void ApplyIncomingLine(string line)
{
   string parts[];
   int count = StringSplit(line, '|', parts);
   if(count < 8)
      return;

   string id = parts[0];
   string action = parts[1];
   string symbol = parts[2];
   string timeframe = parts[3];
   datetime when = (datetime)StringToInteger(parts[4]);
   string source = parts[5];
   string label = parts[7];

   if(action == "remove")
   {
      ObjectDelete(0, MarkerObject(id));
      return;
   }
   if(action != "add")
      return;
   if(symbol != _Symbol || timeframe != EnumToString((ENUM_TIMEFRAMES)_Period))
      return;

   DrawMarker(id, when, label, clrYellow);
   int shift = iBarShift(_Symbol, (ENUM_TIMEFRAMES)_Period, when, false);
   if(shift >= 0)
      ChartNavigate(0, CHART_END, -shift);
   selected_marker_id = id;
   SetStatus("TickLab marker received and centered.");
   ChartRedraw();
}

void DrawMarker(string id, datetime when, string label, color line_color)
{
   string name = MarkerObject(id);
   if(ObjectFind(0, name) < 0)
      ObjectCreate(0, name, OBJ_VLINE, 0, when, 0);
   ObjectSetInteger(0, name, OBJPROP_TIME, 0, when);
   ObjectSetInteger(0, name, OBJPROP_COLOR, line_color);
   ObjectSetInteger(0, name, OBJPROP_STYLE, STYLE_SOLID);
   ObjectSetInteger(0, name, OBJPROP_WIDTH, 5);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, true);
   ObjectSetInteger(0, name, OBJPROP_SELECTED, false);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN, false);
   ObjectSetInteger(0, name, OBJPROP_ZORDER, 50);
   ObjectSetString(0, name, OBJPROP_TOOLTIP, label + "\nTickLab marker - normal MT5 lines are ignored.");
   ChartRedraw();
}

void DrawSelection(datetime when)
{
   string name = SelectionObject();
   if(ObjectFind(0, name) < 0)
      ObjectCreate(0, name, OBJ_VLINE, 0, when, 0);
   ObjectSetInteger(0, name, OBJPROP_TIME, 0, when);
   ObjectSetInteger(0, name, OBJPROP_COLOR, clrYellow);
   ObjectSetInteger(0, name, OBJPROP_STYLE, STYLE_SOLID);
   ObjectSetInteger(0, name, OBJPROP_WIDTH, 1);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, true);
   ObjectSetInteger(0, name, OBJPROP_SELECTED, true);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN, false);
   ObjectSetInteger(0, name, OBJPROP_ZORDER, 90);
   ObjectSetString(0, name, OBJPROP_TOOLTIP, "TickLab thin yellow selection - drag to another candle.");
}

void UpdateSelectionFromLine()
{
   if(ObjectFind(0, SelectionObject()) < 0)
      return;
   datetime requested = (datetime)ObjectGetInteger(0, SelectionObject(), OBJPROP_TIME, 0);
   int shift = iBarShift(_Symbol, (ENUM_TIMEFRAMES)_Period, requested, false);
   if(shift < 0)
      return;
   datetime bar_time = iTime(_Symbol, (ENUM_TIMEFRAMES)_Period, shift);
   DrawSelection(bar_time);
   SetSelectedCandle(bar_time, _Symbol, EnumToString((ENUM_TIMEFRAMES)_Period));
}

void ToggleMarkMode()
{
   mark_mode = !mark_mode;
   if(mark_mode)
   {
      datetime bar_time = StringToTime(ObjectGetString(0, N("DATE"), OBJPROP_TEXT) + " " +
                                       ObjectGetString(0, N("TIME"), OBJPROP_TEXT));
      if(bar_time <= 0)
         bar_time = iTime(_Symbol, (ENUM_TIMEFRAMES)_Period, 0);
      int shift = iBarShift(_Symbol, (ENUM_TIMEFRAMES)_Period, bar_time, false);
      if(shift >= 0)
         bar_time = iTime(_Symbol, (ENUM_TIMEFRAMES)_Period, shift);
      DrawSelection(bar_time);
      SetStatus("Mark ON - drag the thin solid yellow line, then Export.");
   }
   else
   {
      ObjectDelete(0, SelectionObject());
      SetStatus("Mark OFF - temporary selection removed.");
   }
   UpdateMarkButton();
   ChartRedraw();
}

void UpdateMarkButton()
{
   if(!panel_visible || ObjectFind(0, N("MARK")) < 0)
      return;
   ObjectSetString(0, N("MARK"), OBJPROP_TEXT, mark_mode ? "Unmark" : "Mark");
   ObjectSetInteger(0, N("MARK"), OBJPROP_BGCOLOR, mark_mode ? clrDarkGoldenrod : clrDarkSlateGray);
   ObjectSetInteger(0, N("MARK"), OBJPROP_STATE, false);
}

void SetStatus(string text)
{
   if(panel_visible && ObjectFind(0, N("STATUS")) >= 0)
      ObjectSetString(0, N("STATUS"), OBJPROP_TEXT, text);
}

void UpdateReceiveButton()
{
   if(!panel_visible || ObjectFind(0, N("RECEIVE")) < 0)
      return;
   ObjectSetString(0, N("RECEIVE"), OBJPROP_TEXT,
      receive_enabled ? "Receive ON" : "Receive OFF");
   ObjectSetInteger(0, N("RECEIVE"), OBJPROP_BGCOLOR,
      receive_enabled ? clrDarkGreen : clrDimGray);
   ObjectSetInteger(0, N("RECEIVE"), OBJPROP_STATE, false);
   ChartRedraw();
}

bool AppendLine(string path, string line)
{
   int handle = FileOpen(path, FILE_READ|FILE_WRITE|FILE_TXT|FILE_ANSI|FILE_COMMON|FILE_SHARE_READ|FILE_SHARE_WRITE);
   if(handle == INVALID_HANDLE)
      return(false);
   FileSeek(handle, 0, SEEK_END);
   FileWriteString(handle, line + "\r\n");
   FileFlush(handle);
   FileClose(handle);
   return(true);
}

int LoadCursor()
{
   int handle = FileOpen(CursorFile(), FILE_READ|FILE_TXT|FILE_ANSI|FILE_COMMON|FILE_SHARE_READ|FILE_SHARE_WRITE);
   if(handle == INVALID_HANDLE)
      return(0);
   string value = FileReadString(handle);
   FileClose(handle);
   return((int)StringToInteger(value));
}

void SaveCursor(int cursor)
{
   int handle = FileOpen(CursorFile(), FILE_WRITE|FILE_TXT|FILE_ANSI|FILE_COMMON|FILE_SHARE_READ|FILE_SHARE_WRITE);
   if(handle == INVALID_HANDLE)
      return;
   FileWriteString(handle, IntegerToString(cursor));
   FileClose(handle);
}

bool LoadReceiveState(bool fallback)
{
   int handle = FileOpen(RECEIVE_STATE, FILE_READ|FILE_TXT|FILE_ANSI|FILE_COMMON|FILE_SHARE_READ|FILE_SHARE_WRITE);
   if(handle == INVALID_HANDLE)
      return(fallback);
   string value = FileReadString(handle);
   FileClose(handle);
   return(value == "1");
}

void SaveReceiveState()
{
   int handle = FileOpen(RECEIVE_STATE, FILE_WRITE|FILE_TXT|FILE_ANSI|FILE_COMMON|FILE_SHARE_READ|FILE_SHARE_WRITE);
   if(handle == INVALID_HANDLE)
      return;
   FileWriteString(handle, receive_enabled ? "1" : "0");
   FileClose(handle);
}

bool TryParseTimeframe(string value, ENUM_TIMEFRAMES &timeframe)
{
   StringTrimLeft(value);
   StringTrimRight(value);
   StringToUpper(value);
   if(value == "PERIOD_M1" || value == "M1") timeframe = PERIOD_M1;
   else if(value == "PERIOD_M2" || value == "M2") timeframe = PERIOD_M2;
   else if(value == "PERIOD_M3" || value == "M3") timeframe = PERIOD_M3;
   else if(value == "PERIOD_M4" || value == "M4") timeframe = PERIOD_M4;
   else if(value == "PERIOD_M5" || value == "M5") timeframe = PERIOD_M5;
   else if(value == "PERIOD_M6" || value == "M6") timeframe = PERIOD_M6;
   else if(value == "PERIOD_M10" || value == "M10") timeframe = PERIOD_M10;
   else if(value == "PERIOD_M12" || value == "M12") timeframe = PERIOD_M12;
   else if(value == "PERIOD_M15" || value == "M15") timeframe = PERIOD_M15;
   else if(value == "PERIOD_M20" || value == "M20") timeframe = PERIOD_M20;
   else if(value == "PERIOD_M30" || value == "M30") timeframe = PERIOD_M30;
   else if(value == "PERIOD_H1" || value == "H1") timeframe = PERIOD_H1;
   else if(value == "PERIOD_H2" || value == "H2") timeframe = PERIOD_H2;
   else if(value == "PERIOD_H3" || value == "H3") timeframe = PERIOD_H3;
   else if(value == "PERIOD_H4" || value == "H4") timeframe = PERIOD_H4;
   else if(value == "PERIOD_H6" || value == "H6") timeframe = PERIOD_H6;
   else if(value == "PERIOD_H8" || value == "H8") timeframe = PERIOD_H8;
   else if(value == "PERIOD_H12" || value == "H12") timeframe = PERIOD_H12;
   else if(value == "PERIOD_D1" || value == "D1") timeframe = PERIOD_D1;
   else if(value == "PERIOD_W1" || value == "W1") timeframe = PERIOD_W1;
   else if(value == "PERIOD_MN1" || value == "MN1") timeframe = PERIOD_MN1;
   else return(false);
   return(true);
}

string Clean(string value)
{
   StringReplace(value, "|", "/");
   StringReplace(value, "\r", " ");
   StringReplace(value, "\n", " ");
   return(value);
}

void SetRect(string name, int x, int y, int width, int height)
{
   if(ObjectFind(0, name) < 0)
      return;
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, name, OBJPROP_XSIZE, MathMax(1, width));
   ObjectSetInteger(0, name, OBJPROP_YSIZE, MathMax(1, height));
}

void SetPoint(string name, int x, int y)
{
   if(ObjectFind(0, name) < 0)
      return;
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
}

void CreateRect(string name, int x, int y, int width, int height, int zorder)
{
   ObjectCreate(0, name, OBJ_RECTANGLE_LABEL, 0, 0, 0);
   ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, name, OBJPROP_XSIZE, width);
   ObjectSetInteger(0, name, OBJPROP_YSIZE, height);
   ObjectSetInteger(0, name, OBJPROP_BGCOLOR, clrBlack);
   ObjectSetInteger(0, name, OBJPROP_BORDER_COLOR, clrSlateGray);
   ObjectSetInteger(0, name, OBJPROP_BACK, false);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN, false);
   ObjectSetInteger(0, name, OBJPROP_ZORDER, zorder);
   ObjectSetInteger(0, name, OBJPROP_TIMEFRAMES, OBJ_ALL_PERIODS);
}

void CreateButton(string name, string text, int x, int y, int width, int height, int zorder)
{
   ObjectCreate(0, name, OBJ_BUTTON, 0, 0, 0);
   ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, name, OBJPROP_XSIZE, width);
   ObjectSetInteger(0, name, OBJPROP_YSIZE, height);
   ObjectSetInteger(0, name, OBJPROP_BGCOLOR, clrDarkSlateGray);
   ObjectSetInteger(0, name, OBJPROP_COLOR, clrWhite);
   ObjectSetInteger(0, name, OBJPROP_BORDER_COLOR, clrSlateGray);
   ObjectSetString(0, name, OBJPROP_TEXT, text);
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE, 9);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN, false);
   ObjectSetInteger(0, name, OBJPROP_ZORDER, zorder);
   ObjectSetInteger(0, name, OBJPROP_TIMEFRAMES, OBJ_ALL_PERIODS);
   ObjectSetInteger(0, name, OBJPROP_STATE, false);
}

void CreateEdit(string name, string text, int x, int y, int width, int height, int zorder)
{
   ObjectCreate(0, name, OBJ_EDIT, 0, 0, 0);
   ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, name, OBJPROP_XSIZE, width);
   ObjectSetInteger(0, name, OBJPROP_YSIZE, height);
   ObjectSetInteger(0, name, OBJPROP_BGCOLOR, clrWhite);
   ObjectSetInteger(0, name, OBJPROP_COLOR, clrBlack);
   ObjectSetInteger(0, name, OBJPROP_BORDER_COLOR, clrSlateGray);
   ObjectSetString(0, name, OBJPROP_TEXT, text);
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE, 9);
   // V109: keep the exact V107 layout, but make the field a real keyboard-editable input.
   ObjectSetInteger(0, name, OBJPROP_ALIGN, ALIGN_LEFT);
   ObjectSetInteger(0, name, OBJPROP_READONLY, false);
   ObjectSetInteger(0, name, OBJPROP_BACK, false);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, name, OBJPROP_SELECTED, false);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN, false);
   ObjectSetInteger(0, name, OBJPROP_ZORDER, zorder);
   ObjectSetInteger(0, name, OBJPROP_TIMEFRAMES, OBJ_ALL_PERIODS);
   ObjectSetString(0, name, OBJPROP_TOOLTIP, "Click inside and type");
}

void CreateLabel(string name, string text, int x, int y, int size, int zorder)
{
   ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, name, OBJPROP_ANCHOR, ANCHOR_LEFT_UPPER);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, name, OBJPROP_COLOR, clrWhite);
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE, size);
   ObjectSetString(0, name, OBJPROP_TEXT, text);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN, false);
   ObjectSetInteger(0, name, OBJPROP_ZORDER, zorder);
   ObjectSetInteger(0, name, OBJPROP_TIMEFRAMES, OBJ_ALL_PERIODS);
}
