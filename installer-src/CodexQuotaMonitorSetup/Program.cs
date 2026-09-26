using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexQuotaMonitorSetup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var uninstall = args.Contains("/uninstall", StringComparer.OrdinalIgnoreCase);
        var quiet = args.Contains("/quiet", StringComparer.OrdinalIgnoreCase);

        if (quiet)
        {
            var errorLog = Path.Combine(Path.GetTempPath(), "CodexQuotaMonitor-Setup-error.txt");
            try
            {
                if (uninstall) InstallerEngine.Uninstall(_ => { });
                else InstallerEngine.Install(_ => { });
                if (File.Exists(errorLog)) File.Delete(errorLog);
                return 0;
            }
            catch (Exception exception)
            {
                File.WriteAllText(errorLog, exception.ToString());
                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm(uninstall));
        return Environment.ExitCode;
    }
}

internal sealed class SetupForm : Form
{
    private readonly bool _uninstall;
    private readonly Label _status;
    private readonly ProgressBar _progress;
    private readonly Button _primary;
    private readonly Button _cancel;
    private bool _completed;

    public SetupForm(bool uninstall)
    {
        _uninstall = uninstall;
        Text = uninstall ? "卸载 Codex 额度监控器" : "安装 Codex 额度监控器";
        ClientSize = new Size(560, 360);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(246, 246, 243);
        ForeColor = Color.FromArgb(30, 30, 29);
        Font = new Font("Microsoft YaHei UI", 9F);

        var dot = new Label
        {
            Text = "●",
            ForeColor = Color.FromArgb(32, 137, 88),
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 13F),
            AutoSize = true,
            Location = new Point(30, 27)
        };
        Controls.Add(dot);

        Controls.Add(new Label
        {
            Text = "Codex 额度监控器",
            Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(55, 25)
        });

        Controls.Add(new Label
        {
            Text = uninstall
                ? "移除悬浮窗、Codex 插件和安装记录。额度缓存与界面设置将保留。"
                : "在桌面右下角显示套餐实际返回的 Codex 额度窗口。",
            ForeColor = Color.FromArgb(100, 100, 97),
            AutoSize = true,
            Location = new Point(35, 70)
        });

        var card = new Panel
        {
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(35, 105),
            Size = new Size(490, 142)
        };
        card.Controls.Add(new Label
        {
            Text = uninstall ? "即将移除" : "将自动完成",
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(18, 16)
        });
        card.Controls.Add(new Label
        {
            Text = uninstall
                ? "• 停止额度悬浮窗\r\n• 从 Codex 移除插件\r\n• 清理本地程序文件"
                : "• 安装原生 Windows 悬浮窗\r\n• 注册并启用 Codex 插件\r\n• 启动监控器（无需管理员权限）",
            ForeColor = Color.FromArgb(75, 75, 72),
            AutoSize = false,
            Size = new Size(450, 74),
            Location = new Point(18, 49),
        });
        Controls.Add(card);

        _status = new Label
        {
            Text = uninstall ? "准备卸载" : "准备安装",
            ForeColor = Color.FromArgb(90, 90, 87),
            AutoSize = false,
            Size = new Size(490, 22),
            Location = new Point(35, 260)
        };
        Controls.Add(_status);

