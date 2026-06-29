using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Chart;

namespace VolaBubbles
{
    public class VolaBubbles : Indicator
    {
        private class TapeBubble
        {
            public long SpawnMs { get; set; }
            public double Price { get; set; }
            public double Delta { get; set; }
            public double Volume { get; set; }
        }

        private class HistoricalBubble
        {
            public DateTime TimeLeft { get; set; }
            public double Price { get; set; }
            public double Delta { get; set; }
            public double Volume { get; set; }
        }

        private class QuotePoint
        {
            public long SpawnMs { get; set; }
            public double Bid { get; set; }
            public double Ask { get; set; }
        }

        private class PriceLevelData
        {
            public double BuyVolume { get; set; }
            public double SellVolume { get; set; }
        }

        private struct DomLevel
        {
            public double Price;
            public double Size;
        }

        private class DomColumn
        {
            public long SpawnMs;
            public DomLevel[] Bids;
            public DomLevel[] Asks;
            public double MaxSize;
        }

        private class ProfileTrade
        {
            public long TimeMs;
            public double Price;
            public double Size;
        }

        // --- Paraméterek ---

        [InputParameter("Delta Threshold", 10)]
        public int DeltaThreshold = 2;

        [InputParameter("Price Step (0 = TickSize)", 11)]
        public double PriceStep = 0.25;

        [InputParameter("Tape Width (bars)", 20)]
        public int TapeWidthBars = 20;

        [InputParameter("Tape Speed (px/sec)", 21)]
        public int TapeSpeedPxPerSec = 20;

        [InputParameter("Bucket (ms)", 22)]
        public int BucketMs = 500;

        [InputParameter("Max Bubbles On Tape", 23)]
        public int MaxBubblesOnTape = 500;

        [InputParameter("Max Bubble Radius", 24)]
        public int MaxRadius = 50;

        [InputParameter("Show Historical Bubbles", 25)]
        public bool ShowHistoricalBubbles = false;

        [InputParameter("Max Historical Bubbles", 26)]
        public int MaxHistoricalBubbles = 300;

        [InputParameter("Hist. Bubble Backfill On Start", 27)]
        public bool HistoricalBubbleBackfill = true;

        [InputParameter("Hist. Bubble Lookback (min)", 28)]
        public int HistoricalBubbleLookbackMin = 60;

        [InputParameter("Historical Delta Threshold", 29)]
        public int HistoricalDeltaThreshold = 2;

        [InputParameter("Historical Positive Delta Color", 36)]
        public Color HistoricalPosColor = Color.FromArgb(120, Color.DarkGreen);

        [InputParameter("Historical Negative Delta Color", 37)]
        public Color HistoricalNegColor = Color.FromArgb(120, Color.DarkRed);

        [InputParameter("Show Volume Text", 30)]
        public bool ShowVolumeText = true;

        [InputParameter("Show Bid/Ask Lines", 31)]
        public bool ShowBidAskLines = true;

        [InputParameter("Positive Delta Color", 40)]
        public Color PosColor = Color.FromArgb(160, Color.Green);

        [InputParameter("Negative Delta Color", 41)]
        public Color NegColor = Color.FromArgb(160, Color.Red);

        [InputParameter("Volume Text Color", 42)]
        public Color VolumeTextColor = Color.White;

        [InputParameter("Bid Line Color", 43)]
        public Color BidLineColor = Color.FromArgb(230, Color.LimeGreen);

        [InputParameter("Ask Line Color", 44)]
        public Color AskLineColor = Color.FromArgb(230, Color.OrangeRed);

        [InputParameter("Show L2 Heatmap", 50)]
        public bool ShowHeatmap = false;

        [InputParameter("Heatmap Price Range (+/-)", 51)]
        public double HeatmapPriceRange = 10.0;

        [InputParameter("Heatmap Max Levels (cap)", 56)]
        public int HeatmapDepthLevels = 50;

        [InputParameter("Heatmap Sample (ms)", 52)]
        public int HeatmapSampleMs = 100;

        [InputParameter("Heatmap Decay (sec)", 53)]
        public int HeatmapDecaySec = 30;

        [InputParameter("Heatmap Gamma (0.1..2.0)", 54)]
        public double HeatmapGamma = 0.5;

        [InputParameter("Heatmap Min Alpha %", 55)]
        public int HeatmapMinAlphaPercent = 0;

        [InputParameter("Heatmap Opacity (0.0..1.0)", 57)]
        public double HeatmapOpacity = 0.7;

        [InputParameter("Show Open Order Indicator", 60)]
        public bool ShowOpenOrderIndicator = true;

        [InputParameter("Open Order Indicator Color", 61)]
        public Color OpenOrderIndicatorColor = Color.Red;

        [InputParameter("Open Order Blink Interval (ms)", 62)]
        public int OpenOrderBlinkIntervalMs = 1000;

        [InputParameter("Open Order Show Account Names", 63)]
        public bool OpenOrderShowAccountNames = true;

        [InputParameter("Show Volume Profile POC", 70)]
        public bool ShowVolumeProfilePoc = true;

        [InputParameter("VP Window (minutes)", 71)]
        public int VolumeProfileWindowMin = 30;

        [InputParameter("VP Rolling Window (false = Fixed)", 72)]
        public bool VolumeProfileRolling = true;

        [InputParameter("POC 1 Color", 73)]
        public Color Poc1Color = Color.Black;

        [InputParameter("POC 2 Color", 74)]
        public Color Poc2Color = Color.FromArgb(200, Color.Black);

        [InputParameter("POC 2 Min Distance (ticks)", 75)]
        public int Poc2MinDistanceTicks = 4;

        [InputParameter("Show POC Labels", 76)]
        public bool ShowPocLabels = true;

        [InputParameter("VP Backfill On Start", 77)]
        public bool VolumeProfileBackfill = true;

        [InputParameter("Show Market Profile", 78)]
        public bool ShowMarketProfile = false;

        [InputParameter("Market Profile Color", 79)]
        public Color MarketProfileColor = Color.FromArgb(140, 90, 120, 180);

        [InputParameter("Market Profile Opacity (0.0..1.0)", 80)]
        public double MarketProfileOpacity = 0.55;

        // Price Targets — kikapcsolva a UI-ból; PriceTargetsFeatureEnabled = true esetén aktiválható.
        private const bool PriceTargetsFeatureEnabled = false;

        private bool showPriceTargetsTicks;
        private Color priceTargetsLabelColor = Color.FromArgb(255, 128, 128, 128);
        private double positionRiskAmount = 100.0;
        private bool showPriceTargetsDirectionDebug;

        // --- Belső állapot ---

        private struct PriceTargetsInfo
        {
            public int SlTicks;
            public int TpTicks;
            public double EntryPrice;
            public bool IsValid;
        }

        private readonly object stateLock = new object();
        private readonly Dictionary<double, PriceLevelData> currentBucketVolumes = new Dictionary<double, PriceLevelData>();
        private readonly List<TapeBubble> tapeBubbles = new List<TapeBubble>();
        private readonly List<HistoricalBubble> historicalBubbles = new List<HistoricalBubble>();
        private readonly List<QuotePoint> quotePoints = new List<QuotePoint>();
        private readonly List<DomColumn> domColumns = new List<DomColumn>();
        private readonly Stopwatch clock = new Stopwatch();
        private long currentBucketStartMs;
        private long lastQuoteSampleMs;
        private long lastHeatmapSampleMs;
        private Timer tapeTimer;
        private const int QuoteSampleIntervalMs = 100;

        // Open Order indikátor állapota
        private volatile bool hasPendingLimitOrStopOrders;
        private volatile bool openOrderBlinkVisible = true;
        private volatile string[] accountsWithPendingOrders = Array.Empty<string>();
        private long lastOrderRefreshMs;
        private long lastBlinkToggleMs;
        private const int OrderRefreshIntervalMs = 500;

        // Volume Profile POC állapota
        private readonly List<ProfileTrade> profileTrades = new List<ProfileTrade>();
        private long profileWindowStartMs;
        private long lastPocComputeMs;
        private double pocPrice1;
        private double pocPrice2;
        private bool pocHasValue1;
        private bool pocHasValue2;
        private readonly Dictionary<double, double> profileVolumeByPrice = new Dictionary<double, double>();
        private double profileMaxVolume;
        private bool profileVolumeValid;
        private const int PocComputeIntervalMs = 250;

        private string lastPriceTargetsDebugSignature = string.Empty;

        private static readonly string[] DirectionDebugPropertyNames =
        {
            "ToolMode", "PriceTargetsMode", "BracketsMode",
            "Side", "PositionSide", "ToolSide", "Mode",
            "Direction", "ModeValue", "ShortMode", "IsLong", "IsShort",
            "TicksLeft", "TicksRight", "StopLoss", "TakeProfit", "OpenPrice", "EntryPrice"
        };

        private const BindingFlags DrawingMemberFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly string[] DirectionDebugNameKeywords =
        {
            "Side", "Mode", "Direction", "Short", "Long", "Position", "Tool", "Bracket",
            "Target", "Operand", "Range", "Trade", "Tick"
        };

        private bool IsVolumeProfileActive => ShowVolumeProfilePoc || ShowMarketProfile;

        // Backfill: egy CancellationTokenSource amit OnClear/újra-OnInit megsemmisít,
        // hogy a háttérben futó history lekérés ne zavarja a friss állapotot.
        private CancellationTokenSource backfillCts;

        public VolaBubbles() : base()
        {
            Name = "VolaBubbles Tape";
            SeparateWindow = false;
        }

        protected override void OnInit()
        {
            // Korábbi timer eldobása (ha a Quantower OnClear nélkül hív újra-init-et).
            var oldTimer = tapeTimer;
            tapeTimer = null;
            oldTimer?.Dispose();

            // Korábbi backfill task leállítása.
            var oldCts = backfillCts;
            backfillCts = null;
            try { oldCts?.Cancel(); } catch { }
            oldCts?.Dispose();

            clock.Restart();

            // FONTOS: az összes idő-mintavételi cursort a friss clock-hoz kell igazítani,
            // különben a clock.Restart() utáni "now (~0) - lastSample (régi nagy érték)"
            // mindig negatív lesz, és az AddQuotePoint / SampleDom soha nem ad új mintát.
            // long.MinValue / 2 biztosítja, hogy az első tick azonnal mintát vegyen.
            currentBucketStartMs = long.MinValue / 2;
            lastQuoteSampleMs = long.MinValue / 2;
            lastHeatmapSampleMs = long.MinValue / 2;
            lastOrderRefreshMs = long.MinValue / 2;
            lastBlinkToggleMs = long.MinValue / 2;

            openOrderBlinkVisible = true;
            hasPendingLimitOrStopOrders = false;
            accountsWithPendingOrders = Array.Empty<string>();

            profileWindowStartMs = long.MinValue / 2;
            lastPocComputeMs = long.MinValue / 2;
            pocPrice1 = 0;
            pocPrice2 = 0;
            pocHasValue1 = false;
            pocHasValue2 = false;
            profileVolumeByPrice.Clear();
            profileMaxVolume = 0;
            profileVolumeValid = false;

            lock (stateLock)
            {
                tapeBubbles.Clear();
                historicalBubbles.Clear();
                quotePoints.Clear();
                domColumns.Clear();
                currentBucketVolumes.Clear();
                profileTrades.Clear();
            }

            if (this.Symbol != null)
            {
                // Defensive: leiratkozás előbb, hogy ne duplázódjon az event handler,
                // ha az OnInit-et a Quantower OnClear nélkül hívja meg.
                this.Symbol.NewLast -= this.Symbol_NewLast;
                this.Symbol.NewLevel2 -= this.Symbol_NewLevel2;

                this.Symbol.NewLast += this.Symbol_NewLast;
                // L2: a feliratkozás kéri a vendor order book streamjét (heatmap-hez szükséges).
                this.Symbol.NewLevel2 += this.Symbol_NewLevel2;
            }

            lastHeatmapSampleMs = long.MinValue / 2;

            tapeTimer = new Timer(OnTapeTick, null, 0, 33);

            // Backfill az ablak hosszára (pl. 30 perc) háttérszálon, hogy ne blokkolja az OnInit-et.
            // A POC indulás után azonnal pontos lesz, nem kell megvárni, amíg élő tick-ekből felépül.
            StartBackgroundBackfill();
            if (PriceTargetsFeatureEnabled)
                SubscribeChartDrawings();

            UpdateShortName();
        }

