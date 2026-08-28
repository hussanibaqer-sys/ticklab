//+------------------------------------------------------------------+
//|                                         ScalperPanel_v5.43.mq5   |
//|                                    One-Click Scalping Tool       |
//|                                + SL Line (draggable, longer)     |
//|                                + Wide error popup                |
//|                                + Daily trade limit (20 max)      |
//|                                + Extension code (110) +10 trades |
//|                                + Trade counter on panel          |
//|                                + Breakeven +10 points            |
//|                                + 50/100/200/300 SL presets       |
//|                                + TP presets 500-3000             |
//|                                + SL line ON by default           |
//|                                + Enlarged panel frame            |
//|                                + DAILY RESET AT MIDNIGHT         |
//|                                + Fixed file I/O warning          |
//+------------------------------------------------------------------+
#property copyright "Scalper Tool"
#property version   "5.43"
#property strict

//+------------------------------------------------------------------+
//| Input parameters                                                  |
//+------------------------------------------------------------------+
input double InpDefaultLot = 0.02;     // Default Lot Size

//+------------------------------------------------------------------+
//| Constants                                                         |
//+------------------------------------------------------------------+
#define ARROW_STEP_POINTS 100
#define MAX_DAILY_TRADES 20
#define EXTRA_TRADES 10
#define UNLOCK_CODE "110"
#define TRADE_HISTORY_FILE "Scalper_TradeHistory.dat"

//+------------------------------------------------------------------+
//| Global variables                                                  |
//+------------------------------------------------------------------+
string g_prefix = "Scalper_";
bool   g_showMain = false;
bool   g_orderWindow = false;

int    g_x = 30;
int    g_y = 50;

double g_lotSize = 0.02;
double g_slPrice = 0.0;
double g_tpPrice = 0.0;
string g_direction = "BUY";
double g_currentPrice = 0.0;
double g_point = 0.01;

bool   g_slPresetActive = false;
int    g_slPresetPoints = 0;
int    g_tpPresetPoints = 0;

string g_slLineName = "";

bool   g_popupActive = false;

bool   g_arrowHeld = false;
string g_arrowName = "";

bool   g_leftButton = false;

bool   g_tradeInProgress = false;

// Daily trade tracking
int    g_dailyTradeCount = 0;
bool   g_dailyLimitReached = false;

// Extra trades extension
bool   g_extraTradesAllowed = false;
int    g_extraTradesRemaining = 0;

// Trade history storage
struct TradeRecord
{
   datetime time;
};

TradeRecord g_tradeHistory[];
int g_tradeHistoryCount = 0;

// Daily reset tracking
datetime g_lastResetDate = 0;   // start of last reset day (midnight server time)

//+------------------------------------------------------------------+
//| Expert initialization                                            |
//+------------------------------------------------------------------+
int OnInit()
{
   g_point = SymbolInfoDouble(Symbol(), SYMBOL_POINT);
   if(g_point <= 0) g_point = 0.01;
   CreateShowButton();
   LoadTradeHistory();
   // Set last reset date to today's start
   MqlDateTime dt;
   TimeCurrent(dt);
   dt.hour = 0; dt.min = 0; dt.sec = 0;
   g_lastResetDate = StructToTime(dt);
   UpdateDailyTradeCount();   // count trades from today
   UpdatePanelTradeCounter();
   return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
//| Deinitialization                                                 |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   ObjectsDeleteAll(0, g_prefix);
   EventKillTimer();
   ChartRedraw();
}

//+------------------------------------------------------------------+
//| OnTick – live price updates & daily reset check                  |
//+------------------------------------------------------------------+
void OnTick()
{
   // Check for day change (reset at midnight server time)
   MqlDateTime dt;
   TimeCurrent(dt);
   dt.hour = 0; dt.min = 0; dt.sec = 0;
   datetime todayStart = StructToTime(dt);
   if(todayStart != g_lastResetDate)
   {
      ResetDailyCounter();
   }

   if(g_orderWindow && !g_popupActive && !g_tradeInProgress)
   {
      UpdatePriceDisplay();
   }
}