        _progress = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24,
            Visible = false,
            Location = new Point(35, 286),
            Size = new Size(490, 5)
        };
        Controls.Add(_progress);

        _cancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(88, 34),
            Location = new Point(343, 309),
            BackColor = Color.White
        };
        _cancel.FlatAppearance.BorderColor = Color.FromArgb(205, 205, 201);
        Controls.Add(_cancel);

        _primary = new Button
        {
            Text = uninstall ? "卸载" : "安装",
            FlatStyle = FlatStyle.Flat,
            Size = new Size(88, 34),
            Location = new Point(437, 309),
            BackColor = uninstall ? Color.FromArgb(180, 58, 51) : Color.FromArgb(31, 130, 84),
            ForeColor = Color.White
        };
        _primary.FlatAppearance.BorderSize = 0;
        _primary.Click += async (_, _) => await RunAsync();
        Controls.Add(_primary);
        CancelButton = _cancel;
    }

    private async Task RunAsync()
    {
        if (_completed)
        {
            Close();
            return;
        }

        if (_uninstall)
        {
            var answer = MessageBox.Show(this, "确定卸载 Codex 额度监控器？", "确认卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
        }

        _primary.Enabled = false;
        _cancel.Enabled = false;
        _progress.Visible = true;
        try
        {
            var progress = new Progress<string>(message => _status.Text = message);
            await Task.Run(() =>
            {
                if (_uninstall) InstallerEngine.Uninstall(message => ((IProgress<string>)progress).Report(message));
                else InstallerEngine.Install(message => ((IProgress<string>)progress).Report(message));
            });
            _progress.Visible = false;
            _status.Text = _uninstall ? "卸载完成" : "安装完成，额度监控器已经启动";
            _primary.Text = "完成";
            _primary.Enabled = true;
            _primary.BackColor = Color.FromArgb(31, 130, 84);
            _cancel.Visible = false;
            _completed = true;
            Environment.ExitCode = 0;
        }
        catch (Exception exception)
        {
            _progress.Visible = false;
            _status.Text = "操作失败";
            _primary.Enabled = true;
            _cancel.Enabled = true;
            MessageBox.Show(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }
}

internal static class InstallerEngine
{
    private const string PluginName = "codex-quota-monitor";
    private const string MarketplaceName = "personal";
	private const string PluginVersion = "1.0.3+codex.20260926044229";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexQuotaMonitor";

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string PluginRoot => Path.Combine(Home, "plugins", PluginName);
    private static string MarketplaceFile => Path.Combine(Home, ".agents", "plugins", "marketplace.json");
    private static string SetupInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CodexQuotaMonitor");

    public static void Install(Action<string> report)
    {
        report("检查 Codex 安装…");
        var codex = FindCodexExecutable();
        EnsureMarketplaceIsCompatible();
        StopMonitor();

        var temporaryRoot = Path.Combine(Path.GetTempPath(), "CodexQuotaMonitorSetup", Guid.NewGuid().ToString("N"));
        var extracted = Path.Combine(temporaryRoot, PluginName);
        var backup = PluginRoot + ".setup-backup";
        Directory.CreateDirectory(extracted);
        try
        {
            report("解包插件与悬浮窗…");
            using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexQuotaMonitor.Payload.zip")
                                 ?? throw new InvalidOperationException("安装包内缺少插件资源。"))
            {
                ZipFile.ExtractToDirectory(payload, extracted, true);
            }

            RunCodex(codex, "remove", required: false);

            report("安装到个人 Codex 插件目录…");
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            if (Directory.Exists(PluginRoot)) Directory.Move(PluginRoot, backup);
            CopyDirectory(extracted, PluginRoot);

            report("注册个人 marketplace…");
            UpdateMarketplaceEntry();

            report("启用 Codex 插件…");
            RunCodex(codex, "add", required: true);
            RegisterUninstaller();
            StartMonitor();

            if (Directory.Exists(backup)) Directory.Delete(backup, true);
        }
        catch
        {
            if (Directory.Exists(backup))
            {
                if (Directory.Exists(PluginRoot)) Directory.Delete(PluginRoot, true);
                Directory.Move(backup, PluginRoot);
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true);
        }
    }

    public static void Uninstall(Action<string> report)
    {
        report("停止额度监控器…");
        StopMonitor();

        var codex = TryFindCodexExecutable();
        if (codex is not null)
        {
            report("从 Codex 移除插件…");
            RunCodex(codex, "remove", required: false);
        }

        report("清理插件文件…");
        RemoveMarketplaceEntry();
        if (Directory.Exists(PluginRoot)) Directory.Delete(PluginRoot, true);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
        ScheduleInstallerCleanup();
    }

    private static void EnsureMarketplaceIsCompatible()
    {
        if (!File.Exists(MarketplaceFile)) return;
        var root = JsonNode.Parse(File.ReadAllText(MarketplaceFile)) as JsonObject
                   ?? throw new InvalidOperationException("个人 marketplace.json 格式无效。请先修复该文件。 ");
        var name = root["name"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(name) && !name.Equals(MarketplaceName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"默认 marketplace 名称为“{name}”，安装器预期为“{MarketplaceName}”。");
        }
    }

    private static void UpdateMarketplaceEntry()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MarketplaceFile)!);
        JsonObject root;
        if (File.Exists(MarketplaceFile))
        {
            root = JsonNode.Parse(File.ReadAllText(MarketplaceFile)) as JsonObject
                   ?? throw new InvalidOperationException("个人 marketplace.json 格式无效。");
        }
        else
        {
            root = new JsonObject();
        }

        root["name"] = MarketplaceName;
        if (root["interface"] is not JsonObject interfaceNode)
        {
            interfaceNode = new JsonObject();
            root["interface"] = interfaceNode;
        }
        interfaceNode["displayName"] ??= "Personal";

        if (root["plugins"] is not JsonArray plugins)
        {
            plugins = new JsonArray();
            root["plugins"] = plugins;
        }

        var entry = CreateMarketplaceEntry();
        var index = -1;
        for (var i = 0; i < plugins.Count; i++)
        {
            if (string.Equals(plugins[i]?["name"]?.GetValue<string>(), PluginName, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }
        if (index >= 0) plugins[index] = entry;
        else plugins.Add(entry);
        WriteJsonAtomically(MarketplaceFile, root);
    }

    private static JsonObject CreateMarketplaceEntry() => new()
    {
        ["name"] = PluginName,
        ["source"] = new JsonObject { ["source"] = "local", ["path"] = $"./plugins/{PluginName}" },
        ["policy"] = new JsonObject { ["installation"] = "AVAILABLE", ["authentication"] = "ON_INSTALL" },
        ["category"] = "Productivity"
    };

    private static void RemoveMarketplaceEntry()
    {
        if (!File.Exists(MarketplaceFile)) return;
        var root = JsonNode.Parse(File.ReadAllText(MarketplaceFile)) as JsonObject;
        if (root?["plugins"] is not JsonArray plugins) return;
        for (var i = plugins.Count - 1; i >= 0; i--)
        {
            if (string.Equals(plugins[i]?["name"]?.GetValue<string>(), PluginName, StringComparison.OrdinalIgnoreCase))
            {
                plugins.RemoveAt(i);
            }
        }
        WriteJsonAtomically(MarketplaceFile, root);
    }

    private static void WriteJsonAtomically(string path, JsonNode root)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    private static void RunCodex(string executable, string command, bool required)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("plugin");
        info.ArgumentList.Add(command);
        info.ArgumentList.Add($"{PluginName}@{MarketplaceName}");
        info.ArgumentList.Add("--json");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动 Codex CLI。");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(true);
            throw new TimeoutException("Codex 插件安装超时。");
        }
        if (required && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Codex 插件安装失败：{error}\r\n{output}".Trim());
        }
    }

    private static string FindCodexExecutable() => TryFindCodexExecutable()
        ?? throw new FileNotFoundException("未找到 Codex。请先安装并登录 Codex，然后重新运行安装器。");

    private static string? TryFindCodexExecutable()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(local))
        {
            var candidate = Directory.EnumerateFiles(local, "codex.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (candidate is not null) return candidate;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(folder => Path.Combine(folder, "codex.exe"))
            .FirstOrDefault(File.Exists);
    }

    private static void StopMonitor()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", "CodexQuotaMonitor.Control", PipeDirection.InOut);
            pipe.Connect(700);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 1024, true);
            writer.WriteLine("exit");
            _ = reader.ReadLine();
        }
        catch
        {
        }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var processes = Process.GetProcessesByName("CodexQuotaMonitor");
            if (processes.Length == 0) return;
            foreach (var process in processes)
            {
                using (process)
                {
                    try { process.WaitForExit(400); } catch { }
                }
            }
        }

        foreach (var process in Process.GetProcessesByName("CodexQuotaMonitor"))
        {
            using (process)
            {
                try
                {
                    var executable = Path.GetFullPath(process.MainModule?.FileName ?? string.Empty);
                    var trustedSource = Path.GetFullPath(Path.Combine(Home, "plugins", PluginName)) + Path.DirectorySeparatorChar;
                    var trustedCache = Path.GetFullPath(Path.Combine(Home, ".codex", "plugins", "cache")) + Path.DirectorySeparatorChar;
                    if (executable.StartsWith(trustedSource, StringComparison.OrdinalIgnoreCase)
                        || executable.StartsWith(trustedCache, StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill(true);
                        process.WaitForExit(3_000);
                    }
                }
                catch
                {
                }
            }
        }

        if (Process.GetProcessesByName("CodexQuotaMonitor").Length > 0)
        {
            throw new IOException("无法停止正在运行的 Codex 额度监控器，请关闭它后重试。");
        }
    }

    private static void StartMonitor()
    {
        var script = Path.Combine(PluginRoot, "scripts", "start-monitor.ps1");
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-WindowStyle");
        info.ArgumentList.Add("Hidden");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(script);
        Process.Start(info);
    }

    private static void RegisterUninstaller()
    {
        Directory.CreateDirectory(SetupInstallDirectory);
        var installedSetup = Path.Combine(SetupInstallDirectory, "Setup.exe");
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位安装器文件。");
        if (!Path.GetFullPath(current).Equals(Path.GetFullPath(installedSetup), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(current, installedSetup, true);
        }

        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey, true)
                        ?? throw new InvalidOperationException("无法写入卸载信息。");
        key.SetValue("DisplayName", "Codex 额度监控器");
        key.SetValue("DisplayVersion", PluginVersion);
        key.SetValue("Publisher", "Codex Quota Monitor");
        key.SetValue("InstallLocation", PluginRoot);
        key.SetValue("DisplayIcon", Path.Combine(PluginRoot, "assets", "app", "CodexQuotaMonitor.exe"));
        key.SetValue("UninstallString", $"\"{installedSetup}\" /uninstall");
        key.SetValue("QuietUninstallString", $"\"{installedSetup}\" /uninstall /quiet");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(DirectorySize(PluginRoot) / 1024), RegistryValueKind.DWord);
    }

    private static long DirectorySize(string directory) => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
        .Sum(file => new FileInfo(file).Length);

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void ScheduleInstallerCleanup()
    {
        var executable = Environment.ProcessPath;
        if (executable is null) return;
        var directory = Path.GetFullPath(Path.GetDirectoryName(executable)!);
        var expected = Path.GetFullPath(SetupInstallDirectory);
        if (!directory.Equals(expected, StringComparison.OrdinalIgnoreCase)) return;

        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-WindowStyle");
        info.ArgumentList.Add("Hidden");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add("Start-Sleep -Seconds 2; Remove-Item -LiteralPath $args[0] -Recurse -Force");
        info.ArgumentList.Add(directory);
        Process.Start(info);
    }
}