        protected override void OnClear()
        {
            if (PriceTargetsFeatureEnabled)
                UnsubscribeChartDrawings();

            var t = tapeTimer;
            tapeTimer = null;
            t?.Dispose();

            var cts = backfillCts;
            backfillCts = null;
            try { cts?.Cancel(); } catch { }
            cts?.Dispose();

            if (this.Symbol != null)
            {
                this.Symbol.NewLast -= this.Symbol_NewLast;
                this.Symbol.NewLevel2 -= this.Symbol_NewLevel2;
            }

            clock.Stop();

            lock (stateLock)
            {
                tapeBubbles.Clear();
                historicalBubbles.Clear();
                quotePoints.Clear();
                domColumns.Clear();
                currentBucketVolumes.Clear();
                profileTrades.Clear();
            }
        }

        private void UpdateShortName()
        {
            string heatmap = ShowHeatmap ? "on" : "off";
            string openOrder = ShowOpenOrderIndicator ? "on" : "off";
            string hist = ShowHistoricalBubbles ? $"on/{MaxHistoricalBubbles}" : "off";
            string vp = IsVolumeProfileActive
                ? $"on/{VolumeProfileWindowMin}min/{(VolumeProfileRolling ? "roll" : "fix")}{(VolumeProfileBackfill ? "/bf" : "")}{(ShowMarketProfile ? "/mp" : "")}{(ShowVolumeProfilePoc ? "/poc" : "")}"
                : "off";
            this.ShortName = $"VolaBubbles Tape (Speed: {TapeSpeedPxPerSec}px/s, Width: {TapeWidthBars} bars, Bucket: {BucketMs}ms, Heatmap: {heatmap}, Hist: {hist}, OpenOrder: {openOrder}, VP: {vp})";
        }

        private double GetRoundedPrice(double price)
        {
            double step = GetActualStep();
            return Math.Round(price / step) * step;
        }

        private double GetActualStep()
        {
            if (this.Symbol == null) return PriceStep > 0 ? PriceStep : 0.25;
            if (PriceStep <= 0 || PriceStep < this.Symbol.TickSize)
                return this.Symbol.TickSize;
            return PriceStep;
        }

        private void Symbol_NewLevel2(Symbol symbol, Level2Quote level2, DOMQuote dom)
        {
            // A feliratkozás kéri az L2 streamet; friss DOM esetén azonnal mintavétel (throttle SampleDom-ban).
            if (!ShowHeatmap) return;

            long now = clock.ElapsedMilliseconds;
            lock (stateLock)
                SampleDom(now);
        }

        private void Symbol_NewLast(Symbol symbol, Last last)
        {
            double roundedPrice = GetRoundedPrice(last.Price);
            long now = clock.ElapsedMilliseconds;

            lock (stateLock)
            {
                // A volume profile minden trade-et regisztrál, aggressor flag-től függetlenül.
                // Ez ad a POC-nak teljes képet, akkor is, ha a vendor nem küld AggressorFlag-et.
                if (IsVolumeProfileActive && last.Size > 0)
                {
                    profileTrades.Add(new ProfileTrade
                    {
                        TimeMs = now,
                        Price = roundedPrice,
                        Size = last.Size
                    });
                }

                // A delta-alapú bubble logikához szükség van az aggressor flag-re.
                if (last.AggressorFlag == AggressorFlag.NotSet) return;

                if (!currentBucketVolumes.TryGetValue(roundedPrice, out var pld))
                {
                    pld = new PriceLevelData();
                    currentBucketVolumes[roundedPrice] = pld;
                }

                if (last.AggressorFlag == AggressorFlag.Buy)
                    pld.BuyVolume += last.Size;
                else
                    pld.SellVolume += last.Size;
            }
        }

        private void OnTapeTick(object state)
        {
            try
            {
                long now = clock.ElapsedMilliseconds;

                lock (stateLock)
                {
                    if (now - currentBucketStartMs >= BucketMs)
                        FlushBucket(now);

                    AddQuotePoint(now);
                    SampleDom(now);

                    // Idő-alapú lejárat: az elemek addig élnek, amíg jobbra ki nem futnak a tape sávból.
                    var chart = this.CurrentChart;
                    double barsWidth = chart?.BarsWidth ?? 0.0;
                    if (barsWidth > 0 && TapeSpeedPxPerSec > 0 && TapeWidthBars > 0)
                    {
                        double tapeDurationMs = (TapeWidthBars * barsWidth / TapeSpeedPxPerSec) * 1000.0;
                        tapeBubbles.RemoveAll(b => (now - b.SpawnMs) > tapeDurationMs);
                        quotePoints.RemoveAll(q => (now - q.SpawnMs) > tapeDurationMs);

                        // A heatmap oszlopokat addig tartjuk, ameddig vagy a tape sávban
                        // látszanak, vagy a futó max decay ablakon belül vannak.
                        double heatmapRetentionMs = Math.Max(tapeDurationMs, HeatmapDecaySec * 1000.0);
                        domColumns.RemoveAll(c => (now - c.SpawnMs) > heatmapRetentionMs);
                    }

                    if (tapeBubbles.Count > MaxBubblesOnTape)
                    {
                        int removeCount = tapeBubbles.Count - MaxBubblesOnTape;
                        tapeBubbles.RemoveRange(0, removeCount);
                    }

                    MaintainVolumeProfile(now);
                }

                UpdateOpenOrderState(now);

                try { this.CurrentChart?.RedrawBuffer(); }
                catch { /* chart not ready */ }
            }
            catch { /* swallow timer exceptions to keep the loop alive */ }
        }

        private void FlushBucket(long now)
        {
            if (currentBucketVolumes.Count == 0)
            {
                currentBucketStartMs = now;
                return;
            }

            DateTime chartTime = GetCurrentChartTime();
            foreach (var kvp in currentBucketVolumes)
            {
                double delta = kvp.Value.BuyVolume - kvp.Value.SellVolume;
                double volume = kvp.Value.BuyVolume + kvp.Value.SellVolume;
                if (Math.Abs(delta) >= DeltaThreshold)
                {
                    tapeBubbles.Add(new TapeBubble
                    {
                        SpawnMs = now,
                        Price = kvp.Key,
                        Delta = delta,
                        Volume = volume
                    });
                }

                if (ShowHistoricalBubbles)
                    AddHistoricalBubbleFromBucket(chartTime, kvp.Key, delta, volume);
            }

            if (ShowHistoricalBubbles)
                TrimHistoricalBubbles();

            currentBucketVolumes.Clear();
            currentBucketStartMs = now;
        }

        private static DateTime GetCurrentChartTime()
        {
            try { return Core.Instance?.TimeUtils?.DateTimeUtcNow ?? DateTime.UtcNow; }
            catch { return DateTime.UtcNow; }
        }

        private void TrimHistoricalBubbles()
        {
            int max = MaxHistoricalBubbles > 0 ? MaxHistoricalBubbles : 300;
            if (historicalBubbles.Count <= max) return;
            historicalBubbles.RemoveRange(0, historicalBubbles.Count - max);
        }

        private int GetHistoricalDeltaThreshold()
        {
            return HistoricalDeltaThreshold > 0 ? HistoricalDeltaThreshold : 1;
        }

        private void AddHistoricalBubbleFromBucket(DateTime chartTime, double price, double delta, double volume)
        {
            if (!ShowHistoricalBubbles) return;
            if (Math.Abs(delta) < GetHistoricalDeltaThreshold()) return;

            historicalBubbles.Add(new HistoricalBubble
            {
                TimeLeft = chartTime,
                Price = price,
                Delta = delta,
                Volume = volume
            });
        }

        private void AddQuotePoint(long now)
        {
            if (!ShowBidAskLines || this.Symbol == null) return;
            if (now - lastQuoteSampleMs < QuoteSampleIntervalMs) return;

            double bid = this.Symbol.Bid;
            double ask = this.Symbol.Ask;
            if (bid <= 0 && ask <= 0) return;

            quotePoints.Add(new QuotePoint
            {
                SpawnMs = now,
                Bid = bid,
                Ask = ask
            });

            lastQuoteSampleMs = now;
        }

        private void SampleDom(long now)
        {
            if (!ShowHeatmap || this.Symbol == null) return;
            if (HeatmapSampleMs > 0 && now - lastHeatmapSampleMs < HeatmapSampleMs) return;

            var dom = this.Symbol.DepthOfMarket;
            if (dom == null) return;

            try
            {
                double step = GetActualStep();
                int levelsCount = HeatmapDepthLevels;
                if (step > 0 && HeatmapPriceRange > 0)
                {
                    int needed = (int)Math.Ceiling(HeatmapPriceRange / step) + 2;
                    if (needed > levelsCount) levelsCount = needed;
                }
                if (levelsCount < 1) levelsCount = 1;
                if (levelsCount > 1000) levelsCount = 1000;

                var pars = new GetLevel2ItemsParameters
                {
                    AggregateMethod = AggregateMethod.ByPriceLVL,
                    LevelsCount = levelsCount,
                    CustomTickSize = step,
                    CalculateCumulative = false
                };

                var snapshot = dom.GetDepthOfMarketAggregatedCollections(pars);
                if (snapshot == null) return;

                var bids = ToDomLevels(snapshot.Bids);
                var asks = ToDomLevels(snapshot.Asks);

                if (bids.Length == 0 && asks.Length == 0) return;

                double maxSize = 0;
                for (int i = 0; i < bids.Length; i++)
                    if (bids[i].Size > maxSize) maxSize = bids[i].Size;
                for (int i = 0; i < asks.Length; i++)
                    if (asks[i].Size > maxSize) maxSize = asks[i].Size;

                domColumns.Add(new DomColumn
                {
                    SpawnMs = now,
                    Bids = bids,
                    Asks = asks,
                    MaxSize = maxSize
                });

                lastHeatmapSampleMs = now;
            }
            catch { /* vendor nem ad L2-t / pillanatnyi hiba */ }
        }

        private static DomLevel[] ToDomLevels(IEnumerable<Level2Item> src)
        {
            if (src == null) return Array.Empty<DomLevel>();
            var list = new List<DomLevel>();
            foreach (var item in src)
            {
                if (item == null) continue;
                if (item.Size <= 0) continue;
                list.Add(new DomLevel { Price = item.Price, Size = item.Size });
            }
            return list.ToArray();
        }

        public override void OnPaintChart(PaintChartEventArgs args)
        {
            var chart = this.CurrentChart;
            if (chart == null) return;

            // Chart overlay-k — history/tape nélkül is rajzolódnak.
            DrawOpenOrderIndicator(args);
            if (PriceTargetsFeatureEnabled)
                DrawPriceTargetsTickLabel(args);
            UpdateShortName();

            var mainWindow = chart.MainWindow;
            if (mainWindow == null) return;

            var converter = mainWindow.CoordinatesConverter;
            if (converter == null) return;

            if (this.HistoricalData == null || this.HistoricalData.Count == 0) return;

            double barsWidth = chart.BarsWidth;
            if (barsWidth <= 0) return;

            var lastBar = this.HistoricalData[0];
            float lastBarLeftX = (float)converter.GetChartX(lastBar.TimeLeft);
            float tapeLeftX = lastBarLeftX + (float)barsWidth;
            float tapeRightX = tapeLeftX + TapeWidthBars * (float)barsWidth;
            if (tapeRightX <= tapeLeftX) return;

            var clientRect = mainWindow.ClientRectangle;
            float top = clientRect.Top;
            float height = clientRect.Bottom - clientRect.Top;

            Graphics gr = args.Graphics;
            var state = gr.Save();
            try
            {
                gr.SmoothingMode = SmoothingMode.AntiAlias;

                DrawHistoricalBubbles(gr, converter, tapeLeftX, top, height);

                gr.SetClip(new RectangleF(tapeLeftX, top, tapeRightX - tapeLeftX, height));

                if (ShowHeatmap)
                {
                    long now = clock.ElapsedMilliseconds;
                    lock (stateLock)
                        SampleDom(now);
                    DrawHeatmap(gr, converter, tapeLeftX, tapeRightX);
                }

                DrawMarketProfile(gr, converter, tapeLeftX, tapeRightX);
                DrawVolumeProfilePoc(gr, converter, tapeLeftX, tapeRightX);

                DrawBidAskLines(gr, converter, tapeLeftX, tapeRightX);
                DrawBubbles(gr, converter, tapeLeftX, tapeRightX);
            }
            finally
            {
                gr.Restore(state);
            }

        }

