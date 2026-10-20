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
	// - No multi-session handling: cumulative delta, volume baselines and level-volume stats
	//   are session-scoped by design and are never rolled over or persisted.
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

		[Range(0, 60000)]
		[Display(Name = "Update interval (ms)", Description = "Minimum milliseconds between recomputations (0 = every incoming tick).", Order = 1, GroupName = "Parameters")]
		public int UpdateIntervalMs { get; set; }

		// --- Order flow thresholds ---------------------------------------------
		// Every value is a ratio, a percentage of a rolling baseline, or a bar count. There are
		// no absolute trade-count or tick-distance thresholds, so the detector tracks volatility
		// as it moves within the session. Percent values are whole numbers (25 = 25%).

		[NinjaScriptProperty]
		[Range(1.5, 10.0)]
		[Display(Name = "Imbalance ratio", Description = "ask[i+1] must be at least this multiple of bid[i] on adjacent levels. A ratio, not a percent.", Order = 1, GroupName = "Order Flow")]
		public double ImbalanceRatio { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Imbalance run (% of avg range)", Description = "A run must span at least this % of the recent average bar range to count as stacked.", Order = 2, GroupName = "Order Flow")]
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
		[Display(Name = "Min live trades (% of avg)", Description = "The live bar must show at least this % of the recent average trade count before live signals fire.", Order = 5, GroupName = "Order Flow")]
		public double MinLiveTradesPct { get; set; }

		[NinjaScriptProperty]
		[Range(1.0, 8.0)]
		[Display(Name = "Absorption sigma", Description = "A level qualifies as absorption above mean + sigma x std-dev of recent level volumes.", Order = 6, GroupName = "Order Flow")]
		public double AbsorptionSigma { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Absorption edge (% of avg range)", Description = "The absorption level must sit within this % of the recent average bar range from the extreme.", Order = 7, GroupName = "Order Flow")]
		public double AbsorptionEdgePct { get; set; }

		[NinjaScriptProperty]
		[Range(50, 100)]
		[Display(Name = "Absorption side dominance (% of level)", Description = "One aggressor side must account for at least this % of the level's total volume.", Order = 8, GroupName = "Order Flow")]
		public double AbsorptionSidePct { get; set; }

		[NinjaScriptProperty]
		[Range(1, 25)]
		[Display(Name = "Zone age (bars)", Description = "How many bars a stacked-imbalance zone stays active before it is discarded.", Order = 9, GroupName = "Order Flow")]
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
				UpdateIntervalMs			= 2000;

				// Order Flow defaults. Kept in sync with FootprintSettings.
				ImbalanceRatio				= 3.0;
				ImbalanceRunRangePct		= 25;
				MinBarVolumePct				= 25;
				MinLevelVolumePct			= 5;
				MinLiveTradesPct			= 60;
				AbsorptionSigma				= 3.0;
				AbsorptionEdgePct			= 25;
				AbsorptionSidePct			= 60;
				MaxZoneAgeBars				= 8;
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
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 1)
				return;

			var nowMs = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;

			if (!IsFirstTickOfBar && UpdateIntervalMs > 0 && nowMs - _lastCalcMs < UpdateIntervalMs)
				return;

			_lastCalcMs = nowMs;

			try
			{
				if (_analyzer == null)
					_analyzer = new FootprintAnalyzer(BuildSettings());

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
			return new FootprintSettings
			{
				ImbalanceRatio			= ImbalanceRatio,
				ImbalanceRunRangePct	= ImbalanceRunRangePct,
				MinBarVolumePct			= MinBarVolumePct,
				MinLevelVolumePct		= MinLevelVolumePct,
				MinLiveTradesPct		= MinLiveTradesPct,
				AbsorptionSigma			= AbsorptionSigma,
				AbsorptionEdgePct		= AbsorptionEdgePct,
				AbsorptionSidePct		= AbsorptionSidePct,
				MaxZoneAgeBars			= MaxZoneAgeBars
			};
		}

		private void RefreshLines()
		{
			EnsureFormats();
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

			var factory = Core.Globals.DirectWriteFactory;
			_fmtNormal = new TextFormat(factory, "Consolas", 13f);
			_fmtBold = new TextFormat(factory, "Consolas", FontWeight.Bold, FontStyle.Normal, 13f);
			_fmtSmall = new TextFormat(factory, "Consolas", 11f);
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

			// Only sections with something actionable are drawn: no "nothing is happening"
			// placeholders. On a quiet bar the panel is the status line and the auction bias.
			if (_interp != null && !string.IsNullOrEmpty(_interp.Title))
			{
				lines.Add(Line("INTERPRETATION", ColSection, _fmtBold, 10f));
				lines.Add(Line(_interp.Title, ToneColor(_interp.Tone), _fmtBold, 3f));
				if (!string.IsNullOrEmpty(_interp.Action))
					lines.Add(Line("Action: " + _interp.Action, ToneColor(_interp.Tone), _fmtNormal, 1f));
				if (!string.IsNullOrEmpty(_interp.Details))
					lines.Add(Line(_interp.Details, ColDim, _fmtSmall, 1f));
			}

			var ctx = _ctx;
			if (ctx != null)
			{
				lines.Add(Line("AUCTION BIAS", ColSection, _fmtBold, 10f));
				lines.Add(Line("Bias: " + ctx.Bias, ToneColor(ctx.BiasTone), _fmtBold, 3f));
				lines.Add(Line("Action: " + ctx.Action, ColWhite, _fmtNormal, 1f));
				lines.Add(Line("Structure: " + ctx.StructureBias + " (" +
					(ctx.StructureScore >= 0 ? "+" : "") + ctx.StructureScore + "/4)",
					ToneColor(ctx.StructureTone), _fmtSmall, 1f));
			}

			var sigs = ctx != null ? ctx.Signals : null;
			if (sigs != null && sigs.Count > 0)
			{
				lines.Add(Line("ORDER FLOW MESSAGES", ColSection, _fmtBold, 10f));
				var n = 0;
				foreach (var s in sigs)
				{
					if (n++ >= 5)
						break;

					var time = string.IsNullOrEmpty(s.Time) ? "" : "  [" + s.Time + "]";
					lines.Add(Line("- " + s.Title + time, ToneColor(s.Tone), _fmtBold, 3f));
				}
			}

			return lines;
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

			var panelX = ChartPanel.X + ChartPanel.W - _cachedPanelW - PanelOffsetX;
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
