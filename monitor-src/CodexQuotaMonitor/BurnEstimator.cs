using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexQuotaMonitor;

internal sealed class BurnEstimator
{
	private readonly Queue<(DateTimeOffset At, double UsedPercent, DateTimeOffset ResetsAt)> _samples = new Queue<(DateTimeOffset, double, DateTimeOffset)>();

	public QuotaSnapshot Apply(QuotaSnapshot snapshot)
	{
		if ((object)snapshot.ShortWindow == null)
		{
			return snapshot;
		}
		RateWindow shortWindow = snapshot.ShortWindow;
		if (_samples.Count > 0 && _samples.Last().ResetsAt != shortWindow.ResetsAt)
		{
			_samples.Clear();
		}
		_samples.Enqueue((snapshot.FetchedAt, shortWindow.UsedPercent, shortWindow.ResetsAt));
		while (_samples.Count > 0 && snapshot.FetchedAt - _samples.Peek().At > TimeSpan.FromHours(2.0))
		{
			_samples.Dequeue();
		}
		if (_samples.Count < 2)
		{
			return snapshot;
		}
		(DateTimeOffset, double, DateTimeOffset) tuple = _samples.Peek();
		(DateTimeOffset At, double UsedPercent, DateTimeOffset ResetsAt) tuple2 = _samples.Last();
		double totalHours = (tuple2.At - tuple.Item1).TotalHours;
		double num = tuple2.UsedPercent - tuple.Item2;
		if (totalHours < 1.0 / 60.0 || num <= 0.05)
		{
			return snapshot;
		}
		double num2 = num / totalHours;
		TimeSpan value = TimeSpan.FromHours(shortWindow.RemainingPercent / num2);
		return snapshot with
		{
			BurnRatePerHour = num2,
			EstimatedExhaustion = value
		};
	}
}