        private void DrawBidAskLines(Graphics gr, IChartWindowCoordinatesConverter converter, float tapeLeftX, float tapeRightX)
        {
            if (!ShowBidAskLines) return;

            long now = clock.ElapsedMilliseconds;

            QuotePoint[] snapshot;
            lock (stateLock)
            {
                AddQuotePoint(now);
                snapshot = quotePoints.ToArray();
            }

            using (var bidPen = new Pen(BidLineColor, 2.5f))
            using (var askPen = new Pen(AskLineColor, 2.5f))
            {
                PointF? previousBid = null;
                PointF? previousAsk = null;
                PointF? lastBid = null;
                PointF? lastAsk = null;

                foreach (var quote in snapshot)
                {
                    double elapsedSec = (now - quote.SpawnMs) / 1000.0;
                    float x = tapeLeftX + (float)(elapsedSec * TapeSpeedPxPerSec);
                    if (x < tapeLeftX || x > tapeRightX) continue;

                    if (quote.Bid > 0)
                    {
                        var point = new PointF(x, (float)converter.GetChartY(quote.Bid));
                        if (previousBid.HasValue)
                            gr.DrawLine(bidPen, previousBid.Value, point);
                        previousBid = point;
                        lastBid = point;
                    }

                    if (quote.Ask > 0)
                    {
                        var point = new PointF(x, (float)converter.GetChartY(quote.Ask));
                        if (previousAsk.HasValue)
                            gr.DrawLine(askPen, previousAsk.Value, point);
                        previousAsk = point;
                        lastAsk = point;
                    }
                }

                DrawLiveQuoteFallback(gr, converter, tapeLeftX, tapeRightX, lastBid, lastAsk);
            }
        }

        private void DrawLiveQuoteFallback(
            Graphics gr,
            IChartWindowCoordinatesConverter converter,
            float tapeLeftX,
            float tapeRightX,
            PointF? lastBid,
            PointF? lastAsk)
        {
            if (this.Symbol == null) return;

            double bid = this.Symbol.Bid;
            double ask = this.Symbol.Ask;

            // A "farkat" halványan és vékonyabban húzzuk ki, hogy ne tűnjön ugyanolyan tömörnek,
            // mint a buborékokkal mozgó történeti trail.
            using (var bidFadedPen = new Pen(Fade(BidLineColor, 0.35f), 1f))
            using (var askFadedPen = new Pen(Fade(AskLineColor, 0.35f), 1f))
            {
                if (bid > 0)
                {
                    float y = (float)converter.GetChartY(bid);
                    float startX = lastBid?.X ?? tapeLeftX;
                    if (startX < tapeRightX)
                        gr.DrawLine(bidFadedPen, startX, y, tapeRightX, y);
                }

                if (ask > 0)
                {
                    float y = (float)converter.GetChartY(ask);
                    float startX = lastAsk?.X ?? tapeLeftX;
                    if (startX < tapeRightX)
                        gr.DrawLine(askFadedPen, startX, y, tapeRightX, y);
                }
            }
        }

        private static Color Fade(Color color, float factor)
        {
            int alpha = (int)Math.Round(color.A * factor);
            if (alpha < 0) alpha = 0;
            if (alpha > 255) alpha = 255;
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        // --- Heatmap segédek (hot paletta + futó max + rajzolás) ---

        private static readonly (double Stop, Color Color)[] HeatStops =
        {
            (0.00, Color.FromArgb(0, 0, 0, 0)),
            (0.10, Color.FromArgb(120, 20, 30, 120)),
            (0.45, Color.FromArgb(180, 110, 30, 150)),
            (0.75, Color.FromArgb(220, 220, 50, 30)),
            (1.00, Color.FromArgb(255, 255, 230, 60)),
        };

        private static Color LerpStops((double Stop, Color Color)[] stops, double t)
        {
            if (t <= stops[0].Stop) return stops[0].Color;
            if (t >= stops[stops.Length - 1].Stop) return stops[stops.Length - 1].Color;

            for (int i = 1; i < stops.Length; i++)
            {
                if (t <= stops[i].Stop)
                {
                    double range = stops[i].Stop - stops[i - 1].Stop;
                    double local = range > 0 ? (t - stops[i - 1].Stop) / range : 0;
                    return LerpColor(stops[i - 1].Color, stops[i].Color, local);
                }
            }
            return stops[stops.Length - 1].Color;
        }

        private static Color LerpColor(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            int A = (int)Math.Round(a.A + (b.A - a.A) * t);
            int R = (int)Math.Round(a.R + (b.R - a.R) * t);
            int G = (int)Math.Round(a.G + (b.G - a.G) * t);
            int B = (int)Math.Round(a.B + (b.B - a.B) * t);
            return Color.FromArgb(A, R, G, B);
        }

        private Color HeatColor(double size, double runningMax)
        {
            if (size <= 0 || runningMax <= 0) return Color.Transparent;

            double t = size / runningMax;
            if (t < 0) t = 0;
            if (t > 1) t = 1;

            double gamma = HeatmapGamma;
            if (gamma <= 0) gamma = 1;
            t = Math.Pow(t, gamma);

            double minT = HeatmapMinAlphaPercent / 100.0;
            if (minT > 0 && t < minT) return Color.Transparent;

            Color color = LerpStops(HeatStops, t);

            double opacity = HeatmapOpacity;
            if (opacity < 0) opacity = 0;
            if (opacity > 1) opacity = 1;
            if (opacity < 1.0)
            {
                int scaledAlpha = (int)Math.Round(color.A * opacity);
                if (scaledAlpha < 0) scaledAlpha = 0;
                if (scaledAlpha > 255) scaledAlpha = 255;
                color = Color.FromArgb(scaledAlpha, color.R, color.G, color.B);
            }

            return color;
        }

        private double ComputeRunningMaxSize(long now)
        {
            double max = 0;
            long windowMs = (long)HeatmapDecaySec * 1000L;
            if (windowMs <= 0) windowMs = 1000;

            for (int i = 0; i < domColumns.Count; i++)
            {
                var col = domColumns[i];
                if ((now - col.SpawnMs) > windowMs) continue;
                if (col.MaxSize > max) max = col.MaxSize;
            }

            return max > 0 ? max : 1.0;
        }

        private void DrawHeatmap(Graphics gr, IChartWindowCoordinatesConverter converter, float tapeLeftX, float tapeRightX)
        {
            long now = clock.ElapsedMilliseconds;

            DomColumn[] snapshot;
            double runningMax;
            lock (stateLock)
            {
                snapshot = domColumns.ToArray();
                runningMax = ComputeRunningMaxSize(now);
            }

            if (snapshot.Length == 0) return;

            double midPrice = GetCurrentMidPrice();
            if (midPrice <= 0) return;

            double step = GetActualStep();
            if (step <= 0) step = 0.25;

            double rangeAbsolute = HeatmapPriceRange > 0 ? HeatmapPriceRange : double.PositiveInfinity;

            // Az oszlopok aktuális X koordinátái előre, hogy a téglalap szélessége
            // a szomszédjához igazodhasson (hézagmentes folytonosság).
            // snapshot[i] indexelése: 0 = legrégebbi spawn (nagy X, jobb oldal),
            //                         N-1 = legújabb spawn (kicsi X, bal oldal).
            var xs = new float[snapshot.Length];
            for (int i = 0; i < snapshot.Length; i++)
            {
                double elapsedSec = (now - snapshot[i].SpawnMs) / 1000.0;
                xs[i] = tapeLeftX + (float)(elapsedSec * TapeSpeedPxPerSec);
            }

            float defaultWidth = (float)Math.Max(1.0, TapeSpeedPxPerSec * HeatmapSampleMs / 1000.0);
            float halfDefault = defaultWidth / 2f;

            var prevSmoothing = gr.SmoothingMode;
            gr.SmoothingMode = SmoothingMode.None;

            try
            {
                for (int i = 0; i < snapshot.Length; i++)
                {
                    float x = xs[i];
                    // jobb él: félúton a régebbi szomszéd felé (i-1, nagyobb X);
                    // ha nincs régebbi szomszéd, a default szélesség felét adjuk hozzá.
                    float xRight = (i > 0) ? (x + xs[i - 1]) / 2f : x + halfDefault;
                    // bal él: félúton a frissebb szomszéd felé (i+1, kisebb X);
                    // ha nincs frissebb szomszéd, a default szélesség felét vonjuk le.
                    float xLeft = (i < snapshot.Length - 1) ? (xs[i + 1] + x) / 2f : x - halfDefault;

                    if (xRight < tapeLeftX || xLeft > tapeRightX) continue;

                    float colW = xRight - xLeft;
                    if (colW < 1f) colW = 1f;

                    DrawHeatmapColumn(gr, converter, xLeft, colW, step, runningMax, midPrice, rangeAbsolute, snapshot[i].Bids);
                    DrawHeatmapColumn(gr, converter, xLeft, colW, step, runningMax, midPrice, rangeAbsolute, snapshot[i].Asks);
                }
            }
            finally
            {
                gr.SmoothingMode = prevSmoothing;
            }
        }

        private double GetCurrentMidPrice()
        {
            if (this.Symbol == null) return 0;

            double bid = this.Symbol.Bid;
            double ask = this.Symbol.Ask;

            if (bid > 0 && ask > 0) return (bid + ask) / 2.0;
            if (bid > 0) return bid;
            if (ask > 0) return ask;

            double last = this.Symbol.Last;
            return last > 0 ? last : 0;
        }

        private void DrawHeatmapColumn(
            Graphics gr,
            IChartWindowCoordinatesConverter converter,
            float xLeft,
            float width,
            double step,
            double runningMax,
            double midPrice,
            double rangeAbsolute,
            DomLevel[] levels)
        {
            if (levels == null) return;

            float halfStep = (float)(step / 2.0);

            for (int i = 0; i < levels.Length; i++)
            {
                var lvl = levels[i];
                if (lvl.Size <= 0) continue;

                if (Math.Abs(lvl.Price - midPrice) > rangeAbsolute) continue;

                Color color = HeatColor(lvl.Size, runningMax);
                if (color.A == 0) continue;

                float yTop = (float)converter.GetChartY(lvl.Price + halfStep);
                float yBottom = (float)converter.GetChartY(lvl.Price - halfStep);
                if (yBottom < yTop)
                {
                    float tmp = yTop; yTop = yBottom; yBottom = tmp;
                }

                float h = yBottom - yTop;
                if (h < 1f) h = 1f;

                using (var brush = new SolidBrush(color))
                    gr.FillRectangle(brush, xLeft, yTop, width, h);
            }
        }

        private void DrawHistoricalBubbles(
            Graphics gr,
            IChartWindowCoordinatesConverter converter,
            float chartRightX,
            float top,
            float height)
        {
            if (!ShowHistoricalBubbles) return;

            HistoricalBubble[] snapshot;
            lock (stateLock)
            {
                snapshot = historicalBubbles.ToArray();
            }

            if (snapshot.Length == 0) return;

            var clipState = gr.Save();
            try
            {
                gr.SetClip(new RectangleF(0, top, chartRightX, height));

                using (var volumeFont = new Font("Arial", 8f, FontStyle.Bold))
                using (var volumeBrush = new SolidBrush(VolumeTextColor))
                using (var outlinePen = new Pen(Color.FromArgb(220, Color.White), 1f))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    for (int i = 0; i < snapshot.Length; i++)
                    {
                        var bubble = snapshot[i];
                        float x = (float)converter.GetChartX(bubble.TimeLeft);
                        if (x < 0 || x > chartRightX) continue;

                        float y = (float)converter.GetChartY(bubble.Price);
                        DrawBubbleAt(gr, x, y, bubble.Delta, bubble.Volume, volumeFont, volumeBrush, outlinePen, sf, HistoricalPosColor, HistoricalNegColor);
                    }
                }
            }
            finally
            {
                gr.Restore(clipState);
            }
        }

