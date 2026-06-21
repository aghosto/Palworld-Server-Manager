using Hardcodet.Wpf.TaskbarNotification;
using ModernWpf;
using ModernWpf.Controls;
using PalworldServerManager;
using PalworldServerManager.Controls;
using PalworldServerManager.RCON;
using PalworldServerManager.REST;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using static PalworldServerManager.Log;

namespace PalworldServerManager;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainSettings SsmSettings = new();
    private static dWebhook DiscordSender = new();
    private static HttpClient HttpClient = new();
    private PeriodicTimer? AutoUpdateTimer;
    private PeriodicTimer? AutoDelectSaveTimer;
    private RemoteConClient RCONClient = new();
    private DispatcherTimer _autoRestartTimer;
    private bool _sentRestart10Min = false;
    private bool _sentRestart5Min = false;
    private bool _sentRestart1Min = false;
    private DispatcherTimer _playerRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource _updateCts;

    private string _activeLogType;
    private const int MAX_LOG_LINES = 500;
    private DispatcherTimer _logUpdateTimer;
    private Dictionary<string, LogType> _logTagToType;
    private Dictionary<LogType, RichTextBox> _logTypeToTexbox;
    private Dictionary<LogType, CheckBox> _logTypeToCheckbox;
    private Dictionary<LogType, FileSystemWatcher> _logWatchers = new Dictionary<LogType, FileSystemWatcher>();
    private Dictionary<LogType, long> _lastFileSizes = new Dictionary<LogType, long>();

    // 当前选中的服务器
    private Server _currentServer;

    private SSMPathManager _ssmPathManager;
    private ObservableCollection<PlayerDisplayInfo> _players = new();
    private ObservableCollection<PlayerDisplayInfo> _bannedPlayers = new();

    public MainWindow()
    {
        Process currentProcess = Process.GetCurrentProcess();
        Process[] processes = Process.GetProcessesByName(currentProcess.ProcessName);

        if (processes.Length > 1)
        {
            foreach (Process process in processes)
            {
                if (process.Id != currentProcess.Id)
                {
                    ShowWindow(process.MainWindowHandle, 9);
                    SetForegroundWindow(process.MainWindowHandle);

                    Environment.Exit(0);
                    return;
                }
            }
        }

        if (!File.Exists(Directory.GetCurrentDirectory() + @"\PSMSettings.json"))
            MainSettings.Save(SsmSettings);
        else
            SsmSettings = MainSettings.LoadManagerSettings();
        DataContext = SsmSettings;

        if (SsmSettings.AppSettings.DarkMode == true)
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
        else
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;

        InitializeComponent();
        Closing += MainWindow_Closing;
        Loaded += MainWindow_Loaded;

        if (SsmSettings.Servers.Count != 0)
        {
            _logUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _logUpdateTimer.Tick += LogUpdateTimer_Tick;
            _logUpdateTimer.Start();
        }

        _logTypeToTexbox = new Dictionary<LogType, RichTextBox>
        {
            { LogType.WSServer, PalworldLogTextBox },
            { LogType.MainConsole, MainMenuConsoleTextBox },
        };

        _logTypeToCheckbox = new Dictionary<LogType, CheckBox>
        {
            { LogType.WSServer, AutoScrollPalworldLog },
            { LogType.MainConsole, AutoScrollMainConsole },
        };
        
        _logTagToType = new Dictionary<string, LogType>
        {
            { "WSServer", LogType.WSServer },
            { "PlayerData", LogType.PlayerData },
            { "MainConsole", LogType.MainConsole },
        };

        // 绑定自动滚动复选框事件
        AutoScrollPalworldLog.Checked += AutoScrollCheckBox_CheckedChanged;
        AutoScrollPalworldLog.Unchecked += AutoScrollCheckBox_CheckedChanged;
        SsmSettings.Servers.CollectionChanged += Servers_CollectionChanged;
        SsmSettings.AppSettings.PropertyChanged += AppSettings_PropertyChanged;

        ServerTabControl.SelectionChanged += async (s, e) =>
        {
            if (SsmSettings.Servers.Count == 0)
                return;
            if (ServerTabControl.SelectedItem is Server selectedServer)
            {
                _currentServer = selectedServer;
                _ssmPathManager = new (Directory.GetCurrentDirectory(), _currentServer);
                if (!string.IsNullOrEmpty(_activeLogType))
                {
                    if (_activeLogType == "PlayerData")
                    {
                        LoadBannedPlayersFromFile();
                        await RefreshPlayersAsync();
                        return;
                    }
                    else
                        LoadLogByType(_logTagToType[_activeLogType], true);
                }
                if (_currentServer.Runtime.State == ServerRuntime.ServerState.更新中)
                {
                    UpdateButton.IsEnabled = true;
                    UpdateButtonText.Text = "取消更新";
                }
                else if (_currentServer.Runtime.State == ServerRuntime.ServerState.已停止)
                {
                    UpdateButton.IsEnabled = true;
                    UpdateButtonText.Text = "更新服务器";
                }
                else if (_currentServer.Runtime.State == ServerRuntime.ServerState.运行中)
                {
                    UpdateButton.IsEnabled = false;
                    UpdateButtonText.Text = "更新服务器";
                }
            }
        };

        //SsmSettings.AppSettings.Version = new AppSettings().Version;

        ShowLogDefault($"幻兽帕鲁服务端管理器(PSM)启动成功。");
        if (SsmSettings.Servers.Count > 0)
            ShowLogDefault($"{SsmSettings.Servers.Count} 个服务器从设置中加载成功。");
        else
            ShowLogWarning("未找到服务器，请点击\"添加服务器\"以开始使用。");

        SetupServerAutoUpdateTimer();
        InitAutoRestartTimer();
        InitPlayerRefreshTimer();

        //if (File.Exists("SSMUpdater.exe") && File.Exists("SSMUpdater.deps.json") && File.Exists("SSMUpdater.dll") && File.Exists("SSMUpdater.runtimeconfig.json"))
        //{
        //    File.Delete("SSMUpdater.exe");
        //    File.Delete("SSMUpdater.dll");
        //    File.Delete("SSMUpdater.deps.json");
        //    File.Delete("SSMUpdater.runtimeconfig.json");
        //    ShowLogMsg($"旧版更新程序清理完成。", Brushes.Gray);
        //}

        if (SsmSettings.AppSettings.AutoUpdateApp == true)
            LookForAppUpdate();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RestoreRunningServers();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        MainSettings mainSettings = MainSettings.LoadManagerSettings();

        switch (mainSettings.AppSettings.CloseExecuteSelect)
        {
            case 0:
                TrayIcon.Visibility = Visibility.Collapsed;
                TrayIcon.Dispose();
                break;

            case 1:
                e.Cancel = true;
                MinimizeToTray();
                break;
        }
    }

    #region MinimizeAndClose

    private void MinimizeToTray()
    {
        Hide();
        TrayIcon.Visibility = Visibility.Visible;
        TrayIcon.ShowBalloonTip("已最小化", "程序在托盘运行中", BalloonIcon.Info);
    }

    private void TrayIcon_ShowWindow(object sender, RoutedEventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        TrayIcon.Visibility = Visibility.Visible;
        Activate();
    }

    private void TrayIcon_Exit(object sender, RoutedEventArgs e)
    {
        TrayIcon.Visibility = Visibility.Collapsed;
        TrayIcon.Dispose();

        Closing -= MainWindow_Closing;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (TrayIcon != null)
        {
            TrayIcon.Visibility = Visibility.Collapsed;
            TrayIcon.Dispose();
            TrayIcon = null;
        }
        Application.Current.Shutdown();
    }

    #endregion MinimizeAndClose

    private void AutoScrollCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && !string.IsNullOrEmpty(_activeLogType))
        {
            // 找到与当前复选框关联的日志类型
            LogType logType = new();
            foreach (var pair in _logTypeToCheckbox)
            {
                if (pair.Value == checkBox)
                {
                    logType = pair.Key;
                    break;
                }
            }

            if (logType == _logTagToType[_activeLogType] && checkBox.IsChecked == true)
            {
                _logTypeToTexbox[logType].ScrollToEnd();
            }
        }
    }

    // 日志标签页切换事件
    private async void LogTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource is not TabControl)
            return;

        if (_currentServer == null)
            return;

        if (LogTabControl.SelectedItem is TabItem selectedTab && selectedTab.Tag is string logType)
        {
            foreach (var watcher in _logWatchers.Values)
            {
                watcher.EnableRaisingEvents = false;
            }
            _activeLogType = logType;
            if (_activeLogType == "PlayerData")
            {
                if (File.Exists(_ssmPathManager.BanListPath))
                    LoadBannedPlayersFromFile();
                await RefreshPlayersAsync();
                return;
            }
            else
                LoadLogByType(_logTagToType[_activeLogType], forceRefresh: true);

            if (_currentServer.Runtime.State == ServerRuntime.ServerState.运行中)
                StartActiveLogWatcher();
        }
    }

    #region Timers

    private void LogUpdateTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            foreach (var server in SsmSettings.Servers)
            {
                if (server.Runtime.State == ServerRuntime.ServerState.运行中 && server.Runtime.Process != null)
                {
                    try
                    {
                        if (server.Runtime.Process.HasExited)
                            ServerProcessExited(server.Runtime.Process, EventArgs.Empty, server);
                    }
                    catch { }
                }
            }

            if (!_logTagToType.TryGetValue(_activeLogType, out LogType logType))
                return;

            if (logType == LogType.MainConsole)
                return;

            if (!File.Exists(_ssmPathManager.LogsPath))
                return;

            //if (_currentServer == null || _currentServer.Runtime?.State != ServerRuntime.ServerState.运行中)
            //    return;

            //string relativePath = Path.Combine(_currentServer.Path, _logPath);

            if (File.Exists(_ssmPathManager.LogsPath))
            {
                long currentSize = new FileInfo(_ssmPathManager.LogsPath).Length;
                bool sizeChanged = !_lastFileSizes.TryGetValue(logType, out long lastSize) || currentSize != lastSize;
                bool forceUpdate = DateTime.Now.Second % 10 == 0;

                if (sizeChanged || forceUpdate)
                {
                    _lastFileSizes[logType] = currentSize;
                    OnLogFileChanged(logType);
                }
            }
        }
        catch (Exception ex)
        {
            ShowLogError($"定时器检查日志更新失败：{ex.Message}");
        }
    }

    public void InitPlayerRefreshTimer()
    {
        if (_playerRefreshTimer != null)
        {
            _playerRefreshTimer.Stop();
            _playerRefreshTimer.Tick -= AutoRestartTimer_Tick;
            _playerRefreshTimer = null;
        }

        _playerRefreshTimer = new DispatcherTimer();
        _playerRefreshTimer.Interval = TimeSpan.FromSeconds(30);
        _playerRefreshTimer.Tick += async (_, _) =>
        {
            if (LogTabControl.SelectedItem == PlayerTab && _currentServer.Runtime.State == ServerRuntime.ServerState.运行中 && AutoRefreshPlayerCheckBox.IsChecked == true)
            {
                await RefreshPlayersAsync();
            }
        };
        _playerRefreshTimer.Start();
    }

    public void SetupServerAutoUpdateTimer()
    {
        if (SsmSettings.AppSettings.AutoUpdate == true)
        {
            AutoUpdateTimer = new PeriodicTimer(TimeSpan.FromMinutes(SsmSettings.AppSettings.AutoUpdateInterval));
            AutoUpdateLoop();
        }
    }

    public void InitAutoRestartTimer()
    {
        if (_autoRestartTimer != null)
        {
            _autoRestartTimer.Stop();
            _autoRestartTimer.Tick -= AutoRestartTimer_Tick;
            _autoRestartTimer = null;
        }

        if (!SsmSettings.AppSettings.EnableAutoRestart)
        {
            ShowLogMsg("全局自动重启：已关闭", Brushes.Gray);
            return;
        }

        _autoRestartTimer = new DispatcherTimer();
        _autoRestartTimer.Interval = TimeSpan.FromSeconds(1);
        _autoRestartTimer.Tick += AutoRestartTimer_Tick;
        _autoRestartTimer.Start();

        // 每天重置公告标记
        _sentRestart10Min = false;
        _sentRestart5Min = false;
        _sentRestart1Min = false;

        int h = SsmSettings.AppSettings.AutoRestartHour;
        int m = SsmSettings.AppSettings.AutoRestartMin;
        int s = SsmSettings.AppSettings.AutoRestartSec;

        ShowLogMsg($"全局自动重启已启用 → 每天 {h:D2}:{m:D2}:{s:D2}", Brushes.LimeGreen);
    }

    private async void AutoRestartTimer_Tick(object sender, EventArgs e)
    {
        try
        {
            if (!SsmSettings.AppSettings.EnableAutoRestart)
                return;

            var now = DateTime.Now;
            int targetH = SsmSettings.AppSettings.AutoRestartHour;
            int targetM = SsmSettings.AppSettings.AutoRestartMin;
            int targetS = SsmSettings.AppSettings.AutoRestartSec;

            DateTime targetTime = new DateTime(now.Year, now.Month, now.Day, targetH, targetM, targetS);
            TimeSpan left = targetTime - now;

            var runningServers = SsmSettings.Servers
                .Where(s => s.Runtime.State == ServerRuntime.ServerState.运行中)
                .ToList();

            if (left.TotalMinutes <= 10 && left.TotalMinutes > 5 && !_sentRestart10Min)
            {
                _sentRestart10Min = true;
                string msg = "【服务器通知】服务器将在 10 分钟后自动重启，请尽快安全下线！";
                foreach (var server in runningServers)
                    await RCONClient.SendRestartAnnounceToSingleServer(server, msg);
            }

            if (left.TotalMinutes <= 5 && left.TotalMinutes > 1 && !_sentRestart5Min)
            {
                _sentRestart5Min = true;
                string msg = "【服务器通知】服务器将在 5 分钟后自动重启！";
                foreach (var server in runningServers)
                    await RCONClient.SendRestartAnnounceToSingleServer(server, msg);
            }

            if (left.TotalMinutes <= 1 && left.TotalSeconds > 10 && !_sentRestart1Min)
            {
                _sentRestart1Min = true;
                string msg = "【服务器通知】服务器将在 1 分钟后立即重启，请立刻下线！";
                foreach (var server in runningServers)
                    await RCONClient.SendRestartAnnounceToSingleServer(server, msg);
            }

            if (now.Hour == targetH && now.Minute == targetM && now.Second == targetS)
            {
                _sentRestart10Min = false;
                _sentRestart5Min = false;
                _sentRestart1Min = false;

                AutoRestart();

                _autoRestartTimer.Stop();
                await Task.Delay(1000);
                _autoRestartTimer.Start();
            }
        }
        catch { }
    }

    #endregion Timer

    private bool _isLoadingLog = false;
    // 根据窗口类型加载日志文件
    private async void LoadLogByType(LogType logType, bool forceRefresh = false)
    {
        if (_isLoadingLog) return;
        if (_currentServer == null) return;

        if (logType == LogType.MainConsole)
        {
            RichTextBox mainLogTextBox = _logTypeToTexbox[logType];
            if (_logTypeToCheckbox[logType].IsChecked == true)
            {
                mainLogTextBox.ScrollToEnd();
            }
            return;
        }
        else if (logType == LogType.WSServer)
        {
            try
            {
                _isLoadingLog = true;
                RichTextBox logBox = _logTypeToTexbox[logType];
                string fullPath = _ssmPathManager.LogsPath;

                bool needLoad = await Task.Run(() =>
                {
                    if (forceRefresh) return true;
                    if (!_lastFileSizes.TryGetValue(logType, out long lastSize)) return true;

                    try
                    {
                        return new FileInfo(fullPath).Length != lastSize;
                    }
                    catch
                    {
                        return false;
                    }
                });

                if (!needLoad)
                {
                    if (_logTypeToCheckbox[logType].IsChecked == true)
                    {
                        logBox.ScrollToEnd();
                    }
                    _isLoadingLog = false;
                    return;
                }

                string[] lines = await Task.Run(() =>
                {
                    if (!File.Exists(fullPath))
                    {
                        return new[] {$"日志文件不存在：{fullPath} 请确保服务器有正常启动过至少一次"};
                    }
                    return ReadLastNLines(fullPath, MAX_LOG_LINES);
                });

                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        logBox.Document.Blocks.Clear();

                        foreach (string line in lines)
                        {
                            AppendLogLine(logType, line);
                        }

                        _lastFileSizes[logType] = new FileInfo(fullPath).Length;

                        ShowLogMsg($"已加载最近 {lines.Length} 行日志", Brushes.Gray, logType);

                        if (_logTypeToCheckbox[logType].IsChecked == true)
                        {
                            logBox.ScrollToEnd();
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowLogError("加载失败：当前游戏并不存在Log文件！", logType);
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                ShowLogError($"加载失败：{ex.Message}", logType);
            }
            finally
            {
                _isLoadingLog = false;
            }
        }
    }

    // 读取文件的最后N行
    private string[] ReadLastNLines(string filePath, int lineCount)
    {
        List<string> lines = new List<string>();

        try
        {
            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                // 强制刷新流，确保读取最新内容
                stream.Position = 0;
                reader.DiscardBufferedData();

                string[] buffer = new string[lineCount];
                int bufferIndex = 0;
                int totalLines = 0;

                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    if (line != null)
                    {
                        buffer[bufferIndex] = line;
                        bufferIndex = (bufferIndex + 1) % lineCount;
                        totalLines++;
                    }
                }

                int startIndex = totalLines > lineCount ? bufferIndex : 0;
                int count = Math.Min(lineCount, totalLines);

                for (int i = 0; i < count; i++)
                {
                    string line = buffer[(startIndex + i) % lineCount];
                    if (!string.IsNullOrEmpty(line))
                    {
                        lines.Add(line);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            lines.Clear();
            lines.Add($"[警告] 读取日志失败：{ex.Message}");
        }

        return lines.ToArray();
    }

    private void OnLogFileChanged(LogType logType)
    {
        if (_currentServer?.Runtime?.State != ServerRuntime.ServerState.运行中)
            return;

        if (!File.Exists(_ssmPathManager.LogsPath)) return;

        try
        {
            var logTag = _logTagToType.FirstOrDefault(t => t.Value == logType).Key;
            if (!string.IsNullOrEmpty(logTag) && _activeLogType == logTag)
            {
                Dispatcher.Invoke(() => LoadLogByType(logType, forceRefresh: true));
            }
        }
        catch (Exception ex)
        {
            ShowLogError($"更新 {logType} 日志失败: {ex.Message}");
        }
    }

    private async Task RestoreRunningServers()
    {
        int foundServers = 0;
        foreach (var server in SsmSettings.Servers)
        {
            try
            {
                string serverExeFolder = Path.Combine(server.Path, "Pal", "Binaries", "Win64");
                int pid = GetPalServerPidByPath(serverExeFolder);

                if (pid > 0)
                {
                    Process realProcess = Process.GetProcessById(pid);

                    if (!realProcess.HasExited)
                    {
                        realProcess.EnableRaisingEvents = true;
                        realProcess.Exited += (s, e) => ServerProcessExited(s, e, server);
                        server.Runtime.Process = realProcess;
                        server.Runtime.Pid = pid;
                        server.Runtime.State = ServerRuntime.ServerState.运行中;

                        foundServers++;
                    }
                }
                else
                {
                    server.Runtime.State = ServerRuntime.ServerState.已停止;
                }
            }
            catch { }
        }

        if (foundServers > 0)
        {
            ShowLogDefault($"{foundServers} 个服务器正在运行。");
        }

        foreach (Server server in SsmSettings.Servers)
        {
            if (server.AutoStart == true && server.Runtime.State == ServerRuntime.ServerState.已停止)
            {
                await StartServer(server);
                ShowLogDefault($"{server.ssmServerName} 正在自启动");
            }
        }
        await Task.CompletedTask;
    }

    public async Task ShowErrorDialog(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "操作失败",
            Content = message,
            CloseButtonText = "确定"
        };
        await dialog.ShowAsync();
    }

    private void AppendLogLine(LogType logType, string line)
    {
        var paragraph = new Paragraph();
        paragraph.Foreground = GetLogColor(line);
        paragraph.Inlines.Add(new Run(line));
        paragraph.Margin = new Thickness(2);
        _logTypeToTexbox[logType].Document.Blocks.Add(paragraph);
    }

    public void InternalShowLogMsg(LogType logType, string message, Brush color)
    {
        RichTextBox targetTextBox = _logTypeToTexbox[logType];
        if (targetTextBox != null)
        {
            string timestampedMessage = $"[{GetTimestamp("log")}]  {message}";
            Paragraph paragraph = new Paragraph(new Run(timestampedMessage));
            paragraph.Foreground = color;
            paragraph.Margin = new Thickness(0);

            targetTextBox.Document.Blocks.Add(paragraph);
            if (_logTypeToCheckbox[logType]?.IsChecked == true)
            {
                targetTextBox.ScrollToEnd();
            }
        }

    }

    private Brush GetLogColor(string line)
    {
        if (string.IsNullOrEmpty(line)) return Brushes.AliceBlue;
        string lowerLine = line.ToLower();

        if (lowerLine.Contains("error:") || lowerLine.Contains("exception"))
            return Brushes.Red;
        if (lowerLine.Contains("warning:") || lowerLine.Contains("warn:") || lowerLine.Contains("internal:") || lowerLine.Contains("debug:"))
            return Brushes.Yellow;
        return Brushes.White;
    }

    private void StartActiveLogWatcher()
    {
        if (_logWatchers.TryGetValue(_logTagToType[_activeLogType], out var watcher))
        {
            watcher.EnableRaisingEvents = true;
        }
    }

    private async void LookForAppUpdate()
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        httpClient.DefaultRequestHeaders.Add("User-Agent", "SSM-Client/1.0");

        try
        {
            string latestVersion = null;

            try
            {
                var response = await httpClient.GetAsync("https://raw.githubusercontent.com/aghosto/Palworld-Server-Manager/refs/heads/master/VERSION");
                response.EnsureSuccessStatusCode();
                latestVersion = await response.Content.ReadAsStringAsync();
            }
            catch
            {
                var response = await httpClient.GetAsync("https://gitee.com/aGHOSToZero/Palworld-Server-Manager/raw/master/VERSION");
                response.EnsureSuccessStatusCode();
                latestVersion = await response.Content.ReadAsStringAsync();
            }

            latestVersion = latestVersion.Trim();

            string currentVersion = AppVersion.Text
            .Replace("软件版本：", "") 
            .Trim();

            if (latestVersion != currentVersion)
            {
                SsmSettings.AppSettings.HasNewVersion = true;
                SsmSettings.AppSettings.NewVersion = latestVersion;
                ShowLogWarning($"发现新版本：{latestVersion}，可点击左下角更新");
            }
            else
            {
                SsmSettings.AppSettings.HasNewVersion = false;
                ShowLogDefault($"当前软件已是最新版本：{latestVersion}");
            }
        }
        catch (HttpRequestException ex)
        {
            string errorMessage = "检查更新失败：网络异常";

            if (ex.InnerException != null)
            {
                string inner = ex.InnerException.Message.ToLower();
                if (inner.Contains("eof") || inner.Contains("closed")) 
                    errorMessage = "服务器连接关闭";
                else if (inner.Contains("timeout")) 
                    errorMessage = "连接超时";
                else if (inner.Contains("host") || inner.Contains("resolve")) 
                    errorMessage = "无法连接服务器";
                else if (ex.InnerException is System.Security.Authentication.AuthenticationException) 
                    errorMessage = "SSL安全认证失败";
            }
            else if (ex.StatusCode.HasValue)
            {
                errorMessage = $"服务器错误：{ex.StatusCode}";
            }

            ShowLogError(errorMessage);
            ShowLogError("无法检查更新，请检查网络后重试");
        }
        catch (Exception ex)
        {
            ShowLogError($"检查更新出错：{ex.Message}");
        }
    }

    private async void AutoUpdateLoop()
    {
        while (await AutoUpdateTimer.WaitForNextTickAsync())
        {
            bool foundUpdate = await CheckForUpdate();
            if (foundUpdate && SsmSettings.Servers.Count > 0)
            {
                ShowLogMsg("检测到服务器新版本，即将执行自动更新", Brushes.Orange);
                await AutoUpdate();
            }
        }
    }

    private async void AutoRestart()
    {
        List<Task> serverTasks = new List<Task>();
        List<Server> runningServers = new List<Server>();

        foreach (Server server in SsmSettings.Servers)
        {
            if (server.Runtime.State == ServerRuntime.ServerState.运行中)
            {
                server.Runtime.UserStopped = true;

                runningServers.Add(server);
            }
        }

        if (runningServers.Count > 0)
        {
            //SendDiscordMessage(SsmSettings.WebhookSettings.UpdateWait);
            await Task.Delay(TimeSpan.FromSeconds(0));
        }
        else
        {
            ShowLogWarning($"当前无正在运行的服务器，自动重启未生效。");
            return;
        }

        ShowLogWarning($"正在自动重启 {runningServers.Count} 个服务器" + ((runningServers.Count > 0) ? $"，在此之前即将关闭 {runningServers.Count} 个服务器" : ""));
        foreach (Server server in runningServers)
        {
            _ssmPathManager = new(Directory.GetCurrentDirectory(), server);
            await StopServer(server);
        }
        foreach (Server server in runningServers)
        {
            _ssmPathManager = new(Directory.GetCurrentDirectory(), server);
            await StartServer(server);
        }
        ShowLogDefault($"自动重启完成。");
    }

    private async Task RefreshPlayersAsync()
    {
        try
        {
            string settingsPath = Path.Combine(_currentServer.Path, "SaveData", "Settings", "ServerSettings.json");
            if (!File.Exists(settingsPath)) return;

            string json = File.ReadAllText(settingsPath);
            var combined = System.Text.Json.JsonSerializer.Deserialize<CombinedServerSettings>(json);
            if (combined?.HostSettings == null || !combined.HostSettings.RESTAPIEnabled || string.IsNullOrEmpty(combined.HostSettings.AdminPassword))
            {
                PlayerCountText.Text = "REST API未启用";
                return;
            }

            using var restClient = new RestApiClient();
            restClient.SetAuth("127.0.0.1", combined.HostSettings.RESTAPIPort, combined.HostSettings.AdminPassword);

            var playerList = await restClient.GetPlayerListAsync();
            if (playerList?.Players == null)
            {
                PlayerCountText.Text = "获取失败";
                return;
            }

            _players.Clear();
            foreach (var p in playerList.Players)
            {
                string userId = p.UserId ?? "";
                string platform = "";
                string displayId = userId;
                int underscoreIdx = userId.IndexOf('_');
                if (underscoreIdx > 0)
                {
                    platform = userId.Substring(0, underscoreIdx);
                    displayId = userId.Substring(underscoreIdx + 1);
                }

                _players.Add(new PlayerDisplayInfo
                {
                    CharacterName = p.Name,
                    UserId = userId,
                    SteamId = displayId,
                    Platform = platform,
                    Level = p.Level,
                    Ping = p.Ping,
                    Ip = p.Ip
                });
            }
            PlayerDataGrid.ItemsSource = _players;
            PlayerCountText.Text = playerList.Players.Count.ToString();
            LastUpdatedText.Text = $"最后刷新：{DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            PlayerCountText.Text = $"获取失败：{ex.Message}";
        }
    }

    private void SendDiscordMessage(string message)
    {
        if (SsmSettings.WebhookSettings.Enabled == false || message == "")
            return;

        if (SsmSettings.WebhookSettings.URL == "")
        {
            //ShowLogWarning("Discord webhook尝试发送消息，但URL未定义。");
            return;
        }

        if (DiscordSender.WebHook == null)
        {
            DiscordSender.WebHook = SsmSettings.WebhookSettings.URL;
        }

        DiscordSender.SendMessage(message);
    }

    /// <summary>
    /// Updates SteamCMD, used when the executable could not be found
    /// </summary>
    /// <returns><see cref="bool"/> true if succeeded</returns>
    private async Task<bool> UpdateSteamCMD()
    {
        string workingDir = Directory.GetCurrentDirectory();
        ShowLogWarning("未找到SteamCMD，正在下载...");
        byte[] fileBytes = await HttpClient.GetByteArrayAsync(@"https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip");
        await File.WriteAllBytesAsync(workingDir + @"\steamcmd.zip", fileBytes);
        if (File.Exists(workingDir + @"\SteamCMD\steamcmd.exe") == true)
        {
            File.Delete(workingDir + @"\SteamCMD\steamcmd.exe");
        }
        ShowLogWarning("解压中...");
        ZipFile.ExtractToDirectory(workingDir + @"\steamcmd.zip", workingDir + @"\SteamCMD");
        if (File.Exists(workingDir + @"\steamcmd.zip"))
        {
            File.Delete(workingDir + @"\steamcmd.zip");
        }

        ShowLogDefault("正在获取Palworld Dedicated Server应用信息。");
        await CheckForUpdate();

        return true;
    }

    private async Task<bool> UpdateGame(Server server)
    {
        if (server.Runtime.State == ServerRuntime.ServerState.更新中)
        {
            ShowLogWarning($"服务器「{server.ssmServerName}」正在更新中，正在终止 SteamCMD 进程...");
            KillCurrentServerSteamcmd();
            server.Runtime.State = ServerRuntime.ServerState.已停止;
            return false;
        }
        if (server.Runtime.State != ServerRuntime.ServerState.已停止)
        {
            ShowLogError($"服务器「{server.ssmServerName}」当前状态为「{server.Runtime.State}」，仅允许在「已停止」状态下更新");
            return false;
        }
        server.Runtime.State = ServerRuntime.ServerState.更新中;
        Process steamcmd = null;

        Dispatcher.Invoke(() =>
        {
            InstallationProgressBar.IsIndeterminate = true;
            InstallationProgressBar.Visibility = Visibility.Visible;
        });

        if (!Directory.Exists(server.Path))
        {
            ShowLogDefault($"服务器目录不存在，正在创建：{server.Path}");
            Directory.CreateDirectory(server.Path);
        }

        if (server.Runtime.Process != null && !server.Runtime.Process.HasExited)
        {
            ShowLogWarning($"服务器「{server.ssmServerName}」仍在运行中，请先停止服务器再更新");
            server.Runtime.State = ServerRuntime.ServerState.已停止;
            return false;
        }

        string steamCmdDir = Path.Combine(Directory.GetCurrentDirectory(), @"SteamCMD");
        string steamCmdPath = Path.Combine(steamCmdDir, "steamcmd.exe");

        if (!File.Exists(steamCmdPath))
        {
            ShowLogDefault("未检测到 SteamCMD，开始自动下载...");

            try
            {
                using var httpClient = new HttpClient();
                byte[] fileBytes = await httpClient.GetByteArrayAsync(@"https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip");
                string zipPath = Path.Combine(Directory.GetCurrentDirectory(), "steamcmd.zip");
                await File.WriteAllBytesAsync(zipPath, fileBytes);

                if (!Directory.Exists(steamCmdDir))
                    Directory.CreateDirectory(steamCmdDir);

                ZipFile.ExtractToDirectory(zipPath, steamCmdDir);
                File.Delete(zipPath);
                ShowLogDefault("SteamCMD 下载并安装完成");
            }
            catch (Exception ex)
            {
                ShowLogError($"SteamCMD 下载失败：{ex.Message}");
                server.Runtime.State = ServerRuntime.ServerState.已停止;
                return false;
            }
        }

        bool isNewInstall = !Directory.EnumerateFiles(server.Path, "*.exe").Any();
        string action = isNewInstall ? "下载" : "更新";

        ShowLogWarning($"正在{action}游戏服务器「{server.ssmServerName}」，请稍候...");

        if (SsmSettings.AppSettings == null)
        {
            ShowLogWarning("警告：应用设置未初始化，已使用默认值替代");
            SsmSettings.AppSettings = new AppSettings();
        }

        string[] installScript = {
            $"force_install_dir \"{server.Path}\"",
            "login anonymous",
            $"app_update 2394010 {(SsmSettings.AppSettings.VerifyUpdates ? "validate" : "")}",
            "quit"
        };

        string scriptPath = Path.Combine(server.Path, "steamcmd.txt");
        if (File.Exists(scriptPath))
            File.Delete(scriptPath);
        File.WriteAllLines(scriptPath, installScript);

        string parameters = $@"+runscript ""{scriptPath}""";

        bool hasError = false;
        string errorMsg = "";

        _updateCts = new CancellationTokenSource();
        const int maxRetries = 3;

        for (int retry = 0; retry < maxRetries; retry++)
        {
            if (_updateCts.IsCancellationRequested)
            {
                ShowLogWarning("已取消更新操作，不再重试");
                server.Runtime.State = ServerRuntime.ServerState.已停止;
                return false;
            }

            if (retry > 0)
            {
                ShowLogWarning($"正在第 {retry + 1} 次尝试更新「{server.ssmServerName}」（最多重试 {maxRetries} 次）");
                await Task.Delay(3000);
            }

            hasError = false;
            errorMsg = "";

            try
            {
                if (!SsmSettings.AppSettings.ShowSteamWindow)
                {
                    steamcmd = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = steamCmdPath,
                            Arguments = parameters,
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            WorkingDirectory = server.Path,
                            StandardOutputEncoding = Encoding.UTF8,
                            StandardErrorEncoding = Encoding.UTF8
                        }
                    };

                    steamcmd.Start();
                    server.Runtime.Process = steamcmd;
                    steamcmd.OutputDataReceived += (sender, e) =>
                    {
                        if (hasError)
                            return;

                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            if (e.Data.Contains("默认文件夹"))
                            {
                                errorMsg = "路径包含中文字符，SteamCMD 不支持中文路径，请将服务器目录移至纯英文路径";
                                ShowLogError($"更新失败：{errorMsg}");
                                hasError = true;
                                KillCurrentServerSteamcmd();
                                return;
                            }

                            if (e.Data.Contains("FAILED (No Connection)") || e.Data.Contains("No connection"))
                            {
                                errorMsg = "无法连接到 Steam 服务器，请检查网络连接是否正常";
                                ShowLogError($"更新失败：{errorMsg}");
                                hasError = true;
                                KillCurrentServerSteamcmd();
                                return;
                            }

                            if (e.Data.Contains("Disk Write Failure") || e.Data.Contains("disk write"))
                            {
                                errorMsg = "磁盘写入失败，请检查磁盘空间是否充足以及目录写入权限";
                                ShowLogError($"更新失败：{errorMsg}");
                                hasError = true;
                                KillCurrentServerSteamcmd();
                                return;
                            }
                        }
                    };

                    steamcmd.BeginOutputReadLine();
                    steamcmd.BeginErrorReadLine();
                    await steamcmd.WaitForExitAsync();

                    if (_updateCts.IsCancellationRequested)
                    {
                        server.Runtime.State = ServerRuntime.ServerState.已停止;
                        return false;
                    }

                    if (hasError || steamcmd.ExitCode != 0)
                    {
                        if (string.IsNullOrEmpty(errorMsg))
                            errorMsg = $"ExitCode: {steamcmd.ExitCode}";
                        ShowLogError($"{action}失败：{errorMsg}");
                        if (retry < maxRetries - 1)
                        {
                            ShowLogWarning("3 秒后自动重试...");
                            continue;
                        }
                        ShowLogError("已达最大重试次数，更新操作已终止");
                        server.Runtime.State = ServerRuntime.ServerState.已停止;
                        return false;
                    }
                    else
                    {
                        server.Runtime.State = ServerRuntime.ServerState.已停止;
                        return true;
                    }
                }
                else
                {
                    steamcmd = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = steamCmdPath,
                            Arguments = parameters,
                            CreateNoWindow = false
                        }
                    };
                    steamcmd.Start();
                    server.Runtime.Process = steamcmd;
                    await steamcmd.WaitForExitAsync();

                    if (_updateCts.IsCancellationRequested)
                    {
                        server.Runtime.State = ServerRuntime.ServerState.已停止;
                        return false;
                    }

                    if (steamcmd.ExitCode != 0)
                    {
                        if (retry < maxRetries - 1)
                        {
                            ShowLogWarning($"SteamCMD 异常退出（ExitCode: {steamcmd.ExitCode}），3 秒后自动重试...");
                            continue;
                        }
                        ShowLogError("已达最大重试次数，更新操作已终止");
                        server.Runtime.State = ServerRuntime.ServerState.已停止;
                        return false;
                    }
                    else
                    {
                        server.Runtime.State = ServerRuntime.ServerState.已停止;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                if (retry < maxRetries - 1)
                {
                    ShowLogWarning($"更新过程发生异常：{errorMsg}，3 秒后自动重试...");
                    continue;
                }
                ShowLogError("已达最大重试次数，更新操作已终止");
                server.Runtime.State = ServerRuntime.ServerState.已停止;
                return false;
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    InstallationProgressBar.IsIndeterminate = false;
                    InstallationProgressBar.Visibility = Visibility.Collapsed;
                });
                steamcmd?.Dispose();
                server.Runtime.Process = null;
                if (File.Exists(scriptPath))
                {
                    try
                    {
                        File.Delete(scriptPath);
                    }
                    catch { }
                }
            }
        }
        server.Runtime.State = ServerRuntime.ServerState.已停止;
        return false;
    }
    private void KillCurrentServerSteamcmd()
    {
        if (_currentServer == null) return;

        try
        {
            var process = _currentServer.Runtime.Process;
            if (process != null && !process.HasExited)
            {
                process.Kill();
                process.WaitForExit(1000);
                process.Dispose();
                _currentServer.Runtime.Process = null;
                ShowLogError($"已终止当前服务器的更新");
            }
        }
        catch (Exception ex)
        {
            ShowLogError($"终止当前服务器 SteamCMD 失败：{ex.Message}");
        }
    }

    private async Task<bool> StartServer(Server server)
    {
        if (server.Runtime.Process != null)
        {
            ShowLogError($"错误：{server.ssmServerName} 已在运行中");
            return false;
        }

        try
        {

            CombinedServerSettings jsonObject = UnifiedSettingsEditor.LoadServerSettings(_ssmPathManager.ServerSettings);
            server = SsmSettings.Servers.FirstOrDefault(s => s.ssmServerName == server.ssmServerName) ?? server;

            ShowLogWarning($"启动服务器：{server.ssmServerName}{(server.Runtime.RestartAttempts > 0 ? $" 尝试 {server.Runtime.RestartAttempts}/3" : "")}");

            string serverExePath = Path.Combine(server.Path, "StartServer.bat");
            string palExe = _ssmPathManager.ServerExePath;

            if (!File.Exists(serverExePath))
            {
                if (!File.Exists(palExe))
                {
                    ShowLogError($"错误：未找到 {serverExePath} 且服务器程序不存在");
                    return false;
                }

                ShowLogWarning("未找到 StartServer.bat，正在自动创建...");
                TryCreateStartServerBatFromSettings(server);
            }
            else
            {
                TryCreateStartServerBatFromSettings(server);
            }

            if (SsmSettings.WebhookSettings.Enabled && !string.IsNullOrEmpty(server.WebhookMessages.StartServer) && server.WebhookMessages.Enabled)
            {
                SendDiscordMessage(server.WebhookMessages.StartServer);
            }

            await InitializePalWorldSettingsIni(server);
            UnifiedSettingsEditor.TrySaveToPalWorldSettingsIni(server);

            var paramBuilder = new System.Text.StringBuilder();
            paramBuilder.Append($"-Port={jsonObject.HostSettings.Port}");

            if (jsonObject.HostSettings.PublicLobby)
                paramBuilder.Append(" -publiclobby");

            if (jsonObject.Performances.bUseMultiThreadPerformance)
            {
                paramBuilder.Append(" -useperfthreads -NoAsyncLoadingThread -UseMultithreadForDS");
                paramBuilder.Append($" -NumberOfWorkerThreadsServer={jsonObject.Performances.NumberOfWorkerThreadsServer}");
            }

            Process serverProcess = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    WindowStyle = server.RunWithoutWindow ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
                    FileName = palExe,
                    UseShellExecute = true,
                    Arguments = paramBuilder.ToString()
                },
                EnableRaisingEvents = true
            };

            serverProcess.Exited += (sender, e) => ServerProcessExited(sender, e, server);
            serverProcess.Start();

            server.Runtime.State = ServerRuntime.ServerState.运行中;
            server.Runtime.UserStopped = false;
            server.Runtime.Process = serverProcess;

            await Task.Delay(3000);
            ShowWindow(serverProcess.MainWindowHandle, SW_MINIMIZE);
            ShowLogDefault($"已成功启动服务器：{server.ssmServerName} | {jsonObject.HostSettings.ServerName}");

            MainSettings.Save(SsmSettings);
            return true;
        }
        catch (Exception ex)
        {
            ShowLogError($"启动服务器失败：{ex.Message}");
            return false;
        }
    }

    private async Task<bool> StopServer(Server server)
    {
        if (server.Runtime.Process == null || server.Runtime.Process.HasExited)
        {
            ShowLogWarning($"服务器 {server.ssmServerName} 未运行或已退出");
            server.Runtime.Process = null;
            return true;
        }

        // 发送关闭通知
        if (SsmSettings.WebhookSettings.Enabled && !string.IsNullOrEmpty(server.WebhookMessages.StopServer) && server.WebhookMessages.Enabled)
        {
            SendDiscordMessage(server.WebhookMessages.StopServer);
        }

        server.Runtime.UserStopped = true;

        string settingsPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");
        if (File.Exists(settingsPath))
        {
            try
            {
                string json = File.ReadAllText(settingsPath);
                var combined = System.Text.Json.JsonSerializer.Deserialize<CombinedServerSettings>(json);
                if (combined?.HostSettings != null && combined.HostSettings.RESTAPIEnabled && !string.IsNullOrEmpty(combined.HostSettings.AdminPassword))
                {
                    try
                    {
                        using var restClient = new REST.RestApiClient();
                        restClient.SetAuth("127.0.0.1", combined.HostSettings.RESTAPIPort, combined.HostSettings.AdminPassword);
                        bool saved = await restClient.SaveWorldAsync();
                        bool stopped = await restClient.ShutdownAsync(3, "正在关闭服务器");
                        if (stopped)
                        {
                            await Task.Delay(8000);
                            if (server.Runtime.Process != null && !server.Runtime.Process.HasExited)
                                server.Runtime.Process.Kill();
                            server.Runtime.State = ServerRuntime.ServerState.已停止;
                            server.Runtime.Process = null;
                            return true;
                        }
                    }
                    catch { }
                }

                bool close = server.Runtime.Process.CloseMainWindow();
                await server.Runtime.Process.WaitForExitAsync();
                server.Runtime.State = ServerRuntime.ServerState.已停止;
                server.Runtime.Process = null;
                //ShowLogDefault($"成功关闭服务器 {server.ssmServerName} | {combined.HostSettings.ServerName} 成功关闭");
                return true;
            }
            catch (Exception ex)
            {
                ShowLogError($"服务器 {server.ssmServerName} 关闭失败：{ex.Message}");
                return false;
            }
        }
        else
        {
            ShowLogError("未找到服务器设置文件，请检查是否正确创建服务器");
            return false;
        }
    }

    private async Task<bool> RestartServer(Server server)
    {
        ShowLogWarning($"正在重启服务器：" + server.ssmServerName);
        try
        {
            bool success = await StopServer(server);
            if (success)
            {
                ShowLogDefault($"已成功停止服务器：{server.ssmServerName}");

                //if (!WriteServerCrashLog(server))
                //    ShowLogError($"备份 {server.ssmServerName} 服务器日志失败");
                //else
                //    ShowLogDefault($"已备份 {server.ssmServerName} 服务器日志");

                if (File.Exists(server.Path + @"\StartServer.bat") || File.Exists(Path.Combine(server.Path, "PalServer.exe")))
                {
                    if (!File.Exists(server.Path + @"\StartServer.bat"))
                        TryCreateStartServerBatFromSettings(server);
                    success = await StartServer(server);
                }
                else
                {
                    success = false;
                    ShowLogError($"未找到服务器启动脚本，请检查服务器安装是否有误");
                    return success;
                }
                return true;
            }
            else
            {
                ShowLogError($"无法停止服务器：{server.ssmServerName}");
                WriteServerCrashLog(server);
                return false;
            }

        }
        catch (Exception ex)
        {
            ShowLogError($"重启服务器发生错误：{ex.Message}");
            return false;
        }
    }

    private async Task<bool> WaitForProcessExitAsync(Process process, int timeoutSeconds)
    {
        if (process == null || process.HasExited)
            return true;

        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
            return process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private const int SW_MINIMIZE = 2; // 最小化

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private int GetPalServerPidByPath(string serverExeFolder)
    {
        foreach (var p in Process.GetProcessesByName("PalServer-Win64-Shipping-Cmd"))
        {
            try
            {
                string? processPath = p.MainModule?.FileName;
                if (!string.IsNullOrEmpty(processPath) && processPath.StartsWith(serverExeFolder, StringComparison.OrdinalIgnoreCase))
                    return p.Id;
            }
            catch { }
        }
        return -1;
    }


    private async void MonitorSingleProcess(int pid, Action<bool> onStateChanged)
    {
        await Task.Run(async () =>
        {
            while (true)
            {
                bool isRunning = IsProcessRunning(pid);
                onStateChanged?.Invoke(isRunning);

                if (!isRunning) 
                    break;
                await Task.Delay(1000);
            }
        });
    }

    private bool IsProcessRunning(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            return !Process.GetProcessById(pid).HasExited;
        }
        catch
        {
            return false;
        }
    }

    private async Task SendRconRestartMessage(Server server)
    {
        CombinedServerSettings serverSettings = UnifiedSettingsEditor.LoadServerSettings(server.Path);
        RCONClient = new()
        {
            UseUtf8 = true
        };

        RCONClient.OnLog += async message =>
        {
            if (message == "Authentication success.")
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                RCONClient.SendCommand("announcerestart 5", result =>
                {
                    //Do nothing
                });
            }

        };

        RCONClient.OnConnectionStateChange += state =>
        {
            if (state == RemoteConClient.ConnectionStateChange.Connected)
            {
                RCONClient.Authenticate(serverSettings.HostSettings.AdminPassword);
            }
        };

        RCONClient.Connect(serverSettings.HostSettings.PublicIP, serverSettings.HostSettings.RESTAPIPort);
        await Task.Delay(TimeSpan.FromSeconds(3));
        RCONClient.Disconnect();
    }

    private async Task AutoUpdate()
    {
        SendDiscordMessage(SsmSettings.WebhookSettings.UpdateFound);

        if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "SteamCMD", "steamcmd.exe")))
        {
            await UpdateSteamCMD();
        }

        var runningServers = SsmSettings.Servers
            .Where(s => s.Runtime.State == ServerRuntime.ServerState.运行中)
            .ToList();

        if (runningServers.Count == 0)
        { 
            ShowLogDefault("无运行中的服务器，直接执行更新");
            foreach (var server in SsmSettings.Servers)
                await UpdateGame(server);
            ShowLogDefault("自动更新完成");
            return;
        }

        foreach (var server in runningServers)
            await RCONClient.SendRestartAnnounceToSingleServer(server, "【服务器更新公告】服务器将在 5分钟 后立即更新重启，更新耗时预计为 30 分钟，请尽快下线并更新玩家客户端后重新加入游戏！");

        await Task.Delay(TimeSpan.FromMinutes(4));
        foreach (var server in runningServers)
            await RCONClient.SendRestartAnnounceToSingleServer(server, "【服务器更新公告】服务器将在 1分钟 后立即更新重启，更新耗时预计为 30 分钟，请立刻下线并更新玩家客户端后重新加入游戏！");

        await Task.Delay(TimeSpan.FromMinutes(1));
        var stopTasks = runningServers.Select(StopServer).ToList();
        await Task.WhenAll(stopTasks);

        ShowLogDefault("所有服务器已关闭，开始更新...");
        foreach (var server in SsmSettings.Servers)
            await UpdateGame(server);

        ShowLogDefault("更新完成，开始启动服务器...");
        var startTasks = runningServers.Select(StartServer).ToList();
        await Task.WhenAll(startTasks);

        //SendDiscordMessage(SsmSettings.WebhookSettings.UpdateFound);
        ShowLogDefault("所有服务器自动更新并重启完成");
    }

    private async Task<bool> TryGracefulShutdownAsync(Process process, int timeoutSeconds)
    {
        if (process == null || process.HasExited)
            return true;

        FocusWindowAndSendCtrlC(process);

        return await WaitForProcessExitAsync(process, timeoutSeconds);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_RESTORE = 9;

    public void FocusWindowAndSendCtrlC(Process targetProcess)
    {
        if (targetProcess == null || targetProcess.HasExited)
            return;

        IntPtr targetHwnd = targetProcess.MainWindowHandle;
        if (targetHwnd == IntPtr.Zero)
            return;

        IntPtr mainWindowHandle = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle;
        try
        {
            ShowWindow(targetHwnd, SW_RESTORE);
            SetForegroundWindow(targetHwnd);
            Thread.Sleep(100);
            System.Windows.Forms.SendKeys.SendWait("^c");
        }
        finally
        {
            ShowWindow(mainWindowHandle, SW_RESTORE);
            SetForegroundWindow(mainWindowHandle);
        }
    }

    private async Task<bool> RemoveServer(Server server)
    {
        int serverIndex = SsmSettings.Servers.IndexOf(server);
        string workingDir = Directory.GetCurrentDirectory();
        string serverName = server.ssmServerName.Replace(" ", "_");

        bool success;
        ContentDialog yesNoDialog = new()
        {
            Content = $"确认要移除服务器 {server.ssmServerName}？\n此动作将永久移除该服务器及其文件。",
            PrimaryButtonText = "是",
            SecondaryButtonText = "否"
        };
        if (await yesNoDialog.ShowAsync() is ContentDialogResult.Secondary)
            return false;

        if (serverIndex != -1)
        {
            ContentDialog bakDialog = new()
            {
                Content = $@"是否为该服务器连接设置创建备份？{Environment.NewLine}备份将保存于：{workingDir}\Backups\{serverName}_Bak.zip",
                PrimaryButtonText = "是",
                SecondaryButtonText = "否"
            };
            if (await bakDialog.ShowAsync() is ContentDialogResult.Primary)
            {
                if (!Directory.Exists(workingDir + @"\Backups"))
                    Directory.CreateDirectory(workingDir + @"\Backups");

                if (Directory.Exists(server.Path + @"\SaveData\"))
                {
                    if (File.Exists(workingDir + @"\Backups\" + serverName + "_Bak.zip"))
                        File.Delete(workingDir + @"\Backups\" + serverName + "_Bak.zip");

                    ZipFile.CreateFromDirectory(server.Path + @"\SaveData\", workingDir + @"\Backups\" + serverName + "_Bak.zip");
                }
            }
            SsmSettings.Servers.RemoveAt(serverIndex);
            if (Directory.Exists(server.Path))
                Directory.Delete(server.Path, true);
            success = true;
            return success;
        }
        else
        {
            return false;
        }
    }

    private async Task<bool> CheckForUpdate()
    {
        bool foundUpdate = false;
        try
        {
            string json = await HttpClient.GetStringAsync("https://api.steamcmd.net/v1/info/2394010");
            JsonNode jsonNode = JsonNode.Parse(json);

            var version = jsonNode?["data"]?["2394010"]?["depots"]?["branches"]?["public"]?["timeupdated"]?.ToString();

            if (string.IsNullOrEmpty(version))
            {
                ShowLogWarning("无法获取服务器版本信息，API 返回异常");
                return false;
            }

            if (version == SsmSettings.AppSettings.LastUpdateTimeUNIX)
            {
                SsmSettings.AppSettings.LastUpdateTimeUNIX = version;
                foundUpdate = false;
                if (SsmSettings.AppSettings.LastUpdateTimeUNIX != "")
                    SsmSettings.AppSettings.LastUpdateTime = "服务器最近更新时间：" + DateTimeOffset.FromUnixTimeSeconds(long.Parse(SsmSettings.AppSettings.LastUpdateTimeUNIX)).DateTime.ToString();

                MainSettings.Save(SsmSettings);
                return foundUpdate;
            }

            if (version != SsmSettings.AppSettings.LastUpdateTimeUNIX)
            {
                SsmSettings.AppSettings.LastUpdateTimeUNIX = version;
                foundUpdate = true;
            }

            if (SsmSettings.AppSettings.LastUpdateTimeUNIX == "")
            {
                SsmSettings.AppSettings.LastUpdateTimeUNIX = version;
                foundUpdate = true;
            }

            if (SsmSettings.AppSettings.LastUpdateTimeUNIX != "")
                SsmSettings.AppSettings.LastUpdateTime = "服务器上一次更新的时间：" + DateTimeOffset.FromUnixTimeSeconds(long.Parse(SsmSettings.AppSettings.LastUpdateTimeUNIX)).DateTime.ToString();

            MainSettings.Save(SsmSettings);
        }
        catch (HttpRequestException)
        {
            ShowLogWarning("检查更新失败：无法连接到 Steam API");
        }
        catch (Exception ex)
        {
            ShowLogWarning($"检查更新异常：{ex.Message}");
        }
        return foundUpdate;
    }

    // 读取服务器日志并处理特定事件
    private async void ReadLog(Server server)
    {
        //if (server == null)
        //{
        //    ShowLogError($"传入的服务器为空！");
        //    return;
        //}
        //string logPath = Path.Combine(server.Path, "Pal", "Saved", "Logs", "Pal.log");
        
        //try
        //{
            //if (!server.LogFileExists)
            //{
            //    if (!File.Exists(logPath))
            //    {
            //        ShowLogWarning($"【{server.ssmServerName}】日志文件不存在，请确保服务器已成功启动过一次");
            //        await Task.Delay(5000);
            //        if (!File.Exists(logPath))
            //        {
            //            ShowLogError($"【{server.ssmServerName}】日志文件仍不存在，请手动启动服务器一次");
            //            return;
            //        }
            //    }
            //    server.LogFileExists = true;
            //    ShowLogMsg($"【{server.ssmServerName}】已检测到日志文件：{logPath}", Brushes.Green);
            //}

        //    using FileStream fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        //    using StreamReader sr = new StreamReader(fs);

        //    while (server.FirstStart)
        //    {
        //        string line = await sr.ReadLineAsync();
        //        if (line != null)
        //        {
        //            if (line.Contains("Game Engine Initialized"))
        //            {
        //                ShowLogWarning("首次启动服务器，正在关闭以便进行配置");
        //                server.FirstStart = false;
        //                await StopServer(server);

        //                await InitializePalWorldSettingsIni(server);
        //            }
        //        }
        //        else
        //        {
        //            await Task.Delay(100);
        //        }
        //    }

        //    MainSettings.Save(SsmSettings);
        //    fs.Seek(0, SeekOrigin.End);
        //    long initialPosition = fs.Position;
        //}
        //catch (FileNotFoundException ex)
        //{
        //    server.LogFileExists = false;
        //    ShowLogError($"【{server.ssmServerName}】日志文件已被删除，请重启服务器: {ex.Message}");
        //}
        //catch (Exception ex)
        //{
        //    ShowLogError($"【{server.ssmServerName}】日志处理错误：{ex.Message}");
        //}

    }

    private async Task InitializePalWorldSettingsIni(Server server)
    {
        string iniPath = Path.Combine(server.Path, "Pal", "Saved", "Config", "WindowsServer", "PalWorldSettings.ini");
        string defaultIniPath = Path.Combine(server.Path, "DefaultPalWorldSettings.ini");

        if (!File.Exists(iniPath))
        {
            ShowLogWarning($"未找到 {iniPath}，请手动启动一次服务器生成默认配置。");
            return;
        }

        var fi = new FileInfo(iniPath);
        if (fi.Length > 0)
        {
            string existing = File.ReadAllText(iniPath).Trim();
            if (!string.IsNullOrEmpty(existing) && existing != "OptionSettings=()" && !existing.Contains("OptionSettings=()") && !existing.Contains("OptionSettings=\"\"") && existing != "")
                return;
        }

        ShowLogWarning("检测到 PalWorldSettings.ini 为空，正在从 DefaultPalWorldSettings.ini 复制默认配置...");

        if (!File.Exists(defaultIniPath))
        {
            ShowLogError($"未找到默认配置文件：{defaultIniPath}");
            return;
        }

        try
        {
            bool inSection = false;
            var lines = new List<string>();
            foreach (string rawLine in File.ReadAllLines(defaultIniPath))
            {
                string trimmed = rawLine.Trim();
                if (trimmed.StartsWith(";") || trimmed.StartsWith("#"))
                    continue;
                if (trimmed.StartsWith("[/Script"))
                {
                    inSection = true;
                    lines.Add(rawLine);
                    continue;
                }
                if (inSection)
                    lines.Add(rawLine);
            }

            File.WriteAllLines(iniPath, lines);
            ShowLogWarning("默认配置已写入 PalWorldSettings.ini，正在应用已保存的服务器参数...");

            string settingsPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");
            if (File.Exists(settingsPath))
            {
                string json = File.ReadAllText(settingsPath);
                var combined = System.Text.Json.JsonSerializer.Deserialize<CombinedServerSettings>(json);
                if (combined != null)
                {
                    var lines2 = File.ReadAllLines(iniPath);
                    for (int i = 0; i < lines2.Length; i++)
                    {
                        string line = lines2[i].Trim();
                        if (!line.StartsWith("OptionSettings="))
                            continue;

                        int startIdx = line.IndexOf('(');
                        int endIdx = line.LastIndexOf(')');
                        if (startIdx == -1 || endIdx == -1 || endIdx <= startIdx)
                            continue;

                        string prefix = line[..(startIdx + 1)];
                        string suffix = line[endIdx..];
                        string content = line.Substring(startIdx + 1, endIdx - startIdx - 1);

                        var pairs = ParseIniPairsForFirstStart(content);

                        foreach (var subObj in new object[] { combined.HostSettings, combined.Performances, combined.Features, combined.GameBalances })
                        {
                            foreach (var prop in subObj.GetType().GetProperties())
                            {
                                if (pairs.ContainsKey(prop.Name))
                                {
                                    object? val = prop.GetValue(subObj);
                                    pairs[prop.Name] = SerializeIniValueForFirstStart(val, prop.Name);
                                }
                            }
                        }

                        lines2[i] = prefix + string.Join(",", pairs.Select(p => $"{p.Key}={p.Value}")) + suffix;
                        break;
                    }
                    File.WriteAllLines(iniPath, lines2);
                }
            }

            ShowLogDefault("PalWorldSettings.ini 已初始化并应用了保存的配置。现在可以对服务器进行更改了！");
        }
        catch (Exception ex)
        {
            ShowLogError($"初始化 PalWorldSettings.ini 失败：{ex.Message}");
        }
    }

    private static Dictionary<string, string> ParseIniPairsForFirstStart(string content)
    {
        var result = new Dictionary<string, string>();
        int i = 0;
        while (i < content.Length)
        {
            while (i < content.Length && char.IsWhiteSpace(content[i])) i++;
            if (i >= content.Length) break;
            int start = i;
            while (i < content.Length && content[i] != '=') i++;
            if (i >= content.Length) break;
            string key = content.Substring(start, i - start).Trim();
            i++;
            string val;
            if (i < content.Length && content[i] == '"')
            {
                i++; start = i;
                while (i < content.Length && content[i] != '"') i++;
                val = content.Substring(start, i - start);
                i++;
            }
            else if (i < content.Length && content[i] == '(')
            {
                int depth = 1; start = i; i++;
                while (i < content.Length && depth > 0)
                {
                    if (content[i] == '(') depth++;
                    else if (content[i] == ')') depth--;
                    i++;
                }
                val = content.Substring(start, i - start);
            }
            else
            {
                start = i;
                while (i < content.Length && content[i] != ',') i++;
                val = content.Substring(start, i - start).Trim();
            }
            if (!string.IsNullOrEmpty(key)) result[key] = val;
            i++;
        }
        return result;
    }

    private static string SerializeIniValueForFirstStart(object? value, string propertyName = "")
    {
        if (value == null) return "";
        if (value is double d) return d.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
        if (value is float f) return f.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
        if (value is bool b) return b ? "True" : "False";
        if (value is string str)
        {
            if (str.Length == 0) return "";
            if (str.StartsWith('(') && str.EndsWith(')')) return str;
            if (propertyName is "ServerName" or "RandomizerSeed" or "ServerDescription" or "AdminPassword" or "ServerPassword" or "PublicIP" or "Region" or "BanListURL")
                return $"\"{str}\"";
            return str;
        }
        return value.ToString() ?? "";
    }

    #region Events
    private async void ServerProcessExited(object sender, EventArgs e, Server server)
    {
        if (server == null)
        {
            ShowLogError("错误：服务器实例为空，无法处理进程退出事件");
            return;
        }

        if (server.Runtime == null)
        {
            ShowLogError($"错误：[{server.ssmServerName}] 运行时对象未初始化");
            return;
        }

        if (server.Runtime.State == ServerRuntime.ServerState.已停止)
            return;

        int exitCode = -1;
        Process exitedProcess = sender as Process;
        if (exitedProcess != null && !exitedProcess.HasExited)
        {
            try
            {
                exitCode = exitedProcess.ExitCode; 
            }
            catch (InvalidOperationException)
            {
                exitCode = -1;
            }
        }

        server.Runtime.State = ServerRuntime.ServerState.已停止;
        server.Runtime.Process = null;

        try
        {
            switch (exitCode)
            {
                case 1:
                    ShowLogError($"{server.ssmServerName} 崩溃了。");
                    break;
                case -2147483645:
                    ShowLogError($"{server.ssmServerName} 已中断（代码：-2147483645），可能是端口被占用。");
                    break;
                default:
                    //ShowLogWarning($"{server.ssmServerName} 已停止（退出码：{exitCode}）");
                    break;
            }

            if (server.Runtime.RestartAttempts >= 3)
            {
                ShowLogError($"服务器 '{server.ssmServerName}' 已尝试重启3次失败，禁用自动重启。");

                if (SsmSettings.WebhookSettings.Enabled &&
                    !string.IsNullOrEmpty(server.WebhookMessages.AttemptStart3) &&
                    server.WebhookMessages.Enabled)
                {
                    SendDiscordMessage(server.WebhookMessages.AttemptStart3);
                }

                if (SsmSettings.AppSettings.SaveLogWhenCrash)
                {
                    if (WriteServerCrashLog(server))
                    {
                        ShowLogWarning($"已创建崩溃日志：{Path.Combine(server.Path, "CrashLog")}");
                    }
                }

                ShowLogDefault("尝试最后一次重启服务器...");
                await Task.Delay(5000);

                bool restartSuccess = await StartServer(server);
                if (restartSuccess)
                {
                    ShowLogDefault($"{server.ssmServerName} 重启成功，重新启用自动重启。");
                    server.AutoRestart = true;
                    server.Runtime.RestartAttempts = 0;
                }
                else
                {
                    ShowLogError($"{server.ssmServerName} 最后一次重启失败，请手动检查。");
                }
                return;
            }

            if (server.AutoRestart && !server.Runtime.UserStopped)
            {
                server.Runtime.RestartAttempts++;
                ShowLogDefault($"{server.ssmServerName} 将自动重启（尝试 {server.Runtime.RestartAttempts}/3）");

                if (SsmSettings.WebhookSettings.Enabled &&
                    !string.IsNullOrEmpty(server.WebhookMessages.ServerCrash) &&
                    server.WebhookMessages.Enabled)
                {
                    SendDiscordMessage(server.WebhookMessages.ServerCrash);
                }

                if (SsmSettings.AppSettings.SaveLogWhenCrash)
                {
                    if (WriteServerCrashLog(server))
                    {
                        ShowLogWarning($"已创建崩溃日志：{Path.Combine(server.Path, "CrashLog")}");
                    }
                }

                await Task.Delay(3000);
                await StartServer(server);
            }
        }
        catch (Exception ex)
        {
            ShowLogError($"[{server.ssmServerName}] 处理进程退出时出错：{ex.Message}");
        }
    }

    private void AppSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {

        switch (e.PropertyName)
        {
            case "AutoUpdate":
                if (SsmSettings.AppSettings.AutoUpdate == true)
                {
#if DEBUG
                    //AutoUpdateTimer = new PeriodicTimer(TimeSpan.FromSeconds(10));
#else
//                        AutoUpdateTimer = new PeriodicTimer(TimeSpan.FromMinutes(SsmSettings.AppSettings.AutoUpdateInterval));
#endif
                    //AutoUpdateLoop();
                    LookForAppUpdate();
                }
                else
                {
                    if (AutoUpdateTimer != null)
                    {
                        AutoUpdateTimer.Dispose();
                    }
                }
                break;
            case "AutoUpdateInterval":
                if (SsmSettings.AppSettings.AutoUpdate == true && AutoUpdateTimer != null)
                {
                    AutoUpdateTimer.Dispose();
#if DEBUG
                    AutoUpdateTimer = new PeriodicTimer(TimeSpan.FromSeconds(10));
#else
//                        AutoUpdateTimer = new PeriodicTimer(TimeSpan.FromMinutes(SsmSettings.AppSettings.AutoUpdateInterval));
#endif
                    AutoUpdateLoop();
                }
                break;
            case "DarkMode":
                if (SsmSettings.AppSettings.DarkMode == true)
                    ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
                else
                    ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
                break;
        }
    }

    private void Servers_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        int serversLength = ServerTabControl.Items.Count;
        if (serversLength > 0)
        {
            ServerTabControl.SelectedIndex = serversLength - 1;
        }
    }


    #endregion


    #region Buttons
    private async void StartServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not Server server)
        {
            ShowLogError("启动服务器失败：无效的按钮或服务器实例");
            return;
        }

        try
        {
            button.IsEnabled = false;
            bool started = await StartServer(server);
            await Task.Delay(3000);

            if (started == true)
            {
                ReadLog(server);
            }
        }
        catch (Exception ex)
        {
            ShowLogError($"{server.ssmServerName} 启动异常：{ex.Message}");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void UpdateServerButton_Click(object sender, RoutedEventArgs e)
    {
        Button button = (Button)sender;
        Server server = button.DataContext as Server;

        if (server == null)
        {
            ShowLogError($"错误：未找到服务器信息");
            return;
        }

        try
        {
            if (server.Runtime.State == ServerRuntime.ServerState.更新中)
            {
                ShowLogWarning($"正在取消服务器 {server.ssmServerName} 的更新...");
                _updateCts?.Cancel();
                KillCurrentServerSteamcmd();
                return;
            }

            button.IsEnabled = false;
            UpdateButtonText.Text = "取消更新";
            button.IsEnabled = true;

            bool success = await UpdateGame(server);

            if (success)
                ShowLogDefault($"服务器 {server.ssmServerName} 更新成功！");
        }
        catch (Exception ex)
        {
            ShowLogError($"更新过程出现意外错误：{ex.Message}");
        }
        finally
        {
            UpdateButtonText.Text = "更新服务器";
            button.IsEnabled = true;
        }
    }

    private async void StopServerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Button button = (Button)sender;
            Server server = button.DataContext as Server;

            if (server == null)
            {
                ShowLogError($"未找到服务器信息，请确认服务器有正常运行过至少一次");
                return;
            }

            ShowLogWarning($"正在停止服务器：{server.ssmServerName}");
            bool wasRunning = server.Runtime?.State == ServerRuntime.ServerState.运行中;
            bool success = await StopServer(server);

            if (success)
            {
                ShowLogDefault($"已成功停止服务器：{server.ssmServerName}");
            }
            else if (wasRunning)
            {
                WriteServerCrashLog(server);
                ShowLogError($"停止服务器失败：{server.ssmServerName}");
            }
        }
        catch (Exception ex)
        {
            ShowLogError($"停止服务器时出错：{ex.Message}");
            if (sender is Button button && button.DataContext is Server server)
                if (server.Runtime?.State == ServerRuntime.ServerState.运行中)
                    WriteServerCrashLog(server);
        }
    }

    private async void RestartServerButton_Click(object sender, RoutedEventArgs e)
    {
        Button button = (Button)sender;
        Server server = button.DataContext as Server;

        if (server == null)
        {
            ShowLogError($"未找到服务器信息，请确认服务器有正常运行过至少一次");
            return;
        }
        bool restartSuccess = await RestartServer(server);
        if (restartSuccess == true)
            ReadLog(server);
    }

    private void ThemeSelect_Click(object sender, RoutedEventArgs e)
    {
        if (ThemeManager.Current.ApplicationTheme == ApplicationTheme.Light)
        {
            SsmSettings.AppSettings.DarkMode = true;

        }
        else
        {
            SsmSettings.AppSettings.DarkMode = false;
        }
            MainSettings.Save(SsmSettings);
    }

    private async void RenameServerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Server server = ((MenuItem)sender).DataContext as Server;
        if (server == null)
            return;

        var dialog = new ModifyPsmNameDialog(server, SsmSettings);
        await dialog.ShowAsync();
    }

    private async void RemoveServerButton_Click(object sender, RoutedEventArgs e)
    {
        Server server = ((Button)sender).DataContext as Server;

        if (server == null)
        {
            ShowLogError($"错误：找不到要删除的选定服务器");
            return;
        }
        if (server.Runtime.State == ServerRuntime.ServerState.运行中 || server.Runtime.State == ServerRuntime.ServerState.更新中)
        {
            ShowLogError($"错误：服务器正在运行或者更新中，请先停止服务器！");
            return;
        }
        try
        {
            bool success = await RemoveServer(server);
            if (!success)
                ShowLogError($"删除服务器时出错，或操作已中止。");
            else
                MainSettings.Save(SsmSettings);
        }
        catch { }
    }

    private void ImportServerButton_Click(object sender, RoutedEventArgs e)
    {
        Server server = ((Button)sender).DataContext as Server;
        if (server == null) 
            return;

        var window = Application.Current.Windows.OfType<ImportServerWindow>().FirstOrDefault();

        if (window != null)
        {
            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
        }
        else
        {
            window = new (server);
            window.Show();
        }
    }

    private void ChangeSaveButton_Click(object sender, RoutedEventArgs e)
    {
        Server server = ((Button)sender).DataContext as Server;
        if (server == null) 
            return;

        var window = Application.Current.Windows.OfType<ChangeSaveWindow>().FirstOrDefault();

        if (window != null)
        {
            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
        }
        else
        {
            window = new (server);
            window.Show();
        }
    }

    private void SaveEditorButton_Click(object sender, RoutedEventArgs e)
    {
        var window = Application.Current.Windows.OfType<SaveEditorWindow>().FirstOrDefault();

        if (window != null)
        {
            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
        }
        else
        {
            window = new SaveEditorWindow();
            window.Owner = this;
            window.Show();
        }
    }

    private async void ServerAccountExchangeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Server server = ((Button)sender).DataContext as Server;
            if (server == null) return;

            var confirmDialog = new ContentDialog
            {
                Title = "玩家数据转移",
                Content = "确定要执行玩家数据转移吗？\n\n冲突时优先使用源存档数据",
                PrimaryButtonText = "确定",
                SecondaryButtonText = "取消"
            };
            if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            string mainPath = Path.Combine(_ssmPathManager.DedicatedPath, "Level01_Main", "world.db");
            string dlcPath = Path.Combine(_ssmPathManager.DedicatedPath, "DLC_Level01_Main", "world.db");
            string targetDb = Path.Combine(_ssmPathManager.SavedDir, "Accounts", "account.db");
            string copyRolesExe = Path.Combine(_ssmPathManager.PluginDir, "DBAgent", "ThirdParty", "Binaries", "CopyRoles.exe");

            if (!File.Exists(copyRolesExe))
            {
                await new ContentDialog
                {
                    Title = "错误",
                    Content = $"未找到转移工具：\n{copyRolesExe}",
                    PrimaryButtonText = "确定"
                }.ShowAsync();
                return;
            }

            bool hasMain = File.Exists(mainPath);
            bool hasDLC = File.Exists(dlcPath);

            if (!hasMain && !hasDLC)
            {
                await new ContentDialog
                {
                    Title = "无存档",
                    Content = $"未找到任何地图的 world.db 存档",
                    PrimaryButtonText = "确定"
                }.ShowAsync();
                return;
            }

            string selectedSourceDb = null;
            string mapName = "";

            if (hasMain && hasDLC)
            {
                var mapDialog = new ContentDialog
                {
                    Title = "选择源地图",
                    Content = "请选择要从哪个地图读取玩家数据：",
                    PrimaryButtonText = "云雾之森",
                    SecondaryButtonText = "金色浮沙"
                };
                var res = await mapDialog.ShowAsync();

                if (res == ContentDialogResult.Primary)
                {
                    selectedSourceDb = mainPath;
                    mapName = "云雾之森";
                }
                else
                {
                    selectedSourceDb = dlcPath;
                    mapName = "金色浮沙";
                }
            }
            else
            {
                if (hasMain)
                {
                    selectedSourceDb = mainPath;
                    mapName = "云雾之森";
                }
                else
                {
                    selectedSourceDb = dlcPath;
                    mapName = "金色浮沙";
                }
            }

            var finalConfirm = new ContentDialog
            {
                Title = "开始转移",
                Content = $"源地图：{mapName}\n\n确定执行转移？",
                PrimaryButtonText = "开始",
                SecondaryButtonText = "取消"
            };
            if (await finalConfirm.ShowAsync() != ContentDialogResult.Primary)
                return;

            var processing = new ContentDialog
            {
                Title = "转移中",
                Content = "正在执行玩家数据转移...\n请勿关闭程序",
                IsPrimaryButtonEnabled = false
            };
            var _ = processing.ShowAsync();

            var psi = new ProcessStartInfo
            {
                FileName = copyRolesExe,
                Arguments = $"-src=\"{selectedSourceDb}\" -dst=\"{targetDb}\" -type=1",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            await Task.Run(() => process.WaitForExit());
            processing.Hide();

            if (process.ExitCode == 0)
            {
                await new ContentDialog
                {
                    Title = "转移成功",
                    Content = $"从【{mapName}】转移玩家数据完成！",
                    PrimaryButtonText = "确定"
                }.ShowAsync(); 
            }
            else
            {
                string err = process.StandardError.ReadToEnd();
                await new ContentDialog
                {
                    Title = "转移失败",
                    Content = $"错误代码：{process.ExitCode}\n{err}",
                    PrimaryButtonText = "确定"
                }.ShowAsync();
            }
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "异常",
                Content = $"ex.Message",
                PrimaryButtonText = "确定"
            }.ShowAsync();
        }
    }

    private void UnifiedSettingsEditorButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var existing = Application.Current.Windows.OfType<UnifiedSettingsEditor>().FirstOrDefault();
            if (existing != null)
            {
                existing.Activate();
                existing.Topmost = true;
                existing.Topmost = false;
            }
            else
            {
                try
                {
                    if (SsmSettings.AppSettings.AutoLoadEditor == true && !(ServerTabControl.SelectedIndex == -1))
                    {
                        UnifiedSettingsEditor editor = new(SsmSettings.Servers, true, ServerTabControl.SelectedIndex);
                        editor.Show();
                    }
                    else
                    {
                        UnifiedSettingsEditor editor = new(SsmSettings.Servers);
                        editor.Show();
                    }
                }
                catch (Exception ex)
                {
                    ShowLogError($"错误：{ex}");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            ShowLogError($"错误：{ex}");
            return;
        }
    }

    private async void ServerFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Server server = ((Button)sender).DataContext as Server;
        string path = server?.Path;

        try
        {
            if (string.IsNullOrEmpty(path))
            {
                await ShowErrorDialog("路径为空");
                return;
            }
        }
        catch (Exception ex)
        {
            ShowLogError(ex.Message.ToString());
        }

        if (Directory.Exists(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                await ShowErrorDialog($"打开失败：{ex.Message}");
            }
        }
        else
        {
            await ShowErrorDialog("找不到服务器文件夹。");
        }
    }

    private void AddServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Application.Current.Windows.OfType<CreateServer>().Any())
        {
            CreateServer cServer = new(SsmSettings);
            cServer.Show();
        }
    }

    private void ManageModsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Application.Current.Windows.OfType<ModManagerWindows>().Any())
        {
            ModManagerWindows modManagerWindows = new ModManagerWindows(SsmSettings);
            modManagerWindows.Show();
        }
    }

    private void ManagerSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var mSettings = Application.Current.Windows.OfType<ManagerSettings>().FirstOrDefault();
        if (mSettings != null)
        {
            mSettings.Activate();
            mSettings.Topmost = true;
            mSettings.Topmost = false;
        }
        else
        {
            if (!Application.Current.Windows.OfType<ManagerSettings>().Any())
            {
                mSettings = new(SsmSettings);
                mSettings.Show();
            }
        }
    }

    private async void VersionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string latestVersion = null;

            try
            {
                latestVersion = await HttpClient.GetStringAsync("https://raw.githubusercontent.com/aghosto/Palworld-Server-Manager/refs/heads/master/VERSION");
            }
            catch
            {
                latestVersion = await HttpClient.GetStringAsync("https://gitee.com/aGHOSToZero/Palworld-Server-Manager/raw/master/VERSION");
            }

            latestVersion = latestVersion.Trim();

            string currentVersion = AppVersion.Text
            .Replace("软件版本：", "") 
            .Trim();

            if (latestVersion != currentVersion)
            {
                ContentDialog yesNoDialog = new()
                {
                    Content = $"软件有新版本可用于下载，需要关闭软件进行更新，是否更新？\r\r当前版本：{currentVersion}\r最新版本：{latestVersion}",
                    PrimaryButtonText = "是",
                    SecondaryButtonText = "否"
                };

                if (await yesNoDialog.ShowAsync() is ContentDialogResult.Primary)
                {
                    Process.Start("SSMUpdater.exe");
                    Process.GetCurrentProcess().Kill();
                }
                else
                {
                    ShowLogWarning($"用户取消了本次软件更新。");
                }
            }
            else
            {
                ShowLogDefault($"当前软件已是最新版本：{latestVersion}");
            }
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("不知道这样的主机") || ex.Message.Contains("无法连接") || ex.Message.Contains("404"))
            {
                ShowLogError($"检查更新失败：网络异常或服务器不可用");
            }
            else
            {
                ShowLogError($"检查更新错误：{ex.Message}");
            }
        }
    }

    private void RconServerButton_Click(object sender, RoutedEventArgs e)
    {
        Server server = ((Button)sender).DataContext as Server;

        if (!Application.Current.Windows.OfType<RconConsole>().Any())
        {
            RconConsole rConsole = new(server);
            rConsole.Show();
        }
    }

    private void RestServerButton_Click(object sender, RoutedEventArgs e)
    {
        Server server = ((Button)sender).DataContext as Server;

        if (!Application.Current.Windows.OfType<RestConsole>().Any())
        {
            RestConsole restConsole = new(server);
            restConsole.Show();
        }
    }

    // 修复工具
    private void FixTools_Click(object sender, RoutedEventArgs e)
    {
        
    }

    private async void DonateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string newDonateUrl = "https://afdian.com/a/aGHOSToZero/plan";

            Process.Start(new ProcessStartInfo(newDonateUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"无法打开爱发电页面：{ex.Message}");
        }
    }

    private async void ReportIssue_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string newIssueUrl = "https://github.com/aghosto/Palworld-Server-Manager/issues/new";

            Process.Start(new ProcessStartInfo(newIssueUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"无法打开问题反馈页面：{ex.Message}");
        }
    }

    private void RefreshLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string logType)
        {
            LoadLogByType(_logTagToType[logType], true);
        }
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string logType && _logTagToType.ContainsKey(logType))
        {
            _logTypeToTexbox[_logTagToType[logType]].Document.Blocks.Clear();
            ShowLogMsg("日志已清空", Brushes.Gray, _logTagToType[logType]);
        }
    }

    private async void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentServer == null)
        {
            await ShowErrorDialog($"未找到对应的服务器实例");
            return;
        }

        if (sender is not Button btn || btn.Tag is not string logType || !_logTagToType.ContainsKey(logType))
        {
            ShowLogError($"日志类型配置错误");
            return;
        }

        try
        {
            string logPath = string.Empty;
            switch (_logTagToType[logType])
            {
                case LogType.WSServer:
                    logPath = _ssmPathManager.LogsPath;
                    break;
                case LogType.MainConsole:
                    break;
                default:
                    await ShowErrorDialog($"不支持的日志类型");
                    return;
            }

            if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
            {
                await ShowErrorDialog($"日志文件不存在：{logPath}");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = logPath,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"打开日志失败：{ex.Message}");
        }
    }

    private async void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(_ssmPathManager.LogsDir))
            {
                await ShowErrorDialog("路径为空");
                return;
            }
        }
        catch (Exception ex)
        {
            ShowLogError(ex.Message.ToString());
        }

        if (Directory.Exists(_ssmPathManager.LogsDir))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _ssmPathManager.LogsDir,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                await ShowErrorDialog($"打开失败：{ex.Message}");
            }
        }
        else
        {
            await ShowErrorDialog("找不到服务器文件夹。");
        }
    }

    // 点击托盘显示或隐藏
    private void TrayIcon_Click(object sender, RoutedEventArgs e)
    {
        if (Visibility == Visibility.Visible)
            Hide();
        else
        {
            Show();
            Activate();
        }
    }

    private async void RefreshPlayerList_Click(object sender, RoutedEventArgs e) 
    {
        await RefreshPlayersAsync();
        LoadBannedPlayersFromFile();
    }

    private async void BanPlayerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerDataGrid.SelectedItem is not PlayerDisplayInfo selectedPlayer) return;

        string settingsPath = Path.Combine(_currentServer.Path, "SaveData", "Settings", "ServerSettings.json");
        if (!File.Exists(settingsPath)) return;
        string json = File.ReadAllText(settingsPath);
        var combined = System.Text.Json.JsonSerializer.Deserialize<CombinedServerSettings>(json);
        if (combined?.HostSettings == null) return;

        using var restClient = new RestApiClient();
        restClient.SetAuth("127.0.0.1", combined.HostSettings.RESTAPIPort, combined.HostSettings.AdminPassword);
        await restClient.BanPlayerAsync(selectedPlayer.UserId);
        
        LoadBannedPlayersFromFile();
        await RefreshPlayersAsync();
    }

    private async void KickPlayerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerDataGrid.SelectedItem is not PlayerDisplayInfo selectedPlayer) return;

        string settingsPath = Path.Combine(_currentServer.Path, "SaveData", "Settings", "ServerSettings.json");
        if (!File.Exists(settingsPath)) return;
        string json = File.ReadAllText(settingsPath);
        var combined = System.Text.Json.JsonSerializer.Deserialize<CombinedServerSettings>(json);
        if (combined?.HostSettings == null) return;

        using var restClient = new RestApiClient();
        restClient.SetAuth("127.0.0.1", combined.HostSettings.RESTAPIPort, combined.HostSettings.AdminPassword);
        await restClient.KickPlayerAsync(selectedPlayer.UserId);
        await RefreshPlayersAsync();
    }

    private async void UnBanPlayerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (BanListDataGrid.SelectedItem is not PlayerDisplayInfo selectedPlayer)
            return;

        string settingsPath = Path.Combine(_currentServer.Path, "SaveData", "Settings", "ServerSettings.json");
        if (!File.Exists(settingsPath)) return;
        string json = File.ReadAllText(settingsPath);
        var combined = System.Text.Json.JsonSerializer.Deserialize<CombinedServerSettings>(json);
        if (combined?.HostSettings == null) return;

        using var restClient = new RestApiClient();
        restClient.SetAuth("127.0.0.1", combined.HostSettings.RESTAPIPort, combined.HostSettings.AdminPassword);
        await restClient.UnbanPlayerAsync(selectedPlayer.UserId);
        LoadBannedPlayersFromFile();
        await RefreshPlayersAsync();
    }

    private static void SaveHostSettings(Server server, ServerManagementSettings hostSettings)
    {
        string settingsDir = Path.Combine(server.Path, "SaveData", "Settings");
        string path = Path.Combine(settingsDir, "ServerSettings.json");

        CombinedServerSettings combined;
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                combined = System.Text.Json.JsonSerializer.Deserialize<CombinedServerSettings>(json) ?? new CombinedServerSettings();
            }
            catch
            {
                combined = new CombinedServerSettings();
            }
        }
        else
        {
            combined = new CombinedServerSettings();
        }

        combined.HostSettings = hostSettings;
        string output = System.Text.Json.JsonSerializer.Serialize(combined, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        File.WriteAllText(path, output);
    }

    private static void TryCreateStartServerBatFromSettings(Server server)
    {
        string batPath = Path.Combine(server.Path, "StartServer.bat");
        string settingsPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");

        if (!File.Exists(settingsPath)) return;

        try
        {
            string json = File.ReadAllText(settingsPath);
            var combined = System.Text.Json.JsonSerializer.Deserialize<CombinedServerSettings>(json);
            if (combined == null) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("pushd \"%~dp0\"");
            sb.Append($"PalServer.exe -Port={combined.HostSettings.Port}");

            if (combined.HostSettings.PublicLobby)
                sb.Append(" -publiclobby");

            if (combined.Performances.bUseMultiThreadPerformance)
            {
                sb.Append(" -useperfthreads -NoAsyncLoadingThread -UseMultithreadForDS");
                sb.Append($" -NumberOfWorkerThreadsServer={combined.Performances.NumberOfWorkerThreadsServer}");
            }

            sb.AppendLine();
            sb.AppendLine("popd");
            sb.Append("exit /B");

            File.WriteAllText(batPath, sb.ToString(), System.Text.Encoding.UTF8);
        }
        catch
        {
        }
    }

    #endregion

    private void LoadBannedPlayersFromFile()
    {
        _bannedPlayers.Clear();
        if (!File.Exists(_ssmPathManager.BanListPath)) return;

        var lines = File.ReadAllLines(_ssmPathManager.BanListPath);

        foreach (var line in lines)
        {
            string raw = line.Trim();
            if (string.IsNullOrWhiteSpace(raw)) continue;

            string platform = "";
            string displayId = raw;
            int underscoreIdx = raw.IndexOf('_');
            if (underscoreIdx > 0)
            {
                platform = raw.Substring(0, underscoreIdx);
                displayId = raw.Substring(underscoreIdx + 1);
            }

            _bannedPlayers.Add(new PlayerDisplayInfo
            {
                CharacterName = "[已封禁]",
                UserId = raw,
                SteamId = displayId,
                Platform = platform
            });
        }
        BanListDataGrid.ItemsSource = _bannedPlayers;
    }

    private void DirectoryCopy(string sourceDir, string destDir, bool copySubDirs)
    {
        DirectoryInfo dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        Directory.CreateDirectory(destDir);

        foreach (FileInfo file in dir.GetFiles())
        {
            string target = Path.Combine(destDir, file.Name);
            file.CopyTo(target, true);
        }

        if (copySubDirs)
        {
            foreach (DirectoryInfo sub in dir.GetDirectories())
            {
                string targetSub = Path.Combine(destDir, sub.Name);
                DirectoryCopy(sub.FullName, targetSub, true);
            }
        }
    }

    /// <summary>
    /// 生成时间戳字符串
    /// </summary>
    /// <param name="format" 时间戳格式/>
    /// <returns>格式化后的时间戳字符串</returns>
    public static string GetTimestamp(string format = "file")
    {
        DateTime now = DateTime.Now;

        return format.ToLower() switch
        {
            "file" => now.ToString("yyyyMMdd_HHmmss"),
            "log" => now.ToString("yyyy/MM/dd HH:mm:ss"),
            "unix" => ((long)(now - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds).ToString(),
            "unix-ms" => ((long)(now - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds).ToString(),
            _ => now.ToString(format)
        };
    }

    public void ShowLogError(string message, LogType logType = LogType.MainConsole) => ShowLogMsg($"{message}", Brushes.Red, logType);
    public void ShowLogWarning(string message, LogType logType = LogType.MainConsole) => ShowLogMsg($"{message}", Brushes.Yellow, logType);
    public void ShowLogDefault(string message, LogType logType = LogType.MainConsole) => ShowLogMsg($"{message}", Brushes.Lime, logType);
    public void ShowLogMsg(string message, Brush color, LogType logType = LogType.MainConsole)
    {
        if (Dispatcher.CheckAccess())
            InternalShowLogMsg(logType, message, color);
        else
            Dispatcher.Invoke(() => InternalShowLogMsg(logType, message, color));
    }
}
