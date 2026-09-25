using System;

namespace CodexQuotaMonitor;

internal static class DisplayText
{
	public static string Percent(double value)
	{
		return $"{Math.Round(value):0}%";
	}

	public static string WindowName(RateWindow? window, string fallback)
	{
		if ((object)window == null)
		{
			return fallback;
		}
		if (window.WindowDurationMins % 10080 == 0)
		{
			return "每周";
		}
		if (window.WindowDurationMins % 1440 != 0)
		{
			if (window.WindowDurationMins % 60 != 0)
			{
				return $"{window.WindowDurationMins} 分钟";
			}
			return $"{window.WindowDurationMins / 60} 小时";
		}
		return $"{window.WindowDurationMins / 1440} 天";
	}

	public static string Until(DateTimeOffset at, DateTimeOffset? now = null)
	{
		TimeSpan timeSpan = at - (now ?? DateTimeOffset.Now);
		if (timeSpan <= TimeSpan.Zero)
		{
			return "即将重置";
		}
		if (!(timeSpan.TotalDays >= 1.0))
		{
			if (!(timeSpan.TotalHours >= 1.0))
			{
				return $"{Math.Max(1, timeSpan.Minutes)} 分钟后重置";
			}
			return $"{(int)timeSpan.TotalHours} 小时 {timeSpan.Minutes} 分后重置";
		}
		return $"{(int)timeSpan.TotalDays} 天 {timeSpan.Hours} 小时后重置";
	}

	public static string CompactDuration(TimeSpan duration)
	{
		if (!(duration.TotalDays >= 1.0))
		{
			if (!(duration.TotalHours >= 1.0))
			{
				return $"{Math.Max(1, duration.Minutes)}m";
			}
			return $"{(int)duration.TotalHours}h {duration.Minutes}m";
		}
		return $"{(int)duration.TotalDays}d {duration.Hours}h";
	}

	public static string Tokens(long tokens)
	{
		if (tokens < 1000000)
		{
			if (tokens < 1000)
			{
				return $"{tokens} tok";
			}
			return $"{(double)tokens / 1000.0:0.#}K tok";
		}
		return $"{(double)tokens / 1000000.0:0.#}M tok";
	}
}
