using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CodexQuotaMonitor;

internal sealed record QuotaSnapshot(RateWindow? ShortWindow, RateWindow? LongWindow, string? PlanType, string? CreditBalance, IReadOnlyList<DailyUsage> DailyUsage, DateTimeOffset FetchedAt, bool IsStale = false, string? Error = null, double? BurnRatePerHour = null, TimeSpan? EstimatedExhaustion = null)
{
	public static QuotaSnapshot Loading { get; } = new QuotaSnapshot(null, null, null, null, Array.Empty<DailyUsage>(), DateTimeOffset.MinValue, IsStale: true, "正在连接 Codex…");

	[CompilerGenerated]
	private QuotaSnapshot(QuotaSnapshot original)
	{
		ShortWindow = original.ShortWindow;
		LongWindow = original.LongWindow;
		PlanType = original.PlanType;
		CreditBalance = original.CreditBalance;
		DailyUsage = original.DailyUsage;
		FetchedAt = original.FetchedAt;
		IsStale = original.IsStale;
		Error = original.Error;
		BurnRatePerHour = original.BurnRatePerHour;
		EstimatedExhaustion = original.EstimatedExhaustion;
	}
}
