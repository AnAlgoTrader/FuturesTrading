#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
#endregion

namespace FootPrintTrader.Helper
{
	public class OrderFlowSignal
	{
		public string Type { get; set; } = "";
		public string Title { get; set; } = "";
		public string Tone { get; set; } = "neutral";
		public double Price { get; set; }
		public int Priority { get; set; } = 5;
		public string Time { get; set; } = "";

		// Raw unix seconds of the bar the signal came from. Used only as a tie-break when
		// ranking: on equal priority the newest signal wins. This is what stops a zone retest
		// born several bars ago from outranking what the developing bar is doing right now.
		public long BarTime { get; set; }
	}

	public class OrderFlowZone
	{
		public long BarStartTime { get; set; }
		public bool IsBuy { get; set; }
		public double LowPrice { get; set; }
		public double HighPrice { get; set; }
		public bool IsExhaustionAtBirth { get; set; }
		public int AgeBars { get; set; }
		public bool IsTrapped { get; set; }

		// Trapped is an *event* (price crossed through the zone), not a standing condition.
		// Reported once; the zone lingers only so a one-bar retest can still be flagged.
		public bool TrappedReported { get; set; }
	}

	public class OrderFlowContext
	{
		public string Bias { get; set; } = "Neutral";
		public string BiasTone { get; set; } = "neutral";
		public string Action { get; set; } = "";
		public double ActionPrice { get; set; }

		// Live Bar Metrics
		public double PocPrice { get; set; }
		public double CurrentDelta { get; set; }
		public double DeltaPercent { get; set; }
		public decimal CurrentVolume { get; set; }
		public string PocPosition { get; set; } = "-";
		public string PocMigration { get; set; } = "-";

		// Multi-bar / Session Structure
		public double CumulativeDelta { get; set; }
		public string DeltaDivergence { get; set; } = "None";

		// Recent average bar range, handed to the interpreter so "compressed range" can be
		// judged against the session's own volatility instead of a flat tick count.
		public double AvgBarRange { get; set; }

		// Standing read of which side is in control, independent of the one-off signals.
		// Shown on the panel even when a structural signal overrides the headline bias.
		public string StructureBias { get; set; } = "Rotational / Balance";
		public string StructureTone { get; set; } = "neutral";
		public int StructureScore { get; set; }

		// Active Signals
		public List<OrderFlowSignal> Signals { get; set; } = new List<OrderFlowSignal>();
	}

	public class FootprintAnalyzer
	{
		// User-tunable thresholds. Every one of these is either already a ratio between two
		// volumes, or a percentage of a rolling baseline (average bar volume / range / trade
		// count) so that it tracks volatility within the session. See FootprintSettings.
		private readonly FootprintSettings _settings;

		// --- Divergence ---
		private const int DivergenceWindowBars = 20;

		// Required CVD shortfall/excess, as a fraction of one average bar's volume. Scaling by
		// volume rather than by the CVD level keeps the threshold meaningful early in the
		// session, when cumulative delta is still small.
		private const double DivergenceCvdGapRatio = 0.15;

		// --- Rolling baselines ---
		private const int BaselineWindowBars = 20;
		private const int LevelVolumeWindow = 2000;

		// How long an absorption candidate stays alive waiting for price to reject the level:
		// the live bar plus this many completed bars.
		private const int AbsorptionMaxAgeBars = 2;

		// Floor for the "imbalance run spans N% of the bar" rule, so a very tight bar cannot
		// produce a 1-level "stack".
		private const int MinImbalanceRunLevels = 2;

		// --- Signal priority (lower = more important) ---
		// Ranked by (Priority, then newest BarTime first). Exhaustion is ranked with the
		// structural signals rather than with retests, so a live-bar exhaustion can win the
		// bias instead of being buried under zone retracements.
		public const int PriorityTrapped = 1;
		public const int PriorityAbsorption = 2;
		public const int PriorityExhaustion = 3;
		public const int PriorityDivergence = 4;
		public const int PriorityRetest = 5;
		public const int PriorityDeveloping = 6;

		// --- Debounce / structural ---
		private const int TrappedZoneMaxAgeBars = 1;

		// ============================================================
		// STATE (per analyzer instance: one chart, one indicator)
		// ============================================================
		private readonly List<OrderFlowZone> _activeZones = new List<OrderFlowZone>();
		private readonly HashSet<long> _processedBars = new HashSet<long>();
		private readonly Queue<decimal> _recentBarVolumes = new Queue<decimal>();
		private readonly Queue<double> _recentBarRanges = new Queue<double>();
		private readonly Queue<int> _recentBarTrades = new Queue<int>();
		private readonly Queue<decimal> _recentLevelVolumes = new Queue<decimal>();
		private OrderFlowContext _lastContext;
		private decimal _cumulativeDelta;

