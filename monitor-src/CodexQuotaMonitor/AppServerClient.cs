using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexQuotaMonitor;

internal sealed class AppServerClient : IAsyncDisposable
{
	private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>();

	private readonly SemaphoreSlim _startLock = new SemaphoreSlim(1, 1);

	private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

	private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();

	private Process? _process;

	private StreamWriter? _input;

	private Task? _readerTask;

	private long _nextId;

	public event Action? RateLimitsUpdated;

	public async Task<QuotaSnapshot> ReadQuotaAsync(CancellationToken cancellationToken)
	{
		return await RetryAuthenticationFailureAsync(
			() => ReadQuotaCoreAsync(cancellationToken),
			() => RestartAndRefreshAccountAsync(cancellationToken));
	}

	private async Task<QuotaSnapshot> ReadQuotaCoreAsync(CancellationToken cancellationToken)
	{
		await EnsureStartedAsync(cancellationToken);
		Task<JsonElement> task = RequestCoreAsync("account/rateLimits/read", null, cancellationToken);
		return QuotaParser.Parse(usageResult: await ReadUsageBestEffortAsync(cancellationToken), rateResult: await task, now: DateTimeOffset.Now);
	}

	private async Task RestartAndRefreshAccountAsync(CancellationToken cancellationToken)
	{
		StopProcess();
		await EnsureStartedAsync(cancellationToken);
		await RequestCoreAsync("account/read", new
		{
			refreshToken = true
		}, cancellationToken);
	}

	internal static async Task<T> RetryAuthenticationFailureAsync<T>(Func<Task<T>> operation, Func<Task> recover)
	{
		try
		{
			return await operation();
		}
		catch (Exception exception) when (IsAuthenticationFailure(exception))
		{
			await recover();
			return await operation();
		}
	}

