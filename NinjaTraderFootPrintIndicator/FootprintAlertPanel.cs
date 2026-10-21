#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript.BarsTypes;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using FootPrintTrader.Helper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	// SCOPE: daytrading panel for ONE instrument over a short intraday session (a couple of hours).
	// It is not expected to run across multiple sessions or switch instruments.
	//
	// Deliberate omissions (do not "fix" these):
	// - No state reset on State.Configure. _analyzer (_cumulativeDelta, _processedBars,
	//   _activeZones, baselines), _completedCache and _snapshot/_ctx/_interp carry over
	//   across a reconfigure. That is acceptable here: the only realistic reconfigures are
	//   a bar-period change or a template reload mid-session, and the panel is rebuilt from
	//   scratch within a few bars anyway. Resetting would trade a real edge case for churn
	//   in code that is otherwise stable.
	//   The thresholds themselves are NOT frozen, though: BuildSettings writes the current
	//   UI values into the one settings object the analyzer holds a reference to, so a
	//   parameter change applies on the next tick while the analyzer state survives.
	// - No multi-session handling: cumulative delta, volume baselines and level-volume stats
	//   are session-scoped by design and are never rolled over or persisted.
	//
	// The processed-bar set is keyed on bar StartTime (unix seconds), not on bar index.
	// That is what makes the reconfigure case above safe: after a bar-period change or
	// template reload, a bar that is re-evaluated with a shifted CurrentBar still has the
	// same StartTime and is not double-counted into CVD or the baselines.
	public class FootprintAlertPanel : Indicator
	{
		private const int CompletedBars = 25;
		private const float InnerWidth = 450f;
		private const float PadX = 12f;
		private const float PadY = 10f;
		private const float PanelOffsetX = 14f;
		private const float PanelOffsetY = 60f;

		private static readonly SharpDX.Color ColSection = new SharpDX.Color(120, 190, 255);
		private static readonly SharpDX.Color ColDim = new SharpDX.Color(165, 168, 175);
		private static readonly SharpDX.Color ColWhite = new SharpDX.Color(235, 235, 235);
		private static readonly SharpDX.Color ColNeutral = new SharpDX.Color(180, 180, 180);
		private static readonly SharpDX.Color ColBull = new SharpDX.Color(0, 205, 100);
		private static readonly SharpDX.Color ColBear = new SharpDX.Color(240, 85, 85);
		private static readonly SharpDX.Color ColWarn = new SharpDX.Color(240, 185, 45);

		private TextFormat _fmtNormal;
		private TextFormat _fmtBold;
		private TextFormat _fmtSmall;

		private FootprintSnapshot _snapshot;
		private FootprintAnalyzer _analyzer;
		private FootprintSettings _settings;
		private OrderFlowContext _ctx;
		private MarketInterpretation _interp;
		private string _status = "";
		private FootprintBarData[] _completedCache;
		private int _completedCacheBar = -1;

		private long _lastCalcMs;

		private List<PanelLine> _lines;
		private int _linesVersion;
		private List<TextLayout> _cachedLayouts;
		private int _cachedVersion = -1;
		private float _cachedPanelW;
		private float _cachedPanelH;

		private struct PanelLine
		{
			public string Text;
			public SharpDX.Color Color;
			public TextFormat Fmt;
			public float Gap;
		}

		[NinjaScriptProperty]
		[Range(0, 60000)]
		[Display(Name = "Update interval (ms)", Description = "Minimum milliseconds between recomputations (0 = every incoming tick).", Order = 1, GroupName = "Parameters")]
		public int UpdateIntervalMs { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show gate diagnostics", Description = "Renders a one-line gate state (zones, live trades/volume/levels vs required) under the message list. Use this to see why the message list is empty.", Order = 2, GroupName = "Parameters")]
		public bool ShowGateDiagnostics { get; set; }

		// --- Order flow thresholds ---------------------------------------------
		// Every value is a ratio, a percentage of a rolling baseline, or a bar count. There are
		// no absolute trade-count or tick-distance thresholds, so the detector tracks volatility
		// as it moves within the session. Percent values are whole numbers (25 = 25%); every
		// consumer divides by 100 before using them as multipliers.

		[NinjaScriptProperty]
		[Range(1.5, 10.0)]
		[Display(Name = "Imbalance ratio", Description = "ask[i+1] must be at least this multiple of bid[i] on adjacent levels. A ratio, not a percent. 2.0 is the ES setting; 3.0+ is for large-tick instruments.", Order = 1, GroupName = "Order Flow")]
		public double ImbalanceRatio { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Imbalance run (% of avg range)", Description = "A run must span at least this % of the recent average bar range to count as stacked. Capped at 4 consecutive levels.", Order = 2, GroupName = "Order Flow")]
		public double ImbalanceRunRangePct { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Min bar volume (% of avg)", Description = "A bar must carry at least this % of the recent average bar volume to be considered.", Order = 3, GroupName = "Order Flow")]
		public double MinBarVolumePct { get; set; }

		[NinjaScriptProperty]
		[Range(0, 50)]
		[Display(Name = "Min level volume (% of bar avg)", Description = "A price level must carry at least this % of its bar's average level volume.", Order = 4, GroupName = "Order Flow")]
		public double MinLevelVolumePct { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Min live trades (% of avg)", Description = "The live bar must show at least this % of the recent average trade count before live signals fire. 60 = roughly the last third of the bar.", Order = 5, GroupName = "Order Flow")]
		public double MinLiveTradesPct { get; set; }

		[NinjaScriptProperty]
		[Range(2, 200)]
		[Display(Name = "Min live levels", Description = "The live bar must have printed at least this many price levels before live signals are allowed. Guards against reading a stacked run off a truncated ladder.", Order = 6, GroupName = "Order Flow")]
		public int MinLiveLevels { get; set; }

		[NinjaScriptProperty]
		[Range(1.0, 8.0)]
		[Display(Name = "Absorption sigma", Description = "A level qualifies as absorption above mean + sigma x std-dev of recent level volumes.", Order = 7, GroupName = "Order Flow")]
		public double AbsorptionSigma { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Absorption edge (% of avg range)", Description = "The absorption level must sit within this % of the recent average bar range from the extreme.", Order = 8, GroupName = "Order Flow")]
		public double AbsorptionEdgePct { get; set; }

		[NinjaScriptProperty]
		[Range(50, 100)]
		[Display(Name = "Absorption side dominance (% of level)", Description = "One aggressor side must account for at least this % of the level's total volume.", Order = 9, GroupName = "Order Flow")]
		public double AbsorptionSidePct { get; set; }

		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name = "Absorption min confirm depth (ticks)", Description = "A rejection must move at least this many ticks beyond the absorption level to confirm. Prevents 1-tick confirms during warm-up.", Order = 10, GroupName = "Order Flow")]
		public double AbsorptionMinDepthTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, 25)]
		[Display(Name = "Zone age (bars)", Description = "How many bars a stacked-imbalance zone stays active before it is discarded.", Order = 11, GroupName = "Order Flow")]
		public int MaxZoneAgeBars { get; set; }

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= "Displays interpretation, auction bias and order flow messages in a panel on the chart.";
				Name						= "FootprintAlertPanel";
				Calculate					= Calculate.OnEachTick;
				IsOverlay					= true;
				IsChartOnly					= true;
				IsSuspendedWhileInactive	= false;
				DisplayInDataBox			= false;
				DrawOnPricePanel			= true;
				PaintPriceMarkers			= false;
				ScaleJustification			= ScaleJustification.Right;
				UpdateIntervalMs			= 500;
				ShowGateDiagnostics			= false;

				// Order Flow defaults. Kept in sync with FootprintSettings.
				ImbalanceRatio				= 2.0;
				ImbalanceRunRangePct		= 15;
				MinBarVolumePct				= 25;
				MinLevelVolumePct			= 25;
				MinLiveTradesPct			= 60;
				MinLiveLevels				= 8;
				AbsorptionSigma				= 3.0;
				AbsorptionEdgePct			= 25;
				AbsorptionSidePct			= 60;
				AbsorptionMinDepthTicks		= 3.0;
				MaxZoneAgeBars				= 20;
			}
			else if (State == State.Terminated)
			{
				if (_fmtNormal != null) { _fmtNormal.Dispose(); _fmtNormal = null; }
				if (_fmtBold != null) { _fmtBold.Dispose(); _fmtBold = null; }
				if (_fmtSmall != null) { _fmtSmall.Dispose(); _fmtSmall = null; }

				if (_cachedLayouts != null)
				{
					foreach (var l in _cachedLayouts)
						l.Dispose();
					_cachedLayouts = null;
				}
				// The analyzer holds the settings object by reference; dropping it on
				// terminate is fine because a fresh instance is built on next State.Realtime.
				_settings = null;
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 1)
				return;

			var nowMs = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;

			// Throttle: when analysis is skipped, still refresh the cheap status line so the
			// panel does not show a stale timestamp with a fresh context. Only the expensive
			// analyzer pass is throttled.
			if (!IsFirstTickOfBar && UpdateIntervalMs > 0 && nowMs - _lastCalcMs < UpdateIntervalMs)
			{
				_status = "Throttled  |  " + Time[0].ToString("HH:mm:ss") + "  |  Bar " + CurrentBar;
				return;
			}

			_lastCalcMs = nowMs;

			try
			{
				// The analyzer holds a reference to this settings object, so writing the
				// current UI values into it on every update makes a parameter change take
				// effect immediately - without throwing away the analyzer's accumulated
				// state (CVD, zones, baselines).
				var settings = BuildSettings();

				if (_analyzer == null)
					_analyzer = new FootprintAnalyzer(settings);

				var snapshot = BuildSnapshot();
				_snapshot = snapshot;
				_ctx = _analyzer.AnalyzeContext(snapshot, TickSize, Close[0]);
				_interp = FootprintInterpreter.Interpret(_ctx, snapshot, TickSize);
				_status = "Updated " + Time[0].ToString("HH:mm:ss") + "  |  Bar " + CurrentBar;
			}
			catch (Exception ex)
			{
				_status = "Error: " + ex.Message;
			}

			RefreshLines();
		}

		private FootprintSettings BuildSettings()
		{
			if (_settings == null)
				_settings = new FootprintSettings();

			_settings.ImbalanceRatio			= ImbalanceRatio;
			_settings.ImbalanceRunRangePct	= ImbalanceRunRangePct;
			_settings.MinBarVolumePct		= MinBarVolumePct;
			_settings.MinLevelVolumePct		= MinLevelVolumePct;
			_settings.MinLiveTradesPct		= MinLiveTradesPct;
			_settings.MinLiveLevels			= MinLiveLevels;
			_settings.AbsorptionSigma		= AbsorptionSigma;
			_settings.AbsorptionEdgePct		= AbsorptionEdgePct;
			_settings.AbsorptionSidePct		= AbsorptionSidePct;
			_settings.AbsorptionMinDepthTicks = AbsorptionMinDepthTicks;
			_settings.MaxZoneAgeBars		= MaxZoneAgeBars;

			return _settings;
		}

		private void RefreshLines()
		{
			// EnsureFormats can fail if the DirectWrite factory is not ready yet (cold start,
			// State.Realtime transition). Retry on each RefreshLines instead of letting the
			// null TextFormat turn into a per-tick exception loop.
			EnsureFormats();
			if (_fmtNormal == null || _fmtBold == null || _fmtSmall == null)
				return;

			_lines = BuildLines();
			_linesVersion++;
			RequestRedraw();
		}

		private FootprintSnapshot BuildSnapshot()
		{
			if (Bars == null || Bars.BarsSeries == null || CurrentBar < 1)
				return null;

			var barsType = Bars.BarsSeries.BarsType as VolumetricBarsType;
			if (barsType == null)
				return null;

			// Rebuild the completed-bar window only when the bar index changes. Within a bar,
			// the cache is frozen on purpose: completed bars do not change. The current bar
			// is rebuilt every call because it is still developing.
			//
			// Indexing invariant: BuildCompleted iterates barsAgo from min(CompletedBars,
			// CurrentBar) down to 1 and computes volIndex = CurrentBar - barsAgo. This
			// assumes BarsSeries is zero-based and that CurrentBar equals the last index.
			// Both hold for the standard NinjaTrader bar series; do not reuse this helper
			// against a sliced or offset series without re-deriving the index mapping.
			if (_completedCache == null || _completedCacheBar != CurrentBar)
			{
				_completedCache = BuildCompleted(barsType);
				_completedCacheBar = CurrentBar;
			}

			return new FootprintSnapshot
			{
				Completed = _completedCache,
				Current = BuildBar(barsType, CurrentBar, 0)
			};
		}

		private FootprintBarData[] BuildCompleted(VolumetricBarsType barsType)
		{
			var list = new List<FootprintBarData>();
			var count = Math.Min(CompletedBars, CurrentBar);

			for (var barsAgo = count; barsAgo >= 1; barsAgo--)
			{
				var volIndex = CurrentBar - barsAgo;
				if (volIndex < 0)
					continue;

				var bar = BuildBar(barsType, volIndex, barsAgo);
				if (bar != null)
					list.Add(bar);
			}

			return list.ToArray();
		}

		private FootprintBarData BuildBar(VolumetricBarsType barsType, int volIndex, int barsAgo)
		{
			var high = High[barsAgo];
			var low = Low[barsAgo];

			if (high <= 0 && low <= 0)
				return null;

			var vol = barsType.Volumes[volIndex];
			if (vol == null)
				return null;

			var tick = TickSize;
			var levels = new List<FootprintLevel>();
			var guard = 0;

			// Cost note: this loops every tick level in [low, high] and calls
			// GetBid/AskVolumeForPrice twice per level. On a 1000-tick ES bar that is
			// ~40-80 levels x 2 calls. That is fine at 2 Hz but should be revisited if
			// the update interval is dropped to 0 on a slow machine.
			for (var p = low; p <= high + tick * 0.5 && guard < 5000; p += tick, guard++)
			{
				var price = Instrument.MasterInstrument.RoundToTickSize(p);

				long bid = 0;
				long ask = 0;
				try
				{
					bid = vol.GetBidVolumeForPrice(price);
					ask = vol.GetAskVolumeForPrice(price);
				}
				catch
				{
				}

				levels.Add(new FootprintLevel
				{
					Price = price,
					BidVolume = bid,
					AskVolume = ask,
					Total = (decimal)bid + ask
				});
			}

			return new FootprintBarData
			{
				StartTime = new DateTimeOffset(Time[barsAgo]).ToUnixTimeSeconds(),
				High = high,
				Low = low,
				Close = Close[barsAgo],
				Delta = vol.BarDelta,
				Volume = vol.TotalVolume,
				TradeCount = (int)vol.Trades,
				Levels = levels.ToArray()
			};
		}

		private void RequestRedraw()
		{
			var cc = ChartControl;
			if (cc == null)
				return;

			if (cc.Dispatcher.CheckAccess())
				cc.InvalidateVisual();
			else
				cc.Dispatcher.InvokeAsync(() => cc.InvalidateVisual());
		}

		private void EnsureFormats()
		{
			if (_fmtNormal != null)
				return;

			try
			{
				var factory = Core.Globals.DirectWriteFactory;
				if (factory == null)
					return;

				_fmtNormal = new TextFormat(factory, "Consolas", 13f);
				_fmtBold = new TextFormat(factory, "Consolas", FontWeight.Bold, FontStyle.Normal, 13f);
				_fmtSmall = new TextFormat(factory, "Consolas", 11f);
			}
			catch
			{
				// Leave formats null; RefreshLines will retry on the next tick.
				if (_fmtNormal != null) { _fmtNormal.Dispose(); _fmtNormal = null; }
				if (_fmtBold != null) { _fmtBold.Dispose(); _fmtBold = null; }
				if (_fmtSmall != null) { _fmtSmall.Dispose(); _fmtSmall = null; }
			}
		}

		private static PanelLine Line(string text, SharpDX.Color color, TextFormat fmt, float gap)
		{
			return new PanelLine { Text = text, Color = color, Fmt = fmt, Gap = gap };
		}

		private static SharpDX.Color ToneColor(string tone)
		{
			switch (tone)
			{
				case "bullish": return ColBull;
				case "bearish": return ColBear;
				case "warning": return ColWarn;
				default: return ColNeutral;
			}
		}

		private List<PanelLine> BuildLines()
		{
			var lines = new List<PanelLine>();

			lines.Add(Line(_status ?? "", ColDim, _fmtSmall, 0f));

			if (_snapshot == null)
			{
				lines.Add(Line("Waiting for volumetric (Order Flow) data...", ColNeutral, _fmtNormal, 8f));
				return lines;
			}

			// Section headers are always drawn; only the content under them is conditional.
			// In a neutral state the auction bias content is omitted entirely - no
			// "Stand Aside", no "Rotational / Balance", no "(0/4)" score - the header
			// stays visible with nothing under it.
			lines.Add(Line("INTERPRETATION", ColSection, _fmtBold, 10f));
			if (_interp != null && !string.IsNullOrEmpty(_interp.Title))
			{
				lines.Add(Line(_interp.Title, ToneColor(_interp.Tone), _fmtBold, 3f));
				if (!string.IsNullOrEmpty(_interp.Action))
					lines.Add(Line("Action: " + _interp.Action, ToneColor(_interp.Tone), _fmtNormal, 1f));
				if (!string.IsNullOrEmpty(_interp.Details))
					lines.Add(Line(_interp.Details, ColDim, _fmtSmall, 1f));
			}

			var ctx = _ctx;

			lines.Add(Line("AUCTION BIAS", ColSection, _fmtBold, 10f));
			if (ctx != null && !IsNeutralState(ctx))
			{
				lines.Add(Line("Bias: " + ctx.Bias, ToneColor(ctx.BiasTone), _fmtBold, 3f));
				lines.Add(Line("Action: " + ctx.Action, ColWhite, _fmtNormal, 1f));
				lines.Add(Line("Structure: " + ctx.StructureBias + " (" +
					(ctx.StructureScore >= 0 ? "+" : "") + ctx.StructureScore + "/4)",
					ToneColor(ctx.StructureTone), _fmtSmall, 1f));
			}

			lines.Add(Line("ORDER FLOW MESSAGES", ColSection, _fmtBold, 10f));
			var sigs = ctx != null ? ctx.Signals : null;
			if (sigs != null && sigs.Count > 0)
			{
				var n = 0;
				foreach (var s in sigs)
				{
					if (n++ >= 5)
						break;

					var time = string.IsNullOrEmpty(s.Time) ? "" : "  [" + s.Time + "]";
					lines.Add(Line("- " + s.Title + time, ToneColor(s.Tone), _fmtBold, 3f));
				}
			}

			// Gate diagnostics: shows exactly which gate is blocking live signals. This is
			// the line that makes "why is the message list empty" answerable without a
			// debugger. Cur/req are the raw values the analyzer compared this pass.
			if (ShowGateDiagnostics && ctx != null)
			{
				var gateText = string.Format(
					"Gates: zones={0}  signals={1}  live={2} (trades {3}/{4}, vol {5}/{6}, levels {7}/{8})",
					ctx.ActiveZones,
					ctx.LiveSignalCount,
					ctx.GateLiveOpen ? "OPEN" : "closed",
					ctx.GateLiveTradesCurrent,
					ctx.GateLiveTradesRequired,
					ctx.GateLiveVolumeCurrent.ToString("F0"),
					ctx.GateLiveVolumeRequired.ToString("F0"),
					ctx.GateLiveLevelsCurrent,
					ctx.GateLiveLevelsRequired);

				lines.Add(Line(gateText, ColWarn, _fmtSmall, 6f));
			}

			return lines;
		}

		// Neutral state is now sourced from the analyzer, which computes it from the same
		// structure read and the same priority threshold used to pick the headline. If the
		// neutral rule changes in the analyzer, this panel follows without a second edit.
		private static bool IsNeutralState(OrderFlowContext ctx)
		{
			return ctx == null || ctx.IsNeutral;
		}

		private void RebuildLayouts(List<PanelLine> lines, int version)
		{
			if (_cachedLayouts != null)
			{
				foreach (var l in _cachedLayouts)
					l.Dispose();
				_cachedLayouts = null;
			}

			var layouts = new List<TextLayout>(lines.Count);
			float totalH = 0f;
			float maxW = 0f;

			foreach (var ln in lines)
			{
				var text = string.IsNullOrEmpty(ln.Text) ? " " : ln.Text;
				var layout = new TextLayout(Core.Globals.DirectWriteFactory, text, ln.Fmt, InnerWidth, 5000f);
				layout.WordWrapping = WordWrapping.Wrap;
				layouts.Add(layout);
				maxW = Math.Max(maxW, layout.Metrics.Width);
				totalH += ln.Gap + layout.Metrics.Height;
			}

			_cachedLayouts = layouts;
			_cachedPanelW = Math.Min(InnerWidth, maxW) + PadX * 2f;
			_cachedPanelH = totalH + PadY * 2f;
			_cachedVersion = version;
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);

			if (RenderTarget == null || ChartPanel == null || IsInHitTest)
				return;

			var lines = _lines;
			if (lines == null || lines.Count == 0)
				return;

			var version = _linesVersion;
			if (_cachedLayouts == null || _cachedVersion != version || _cachedLayouts.Count != lines.Count)
				RebuildLayouts(lines, version);

			// Clamp to the chart panel so a narrow chart does not push the panel off the left
			// edge. If the panel is wider than the chart, it anchors to the left edge and
			// clips on the right, which is the least-bad outcome.
			var panelX = ChartPanel.X + ChartPanel.W - _cachedPanelW - PanelOffsetX;
			if (panelX < ChartPanel.X)
				panelX = ChartPanel.X;

			var panelY = ChartPanel.Y + PanelOffsetY;
			var rect = new SharpDX.RectangleF(panelX, panelY, _cachedPanelW, _cachedPanelH);

			using (var bg = new SolidColorBrush(RenderTarget, new SharpDX.Color4(0.04f, 0.05f, 0.07f, 0.86f)))
			using (var border = new SolidColorBrush(RenderTarget, new SharpDX.Color4(0.35f, 0.38f, 0.45f, 1f)))
			{
				RenderTarget.FillRectangle(rect, bg);
				RenderTarget.DrawRectangle(rect, border, 1f);
			}

			var brushes = new Dictionary<SharpDX.Color, SolidColorBrush>();

			try
			{
				var y = panelY + PadY;
				for (var i = 0; i < _cachedLayouts.Count; i++)
				{
					y += lines[i].Gap;
					var brush = GetBrush(brushes, lines[i].Color);
					RenderTarget.DrawTextLayout(new SharpDX.Vector2(panelX + PadX, y), _cachedLayouts[i], brush, DrawTextOptions.NoSnap);
					y += _cachedLayouts[i].Metrics.Height;
				}
			}
			finally
			{
				foreach (var b in brushes.Values)
					b.Dispose();
			}
		}

		private SolidColorBrush GetBrush(Dictionary<SharpDX.Color, SolidColorBrush> cache, SharpDX.Color color)
		{
			SolidColorBrush brush;
			if (!cache.TryGetValue(color, out brush))
			{
				brush = new SolidColorBrush(RenderTarget, color);
				cache[color] = brush;
			}

			return brush;
		}
	}
}