		// The three adaptive baselines everything relative is measured against.
		private decimal _avgBarVolume;
		private double _avgBarRange;
		private double _avgBarTrades;

		private double _levelVolMean;
		private double _levelVolStd;

		private readonly List<AbsorptionCandidate> _absorptionCandidates = new List<AbsorptionCandidate>();
		private readonly List<WindowPoint> _barWindow = new List<WindowPoint>();
		private string _divergence = "None";
		private double _divergencePrice;
		private long _divergenceBarStart;

		private sealed class AbsorptionCandidate
		{
			public bool SellerAbsorption;
			public double Price;
			public long BarStart;
		}

		// Per-bar summary of a completed bar. Shared by the divergence detector and by the
		// structural bias score, so both read the same rolling window of completed bars.
		private readonly struct WindowPoint
		{
			public readonly double High;
			public readonly double Low;
			public readonly double Close;
			public readonly double Poc;
			public readonly decimal Cvd;
			public readonly decimal Delta;
			public readonly long Start;

			public WindowPoint(double high, double low, double close, double poc, decimal cvd, decimal delta, long start)
			{
				High = high;
				Low = low;
				Close = close;
				Poc = poc;
				Cvd = cvd;
				Delta = delta;
				Start = start;
			}
		}

		// Structural bias: a standing read of which side is in control, independent of the
		// one-off signals. Structural signals (trapped/absorption/exhaustion/divergence) still
		// override it; this only decides the headline when nothing structural is firing.
		private const double StructureCvdSlopeRatio = 0.15;
		private const double StructurePersistenceBullRatio = 0.70;
		private const double StructurePersistenceBearRatio = 0.30;
		private const int StructurePersistenceWindowBars = 10;
		private const int StructurePocWindowBars = 3;
		private const double StructureCloseHighRatio = 0.70;
		private const double StructureCloseLowRatio = 0.30;
		private const int StructureStrongScore = 3;
		private const int StructureBullishScore = 2;
		private const int StructureBearishScore = -2;

		public FootprintAnalyzer(FootprintSettings settings = null)
		{
			_settings = settings ?? new FootprintSettings();
		}

