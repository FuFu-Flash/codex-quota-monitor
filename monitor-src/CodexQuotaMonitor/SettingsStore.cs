using System;
using System.IO;
using System.Text.Json;

namespace CodexQuotaMonitor;

internal sealed class SettingsStore
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	private readonly string _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexQuotaMonitor");

	private string SettingsPath => Path.Combine(_directory, "settings.json");

	private string SnapshotPath => Path.Combine(_directory, "last-snapshot.json");

	public UserSettings LoadSettings()
	{
		try
		{
			return File.Exists(SettingsPath) ? (JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new UserSettings()) : new UserSettings();
		}
		catch
		{
			return new UserSettings();
		}
	}

	public void SaveSettings(UserSettings settings)
	{
		Directory.CreateDirectory(_directory);
		AtomicWrite(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
	}

	public QuotaSnapshot? LoadSnapshot()
	{
		try
		{
			return File.Exists(SnapshotPath) ? JsonSerializer.Deserialize<QuotaSnapshot>(File.ReadAllText(SnapshotPath), JsonOptions) : null;
		}
		catch
		{
			return null;
		}
	}

	public void SaveSnapshot(QuotaSnapshot snapshot)
	{
		Directory.CreateDirectory(_directory);
		AtomicWrite(SnapshotPath, JsonSerializer.Serialize(snapshot with
		{
			IsStale = false,
			Error = null
		}, JsonOptions));
	}

	private static void AtomicWrite(string path, string content)
	{
		string text = path + ".tmp";
		File.WriteAllText(text, content);
		File.Move(text, path, overwrite: true);
	}
}