        private void DrawBubbles(Graphics gr, IChartWindowCoordinatesConverter converter, float tapeLeftX, float tapeRightX)
        {
            long now = clock.ElapsedMilliseconds;

            TapeBubble[] snapshot;
            lock (stateLock)
            {
                snapshot = tapeBubbles.ToArray();
            }

            using (var volumeFont = new Font("Arial", 8f, FontStyle.Bold))
            using (var volumeBrush = new SolidBrush(VolumeTextColor))
            using (var outlinePen = new Pen(Color.FromArgb(220, Color.White), 1f))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                foreach (var bubble in snapshot)
                {
                    double elapsedSec = (now - bubble.SpawnMs) / 1000.0;
                    float x = tapeLeftX + (float)(elapsedSec * TapeSpeedPxPerSec);
                    if (x < tapeLeftX || x > tapeRightX) continue;

                    float y = (float)converter.GetChartY(bubble.Price);
                    DrawBubbleAt(gr, x, y, bubble.Delta, bubble.Volume, volumeFont, volumeBrush, outlinePen, sf);
                }
            }
        }

        private void DrawBubbleAt(
            Graphics gr,
            float x,
            float y,
            double delta,
            double volume,
            Font volumeFont,
            Brush volumeBrush,
            Pen outlinePen,
            StringFormat sf,
            Color? posColor = null,
            Color? negColor = null)
        {
            float radius = 8f + (float)Math.Abs(delta);
            if (radius > MaxRadius) radius = MaxRadius;

            Color bubbleColor = delta > 0
                ? (posColor ?? PosColor)
                : (negColor ?? NegColor);
            using (var b = new SolidBrush(bubbleColor))
                gr.FillEllipse(b, x - radius, y - radius, radius * 2, radius * 2);

            gr.DrawEllipse(outlinePen, x - radius, y - radius, radius * 2, radius * 2);

            if (ShowVolumeText && volume > 0)
            {
                string text = FormatVolume(volume);
                gr.DrawString(text, volumeFont, volumeBrush, x, y, sf);
            }
        }

        private static string FormatVolume(double v)
        {
            if (v >= 10000) return (v / 1000.0).ToString("0") + "K";
            if (v >= 1000) return (v / 1000.0).ToString("0.#") + "K";
            return ((long)Math.Round(v)).ToString();
        }

        // --- Price Targets (chart drawing → SL/TP ticks) ---

        private void SubscribeChartDrawings()
        {
            if (!PriceTargetsFeatureEnabled) return;
            if (!showPriceTargetsTicks && !showPriceTargetsDirectionDebug) return;

            var chart = this.CurrentChart;
            if (chart?.Drawings == null) return;

            chart.Drawings.Added += OnChartDrawingChanged;
            chart.Drawings.Moved += OnChartDrawingChanged;
            chart.Drawings.Removed += OnChartDrawingChanged;
        }

        private void UnsubscribeChartDrawings()
        {
            var chart = this.CurrentChart;
            if (chart?.Drawings == null) return;

            chart.Drawings.Added -= OnChartDrawingChanged;
            chart.Drawings.Moved -= OnChartDrawingChanged;
            chart.Drawings.Removed -= OnChartDrawingChanged;
        }

        private void OnChartDrawingChanged(DrawingEventArgs e)
        {
            try { this.CurrentChart?.RedrawBuffer(); }
            catch { }
        }

