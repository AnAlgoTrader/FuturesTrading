using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;

namespace FootPrintNT
{
    /// <summary>
    /// One order-flow side panel tab. Subscribes to L1 market data of the selected
    /// instrument, feeds the footprint classifier, runs analyzer + interpreter every
    /// second and renders the result — a WPF equivalent of the Photino sidebar.
    /// </summary>
    public class FlowPanelTab : NTTabPage, IInstrumentProvider
    {
        private Instrument instrument;
        private InstrumentSelector instrumentSelector;

        private FootprintClassifier classifier;
        private readonly FootprintAnalyzer analyzer = new FootprintAnalyzer();
        private DispatcherTimer timer;

        private double bid;
        private double ask;
        private double lastPrice;
        private double tickSize = 0.25;
        private int decimals = 2;

        // Header
        private TextBlock headerSymbol;

        // Interpretation card
        private Border interpCard;
        private TextBlock interpHeadline;
        private Border interpActionRow;
        private TextBlock interpAction;
        private TextBlock interpDetails;

        // Bias card
        private Border biasCard;
        private TextBlock biasHeadline;
        private TextBlock biasAction;

        // Signals card
        private TextBlock signalsCount;
        private StackPanel signalsPanel;

        // Live metrics
        private TextBlock mDelta, mDeltaPct, mVol, mPoc, mPocZone, mPocShift;
        private TextBlock mCumDelta, mDiv;

        public FlowPanelTab()
        {
            Content = BuildUi();
        }

        // ================================================================
        // UI construction
        // ================================================================

        private UIElement BuildUi()
        {
            var root = new Grid { Background = PanelTheme.Bg };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // instrument selector
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // content

            // --- Header -------------------------------------------------
            var headerGrid = new Grid { Margin = new Thickness(12, 10, 12, 4) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = PanelTheme.TextBlock("ORDER FLOW CONTEXT", PanelTheme.Text, 13, FontWeights.Bold);
            Grid.SetColumn(title, 0);
            headerGrid.Children.Add(title);

            var liveGrid = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = PanelTheme.Up };
            liveGrid.Children.Add(dot);
            liveGrid.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = " LIVE",
                Foreground = PanelTheme.Muted,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(liveGrid, 1);
            headerGrid.Children.Add(liveGrid);
            Grid.SetRow(headerGrid, 0);
            root.Children.Add(headerGrid);

            // --- Instrument selector ------------------------------------
            instrumentSelector = new InstrumentSelector { LastUsedGroup = "FootPrintPanel" };
            instrumentSelector.InstrumentChanged += OnInstrumentChanged;
            headerSymbol = PanelTheme.TextBlock("Select an instrument…", PanelTheme.Muted, 11);
            headerSymbol.Margin = new Thickness(2, 2, 0, 0);

            var selectorHost = new StackPanel { Margin = new Thickness(12, 0, 12, 8) };
            selectorHost.Children.Add(instrumentSelector);
            selectorHost.Children.Add(headerSymbol);
            Grid.SetRow(selectorHost, 1);
            root.Children.Add(selectorHost);
            // --- Scrollable card stack ----------------------------------
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(12, 0, 12, 12)
            };
            var cards = new StackPanel();

            cards.Children.Add(BuildInterpretationCard());
            cards.Children.Add(BuildBiasCard());
            cards.Children.Add(BuildSignalsCard());
            cards.Children.Add(BuildMetricsCard());
            cards.Children.Add(BuildStructureCard());

            scroll.Content = cards;
            Grid.SetRow(scroll, 2);
            root.Children.Add(scroll);

            return root;
        }

        private Border BuildInterpretationCard()
        {
            interpCard = PanelTheme.MakeCard("INTERPRETATION", PanelTheme.Border, out var body);

            interpHeadline = PanelTheme.TextBlock("-", PanelTheme.Text, 14, FontWeights.SemiBold);
            interpHeadline.Margin = new Thickness(0, 0, 0, 4);
            body.Children.Add(interpHeadline);

            interpActionRow = new Border
            {
                Background = PanelTheme.Panel,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Visibility = Visibility.Collapsed
            };
            interpAction = PanelTheme.TextBlock("", PanelTheme.Text, 12, FontWeights.Bold);
            interpActionRow.Child = interpAction;
            body.Children.Add(interpActionRow);

            interpDetails = PanelTheme.TextBlock("", PanelTheme.Muted, 11);
            interpDetails.Visibility = Visibility.Collapsed;
            body.Children.Add(interpDetails);

            return interpCard;
        }