		public OrderFlowContext AnalyzeContext(FootprintSnapshot snapshot, double tickSize, double currentPrice = 0)
		{
			if (snapshot == null)
				return _lastContext ?? new OrderFlowContext();

			var completed = snapshot.Completed ?? new FootprintBarData[0];
			var current = snapshot.Current;

			var bar = current ?? completed.LastOrDefault();
			FootprintBarData prevBar = current != null
				? completed.LastOrDefault()
				: (completed.Length > 1 ? completed[completed.Length - 2] : null);

			if (bar == null)
				return _lastContext ?? new OrderFlowContext();

			var dec = Decimals(tickSize);
			string P(double v) { return v.ToString("F" + dec, CultureInfo.InvariantCulture); }
			string T(long unixSeconds) { return FootprintRenderBuilder.FormatTime(unixSeconds); }
			var signals = new List<OrderFlowSignal>();
			var evalPrice = currentPrice > 0 ? currentPrice : bar.Close;

			var levels = bar.Levels ?? new FootprintLevel[0];
			var pocPrice = bar.Close;

			if (levels.Length > 0)
			{
				var pocLevel = levels.OrderByDescending(l => l.Total).First();
				pocPrice = pocLevel.Price;
			}

			var currentDelta = (double)bar.Delta;
			var currentVolume = bar.Volume;
			var deltaPercent = currentVolume > 0 ? currentDelta / (double)currentVolume * 100.0 : 0.0;

			var barRange = bar.High - bar.Low;
			string pocPosition;
			if (barRange > 1e-9)
			{
				var pos = (pocPrice - bar.Low) / barRange;
				pocPosition = pos >= 0.66 ? "High" : pos <= 0.33 ? "Low" : "Middle";
			}
			else pocPosition = "-";

			var pocMigration = "-";
			if (prevBar != null)
			{
				var prevPoc = prevBar.Close;
				var prevLevels = prevBar.Levels ?? new FootprintLevel[0];
				if (prevLevels.Length > 0)
					prevPoc = prevLevels.OrderByDescending(l => l.Total).First().Price;
				if (pocPrice > prevPoc + tickSize * 0.5) pocMigration = "Up";
				else if (pocPrice < prevPoc - tickSize * 0.5) pocMigration = "Down";
				else pocMigration = "Flat";
			}

			var liveDelta = current != null ? current.Delta : 0m;
			var cumulativeDelta = (double)(_cumulativeDelta + liveDelta);

			foreach (var compBar in completed)
			{
				if (_processedBars.Add(compBar.StartTime))
				{
					_cumulativeDelta += compBar.Delta;

					_avgBarVolume = RollMean(_recentBarVolumes, compBar.Volume, BaselineWindowBars);
					_avgBarRange = RollMean(_recentBarRanges, compBar.High - compBar.Low, BaselineWindowBars);
					_avgBarTrades = RollMean(_recentBarTrades, compBar.TradeCount, BaselineWindowBars);

					if (compBar.Levels != null)
					{
						foreach (var lvl in compBar.Levels)
							_recentLevelVolumes.Enqueue(Math.Max(lvl.BidVolume, lvl.AskVolume));
					}
					while (_recentLevelVolumes.Count > LevelVolumeWindow)
						_recentLevelVolumes.Dequeue();
					UpdateLevelVolumeStats();

					_barWindow.Add(new WindowPoint(
						compBar.High, compBar.Low, compBar.Close, PocOf(compBar),
						_cumulativeDelta, compBar.Delta, compBar.StartTime));
					while (_barWindow.Count > DivergenceWindowBars)
						_barWindow.RemoveAt(0);

					var barImbalances = compBar.Volume >= _avgBarVolume * (decimal)(_settings.MinBarVolumePct / 100.0)
						? DetectStackedImbalances(compBar, tickSize)
						: new List<ImbalanceRun>();
					var bRange = compBar.High - compBar.Low;

					foreach (var imb in barImbalances)
					{
						bool isExhaustion;
						if (imb.IsBuy)
						{
							isExhaustion = bRange > 1e-6 &&
										   (imb.EndP >= compBar.High - tickSize * 0.5 ||
											(imb.EndP - compBar.Low) / bRange >= 0.75) &&
										   compBar.Close < imb.EndP;
						}
						else
						{
							isExhaustion = bRange > 1e-6 &&
										   (imb.StartP <= compBar.Low + tickSize * 0.5 ||
											(imb.StartP - compBar.Low) / bRange <= 0.25) &&
										   compBar.Close > imb.StartP;
						}

						_activeZones.Add(new OrderFlowZone
						{
							BarStartTime = compBar.StartTime,
							IsBuy = imb.IsBuy,
							LowPrice = imb.StartP,
							HighPrice = imb.EndP,
							IsExhaustionAtBirth = isExhaustion,
							AgeBars = 0,
							IsTrapped = false
						});
					}
				}
			}

			ComputeDivergence(tickSize, _avgBarVolume);

			foreach (var zone in _activeZones)
			{
				zone.AgeBars = completed.Count(c => c.StartTime > zone.BarStartTime);
			}

			// A trapped zone has already failed as structure: it is kept only long enough for a
			// one-bar retest, so it stops feeding the interpreter's conflict gate for 8 bars.
			_activeZones.RemoveAll(z => z.AgeBars > (z.IsTrapped ? TrappedZoneMaxAgeBars : _settings.MaxZoneAgeBars));

			if (completed.Length > 0)
			{
				var oldestInWindow = completed.Min(c => c.StartTime);
				_processedBars.RemoveWhere(t => t < oldestInWindow);
			}

			foreach (var zone in _activeZones.OrderByDescending(z => z.BarStartTime))
			{
				if (zone.IsBuy)
				{
					if (evalPrice < zone.LowPrice - tickSize * 0.5)
					{
						zone.IsTrapped = true;
						if (!zone.TrappedReported)
						{
							zone.TrappedReported = true;
							signals.Add(new OrderFlowSignal
							{
								Type = "trapped_traders",
								Title = zone.IsExhaustionAtBirth ? "Trapped Longs (" + P(zone.LowPrice) + ")" : "Trapped Buyers (" + P(zone.LowPrice) + ")",
								Tone = "bearish",
								Price = zone.LowPrice,
								Priority = PriorityTrapped,
								Time = T(zone.BarStartTime),
								BarTime = zone.BarStartTime
							});
						}
					}
					else if (Math.Abs(evalPrice - zone.LowPrice) <= tickSize * 1.5 ||
							 (evalPrice >= zone.LowPrice && evalPrice <= zone.HighPrice + tickSize * 0.5))
					{
						signals.Add(new OrderFlowSignal
						{
							Type = "stacked_imbalance",
							Title = "Retesting Buy Zone (" + P(zone.LowPrice) + ")",
							Tone = "bullish",
							Price = zone.LowPrice,
							Priority = PriorityRetest,
							Time = T(zone.BarStartTime),
							BarTime = zone.BarStartTime
						});
					}
					else if (evalPrice > zone.HighPrice && zone.AgeBars <= 2 && !zone.IsExhaustionAtBirth && !zone.IsTrapped)
					{
						signals.Add(new OrderFlowSignal
						{
							Type = "stacked_imbalance",
							Title = "Buyer Support (" + P(zone.LowPrice) + ")",
							Tone = "bullish",
							Price = zone.LowPrice,
							Priority = PriorityRetest,
							Time = T(zone.BarStartTime),
							BarTime = zone.BarStartTime
						});
					}
				}
				else
				{
					if (evalPrice > zone.HighPrice + tickSize * 0.5)
					{
						zone.IsTrapped = true;
						if (!zone.TrappedReported)
						{
							zone.TrappedReported = true;
							signals.Add(new OrderFlowSignal
							{
								Type = "trapped_traders",
								Title = zone.IsExhaustionAtBirth ? "Trapped Shorts (" + P(zone.HighPrice) + ")" : "Trapped Sellers (" + P(zone.HighPrice) + ")",
								Tone = "bullish",
								Price = zone.HighPrice,
								Priority = PriorityTrapped,
								Time = T(zone.BarStartTime),
								BarTime = zone.BarStartTime
							});
						}
					}
					else if (Math.Abs(evalPrice - zone.HighPrice) <= tickSize * 1.5 ||
							 (evalPrice <= zone.HighPrice && evalPrice >= zone.LowPrice - tickSize * 0.5))
					{
						signals.Add(new OrderFlowSignal
						{
							Type = "stacked_imbalance",
							Title = "Retesting Sell Zone (" + P(zone.HighPrice) + ")",
							Tone = "bearish",
							Price = zone.HighPrice,
							Priority = PriorityRetest,
							Time = T(zone.BarStartTime),
							BarTime = zone.BarStartTime
						});
					}
					else if (evalPrice < zone.LowPrice && zone.AgeBars <= 2 && !zone.IsExhaustionAtBirth && !zone.IsTrapped)
					{
						signals.Add(new OrderFlowSignal
						{
							Type = "stacked_imbalance",
							Title = "Seller Resistance (" + P(zone.HighPrice) + ")",
							Tone = "bearish",
							Price = zone.HighPrice,
							Priority = PriorityRetest,
							Time = T(zone.BarStartTime),
							BarTime = zone.BarStartTime
						});
					}
				}
			}

			if (current != null && _avgBarTrades > 0 &&
				current.TradeCount >= _avgBarTrades * _settings.MinLiveTradesPct &&
				current.Volume >= _avgBarVolume * (decimal)(_settings.MinBarVolumePct / 100.0))
			{
				var liveImbalances = DetectStackedImbalances(current, tickSize);
				var liveRange = current.High - current.Low;

				foreach (var imb in liveImbalances)
				{
					if (imb.IsBuy)
					{
						bool isAtHigh = liveRange > 1e-6 &&
										(imb.EndP >= current.High - tickSize * 0.5 ||
										 (imb.EndP - current.Low) / liveRange >= 0.75);

						if (isAtHigh && (evalPrice < imb.EndP || current.Close < imb.EndP))
						{
							signals.Add(new OrderFlowSignal
							{
								Type = "exhaustion",
								Title = "Buying Exhaustion at High (" + P(imb.EndP) + ")",
								Tone = "bearish",
								Price = imb.EndP,
								Priority = PriorityExhaustion,
								Time = T(current.StartTime),
								BarTime = current.StartTime
							});
						}
						else if (evalPrice >= imb.EndP)
						{
							signals.Add(new OrderFlowSignal
							{
								Type = "stacked_imbalance",
								Title = "Stacked Buy Imbalance (" + P(imb.EndP) + ")",
								Tone = "bullish",
								Price = imb.EndP,
								Priority = PriorityDeveloping,
								Time = T(current.StartTime),
								BarTime = current.StartTime
							});
						}
					}
					else
					{
						bool isAtLow = liveRange > 1e-6 &&
									   (imb.StartP <= current.Low + tickSize * 0.5 ||
										(imb.StartP - current.Low) / liveRange <= 0.25);

						if (isAtLow && (evalPrice > imb.StartP || current.Close > imb.StartP))
						{
							signals.Add(new OrderFlowSignal
							{
								Type = "exhaustion",
								Title = "Selling Exhaustion at Low (" + P(imb.StartP) + ")",
								Tone = "bullish",
								Price = imb.StartP,
								Priority = PriorityExhaustion,
								Time = T(current.StartTime),
								BarTime = current.StartTime
							});
						}
						else if (evalPrice <= imb.StartP)
						{
							signals.Add(new OrderFlowSignal
							{
								Type = "stacked_imbalance",
								Title = "Stacked Sell Imbalance (" + P(imb.StartP) + ")",
								Tone = "bearish",
								Price = imb.StartP,
								Priority = PriorityDeveloping,
								Time = T(current.StartTime),
								BarTime = current.StartTime
							});
						}
					}
				}
			}

			var absorptionThreshold = _levelVolMean + _settings.AbsorptionSigma * _levelVolStd;

			// "Near the extreme" scales with the recent average bar range, not with a fixed tick
			// count, so it means the same thing in a quiet and a volatile session. Floor of one
			// tick so a collapsed baseline cannot make the whole bar an "edge".
			var absorptionDepth = Math.Max(tickSize, _avgBarRange * (_settings.AbsorptionEdgePct / 100.0));

			// Detection needs enough trades on the live bar to make a level meaningful, so it is
			// gated. Pruning and confirmation are NOT: they used to sit inside the same guard,
			// which made confirmed absorption signals blink off at the start of every bar.
			var canDetectAbsorption = absorptionThreshold > 0 && levels.Length > 0 &&
				_avgBarTrades > 0 && bar.TradeCount >= _avgBarTrades * _settings.MinLiveTradesPct &&
				bar.Volume >= _avgBarVolume * (decimal)(_settings.MinBarVolumePct / 100.0);

			var absorptionSide = (decimal)(_settings.AbsorptionSidePct / 100.0);

			if (canDetectAbsorption)
			{
				foreach (var lvl in levels)
				{
					if ((double)lvl.AskVolume >= absorptionThreshold &&
						lvl.Total > 0 && lvl.AskVolume >= lvl.Total * absorptionSide &&
						lvl.Price >= bar.High - absorptionDepth + 1e-9 &&
						!_absorptionCandidates.Any(c => c.SellerAbsorption && c.BarStart == bar.StartTime && Math.Abs(c.Price - lvl.Price) < tickSize * 0.5))
					{
						_absorptionCandidates.Add(new AbsorptionCandidate { SellerAbsorption = true, Price = lvl.Price, BarStart = bar.StartTime });
					}

					if ((double)lvl.BidVolume >= absorptionThreshold &&
						lvl.Total > 0 && lvl.BidVolume >= lvl.Total * absorptionSide &&
						lvl.Price <= bar.Low + absorptionDepth + 1e-9 &&
						!_absorptionCandidates.Any(c => !c.SellerAbsorption && c.BarStart == bar.StartTime && Math.Abs(c.Price - lvl.Price) < tickSize * 0.5))
					{
						_absorptionCandidates.Add(new AbsorptionCandidate { SellerAbsorption = false, Price = lvl.Price, BarStart = bar.StartTime });
					}
				}
			}

			// Explicit lifetime instead of the old accidental one-bar window. A candidate gets
			// the live bar plus AbsorptionMaxAgeBars completed bars for price to reject it.
			_absorptionCandidates.RemoveAll(c =>
				completed.Count(b => b.StartTime > c.BarStart) > AbsorptionMaxAgeBars);

			foreach (var c in _absorptionCandidates)
			{
				var rejected = c.SellerAbsorption
					? evalPrice <= c.Price - absorptionDepth
					: evalPrice >= c.Price + absorptionDepth;
				if (!rejected) continue;

				signals.Add(new OrderFlowSignal
				{
					Type = "absorption",
					Title = (c.SellerAbsorption ? "Seller" : "Buyer") + " Absorption (" + P(c.Price) + ")",
					Tone = c.SellerAbsorption ? "bearish" : "bullish",
					Price = c.Price,
					Priority = PriorityAbsorption,
					Time = T(c.BarStart),
					BarTime = c.BarStart
				});
			}

			var deltaDivergence = _divergence;
			if (deltaDivergence != "None")
			{
				signals.Add(new OrderFlowSignal
				{
					Type = "divergence",
					Title = deltaDivergence + " Divergence (" + P(_divergencePrice) + ")",
					Tone = deltaDivergence == "Bearish" ? "bearish" : "bullish",
					Price = _divergencePrice,
					Priority = PriorityDivergence,
					Time = T(_divergenceBarStart),
					BarTime = _divergenceBarStart
				});
			}

			// Rank: priority first, then newest bar first. The ThenByDescending is what makes a
			// live-bar signal beat an equally-prioritised zone retest that was born earlier.
			var distinctSignals = signals
				.GroupBy(s => s.Type + ":" + s.Tone + ":" + s.Title)
				.Select(g => g.First())
				.OrderBy(s => s.Priority)
				.ThenByDescending(s => s.BarTime)
				.Take(5)
				.ToList();

			// Structural bias is the base layer; only trapped / absorption / exhaustion /
			// divergence can override it. Retests and developing imbalances stay in the signal
			// list but no longer get to set the headline, so the panel reports what the market
			// is actually doing between events. A neutral structure produces no action message
			// at all: the panel leaves the auction bias content empty rather than printing a
			// stand-aside line.
			var structure = ComputeStructureBias(tickSize, _avgBarVolume);
			var top = distinctSignals.FirstOrDefault(s => s.Priority <= PriorityDivergence);

			string bias;
			string biasTone;
			string action;
			double actionPrice;

			if (top == null)
			{
				var pocLevel = pocPrice.ToString("F" + dec, CultureInfo.InvariantCulture);
				bias = structure.Bias;
				biasTone = structure.Tone;
				actionPrice = pocPrice;
				action = structure.Tone == "bullish" ? "Buy pullbacks to " + pocLevel
					: structure.Tone == "bearish" ? "Sell rallies to " + pocLevel
					: "";
			}
			else
			{
				biasTone = top.Tone;
				actionPrice = top.Price > 0 ? top.Price : pocPrice;
				var level = actionPrice.ToString("F" + dec, CultureInfo.InvariantCulture);

				switch (top.Type)
				{
					case "trapped_traders":
						bias = top.Tone == "bearish" ? "Trapped Buyers" : "Trapped Sellers";
						action = (top.Tone == "bearish" ? "Short on retest of " : "Long on retest of ") + level;
						break;
					case "absorption":
						bias = top.Tone == "bearish" ? "Seller Absorption" : "Buyer Absorption";
						action = (top.Tone == "bearish" ? "Short on retest of " : "Long on retest of ") + level;
						break;
					case "divergence":
						bias = top.Tone == "bearish" ? "Bearish Divergence" : "Bullish Divergence";
						action = (top.Tone == "bearish" ? "Short on retest of " : "Long on retest of ") + level;
						break;
					case "exhaustion":
						bias = top.Tone == "bearish" ? "Buying Exhaustion" : "Selling Exhaustion";
						action = (top.Tone == "bearish" ? "Short on retest of " : "Long on retest of ") + level;
						break;
					default:
						bias = top.Tone == "bearish" ? "Bearish Initiative" : "Bullish Initiative";
						action = (top.Tone == "bearish" ? "Sell rallies to " : "Buy pullbacks to ") + level;
						break;
				}
			}

			var result = new OrderFlowContext
			{
				Bias = bias,
				BiasTone = biasTone,
				Action = action,
				ActionPrice = actionPrice,
				PocPrice = pocPrice,
				CurrentDelta = currentDelta,
				DeltaPercent = deltaPercent,
				CurrentVolume = currentVolume,
				PocPosition = pocPosition,
				PocMigration = pocMigration,
				CumulativeDelta = cumulativeDelta,
				AvgBarRange = _avgBarRange,
				DeltaDivergence = deltaDivergence,
				StructureBias = structure.Bias,
				StructureTone = structure.Tone,
				StructureScore = structure.Score,
				Signals = distinctSignals
			};

			_lastContext = result;
			return result;
		}