//+------------------------------------------------------------------+
//| ChartEvent                                                       |
//+------------------------------------------------------------------+
void OnChartEvent(const int id,
                  const long &lparam,
                  const double &dparam,
                  const string &sparam)
{
   if(id == CHARTEVENT_MOUSE_MOVE)
   {
      int comma1 = StringFind(sparam, ",");
      int comma2 = StringFind(sparam, ",", comma1+1);
      int state = 0;
      if(comma2 > 0)
         state = (int)StringToInteger(StringSubstr(sparam, comma2+1));
      bool leftWas = g_leftButton;
      g_leftButton = (state & 1) != 0;
      if(leftWas && !g_leftButton && g_arrowHeld)
      {
         g_arrowHeld = false;
         g_arrowName = "";
         EventKillTimer();
         ChartRedraw();
      }
      return;
   }

   if(id == CHARTEVENT_OBJECT_CLICK)
   {
      string objName = sparam;
      if(g_popupActive)
      {
         // Handle unlock popup buttons
         if(objName == g_prefix + "UnlockPopupOK")
         {
            string code = ObjectGetString(0, g_prefix + "UnlockPopupEdit", OBJPROP_TEXT);
            if(code == UNLOCK_CODE)
            {
               g_extraTradesAllowed = true;
               g_extraTradesRemaining = EXTRA_TRADES;
               CloseUnlockPopup();
               UpdatePanelTradeCounter();
               PlaySound("ok.wav");
            }
            else
            {
               ShowErrorPopup("Wrong unlock code!");
            }
         }
         else if(objName == g_prefix + "UnlockPopupCancel" || objName == g_prefix + "UnlockPopupClose")
         {
            CloseUnlockPopup();
         }
         else if(objName == g_prefix + "PopupOK")
         {
            CloseErrorPopup();
         }
         return;
      }

      // Normal button clicks
      if(objName == g_prefix + "ShowBtn")
      {
         g_showMain = !g_showMain;
         if(g_showMain)
         {
            CreateOrderButton();
            ObjectSetString(0, g_prefix + "ShowBtn", OBJPROP_TEXT, "▲ HIDE");
         }
         else HideAll();
      }
      else if(objName == g_prefix + "OrderBtn")
      {
         g_orderWindow = !g_orderWindow;
         if(g_orderWindow) CreateOrderWindow();
         else CloseOrderWindow();
      }
      else if(objName == g_prefix + "UnlockBtn")
      {
         if(!g_extraTradesAllowed)
            ShowUnlockPopup();
      }
      else if(objName == g_prefix + "DirBuy")
      {
         g_direction = "BUY";
         UpdateDirectionHighlight();
         UpdatePriceDisplay();
         if(g_slPresetActive) UpdateSLFromPreset();
      }
      else if(objName == g_prefix + "DirSell")
      {
         g_direction = "SELL";
         UpdateDirectionHighlight();
         UpdatePriceDisplay();
         if(g_slPresetActive) UpdateSLFromPreset();
      }
      // SL presets
      else if(objName == g_prefix + "SL50")  SetSLPreset(50);
      else if(objName == g_prefix + "SL100") SetSLPreset(100);
      else if(objName == g_prefix + "SL200") SetSLPreset(200);
      else if(objName == g_prefix + "SL300") SetSLPreset(300);
      // TP presets
      else if(objName == g_prefix + "TP500")  SetTPPreset(500);
      else if(objName == g_prefix + "TP1000") SetTPPreset(1000);
      else if(objName == g_prefix + "TP1500") SetTPPreset(1500);
      else if(objName == g_prefix + "TP2000") SetTPPreset(2000);
      else if(objName == g_prefix + "TP3000") SetTPPreset(3000);
      // Other controls
      else if(objName == g_prefix + "SLToggle")
      {
         ToggleSLLine();
      }
      else if(objName == g_prefix + "VolUp")
      {
         double val = StringToDouble(ObjectGetString(0, g_prefix + "VolEdit", OBJPROP_TEXT));
         val += 0.01;
         val = NormalizeDouble(val, 2);
         if(val < 0) val = 0;
         ObjectSetString(0, g_prefix + "VolEdit", OBJPROP_TEXT, DoubleToString(val, 2));
         g_lotSize = val;
         ChartRedraw();
      }
      else if(objName == g_prefix + "VolDown")
      {
         double val = StringToDouble(ObjectGetString(0, g_prefix + "VolEdit", OBJPROP_TEXT));
         val -= 0.01;
         if(val < 0) val = 0;
         val = NormalizeDouble(val, 2);
         ObjectSetString(0, g_prefix + "VolEdit", OBJPROP_TEXT, DoubleToString(val, 2));
         g_lotSize = val;
         ChartRedraw();
      }
      else if(objName == g_prefix + "SLUp")
      {
         g_arrowHeld = true;
         g_arrowName = "SLUp";
         g_slPresetActive = false;
         HighlightSLPreset(0);
         AdjustSLPrice(+ARROW_STEP_POINTS);
         EventSetTimer(50);
      }
      else if(objName == g_prefix + "SLDown")
      {
         g_arrowHeld = true;
         g_arrowName = "SLDown";
         g_slPresetActive = false;
         HighlightSLPreset(0);
         AdjustSLPrice(-ARROW_STEP_POINTS);
         EventSetTimer(50);
      }
      else if(objName == g_prefix + "TPUp")
      {
         g_arrowHeld = true;
         g_arrowName = "TPUp";
         AdjustTPPrice(+ARROW_STEP_POINTS);
         EventSetTimer(50);
      }
      else if(objName == g_prefix + "TPDown")
      {
         g_arrowHeld = true;
         g_arrowName = "TPDown";
         AdjustTPPrice(-ARROW_STEP_POINTS);
         EventSetTimer(50);
      }
      else if(objName == g_prefix + "BuyBtn")
      {
         g_direction = "BUY";
         UpdateDirectionHighlight();
         ExecuteTrade(ORDER_TYPE_BUY);
      }
      else if(objName == g_prefix + "SellBtn")
      {
         g_direction = "SELL";
         UpdateDirectionHighlight();
         ExecuteTrade(ORDER_TYPE_SELL);
      }
      else if(objName == g_prefix + "BreakevenBtn")
      {
         BreakevenPlus10();
      }
      else if(objName == g_prefix + "CloseWin")
      {
         g_orderWindow = false;
         CloseOrderWindow();
      }
   }

   if(id == CHARTEVENT_OBJECT_ENDEDIT && !g_popupActive)
   {
      string objName = sparam;
      if(objName == g_prefix + "SLEdit")
      {
         string text = ObjectGetString(0, objName, OBJPROP_TEXT);
         if(StringLen(text) > 0)
         {
            double val = StringToDouble(text);
            if(val > 0)
            {
               g_slPrice = val;
               g_slPresetActive = false;
               HighlightSLPreset(0);
               UpdateSLLinePrice();
            }
         }
      }
      else if(objName == g_prefix + "TPEdit")
      {
         string text = ObjectGetString(0, objName, OBJPROP_TEXT);
         if(StringLen(text) > 0)
         {
            double val = StringToDouble(text);
            g_tpPrice = (val > 0) ? val : 0;
         }
         else g_tpPrice = 0;
      }
      else if(objName == g_prefix + "VolEdit")
      {
         string text = ObjectGetString(0, objName, OBJPROP_TEXT);
         if(StringLen(text) > 0)
         {
            double val = StringToDouble(text);
            if(val >= 0) g_lotSize = val;
            else ObjectSetString(0, objName, OBJPROP_TEXT, DoubleToString(g_lotSize, 2));
         }
      }
   }

   if(id == CHARTEVENT_OBJECT_DRAG)
   {
      string objName = sparam;
      if(objName == g_slLineName && ObjectFind(0, objName) >= 0)
      {
         double newSL = ObjectGetDouble(0, objName, OBJPROP_PRICE, 0);
         if(newSL > 0)
         {
            int digits = (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS);
            newSL = NormalizeDouble(newSL, digits);
            g_slPrice = newSL;
            if(g_slPresetActive)
            {
               g_slPresetActive = false;
               HighlightSLPreset(0);
            }
            ObjectSetString(0, g_prefix + "SLEdit", OBJPROP_TEXT, DoubleToString(newSL, digits));
            ChartRedraw();
         }
      }
   }

   if(id == CHARTEVENT_CHART_CHANGE && !g_popupActive)
   {
      if(g_orderWindow)
      {
         UpdatePriceDisplay();
         if(g_slPresetActive) UpdateSLFromPreset();
      }
   }
}

//+------------------------------------------------------------------+
//| Timer – arrow hold                                               |
//+------------------------------------------------------------------+
void OnTimer()
{
   if(!g_orderWindow || g_popupActive) return;
   if(!g_arrowHeld) { EventKillTimer(); return; }
   if(!g_leftButton)
   {
      g_arrowHeld = false;
      g_arrowName = "";
      EventKillTimer();
      return;
   }
   if(g_arrowName == "SLUp")      AdjustSLPrice(+ARROW_STEP_POINTS);
   else if(g_arrowName == "SLDown") AdjustSLPrice(-ARROW_STEP_POINTS);
   else if(g_arrowName == "TPUp")   AdjustTPPrice(+ARROW_STEP_POINTS);
   else if(g_arrowName == "TPDown") AdjustTPPrice(-ARROW_STEP_POINTS);
}

//+------------------------------------------------------------------+
//| SL Line toggle – 600 seconds each side                           |
//+------------------------------------------------------------------+
void ToggleSLLine()
{
   if(g_slLineName != "" && ObjectFind(0, g_slLineName) >= 0)
   {
      ObjectDelete(0, g_slLineName);
      g_slLineName = "";
      ObjectSetString(0, g_prefix + "SLToggle", OBJPROP_TEXT, "🔴 SL Line");
      ChartRedraw();
      return;
   }

   double price = g_slPrice;
   if(price <= 0) price = g_currentPrice;
   if(price <= 0) return;

   datetime t1 = TimeCurrent() - 600;
   datetime t2 = TimeCurrent() + 600;
   string lineName = g_prefix + "SLLine_" + IntegerToString(rand());
   if(ObjectCreate(0, lineName, OBJ_TREND, 0, t1, price, t2, price))
   {
      ObjectSetInteger(0, lineName, OBJPROP_COLOR, clrRed);
      ObjectSetInteger(0, lineName, OBJPROP_STYLE, STYLE_DASHDOT);
      ObjectSetInteger(0, lineName, OBJPROP_WIDTH, 2);
      ObjectSetInteger(0, lineName, OBJPROP_BACK, false);
      ObjectSetInteger(0, lineName, OBJPROP_SELECTABLE, true);
      ObjectSetInteger(0, lineName, OBJPROP_RAY_RIGHT, false);
      ObjectSetInteger(0, lineName, OBJPROP_SELECTED, true);
      ObjectSetInteger(0, lineName, OBJPROP_ZORDER, 10);

      g_slLineName = lineName;
      ObjectSetString(0, g_prefix + "SLToggle", OBJPROP_TEXT, "✅ SL Line");
      ChartRedraw();
   }
}

//+------------------------------------------------------------------+
//| Update SL line position                                          |
//+------------------------------------------------------------------+
void UpdateSLLinePrice()
{
   if(g_slLineName == "" || ObjectFind(0, g_slLineName) < 0) return;
   if(g_slPrice <= 0) return;
   ObjectSetDouble(0, g_slLineName, OBJPROP_PRICE, 0, g_slPrice);
   ObjectSetDouble(0, g_slLineName, OBJPROP_PRICE, 1, g_slPrice);
   ChartRedraw();
}