        private Border BuildBiasCard()
        {
            biasCard = PanelTheme.MakeCard("AUCTION BIAS", PanelTheme.Border, out var body);

            biasHeadline = PanelTheme.TextBlock("Waiting for Data...", PanelTheme.Text, 14, FontWeights.SemiBold);
            biasHeadline.Margin = new Thickness(0, 0, 0, 2);
            body.Children.Add(biasHeadline);

            biasAction = PanelTheme.TextBlock("-", PanelTheme.Muted, 12);
            body.Children.Add(biasAction);

            return biasCard;
        }

        private Border BuildSignalsCard()
        {
            var card = PanelTheme.MakeCard("ORDER FLOW SIGNALS", PanelTheme.Border, out var body);

            signalsCount = PanelTheme.TextBlock("0", PanelTheme.Muted, 11, FontWeights.Bold);
            signalsCount.Margin = new Thickness(6, 0, 0, 0);

            signalsPanel = new StackPanel();
            body.Children.Add(signalsPanel);

            var placeholder = PanelTheme.TextBlock("No active order flow signals", PanelTheme.Muted, 11);
            placeholder.Margin = new Thickness(0, 2, 0, 2);
            signalsPanel.Children.Add(placeholder);

            return card;
        }

        private Border BuildMetricsCard()
        {
            var card = PanelTheme.MakeCard("LIVE BAR METRICS", PanelTheme.Border, out var body);
            var grid = PanelTheme.MetricsGrid(2);

            PanelTheme.AddMetric(grid, 0, 0, PanelTheme.Metric("Bar Delta", out mDelta));
            PanelTheme.AddMetric(grid, 0, 1, PanelTheme.Metric("Delta %", out mDeltaPct));
            PanelTheme.AddMetric(grid, 1, 0, PanelTheme.Metric("Bar Volume", out mVol));
            PanelTheme.AddMetric(grid, 1, 1, PanelTheme.Metric("POC Price", out mPoc));
            PanelTheme.AddMetric(grid, 2, 0, PanelTheme.Metric("POC Zone", out mPocZone));
            PanelTheme.AddMetric(grid, 2, 1, PanelTheme.Metric("POC Shift", out mPocShift));

            body.Children.Add(grid);
            return card;
        }

        private Border BuildStructureCard()
        {
            var card = PanelTheme.MakeCard("AUCTION STRUCTURE", PanelTheme.Border, out var body);
            var grid = PanelTheme.MetricsGrid(2);

            PanelTheme.AddMetric(grid, 0, 0, PanelTheme.Metric("Cum. Delta", out mCumDelta));
            PanelTheme.AddMetric(grid, 0, 1, PanelTheme.Metric("Divergence", out mDiv));

            body.Children.Add(grid);
            return card;
        }

        // ================================================================
        // Instrument handling / data subscription
        // ================================================================

        private void OnInstrumentChanged(object sender, EventArgs e)
        {
            Instrument = sender as Instrument;
        }

        public Instrument Instrument
        {
            get { return instrument; }
            set
            {
                if (instrument == value) return;

                var old = instrument;
                instrument = value;

                Unsubscribe(old);

                if (instrumentSelector != null)
                    instrumentSelector.Instrument = value;

                if (value != null)
                    Subscribe(value);

                // Send instrument to other windows linked to the same color
                PropagateInstrumentChange(value);
                RefreshHeader();
            }
        }