		private static decimal RollMean(Queue<decimal> q, decimal value, int window)
		{
			q.Enqueue(value);
			while (q.Count > window)
				q.Dequeue();
			return q.Count > 0 ? q.Average() : value;
		}

		private static double RollMean(Queue<double> q, double value, int window)
		{
			q.Enqueue(value);
			while (q.Count > window)
				q.Dequeue();
			return q.Count > 0 ? q.Average() : value;
		}

		private static double RollMean(Queue<int> q, int value, int window)
		{
			q.Enqueue(value);
			while (q.Count > window)
				q.Dequeue();
			return q.Count > 0 ? q.Average() : value;
		}

		private void UpdateLevelVolumeStats()
		{
			var n = _recentLevelVolumes.Count;
			if (n == 0)
			{
				_levelVolMean = 0;
				_levelVolStd = 0;
				return;
			}

			double sum = 0;
			foreach (var v in _recentLevelVolumes)
				sum += (double)v;
			var mean = sum / n;

			double sq = 0;
			foreach (var v in _recentLevelVolumes)
			{
				var d = (double)v - mean;
				sq += d * d;
			}

			_levelVolMean = mean;
			_levelVolStd = Math.Sqrt(sq / n);
		}

		private void ComputeDivergence(double tickSize, decimal baselineBarVolume)
		{
			_divergence = "None";
			_divergencePrice = 0;
			_divergenceBarStart = 0;

			var n = _barWindow.Count;
			if (n < 3 || baselineBarVolume <= 0)
				return;

			var latest = _barWindow[n - 1];

			var priorHigh = double.MinValue;
			var priorHighCvd = 0m;
			var priorLow = double.MaxValue;
			var priorLowCvd = 0m;

			for (var i = 0; i < n - 1; i++)
			{
				var p = _barWindow[i];
				if (p.High >= priorHigh) { priorHigh = p.High; priorHighCvd = p.Cvd; }
				if (p.Low <= priorLow) { priorLow = p.Low; priorLowCvd = p.Cvd; }
			}

			var bearGap = (decimal)((double)baselineBarVolume * DivergenceCvdGapRatio);
			var bullGap = bearGap;

			if (latest.High > priorHigh + tickSize * 0.5 &&
				latest.Cvd < priorHighCvd - bearGap &&
				latest.Delta <= 0)
			{
				_divergence = "Bearish";
				_divergencePrice = latest.High;
				_divergenceBarStart = latest.Start;
			}
			else if (latest.Low < priorLow - tickSize * 0.5 &&
					 latest.Cvd > priorLowCvd + bullGap &&
					 latest.Delta >= 0)
			{
				_divergence = "Bullish";
				_divergencePrice = latest.Low;
				_divergenceBarStart = latest.Start;
			}
		}

