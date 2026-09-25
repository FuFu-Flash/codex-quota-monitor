using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CodexQuotaMonitor;

internal static class Program
{
	[STAThread]
	public static int Main(string[] args)
	{
		if (args.Contains<string>("--self-test", StringComparer.OrdinalIgnoreCase))
		{
			return RunSelfTest();
		}
		bool createdNew;
		using (new Mutex(initiallyOwned: true, "Local\\CodexQuotaMonitor.Singleton", out createdNew))
		{
			if (!createdNew)
			{
				return 0;
			}
			Application application = new Application();
			application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			MainWindow window = new MainWindow();
			application.Run(window);
			return 0;
		}
	}

	private static int RunSelfTest()
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse("{\n  \"rateLimits\": {\n    \"planType\": \"plus\",\n    \"primary\": { \"usedPercent\": 23, \"windowDurationMins\": 300, \"resetsAt\": 1789482169 },\n    \"secondary\": { \"usedPercent\": 93, \"windowDurationMins\": 10080, \"resetsAt\": 1789812532 },\n    \"credits\": { \"balance\": \"0\" }\n  }\n}");
			using JsonDocument jsonDocument2 = JsonDocument.Parse("{ \"dailyUsageBuckets\": [{ \"startDate\": \"2026-09-14\", \"tokens\": 20660160 }] }");
			QuotaSnapshot quotaSnapshot = QuotaParser.Parse(jsonDocument.RootElement, jsonDocument2.RootElement, DateTimeOffset.Now);
			RateWindow? shortWindow = quotaSnapshot.ShortWindow;
			Assert((object)shortWindow != null && shortWindow.WindowDurationMins == 300, "short window");
			RateWindow? longWindow = quotaSnapshot.LongWindow;
			Assert((object)longWindow != null && longWindow.WindowDurationMins == 10080, "long window");
			RateWindow? shortWindow2 = quotaSnapshot.ShortWindow;
			Assert((object)shortWindow2 != null && shortWindow2.RemainingPercent == 77.0, "short remaining");
			RateWindow? longWindow2 = quotaSnapshot.LongWindow;
			Assert((object)longWindow2 != null && longWindow2.RemainingPercent == 7.0, "long remaining");
			Assert(quotaSnapshot.DailyUsage.Single().Tokens == 20660160, "daily usage");
			Assert(DisplayText.WindowName(quotaSnapshot.ShortWindow, "") == "5 小时", "window label");
			Assert(AvatarIconProvider.FindAvatarUrlInText("x https://cdn.auth0.com/avatars/ff.png y") == "https://cdn.auth0.com/avatars/ff.png", "avatar url");
			int attempts = 0;
			int recoveries = 0;
			string recoveryResult = AppServerClient.RetryAuthenticationFailureAsync(
				() =>
				{
					attempts++;
					if (attempts == 1)
					{
						throw new InvalidOperationException("authentication required");
					}
					return Task.FromResult("online");
				},
				() =>
				{
					recoveries++;
					return Task.CompletedTask;
				}).GetAwaiter().GetResult();
			Assert(recoveryResult == "online" && attempts == 2 && recoveries == 1, "authentication recovery");
			string telemetryRoot = Path.Combine(Path.GetTempPath(), "CodexQuotaMonitor-SelfTest-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(telemetryRoot);
			try
			{
				string telemetryFile = Path.Combine(telemetryRoot, "sample.jsonl");
				File.WriteAllLines(telemetryFile, new[]
				{
					"{\"timestamp\":\"2026-09-25T08:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"user_message\"}}",
					"{\"timestamp\":\"2026-09-25T08:00:02Z\",\"type\":\"token_usage_record\",\"payload\":{\"turn_id\":\"turn-test\",\"usage\":{\"input_tokens\":1000,\"cached_input_tokens\":750,\"output_tokens\":100}}}"
				});
				TokenTelemetrySnapshot telemetry = new TokenTelemetryReader(telemetryRoot).ReadLatest();
				Assert(telemetry.CacheHitPercent == 75.0, "cache hit percentage");
				Assert(telemetry.OutputTokensPerSecond == 50.0, "output token rate");
			}
			finally
			{
				Directory.Delete(telemetryRoot, recursive: true);
			}
			Assert(File.Exists(AppServerClient.FindCodexExecutable()), "codex locator");
			Console.WriteLine("Self-test passed.");
			return 0;
		}
		catch (Exception value)
		{
			Console.Error.WriteLine(value);
			return 1;
		}
	}

	private static void Assert(bool condition, string name)
	{
		if (!condition)
		{
			throw new InvalidOperationException("Self-test failed: " + name);
		}
	}
}