        private void Subscribe(Instrument inst)
        {
            tickSize = inst.MasterInstrument.TickSize;
            decimals = FootprintAnalyzer.Decimals(tickSize);
            bid = 0;
            ask = 0;
            lastPrice = 0;

            classifier = new FootprintClassifier(tickSize, TradingConfig.TicksPerBucket);
            analyzer.Reset();

            if (!inst.Dispatcher.HasShutdownStarted)
                inst.Dispatcher.InvokeAsync(() => inst.MarketData.Update += OnMarketData);

            if (timer == null)
            {
                timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnTimerTick, Dispatcher);
            }
            timer.Start();

            if (headerSymbol != null)
            {
                headerSymbol.Text = inst.ToString();
                headerSymbol.Foreground = PanelTheme.Muted;
            }
        }

        private void Unsubscribe(Instrument inst)
        {
            if (timer != null)
                timer.Stop();

            if (inst != null && !inst.Dispatcher.HasShutdownStarted)
                inst.Dispatcher.InvokeAsync(() =>
                {
                    try { inst.MarketData.Update -= OnMarketData; }
                    catch { /* subscription may not exist */ }
                });

            classifier = null;
            bid = 0;
            ask = 0;
            lastPrice = 0;

            ResetUi();
        }

        private void OnMarketData(object sender, MarketDataEventArgs e)
        {
            if (classifier == null) return;

            if (e.MarketDataType == MarketDataType.Last)
            {
                if (e.Price <= 0 || e.Volume <= 0) return;
                lastPrice = e.Price;
                classifier.OnTrade(FootprintRender.ToUnixSeconds(e.Time), e.Price, e.Volume);
            }
            else if (e.MarketDataType == MarketDataType.Bid)
            {
                if (e.Price <= 0) return;
                bid = e.Price;
                TryQuote(e.Time);
            }
            else if (e.MarketDataType == MarketDataType.Ask)
            {
                if (e.Price <= 0) return;
                ask = e.Price;
                TryQuote(e.Time);
            }
        }

        private void TryQuote(DateTime time)
        {
            // The classifier wants a complete quote; until both sides are known it
            // classifies trades with the tick-rule fallback.
            if (classifier != null && bid > 0 && ask > 0)
                classifier.OnQuote(FootprintRender.ToUnixSeconds(time), bid, ask);
        }

        // ================================================================
        // 1s refresh tick → analyze → render
        // ================================================================

        private void OnTimerTick(object sender, EventArgs e)
        {
            var cls = classifier;
            if (cls == null) return;

            var snap = cls.GetSnapshot();
            if (snap.Current == null && snap.Completed.Length == 0)
                return;

            var ctx = analyzer.AnalyzeContext(snap, tickSize, lastPrice);
            var interp = FootprintInterpreter.Interpret(ctx, snap, tickSize);

            RenderContext(ctx);
            RenderInterpretation(interp);
        }

        // ================================================================
        // Rendering
        // ================================================================

        private void ResetUi()
        {
            interpHeadline.Text = "-";
            interpHeadline.Foreground = PanelTheme.Text;
            interpAction.Text = "";
            interpActionRow.Visibility = Visibility.Collapsed;
            interpDetails.Text = "";
            interpDetails.Visibility = Visibility.Collapsed;
            interpCard.BorderBrush = PanelTheme.Border;
            interpCard.BorderThickness = new Thickness(1);

            biasHeadline.Text = "Waiting for Data...";
            biasHeadline.Foreground = PanelTheme.Text;
            biasAction.Text = "-";
            biasCard.BorderBrush = PanelTheme.Border;
            biasCard.BorderThickness = new Thickness(1);

            signalsCount.Text = "0";
            signalsPanel.Children.Clear();
            signalsPanel.Children.Add(PanelTheme.TextBlock("No active order flow signals", PanelTheme.Muted, 11));

            mDelta.Text = "-"; mDelta.Foreground = PanelTheme.Text;
            mDeltaPct.Text = "-"; mDeltaPct.Foreground = PanelTheme.Text;
            mVol.Text = "-";
            mPoc.Text = "-";
            mPocZone.Text = "-";
            mPocShift.Text = "-"; mPocShift.Foreground = PanelTheme.Text;
            mCumDelta.Text = "-"; mCumDelta.Foreground = PanelTheme.Text;
            mDiv.Text = "None"; mDiv.Foreground = PanelTheme.Text;
        }

        private void RenderContext(OrderFlowContext ctx)
        {
            if (ctx == null) return;

            // Bias card
            var toneBrush = PanelTheme.Tone(ctx.BiasTone);
            biasHeadline.Text = string.IsNullOrEmpty(ctx.Bias) ? "Balanced" : ctx.Bias;
            biasHeadline.Foreground = ctx.BiasTone == "neutral" ? PanelTheme.Text : toneBrush;
            biasAction.Text = string.IsNullOrEmpty(ctx.Action) ? "-" : ctx.Action;
            biasCard.BorderBrush = ctx.BiasTone == "neutral" ? PanelTheme.Border : toneBrush;
            biasCard.BorderThickness = ctx.BiasTone == "neutral" ? new Thickness(1) : new Thickness(3, 0, 0, 0);

            // Signals
            var signals = ctx.Signals ?? new System.Collections.Generic.List<OrderFlowSignal>();
            signalsCount.Text = signals.Count.ToString();
            signalsPanel.Children.Clear();
            if (signals.Count == 0)
            {
                signalsPanel.Children.Add(PanelTheme.TextBlock("No active order flow signals", PanelTheme.Muted, 11));
            }
            else
            {
                foreach (var sig in signals)
                    signalsPanel.Children.Add(BuildSignalRow(sig));
            }

            // Live metrics
            SetSigned(mDelta, ctx.CurrentDelta);
            mDelta.Foreground = ctx.CurrentDelta > 0 ? PanelTheme.Up : ctx.CurrentDelta < 0 ? PanelTheme.Down : PanelTheme.Text;

            var pctSign = ctx.DeltaPercent > 0 ? "+" : "";
            mDeltaPct.Text = pctSign + ctx.DeltaPercent.ToString("F1", CultureInfo.InvariantCulture) + "%";
            mDeltaPct.Foreground = ctx.DeltaPercent > 0 ? PanelTheme.Up : ctx.DeltaPercent < 0 ? PanelTheme.Down : PanelTheme.Text;

            mVol.Text = FmtNum((double)ctx.CurrentVolume);
            mPoc.Text = ctx.PocPrice.ToString("F" + decimals, CultureInfo.InvariantCulture);
            mPocZone.Text = string.IsNullOrEmpty(ctx.PocPosition) ? "-" : ctx.PocPosition;

            mPocShift.Text = string.IsNullOrEmpty(ctx.PocMigration) ? "-" : ctx.PocMigration;
            if (ctx.PocMigration != null && ctx.PocMigration.Contains("▲")) mPocShift.Foreground = PanelTheme.Up;
            else if (ctx.PocMigration != null && ctx.PocMigration.Contains("▼")) mPocShift.Foreground = PanelTheme.Down;
            else mPocShift.Foreground = PanelTheme.Text;

            // Structure
            SetSigned(mCumDelta, ctx.CumulativeDelta);
            mCumDelta.Foreground = ctx.CumulativeDelta > 0 ? PanelTheme.Up : ctx.CumulativeDelta < 0 ? PanelTheme.Down : PanelTheme.Text;

            var divText = string.IsNullOrEmpty(ctx.DeltaDivergence) ? "None" : ctx.DeltaDivergence;
            mDiv.Text = divText;
            if (divText.Contains("Bullish")) mDiv.Foreground = PanelTheme.Up;
            else if (divText.Contains("Bearish")) mDiv.Foreground = PanelTheme.Down;
            else mDiv.Foreground = PanelTheme.Text;
        }

        private void RenderInterpretation(MarketInterpretation interp)
        {
            if (interp == null) return;

            var isNeutral = interp.Tone == "neutral" || (string.IsNullOrEmpty(interp.Action) && string.IsNullOrEmpty(interp.Title));
            if (isNeutral)
            {
                interpHeadline.Text = "-";
                interpHeadline.Foreground = PanelTheme.Text;
                interpAction.Text = "";
                interpActionRow.Visibility = Visibility.Collapsed;
                interpDetails.Text = "";
                interpDetails.Visibility = Visibility.Collapsed;
                interpCard.BorderBrush = PanelTheme.Border;
                interpCard.BorderThickness = new Thickness(1);
                return;
            }

            var toneBrush = PanelTheme.Tone(interp.Tone);
            interpHeadline.Text = string.IsNullOrEmpty(interp.Title) ? "-" : interp.Title;
            interpHeadline.Foreground = toneBrush;

            if (string.IsNullOrEmpty(interp.Action))
            {
                interpAction.Text = "";
                interpActionRow.Visibility = Visibility.Collapsed;
            }
            else
            {
                interpAction.Text = interp.Action;
                interpAction.Foreground = toneBrush;
                interpActionRow.Visibility = Visibility.Visible;
            }

            if (string.IsNullOrEmpty(interp.Details))
            {
                interpDetails.Text = "";
                interpDetails.Visibility = Visibility.Collapsed;
            }
            else
            {
                interpDetails.Text = interp.Details;
                interpDetails.Visibility = Visibility.Visible;
            }

            interpCard.BorderBrush = toneBrush;
            interpCard.BorderThickness = new Thickness(3, 0, 0, 0);
        }

        private UIElement BuildSignalRow(OrderFlowSignal sig)
        {
            var toneBrush = PanelTheme.Tone(sig.Tone);

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = PanelTheme.TextBlock(sig.Title, PanelTheme.Text, 11, FontWeights.SemiBold);
            Grid.SetColumn(title, 0);
            header.Children.Add(title);

            if (!string.IsNullOrEmpty(sig.Time))
            {
                var time = PanelTheme.TextBlock(sig.Time, PanelTheme.Muted, 10);
                Grid.SetColumn(time, 1);
                header.Children.Add(time);
            }

            var inner = new StackPanel();
            inner.Children.Add(header);
            if (!string.IsNullOrEmpty(sig.Description))
            {
                var desc = PanelTheme.TextBlock(sig.Description, PanelTheme.Muted, 10);
                desc.Margin = new Thickness(0, 2, 0, 0);
                inner.Children.Add(desc);
            }

            var row = new Border
            {
                Background = PanelTheme.Panel,
                BorderBrush = toneBrush,
                BorderThickness = new Thickness(2, 0, 0, 0),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 4),
                Child = inner
            };
            return row;
        }

        // ================================================================
        // Formatting helpers (mirror main.js fmtNum / fmtSigned)
        // ================================================================

        private static string FmtNum(double n)
        {
            if (double.IsNaN(n) || double.IsInfinity(n)) return "-";
            var abs = Math.Abs(n);
            if (abs >= 1000) return (n / 1000).ToString("F1", CultureInfo.InvariantCulture) + "k";
            return (Math.Round(n * 10) / 10).ToString(CultureInfo.InvariantCulture);
        }

        private static string FmtSigned(double n)
        {
            if (double.IsNaN(n) || double.IsInfinity(n)) return "-";
            var sign = n > 0 ? "+" : n < 0 ? "−" : "";
            return sign + FmtNum(Math.Abs(n));
        }

        private static void SetSigned(TextBlock tb, double value)
        {
            tb.Text = FmtSigned(value);
        }

        // ================================================================
        // NTTabPage required members
        // ================================================================

        protected override string GetHeaderPart(string variable)
        {
            return "@INSTRUMENT";
        }

        protected override void Restore(XElement element)
        {
            // No custom state to restore; the InstrumentSelector persists its own selection.
        }

        protected override void Save(XElement element)
        {
            // No custom state to persist.
        }

        /// <summary>
        /// Called by TabControl when a tab is being removed or the window is closed.
        /// </summary>
        public override void Cleanup()
        {
            if (timer != null)
                timer.Stop();

            var inst = instrument;
            if (inst != null && !inst.Dispatcher.HasShutdownStarted)
                inst.Dispatcher.InvokeAsync(() =>
                {
                    try { inst.MarketData.Update -= OnMarketData; }
                    catch { /* already detached */ }
                });

            if (instrumentSelector != null)
                instrumentSelector.InstrumentChanged -= OnInstrumentChanged;

            base.Cleanup();
        }
    }
}