		private static double PocOf(FootprintBarData bar)
		{
			var levels = bar != null ? bar.Levels : null;
			if (levels == null || levels.Length == 0)
				return bar != null ? bar.Close : 0;

			var best = levels[0];
			foreach (var l in levels)
				if (l.Total > best.Total)
					best = l;
			return best.Price;
		}

		private struct StructureRead
		{
			public readonly string Bias;
			public readonly string Tone;
			public readonly int Score;

			public StructureRead(string bias, string tone, int score)
			{
				Bias = bias;
				Tone = tone;
				Score = score;
			}
		}

		// Four independent readings over the completed-bar window, each worth +/-1:
		//   1. CVD slope across the window, thresholded against average bar volume so it does
		//      not collapse to zero early in the session.
		//   2. Delta persistence over the last N completed bars (initiative, not one spike).
		//   3. POC migration over the last few completed bars (acceptance moving with price).
		//   4. Close location within the last completed bar's range (who controlled the close).
		// |score| >= 3 is reported as "Strong".
		private StructureRead ComputeStructureBias(double tickSize, decimal baselineBarVolume)
		{
			var w = _barWindow;
			var n = w.Count;
			if (n < 2 || baselineBarVolume <= 0)
				return new StructureRead("Rotational / Balance", "neutral", 0);

			var score = 0;

			// 1. CVD slope
			var cvdThreshold = (decimal)((double)baselineBarVolume * StructureCvdSlopeRatio);
			var cvdDelta = w[n - 1].Cvd - w[0].Cvd;
			if (cvdDelta > cvdThreshold) score++;
			else if (cvdDelta < -cvdThreshold) score--;

			// 2. Delta persistence
			var persistCount = Math.Min(StructurePersistenceWindowBars, n);
			var positive = 0;
			for (var i = n - persistCount; i < n; i++)
				if (w[i].Delta > 0) positive++;

			var bullVotes = (int)Math.Ceiling(persistCount * StructurePersistenceBullRatio);
			var bearVotes = (int)Math.Floor(persistCount * StructurePersistenceBearRatio);
			if (positive >= bullVotes) score++;
			else if (positive <= bearVotes) score--;

			// 3. POC migration
			var pocCount = Math.Min(StructurePocWindowBars, n);
			if (pocCount >= 2)
			{
				var pocMove = w[n - 1].Poc - w[n - pocCount].Poc;
				if (pocMove > tickSize * 0.5) score++;
				else if (pocMove < -tickSize * 0.5) score--;
			}

			// 4. Close location
			var last = w[n - 1];
			var range = last.High - last.Low;
			if (range > tickSize * 0.5)
			{
				var loc = (last.Close - last.Low) / range;
				if (loc >= StructureCloseHighRatio) score++;
				else if (loc <= StructureCloseLowRatio) score--;
			}

			if (score >= StructureBullishScore)
				return new StructureRead(score >= StructureStrongScore ? "Strong Bullish" : "Bullish", "bullish", score);
			if (score <= StructureBearishScore)
				return new StructureRead(score <= -StructureStrongScore ? "Strong Bearish" : "Bearish", "bearish", score);

			return new StructureRead("Rotational / Balance", "neutral", score);
		}

