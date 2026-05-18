using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
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

        // --- Belső állapot ---

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

            UpdateShortName();
        }

        protected override void OnClear()
        {
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

            // A nyitott order indikátort a clip-en kívül rajzoljuk, hogy a chart sarkában
            // mindig látható legyen, függetlenül a tape sávtól.
            DrawOpenOrderIndicator(args);

            UpdateShortName();
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