//+------------------------------------------------------------------+
//| Set SL preset                                                    |
//+------------------------------------------------------------------+
void SetSLPreset(int points)
{
   g_slPresetPoints = points;
   g_slPresetActive = true;
   HighlightSLPreset(points);
   UpdateSLFromPreset();
   UpdateSLLinePrice();
}

//+------------------------------------------------------------------+
//| Highlight SL preset (50,100,200,300)                             |
//+------------------------------------------------------------------+
void HighlightSLPreset(int selected)
{
   color bg50  = (selected == 50)  ? clrYellow : clrGray;
   color bg100 = (selected == 100) ? clrYellow : clrGray;
   color bg200 = (selected == 200) ? clrYellow : clrGray;
   color bg300 = (selected == 300) ? clrYellow : clrGray;
   ObjectSetInteger(0, g_prefix + "SL50",  OBJPROP_BGCOLOR, bg50);
   ObjectSetInteger(0, g_prefix + "SL100", OBJPROP_BGCOLOR, bg100);
   ObjectSetInteger(0, g_prefix + "SL200", OBJPROP_BGCOLOR, bg200);
   ObjectSetInteger(0, g_prefix + "SL300", OBJPROP_BGCOLOR, bg300);
   ChartRedraw();
}

//+------------------------------------------------------------------+
//| Highlight TP preset (none selected by default)                   |
//+------------------------------------------------------------------+
void HighlightTPPreset(int selected)
{
   color bg500  = (selected == 500)  ? clrYellow : clrGray;
   color bg1000 = (selected == 1000) ? clrYellow : clrGray;
   color bg1500 = (selected == 1500) ? clrYellow : clrGray;
   color bg2000 = (selected == 2000) ? clrYellow : clrGray;
   color bg3000 = (selected == 3000) ? clrYellow : clrGray;
   ObjectSetInteger(0, g_prefix + "TP500",  OBJPROP_BGCOLOR, bg500);
   ObjectSetInteger(0, g_prefix + "TP1000", OBJPROP_BGCOLOR, bg1000);
   ObjectSetInteger(0, g_prefix + "TP1500", OBJPROP_BGCOLOR, bg1500);
   ObjectSetInteger(0, g_prefix + "TP2000", OBJPROP_BGCOLOR, bg2000);
   ObjectSetInteger(0, g_prefix + "TP3000", OBJPROP_BGCOLOR, bg3000);
   ChartRedraw();
}

//+------------------------------------------------------------------+
//| Set TP preset (0 = none selected)                                |
//+------------------------------------------------------------------+
void SetTPPreset(int points)
{
   g_tpPresetPoints = points;
   HighlightTPPreset(points);
   if(points > 0)
      UpdateTPFromPreset();
   else
   {
      g_tpPrice = 0;
      ObjectSetString(0, g_prefix + "TPEdit", OBJPROP_TEXT, "");
   }
}

//+------------------------------------------------------------------+
//| Update TP from preset                                            |
//+------------------------------------------------------------------+
void UpdateTPFromPreset()
{
   if(!g_orderWindow || g_tpPresetPoints <= 0) return;
   int digits = (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS);
   double point = SymbolInfoDouble(Symbol(), SYMBOL_POINT);
   double distance = g_tpPresetPoints * point;
   double ask = SymbolInfoDouble(Symbol(), SYMBOL_ASK);
   double bid = SymbolInfoDouble(Symbol(), SYMBOL_BID);
   double entryPrice = (g_direction == "BUY") ? ask : bid;
   if(entryPrice <= 0) return;
   if(g_direction == "BUY")
      g_tpPrice = entryPrice + distance;
   else
      g_tpPrice = entryPrice - distance;
   g_tpPrice = NormalizeDouble(g_tpPrice, digits);
   ObjectSetString(0, g_prefix + "TPEdit", OBJPROP_TEXT, DoubleToString(g_tpPrice, digits));
   ChartRedraw();
}

//+------------------------------------------------------------------+
//| Adjust SL price                                                  |
//+------------------------------------------------------------------+
void AdjustSLPrice(int deltaPoints)
{
   int digits = (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS);
   if(g_slPrice == 0) g_slPrice = g_currentPrice;
   g_slPrice += deltaPoints * g_point;
   g_slPrice = NormalizeDouble(g_slPrice, digits);
   if(g_slPrice < 0) g_slPrice = 0;
   ObjectSetString(0, g_prefix + "SLEdit", OBJPROP_TEXT, DoubleToString(g_slPrice, digits));
   UpdateSLLinePrice();
   ChartRedraw();
}

//+------------------------------------------------------------------+
//| Adjust TP price                                                  |
//+------------------------------------------------------------------+
void AdjustTPPrice(int deltaPoints)
{
   int digits = (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS);
   if(g_tpPrice == 0) g_tpPrice = g_currentPrice;
   g_tpPrice += deltaPoints * g_point;
   g_tpPrice = NormalizeDouble(g_tpPrice, digits);
   if(g_tpPrice < 0) g_tpPrice = 0;
   ObjectSetString(0, g_prefix + "TPEdit", OBJPROP_TEXT, DoubleToString(g_tpPrice, digits));
   ChartRedraw();
}

//+------------------------------------------------------------------+
//| Update SL from preset                                            |
//+------------------------------------------------------------------+
void UpdateSLFromPreset()
{
   if(!g_orderWindow || g_slPresetPoints <= 0) return;
   int digits = (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS);
   double point = SymbolInfoDouble(Symbol(), SYMBOL_POINT);
   double distance = g_slPresetPoints * point;
   double ask = SymbolInfoDouble(Symbol(), SYMBOL_ASK);
   double bid = SymbolInfoDouble(Symbol(), SYMBOL_BID);
   double entryPrice = (g_direction == "BUY") ? ask : bid;
   if(entryPrice <= 0) return;
   if(g_direction == "BUY")
      g_slPrice = entryPrice - distance;
   else
      g_slPrice = entryPrice + distance;
   g_slPrice = NormalizeDouble(g_slPrice, digits);
   ObjectSetString(0, g_prefix + "SLEdit", OBJPROP_TEXT, DoubleToString(g_slPrice, digits));
   UpdateSLLinePrice();
   ChartRedraw();
}

//+------------------------------------------------------------------+
//| Update price display                                             |
//+------------------------------------------------------------------+
void UpdatePriceDisplay()
{
   if(!g_orderWindow) return;
   int digits = (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS);
   double ask = SymbolInfoDouble(Symbol(), SYMBOL_ASK);
   double bid = SymbolInfoDouble(Symbol(), SYMBOL_BID);
   g_currentPrice = (g_direction == "BUY") ? ask : bid;
   string labelName = g_prefix + "PriceLabel";
   if(ObjectFind(0, labelName) >= 0)
   {
      ObjectSetString(0, labelName, OBJPROP_TEXT,
                      "Price: " + DoubleToString(g_currentPrice, digits) +
                      " (" + g_direction + ")");
      ChartRedraw();
   }
   if(g_slPresetActive && g_slPresetPoints > 0)
   {
      UpdateSLFromPreset();
   }
   UpdatePanelTradeCounter();
}

//+------------------------------------------------------------------+
//| Direction highlight                                              |
//+------------------------------------------------------------------+
void UpdateDirectionHighlight()
{
   color bgBuy = (g_direction == "BUY") ? clrLime : clrGray;
   color bgSell = (g_direction == "SELL") ? clrLime : clrGray;
   ObjectSetInteger(0, g_prefix + "DirBuy", OBJPROP_BGCOLOR, bgBuy);
   ObjectSetInteger(0, g_prefix + "DirSell", OBJPROP_BGCOLOR, bgSell);
   ChartRedraw();
}

