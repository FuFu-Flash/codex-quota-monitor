using System;
using System.Collections.Generic;

namespace CodexQuotaMonitor;

internal sealed class UserSettings
{
	public string Variant { get; set; } = "A";

	public string Theme { get; set; } = "Auto";

	public string? LastMonitor { get; set; }

	public Dictionary<string, WindowPosition> Positions { get; set; } = new Dictionary<string, WindowPosition>(StringComparer.OrdinalIgnoreCase);
}
