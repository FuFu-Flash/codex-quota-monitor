using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexQuotaMonitor;

internal sealed class ControlServer : IAsyncDisposable
{
	private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();

	private readonly Func<string, Task<string>> _handler;

	private readonly Task _loop;

	public ControlServer(Func<string, Task<string>> handler)
	{
		_handler = handler;
		_loop = Task.Run((Func<Task?>)RunAsync);
	}

	private async Task RunAsync()
	{
		while (!_shutdown.IsCancellationRequested)
		{
			try
			{
				await using NamedPipeServerStream pipe = new NamedPipeServerStream("CodexQuotaMonitor.Control", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
				await pipe.WaitForConnectionAsync(_shutdown.Token);
				using StreamReader reader = new StreamReader(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: false, 1024, leaveOpen: true);
				using StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1024, leaveOpen: true)
				{
					AutoFlush = true
				};
				string text = await reader.ReadLineAsync(_shutdown.Token);
				if (!string.IsNullOrWhiteSpace(text))
				{
					StreamWriter streamWriter = writer;
					await streamWriter.WriteLineAsync(await _handler(text.Trim().ToLowerInvariant()));
				}
			}
			catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
			{
				break;
			}
			catch
			{
				await Task.Delay(250, _shutdown.Token).ContinueWith(delegate
				{
				}, TaskScheduler.Default);
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		_shutdown.Cancel();
		try
		{
			await _loop;
		}
		catch
		{
		}
		_shutdown.Dispose();
	}
}
