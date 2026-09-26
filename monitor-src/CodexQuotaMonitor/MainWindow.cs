using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace CodexQuotaMonitor;

internal sealed class MainWindow : Window
{
	private sealed record Palette(bool IsDark, System.Windows.Media.Brush Surface, System.Windows.Media.Brush Border, System.Windows.Media.Brush Text, System.Windows.Media.Brush Muted, System.Windows.Media.Brush Faint, System.Windows.Media.Brush Track, System.Windows.Media.Brush Active, System.Windows.Media.Brush Green, System.Windows.Media.Brush Warning, System.Windows.Media.Brush Danger)
	{
		public static Palette Dark { get; } = new Palette(IsDark: true, Brush("#F21D1D1D"), Brush("#22FFFFFF"), Brush("#FFF4F4F4"), Brush("#FFAAAAAA"), Brush("#FF747474"), Brush("#17FFFFFF"), Brush("#27FFFFFF"), Brush("#FF74D49B"), Brush("#FFF0AA62"), Brush("#FFF07575"));

		public static Palette Light { get; } = new Palette(IsDark: false, Brush("#F5FFFFFD"), Brush("#1F161612"), Brush("#FF20201E"), Brush("#FF71716D"), Brush("#FF999994"), Brush("#17141410"), Brush("#19141410"), Brush("#FF238655"), Brush("#FFC56E17"), Brush("#FFC74242"));