//+------------------------------------------------------------------+
//| Disable trade buttons                                            |
//+------------------------------------------------------------------+
void DisableTradeButtons(bool disable)
{
   string buyBtn = g_prefix + "BuyBtn";
   string sellBtn = g_prefix + "SellBtn";
   if(ObjectFind(0, buyBtn) >= 0)
   {
      ObjectSetInteger(0, buyBtn, OBJPROP_STATE, disable ? false : true);
      ObjectSetInteger(0, buyBtn, OBJPROP_BGCOLOR, disable ? clrGray : clrBlue);
   }
   if(ObjectFind(0, sellBtn) >= 0)
   {
      ObjectSetInteger(0, sellBtn, OBJPROP_STATE, disable ? false : true);
      ObjectSetInteger(0, sellBtn, OBJPROP_BGCOLOR, disable ? clrGray : clrRed);
   }
   ChartRedraw();
}

//+------------------------------------------------------------------+
//| Error popup                                                      |
//+------------------------------------------------------------------+
void ShowErrorPopup(string message)
{
   if(g_popupActive) return;
   g_popupActive = true;

   int x = 100;
   int y = 200;
   int w = 550;
   int h = 150;

   ObjectCreate(0, g_prefix + "PopupBG", OBJ_RECTANGLE_LABEL, 0, 0, 0);
   ObjectSetInteger(0, g_prefix + "PopupBG", OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, g_prefix + "PopupBG", OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, g_prefix + "PopupBG", OBJPROP_XSIZE, w);
   ObjectSetInteger(0, g_prefix + "PopupBG", OBJPROP_YSIZE, h);
   ObjectSetInteger(0, g_prefix + "PopupBG", OBJPROP_BGCOLOR, clrDarkRed);
   ObjectSetInteger(0, g_prefix + "PopupBG", OBJPROP_BORDER_COLOR, clrWhite);
   ObjectSetInteger(0, g_prefix + "PopupBG", OBJPROP_BORDER_TYPE, BORDER_FLAT);
   ObjectSetInteger(0, g_prefix + "PopupBG", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, g_prefix + "PopupBG", OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, g_prefix + "PopupBG", OBJPROP_ZORDER, 200);

   ObjectCreate(0, g_prefix + "PopupMsg", OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, g_prefix + "PopupMsg", OBJPROP_XDISTANCE, x + 15);
   ObjectSetInteger(0, g_prefix + "PopupMsg", OBJPROP_YDISTANCE, y + 20);
   ObjectSetInteger(0, g_prefix + "PopupMsg", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, g_prefix + "PopupMsg", OBJPROP_COLOR, clrWhite);
   ObjectSetInteger(0, g_prefix + "PopupMsg", OBJPROP_FONTSIZE, 12);
   ObjectSetString(0, g_prefix + "PopupMsg", OBJPROP_TEXT, "❌ " + message);
   ObjectSetInteger(0, g_prefix + "PopupMsg", OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, g_prefix + "PopupMsg", OBJPROP_ZORDER, 201);
   ObjectSetInteger(0, g_prefix + "PopupMsg", OBJPROP_ALIGN, ALIGN_LEFT);

   ObjectCreate(0, g_prefix + "PopupOK", OBJ_BUTTON, 0, 0, 0);
   ObjectSetInteger(0, g_prefix + "PopupOK", OBJPROP_XDISTANCE, x + (w - 80)/2);
   ObjectSetInteger(0, g_prefix + "PopupOK", OBJPROP_YDISTANCE, y + h - 45);
   ObjectSetInteger(0, g_prefix + "PopupOK", OBJPROP_XSIZE, 80);
   ObjectSetInteger(0, g_prefix + "PopupOK", OBJPROP_YSIZE, 30);
   ObjectSetInteger(0, g_prefix + "PopupOK", OBJPROP_BGCOLOR, clrWhite);
   ObjectSetInteger(0, g_prefix + "PopupOK", OBJPROP_COLOR, clrBlack);
   ObjectSetInteger(0, g_prefix + "PopupOK", OBJPROP_FONTSIZE, 11);
   ObjectSetInteger(0, g_prefix + "PopupOK", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetString(0, g_prefix + "PopupOK", OBJPROP_TEXT, "OK");
   ObjectSetInteger(0, g_prefix + "PopupOK", OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, g_prefix + "PopupOK", OBJPROP_ZORDER, 201);

   ChartRedraw();
   PlaySound("alert.wav");
}

void CloseErrorPopup()
{
   g_popupActive = false;
   ObjectsDeleteAll(0, g_prefix + "Popup");
   ChartRedraw();
}

//+------------------------------------------------------------------+
//| Unlock popup – for extra trades                                  |
//+------------------------------------------------------------------+
void ShowUnlockPopup()
{
   if(g_popupActive) return;
   g_popupActive = true;

   int x = 150;
   int y = 250;
   int w = 350;
   int h = 160;

   ObjectCreate(0, g_prefix + "UnlockPopupBG", OBJ_RECTANGLE_LABEL, 0, 0, 0);
   ObjectSetInteger(0, g_prefix + "UnlockPopupBG", OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, g_prefix + "UnlockPopupBG", OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, g_prefix + "UnlockPopupBG", OBJPROP_XSIZE, w);
   ObjectSetInteger(0, g_prefix + "UnlockPopupBG", OBJPROP_YSIZE, h);
   ObjectSetInteger(0, g_prefix + "UnlockPopupBG", OBJPROP_BGCOLOR, clrDarkSlateGray);
   ObjectSetInteger(0, g_prefix + "UnlockPopupBG", OBJPROP_BORDER_COLOR, clrWhite);
   ObjectSetInteger(0, g_prefix + "UnlockPopupBG", OBJPROP_BORDER_TYPE, BORDER_FLAT);
   ObjectSetInteger(0, g_prefix + "UnlockPopupBG", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, g_prefix + "UnlockPopupBG", OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, g_prefix + "UnlockPopupBG", OBJPROP_ZORDER, 200);

   ObjectCreate(0, g_prefix + "UnlockPopupTitle", OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, g_prefix + "UnlockPopupTitle", OBJPROP_XDISTANCE, x + 20);
   ObjectSetInteger(0, g_prefix + "UnlockPopupTitle", OBJPROP_YDISTANCE, y + 15);
   ObjectSetInteger(0, g_prefix + "UnlockPopupTitle", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, g_prefix + "UnlockPopupTitle", OBJPROP_COLOR, clrGold);
   ObjectSetInteger(0, g_prefix + "UnlockPopupTitle", OBJPROP_FONTSIZE, 14);
   ObjectSetString(0, g_prefix + "UnlockPopupTitle", OBJPROP_TEXT, "🔓 Enter Unlock Code");
   ObjectSetInteger(0, g_prefix + "UnlockPopupTitle", OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, g_prefix + "UnlockPopupTitle", OBJPROP_ZORDER, 201);

   ObjectCreate(0, g_prefix + "UnlockPopupLabel", OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, g_prefix + "UnlockPopupLabel", OBJPROP_XDISTANCE, x + 20);
   ObjectSetInteger(0, g_prefix + "UnlockPopupLabel", OBJPROP_YDISTANCE, y + 45);
   ObjectSetInteger(0, g_prefix + "UnlockPopupLabel", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, g_prefix + "UnlockPopupLabel", OBJPROP_COLOR, clrWhite);
   ObjectSetInteger(0, g_prefix + "UnlockPopupLabel", OBJPROP_FONTSIZE, 12);
   ObjectSetString(0, g_prefix + "UnlockPopupLabel", OBJPROP_TEXT, "Enter unlock code:");
   ObjectSetInteger(0, g_prefix + "UnlockPopupLabel", OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, g_prefix + "UnlockPopupLabel", OBJPROP_ZORDER, 201);

   ObjectCreate(0, g_prefix + "UnlockPopupEdit", OBJ_EDIT, 0, 0, 0);
   ObjectSetInteger(0, g_prefix + "UnlockPopupEdit", OBJPROP_XDISTANCE, x + 20);
   ObjectSetInteger(0, g_prefix + "UnlockPopupEdit", OBJPROP_YDISTANCE, y + 70);
   ObjectSetInteger(0, g_prefix + "UnlockPopupEdit", OBJPROP_XSIZE, 150);
   ObjectSetInteger(0, g_prefix + "UnlockPopupEdit", OBJPROP_YSIZE, 25);
   ObjectSetInteger(0, g_prefix + "UnlockPopupEdit", OBJPROP_BGCOLOR, clrBlack);
   ObjectSetInteger(0, g_prefix + "UnlockPopupEdit", OBJPROP_COLOR, clrLime);
   ObjectSetInteger(0, g_prefix + "UnlockPopupEdit", OBJPROP_FONTSIZE, 12);
   ObjectSetInteger(0, g_prefix + "UnlockPopupEdit", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, g_prefix + "UnlockPopupEdit", OBJPROP_ALIGN, ALIGN_CENTER);
   ObjectSetInteger(0, g_prefix + "UnlockPopupEdit", OBJPROP_READONLY, false);
   ObjectSetString(0, g_prefix + "UnlockPopupEdit", OBJPROP_TEXT, "");
   ObjectSetInteger(0, g_prefix + "UnlockPopupEdit", OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, g_prefix + "UnlockPopupEdit", OBJPROP_ZORDER, 201);

   ObjectCreate(0, g_prefix + "UnlockPopupOK", OBJ_BUTTON, 0, 0, 0);
   ObjectSetInteger(0, g_prefix + "UnlockPopupOK", OBJPROP_XDISTANCE, x + 20);
   ObjectSetInteger(0, g_prefix + "UnlockPopupOK", OBJPROP_YDISTANCE, y + 110);
   ObjectSetInteger(0, g_prefix + "UnlockPopupOK", OBJPROP_XSIZE, 70);
   ObjectSetInteger(0, g_prefix + "UnlockPopupOK", OBJPROP_YSIZE, 30);
   ObjectSetInteger(0, g_prefix + "UnlockPopupOK", OBJPROP_BGCOLOR, clrGreen);
   ObjectSetInteger(0, g_prefix + "UnlockPopupOK", OBJPROP_COLOR, clrWhite);
   ObjectSetInteger(0, g_prefix + "UnlockPopupOK", OBJPROP_FONTSIZE, 11);
   ObjectSetInteger(0, g_prefix + "UnlockPopupOK", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetString(0, g_prefix + "UnlockPopupOK", OBJPROP_TEXT, "Unlock");
   ObjectSetInteger(0, g_prefix + "UnlockPopupOK", OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, g_prefix + "UnlockPopupOK", OBJPROP_ZORDER, 201);

   ObjectCreate(0, g_prefix + "UnlockPopupCancel", OBJ_BUTTON, 0, 0, 0);
   ObjectSetInteger(0, g_prefix + "UnlockPopupCancel", OBJPROP_XDISTANCE, x + 100);
   ObjectSetInteger(0, g_prefix + "UnlockPopupCancel", OBJPROP_YDISTANCE, y + 110);
   ObjectSetInteger(0, g_prefix + "UnlockPopupCancel", OBJPROP_XSIZE, 70);
   ObjectSetInteger(0, g_prefix + "UnlockPopupCancel", OBJPROP_YSIZE, 30);
   ObjectSetInteger(0, g_prefix + "UnlockPopupCancel", OBJPROP_BGCOLOR, clrRed);
   ObjectSetInteger(0, g_prefix + "UnlockPopupCancel", OBJPROP_COLOR, clrWhite);
   ObjectSetInteger(0, g_prefix + "UnlockPopupCancel", OBJPROP_FONTSIZE, 11);
   ObjectSetInteger(0, g_prefix + "UnlockPopupCancel", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetString(0, g_prefix + "UnlockPopupCancel", OBJPROP_TEXT, "Cancel");
   ObjectSetInteger(0, g_prefix + "UnlockPopupCancel", OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, g_prefix + "UnlockPopupCancel", OBJPROP_ZORDER, 201);

   ChartRedraw();
}

void CloseUnlockPopup()
{
   g_popupActive = false;
   ObjectsDeleteAll(0, g_prefix + "UnlockPopup");
   ChartRedraw();
}

//+------------------------------------------------------------------+
//| GUI creation                                                     |
//+------------------------------------------------------------------+
void CreateShowButton()
{
   ObjectCreate(0, g_prefix + "ShowBtn", OBJ_BUTTON, 0, 0, 0);
   ObjectSetInteger(0, g_prefix + "ShowBtn", OBJPROP_XDISTANCE, g_x);
   ObjectSetInteger(0, g_prefix + "ShowBtn", OBJPROP_YDISTANCE, g_y);
   ObjectSetInteger(0, g_prefix + "ShowBtn", OBJPROP_XSIZE, 100);
   ObjectSetInteger(0, g_prefix + "ShowBtn", OBJPROP_YSIZE, 35);
   ObjectSetInteger(0, g_prefix + "ShowBtn", OBJPROP_BGCOLOR, clrDarkBlue);
   ObjectSetInteger(0, g_prefix + "ShowBtn", OBJPROP_COLOR, clrWhite);
   ObjectSetInteger(0, g_prefix + "ShowBtn", OBJPROP_FONTSIZE, 12);
   ObjectSetInteger(0, g_prefix + "ShowBtn", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetString(0, g_prefix + "ShowBtn", OBJPROP_TEXT, "▼ SHOW");
   ObjectSetInteger(0, g_prefix + "ShowBtn", OBJPROP_BORDER_COLOR, clrSilver);
   ObjectSetInteger(0, g_prefix + "ShowBtn", OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, g_prefix + "ShowBtn", OBJPROP_ZORDER, 100);
   ChartRedraw();
}

void CreateOrderButton()
{
   int x = g_x;
   int y = g_y + 45;
   ObjectCreate(0, g_prefix + "OrderBtn", OBJ_BUTTON, 0, 0, 0);
   ObjectSetInteger(0, g_prefix + "OrderBtn", OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, g_prefix + "OrderBtn", OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, g_prefix + "OrderBtn", OBJPROP_XSIZE, 100);
   ObjectSetInteger(0, g_prefix + "OrderBtn", OBJPROP_YSIZE, 40);
   ObjectSetInteger(0, g_prefix + "OrderBtn", OBJPROP_BGCOLOR, clrOrange);
   ObjectSetInteger(0, g_prefix + "OrderBtn", OBJPROP_COLOR, clrWhite);
   ObjectSetInteger(0, g_prefix + "OrderBtn", OBJPROP_FONTSIZE, 14);
   ObjectSetInteger(0, g_prefix + "OrderBtn", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetString(0, g_prefix + "OrderBtn", OBJPROP_TEXT, "📊 ORDER");
   ObjectSetInteger(0, g_prefix + "OrderBtn", OBJPROP_BORDER_COLOR, clrSilver);
   ObjectSetInteger(0, g_prefix + "OrderBtn", OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, g_prefix + "OrderBtn", OBJPROP_ZORDER, 100);
   ChartRedraw();
}

void CreateOrderWindow()
{
   int x = g_x - 10;
   int y = g_y - 10;
   int w = 410;          // enlarged width (was 360)
   int h = 580;          // enlarged height (was 480)

   ObjectCreate(0, g_prefix + "WinBG", OBJ_BUTTON, 0, 0, 0);
   ObjectSetInteger(0, g_prefix + "WinBG", OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, g_prefix + "WinBG", OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, g_prefix + "WinBG", OBJPROP_XSIZE, w);
   ObjectSetInteger(0, g_prefix + "WinBG", OBJPROP_YSIZE, h);
   ObjectSetInteger(0, g_prefix + "WinBG", OBJPROP_BGCOLOR, clrDarkSlateGray);
   ObjectSetInteger(0, g_prefix + "WinBG", OBJPROP_BORDER_COLOR, clrSilver);
   ObjectSetInteger(0, g_prefix + "WinBG", OBJPROP_BORDER_TYPE, BORDER_FLAT);
   ObjectSetInteger(0, g_prefix + "WinBG", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, g_prefix + "WinBG", OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, g_prefix + "WinBG", OBJPROP_ZORDER, 100);
   ObjectSetString(0, g_prefix + "WinBG", OBJPROP_TEXT, "");
   ObjectSetInteger(0, g_prefix + "WinBG", OBJPROP_STATE, false);

   int px = x + 15;
   int py = y + 20;

   CreateLabel("Title", px, py, "⚡ ORDER WINDOW", clrGold, 14);
   py += 35;
   CreateLabel("Inst", px, py, "Instrument: " + Symbol(), clrWhite, 11);
   py += 25;

   // ---------- Trade counter and Unlock button at top-right ----------
   int rightX = px + 260;  // shifted right to use the extra width
   CreateLabel("TradeCounterLabel", rightX, py - 20, "Trades: 0/20", clrCyan, 10);
   CreateButton("UnlockBtn", rightX, py - 5, 100, 22, "🔓 Unlock", clrDarkGoldenrod);

   // Continue with normal layout
   CreateLabel("DirLbl", px, py, "Direction:", clrWhite, 11);
   CreateButton("DirBuy", px + 80, py, 50, 25, "BUY", (g_direction == "BUY") ? clrLime : clrGray);
   CreateButton("DirSell", px + 135, py, 50, 25, "SELL", (g_direction == "SELL") ? clrLime : clrGray);
   py += 35;

   int digits = (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS);
   double ask = SymbolInfoDouble(Symbol(), SYMBOL_ASK);
   double bid = SymbolInfoDouble(Symbol(), SYMBOL_BID);
   g_currentPrice = (g_direction == "BUY") ? ask : bid;
   CreateLabel("PriceLabel", px, py, "Price: " + DoubleToString(g_currentPrice, digits) + " (" + g_direction + ")", clrYellow, 11);
   py += 30;

   CreateLabel("VolLbl", px, py, "📊 VOLUME:", clrCyan, 12);
   py += 28;
   ObjectCreate(0, g_prefix + "VolEdit", OBJ_EDIT, 0, 0, 0);
   ObjectSetInteger(0, g_prefix + "VolEdit", OBJPROP_XDISTANCE, px);
   ObjectSetInteger(0, g_prefix + "VolEdit", OBJPROP_YDISTANCE, py);
   ObjectSetInteger(0, g_prefix + "VolEdit", OBJPROP_XSIZE, 80);
   ObjectSetInteger(0, g_prefix + "VolEdit", OBJPROP_YSIZE, 25);
   ObjectSetInteger(0, g_prefix + "VolEdit", OBJPROP_BGCOLOR, clrBlack);
   ObjectSetInteger(0, g_prefix + "VolEdit", OBJPROP_COLOR, clrLime);
   ObjectSetInteger(0, g_prefix + "VolEdit", OBJPROP_FONTSIZE, 12);
   ObjectSetInteger(0, g_prefix + "VolEdit", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, g_prefix + "VolEdit", OBJPROP_ALIGN, ALIGN_CENTER);
   ObjectSetInteger(0, g_prefix + "VolEdit", OBJPROP_READONLY, false);
   ObjectSetString(0, g_prefix + "VolEdit", OBJPROP_TEXT, DoubleToString(g_lotSize, 2));
   ObjectSetInteger(0, g_prefix + "VolEdit", OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, g_prefix + "VolEdit", OBJPROP_ZORDER, 100);
   CreateButton("VolUp", px + 90, py, 30, 12, "▲", clrDarkGreen);
   CreateButton("VolDown", px + 90, py + 13, 30, 12, "▼", clrDarkRed);
   py += 40;

   // SL presets
   CreateLabel("SLLbl", px, py, "🛑 STOP LOSS (points):", clrOrange, 12);
   py += 28;
   CreateButton("SL50", px, py, 45, 28, "50", clrGray);
   CreateButton("SL100", px + 50, py, 45, 28, "100", clrYellow);
   CreateButton("SL200", px + 100, py, 45, 28, "200", clrGray);
   CreateButton("SL300", px + 150, py, 45, 28, "300", clrGray);
   py += 35;

   CreateLabel("SLPriceLbl", px, py, "Price:", clrWhite, 10);
   ObjectCreate(0, g_prefix + "SLEdit", OBJ_EDIT, 0, 0, 0);
   ObjectSetInteger(0, g_prefix + "SLEdit", OBJPROP_XDISTANCE, px + 60);
   ObjectSetInteger(0, g_prefix + "SLEdit", OBJPROP_YDISTANCE, py - 2);
   ObjectSetInteger(0, g_prefix + "SLEdit", OBJPROP_XSIZE, 100);
   ObjectSetInteger(0, g_prefix + "SLEdit", OBJPROP_YSIZE, 25);
   ObjectSetInteger(0, g_prefix + "SLEdit", OBJPROP_BGCOLOR, clrBlack);
   ObjectSetInteger(0, g_prefix + "SLEdit", OBJPROP_COLOR, clrYellow);
   ObjectSetInteger(0, g_prefix + "SLEdit", OBJPROP_FONTSIZE, 12);
   ObjectSetInteger(0, g_prefix + "SLEdit", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, g_prefix + "SLEdit", OBJPROP_ALIGN, ALIGN_CENTER);
   ObjectSetInteger(0, g_prefix + "SLEdit", OBJPROP_READONLY, false);
   ObjectSetString(0, g_prefix + "SLEdit", OBJPROP_TEXT, "");
   ObjectSetInteger(0, g_prefix + "SLEdit", OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, g_prefix + "SLEdit", OBJPROP_ZORDER, 100);
   CreateButton("SLUp", px + 170, py - 2, 25, 12, "▲", clrDarkGreen);
   CreateButton("SLDown", px + 170, py + 11, 25, 12, "▼", clrDarkRed);
   CreateButton("SLToggle", px + 200, py - 2, 70, 25, "🔴 SL Line", clrGray);
   py += 40;

   // TP presets
   CreateLabel("TPLbl", px, py, "🎯 TAKE PROFIT (points):", clrDodgerBlue, 12);
   py += 28;
   CreateButton("TP500", px, py, 55, 28, "500", clrGray);
   CreateButton("TP1000", px + 60, py, 55, 28, "1000", clrGray);
   CreateButton("TP1500", px + 120, py, 55, 28, "1500", clrGray);
   CreateButton("TP2000", px + 180, py, 55, 28, "2000", clrGray);
   CreateButton("TP3000", px + 240, py, 55, 28, "3000", clrGray);
   py += 35;

   CreateLabel("TPPriceLbl", px, py, "Price:", clrWhite, 10);
   ObjectCreate(0, g_prefix + "TPEdit", OBJ_EDIT, 0, 0, 0);
   ObjectSetInteger(0, g_prefix + "TPEdit", OBJPROP_XDISTANCE, px + 60);
   ObjectSetInteger(0, g_prefix + "TPEdit", OBJPROP_YDISTANCE, py - 2);
   ObjectSetInteger(0, g_prefix + "TPEdit", OBJPROP_XSIZE, 100);
   ObjectSetInteger(0, g_prefix + "TPEdit", OBJPROP_YSIZE, 25);
   ObjectSetInteger(0, g_prefix + "TPEdit", OBJPROP_BGCOLOR, clrBlack);
   ObjectSetInteger(0, g_prefix + "TPEdit", OBJPROP_COLOR, clrDodgerBlue);
   ObjectSetInteger(0, g_prefix + "TPEdit", OBJPROP_FONTSIZE, 12);
   ObjectSetInteger(0, g_prefix + "TPEdit", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, g_prefix + "TPEdit", OBJPROP_ALIGN, ALIGN_CENTER);
   ObjectSetInteger(0, g_prefix + "TPEdit", OBJPROP_READONLY, false);
   ObjectSetString(0, g_prefix + "TPEdit", OBJPROP_TEXT, "");
   ObjectSetInteger(0, g_prefix + "TPEdit", OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, g_prefix + "TPEdit", OBJPROP_ZORDER, 100);
   CreateButton("TPUp", px + 170, py - 2, 25, 12, "▲", clrDarkGreen);
   CreateButton("TPDown", px + 170, py + 11, 25, 12, "▼", clrDarkRed);
   py += 40;

   // Breakeven +10 button
   CreateButton("BreakevenBtn", px, py, 140, 30, "⚖️ BE +10 pts", clrDarkGoldenrod);
   py += 40;

   // Trade buttons
   CreateButton("BuyBtn", px, py, 140, 45, "BUY", clrBlue);
   CreateButton("SellBtn", px + 155, py, 140, 45, "SELL", clrRed);
   py += 55;
   CreateButton("CloseWin", px + 90, py, 120, 30, "✖ CLOSE", clrMaroon);

   // Set defaults
   SetSLPreset(100);
   SetTPPreset(0);
   ToggleSLLine();  // SL line ON by default
   UpdatePanelTradeCounter();

   ChartRedraw();
}

void CloseOrderWindow()
{
   EventKillTimer();
   g_arrowHeld = false;
   g_arrowName = "";
   if(g_slLineName != "" && ObjectFind(0, g_slLineName) >= 0)
      ObjectDelete(0, g_slLineName);
   g_slLineName = "";
   for(int i = ObjectsTotal(0) - 1; i >= 0; i--)
   {
      string name = ObjectName(0, i);
      if(name != g_prefix + "ShowBtn" && name != g_prefix + "OrderBtn" && StringFind(name, g_prefix) == 0)
         ObjectDelete(0, name);
   }
   ChartRedraw();
}

void HideAll()
{
   EventKillTimer();
   g_arrowHeld = false;
   for(int i = ObjectsTotal(0) - 1; i >= 0; i--)
   {
      string name = ObjectName(0, i);
      if(name != g_prefix + "ShowBtn")
         ObjectDelete(0, name);
   }
   g_showMain = false;
   g_orderWindow = false;
   ObjectSetString(0, g_prefix + "ShowBtn", OBJPROP_TEXT, "▼ SHOW");
   ChartRedraw();
}

void CreateLabel(string name, int x, int y, string text, color clr, int size)
{
   string fullName = g_prefix + name;
   ObjectCreate(0, fullName, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, fullName, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, fullName, OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, fullName, OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, fullName, OBJPROP_COLOR, clr);
   ObjectSetInteger(0, fullName, OBJPROP_FONTSIZE, size);
   ObjectSetString(0, fullName, OBJPROP_TEXT, text);
   ObjectSetInteger(0, fullName, OBJPROP_BACK, false);
   ObjectSetInteger(0, fullName, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, fullName, OBJPROP_ZORDER, 100);
}

void CreateButton(string name, int x, int y, int w, int h, string text, color bg)
{
   string fullName = g_prefix + name;
   ObjectCreate(0, fullName, OBJ_BUTTON, 0, 0, 0);
   ObjectSetInteger(0, fullName, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, fullName, OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, fullName, OBJPROP_XSIZE, w);
   ObjectSetInteger(0, fullName, OBJPROP_YSIZE, h);
   ObjectSetInteger(0, fullName, OBJPROP_BGCOLOR, bg);
   ObjectSetInteger(0, fullName, OBJPROP_COLOR, clrWhite);
   ObjectSetInteger(0, fullName, OBJPROP_FONTSIZE, 10);
   ObjectSetInteger(0, fullName, OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetString(0, fullName, OBJPROP_TEXT, text);
   ObjectSetInteger(0, fullName, OBJPROP_BORDER_COLOR, clrSilver);
   ObjectSetInteger(0, fullName, OBJPROP_BACK, false);
   ObjectSetInteger(0, fullName, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, fullName, OBJPROP_ZORDER, 100);
}

//+------------------------------------------------------------------+
//| Trade history file handling                                      |
//+------------------------------------------------------------------+
void LoadTradeHistory()
{
   int handle = FileOpen(TRADE_HISTORY_FILE, FILE_READ|FILE_BIN);
   if(handle == INVALID_HANDLE) return;
   FileSeek(handle, 0, SEEK_SET);
   g_tradeHistoryCount = 0;
   while(!FileIsEnding(handle))
   {
      TradeRecord rec;
      rec.time = (datetime)FileReadInteger(handle);   // read 4 bytes
      if(ArraySize(g_tradeHistory) <= g_tradeHistoryCount)
         ArrayResize(g_tradeHistory, g_tradeHistoryCount + 100);
      g_tradeHistory[g_tradeHistoryCount] = rec;
      g_tradeHistoryCount++;
   }
   FileClose(handle);
   ArrayResize(g_tradeHistory, g_tradeHistoryCount);
}

void SaveTradeHistory()
{
   int handle = FileOpen(TRADE_HISTORY_FILE, FILE_WRITE|FILE_BIN);
   if(handle == INVALID_HANDLE) return;
   for(int i = 0; i < g_tradeHistoryCount; i++)
      FileWriteInteger(handle, (int)g_tradeHistory[i].time);   // write 4 bytes
   FileClose(handle);
}

void RecordTrade()
{
   datetime now = TimeCurrent();
   if(g_tradeHistoryCount >= ArraySize(g_tradeHistory))
      ArrayResize(g_tradeHistory, g_tradeHistoryCount + 100);
   g_tradeHistory[g_tradeHistoryCount].time = now;
   g_tradeHistoryCount++;
   SaveTradeHistory();
   UpdateDailyTradeCount();
   UpdatePanelTradeCounter();
}

//+------------------------------------------------------------------+
//| Update daily trade count – count trades from today's midnight    |
//+------------------------------------------------------------------+
void UpdateDailyTradeCount()
{
   datetime now = TimeCurrent();
   MqlDateTime dt;
   TimeToStruct(now, dt);
   dt.hour = 0; dt.min = 0; dt.sec = 0;
   datetime todayStart = StructToTime(dt);

   g_dailyTradeCount = 0;
   for(int i = 0; i < g_tradeHistoryCount; i++)
   {
      if(g_tradeHistory[i].time >= todayStart)
         g_dailyTradeCount++;
   }
   g_dailyLimitReached = (g_dailyTradeCount >= MAX_DAILY_TRADES);
}

//+------------------------------------------------------------------+
//| Reset daily counter at midnight                                  |
//+------------------------------------------------------------------+
void ResetDailyCounter()
{
   // Reset extra trades allowance
   g_extraTradesAllowed = false;
   g_extraTradesRemaining = 0;

   // Update last reset date to today's start
   MqlDateTime dt;
   TimeCurrent(dt);
   dt.hour = 0; dt.min = 0; dt.sec = 0;
   g_lastResetDate = StructToTime(dt);

   // Recalculate trade count from today (should be 0 at midnight)
   UpdateDailyTradeCount();
   UpdatePanelTradeCounter();
}

//+------------------------------------------------------------------+
//| Update trade counter and unlock button                           |
//+------------------------------------------------------------------+
void UpdatePanelTradeCounter()
{
   string name = g_prefix + "TradeCounterLabel";
   if(ObjectFind(0, name) >= 0)
   {
      string text;
      if(g_extraTradesAllowed && g_extraTradesRemaining > 0)
      {
         text = "Trades: " + IntegerToString(g_dailyTradeCount) + "/" + IntegerToString(MAX_DAILY_TRADES) +
                " +" + IntegerToString(g_extraTradesRemaining) + " extra";
      }
      else if(g_extraTradesAllowed && g_extraTradesRemaining == 0)
      {
         text = "Trades: " + IntegerToString(g_dailyTradeCount) + "/" + IntegerToString(MAX_DAILY_TRADES) +
                " (extra used)";
      }
      else
      {
         text = "Trades: " + IntegerToString(g_dailyTradeCount) + "/" + IntegerToString(MAX_DAILY_TRADES);
      }
      if(g_dailyLimitReached && !g_extraTradesAllowed)
         text += " (LIMIT)";
      ObjectSetString(0, name, OBJPROP_TEXT, text);
      ObjectSetInteger(0, name, OBJPROP_COLOR, (g_dailyLimitReached && !g_extraTradesAllowed) ? clrRed : clrCyan);
      ChartRedraw();
   }

   string unlockBtn = g_prefix + "UnlockBtn";
   if(ObjectFind(0, unlockBtn) >= 0)
   {
      if(g_extraTradesAllowed)
      {
         ObjectSetString(0, unlockBtn, OBJPROP_TEXT, "✅ Unlocked");
         ObjectSetInteger(0, unlockBtn, OBJPROP_BGCOLOR, clrGray);
         ObjectSetInteger(0, unlockBtn, OBJPROP_STATE, false);
      }
      else
      {
         ObjectSetString(0, unlockBtn, OBJPROP_TEXT, "🔓 Unlock");
         ObjectSetInteger(0, unlockBtn, OBJPROP_BGCOLOR, clrDarkGoldenrod);
         ObjectSetInteger(0, unlockBtn, OBJPROP_STATE, true);
      }
   }
}

//+------------------------------------------------------------------+
//| Check daily limit                                                |
//+------------------------------------------------------------------+
bool CheckDailyLimit()
{
   UpdateDailyTradeCount();
   UpdatePanelTradeCounter();

   if(!g_dailyLimitReached)
      return true;

   if(g_extraTradesAllowed && g_extraTradesRemaining > 0)
      return true;

   if(g_extraTradesAllowed && g_extraTradesRemaining == 0)
   {
      ShowErrorPopup("Extra trades used up. Daily limit of " + IntegerToString(MAX_DAILY_TRADES) + " reached.");
   }
   else
   {
      ShowErrorPopup("Daily trade limit reached! Max " + IntegerToString(MAX_DAILY_TRADES) +
                     " trades per day.\nUse the Unlock button to enter code for 10 extra trades.");
   }
   return false;
}

//+------------------------------------------------------------------+
//| Breakeven +10 points (for active position)                       |
//+------------------------------------------------------------------+
void BreakevenPlus10()
{
   // Find open position for this symbol
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(PositionSelectByTicket(ticket))
      {
         if(PositionGetString(POSITION_SYMBOL) == Symbol())
         {
            double entry = PositionGetDouble(POSITION_PRICE_OPEN);
            ENUM_POSITION_TYPE type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);

            // Calculate breakeven +10 points
            int digits = (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS);
            double offset = 10 * g_point;
            double newSL;
            if(type == POSITION_TYPE_BUY)
               newSL = entry - offset;   // SL below entry (10 points buffer)
            else // SELL
               newSL = entry + offset;   // SL above entry (10 points buffer)

            newSL = NormalizeDouble(newSL, digits);

            // Keep existing TP
            double tp = PositionGetDouble(POSITION_TP);

            MqlTradeRequest req = {};
            MqlTradeResult res = {};
            req.action = TRADE_ACTION_SLTP;
            req.symbol = Symbol();
            req.position = ticket;
            req.sl = newSL;
            if(tp > 0) req.tp = NormalizeDouble(tp, digits);

            if(OrderSend(req, res))
            {
               if(res.retcode == TRADE_RETCODE_DONE)
               {
                  Print("Breakeven +10 set for position #", ticket, " SL=", DoubleToString(newSL, digits));
                  PlaySound("ok.wav");
               }
               else
               {
                  ShowErrorPopup("Breakeven failed: " + IntegerToString(res.retcode) + " - " + res.comment);
               }
            }
            else
            {
               ShowErrorPopup("Breakeven error: " + IntegerToString(GetLastError()));
            }
            return;
         }
      }
   }
   ShowErrorPopup("No open position found for " + Symbol());
}

//+------------------------------------------------------------------+
//| Execute trade                                                    |
//+------------------------------------------------------------------+
void ExecuteTrade(ENUM_ORDER_TYPE orderType)
{
   if(g_tradeInProgress) return;
   if(!CheckDailyLimit()) return;

   g_tradeInProgress = true;
   DisableTradeButtons(true);

   bool success = false;
   int digits = (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS);

   string volText = ObjectGetString(0, g_prefix + "VolEdit", OBJPROP_TEXT);
   if(StringLen(volText) > 0) g_lotSize = StringToDouble(volText);
   if(g_lotSize <= 0)
   {
      ShowErrorPopup("Volume must be > 0");
      g_tradeInProgress = false;
      DisableTradeButtons(false);
      return;
   }

   string slText = ObjectGetString(0, g_prefix + "SLEdit", OBJPROP_TEXT);
   if(StringLen(slText) > 0) g_slPrice = StringToDouble(slText);
   else g_slPrice = 0;

   string tpText = ObjectGetString(0, g_prefix + "TPEdit", OBJPROP_TEXT);
   if(StringLen(tpText) > 0) g_tpPrice = StringToDouble(tpText);
   else g_tpPrice = 0;

   // Check if already have a position on this symbol
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(PositionSelectByTicket(ticket))
      {
         if(PositionGetString(POSITION_SYMBOL) == Symbol())
         {
            ShowErrorPopup("Already have an open position on this symbol.");
            g_tradeInProgress = false;
            DisableTradeButtons(false);
            return;
         }
      }
   }

   double price = (orderType == ORDER_TYPE_BUY) ?
                  SymbolInfoDouble(Symbol(), SYMBOL_ASK) :
                  SymbolInfoDouble(Symbol(), SYMBOL_BID);

   MqlTradeRequest req = {};
   MqlTradeResult res = {};
   req.action = TRADE_ACTION_DEAL;
   req.symbol = Symbol();
   req.volume = g_lotSize;
   req.type = orderType;
   req.price = price;
   req.deviation = 20;
   req.magic = 123456;
   req.comment = "Scalper";

   if(g_slPrice > 0)
   {
      req.sl = g_slPrice;
      if((orderType == ORDER_TYPE_BUY && g_slPrice >= price) ||
         (orderType == ORDER_TYPE_SELL && g_slPrice <= price))
      {
         ShowErrorPopup("Invalid SL price.\nSL must be below (BUY) or above (SELL) entry.");
         g_tradeInProgress = false;
         DisableTradeButtons(false);
         return;
      }
   }

   if(g_tpPrice > 0)
   {
      req.tp = g_tpPrice;
      if((orderType == ORDER_TYPE_BUY && g_tpPrice <= price) ||
         (orderType == ORDER_TYPE_SELL && g_tpPrice >= price))
      {
         ShowErrorPopup("Invalid TP price.\nTP must be above (BUY) or below (SELL) entry.");
         g_tradeInProgress = false;
         DisableTradeButtons(false);
         return;
      }
   }

   if(req.sl > 0) req.sl = NormalizeDouble(req.sl, digits);
   if(req.tp > 0) req.tp = NormalizeDouble(req.tp, digits);

   if(OrderSend(req, res))
   {
      if(res.retcode == TRADE_RETCODE_DONE)
      {
         success = true;
         string dir = (orderType == ORDER_TYPE_BUY) ? "BUY" : "SELL";
         Print("✅ ", dir, " | Lot: ", DoubleToString(g_lotSize, 2),
               " | SL: ", DoubleToString(req.sl, digits),
               " | TP: ", (req.tp > 0 ? DoubleToString(req.tp, digits) : "none"));
         PlaySound("ok.wav");
         RecordTrade();
         if(g_extraTradesAllowed && g_extraTradesRemaining > 0)
         {
            g_extraTradesRemaining--;
            UpdatePanelTradeCounter();
         }
      }
      else
      {
         string errorMsg = "Order failed: " + IntegerToString(res.retcode) + " - " + res.comment;
         ShowErrorPopup(errorMsg);
      }
   }
   else
   {
      string errorMsg = "OrderSend error: " + IntegerToString(GetLastError());
      ShowErrorPopup(errorMsg);
   }

   g_tradeInProgress = false;
   DisableTradeButtons(false);

   if(success)
   {
      g_orderWindow = false;
      CloseOrderWindow();
   }
}
//+------------------------------------------------------------------+