        private void DrawPriceTargetsTickLabel(PaintChartEventArgs args)
        {
            if (!PriceTargetsFeatureEnabled) return;
            if (!showPriceTargetsTicks && !showPriceTargetsDirectionDebug) return;

            if (!TryGetChartOverlayRectangle(out Rectangle overlayRect))
                overlayRect = args.Rectangle;

            var gr = args.Graphics;
            var prevSmoothing = gr.SmoothingMode;
            gr.SmoothingMode = SmoothingMode.AntiAlias;

            try
            {
                if (showPriceTargetsDirectionDebug)
                {
                    if (TryGetLatestPriceTargetsDrawing(out IDrawing debugDrawing))
                        DrawPriceTargetsDirectionDebugPanel(gr, overlayRect, debugDrawing);
                    else
                        DrawPriceTargetsDebugStatus(gr, overlayRect, "Debug ON — nincs PriceTargets rajz a charton");
                }

                if (!showPriceTargetsTicks) return;
                if (!TryGetLatestPriceTargetsInfo(out PriceTargetsInfo info)) return;

                string text = BuildPriceTargetsLabelText(info);
                using (var font = new Font("Segoe UI", 10f, FontStyle.Bold))
                using (var brush = new SolidBrush(priceTargetsLabelColor))
                using (var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Far
                })
                {
                    float y = overlayRect.Bottom - 8f;
                    if (showPriceTargetsDirectionDebug)
                        y -= 140f;
                    gr.DrawString(text, font, brush, overlayRect.Left + overlayRect.Width / 2f, y, format);
                }
            }
            finally
            {
                gr.SmoothingMode = prevSmoothing;
            }
        }

        private bool TryGetChartOverlayRectangle(out Rectangle rect)
        {
            rect = default;
            var mainWindow = this.CurrentChart?.MainWindow;
            if (mainWindow == null) return false;
            rect = mainWindow.ClientRectangle;
            return rect.Width > 0 && rect.Height > 0;
        }

        private static void DrawPriceTargetsDebugStatus(Graphics gr, Rectangle overlayRect, string message)
        {
            using (var font = new Font("Consolas", 9f, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.FromArgb(255, 255, 200, 80)))
            using (var bgBrush = new SolidBrush(Color.FromArgb(200, 30, 30, 30)))
            {
                var size = gr.MeasureString(message, font);
                float w = size.Width + 12f;
                float h = size.Height + 8f;
                float x = overlayRect.Left + 6f;
                float y = overlayRect.Bottom - h - 6f;
                gr.FillRectangle(bgBrush, x, y, w, h);
                gr.DrawString(message, font, brush, x + 4f, y + 2f);
            }
        }

        private void DrawPriceTargetsDirectionDebugPanel(Graphics gr, Rectangle overlayRect, IDrawing drawing)
        {
            double tickSize = GetChartTickSize();
            var allLines = CollectPriceTargetsDirectionDebugLines(drawing, tickSize);
            if (allLines.Count == 0)
            {
                DrawPriceTargetsDebugStatus(gr, overlayRect, "Debug ON — üres reflection dump");
                return;
            }

            string signature = string.Join("\n", allLines);
            if (!string.Equals(signature, lastPriceTargetsDebugSignature, StringComparison.Ordinal))
            {
                lastPriceTargetsDebugSignature = signature;
                TryWritePriceTargetsDebugLog(signature);
            }

            var lines = new List<string>();
            const int maxChartLines = 24;
            for (int i = 0; i < allLines.Count && lines.Count < maxChartLines; i++)
                lines.Add(allLines[i]);
            if (allLines.Count > maxChartLines)
                lines.Add($"... +{allLines.Count - maxChartLines} sor a log fájlban");

            using (var font = new Font("Consolas", 8f, FontStyle.Regular))
            using (var brush = new SolidBrush(Color.FromArgb(230, 255, 220, 120)))
            using (var bgBrush = new SolidBrush(Color.FromArgb(200, 20, 20, 20)))
            {
                float lineHeight = font.GetHeight(gr) + 1f;
                float maxWidth = 0f;
                foreach (string line in lines)
                {
                    float w = gr.MeasureString(line, font).Width;
                    if (w > maxWidth) maxWidth = w;
                }

                float panelHeight = lines.Count * lineHeight + 6f;
                float panelWidth = Math.Min(maxWidth + 10f, overlayRect.Width - 12f);
                float x = overlayRect.Left + 6f;
                float y = overlayRect.Bottom - panelHeight - 6f;
                gr.FillRectangle(bgBrush, x, y, panelWidth, panelHeight);

                float textY = y + 3f;
                foreach (string line in lines)
                {
                    gr.DrawString(line, font, brush, x + 4f, textY);
                    textY += lineHeight;
                }
            }
        }

        private static void TryWritePriceTargetsDebugLog(string content)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Quantower", "VolaBubbles");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "price-targets-direction-debug.txt");
                var sb = new StringBuilder();
                sb.AppendLine($"--- {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ---");
                sb.AppendLine(content);
                sb.AppendLine();
                File.WriteAllText(path, sb.ToString());
            }
            catch { }
        }

        private List<string> CollectPriceTargetsDirectionDebugLines(IDrawing drawing, double tickSize)
        {
            var lines = new List<string>();
            if (drawing == null) return lines;

            lines.Add("=== UI 'Tool mode' => ToolMode / PriceTargetsMode ===");
            foreach (string key in new[] { "ToolMode", "PriceTargetsMode", "BracketsMode" })
            {
                if (TryFindDrawingMemberAny(drawing, key, out object value, out string path, 0))
                    lines.Add($"[TOOL MODE?] {path} = {FormatDirectionDebugValue(value)}");
                else
                    lines.Add($"[TOOL MODE?] {key} => (not found)");
            }

            lines.Add($"Type: {drawing.GetType().Name}");
            lines.Add($"FullName: {drawing.GetType().FullName}");

            for (int i = 0; i < 16; i++)
            {
                try
                {
                    var point = drawing.GetPoint(i);
                    lines.Add($"GetPoint({i}): {point.Item1:yyyy-MM-dd HH:mm:ss} @ {point.Item2:0.#####}");
                }
                catch { break; }
            }

            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            WalkObjectForDirectionDebug(drawing, "root", 0, visited, lines);

            foreach (string propName in DirectionDebugPropertyNames)
            {
                if (TryFindDrawingMemberAny(drawing, propName, out object value, out string path, 0))
                    lines.Add($"[FIND] {path} = {FormatDirectionDebugValue(value)}");
            }

            AppendDirectionResolutionDebug(drawing, tickSize, lines);
            return lines;
        }

        private static void WalkObjectForDirectionDebug(
            object obj,
            string path,
            int depth,
            HashSet<object> visited,
            List<string> lines)
        {
            if (obj == null || depth > 4) return;
            if (!visited.Add(obj)) return;

            const BindingFlags flags = DrawingMemberFlags;

            foreach (var member in GetInspectableMembers(obj.GetType(), flags))
            {
                object raw;
                try { raw = member.GetValue(obj); }
                catch { continue; }

                string memberPath = string.IsNullOrEmpty(path) ? member.Name : $"{path}.{member.Name}";
                bool nameRelevant = IsDirectionRelevantMemberName(member.Name);
                bool valueRelevant = IsDirectionRelevantLeafValue(raw);

                if ((nameRelevant || valueRelevant) && raw != null && !ShouldRecurseIntoProperty(member.MemberType))
                    lines.Add($"{memberPath} = {FormatDirectionDebugValue(raw)} [{member.MemberType.Name}]");

                if (ShouldSkipDrawingReflectionBranch(member.MemberType))
                    continue;

                if (depth < 4 && raw != null && ShouldRecurseIntoProperty(member.MemberType))
                    WalkObjectForDirectionDebug(raw, memberPath, depth + 1, visited, lines);
            }
        }

        private readonly struct InspectableMember
        {
            public string Name { get; }
            public Type MemberType { get; }
            private readonly PropertyInfo prop;
            private readonly FieldInfo field;

            public InspectableMember(PropertyInfo p)
            {
                Name = p.Name;
                MemberType = p.PropertyType;
                prop = p;
                field = null;
            }

            public InspectableMember(FieldInfo f)
            {
                Name = f.Name;
                MemberType = f.FieldType;
                prop = null;
                field = f;
            }

            public object GetValue(object target)
            {
                if (prop != null) return prop.GetValue(target);
                return field.GetValue(target);
            }
        }

        private static IEnumerable<InspectableMember> GetInspectableMembers(Type type, BindingFlags flags)
        {
            foreach (var prop in type.GetProperties(flags))
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                yield return new InspectableMember(prop);
            }

            foreach (var field in type.GetFields(flags))
                yield return new InspectableMember(field);
        }

        private static bool IsDirectionRelevantMemberName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < DirectionDebugNameKeywords.Length; i++)
            {
                if (name.IndexOf(DirectionDebugNameKeywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static bool IsDirectionRelevantLeafValue(object raw)
        {
            if (raw == null) return false;
            if (raw is bool || raw is Enum) return true;

            if (raw is string s)
            {
                return s.IndexOf("Long", StringComparison.OrdinalIgnoreCase) >= 0
                    || s.IndexOf("Short", StringComparison.OrdinalIgnoreCase) >= 0
                    || s.IndexOf("Buy", StringComparison.OrdinalIgnoreCase) >= 0
                    || s.IndexOf("Sell", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return false;
        }

        private static string FormatDirectionDebugValue(object raw)
        {
            if (raw == null) return "null";
            if (raw is Enum e)
            {
                try { return $"{e} ({Convert.ToInt32(e)})"; }
                catch { return e.ToString(); }
            }
            if (raw is double d) return d.ToString("0.#####");
            if (raw is float f) return f.ToString("0.#####");
            if (raw is decimal m) return m.ToString("0.#####");
            return raw.ToString();
        }

        private static bool TryFindDrawingMemberAny(
            object root,
            string name,
            out object value,
            out string path,
            int depth)
        {
            value = null;
            path = null;
            if (root == null || depth > 4) return false;

            const BindingFlags flags = DrawingMemberFlags;
            foreach (var member in GetInspectableMembers(root.GetType(), flags))
            {
                object raw;
                try { raw = member.GetValue(root); }
                catch { continue; }

                string memberPath = member.Name;
                if (member.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && raw != null)
                {
                    value = raw;
                    path = memberPath;
                    return true;
                }

                if (ShouldSkipDrawingReflectionBranch(member.MemberType))
                    continue;

                if (depth < 4 && raw != null && ShouldRecurseIntoProperty(member.MemberType))
                {
                    if (TryFindDrawingMemberAny(raw, name, out value, out string nestedPath, depth + 1))
                    {
                        path = memberPath + "." + nestedPath;
                        return true;
                    }
                }
            }

            return false;
        }

        private static void AppendDirectionResolutionDebug(IDrawing drawing, double tickSize, List<string> lines)
        {
            TryFindDrawingNumericDeep(drawing, "OpenPrice", out double openPrice);
            if (openPrice <= 0)
                TryFindDrawingNumericDeep(drawing, "EntryPrice", out openPrice);
            TryFindDrawingNumericDeep(drawing, "StopLoss", out double stopLoss);
            TryFindDrawingNumericDeep(drawing, "TakeProfit", out double takeProfit);
            TryFindDrawingNumericDeep(drawing, "TicksLeft", out double ticksLeft);
            TryFindDrawingNumericDeep(drawing, "TicksRight", out double ticksRight);

            lines.Add($"--- Prices: Open={openPrice:0.#####} SL={stopLoss:0.#####} TP={takeProfit:0.#####} ---");
            lines.Add($"--- Ticks: Left={ticksLeft:0.##} Right={ticksRight:0.##} ---");

            if (TryFindDrawingToolModeIsShort(drawing, out bool toolModeShort))
                lines.Add($"TryFindDrawingToolModeIsShort (ToolMode/PriceTargetsMode) => {(toolModeShort ? "SHORT" : "LONG")}");
            else
                lines.Add("TryFindDrawingToolModeIsShort => (not found)");

            foreach (string modeName in new[] { "ToolMode", "PriceTargetsMode", "BracketsMode", "Mode", "Direction", "ModeValue", "ToolSide", "PositionSide" })
            {
                if (TryFindDrawingModeIsShort(drawing, modeName, out bool modeShort))
                    lines.Add($"TryFindDrawingModeIsShort({modeName}) => {(modeShort ? "SHORT" : "LONG")}");
                else
                    lines.Add($"TryFindDrawingModeIsShort({modeName}) => (not found)");
            }

            if (TryFindDrawingSideIsShort(drawing, out bool sideShort))
                lines.Add($"TryFindDrawingSideIsShort => {(sideShort ? "SHORT" : "LONG")}");
            else
                lines.Add("TryFindDrawingSideIsShort => (not found)");

            if (TryFindDrawingBoolDeep(drawing, "ShortMode", out bool shortMode))
                lines.Add($"ShortMode => {shortMode}");
            else
                lines.Add("ShortMode => (not found)");

            double entry = openPrice;
            EnsureEntryPrice(drawing, tickSize, stopLoss, takeProfit, ref entry);
            ResolvePriceTargetsIsShort(drawing, entry, stopLoss, takeProfit, tickSize, out bool resolvedShort);
            lines.Add($"EnsureEntryPrice => {entry:0.#####}");
            lines.Add($"=> FINAL ResolvedIsShort: {(resolvedShort ? "SHORT" : "LONG")}");

            if (TryGetNativePriceTargetTicks(drawing, out int slTicks, out int tpTicks, out double nativeEntry))
                lines.Add($"NativeTicks: SL={slTicks} TP={tpTicks} Entry={nativeEntry:0.#####}");
            else
                lines.Add("NativeTicks: (not available)");
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private string BuildPriceTargetsLabelText(PriceTargetsInfo info)
        {
            var parts = new List<string>
            {
                $"SL: {info.SlTicks} ticks",
                $"TP: {info.TpTicks} ticks"
            };

            if (info.SlTicks > 0 && info.TpTicks > 0)
            {
                // Reward/Risk = TP tick távolság / SL tick távolság
                double rr = info.TpTicks / (double)info.SlTicks;
                parts.Add($"RR (TP/SL): {rr:0.##}");
            }

            if (positionRiskAmount > 0 && info.SlTicks > 0 &&
                TryCalculateMaxPositionSize(info, out double contracts))
            {
                double lotStep = this.Symbol?.LotStep > 0 ? this.Symbol.LotStep : 1.0;
                parts.Add($"Size: {FormatContractSize(contracts, lotStep)}");
            }

            return string.Join("  |  ", parts);
        }

        private bool TryCalculateMaxPositionSize(PriceTargetsInfo info, out double contracts)
        {
            contracts = 0;
            var symbol = this.Symbol;
            if (symbol == null || info.SlTicks <= 0) return false;

            double entryPrice = info.EntryPrice > 0 ? info.EntryPrice : GetCurrentMidPrice();
            if (entryPrice <= 0) return false;

            double tickCost;
            try { tickCost = symbol.GetTickCost(entryPrice); }
            catch { return false; }

            if (tickCost <= 0) return false;

            double riskPerContract = info.SlTicks * tickCost;
            if (riskPerContract <= 0) return false;

            double raw = positionRiskAmount / riskPerContract;
            double lotStep = symbol.LotStep > 0 ? symbol.LotStep : 1.0;
            contracts = Math.Floor(raw / lotStep) * lotStep;

            if (symbol.MaxLot > 0 && contracts > symbol.MaxLot)
                contracts = Math.Floor(symbol.MaxLot / lotStep) * lotStep;

            if (contracts <= 0) return false;
            if (symbol.MinLot > 0 && contracts < symbol.MinLot)
                return false;

            return true;
        }

        private static string FormatContractSize(double contracts, double lotStep)
        {
            if (contracts <= 0) return "0";

            if (lotStep >= 1)
                return ((long)Math.Round(contracts)).ToString();

            int decimals = 0;
            double step = lotStep;
            while (step < 1 && decimals < 4)
            {
                step *= 10;
                decimals++;
            }

            return contracts.ToString("0." + new string('#', Math.Max(1, decimals)));
        }

        private bool TryGetLatestPriceTargetsInfo(out PriceTargetsInfo info)
        {
            info = default;

            if (!TryGetLatestPriceTargetsDrawing(out IDrawing latest))
                return false;

            double tickSize = GetChartTickSize();
            if (tickSize <= 0) return false;

            if (!TryGetPriceTargetsInfo(latest, tickSize, out info))
                return false;

            info.IsValid = info.SlTicks > 0 || info.TpTicks > 0;
            return info.IsValid;
        }

        private bool TryGetLatestPriceTargetsDrawing(out IDrawing drawing)
        {
            drawing = null;

            var chart = this.CurrentChart;
            if (chart?.Drawings == null || this.Symbol == null) return false;

            List<IDrawing> drawings;
            try { drawings = chart.Drawings.GetAll(this.Symbol); }
            catch { return false; }

            if (drawings == null || drawings.Count == 0) return false;

            DateTime latestAnchor = DateTime.MinValue;
            IDrawing fallback = null;

            for (int i = 0; i < drawings.Count; i++)
            {
                var candidate = drawings[i];
                if (candidate == null || candidate.Type != DrawingType.PriceTargets) continue;

                fallback = candidate;
                if (!TryGetDrawingAnchorTime(candidate, out DateTime anchor)) continue;
                if (anchor > latestAnchor)
                {
                    latestAnchor = anchor;
                    drawing = candidate;
                }
            }

            if (drawing == null)
                drawing = fallback;

            return drawing != null;
        }

        private static bool TryGetDrawingAnchorTime(IDrawing drawing, out DateTime anchor)
        {
            anchor = DateTime.MinValue;
            for (int i = 0; i < 16; i++)
            {
                try
                {
                    var point = drawing.GetPoint(i);
                    if (point.Item1 > anchor)
                        anchor = point.Item1;
                }
                catch
                {
                    break;
                }
            }

            return anchor > DateTime.MinValue;
        }

        private bool TryGetPriceTargetsInfo(IDrawing drawing, double tickSize, out PriceTargetsInfo info)
        {
            info = default;

            if (TryGetNativePriceTargetTicks(drawing, out int slTicks, out int tpTicks, out double entryFromNative))
            {
                info.SlTicks = slTicks;
                info.TpTicks = tpTicks;
                info.EntryPrice = entryFromNative;
                return slTicks > 0 || tpTicks > 0;
            }

            if (!TryResolvePriceTargetsLevels(drawing, tickSize, out double entry, out double slPrice, out double tpPrice))
                return false;

            info.EntryPrice = entry;
            info.SlTicks = PriceDistanceToTicks(entry, slPrice, tickSize);
            info.TpTicks = PriceDistanceToTicks(entry, tpPrice, tickSize);
            return info.SlTicks > 0 || info.TpTicks > 0;
        }

        // Quantower Price Targets: TicksLeft/TicksRight — csak a rajz modelljén (ne HistoryItem.TicksLeft!).
        private static bool TryGetNativePriceTargetTicks(
            IDrawing drawing,
            out int slTicks,
            out int tpTicks,
            out double entryPrice)
        {
            slTicks = 0;
            tpTicks = 0;
            entryPrice = 0;

            bool hasLeft = TryFindDrawingNumericDeep(drawing, "TicksLeft", out double left);
            bool hasRight = TryFindDrawingNumericDeep(drawing, "TicksRight", out double right);
            if (!hasLeft && !hasRight)
                return false;

            TryFindDrawingNumericDeep(drawing, "OpenPrice", out entryPrice);
            if (entryPrice <= 0)
                TryFindDrawingNumericDeep(drawing, "EntryPrice", out entryPrice);

            int leftTicks = hasLeft ? (int)Math.Round(left) : 0;
            int rightTicks = hasRight ? (int)Math.Round(right) : 0;

            if (TryFindDrawingToolModeIsShort(drawing, out bool isShort))
            {
                // Tool mode short: a zónák vizuálisan tükröződnek — SL/TP tick hozzárendelés csere.
                slTicks = isShort ? rightTicks : leftTicks;
                tpTicks = isShort ? leftTicks : rightTicks;
            }
            else
            {
                slTicks = leftTicks;
                tpTicks = rightTicks;
            }

            return slTicks > 0 && tpTicks > 0;
        }

        private int PriceDistanceToTicks(double price1, double price2, double tickSize)
        {
            if (this.Symbol != null)
            {
                try
                {
                    double ticks = this.Symbol.CalculateTicks(price1, price2);
                    return Math.Max(1, (int)Math.Round(Math.Abs(ticks), MidpointRounding.AwayFromZero));
                }
                catch { /* fallback */ }
            }

            if (tickSize <= 0) return 0;
            return Math.Max(1, (int)Math.Round(Math.Abs(price1 - price2) / tickSize, MidpointRounding.AwayFromZero));
        }

        private static bool TryResolvePriceTargetsLevels(
            IDrawing drawing,
            double tickSize,
            out double entry,
            out double slPrice,
            out double tpPrice)
        {
            entry = 0;
            slPrice = 0;
            tpPrice = 0;

            TryFindDrawingNumericDeep(drawing, "OpenPrice", out entry);
            if (entry <= 0)
                TryFindDrawingNumericDeep(drawing, "EntryPrice", out entry);

            bool hasSl = TryFindDrawingNumericDeep(drawing, "StopLoss", out slPrice);
            bool hasTp = TryFindDrawingNumericDeep(drawing, "TakeProfit", out tpPrice);

            if (hasSl && hasTp)
            {
                EnsureEntryPrice(drawing, tickSize, slPrice, tpPrice, ref entry);
                return entry > 0;
            }

            if (!TryCollectDistinctPointPrices(drawing, tickSize, out var prices) || prices.Count < 2)
                return false;

            prices.Sort();
            double low = prices[0];
            double high = prices[prices.Count - 1];
            double mid = prices.Count >= 3 ? prices[1] : (low + high) / 2.0;

            if (entry <= 0 || entry < low - tickSize * 0.5 || entry > high + tickSize * 0.5)
                entry = mid;

            if (hasSl && hasTp)
                return entry > 0;

            ResolvePriceTargetsIsShort(drawing, entry, slPrice, tpPrice, tickSize, out bool isShort);

            if (isShort)
            {
                slPrice = high;
                tpPrice = low;
            }
            else
            {
                slPrice = low;
                tpPrice = high;
            }

            return entry > 0;
        }

        private static void EnsureEntryPrice(
            IDrawing drawing,
            double tickSize,
            double slPrice,
            double tpPrice,
            ref double entry)
        {
            if (entry > 0) return;

            if (TryCollectDistinctPointPrices(drawing, tickSize, out var prices) && prices.Count >= 3)
            {
                prices.Sort();
                entry = prices[1];
                return;
            }

            if (slPrice > 0 && tpPrice > 0 && Math.Abs(slPrice - tpPrice) > tickSize * 0.5)
                entry = (slPrice + tpPrice) / 2.0;
        }

        // UI "Tool mode" (LONG/SHORT) → reflection: ToolMode / PriceTargetsMode (Plugins DLL).
        private static void ResolvePriceTargetsIsShort(
            IDrawing drawing,
            double entry,
            double knownSlPrice,
            double knownTpPrice,
            double tickSize,
            out bool isShort)
        {
            isShort = false;

            if (TryFindDrawingToolModeIsShort(drawing, out isShort))
                return;

            foreach (string modeName in new[] { "Mode", "Direction", "ModeValue", "ToolSide", "PositionSide" })
            {
                if (TryFindDrawingModeIsShort(drawing, modeName, out isShort))
                    return;
            }

            if (TryFindDrawingBoolDeep(drawing, "ShortMode", out bool shortMode) && shortMode)
            {
                isShort = true;
                return;
            }

            if (TryFindDrawingSideIsShort(drawing, out isShort))
                return;

            if (knownSlPrice > 0 && entry > 0)
            {
                if (knownSlPrice > entry + tickSize * 0.5)
                {
                    isShort = true;
                    return;
                }

                if (knownSlPrice < entry - tickSize * 0.5)
                    return;
            }

            if (knownTpPrice > 0 && entry > 0)
            {
                if (knownTpPrice < entry - tickSize * 0.5)
                {
                    isShort = true;
                    return;
                }
            }
        }

        private static bool TryFindDrawingToolModeIsShort(object root, out bool isShort)
        {
            isShort = false;
            foreach (string modeName in new[] { "ToolMode", "PriceTargetsMode" })
            {
                if (TryFindDrawingModeIsShort(root, modeName, out isShort))
                    return true;
            }

            return false;
        }

        private static bool TryFindDrawingSideIsShort(object root, out bool isShort)
        {
            isShort = false;
            return TryFindDrawingMemberSideIsShort(root, "Side", out isShort, 0)
                || TryFindDrawingMemberSideIsShort(root, "PositionSide", out isShort, 0)
                || TryFindDrawingMemberSideIsShort(root, "ToolSide", out isShort, 0);
        }

        private static bool TryFindDrawingMemberSideIsShort(object root, string name, out bool isShort, int depth)
        {
            isShort = false;
            if (root == null || depth > 4) return false;

            var type = root.GetType();
            foreach (var prop in type.GetProperties(DrawingMemberFlags))
            {
                if (prop.GetIndexParameters().Length > 0) continue;

                try
                {
                    object raw = prop.GetValue(root);
                    if (raw == null) continue;

                    if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!IsDirectionSidePropertyAcceptable(type, depth))
                            continue;

                        if (TryParseSideIsShort(raw, out isShort))
                            return true;
                    }

                    if (ShouldSkipDrawingReflectionBranch(prop.PropertyType))
                        continue;

                    if (depth < 4 && raw != null && ShouldRecurseIntoProperty(prop.PropertyType))
                    {
                        if (TryFindDrawingMemberSideIsShort(raw, name, out isShort, depth + 1))
                            return true;
                    }
                }
                catch { }
            }

            return false;
        }

        private static bool IsDirectionSidePropertyAcceptable(Type ownerType, int depth)
        {
            if (depth <= 1) return true;
            return IsPreferredPriceTargetsOwner(ownerType, "Side");
        }

        private static bool TryFindDrawingModeIsShort(object root, string propertyName, out bool isShort)
        {
            isShort = false;
            return TryFindDrawingMemberModeIsShort(root, propertyName, out isShort, 0);
        }

        private static bool TryFindDrawingMemberModeIsShort(object root, string name, out bool isShort, int depth)
        {
            isShort = false;
            if (root == null || depth > 4) return false;

            foreach (var prop in root.GetType().GetProperties(DrawingMemberFlags))
            {
                if (prop.GetIndexParameters().Length > 0) continue;

                try
                {
                    object raw = prop.GetValue(root);
                    if (raw == null) continue;

                    if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryParseModeIsShort(raw, out isShort))
                            return true;
                    }

                    if (ShouldSkipDrawingReflectionBranch(prop.PropertyType))
                        continue;

                    if (depth < 4 && ShouldRecurseIntoProperty(prop.PropertyType))
                    {
                        if (TryFindDrawingMemberModeIsShort(raw, name, out isShort, depth + 1))
                            return true;
                    }
                }
                catch { }
            }

            return false;
        }

        private static bool TryParseSideIsShort(object raw, out bool isShort)
        {
            isShort = false;
            if (raw is Side side)
            {
                isShort = side == Side.Sell;
                return true;
            }

            return TryParseModeIsShort(raw, out isShort);
        }

        private static bool TryParseModeIsShort(object raw, out bool isShort)
        {
            isShort = false;
            if (raw is bool b)
            {
                isShort = b;
                return true;
            }

            if (raw is int iv)
            {
                isShort = iv == 1;
                return true;
            }

            string name = raw.ToString();
            if (string.IsNullOrEmpty(name)) return false;

            if (name.IndexOf("Short", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Sell", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                isShort = true;
                return true;
            }

            if (name.IndexOf("Long", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Buy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                isShort = false;
                return true;
            }

            return false;
        }

        private static bool ShouldRecurseIntoProperty(Type type)
        {
            if (ShouldSkipDrawingReflectionBranch(type))
                return false;
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type.IsEnum)
                return false;
            if (type.Namespace != null && type.Namespace.StartsWith("System", StringComparison.Ordinal))
                return false;
            return true;
        }

        private static bool ShouldSkipDrawingReflectionBranch(Type type)
        {
            if (type == null) return true;
            string full = type.FullName ?? type.Name;
            if (full.IndexOf("HistoryItem", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (full.IndexOf("IHistoryItem", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool IsPriceTargetsNumericProperty(string propertyName)
        {
            switch (propertyName.ToUpperInvariant())
            {
                case "TICKSLEFT":
                case "TICKSRIGHT":
                case "STOPLOSS":
                case "TAKEPROFIT":
                case "OPENPRICE":
                case "ENTRYPRICE":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsPreferredPriceTargetsOwner(Type ownerType, string propertyName)
        {
            if (ownerType == null) return false;
            string name = ownerType.Name;
            if (name.IndexOf("PriceTarget", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.Equals("Model", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.IndexOf("Drawing", StringComparison.OrdinalIgnoreCase) >= 0 &&
                name.IndexOf("History", StringComparison.OrdinalIgnoreCase) < 0) return true;

            if (IsDirectionPropertyName(propertyName))
            {
                if (name.IndexOf("Setting", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (name.IndexOf("Tool", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        private static bool IsDirectionPropertyName(string propertyName)
        {
            return propertyName.Equals("ToolMode", StringComparison.OrdinalIgnoreCase)
                || propertyName.Equals("PriceTargetsMode", StringComparison.OrdinalIgnoreCase)
                || propertyName.Equals("BracketsMode", StringComparison.OrdinalIgnoreCase)
                || propertyName.Equals("Mode", StringComparison.OrdinalIgnoreCase)
                || propertyName.Equals("Direction", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryFindDrawingNumericDeep(object root, string name, out double value)
        {
            return TryFindDrawingMemberNumeric(root, name, out value, 0);
        }

        private static bool TryFindDrawingMemberNumeric(object root, string name, out double value, int depth)
        {
            value = 0;
            if (root == null || depth > 4) return false;

            foreach (var prop in root.GetType().GetProperties(DrawingMemberFlags))
            {
                if (prop.GetIndexParameters().Length > 0) continue;

                try
                {
                    object raw = prop.GetValue(root);
                    if (raw == null) continue;

                    if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsPriceTargetsNumericProperty(name) &&
                            !IsPreferredPriceTargetsOwner(root.GetType(), name))
                            continue;

                        if (TryConvertToDouble(raw, out value) && value > 0)
                            return true;
                    }

                    if (ShouldSkipDrawingReflectionBranch(prop.PropertyType))
                        continue;

                    if (depth < 4 && ShouldRecurseIntoProperty(prop.PropertyType))
                    {
                        if (TryFindDrawingMemberNumeric(raw, name, out value, depth + 1))
                            return true;
                    }
                }
                catch { }
            }

            return false;
        }

        private static bool TryFindDrawingBoolDeep(object root, string name, out bool value)
        {
            value = false;
            return TryFindDrawingMemberBool(root, name, out value, 0);
        }

        private static bool TryFindDrawingMemberBool(object root, string name, out bool value, int depth)
        {
            value = false;
            if (root == null || depth > 4) return false;

            foreach (var prop in root.GetType().GetProperties(DrawingMemberFlags))
            {
                if (prop.GetIndexParameters().Length > 0) continue;

                try
                {
                    object raw = prop.GetValue(root);
                    if (raw == null) continue;

                    if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && raw is bool b)
                    {
                        value = b;
                        return true;
                    }

                    if (ShouldSkipDrawingReflectionBranch(prop.PropertyType))
                        continue;

                    if (depth < 4 && ShouldRecurseIntoProperty(prop.PropertyType))
                    {
                        if (TryFindDrawingMemberBool(raw, name, out value, depth + 1))
                            return true;
                    }
                }
                catch { }
            }

            return false;
        }

        private static bool TryConvertToDouble(object raw, out double value)
        {
            value = 0;
            if (raw == null) return false;

            switch (raw)
            {
                case double d:
                    value = d;
                    return true;
                case float f:
                    value = f;
                    return true;
                case decimal m:
                    value = (double)m;
                    return true;
                case int i:
                    value = i;
                    return true;
                case long l:
                    value = l;
                    return true;
                default:
                    return double.TryParse(
                        Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out value);
            }
        }

        private static bool TryCollectDistinctPointPrices(IDrawing drawing, double tickSize, out List<double> prices)
        {
            prices = new List<double>();
            var seen = new HashSet<long>();

            for (int i = 0; i < 16; i++)
            {
                try
                {
                    double price = drawing.GetPoint(i).Item2;
                    if (price <= 0) continue;

                    long key = (long)Math.Round(price / tickSize);
                    if (seen.Add(key))
                        prices.Add(price);
                }
                catch
                {
                    break;
                }
            }

            return prices.Count > 0;
        }

        private double GetChartTickSize()
        {
            var chart = this.CurrentChart;
            if (chart != null && chart.TickSize > 0)
                return chart.TickSize;

            if (this.Symbol != null && this.Symbol.TickSize > 0)
                return this.Symbol.TickSize;

            return GetActualStep();
        }

        // --- Open Order indikátor ---

        private void UpdateOpenOrderState(long now)
        {
            if (!ShowOpenOrderIndicator)
            {
                if (hasPendingLimitOrStopOrders) hasPendingLimitOrStopOrders = false;
                return;
            }

            if (now - lastOrderRefreshMs >= OrderRefreshIntervalMs)
            {
                RefreshPendingOrdersState();
                lastOrderRefreshMs = now;
            }

            int blinkInterval = OpenOrderBlinkIntervalMs > 0 ? OpenOrderBlinkIntervalMs : 1000;
            if (now - lastBlinkToggleMs >= blinkInterval)
            {
                openOrderBlinkVisible = !openOrderBlinkVisible;
                lastBlinkToggleMs = now;
            }
        }

        private void RefreshPendingOrdersState()
        {
            try
            {
                var accounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var orders = Core.Instance?.Orders;
                if (orders == null)
                {
                    accountsWithPendingOrders = Array.Empty<string>();
                    hasPendingLimitOrStopOrders = false;
                    return;
                }

                foreach (Order order in orders)
                {
                    if (order == null) continue;
                    if (!IsPendingStatus(order.Status.ToString())) continue;
                    if (!IsLimitOrStop(order.OrderTypeId)) continue;

                    string accountName = TryGetOrderAccountName(order);
                    if (!string.IsNullOrWhiteSpace(accountName))
                        accounts.Add(accountName);
                }

                var sorted = accounts.OrderBy(n => n).ToArray();
                accountsWithPendingOrders = sorted;
                hasPendingLimitOrStopOrders = sorted.Length > 0;
            }
            catch
            {
                // Core nem elérhető vagy átmeneti hiba — csendesen folytatjuk.
            }
        }

        private static string TryGetOrderAccountName(Order order)
        {
            try
            {
                object account = order.GetType().GetProperty("Account")?.GetValue(order);
                if (account == null) return "Unknown account";

                object name = account.GetType().GetProperty("Name")?.GetValue(account);
                return name?.ToString() ?? "Unknown account";
            }
            catch
            {
                return "Unknown account";
            }
        }

        private static bool IsPendingStatus(string status)
        {
            if (string.IsNullOrEmpty(status)) return false;

            string value = status.ToLowerInvariant();
            return value.Contains("open")
                || value.Contains("working")
                || value.Contains("pending")
                || value.Contains("placed")
                || value.Contains("new")
                || value.Contains("active");
        }

        private static bool IsLimitOrStop(string orderTypeId)
        {
            if (string.IsNullOrEmpty(orderTypeId)) return false;

            string value = orderTypeId.ToLowerInvariant();
            return value.Contains("limit") || value.Contains("stop");
        }

        private void DrawOpenOrderIndicator(PaintChartEventArgs args)
        {
            if (!ShowOpenOrderIndicator) return;
            if (!hasPendingLimitOrStopOrders) return;

            const int diameter = 16;
            const int margin = 8;

            int x = args.Rectangle.Right - diameter - margin;
            int y = args.Rectangle.Top + margin;

            var gr = args.Graphics;
            var prevSmoothing = gr.SmoothingMode;
            gr.SmoothingMode = SmoothingMode.AntiAlias;

            try
            {
                if (openOrderBlinkVisible)
                {
                    Color fill = OpenOrderIndicatorColor;
                    Color border = Color.FromArgb(
                        fill.A,
                        (int)(fill.R * 0.6),
                        (int)(fill.G * 0.6),
                        (int)(fill.B * 0.6));

                    using (var fillBrush = new SolidBrush(fill))
                    using (var borderPen = new Pen(border, 1f))
                    {
                        gr.FillEllipse(fillBrush, x, y, diameter, diameter);
                        gr.DrawEllipse(borderPen, x, y, diameter, diameter);
                    }
                }

                if (!OpenOrderShowAccountNames) return;

                var snapshot = accountsWithPendingOrders;
                if (snapshot == null || snapshot.Length == 0) return;

                string currentChartAccountName = this.CurrentChart?.Account?.Name;
                using (var regularFont = new Font("Segoe UI", 9f, FontStyle.Regular))
                using (var boldFont = new Font("Segoe UI", 9f, FontStyle.Bold))
                using (var textBrush = new SolidBrush(OpenOrderIndicatorColor))
                using (var format = new StringFormat { Alignment = StringAlignment.Far })
                {
                    float lineY = y + diameter + 4;
                    foreach (string accountName in snapshot)
                    {
                        Font font = string.Equals(accountName, currentChartAccountName, StringComparison.OrdinalIgnoreCase)
                            ? boldFont
                            : regularFont;

                        gr.DrawString(accountName, font, textBrush, x + diameter, lineY, format);
                        lineY += font.GetHeight(gr) + 2f;
                    }
                }
            }
            finally
            {
                gr.SmoothingMode = prevSmoothing;
            }
        }

        // --- Volume Profile POC (POC 1 & POC 2) ---

        // Karbantartja a profile-trade listát (rolling vagy fixed mód),
        // és időnként (PocComputeIntervalMs) újraszámolja a POC 1 / POC 2 árszinteket.
        // Hívás kontextusa: a meglévő stateLock alól.
        private void MaintainVolumeProfile(long now)
        {
            if (!IsVolumeProfileActive)
            {
                if (profileTrades.Count > 0) profileTrades.Clear();
                pocHasValue1 = false;
                pocHasValue2 = false;
                profileVolumeByPrice.Clear();
                profileMaxVolume = 0;
                profileVolumeValid = false;
                return;
            }

            int windowMin = VolumeProfileWindowMin > 0 ? VolumeProfileWindowMin : 30;
            long windowMs = (long)windowMin * 60_000L;

            if (VolumeProfileRolling)
            {
                long threshold = now - windowMs;
                profileTrades.RemoveAll(t => t.TimeMs < threshold);
            }
            else
            {
                // Fix időszakok: amikor egy teljes ablak letelt, kezdjük újra.
                if (profileWindowStartMs == long.MinValue / 2)
                    profileWindowStartMs = now;
                else if (now - profileWindowStartMs >= windowMs)
                {
                    profileTrades.Clear();
                    profileWindowStartMs = now;
                }
            }

            if (now - lastPocComputeMs >= PocComputeIntervalMs)
            {
                ComputeVolumeProfile();
                lastPocComputeMs = now;
            }
        }

        // A profileTrades-ből árszintenkénti volumen profilt épít; POC-kat is innen számolja.
        // Hívás kontextusa: a meglévő stateLock alól.
        private void ComputeVolumeProfile()
        {
            profileVolumeByPrice.Clear();
            profileMaxVolume = 0;
            profileVolumeValid = false;
            pocHasValue1 = false;
            pocHasValue2 = false;

            if (profileTrades.Count == 0)
                return;

            for (int i = 0; i < profileTrades.Count; i++)
            {
                var t = profileTrades[i];
                if (profileVolumeByPrice.TryGetValue(t.Price, out var v))
                    profileVolumeByPrice[t.Price] = v + t.Size;
                else
                    profileVolumeByPrice[t.Price] = t.Size;
            }

            if (profileVolumeByPrice.Count == 0)
                return;

            foreach (var kvp in profileVolumeByPrice)
            {
                if (kvp.Value > profileMaxVolume)
                    profileMaxVolume = kvp.Value;
            }

            profileVolumeValid = profileMaxVolume > 0;

            if (!ShowVolumeProfilePoc)
                return;

            double p1 = 0;
            double v1 = -1;
            foreach (var kvp in profileVolumeByPrice)
            {
                if (kvp.Value > v1)
                {
                    v1 = kvp.Value;
                    p1 = kvp.Key;
                }
            }

            pocPrice1 = p1;
            pocHasValue1 = v1 > 0;

            double step = GetActualStep();
            double minDist = step * Math.Max(0, Poc2MinDistanceTicks);

            double p2 = 0;
            double v2 = -1;
            foreach (var kvp in profileVolumeByPrice)
            {
                if (Math.Abs(kvp.Key - p1) < minDist) continue;
                if (kvp.Value > v2)
                {
                    v2 = kvp.Value;
                    p2 = kvp.Key;
                }
            }

            if (v2 > 0)
            {
                pocPrice2 = p2;
                pocHasValue2 = true;
            }
        }

        private void DrawMarketProfile(
            Graphics gr,
            IChartWindowCoordinatesConverter converter,
            float tapeLeftX,
            float tapeRightX)
        {
            if (!ShowMarketProfile) return;

            KeyValuePair<double, double>[] levels;
            double maxVol;
            lock (stateLock)
            {
                if (!profileVolumeValid || profileVolumeByPrice.Count == 0)
                    return;

                levels = profileVolumeByPrice.ToArray();
                maxVol = profileMaxVolume;
            }

            if (maxVol <= 0) return;

            double step = GetActualStep();
            if (step <= 0) step = 0.25;

            float halfStep = (float)(step / 2.0);
            float tapeWidth = tapeRightX - tapeLeftX;
            if (tapeWidth < 1f) return;

            double opacity = MarketProfileOpacity;
            if (opacity < 0) opacity = 0;
            if (opacity > 1) opacity = 1;

            int baseAlpha = MarketProfileColor.A;
            int alpha = (int)Math.Round(baseAlpha * opacity);
            if (alpha <= 0) return;
            if (alpha > 255) alpha = 255;

            var fillColor = Color.FromArgb(alpha, MarketProfileColor.R, MarketProfileColor.G, MarketProfileColor.B);

            var prevSmoothing = gr.SmoothingMode;
            gr.SmoothingMode = SmoothingMode.None;

            try
            {
                using (var brush = new SolidBrush(fillColor))
                {
                    for (int i = 0; i < levels.Length; i++)
                    {
                        double volume = levels[i].Value;
                        if (volume <= 0) continue;

                        double price = levels[i].Key;
                        float yTop = (float)converter.GetChartY(price + halfStep);
                        float yBottom = (float)converter.GetChartY(price - halfStep);
                        if (yBottom < yTop)
                        {
                            float tmp = yTop;
                            yTop = yBottom;
                            yBottom = tmp;
                        }

                        float h = yBottom - yTop;
                        if (h < 1f) h = 1f;

                        float barWidth = tapeWidth * (float)(volume / maxVol);
                        if (barWidth < 1f) continue;

                        gr.FillRectangle(brush, tapeLeftX, yTop, barWidth, h);
                    }
                }
            }
            finally
            {
                gr.SmoothingMode = prevSmoothing;
            }
        }

        private void DrawVolumeProfilePoc(
            Graphics gr,
            IChartWindowCoordinatesConverter converter,
            float tapeLeftX,
            float tapeRightX)
        {
            if (!ShowVolumeProfilePoc) return;

            // Snapshot a lock alatt, hogy ne legyen tearing a 64-bites double-ökön.
            double p1, p2;
            bool has1, has2;
            lock (stateLock)
            {
                p1 = pocPrice1;
                p2 = pocPrice2;
                has1 = pocHasValue1;
                has2 = pocHasValue2;
            }

            if (!has1 && !has2) return;

            using (var poc1Pen = new Pen(Poc1Color, 2.5f))
            using (var poc2Pen = new Pen(Poc2Color, 1.5f) { DashStyle = DashStyle.Dash })
            using (var labelFont = new Font("Arial", 8f, FontStyle.Bold))
            using (var labelBrush1 = new SolidBrush(Poc1Color))
            using (var labelBrush2 = new SolidBrush(Poc2Color))
            using (var labelFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
            {
                if (has1 && p1 > 0)
                {
                    float y = (float)converter.GetChartY(p1);
                    gr.DrawLine(poc1Pen, tapeLeftX, y, tapeRightX, y);

                    if (ShowPocLabels)
                    {
                        string text = $"P1 {FormatPrice(p1)}";
                        gr.DrawString(text, labelFont, labelBrush1, tapeRightX - 4f, y, labelFormat);
                    }
                }

                if (has2 && p2 > 0)
                {
                    float y = (float)converter.GetChartY(p2);
                    gr.DrawLine(poc2Pen, tapeLeftX, y, tapeRightX, y);

                    if (ShowPocLabels)
                    {
                        string text = $"P2 {FormatPrice(p2)}";
                        gr.DrawString(text, labelFont, labelBrush2, tapeRightX - 4f, y, labelFormat);
                    }
                }
            }
        }

        private string FormatPrice(double price)
        {
            double step = GetActualStep();
            if (step >= 1) return price.ToString("0");
            if (step >= 0.1) return price.ToString("0.0");
            if (step >= 0.01) return price.ToString("0.00");
            if (step >= 0.001) return price.ToString("0.000");
            return price.ToString("0.0000");
        }

        // --- Háttér backfill (VP + historical bubbles) ---

        private void StartBackgroundBackfill()
        {
            bool vp = IsVolumeProfileActive && VolumeProfileBackfill;
            bool hist = ShowHistoricalBubbles && HistoricalBubbleBackfill;
            if (!vp && !hist) return;
            if (this.Symbol == null) return;

            var cts = new CancellationTokenSource();
            backfillCts = cts;

            Task.Run(() =>
            {
                try
                {
                    if (vp) BackfillVolumeProfile(cts.Token);
                    if (cts.IsCancellationRequested) return;
                    if (hist) BackfillHistoricalBubbles(cts.Token);
                }
                catch { /* csendesen, ha a vendor nem ad history-t */ }
                finally
                {
                    try { this.CurrentChart?.RedrawBuffer(); }
                    catch { }
                }
            }, cts.Token);
        }

        private void BackfillHistoricalBubbles(CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            int lookbackMin = HistoricalBubbleLookbackMin > 0 ? HistoricalBubbleLookbackMin : 60;
            DateTime nowUtc;
            try { nowUtc = Core.Instance?.TimeUtils?.DateTimeUtcNow ?? DateTime.UtcNow; }
            catch { nowUtc = DateTime.UtcNow; }
            DateTime fromUtc = nowUtc.AddMinutes(-lookbackMin);

            bool gotTicks = TryBackfillHistoricalBubblesFromTickHistory(fromUtc, nowUtc, token);
            if (token.IsCancellationRequested) return;

            if (!gotTicks)
                TryBackfillHistoricalBubblesFromChartBars(fromUtc, nowUtc, token);

            lock (stateLock)
                TrimHistoricalBubbles();
        }

        private bool TryBackfillHistoricalBubblesFromTickHistory(DateTime fromUtc, DateTime toUtc, CancellationToken token)
        {
            try
            {
                var symbol = this.Symbol;
                if (symbol == null) return false;

                HistoricalData hist = symbol.GetHistory(Period.TICK1, HistoryType.Last, fromUtc, toUtc);
                if (hist == null || hist.Count == 0) return false;

                var ticks = new List<(DateTime Time, double Price, double Size, AggressorFlag Agg)>();
                for (int i = 0; i < hist.Count; i++)
                {
                    if (token.IsCancellationRequested) return false;

                    var item = hist[i];
                    if (item == null) continue;

                    DateTime time = item.TimeLeft;
                    double price = 0;
                    double size = 0;
                    var agg = AggressorFlag.NotSet;

                    if (item is HistoryItemLast tick)
                    {
                        price = tick.Price;
                        size = tick.Volume;
                        agg = tick.AggressorFlag;
                    }
                    else if (item is HistoryItemBar bar)
                    {
                        price = bar.Close;
                        size = bar.Volume;
                    }

                    if (price <= 0 || size <= 0) continue;
                    if (agg == AggressorFlag.NotSet) continue;

                    ticks.Add((time, GetRoundedPrice(price), size, agg));
                }

                if (ticks.Count == 0) return false;
                ticks.Sort((a, b) => a.Time.CompareTo(b.Time));

                lock (stateLock)
                {
                    BuildHistoricalBubblesFromTicks(ticks);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryBackfillHistoricalBubblesFromChartBars(DateTime fromUtc, DateTime toUtc, CancellationToken token)
        {
            try
            {
                var hd = this.HistoricalData;
                if (hd == null || hd.Count == 0) return false;

                var bars = new List<(DateTime Time, double Price, double Volume)>();
                for (int i = hd.Count - 1; i >= 0; i--)
                {
                    if (token.IsCancellationRequested) return false;

                    var item = hd[i];
                    if (item == null) continue;
                    if (item.TimeLeft < fromUtc) break;

                    double price = 0;
                    double vol = 0;

                    if (item is HistoryItemBar bar)
                    {
                        price = bar.Close;
                        vol = bar.Volume;
                    }
                    else if (item is HistoryItemLast tick)
                    {
                        price = tick.Price;
                        vol = tick.Volume;
                    }

                    if (price <= 0 || vol <= 0) continue;
                    bars.Add((item.TimeLeft, GetRoundedPrice(price), vol));
                }

                if (bars.Count == 0) return false;
                bars.Sort((a, b) => a.Time.CompareTo(b.Time));

                lock (stateLock)
                {
                    foreach (var bar in bars)
                    {
                        // Bar fallback: nincs buy/sell bontás, delta = teljes volume (pozitív buborék).
                        AddHistoricalBubbleFromBucket(bar.Time, bar.Price, bar.Volume, bar.Volume);
                    }
                    TrimHistoricalBubbles();
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        // Tick history: ugyanaz a bucket logika, mint élőben (BucketMs + HistoricalDeltaThreshold).
        private void BuildHistoricalBubblesFromTicks(List<(DateTime Time, double Price, double Size, AggressorFlag Agg)> ticks)
        {
            if (ticks.Count == 0) return;

            int bucketMs = BucketMs > 0 ? BucketMs : 500;
            var bucketVolumes = new Dictionary<double, PriceLevelData>();
            DateTime bucketStart = ticks[0].Time;
            DateTime bucketAnchor = bucketStart;

            void FlushCurrentBucket()
            {
                foreach (var kvp in bucketVolumes)
                {
                    double delta = kvp.Value.BuyVolume - kvp.Value.SellVolume;
                    double volume = kvp.Value.BuyVolume + kvp.Value.SellVolume;
                    AddHistoricalBubbleFromBucket(bucketAnchor, kvp.Key, delta, volume);
                }
                bucketVolumes.Clear();
            }

            for (int i = 0; i < ticks.Count; i++)
            {
                var tick = ticks[i];
                if ((tick.Time - bucketStart).TotalMilliseconds >= bucketMs)
                {
                    FlushCurrentBucket();
                    bucketStart = tick.Time;
                    bucketAnchor = tick.Time;
                }

                if (!bucketVolumes.TryGetValue(tick.Price, out var pld))
                {
                    pld = new PriceLevelData();
                    bucketVolumes[tick.Price] = pld;
                }

                if (tick.Agg == AggressorFlag.Buy)
                    pld.BuyVolume += tick.Size;
                else
                    pld.SellVolume += tick.Size;
            }

            FlushCurrentBucket();
        }

        // --- VP Backfill (induló adatfeltöltés a profil ablakra) ---

        private void BackfillVolumeProfile(CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            int windowMin = VolumeProfileWindowMin > 0 ? VolumeProfileWindowMin : 30;
            DateTime nowUtc;
            try { nowUtc = Core.Instance?.TimeUtils?.DateTimeUtcNow ?? DateTime.UtcNow; }
            catch { nowUtc = DateTime.UtcNow; }
            DateTime fromUtc = nowUtc.AddMinutes(-windowMin);

            bool gotTicks = TryBackfillFromTickHistory(fromUtc, nowUtc, token);
            if (token.IsCancellationRequested) return;

            if (!gotTicks)
                TryBackfillFromChartBars(fromUtc, nowUtc, token);

            if (token.IsCancellationRequested) return;

            // Azonnal számoljunk POC-ot, hogy ne kelljen 250ms-ig várni rá.
            lock (stateLock)
            {
                ComputeVolumeProfile();
                lastPocComputeMs = clock.ElapsedMilliseconds;
            }
        }

        // Tick-szintű historikus adat lekérése (Period.TICK1). Ez a legpontosabb,
        // a vendor szolgáltatja a tényleges trade-eket árral és mérettel.
        private bool TryBackfillFromTickHistory(DateTime fromUtc, DateTime toUtc, CancellationToken token)
        {
            try
            {
                var symbol = this.Symbol;
                if (symbol == null) return false;

                HistoricalData hist = symbol.GetHistory(Period.TICK1, HistoryType.Last, fromUtc, toUtc);
                if (hist == null) return false;
                int count = hist.Count;
                if (count == 0) return false;

                long nowMs = clock.ElapsedMilliseconds;
                int added = 0;

                lock (stateLock)
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (token.IsCancellationRequested) break;

                        var item = hist[i];
                        if (item == null) continue;

                        double price = 0;
                        double size = 0;

                        if (item is HistoryItemLast tick)
                        {
                            price = tick.Price;
                            size = tick.Volume;
                        }
                        else if (item is HistoryItemBar bar)
                        {
                            // Némelyik vendor TICK1 helyett degradál bar-ra; kezeljük le.
                            price = bar.Close;
                            size = bar.Volume;
                        }
                        else
                        {
                            continue;
                        }

                        if (price <= 0 || size <= 0) continue;

                        long ageMs = (long)(toUtc - item.TimeLeft).TotalMilliseconds;
                        if (ageMs < 0) ageMs = 0;
                        long timeMs = nowMs - ageMs;

                        profileTrades.Add(new ProfileTrade
                        {
                            TimeMs = timeMs,
                            Price = GetRoundedPrice(price),
                            Size = size
                        });
                        added++;
                    }
                }

                return added > 0;
            }
            catch
            {
                return false;
            }
        }

        // Fallback: ha tick-history nem érhető el, a chart aktuális bar-jaiból építünk
        // egy közelítő profilt. Ez kevésbé pontos (a bar teljes volumenét egyetlen árszintre
        // tesszük), de jobb mint a semmi.
        private bool TryBackfillFromChartBars(DateTime fromUtc, DateTime toUtc, CancellationToken token)
        {
            try
            {
                var hd = this.HistoricalData;
                if (hd == null || hd.Count == 0) return false;

                long nowMs = clock.ElapsedMilliseconds;
                int added = 0;

                lock (stateLock)
                {
                    // HistoricalData[0] = legfrissebb bar, [Count-1] = legrégebbi.
                    for (int i = 0; i < hd.Count; i++)
                    {
                        if (token.IsCancellationRequested) break;

                        var item = hd[i];
                        if (item == null) continue;
                        if (item.TimeLeft < fromUtc) break; // elértük az ablakot

                        double close = 0;
                        double vol = 0;

                        if (item is HistoryItemBar bar)
                        {
                            close = bar.Close;
                            vol = bar.Volume;
                        }
                        else if (item is HistoryItemLast tick)
                        {
                            close = tick.Price;
                            vol = tick.Volume;
                        }

                        if (close <= 0 || vol <= 0) continue;

                        long ageMs = (long)(toUtc - item.TimeLeft).TotalMilliseconds;
                        if (ageMs < 0) ageMs = 0;
                        long timeMs = nowMs - ageMs;

                        // Egyszerű közelítés: bar teljes volumenét a close áron könyveljük.
                        profileTrades.Add(new ProfileTrade
                        {
                            TimeMs = timeMs,
                            Price = GetRoundedPrice(close),
                            Size = vol
                        });
                        added++;
                    }
                }

                return added > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
