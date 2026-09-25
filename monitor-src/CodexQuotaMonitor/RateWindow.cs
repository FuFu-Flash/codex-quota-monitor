using System;

namespace CodexQuotaMonitor;

internal sealed record RateWindow(double UsedPercent, int WindowDurationMins, DateTimeOffset ResetsAt)
{
	public double RemainingPercent => Math.Clamp(100.0 - UsedPercent, 0.0, 100.0);
}
