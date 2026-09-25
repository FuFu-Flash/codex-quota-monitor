using System;

namespace CodexQuotaMonitor;

internal sealed record DailyUsage(DateOnly Date, long Tokens);