		private sealed class ImbalanceRun
		{
			public bool IsBuy;
			public double StartP;
			public double EndP;
			public int Run;
		}

		private List<ImbalanceRun> DetectStackedImbalances(FootprintBarData bar, double tickSize)
		{
			var list = new List<ImbalanceRun>();
			var levels = bar != null ? bar.Levels : null;
			if (levels == null || levels.Length < 2) return list;

			if (!IsSortedAscending(levels))
				levels = levels.OrderBy(l => l.Price).ToArray();

			// How many consecutive levels count as "stacked" is a percentage of the recent
			// average bar range, not a fixed count. Three ticks of imbalance means something
			// different on a 4-point bar than on a 40-point one. The average range (rather than
			// this bar's own) keeps the threshold stable on a developing bar whose range is
			// still small. Floored so a tight bar cannot produce a one-level "stack".
			var requiredRun = MinImbalanceRunLevels;
			if (tickSize > 0 && _avgBarRange > 0)
			{
				requiredRun = Math.Max(MinImbalanceRunLevels,
					(int)Math.Ceiling(_avgBarRange / tickSize * (_settings.ImbalanceRunRangePct / 100.0)));
			}

			var avgLevelVol = bar.Volume / levels.Length;
			var minLevelVol = avgLevelVol * (decimal)(_settings.MinLevelVolumePct / 100.0);
			var imbalanceRatio = (decimal)_settings.ImbalanceRatio;

			var buyImb = new bool[levels.Length];
			var sellImb = new bool[levels.Length];

			for (var i = 0; i < levels.Length - 1; i++)
			{
				var lLow = levels[i];
				var lHigh = levels[i + 1];

				if (lHigh.AskVolume >= minLevelVol &&
					(lLow.BidVolume == 0 ||
					 lHigh.AskVolume >= lLow.BidVolume * imbalanceRatio))
				{
					buyImb[i + 1] = true;
				}

				if (lLow.BidVolume >= minLevelVol &&
					(lHigh.AskVolume == 0 ||
					 lLow.BidVolume >= lHigh.AskVolume * imbalanceRatio))
				{
					sellImb[i] = true;
				}
			}

			var run = 0;
			for (var i = 0; i < levels.Length; i++)
			{
				if (buyImb[i]) run++;
				else
				{
					if (run >= requiredRun)
						list.Add(new ImbalanceRun { IsBuy = true, StartP = levels[i - run].Price, EndP = levels[i - 1].Price, Run = run });
					run = 0;
				}
			}

			if (run >= requiredRun)
				list.Add(new ImbalanceRun { IsBuy = true, StartP = levels[levels.Length - run].Price, EndP = levels[levels.Length - 1].Price, Run = run });

			run = 0;
			for (var i = 0; i < levels.Length; i++)
			{
				if (sellImb[i]) run++;
				else
				{
					if (run >= requiredRun)
						list.Add(new ImbalanceRun { IsBuy = false, StartP = levels[i - run].Price, EndP = levels[i - 1].Price, Run = run });
					run = 0;
				}
			}

			if (run >= requiredRun)
				list.Add(new ImbalanceRun { IsBuy = false, StartP = levels[levels.Length - run].Price, EndP = levels[levels.Length - 1].Price, Run = run });

			return list;
		}

		private static bool IsSortedAscending(FootprintLevel[] levels)
		{
			for (var i = 1; i < levels.Length; i++)
				if (levels[i].Price < levels[i - 1].Price)
					return false;
			return true;
		}

		public static int Decimals(double tickSize)
		{
			var s = tickSize.ToString(CultureInfo.InvariantCulture);
			var i = s.IndexOf('.');
			return i < 0 ? 0 : s.Length - i - 1;
		}
	}
}
