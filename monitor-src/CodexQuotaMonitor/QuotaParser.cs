using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CodexQuotaMonitor;

internal static class QuotaParser
{
	public static QuotaSnapshot Parse(JsonElement rateResult, JsonElement? usageResult, DateTimeOffset now)
	{
		List<RateWindow> list = new List<RateWindow>();
		string text = null;
		string creditBalance = null;
		if (rateResult.TryGetProperty("rateLimits", out var value) && value.ValueKind == JsonValueKind.Object)
		{
			AddWindows(value, list);
			text = ReadString(value, "planType");
			if (value.TryGetProperty("credits", out var value2) && value2.ValueKind == JsonValueKind.Object)
			{
				creditBalance = ReadString(value2, "balance");
			}
		}
		if (list.Count == 0 && rateResult.TryGetProperty("rateLimitsByLimitId", out var value3) && value3.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty item in value3.EnumerateObject())
			{
				AddWindows(item.Value, list);
				if (text == null)
				{
					text = ReadString(item.Value, "planType");
				}
			}
		}
		List<RateWindow> list2 = (from window in list
			group window by (WindowDurationMins: window.WindowDurationMins, ResetsAt: window.ResetsAt) into @group
			select @group.First() into window
			orderby window.WindowDurationMins
			select window).ToList();
		List<DailyUsage> list3 = new List<DailyUsage>();
		if (usageResult.HasValue)
		{
			JsonElement valueOrDefault = usageResult.GetValueOrDefault();
			if (valueOrDefault.ValueKind == JsonValueKind.Object && valueOrDefault.TryGetProperty("dailyUsageBuckets", out var value4) && value4.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item2 in value4.EnumerateArray())
				{
					if (item2.TryGetProperty("startDate", out var value5) && DateOnly.TryParse(value5.GetString(), out var result) && item2.TryGetProperty("tokens", out var value6) && value6.TryGetInt64(out var value7))
					{
						list3.Add(new DailyUsage(result, value7));
					}
				}
			}
		}
		return new QuotaSnapshot(list2.FirstOrDefault(), (list2.Count > 1) ? list2.Last() : null, text, creditBalance, list3.OrderBy((DailyUsage item) => item.Date).ToArray(), now);
	}

	private static void AddWindows(JsonElement bucket, ICollection<RateWindow> windows)
	{
		if (bucket.TryGetProperty("primary", out var value) && TryParseWindow(value, out RateWindow window))
		{
			windows.Add(window);
		}
		if (bucket.TryGetProperty("secondary", out var value2) && TryParseWindow(value2, out RateWindow window2))
		{
			windows.Add(window2);
		}
	}

	private static bool TryParseWindow(JsonElement element, out RateWindow window)
	{
		window = null;
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("usedPercent", out var value) || !value.TryGetDouble(out var value2) || !element.TryGetProperty("windowDurationMins", out var value3) || !value3.TryGetInt32(out var value4) || !element.TryGetProperty("resetsAt", out var value5) || !value5.TryGetInt64(out var value6))
		{
			return false;
		}
		window = new RateWindow(value2, value4, DateTimeOffset.FromUnixTimeSeconds(value6));
		return true;
	}

	private static string? ReadString(JsonElement element, string property)
	{
		if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return value.GetString();
	}
}