	internal static bool IsAuthenticationFailure(Exception exception)
	{
		string text = exception.ToString();
		return text.Contains("authentication", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("not logged", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("login", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("401", StringComparison.OrdinalIgnoreCase);
	}

	private async Task<JsonElement?> ReadUsageBestEffortAsync(CancellationToken cancellationToken)
	{
		try
		{
			return await RequestCoreAsync("account/usage/read", null, cancellationToken);
		}
		catch
		{
			return null;
		}
	}

	private async Task EnsureStartedAsync(CancellationToken cancellationToken)
	{
		Process process = _process;
		if (process != null && !process.HasExited && _input != null)
		{
			return;
		}
		await _startLock.WaitAsync(cancellationToken);
		try
		{
			process = _process;
			if (process == null || process.HasExited || _input == null)
			{
				StopProcess();
				string fileName = FindCodexExecutable() ?? throw new FileNotFoundException("找不到 codex.exe。请先安装或打开 Codex 桌面应用。");
				ProcessStartInfo processStartInfo = new ProcessStartInfo
				{
					FileName = fileName,
					UseShellExecute = false,
					RedirectStandardInput = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = true,
					WindowStyle = ProcessWindowStyle.Hidden
				};
				processStartInfo.ArgumentList.Add("app-server");
				_process = new Process
				{
					StartInfo = processStartInfo,
					EnableRaisingEvents = true
				};
				_process.Exited += delegate
				{
					FailPending(new IOException("Codex App Server 已退出。"));
				};
				if (!_process.Start())
				{
					throw new InvalidOperationException("无法启动 Codex App Server。");
				}
				_input = _process.StandardInput;
				_input.AutoFlush = true;
				_readerTask = Task.Run(() => ReadLoopAsync(_process.StandardOutput, _shutdown.Token));
				Task.Run(() => DrainErrorsAsync(_process.StandardError, _shutdown.Token));
				await RequestCoreAsync("initialize", new
				{
					clientInfo = new
					{
						name = "codex_quota_monitor",
						title = "Codex Quota Monitor",
						version = "1.0.1"
					}
				}, cancellationToken, skipStartCheck: true);
				await SendAsync(new
				{
					method = "initialized",
					@params = new { }
				}, cancellationToken);
			}
		}
		finally
		{
			_startLock.Release();
		}
	}

	private async Task<JsonElement> RequestCoreAsync(string method, object? parameters, CancellationToken cancellationToken, bool skipStartCheck = false)
	{
		if (!skipStartCheck)
		{
			await EnsureStartedAsync(cancellationToken);
		}
		long id = Interlocked.Increment(ref _nextId);
		TaskCompletionSource<JsonElement> completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pending[id] = completion;
		try
		{
			await SendAsync(new
			{
				method = method,
				id = id,
				@params = parameters
			}, cancellationToken);
			return await completion.Task.WaitAsync(TimeSpan.FromSeconds(15.0), cancellationToken);
		}
		finally
		{
			_pending.TryRemove(id, out TaskCompletionSource<JsonElement> _);
		}
	}

	private async Task SendAsync(object message, CancellationToken cancellationToken)
	{
		StreamWriter input = _input ?? throw new IOException("Codex App Server 尚未连接。");
		string json = JsonSerializer.Serialize(message, message.GetType());
		await _writeLock.WaitAsync(cancellationToken);
		try
		{
			await input.WriteLineAsync(json.AsMemory(), cancellationToken);
			await input.FlushAsync(cancellationToken);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	private async Task ReadLoopAsync(StreamReader output, CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				string text = await output.ReadLineAsync(cancellationToken);
				if (text == null)
				{
					break;
				}
				using JsonDocument jsonDocument = JsonDocument.Parse(text);
				JsonElement rootElement = jsonDocument.RootElement;
				JsonElement value7;
				if (rootElement.TryGetProperty("id", out var value) && value.TryGetInt64(out var value2))
				{
					if (_pending.TryGetValue(value2, out TaskCompletionSource<JsonElement> value3))
					{
						JsonElement value6;
						if (rootElement.TryGetProperty("error", out var value4))
						{
							JsonElement value5;
							string text2 = (value4.TryGetProperty("message", out value5) ? value5.GetString() : value4.GetRawText());
							value3.TrySetException(new InvalidOperationException(text2 ?? "Codex App Server 请求失败。"));
						}
						else if (rootElement.TryGetProperty("result", out value6))
						{
							value3.TrySetResult(value6.Clone());
						}
					}
				}
				else if (rootElement.TryGetProperty("method", out value7) && value7.GetString() == "account/rateLimits/updated")
				{
					this.RateLimitsUpdated?.Invoke();
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			FailPending(exception);
		}
	}

	private static async Task DrainErrorsAsync(StreamReader error, CancellationToken cancellationToken)
	{
		try
		{
			bool flag;
			do
			{
				flag = !cancellationToken.IsCancellationRequested;
				if (flag)
				{
					flag = await error.ReadLineAsync(cancellationToken) != null;
				}
			}
			while (flag);
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void FailPending(Exception exception)
	{
		foreach (TaskCompletionSource<JsonElement> value in _pending.Values)
		{
			value.TrySetException(exception);
		}
	}

	private void StopProcess()
	{
		_input?.Dispose();
		_input = null;
		Process process = _process;
		if (process != null && !process.HasExited)
		{
			try
			{
				_process.Kill(entireProcessTree: true);
			}
			catch
			{
			}
		}
		_process?.Dispose();
		_process = null;
	}

	internal static string? FindCodexExecutable()
	{
		string environmentVariable = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
		if (!string.IsNullOrWhiteSpace(environmentVariable) && File.Exists(environmentVariable))
		{
			return environmentVariable;
		}
		string[] array = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string text in array)
		{
			try
			{
				string text2 = Path.Combine(text.Trim('"'), "codex.exe");
				if (File.Exists(text2))
				{
					return text2;
				}
			}
			catch
			{
			}
		}
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
		if (!Directory.Exists(path))
		{
			return null;
		}
		return Directory.EnumerateFiles(path, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
	}

	public async ValueTask DisposeAsync()
	{
		_shutdown.Cancel();
		StopProcess();
		if (_readerTask != null)
		{
			try
			{
				await _readerTask;
			}
			catch
			{
			}
		}
		_shutdown.Dispose();
		_startLock.Dispose();
		_writeLock.Dispose();
	}
}
