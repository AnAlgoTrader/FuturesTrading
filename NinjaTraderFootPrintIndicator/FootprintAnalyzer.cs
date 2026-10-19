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

		// Active Signals
		public List<OrderFlowSignal> Signals { get; set; } = new List<OrderFlowSignal>();
	}

	public static class FootprintAnalyzer
	{
		// --- Diagonal imbalance (relative by nature - adjacent levels) ---
		private const decimal ImbalanceRatio = 3.0m;
		private const int StackedLevelsRequired = 3;

		// --- Volume floors (percentage-based, relative to recent activity) ---
		private const double MinImbalanceVolRatio = 0.05;
		private const double MinBarVolumeBaselineRatio = 0.25;

		// --- Absorption ---
		private const double AbsorptionLevelSigma = 3.0;
		private const int AbsorptionDepthTicks = 2;
		private const double AbsorptionSideDominance = 0.6;

		// --- Divergence ---
		private const int DivergenceWindowBars = 20;
		private const double DivergenceMinCvdGapRatio = 0.10;

		// --- Unfinished auction ---
		private const int MaxUnfinishedAuctions = 4;

		// --- Rolling baselines ---
		private const int BaselineWindowBars = 20;
		private const int LevelVolumeWindow = 2000;

		// --- Signal priority (lower = more important) ---
		public const int PriorityTrapped = 1;
		public const int PriorityAbsorption = 2;
		public const int PriorityDivergence = 3;
		public const int PriorityRetest = 4;
		public const int PriorityDeveloping = 5;
		public const int PriorityUnfinished = 6;

		// --- Debounce / structural ---
		private const int MinLiveTradesForSignal = 250;
		private const int MaxZoneAgeBars = 8;

		// ============================================================
		// STATE
		// ============================================================
		private static readonly object _syncLock = new object();
		private static readonly List<OrderFlowZone> _activeZones = new List<OrderFlowZone>();
		private static readonly HashSet<long> _processedBars = new HashSet<long>();
		private static readonly Queue<decimal> _recentBarVolumes = new Queue<decimal>();
		private static readonly Queue<decimal> _recentLevelVolumes = new Queue<decimal>();
		private static OrderFlowContext _lastContext;
		private static decimal _sessionCumulativeDelta;
		private static decimal _baselineBarVolume;
		private static double _levelVolMean;
		private static double _levelVolStd;

		private static readonly List<AbsorptionCandidate> _absorptionCandidates = new List<AbsorptionCandidate>();
		private static readonly List<UnfinishedAuction> _unfinishedAuctions = new List<UnfinishedAuction>();
		private static readonly List<DivergencePoint> _divergenceWindow = new List<DivergencePoint>();
		private static string _divergence = "None";
		private static double _divergencePrice;
		private static long _divergenceBarStart;

		private sealed class AbsorptionCandidate
		{
			public bool SellerAbsorption;
			public double Price;
			public long BarStart;
		}

		private sealed class UnfinishedAuction
		{
			public bool IsHigh;
			public double Price;
			public long BarStart;
		}

		private readonly struct DivergencePoint
		{
			public readonly double High;
			public readonly double Low;
			public readonly decimal Cvd;
			public readonly decimal Delta;
			public readonly long Start;

			public DivergencePoint(double high, double low, decimal cvd, decimal delta, long start)
			{
				High = high;
				Low = low;
				Cvd = cvd;
				Delta = delta;
				Start = start;
			}
		}

		public static OrderFlowContext AnalyzeContext(FootprintSnapshot snapshot, double tickSize, double currentPrice = 0)
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
			var cumulativeDelta = (double)(_sessionCumulativeDelta + liveDelta);

			lock (_syncLock)
			{
				foreach (var compBar in completed)
				{
					if (_processedBars.Add(compBar.StartTime))
					{
						_sessionCumulativeDelta += compBar.Delta;

						_recentBarVolumes.Enqueue(compBar.Volume);
						while (_recentBarVolumes.Count > BaselineWindowBars)
							_recentBarVolumes.Dequeue();
						_baselineBarVolume = _recentBarVolumes.Count > 0
							? _recentBarVolumes.Average()
							: compBar.Volume;

						if (compBar.Levels != null)
						{
							foreach (var lvl in compBar.Levels)
								_recentLevelVolumes.Enqueue(Math.Max(lvl.BidVolume, lvl.AskVolume));
						}
						while (_recentLevelVolumes.Count > LevelVolumeWindow)
							_recentLevelVolumes.Dequeue();
						UpdateLevelVolumeStats();

						_divergenceWindow.Add(new DivergencePoint(compBar.High, compBar.Low, _sessionCumulativeDelta, compBar.Delta, compBar.StartTime));
						while (_divergenceWindow.Count > DivergenceWindowBars)
							_divergenceWindow.RemoveAt(0);

						_unfinishedAuctions.RemoveAll(u =>
							u.BarStart < compBar.StartTime &&
							u.Price >= compBar.Low - tickSize * 0.5 &&
							u.Price <= compBar.High + tickSize * 0.5);
						DetectUnfinishedAuctions(compBar, tickSize);

						var barImbalances = compBar.Volume >= _baselineBarVolume * (decimal)MinBarVolumeBaselineRatio
							? DetectStackedImbalances(compBar, _baselineBarVolume)
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

				ComputeDivergence(tickSize);

				foreach (var zone in _activeZones)
				{
					zone.AgeBars = completed.Count(c => c.StartTime > zone.BarStartTime);
				}

				_activeZones.RemoveAll(z => z.AgeBars > MaxZoneAgeBars);

				if (completed.Length > 0)
				{
					var oldestInWindow = completed.Min(c => c.StartTime);
					_processedBars.RemoveWhere(t => t < oldestInWindow);
					_unfinishedAuctions.RemoveAll(u => u.BarStart < oldestInWindow);
				}

				foreach (var zone in _activeZones.OrderByDescending(z => z.BarStartTime))
				{
					if (zone.IsBuy)
					{
						if (evalPrice < zone.LowPrice - tickSize * 0.5)
						{
							zone.IsTrapped = true;
							signals.Add(new OrderFlowSignal
							{
								Type = "trapped_traders",
								Title = zone.IsExhaustionAtBirth ? "Trapped Longs (" + P(zone.LowPrice) + ")" : "Trapped Buyers (" + P(zone.LowPrice) + ")",
								Tone = "bearish",
								Price = zone.LowPrice,
								Priority = PriorityTrapped,
								Time = T(zone.BarStartTime)
							});
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
								Time = T(zone.BarStartTime)
							});
						}
						else if (evalPrice > zone.HighPrice && zone.AgeBars <= 2 && !zone.IsExhaustionAtBirth)
						{
							signals.Add(new OrderFlowSignal
							{
								Type = "stacked_imbalance",
								Title = "Buyer Support (" + P(zone.LowPrice) + ")",
								Tone = "bullish",
								Price = zone.LowPrice,
								Priority = PriorityRetest,
								Time = T(zone.BarStartTime)
							});
						}
					}
					else
					{
						if (evalPrice > zone.HighPrice + tickSize * 0.5)
						{
							zone.IsTrapped = true;
							signals.Add(new OrderFlowSignal
							{
								Type = "trapped_traders",
								Title = zone.IsExhaustionAtBirth ? "Trapped Shorts (" + P(zone.HighPrice) + ")" : "Trapped Sellers (" + P(zone.HighPrice) + ")",
								Tone = "bullish",
								Price = zone.HighPrice,
								Priority = PriorityTrapped,
								Time = T(zone.BarStartTime)
							});
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
								Time = T(zone.BarStartTime)
							});
						}
						else if (evalPrice < zone.LowPrice && zone.AgeBars <= 2 && !zone.IsExhaustionAtBirth)
						{
							signals.Add(new OrderFlowSignal
							{
								Type = "stacked_imbalance",
								Title = "Seller Resistance (" + P(zone.HighPrice) + ")",
								Tone = "bearish",
								Price = zone.HighPrice,
								Priority = PriorityRetest,
								Time = T(zone.BarStartTime)
							});
						}
					}
				}
			}

			if (current != null && current.TradeCount >= MinLiveTradesForSignal &&
				current.Volume >= _baselineBarVolume * (decimal)MinBarVolumeBaselineRatio)
			{
				var liveImbalances = DetectStackedImbalances(current, _baselineBarVolume);
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
								Priority = PriorityRetest,
								Time = T(current.StartTime)
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
								Time = T(current.StartTime)
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
								Priority = PriorityRetest,
								Time = T(current.StartTime)
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
								Time = T(current.StartTime)
							});
						}
					}
				}
			}

			var absorptionThreshold = _levelVolMean + AbsorptionLevelSigma * _levelVolStd;

			if (absorptionThreshold > 0 && levels.Length > 0 &&
				bar.TradeCount >= MinLiveTradesForSignal &&
				bar.Volume >= _baselineBarVolume * (decimal)MinBarVolumeBaselineRatio)
			{
				var depth = tickSize * AbsorptionDepthTicks;

				lock (_syncLock)
				{
					foreach (var lvl in levels)
					{
						if ((double)lvl.AskVolume >= absorptionThreshold &&
							lvl.Total > 0 && lvl.AskVolume >= lvl.Total * (decimal)AbsorptionSideDominance &&
							lvl.Price >= bar.High - depth + 1e-9 &&
							!_absorptionCandidates.Any(c => c.SellerAbsorption && c.BarStart == bar.StartTime && Math.Abs(c.Price - lvl.Price) < tickSize * 0.5))
						{
							_absorptionCandidates.Add(new AbsorptionCandidate { SellerAbsorption = true, Price = lvl.Price, BarStart = bar.StartTime });
						}

						if ((double)lvl.BidVolume >= absorptionThreshold &&
							lvl.Total > 0 && lvl.BidVolume >= lvl.Total * (decimal)AbsorptionSideDominance &&
							lvl.Price <= bar.Low + depth + 1e-9 &&
							!_absorptionCandidates.Any(c => !c.SellerAbsorption && c.BarStart == bar.StartTime && Math.Abs(c.Price - lvl.Price) < tickSize * 0.5))
						{
							_absorptionCandidates.Add(new AbsorptionCandidate { SellerAbsorption = false, Price = lvl.Price, BarStart = bar.StartTime });
						}
					}

					var prevStart = completed.Length > 0 ? completed[completed.Length - 1].StartTime : bar.StartTime;
					_absorptionCandidates.RemoveAll(c => c.BarStart != bar.StartTime && c.BarStart != prevStart);

					foreach (var c in _absorptionCandidates)
					{
						var rejected = c.SellerAbsorption
							? evalPrice <= c.Price - depth
							: evalPrice >= c.Price + depth;
						if (!rejected) continue;

						signals.Add(new OrderFlowSignal
						{
							Type = "absorption",
							Title = (c.SellerAbsorption ? "Seller" : "Buyer") + " Absorption (" + P(c.Price) + ")",
							Tone = c.SellerAbsorption ? "bearish" : "bullish",
							Price = c.Price,
							Priority = PriorityAbsorption,
							Time = T(c.BarStart)
						});
					}
				}
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
					Time = T(_divergenceBarStart)
				});
			}

			lock (_syncLock)
			{
				if (_unfinishedAuctions.Count > 0)
				{
					var ua = _unfinishedAuctions.OrderBy(u => Math.Abs(u.Price - evalPrice)).First();
					signals.Add(new OrderFlowSignal
					{
						Type = "unfinished_auction",
						Title = (ua.IsHigh ? "Unfinished High" : "Unfinished Low") + " (" + P(ua.Price) + ")",
						Tone = ua.IsHigh ? "bullish" : "bearish",
						Price = ua.Price,
						Priority = PriorityUnfinished,
						Time = T(ua.BarStart)
					});
				}
			}

			var distinctSignals = signals
				.GroupBy(s => s.Type + ":" + s.Tone + ":" + s.Title)
				.Select(g => g.First())
				.OrderBy(s => s.Priority)
				.Take(5)
				.ToList();

			var top = distinctSignals.FirstOrDefault(s => s.Priority < PriorityUnfinished);

			string bias;
			string biasTone;
			string action;
			double actionPrice;

			if (top == null)
			{
				bias = "Rotational / Balance";
				biasTone = "neutral";
				action = "Stand Aside";
				actionPrice = pocPrice;
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
						action = (top.Tone == "bearish" ? "Fade at " : "Squeeze at ") + level;
						break;
					case "absorption":
						bias = top.Tone == "bearish" ? "Seller Absorption" : "Buyer Absorption";
						action = (top.Tone == "bearish" ? "Fade at " : "Squeeze at ") + level;
						break;
					case "divergence":
						bias = top.Tone == "bearish" ? "Bearish Divergence" : "Bullish Divergence";
						action = (top.Tone == "bearish" ? "Fade at " : "Squeeze at ") + level;
						break;
					case "unfinished_auction":
						bias = top.Tone == "bearish" ? "Unfinished Low" : "Unfinished High";
						action = (top.Tone == "bearish" ? "Target down " : "Target up ") + level;
						break;
					case "exhaustion":
						bias = top.Tone == "bearish" ? "Buying Exhaustion" : "Selling Exhaustion";
						action = (top.Tone == "bearish" ? "Fade at " : "Squeeze at ") + level;
						break;
					default:
						bias = top.Tone == "bearish" ? "Bearish Initiative" : "Bullish Initiative";
						action = (top.Tone == "bearish" ? "Sell at " : "Buy at ") + level;
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
				DeltaDivergence = deltaDivergence,
				Signals = distinctSignals
			};

			_lastContext = result;
			return result;
		}

		private static void UpdateLevelVolumeStats()
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

		private static void DetectUnfinishedAuctions(FootprintBarData bar, double tickSize)
		{
			var levels = bar.Levels;
			if (levels == null || levels.Length == 0)
				return;

			foreach (var lvl in levels)
			{
				if (Math.Abs(lvl.Price - bar.High) <= tickSize * 0.5 &&
					lvl.BidVolume > 0 &&
					!_unfinishedAuctions.Any(u => u.IsHigh && Math.Abs(u.Price - lvl.Price) < tickSize * 0.5))
				{
					_unfinishedAuctions.Add(new UnfinishedAuction { IsHigh = true, Price = lvl.Price, BarStart = bar.StartTime });
				}

				if (Math.Abs(lvl.Price - bar.Low) <= tickSize * 0.5 &&
					lvl.AskVolume > 0 &&
					!_unfinishedAuctions.Any(u => !u.IsHigh && Math.Abs(u.Price - lvl.Price) < tickSize * 0.5))
				{
					_unfinishedAuctions.Add(new UnfinishedAuction { IsHigh = false, Price = lvl.Price, BarStart = bar.StartTime });
				}
			}

			while (_unfinishedAuctions.Count > MaxUnfinishedAuctions)
				_unfinishedAuctions.RemoveAt(0);
		}

		private static void ComputeDivergence(double tickSize)
		{
			_divergence = "None";
			_divergencePrice = 0;
			_divergenceBarStart = 0;

			var n = _divergenceWindow.Count;
			if (n < 3)
				return;

			var latest = _divergenceWindow[n - 1];

			var priorHigh = double.MinValue;
			var priorHighCvd = 0m;
			var priorLow = double.MaxValue;
			var priorLowCvd = 0m;

			for (var i = 0; i < n - 1; i++)
			{
				var p = _divergenceWindow[i];
				if (p.High >= priorHigh) { priorHigh = p.High; priorHighCvd = p.Cvd; }
				if (p.Low <= priorLow) { priorLow = p.Low; priorLowCvd = p.Cvd; }
			}

			var bearGap = Math.Abs(priorHighCvd) * (decimal)DivergenceMinCvdGapRatio;
			var bullGap = Math.Abs(priorLowCvd) * (decimal)DivergenceMinCvdGapRatio;

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

		private sealed class ImbalanceRun
		{
			public bool IsBuy;
			public double StartP;
			public double EndP;
			public int Run;
		}

		private static List<ImbalanceRun> DetectStackedImbalances(FootprintBarData bar, decimal baselineBarVolume)
		{
			var list = new List<ImbalanceRun>();
			var levels = bar != null ? bar.Levels : null;
			if (levels == null || levels.Length < 2) return list;

			if (!IsSortedAscending(levels))
				levels = levels.OrderBy(l => l.Price).ToArray();

			var avgLevelVol = bar.Volume / levels.Length;
			var minLevelVol = avgLevelVol * (decimal)MinImbalanceVolRatio;

			var buyImb = new bool[levels.Length];
			var sellImb = new bool[levels.Length];

			for (var i = 0; i < levels.Length - 1; i++)
			{
				var lLow = levels[i];
				var lHigh = levels[i + 1];

				if (lHigh.AskVolume >= minLevelVol &&
					(lLow.BidVolume == 0 ||
					 lHigh.AskVolume >= lLow.BidVolume * ImbalanceRatio))
				{
					buyImb[i + 1] = true;
				}

				if (lLow.BidVolume >= minLevelVol &&
					(lHigh.AskVolume == 0 ||
					 lLow.BidVolume >= lHigh.AskVolume * ImbalanceRatio))
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
					if (run >= StackedLevelsRequired)
						list.Add(new ImbalanceRun { IsBuy = true, StartP = levels[i - run].Price, EndP = levels[i - 1].Price, Run = run });
					run = 0;
				}
			}

			if (run >= StackedLevelsRequired)
				list.Add(new ImbalanceRun { IsBuy = true, StartP = levels[levels.Length - run].Price, EndP = levels[levels.Length - 1].Price, Run = run });

			run = 0;
			for (var i = 0; i < levels.Length; i++)
			{
				if (sellImb[i]) run++;
				else
				{
					if (run >= StackedLevelsRequired)
						list.Add(new ImbalanceRun { IsBuy = false, StartP = levels[i - run].Price, EndP = levels[i - 1].Price, Run = run });
					run = 0;
				}
			}

			if (run >= StackedLevelsRequired)
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
