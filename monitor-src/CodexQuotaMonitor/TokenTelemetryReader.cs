using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CodexQuotaMonitor;

internal sealed record TokenTelemetrySnapshot(
	double? CacheHitPercent,
	double? OutputTokensPerSecond,
	long? InputTokens,
	long? CachedInputTokens,
	long? OutputTokens,
	double? ResponseDurationSeconds,
	DateTimeOffset? UpdatedAt,
	string? TurnId)
{
	public static TokenTelemetrySnapshot Empty { get; } = new(null, null, null, null, null, null, null, null);
}

internal sealed class TokenTelemetryReader
{
	private const int TailBytes = 4 * 1024 * 1024;
	private readonly string _sessionsRoot;
	private string? _activePath;
	private DateTime _nextSessionScanUtc = DateTime.MinValue;
	private long _lastLength = -1;
	private TokenTelemetrySnapshot _cached = TokenTelemetrySnapshot.Empty;

	public TokenTelemetryReader(string? sessionsRoot = null)
	{
		string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME")
			?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
		_sessionsRoot = sessionsRoot ?? Path.Combine(codexHome, "sessions");
	}

	public TokenTelemetrySnapshot ReadLatest()
	{
		try
		{
			if (DateTime.UtcNow >= _nextSessionScanUtc || string.IsNullOrWhiteSpace(_activePath) || !File.Exists(_activePath))
			{
				string? latestPath = FindMostRecentlyWrittenSession();
				if (!string.Equals(latestPath, _activePath, StringComparison.OrdinalIgnoreCase))
				{
					_activePath = latestPath;
					_lastLength = -1;
					_cached = TokenTelemetrySnapshot.Empty;
				}
				_nextSessionScanUtc = DateTime.UtcNow.AddSeconds(5);
			}

			if (string.IsNullOrWhiteSpace(_activePath))
			{
				return TokenTelemetrySnapshot.Empty;
			}
			long length = new FileInfo(_activePath).Length;
			if (length == _lastLength)
			{
				return _cached;
			}
			_lastLength = length;
			TokenTelemetrySnapshot parsed = ReadSessionTail(_activePath);
			if (parsed.UpdatedAt.HasValue)
			{
				_cached = parsed;
			}
			return _cached;
		}
		catch
		{
			return TokenTelemetrySnapshot.Empty;
		}
	}

	private string? FindMostRecentlyWrittenSession()
	{
		if (!Directory.Exists(_sessionsRoot))
		{
			return null;
		}

		return Directory.EnumerateFiles(_sessionsRoot, "*.jsonl", SearchOption.AllDirectories)
			.Select(path => new FileInfo(path))
			.OrderByDescending(file => file.LastWriteTimeUtc)
			.Select(file => file.FullName)
			.FirstOrDefault();
	}

	private static TokenTelemetrySnapshot ReadSessionTail(string path)
	{
		using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		long offset = Math.Max(0, stream.Length - TailBytes);
		stream.Seek(offset, SeekOrigin.Begin);
		using StreamReader reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024, leaveOpen: false);
		if (offset > 0)
		{
			reader.ReadLine();
		}

		DateTimeOffset? responseStart = null;
		TokenTelemetrySnapshot latest = TokenTelemetrySnapshot.Empty;
		string? line;
		while ((line = reader.ReadLine()) != null)
		{
			if (!TryParseRecord(line, out JsonDocument? document) || document == null)
			{
				continue;
			}

			using (document)
			{
				JsonElement root = document.RootElement;
				if (!TryTimestamp(root, out DateTimeOffset timestamp) || !root.TryGetProperty("type", out JsonElement typeElement))
				{
					continue;
				}

				string? type = typeElement.GetString();
				if (IsResponseTrigger(root, type))
				{
					responseStart = timestamp;
					continue;
				}

				if (!string.Equals(type, "token_usage_record", StringComparison.Ordinal) || !root.TryGetProperty("payload", out JsonElement payload) || !payload.TryGetProperty("usage", out JsonElement usage))
				{
					continue;
				}

				long? input = ReadInt64(usage, "input_tokens");
				long? cached = ReadInt64(usage, "cached_input_tokens");
				long? output = ReadInt64(usage, "output_tokens");
				double? cachePercent = input > 0 && cached.HasValue ? Math.Clamp(cached.Value * 100.0 / input.Value, 0.0, 100.0) : null;
				double? duration = null;
				if (responseStart.HasValue && responseStart.Value <= timestamp)
				{
					duration = Math.Max(0.001, (timestamp - responseStart.Value).TotalSeconds);
				}
				double? tokensPerSecond = output.HasValue && duration > 0 ? output.Value / duration.Value : null;
				string? turnId = payload.TryGetProperty("turn_id", out JsonElement turnElement) ? turnElement.GetString() : null;
				latest = new TokenTelemetrySnapshot(cachePercent, tokensPerSecond, input, cached, output, duration, timestamp, turnId);
				responseStart = null;
			}
		}

		return latest;
	}

	private static bool IsResponseTrigger(JsonElement root, string? type)
	{
		if (!root.TryGetProperty("payload", out JsonElement payload))
		{
			return false;
		}

		if (string.Equals(type, "response_item", StringComparison.Ordinal) && payload.TryGetProperty("type", out JsonElement itemType))
		{
			string? value = itemType.GetString();
			return value != null && (value.EndsWith("_call_output", StringComparison.Ordinal) || value is "function_call_output" or "custom_tool_call_output");
		}

		if (string.Equals(type, "event_msg", StringComparison.Ordinal) && payload.TryGetProperty("type", out JsonElement eventType))
		{
			return eventType.GetString() is "user_message" or "task_started" or "turn_started";
		}

		return false;
	}

	private static bool TryParseRecord(string line, out JsonDocument? document)
	{
		try
		{
			document = JsonDocument.Parse(line);
			return true;
		}
		catch (JsonException)
		{
			document = null;
			return false;
		}
	}

	private static bool TryTimestamp(JsonElement root, out DateTimeOffset timestamp)
	{
		timestamp = default;
		return root.TryGetProperty("timestamp", out JsonElement value)
			&& DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp);
	}

	private static long? ReadInt64(JsonElement element, string property)
	{
		return element.TryGetProperty(property, out JsonElement value) && value.TryGetInt64(out long number) ? number : null;
	}
}
