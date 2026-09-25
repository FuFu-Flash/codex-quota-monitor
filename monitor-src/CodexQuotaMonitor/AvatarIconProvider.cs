using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CodexQuotaMonitor;

internal static class AvatarIconProvider
{
	private static readonly Regex AvatarUrlPattern = new Regex(
		@"https://cdn\.auth0\.com/avatars/[A-Za-z0-9._~-]+",
		RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static readonly string AvatarCachePath = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"CodexQuotaMonitor",
		"account-avatar.png");

	public static async Task<Icon?> LoadAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			string? avatarUrl = FindCurrentAvatarUrl();
			if (avatarUrl != null)
			{
				byte[]? downloaded = await DownloadAsync(avatarUrl, cancellationToken);
				if (downloaded != null)
				{
					Directory.CreateDirectory(Path.GetDirectoryName(AvatarCachePath)!);
					string temporary = AvatarCachePath + ".tmp";
					await File.WriteAllBytesAsync(temporary, downloaded, cancellationToken);
					File.Move(temporary, AvatarCachePath, true);
					return CreateCircularIcon(downloaded);
				}
			}

			if (File.Exists(AvatarCachePath))
			{
				return CreateCircularIcon(await File.ReadAllBytesAsync(AvatarCachePath, cancellationToken));
			}
		}
		catch
		{
		}
		return null;
	}

	internal static string? FindCurrentAvatarUrl()
	{
		foreach (string directory in FindChromiumCacheDirectories())
		{
			if (!Directory.Exists(directory))
			{
				continue;
			}

			IEnumerable<string> files;
			try
			{
				files = Directory.EnumerateFiles(directory)
					.OrderByDescending(File.GetLastWriteTimeUtc)
					.ToArray();
			}
			catch
			{
				continue;
			}

			foreach (string file in files)
			{
				string? url = FindLastAvatarUrl(file);
				if (url != null)
				{
					return url;
				}
			}
		}
		return null;
	}

	internal static string? FindAvatarUrlInText(string text)
	{
		MatchCollection matches = AvatarUrlPattern.Matches(text);
		return matches.Count == 0 ? null : matches[matches.Count - 1].Value;
	}

	private static IEnumerable<string> FindChromiumCacheDirectories()
	{
		string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		string packages = Path.Combine(local, "Packages");
		if (Directory.Exists(packages))
		{
			IEnumerable<string> codexPackages;
			try
			{
				codexPackages = Directory.EnumerateDirectories(packages, "OpenAI.Codex_*").ToArray();
			}
			catch
			{
				codexPackages = Array.Empty<string>();
			}

			foreach (string package in codexPackages)
			{
				yield return Path.Combine(
					package,
					"LocalCache",
					"Roaming",
					"Codex",
					"web",
					"Codex",
					"Default",
					"Cache",
					"Cache_Data");
			}
		}

		yield return Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"Codex",
			"web",
			"Codex",
			"Default",
			"Cache",
			"Cache_Data");
	}

	private static string? FindLastAvatarUrl(string file)
	{
		try
		{
			using FileStream stream = new FileStream(
				file,
				FileMode.Open,
				FileAccess.Read,
				FileShare.ReadWrite | FileShare.Delete,
				81920,
				FileOptions.SequentialScan);
			if (stream.Length <= 0 || stream.Length > 64L * 1024 * 1024)
			{
				return null;
			}

			byte[] bytes = new byte[stream.Length];
			int total = 0;
			while (total < bytes.Length)
			{
				int read = stream.Read(bytes, total, bytes.Length - total);
				if (read == 0)
				{
					break;
				}
				total += read;
			}
			return FindAvatarUrlInText(Encoding.UTF8.GetString(bytes, 0, total));
		}
		catch
		{
			return null;
		}
	}

	private static async Task<byte[]?> DownloadAsync(string avatarUrl, CancellationToken cancellationToken)
	{
		if (!Uri.TryCreate(avatarUrl, UriKind.Absolute, out Uri? uri)
			|| uri.Scheme != Uri.UriSchemeHttps
			|| !uri.Host.Equals("cdn.auth0.com", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		using HttpClientHandler handler = new HttpClientHandler { UseCookies = false };
		using HttpClient client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
		using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();
		if (response.Content.Headers.ContentLength > 2L * 1024 * 1024)
		{
			return null;
		}

		byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
		return bytes.Length is > 0 and <= 2097152 ? bytes : null;
	}

	private static Icon CreateCircularIcon(byte[] imageBytes)
	{
		using MemoryStream stream = new MemoryStream(imageBytes, writable: false);
		using Image source = Image.FromStream(stream, useEmbeddedColorManagement: true, validateImageData: true);
		using Bitmap canvas = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
		using (Graphics graphics = Graphics.FromImage(canvas))
		{
			graphics.Clear(Color.Transparent);
			graphics.SmoothingMode = SmoothingMode.HighQuality;
			graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

			using GraphicsPath circle = new GraphicsPath();
			circle.AddEllipse(1, 1, 62, 62);
			graphics.SetClip(circle);
			float scale = Math.Max(64f / source.Width, 64f / source.Height);
			float width = source.Width * scale;
			float height = source.Height * scale;
			graphics.DrawImage(source, (64f - width) / 2f, (64f - height) / 2f, width, height);
			graphics.ResetClip();
			using Pen border = new Pen(Color.FromArgb(210, 255, 255, 255), 2f);
			graphics.DrawEllipse(border, 1.5f, 1.5f, 61f, 61f);
		}

		IntPtr handle = canvas.GetHicon();
		try
		{
			return (Icon)Icon.FromHandle(handle).Clone();
		}
		finally
		{
			DestroyIcon(handle);
		}
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool DestroyIcon(IntPtr handle);
}