		private static SolidColorBrush Brush(string value)
		{
			SolidColorBrush solidColorBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));
			solidColorBrush.Freeze();
			return solidColorBrush;
		}

		[CompilerGenerated]
		private Palette(Palette original)
		{
			IsDark = original.IsDark;
			Surface = original.Surface;
			Border = original.Border;
			Text = original.Text;
			Muted = original.Muted;
			Faint = original.Faint;
			Track = original.Track;
			Active = original.Active;
			Green = original.Green;
			Warning = original.Warning;
			Danger = original.Danger;
		}
	}

	private readonly SettingsStore _store = new SettingsStore();

	private readonly UserSettings _settings;

	private readonly AppServerClient _client = new AppServerClient();

	private readonly BurnEstimator _burnEstimator = new BurnEstimator();

	private readonly TokenTelemetryReader _tokenTelemetryReader = new TokenTelemetryReader();

	private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);

	private readonly DispatcherTimer _hostTimer;

	private readonly DispatcherTimer _refreshTimer;

	private readonly DispatcherTimer _clockTimer;

	private readonly DispatcherTimer _telemetryTimer;

	private readonly List<FrameworkElement> _hoverControls = new List<FrameworkElement>();

	private readonly HashSet<string> _notifiedWindows = new HashSet<string>(StringComparer.Ordinal);

	private QuotaSnapshot _snapshot;

	private TokenTelemetrySnapshot _tokenTelemetry = TokenTelemetrySnapshot.Empty;

	private Palette _palette = Palette.Dark;

	private NotifyIcon? _trayIcon;

	private Icon? _trayAvatarIcon;

	private bool _avatarLoadInProgress;

	private DateTime _nextAvatarRefreshUtc = DateTime.MinValue;

	private ControlServer? _controlServer;

	private bool _hostRunning;

	private bool _manualHidden;

	private bool _allowClose;

	private bool _positionReady;

	private string _appliedTheme = string.Empty;

	public MainWindow()
	{
		_settings = _store.LoadSettings();
		_settings.Variant = NormalizeVariant(_settings.Variant);
		QuotaSnapshot quotaSnapshot = _store.LoadSnapshot();
		_snapshot = (((object)quotaSnapshot != null) ? quotaSnapshot with
		{
			IsStale = true,
			Error = "正在刷新…"
		} : QuotaSnapshot.Loading);
		base.Width = 308.0;
		base.Height = 208.0;
		base.MinWidth = 292.0;
		base.MinHeight = 194.0;
		base.WindowStyle = WindowStyle.None;
		base.ResizeMode = ResizeMode.NoResize;
		base.AllowsTransparency = true;
		base.Background = System.Windows.Media.Brushes.Transparent;
		base.ShowInTaskbar = false;
		base.Topmost = true;
		base.WindowStartupLocation = WindowStartupLocation.Manual;
		base.SnapsToDevicePixels = true;
		base.UseLayoutRounding = true;
		base.FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI");
		_hostTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(2.0)
		};
		_hostTimer.Tick += delegate
		{
			CheckHostAndTheme();
		};
		_refreshTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(30.0)
		};
		_refreshTimer.Tick += async delegate
		{
			await RefreshAsync();
		};
		_clockTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(20.0)
		};
		_clockTimer.Tick += delegate
		{
			BuildWindow();
		};
		_telemetryTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1.0)
		};
		_telemetryTimer.Tick += delegate
		{
			RefreshTokenTelemetry();
		};
		base.SourceInitialized += delegate
		{
			RestorePosition();
		};
		base.Loaded += OnLoaded;
		base.Closing += OnClosing;
		base.MouseEnter += delegate
		{
			SetHoverControls(visible: true);
		};
		base.MouseLeave += delegate
		{
			SetHoverControls(visible: false);
		};
		_client.RateLimitsUpdated += delegate
		{
			base.Dispatcher.BeginInvoke((Func<Task>)async delegate
			{
				await RefreshAsync();
			});
		};
		ApplyTheme(force: true);
		BuildWindow();
		CreateTrayIcon();
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		_hostRunning = IsCodexDesktopRunning();
		UpdateHostVisibility();
		_hostTimer.Start();
		_refreshTimer.Start();
		_clockTimer.Start();
		RefreshTokenTelemetry();
		_telemetryTimer.Start();
		_controlServer = new ControlServer(HandleControlAsync);
		if (_hostRunning)
		{
			await RefreshAsync();
		}
	}

	private void OnClosing(object? sender, CancelEventArgs e)
	{
		if (!_allowClose)
		{
			e.Cancel = true;
			HideManually();
		}
	}

	private void CreateTrayIcon()
	{
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
		contextMenuStrip.Items.Add("显示悬浮窗", null, delegate
		{
			base.Dispatcher.BeginInvoke(new Action(ShowManually));
		});
		contextMenuStrip.Items.Add("刷新额度", null, delegate
		{
			base.Dispatcher.BeginInvoke((Func<Task>)async delegate
			{
				await RefreshAsync();
			});
		});
		contextMenuStrip.Items.Add(new ToolStripSeparator());
		ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem("界面样式");
		toolStripMenuItem.DropDownItems.Add("A · 原生清单", null, delegate
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				SelectVariant("A");
			});
		});
		toolStripMenuItem.DropDownItems.Add("B · 双环仪表", null, delegate
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				SelectVariant("B");
			});
		});
		toolStripMenuItem.DropDownItems.Add("C · 预测 HUD", null, delegate
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				SelectVariant("C");
			});
		});
		contextMenuStrip.Items.Add(toolStripMenuItem);
		ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem("主题");
		toolStripMenuItem2.DropDownItems.Add("自动（跟随 Codex/系统）", null, delegate
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				SelectTheme("Auto");
			});
		});
		toolStripMenuItem2.DropDownItems.Add("浅色", null, delegate
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				SelectTheme("Light");
			});
		});
		toolStripMenuItem2.DropDownItems.Add("深色", null, delegate
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				SelectTheme("Dark");
			});
		});
		contextMenuStrip.Items.Add(toolStripMenuItem2);
		contextMenuStrip.Items.Add(new ToolStripSeparator());
		contextMenuStrip.Items.Add("退出", null, delegate
		{
			base.Dispatcher.BeginInvoke((Func<Task>)async delegate
			{
				await ExitApplicationAsync();
			});
		});
		_trayIcon = new NotifyIcon
		{
			Icon = SystemIcons.Information,
			Text = "Codex 额度",
			ContextMenuStrip = contextMenuStrip,
			Visible = false
		};
		_trayIcon.DoubleClick += delegate
		{
			base.Dispatcher.BeginInvoke(new Action(ShowManually));
		};
		_ = LoadTrayAvatarAsync();
	}

	private async Task LoadTrayAvatarAsync()
	{
		if (_avatarLoadInProgress)
		{
			return;
		}

		_avatarLoadInProgress = true;
		try
		{
			Icon? avatar = await AvatarIconProvider.LoadAsync();
			if (avatar == null)
			{
				_nextAvatarRefreshUtc = DateTime.UtcNow.AddMinutes(5.0);
				return;
			}
			if (_trayIcon == null)
			{
				avatar.Dispose();
				return;
			}

			Icon? previous = _trayAvatarIcon;
			_trayAvatarIcon = avatar;
			_trayIcon.Icon = avatar;
			previous?.Dispose();
			_nextAvatarRefreshUtc = DateTime.UtcNow.AddHours(1.0);
		}
		finally
		{
			_avatarLoadInProgress = false;
		}
	}

	private void BuildWindow()
	{
		_hoverControls.Clear();
		Border border = new Border
		{
			CornerRadius = new CornerRadius((_settings.Variant == "C") ? 12 : 15),
			Background = _palette.Surface,
			BorderBrush = _palette.Border,
			BorderThickness = new Thickness(1.0),
			Effect = new DropShadowEffect
			{
				BlurRadius = 30.0,
				ShadowDepth = 8.0,
				Opacity = (_palette.IsDark ? 0.48 : 0.19),
				Color = Colors.Black
			},
			ClipToBounds = true
		};
		Grid grid = new Grid();
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(34.0)
		});
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(1.0, GridUnitType.Star)
		});
		grid.Children.Add(CreateTitleBar());
		FrameworkElement frameworkElement;
		if ((object)_snapshot.ShortWindow == null && (object)_snapshot.LongWindow == null)
		{
			frameworkElement = CreateUnavailableBody();
		}
		else
		{
			string variant = _settings.Variant;
			FrameworkElement frameworkElement2 = ((variant == "B") ? CreateVariantB() : ((!(variant == "C")) ? CreateVariantA() : CreateVariantC()));
			frameworkElement = frameworkElement2;
		}
		Grid body = new Grid();
		body.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(1.0, GridUnitType.Star)
		});
		body.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(25.0)
		});
		body.Children.Add(frameworkElement);
		FrameworkElement telemetry = CreateTokenTelemetryBar();
		Grid.SetRow(telemetry, 1);
		body.Children.Add(telemetry);
		Grid.SetRow(body, 1);
		grid.Children.Add(body);
		border.Child = grid;
		base.Content = border;
		if (base.IsMouseOver)
		{
			SetHoverControls(visible: true);
		}
	}

	private FrameworkElement CreateTokenTelemetryBar()
	{
		Grid grid = new Grid
		{
			Margin = new Thickness(12.0, 0.0, 12.0, 7.0),
			VerticalAlignment = VerticalAlignment.Bottom,
			ToolTip = TelemetryToolTip()
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.Children.Add(Text("缓存命中  " + CacheHitText(), 8.0, _palette.Muted, FontWeights.SemiBold));
		TextBlock speed = Text("输出  " + OutputSpeedText(), 8.0, _palette.Muted, FontWeights.SemiBold);
		speed.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
		Grid.SetColumn(speed, 1);
		grid.Children.Add(speed);
		return grid;
	}

	private string CacheHitText()
	{
		return _tokenTelemetry.CacheHitPercent.HasValue ? $"{_tokenTelemetry.CacheHitPercent.Value:0.#}%" : "—";
	}

	private string OutputSpeedText()
	{
		return _tokenTelemetry.OutputTokensPerSecond.HasValue ? $"{_tokenTelemetry.OutputTokensPerSecond.Value:0.0} tok/s" : "—";
	}

	private string TelemetryToolTip()
	{
		if (!_tokenTelemetry.UpdatedAt.HasValue)
		{
			return "等待 Codex 写入 token 使用记录";
		}
		return $"最近一次模型响应 · 输入 {_tokenTelemetry.InputTokens:N0} · 缓存 {_tokenTelemetry.CachedInputTokens:N0} · 输出 {_tokenTelemetry.OutputTokens:N0} · 用时 {_tokenTelemetry.ResponseDurationSeconds:0.0} 秒";
	}

	private void RefreshTokenTelemetry()
	{
		TokenTelemetrySnapshot latest = _tokenTelemetryReader.ReadLatest();
		if (!Equals(latest, _tokenTelemetry))
		{
			_tokenTelemetry = latest;
			BuildWindow();
		}
	}

	private Grid CreateTitleBar()
	{
		Grid grid = new Grid
		{
			Background = System.Windows.Media.Brushes.Transparent,
			Cursor = System.Windows.Input.Cursors.SizeAll
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs eventArgs)
		{
			if (eventArgs.OriginalSource is DependencyObject child && FindParent<System.Windows.Controls.Button>(child) != null)
			{
				return;
			}
			try
			{
				DragMove();
				SavePosition();
			}
			catch
			{
			}
		};
		StackPanel stackPanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(12.0, 0.0, 0.0, 0.0)
		};
		stackPanel.Children.Add(new Border
		{
			Width = 7.0,
			Height = 7.0,
			CornerRadius = new CornerRadius(4.0),
			Background = (_snapshot.IsStale ? _palette.Warning : _palette.Green),
			Margin = new Thickness(0.0, 0.0, 7.0, 0.0)
		});
		stackPanel.Children.Add(Text("Codex 额度", 11.0, _palette.Text, FontWeights.SemiBold));
		string? planBadge = PlanBadgeText();
		if (planBadge != null)
		{
			stackPanel.Children.Add(Text(planBadge, 7.5, _palette.Green, FontWeights.Bold, new Thickness(7.0, 1.0, 0.0, 0.0)));
		}
		if (_snapshot.IsStale)
		{
			stackPanel.Children.Add(Text("离线", 8.0, _palette.Warning, FontWeights.SemiBold, new Thickness(7.0, 1.0, 0.0, 0.0)));
		}
		grid.Children.Add(stackPanel);
		Border border = new Border
		{
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Padding = new Thickness(2.0),
			CornerRadius = new CornerRadius(7.0),
			Background = _palette.Track,
			BorderBrush = _palette.Border,
			BorderThickness = new Thickness(1.0),
			Visibility = Visibility.Hidden
		};
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal
		};
		string[] array = new string[3] { "A", "B", "C" };
		foreach (string variant in array)
		{
			System.Windows.Controls.Button button = SmallButton(variant, 21.0, 18.0, delegate
			{
				SelectVariant(variant);
			});
			button.FontSize = 8.0;
			button.FontWeight = FontWeights.Bold;
			button.Margin = new Thickness(1.0, 0.0, 0.0, 0.0);
			System.Windows.Controls.Button button2 = button;
			string text = variant;
			string toolTip = ((text == "A") ? "原生清单" : ((!(text == "B")) ? "预测 HUD" : "双环仪表"));
			button2.ToolTip = toolTip;
			if (_settings.Variant == variant)
			{
				button.Background = _palette.Active;
				button.Foreground = _palette.Text;
			}
			stackPanel2.Children.Add(button);
		}
		border.Child = stackPanel2;
		Grid.SetColumn(border, 1);
		grid.Children.Add(border);
		_hoverControls.Add(border);
		StackPanel stackPanel3 = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 7.0, 0.0),
			Visibility = Visibility.Hidden
		};
		stackPanel3.Children.Add(SmallButton("↻", 23.0, 23.0, async delegate
		{
			await RefreshAsync();
		}, "刷新"));
		stackPanel3.Children.Add(SmallButton("⚙", 23.0, 23.0, (Action)ShowSettingsMenu, "设置"));
		stackPanel3.Children.Add(SmallButton("×", 23.0, 23.0, (Action)HideManually, "隐藏"));
		Grid.SetColumn(stackPanel3, 2);
		grid.Children.Add(stackPanel3);
		_hoverControls.Add(stackPanel3);
		return grid;
	}

	private FrameworkElement CreateUnavailableBody()
	{
		StackPanel obj = new StackPanel
		{
			Margin = new Thickness(15.0, 12.0, 15.0, 12.0),
			VerticalAlignment = VerticalAlignment.Center,
			Children =
			{
				(UIElement)Text(_snapshot.Error ?? "暂时无法读取额度", 12.0, _palette.Text, FontWeights.SemiBold),
				(UIElement)Text("请确认 Codex 已登录 ChatGPT，并且 codex.exe 可用。悬浮窗会自动重试。", 9.0, _palette.Muted, FontWeights.Normal, new Thickness(0.0, 7.0, 0.0, 0.0), wrap: true)
			}
		};
		System.Windows.Controls.Button button = SmallButton("立即重试", 78.0, 26.0, async delegate
		{
			await RefreshAsync();
		});
		button.Margin = new Thickness(0.0, 11.0, 0.0, 0.0);
		obj.Children.Add(button);
		return obj;
	}

	private FrameworkElement CreateVariantA()
	{
		Grid obj = new Grid
		{
			Margin = new Thickness(12.0, 0.0, 12.0, 10.0),
			RowDefinitions =
			{
				new RowDefinition
				{
					Height = GridLength.Auto
				},
				new RowDefinition
				{
					Height = GridLength.Auto
				},
				new RowDefinition
				{
					Height = new GridLength(1.0, GridUnitType.Star)
				}
			}
		};
		Grid element = CreateMetricRow(_snapshot.ShortWindow, "5 小时");
		Grid.SetRow(element, 0);
		obj.Children.Add(element);
		if (_snapshot.ShortWindow == null || _snapshot.LongWindow != null)
		{
			Grid element2 = CreateMetricRow(_snapshot.LongWindow, "每周");
			Grid.SetRow(element2, 1);
			obj.Children.Add(element2);
		}
		Grid grid = new Grid
		{
			Margin = new Thickness(0.0, 11.0, 0.0, 0.0)
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(82.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.Children.Add(CreateSparkline());
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0)
		};
		stackPanel.Children.Add(Text(PredictionText(), 10.0, _palette.Text, FontWeights.SemiBold));
		stackPanel.Children.Add(Text(TodayUsageText() + " · " + BurnRateText(), 8.0, _palette.Faint, FontWeights.Normal, new Thickness(0.0, 3.0, 0.0, 0.0)));
		Grid.SetColumn(stackPanel, 1);
		grid.Children.Add(stackPanel);
		grid.Children.Add(new Border
		{
			Height = 1.0,
			Background = _palette.Border,
			VerticalAlignment = VerticalAlignment.Top
		});
		Grid.SetRow(grid, 2);
		obj.Children.Add(grid);
		return obj;
	}

	private Grid CreateMetricRow(RateWindow? window, string fallback)
	{
		double num = window?.RemainingPercent ?? 0.0;
		Grid obj = new Grid
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 3.0),
			RowDefinitions =
			{
				new RowDefinition
				{
					Height = new GridLength(17.0)
				},
				new RowDefinition
				{
					Height = new GridLength(6.0)
				},
				new RowDefinition
				{
					Height = new GridLength(11.0)
				}
			},
			ColumnDefinitions =
			{
				new ColumnDefinition
				{
					Width = new GridLength(54.0)
				},
				new ColumnDefinition
				{
					Width = new GridLength(1.0, GridUnitType.Star)
				},
				new ColumnDefinition
				{
					Width = new GridLength(39.0)
				}
			},
			Children = { (UIElement)Text(DisplayText.WindowName(window, fallback), 9.0, _palette.Muted) }
		};
		TextBlock textBlock = Text(((object)window == null) ? "--" : DisplayText.Percent(num), 10.0, _palette.Text, FontWeights.Bold);
		textBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
		Grid.SetColumn(textBlock, 2);
		obj.Children.Add(textBlock);
		Grid element = CreateProgress(num);
		Grid.SetRow(element, 1);
		Grid.SetColumn(element, 1);
		Grid.SetColumnSpan(element, 2);
		obj.Children.Add(element);
		TextBlock element2 = Text(((object)window == null) ? "等待数据" : DisplayText.Until(window.ResetsAt), 7.5, _palette.Faint);
		Grid.SetRow(element2, 2);
		Grid.SetColumn(element2, 1);
		Grid.SetColumnSpan(element2, 2);
		obj.Children.Add(element2);
		return obj;
	}

	private FrameworkElement CreateVariantB()
	{
		Grid obj = new Grid
		{
			Margin = new Thickness(11.0, 0.0, 11.0, 10.0),
			RowDefinitions =
			{
				new RowDefinition
				{
					Height = new GridLength(77.0)
				},
				new RowDefinition
				{
					Height = new GridLength(1.0, GridUnitType.Star)
				}
			}
		};
		Grid grid = new Grid();
		if (_snapshot.ShortWindow != null && _snapshot.LongWindow == null)
		{
			Grid singleGauge = CreateGaugeCard(_snapshot.ShortWindow, "额度");
			singleGauge.Width = 190.0;
			singleGauge.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
			grid.Children.Add(singleGauge);
		}
		else
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1.0, GridUnitType.Star)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1.0, GridUnitType.Star)
			});
			grid.Children.Add(CreateGaugeCard(_snapshot.ShortWindow, "5 小时"));
			Grid element = CreateGaugeCard(_snapshot.LongWindow, "每周");
			Grid.SetColumn(element, 1);
			grid.Children.Add(element);
		}
		obj.Children.Add(grid);
		Border border = new Border
		{
			CornerRadius = new CornerRadius(9.0),
			Background = _palette.Track,
			Padding = new Thickness(9.0, 6.0, 9.0, 6.0)
		};
		Grid grid2 = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition
				{
					Width = new GridLength(1.0, GridUnitType.Star)
				},
				new ColumnDefinition
				{
					Width = GridLength.Auto
				}
			}
		};
		StackPanel element2 = new StackPanel
		{
			Children =
			{
				(UIElement)Text("预估 · 当前速度", 7.5, _palette.Warning),
				(UIElement)Text(PredictionText(), 10.0, _palette.Text, FontWeights.SemiBold, new Thickness(0.0, 2.0, 0.0, 0.0))
			}
		};
		grid2.Children.Add(element2);
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(12.0, 0.0, 0.0, 0.0)
		};
		stackPanel.Children.Add(Text(BurnRateText(), 9.0, _palette.Text, FontWeights.SemiBold));
		stackPanel.Children.Add(Text(TodayUsageText(), 7.5, _palette.Faint, FontWeights.Normal, new Thickness(0.0, 2.0, 0.0, 0.0)));
		Grid.SetColumn(stackPanel, 1);
		grid2.Children.Add(stackPanel);
		border.Child = grid2;
		Grid.SetRow(border, 1);
		obj.Children.Add(border);
		return obj;
	}

	private Grid CreateGaugeCard(RateWindow? window, string fallback)
	{
		double percentage = window?.RemainingPercent ?? 0.0;
		Grid obj = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition
				{
					Width = new GridLength(66.0)
				},
				new ColumnDefinition
				{
					Width = new GridLength(1.0, GridUnitType.Star)
				}
			}
		};
		Grid grid = CreateGauge(percentage);
		grid.Margin = new Thickness(1.0, 4.0, 0.0, 4.0);
		obj.Children.Add(grid);
		StackPanel stackPanel = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(3.0, 0.0, 0.0, 0.0)
		};
		stackPanel.Children.Add(Text(DisplayText.WindowName(window, fallback), 9.0, _palette.Text, FontWeights.SemiBold));
		stackPanel.Children.Add(Text(((object)window == null) ? "等待数据" : DisplayText.Until(window.ResetsAt).Replace("后重置", "\n后重置"), 7.5, _palette.Faint, FontWeights.Normal, new Thickness(0.0, 4.0, 0.0, 0.0)));
		Grid.SetColumn(stackPanel, 1);
		obj.Children.Add(stackPanel);
		return obj;
	}

	private Grid CreateGauge(double percentage)
	{
		Grid grid = new Grid
		{
			Width = 61.0,
			Height = 61.0
		};
		grid.Children.Add(new Ellipse
		{
			Width = 53.0,
			Height = 53.0,
			Stroke = _palette.Track,
			StrokeThickness = 5.5
		});
		if (percentage > 0.01)
		{
			double num = 26.5;
			System.Windows.Point point = new System.Windows.Point(30.5, 30.5);
			double num2 = Math.Min(359.9, percentage / 100.0 * 360.0);
			System.Windows.Point startPoint = new System.Windows.Point(point.X, point.Y - num);
			double num3 = num2 * Math.PI / 180.0;
			System.Windows.Point point2 = new System.Windows.Point(point.X + num * Math.Sin(num3), point.Y - num * Math.Cos(num3));
			PathFigure pathFigure = new PathFigure
			{
				StartPoint = startPoint,
				IsClosed = false
			};
			pathFigure.Segments.Add(new ArcSegment(point2, new System.Windows.Size(num, num), 0.0, num2 > 180.0, SweepDirection.Clockwise, isStroked: true));
			PathGeometry pathGeometry = new PathGeometry();
			pathGeometry.Figures.Add(pathFigure);
			grid.Children.Add(new Path
			{
				Data = pathGeometry,
				Stroke = QuotaBrush(percentage),
				StrokeThickness = 5.5,
				StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round
			});
		}
		TextBlock textBlock = Text(DisplayText.Percent(percentage), 13.0, _palette.Text, FontWeights.Bold);
		textBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
		textBlock.VerticalAlignment = VerticalAlignment.Center;
		grid.Children.Add(textBlock);
		return grid;
	}

	private FrameworkElement CreateVariantC()
	{
		Grid obj = new Grid
		{
			Margin = new Thickness(12.0, 0.0, 12.0, 8.0),
			RowDefinitions =
			{
				new RowDefinition
				{
					Height = new GridLength(83.0)
				},
				new RowDefinition
				{
					Height = new GridLength(27.0)
				},
				new RowDefinition
				{
					Height = new GridLength(1.0, GridUnitType.Star)
				}
			}
		};
		Grid grid = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition
				{
					Width = new GridLength(1.45, GridUnitType.Star)
				},
				new ColumnDefinition
				{
					Width = new GridLength(0.9, GridUnitType.Star)
				}
			}
		};
		StackPanel stackPanel = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel.Children.Add(Text("预估 · 可继续高强度使用", 7.5, _palette.Warning));
		stackPanel.Children.Add(Text(PredictionDuration(), 24.0, _palette.Text, FontWeights.Bold, new Thickness(0.0, 2.0, 0.0, 0.0)));
		stackPanel.Children.Add(Text(((object)_snapshot.ShortWindow == null) ? "5 小时额度等待数据" : $"{DisplayText.WindowName(_snapshot.ShortWindow, "短期")}剩余 {DisplayText.Percent(_snapshot.ShortWindow.RemainingPercent)} · {DisplayText.Until(_snapshot.ShortWindow.ResetsAt)}", 7.5, _palette.Faint, FontWeights.Normal, new Thickness(0.0, 5.0, 0.0, 0.0)));
		grid.Children.Add(stackPanel);
		Border border = new Border
		{
			BorderBrush = _palette.Border,
			BorderThickness = new Thickness(1.0, 0.0, 0.0, 0.0),
			Padding = new Thickness(11.0, 0.0, 0.0, 0.0)
		};
		StackPanel stackPanel2 = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		if (_snapshot.ShortWindow != null && _snapshot.LongWindow == null)
		{
			border.Padding = new Thickness(0.0);
			stackPanel2.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
			TextBlock planLabel = Text(PlanBadgeText() ?? "单窗口", 28.0, _palette.Green, FontWeights.Bold);
			planLabel.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
			stackPanel2.Children.Add(planLabel);
		}
		else
		{
			double num = _snapshot.LongWindow?.RemainingPercent ?? 0.0;
			stackPanel2.Children.Add(Text(((object)_snapshot.LongWindow == null) ? "--" : DisplayText.Percent(num), 19.0, QuotaBrush(num), FontWeights.Bold));
			stackPanel2.Children.Add(Text("每周额度剩余", 7.5, _palette.Muted, FontWeights.Normal, new Thickness(0.0, 4.0, 0.0, 0.0)));
			stackPanel2.Children.Add(Text(((object)_snapshot.LongWindow == null) ? "等待数据" : DisplayText.Until(_snapshot.LongWindow.ResetsAt), 7.0, _palette.Faint, FontWeights.Normal, new Thickness(0.0, 5.0, 0.0, 0.0)));
		}
		border.Child = stackPanel2;
		Grid.SetColumn(border, 1);
		grid.Children.Add(border);
		obj.Children.Add(grid);
		Grid element = CreateTimeline(_snapshot.ShortWindow?.RemainingPercent ?? 0.0);
		Grid.SetRow(element, 1);
		obj.Children.Add(element);
		Grid grid2 = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition
				{
					Width = new GridLength(1.0, GridUnitType.Star)
				},
				new ColumnDefinition
				{
					Width = GridLength.Auto
				},
				new ColumnDefinition
				{
					Width = GridLength.Auto
				}
			},
			Children = { (UIElement)Text(TodayUsageText(), 7.5, _palette.Faint) }
		};
		TextBlock element2 = Text(BurnRateText(), 7.5, _palette.Faint);
		Grid.SetColumn(element2, 1);
		grid2.Children.Add(element2);
		TextBlock element3 = Text(FreshnessText(), 7.5, _snapshot.IsStale ? _palette.Warning : _palette.Text, FontWeights.SemiBold, new Thickness(12.0, 0.0, 0.0, 0.0));
		Grid.SetColumn(element3, 2);
		grid2.Children.Add(element3);
		Grid.SetRow(grid2, 2);
		obj.Children.Add(grid2);
		return obj;
	}

	private Grid CreateTimeline(double percentage)
	{
		Grid obj = new Grid
		{
			Margin = new Thickness(0.0, 2.0, 0.0, 0.0),
			RowDefinitions =
			{
				new RowDefinition
				{
					Height = new GridLength(13.0)
				},
				new RowDefinition
				{
					Height = new GridLength(11.0)
				}
			}
		};
		Grid grid = CreateProgress(percentage, 3.0);
		grid.VerticalAlignment = VerticalAlignment.Center;
		obj.Children.Add(grid);
		Grid grid2 = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition
				{
					Width = new GridLength(1.0, GridUnitType.Star)
				},
				new ColumnDefinition
				{
					Width = new GridLength(1.0, GridUnitType.Star)
				},
				new ColumnDefinition
				{
					Width = new GridLength(1.0, GridUnitType.Star)
				}
			},
			Children = { (UIElement)Text("本窗口开始", 6.5, _palette.Faint) }
		};
		TextBlock textBlock = Text("现在", 6.5, _palette.Faint);
		textBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
		Grid.SetColumn(textBlock, 1);
		grid2.Children.Add(textBlock);
		TextBlock textBlock2 = Text("预计耗尽", 6.5, _palette.Faint);
		textBlock2.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
		Grid.SetColumn(textBlock2, 2);
		grid2.Children.Add(textBlock2);
		Grid.SetRow(grid2, 1);
		obj.Children.Add(grid2);
		return obj;
	}

	private Grid CreateProgress(double percentage, double height = 5.0)
	{
		return new Grid
		{
			Height = height,
			Background = _palette.Track,
			ClipToBounds = true,
			ColumnDefinitions =
			{
				new ColumnDefinition
				{
					Width = new GridLength(Math.Max(0.001, percentage), GridUnitType.Star)
				},
				new ColumnDefinition
				{
					Width = new GridLength(Math.Max(0.001, 100.0 - percentage), GridUnitType.Star)
				}
			},
			Children = { (UIElement)new Border
			{
				Background = QuotaBrush(percentage),
				CornerRadius = new CornerRadius(height / 2.0)
			} }
		};
	}

	private Canvas CreateSparkline()
	{
		Canvas canvas = new Canvas
		{
			Width = 78.0,
			Height = 25.0
		};
		double[] array = _snapshot.DailyUsage.TakeLast(7).Select((Func<DailyUsage, double>)((DailyUsage item) => item.Tokens)).ToArray();
		if (array.Length < 2)
		{
			array = new double[7] { 1.0, 1.1, 1.05, 1.2, 1.15, 1.3, 1.25 };
		}
		double num = array.Min();
		double num2 = array.Max();
		double num3 = Math.Max(1.0, num2 - num);
		PointCollection pointCollection = new PointCollection();
		for (int num4 = 0; num4 < array.Length; num4++)
		{
			pointCollection.Add(new System.Windows.Point((double)num4 * 78.0 / (double)(array.Length - 1), 23.0 - (array[num4] - num) / num3 * 20.0));
		}
		canvas.Children.Add(new Polyline
		{
			Points = pointCollection,
			Stroke = _palette.Green,
			StrokeThickness = 1.6,
			StrokeLineJoin = PenLineJoin.Round
		});
		return canvas;
	}

	private string PredictionText()
	{
		TimeSpan? estimatedExhaustion = _snapshot.EstimatedExhaustion;
		if (estimatedExhaustion.HasValue)
		{
			TimeSpan valueOrDefault = estimatedExhaustion.GetValueOrDefault();
			RateWindow shortWindow = _snapshot.ShortWindow;
			if ((object)shortWindow != null && DateTimeOffset.Now + valueOrDefault >= shortWindow.ResetsAt)
			{
				return "预估 · 本窗口额度足够";
			}
			return "预估 · " + DisplayText.CompactDuration(valueOrDefault) + " 后耗尽";
		}
		return "预估 · 正在收集消耗速度";
	}

	private string PredictionDuration()
	{
		TimeSpan? estimatedExhaustion = _snapshot.EstimatedExhaustion;
		if (estimatedExhaustion.HasValue)
		{
			TimeSpan valueOrDefault = estimatedExhaustion.GetValueOrDefault();
			RateWindow shortWindow = _snapshot.ShortWindow;
			if ((object)shortWindow != null && DateTimeOffset.Now + valueOrDefault >= shortWindow.ResetsAt)
			{
				return "足够";
			}
			return DisplayText.CompactDuration(valueOrDefault);
		}
		return "计算中";
	}

	private string BurnRateText()
	{
		double? burnRatePerHour = _snapshot.BurnRatePerHour;
		if (burnRatePerHour.HasValue)
		{
			double valueOrDefault = burnRatePerHour.GetValueOrDefault();
			return $"每小时 {valueOrDefault:0.#}%";
		}
		return "速度收集中";
	}

	private string TodayUsageText()
	{
		DateOnly today = DateOnly.FromDateTime(DateTime.Now);
		DailyUsage dailyUsage = _snapshot.DailyUsage.LastOrDefault((DailyUsage entry) => entry.Date == today);
		if ((object)dailyUsage != null)
		{
			return "今日 " + DisplayText.Tokens(dailyUsage.Tokens);
		}
		dailyUsage = _snapshot.DailyUsage.LastOrDefault();
		if ((object)dailyUsage != null)
		{
			return "最近 " + DisplayText.Tokens(dailyUsage.Tokens);
		}
		return "今日统计待同步";
	}

	private string FreshnessText()
	{
		if (!_snapshot.IsStale)
		{
			return "实时";
		}
		if (_snapshot.FetchedAt == DateTimeOffset.MinValue)
		{
			return "等待连接";
		}
		int value = Math.Max(1, (int)(DateTimeOffset.Now - _snapshot.FetchedAt).TotalMinutes);
		return $"{value} 分钟前";
	}

	private string? PlanBadgeText()
	{
		string? plan = _snapshot.PlanType?.Trim().ToLowerInvariant();
		return plan switch
		{
			"pro" or "prolite" => "PRO",
			"plus" => "PLUS",
			"business" or "team" => "BUSINESS",
			"enterprise" => "ENTERPRISE",
			"edu" => "EDU",
			_ => null
		};
	}

	private TextBlock Text(string value, double size, System.Windows.Media.Brush brush, FontWeight? weight = null, Thickness? margin = null, bool wrap = false)
	{
		return new TextBlock
		{
			Text = value,
			FontSize = size,
			Foreground = brush,
			FontWeight = (weight ?? FontWeights.Normal),
			Margin = (margin ?? new Thickness(0.0)),
			TextWrapping = ((!wrap) ? TextWrapping.NoWrap : TextWrapping.Wrap),
			VerticalAlignment = VerticalAlignment.Center
		};
	}

	private System.Windows.Controls.Button SmallButton(string text, double width, double height, Action action, string? tooltip = null)
	{
		System.Windows.Controls.Button button = new System.Windows.Controls.Button();
		button.Content = text;
		button.Width = width;
		button.Height = height;
		button.Padding = new Thickness(0.0);
		button.Margin = new Thickness(1.0, 0.0, 0.0, 0.0);
		button.BorderThickness = new Thickness(0.0);
		button.Background = System.Windows.Media.Brushes.Transparent;
		button.Foreground = _palette.Muted;
		button.Cursor = System.Windows.Input.Cursors.Hand;
		button.Focusable = false;
		button.ToolTip = tooltip;
		button.Click += delegate(object _, RoutedEventArgs eventArgs)
		{
			eventArgs.Handled = true;
			action();
		};
		return button;
	}

	private System.Windows.Controls.Button SmallButton(string text, double width, double height, Func<Task> action, string? tooltip = null)
	{
		return SmallButton(text, width, height, delegate
		{
			action();
		}, tooltip);
	}

	private void ShowSettingsMenu()
	{
		ContextMenu contextMenu = new ContextMenu();
		contextMenu.Placement = PlacementMode.MousePoint;
		contextMenu.StaysOpen = false;
		contextMenu.Items.Add(Menu("主题 · 自动", delegate
		{
			SelectTheme("Auto");
		}));
		contextMenu.Items.Add(Menu("主题 · 浅色", delegate
		{
			SelectTheme("Light");
		}));
		contextMenu.Items.Add(Menu("主题 · 深色", delegate
		{
			SelectTheme("Dark");
		}));
		contextMenu.Items.Add(new Separator());
		contextMenu.Items.Add(Menu("刷新额度", async delegate
		{
			await RefreshAsync();
		}));
		contextMenu.Items.Add(Menu("退出监控器", async delegate
		{
			await ExitApplicationAsync();
		}));
		contextMenu.Closed += delegate
		{
			SetHoverControls(base.IsMouseOver);
		};
		contextMenu.IsOpen = true;
	}

	private MenuItem Menu(string header, Action action)
	{
		MenuItem menuItem = new MenuItem();
		menuItem.Header = header;
		menuItem.Click += delegate
		{
			action();
		};
		return menuItem;
	}

	private MenuItem Menu(string header, Func<Task> action)
	{
		return Menu(header, delegate
		{
			action();
		});
	}

	private void SetHoverControls(bool visible)
	{
		foreach (FrameworkElement hoverControl in _hoverControls)
		{
			hoverControl.Visibility = ((!visible) ? Visibility.Hidden : Visibility.Visible);
		}
	}

	private void SelectVariant(string variant)
	{
		_settings.Variant = NormalizeVariant(variant);
		_store.SaveSettings(_settings);
		BuildWindow();
	}

	private static string NormalizeVariant(string? variant)
	{
		string text = variant?.Trim().ToUpperInvariant();
		bool flag;
		switch (text)
		{
		case "A":
		case "B":
		case "C":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			return "A";
		}
		return text;
	}

	private void SelectTheme(string theme)
	{
		_settings.Theme = theme;
		_store.SaveSettings(_settings);
		ApplyTheme(force: true);
		BuildWindow();
	}

	private void ApplyTheme(bool force = false)
	{
		string text = ((!_settings.Theme.Equals("Auto", StringComparison.OrdinalIgnoreCase)) ? _settings.Theme : (SystemUsesLightTheme() ? "Light" : "Dark"));
		if (force || !text.Equals(_appliedTheme, StringComparison.OrdinalIgnoreCase))
		{
			_appliedTheme = text;
			_palette = (text.Equals("Light", StringComparison.OrdinalIgnoreCase) ? Palette.Light : Palette.Dark);
		}
	}

	private static bool SystemUsesLightTheme()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
			return registryKey?.GetValue("AppsUseLightTheme") is int num && num != 0;
		}
		catch
		{
			return false;
		}
	}

	private async Task RefreshAsync()
	{
		if (!(await _refreshLock.WaitAsync(0)))
		{
			return;
		}
		try
		{
			using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20.0));
			QuotaSnapshot snapshot = await _client.ReadQuotaAsync(timeout.Token);
			_snapshot = _burnEstimator.Apply(snapshot);
			_store.SaveSnapshot(_snapshot);
			BuildWindow();
			MaybeNotifyLowQuota();
		}
		catch (Exception exception)
		{
			QuotaSnapshot quotaSnapshot = ((_snapshot.FetchedAt == DateTimeOffset.MinValue) ? _store.LoadSnapshot() : _snapshot);
			_snapshot = (((object)quotaSnapshot == null) ? QuotaSnapshot.Loading with
			{
				Error = FriendlyError(exception),
				IsStale = true
			} : quotaSnapshot with
			{
				Error = FriendlyError(exception),
				IsStale = true
			});
			BuildWindow();
		}
		finally
		{
			_refreshLock.Release();
		}
	}

	private static string FriendlyError(Exception exception)
	{
		if (exception.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase) || exception.Message.Contains("login", StringComparison.OrdinalIgnoreCase))
		{
			return "Codex 尚未登录 ChatGPT";
		}
		if (exception.Message.Length <= 90)
		{
			return exception.Message;
		}
		return "暂时无法刷新额度";
	}

	private void MaybeNotifyLowQuota()
	{
		if (_trayIcon == null)
		{
			return;
		}
		RateWindow rateWindow = (from window in new RateWindow[2] { _snapshot.ShortWindow, _snapshot.LongWindow }
			where (object)window != null && window.RemainingPercent <= 10.0
			orderby window.RemainingPercent
			select window).FirstOrDefault();
		if ((object)rateWindow != null)
		{
			string item = $"{rateWindow.WindowDurationMins}:{rateWindow.ResetsAt.ToUnixTimeSeconds()}";
			if (_notifiedWindows.Add(item))
			{
				_trayIcon.BalloonTipTitle = "Codex 额度即将用尽";
				_trayIcon.BalloonTipText = $"{DisplayText.WindowName(rateWindow, "额度")}仅剩 {DisplayText.Percent(rateWindow.RemainingPercent)}，{DisplayText.Until(rateWindow.ResetsAt)}。";
				_trayIcon.BalloonTipIcon = ToolTipIcon.Warning;
				_trayIcon.ShowBalloonTip(5000);
			}
		}
	}

	private void CheckHostAndTheme()
	{
		bool flag = IsCodexDesktopRunning();
		if (flag && DateTime.UtcNow >= _nextAvatarRefreshUtc)
		{
			_ = LoadTrayAvatarAsync();
		}
		if (flag != _hostRunning)
		{
			_hostRunning = flag;
			if (flag)
			{
				_manualHidden = false;
				RefreshAsync();
			}
			UpdateHostVisibility();
		}
		string appliedTheme = _appliedTheme;
		ApplyTheme();
		if (appliedTheme != _appliedTheme)
		{
			BuildWindow();
		}
	}

	private static bool IsCodexDesktopRunning()
	{
		try
		{
			return Process.GetProcessesByName("ChatGPT").Any(delegate(Process process)
			{
				using (process)
				{
					try
					{
						return process.MainModule?.FileName.Contains("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) ?? false;
					}
					catch
					{
						return true;
					}
				}
			});
		}
		catch
		{
			return false;
		}
	}

	private void UpdateHostVisibility()
	{
		if (_trayIcon != null)
		{
			_trayIcon.Visible = _hostRunning;
		}
		if (_hostRunning && !_manualHidden)
		{
			Show();
			base.Topmost = true;
		}
		else
		{
			Hide();
		}
	}

	private void ShowManually()
	{
		_manualHidden = false;
		Show();
		base.Topmost = true;
	}

	private void HideManually()
	{
		_manualHidden = true;
		Hide();
	}

	private async Task<string> HandleControlAsync(string command)
	{
		TaskCompletionSource<string> completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		base.Dispatcher.BeginInvoke((Func<Task>)async delegate
		{
			try
			{
				switch (command)
				{
				case "show":
					ShowManually();
					break;
				case "hide":
					HideManually();
					break;
				case "toggle":
					if (base.IsVisible)
					{
						HideManually();
					}
					else
					{
						ShowManually();
					}
					break;
				case "refresh":
					await RefreshAsync();
					break;
				case "exit":
					completion.TrySetResult(JsonSerializer.Serialize(new
					{
						ok = true,
						command = command
					}));
					base.Dispatcher.BeginInvoke((Func<Task>)async delegate
					{
						await ExitApplicationAsync();
					}, DispatcherPriority.Background);
					return;
				case "variant-a":
					SelectVariant("A");
					break;
				case "variant-b":
					SelectVariant("B");
					break;
				case "variant-c":
					SelectVariant("C");
					break;
				case "theme-auto":
					SelectTheme("Auto");
					break;
				case "theme-light":
					SelectTheme("Light");
					break;
				case "theme-dark":
					SelectTheme("Dark");
					break;
				case "status":
					completion.TrySetResult(StatusJson());
					return;
				default:
					completion.TrySetResult(JsonSerializer.Serialize(new
					{
						ok = false,
						error = "unknown command"
					}));
					return;
				}
				completion.TrySetResult(JsonSerializer.Serialize(new
				{
					ok = true,
					command = command
				}));
			}
			catch (Exception ex)
			{
				completion.TrySetResult(JsonSerializer.Serialize(new
				{
					ok = false,
					error = ex.Message
				}));
			}
		});
		return await completion.Task;
	}

	private string StatusJson()
	{
		return JsonSerializer.Serialize(new
		{
			ok = true,
			variant = _settings.Variant,
			theme = _settings.Theme,
			planType = _snapshot.PlanType,
			stale = _snapshot.IsStale,
			fetchedAt = _snapshot.FetchedAt,
			shortWindow = WindowStatus(_snapshot.ShortWindow),
			longWindow = WindowStatus(_snapshot.LongWindow),
			burnRatePerHour = _snapshot.BurnRatePerHour,
			estimatedExhaustion = _snapshot.EstimatedExhaustion?.ToString(),
			cacheHitPercent = _tokenTelemetry.CacheHitPercent,
			outputTokensPerSecond = _tokenTelemetry.OutputTokensPerSecond,
			telemetryInputTokens = _tokenTelemetry.InputTokens,
			telemetryCachedInputTokens = _tokenTelemetry.CachedInputTokens,
			telemetryOutputTokens = _tokenTelemetry.OutputTokens,
			telemetryResponseDurationSeconds = _tokenTelemetry.ResponseDurationSeconds,
			telemetryUpdatedAt = _tokenTelemetry.UpdatedAt,
			error = _snapshot.Error
		});
	}

	private static object? WindowStatus(RateWindow? window)
	{
		if ((object)window != null)
		{
			return new
			{
				usedPercent = window.UsedPercent,
				remainingPercent = window.RemainingPercent,
				windowDurationMins = window.WindowDurationMins,
				resetsAt = window.ResetsAt
			};
		}
		return null;
	}

	private async Task ExitApplicationAsync()
	{
		_allowClose = true;
		_hostTimer.Stop();
		_refreshTimer.Stop();
		_clockTimer.Stop();
		_telemetryTimer.Stop();
		_trayIcon?.Dispose();
		_trayAvatarIcon?.Dispose();
		if (_controlServer != null)
		{
			await _controlServer.DisposeAsync();
		}
		await _client.DisposeAsync();
		Close();
		System.Windows.Application.Current.Shutdown();
	}

	private void RestorePosition()
	{
		Screen screen = Screen.AllScreens.FirstOrDefault((Screen candidate) => candidate.DeviceName.Equals(_settings.LastMonitor, StringComparison.OrdinalIgnoreCase)) ?? Screen.PrimaryScreen ?? Screen.AllScreens.First();
		DpiScale dpi = VisualTreeHelper.GetDpi(this);
		System.Drawing.Rectangle workingArea = screen.WorkingArea;
		if (_settings.Positions.TryGetValue(screen.DeviceName, out WindowPosition value))
		{
			base.Left = Math.Clamp(value.Left, (double)workingArea.Left / dpi.DpiScaleX, (double)workingArea.Right / dpi.DpiScaleX - base.Width);
			base.Top = Math.Clamp(value.Top, (double)workingArea.Top / dpi.DpiScaleY, (double)workingArea.Bottom / dpi.DpiScaleY - base.Height);
		}
		else
		{
			base.Left = (double)workingArea.Right / dpi.DpiScaleX - base.Width - 20.0;
			base.Top = (double)workingArea.Bottom / dpi.DpiScaleY - base.Height - 20.0;
		}
		_positionReady = true;
	}

	private void SavePosition()
	{
		if (_positionReady)
		{
			Screen screen = Screen.FromHandle(new WindowInteropHelper(this).Handle);
			_settings.LastMonitor = screen.DeviceName;
			_settings.Positions[screen.DeviceName] = new WindowPosition
			{
				Left = base.Left,
				Top = base.Top
			};
			_store.SaveSettings(_settings);
		}
	}

	private System.Windows.Media.Brush QuotaBrush(double remaining)
	{
		if (remaining <= 10.0)
		{
			return _palette.Danger;
		}
		if (remaining <= 20.0)
		{
			return _palette.Warning;
		}
		return _palette.Green;
	}

	private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
	{
		for (DependencyObject dependencyObject = child; dependencyObject != null; dependencyObject = VisualTreeHelper.GetParent(dependencyObject))
		{
			if (dependencyObject is T result)
			{
				return result;
			}
		}
		return null;
	}
}
