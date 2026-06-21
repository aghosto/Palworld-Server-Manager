using Microsoft.Win32;
using ModernWpf.Controls;
using PalworldServerManager.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;


namespace PalworldServerManager
{
    public partial class UnifiedSettingsEditor : Window
    {
        private CombinedServerSettings combinedSettings;
        private readonly ObservableCollection<Server> servers;
        private ObservableCollection<TechItem> techItems = new ObservableCollection<TechItem>();
        private ICollectionView techView;
        private static readonly JsonSerializerOptions serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public ObservableCollection<TechItem> TechItems
        {
            get => techItems;
            set => techItems = value;
        }

        public UnifiedSettingsEditor(ObservableCollection<Server> sentServers, bool autoLoad = false, int indexToLoad = -1)
        {
            servers = sentServers;
            combinedSettings = new CombinedServerSettings();
            DataContext = combinedSettings;
            InitializeComponent();

            InitializeTechItems();
            InitializeDeathPenaltyComboBox();
            InitializeRandomizerTypeComboBox();
            InitializeDifficultyComboBox();
            InitializeLogFormatComboBox();
            InitializeWorkerThreads();
            UpdateTechCountText();
            ApplyPvPState();
            SyncPvPDropItemIdBox();

            if (autoLoad && indexToLoad != -1 && servers.Count > 0)
            {
                AutoLoad(indexToLoad);
            }
        }

        #region Initialization

        private void InitializeDeathPenaltyComboBox()
        {
            DeathPenaltyComboBox.SelectedIndex = combinedSettings.GameBalances.DeathPenalty switch
            {
                "None" => 0,
                "Item" => 1,
                "ItemAndEquipment" => 2,
                _ => 3
            };
        }

        private void InitializeRandomizerTypeComboBox()
        {
            switch (combinedSettings.Features.RandomizerType)
            {
                case "None": RandomizerTypeComboBox.SelectedIndex = 0; break;
                case "Region": RandomizerTypeComboBox.SelectedIndex = 1; break;
                case "All": RandomizerTypeComboBox.SelectedIndex = 2; break;
                default: RandomizerTypeComboBox.SelectedIndex = 0; break;
            }
            UpdateRandomizerSeedState();
        }

        private void UpdateRandomizerSeedState()
        {
            bool isNone = RandomizerTypeComboBox.SelectedIndex == 0;
            RandomizerSeedTextBox.IsEnabled = !isNone;
            if (isNone)
            {
                combinedSettings.Features.RandomizerSeed = "";
                RandomizerSeedTextBox.Text = "";
            }
        }

        private void InitializeDifficultyComboBox()
        {
            DifficultyComboBox.SelectedIndex = combinedSettings.HostSettings.Difficulty switch
            {
                "None" => 0,
                "Easy" => 1,
                "Normal" => 2,
                "Hard" => 3,
                _ => 0
            };
        }

        private void InitializeLogFormatComboBox()
        {
            LogFormatComboBox.SelectedIndex = combinedSettings.HostSettings.LogFormatType == "Json" ? 1 : 0;
        }

        private void InitializeWorkerThreads()
        {
            int maxThreads = Environment.ProcessorCount;
            WorkerThreadsNumberBox.Maximum = maxThreads;
            if (combinedSettings.Performances.NumberOfWorkerThreadsServer > maxThreads)
                combinedSettings.Performances.NumberOfWorkerThreadsServer = maxThreads;
        }

        private void InitializeTechItems()
        {
            foreach (var kvp in UnifiedSettingsEditor.CreateTechData())
            {
                var item = new TechItem
                {
                    Id = kvp.Key,
                    Name = kvp.Value.Name,
                    NameCn = kvp.Value.NameCn,
                    Description = kvp.Value.Description
                };
                item.PropertyChanged += TechItem_PropertyChanged;
                TechItems.Add(item);
            }

            SyncTechItemsFromModel();
            techView = CollectionViewSource.GetDefaultView(techItems);
        }

        

        private void SyncTechItemsFromModel()
        {
            string raw = combinedSettings.GameBalances.DenyTechnologyList;
            if (string.IsNullOrEmpty(raw) || raw == "()")
            {
                foreach (var item in techItems)
                    item.IsChecked = false;
                return;
            }

            var selected = new HashSet<string>(
                raw.Trim('(', ')').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => s.Trim()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var item in techItems)
                item.IsChecked = selected.Contains(item.Id);
        }

        private void SyncTechItemsToModel()
        {
            var selected = techItems.Where(t => t.IsChecked).Select(t => t.Id).ToList();
            if (selected.Count == 0)
                combinedSettings.GameBalances.DenyTechnologyList = "()";
            else
                combinedSettings.GameBalances.DenyTechnologyList = $"({string.Join(", ", selected)})";
        }

        private void UpdateTechCountText()
        {
            int count = techItems?.Count(t => t.IsChecked) ?? 0;
            TechCountTextBlock.Text = count.ToString();
        }

        #endregion

        #region Difficulty / LogFormat / RandomizerType

        private void DifficultyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (combinedSettings?.HostSettings == null) return;
            combinedSettings.HostSettings.Difficulty = DifficultyComboBox.SelectedIndex switch
            {
                0 => "None",
                1 => "Easy",
                2 => "Normal",
                3 => "Hard",
                _ => "None"
            };
        }

        private void LogFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (combinedSettings?.HostSettings == null) return;
            combinedSettings.HostSettings.LogFormatType = LogFormatComboBox.SelectedIndex == 0 ? "Text" : "Json";
        }

        private void RandomizerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (combinedSettings?.Features == null) return;
            combinedSettings.Features.RandomizerType = RandomizerTypeComboBox.SelectedIndex switch
            {
                1 => "Region",
                2 => "All",
                _ => "None"
            };
            UpdateRandomizerSeedState();
        }

        private void DeathPenalty_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (combinedSettings?.GameBalances == null) return;
            combinedSettings.GameBalances.DeathPenalty = DeathPenaltyComboBox.SelectedIndex switch
            {
                0 => "None",
                1 => "Item",
                2 => "ItemAndEquipment",
                _ => "All"
            };
        }

        #endregion

        #region Tech Search

        private void TechSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filter = TechSearchBox.Text?.Trim().ToLower() ?? "";
            Application.Current.Dispatcher.Invoke(() =>
            {
                techView.Filter = item =>
                {
                    var tech = item as TechItem;
                    return tech != null && tech.DisplayName.ToLower().Contains(filter);
                };
            });
        }

        #endregion

        #region PvP

        private void IsPvP_CheckedChanged(object sender, RoutedEventArgs e)
        {
            ApplyPvPState();
        }

        private void ApplyPvPState()
        {
            bool isPvP = combinedSettings.Features.bIsPvP;
            CheckDisplayPvPItemNumOnWorldMap_BaseCamp.IsEnabled = isPvP;
            CheckDisplayPvPItemNumOnWorldMap_Player.IsEnabled = isPvP;
            CheckEnableDefenseOtherGuildPlayer.IsEnabled = isPvP;
            CheckInvisibleOtherGuildBaseCampAreaFX.IsEnabled = isPvP;
            PvPDropItemIdPanel.IsEnabled = isPvP;
            PvPDropItemNumPanel.IsEnabled = isPvP;
            CheckAdditionalPvPDrop.IsEnabled = isPvP;
        }

        private void SyncPvPDropItemIdBox()
        {
            string raw = combinedSettings.GameBalances.AdditionalDropItemWhenPlayerKillingInPvPMode;
            var match = Regex.Match(raw, @"PlayerDropItem\(""(.*?)""\)");
            PvPDropItemIdBox.Text = match.Success ? match.Groups[1].Value : raw;
        }

        private void PvPDropItemIdBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            combinedSettings.GameBalances.AdditionalDropItemWhenPlayerKillingInPvPMode = $"PlayerDropItem(\"{PvPDropItemIdBox.Text}\")";
        }

        #endregion

        #region Crossplay

        private void SyncCrossplayToModel()
        {
            var platforms = new List<string>();
            if (CrossplaySteamCheckBox.IsChecked == true) platforms.Add("Steam");
            if (CrossplayXboxCheckBox.IsChecked == true) platforms.Add("Xbox");
            if (CrossplayPS5CheckBox.IsChecked == true) platforms.Add("PS5");
            combinedSettings.HostSettings.CrossplayPlatforms = $"({string.Join(",", platforms)})";
        }

        private void SyncCrossplayFromModel()
        {
            string raw = combinedSettings.HostSettings.CrossplayPlatforms;
            var platforms = raw.Trim('(', ')').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(p => p.Trim()).ToHashSet();
            CrossplaySteamCheckBox.IsChecked = platforms.Contains("Steam");
            CrossplayXboxCheckBox.IsChecked = platforms.Contains("Xbox");
            CrossplayPS5CheckBox.IsChecked = platforms.Contains("PS5");
        }

        #endregion

        #region Load / Save

        private void AutoLoad(int serverIndex)
        {
            LoadFromServer(servers[serverIndex]);
        }

        private void LoadFromServer(Server server)
        {
            string combinedPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");

            if (File.Exists(combinedPath))
            {
                try
                {
                    string json = File.ReadAllText(combinedPath);
                    var loaded = JsonSerializer.Deserialize<CombinedServerSettings>(json);
                    if (loaded != null)
                    {
                        combinedSettings = loaded;
                        DataContext = combinedSettings;
                        ApplyAfterLoad();
                        return;
                    }
                }
                catch { }
            }

            DataContext = combinedSettings;
            ApplyAfterLoad();
        }

        private void ApplyAfterLoad()
        {
            SyncTechItemsFromModel();
            SyncCrossplayFromModel();
            UpdateTechCountText();
            ApplyPvPState();
            SyncPvPDropItemIdBox();

            DifficultyComboBox.SelectedIndex = combinedSettings.HostSettings.Difficulty switch
            {
                "None" => 0,
                "Easy" => 1,
                "Normal" => 2,
                "Hard" => 3,
                _ => 0
            };

            LogFormatComboBox.SelectedIndex = combinedSettings.HostSettings.LogFormatType == "Json" ? 1 : 0;

            RandomizerTypeComboBox.SelectedIndex = combinedSettings.Features.RandomizerType switch
            {
                "None" => 0,
                "Region" => 1,
                "All" => 2,
                _ => 0
            };
        }
        public static CombinedServerSettings LoadServerSettings(string settingsPathOrDir)
        {
            string path = settingsPathOrDir;
            if (!File.Exists(path) && Directory.Exists(path))
                path = Path.Combine(path, "SaveData", "Settings", "ServerSettings.json");

            if (!File.Exists(path))
                return new CombinedServerSettings();

            try
            {
                string json = File.ReadAllText(path);
                var combined = System.Text.Json.JsonSerializer.Deserialize<CombinedServerSettings>(json);
                return combined ?? new CombinedServerSettings();
            }
            catch
            {
                return new CombinedServerSettings();
            }
        }
        public static void CreateDefaultSettingsFile(Server server)
        {
            string dir = Path.Combine(server.Path, "SaveData", "Settings");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var defaultSettings = new CombinedServerSettings();

            string json = System.Text.Json.JsonSerializer.Serialize(defaultSettings, serializerOptions);
            File.WriteAllText(Path.Combine(dir, "ServerSettings.json"), json);
        }

        private async void FileMenuLoad_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "JSON files|*.json",
                DefaultExt = "json",
                FileName = "ServerSettings.json",
                InitialDirectory = Directory.GetCurrentDirectory()
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                string json = File.ReadAllText(dialog.FileName);
                var loaded = JsonSerializer.Deserialize<CombinedServerSettings>(json);
                if (loaded != null)
                {
                    combinedSettings = loaded;
                    DataContext = combinedSettings;
                    ApplyAfterLoad();
                }
            }
            catch
            {
                _ = new ContentDialog
                {
                    Owner = this,
                    Title = "错误",
                    Content = "文件加载失败，请检查文件格式。",
                    CloseButtonText = "确定"
                }.ShowAsync();
            }
        }

        private async void FileMenuSave_Click(object sender, RoutedEventArgs e)
        {
            if (servers.Count > 0)
            {
                ContentDialog yesNoDialog = new()
                {
                    Content = "是否自动保存到服务器？如果原始文件存在，将创建其备份。",
                    PrimaryButtonText = "是",
                    SecondaryButtonText = "否"
                };

                if (await yesNoDialog.ShowAsync() is ContentDialogResult.Primary)
                {
                    EditorSaveDialog saveDialog = new(servers)
                    {
                        PrimaryButtonText = "保存",
                        CloseButtonText = "取消"
                    };

                    if (await saveDialog.ShowAsync() is ContentDialogResult.Primary)
                    {
                        Server server = saveDialog.GetServer();
                        await SaveToServer(server);
                        return;
                    }
                }
            }
        }

        private async Task SaveToServer(Server server)
        {
            if (!combinedSettings.HostSettings.bUseManualPublicIP)
            {
                try
                {
                    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    string ip = await httpClient.GetStringAsync("http://ipinfo.io/ip");
                    combinedSettings.HostSettings.PublicIP = ip.Trim();
                }
                catch
                {
                    combinedSettings.HostSettings.bUseManualPublicIP = true;
                }
            }

            SyncTechItemsToModel();
            SyncCrossplayToModel();

            string settingsDir = Path.Combine(server.Path, "SaveData", "Settings");
            if (!Directory.Exists(settingsDir))
                Directory.CreateDirectory(settingsDir);

            string combinedPath = Path.Combine(settingsDir, "ServerSettings.json");
            if (File.Exists(combinedPath))
                File.Copy(combinedPath, Path.Combine(settingsDir, "ServerSettings.bak"), true);

            string json = JsonSerializer.Serialize(combinedSettings, serializerOptions);
            File.WriteAllText(combinedPath, json);

            string iniPath = Path.Combine(server.Path, "Pal", "Saved", "Config", "WindowsServer", "PalWorldSettings.ini");
            if (File.Exists(iniPath))
            {
                string iniContent = File.ReadAllText(iniPath).Trim();
                if (string.IsNullOrEmpty(iniContent) || iniContent == "OptionSettings=()" || iniContent.Contains("OptionSettings=()") || iniContent.Contains("OptionSettings=\"\"") || iniContent == "\"\"")
                {
                    string defaultIniPath = Path.Combine(server.Path, "DefaultPalWorldSettings.ini");
                    if (File.Exists(defaultIniPath))
                    {
                        var lines = new List<string>();
                        bool inSection = false;
                        foreach (string rawLine in File.ReadAllLines(defaultIniPath))
                        {
                            string trimmed = rawLine.Trim();
                            if (trimmed.StartsWith(";") || trimmed.StartsWith("#")) continue;
                            if (trimmed.StartsWith("[/Script")) { inSection = true; lines.Add(rawLine); continue; }
                            if (inSection) lines.Add(rawLine);
                        }
                        File.WriteAllLines(iniPath, lines);
                    }
                }
            }

            TrySaveToPalWorldSettingsIni(server);
            TryUpdateStartServerBat(server);

            _ = new ContentDialog()
            {
                Content = "文件已成功保存到：\n" + combinedPath,
                PrimaryButtonText = "确定",
            }.ShowAsync();
        }

        private void FileMenuExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        #region INI Save

        private static void TryUpdateStartServerBat(Server server)
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

        internal static void TrySaveToPalWorldSettingsIni(Server server)
        {
            string jsonPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");
            if (!File.Exists(jsonPath)) return;

            string json = File.ReadAllText(jsonPath);
            var settings = JsonSerializer.Deserialize<CombinedServerSettings>(json);
            if (settings == null) return;

            string iniPath = Path.Combine(server.Path, "Pal", "Saved", "Config", "WindowsServer", "PalWorldSettings.ini");
            if (!File.Exists(iniPath))
            {
                System.Windows.MessageBox.Show(
                    "未找到 PalWorldSettings.ini 文件。\n\n请先启动服务器一次以生成默认配置文件，\n然后再保存服务器参数。",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(iniPath);

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (!line.StartsWith("OptionSettings="))
                        continue;

                    int startIdx = line.IndexOf('(');
                    int endIdx = line.LastIndexOf(')');

                    if (startIdx == -1 || endIdx == -1 || endIdx <= startIdx)
                        continue;

                    string prefix = line[..(startIdx + 1)];
                    string suffix = line[endIdx..];
                    string content = line.Substring(startIdx + 1, endIdx - startIdx - 1);

                    var pairs = ParseIniPairs(content);

                    foreach (var subObj in new object[] { settings.HostSettings, settings.Performances, settings.Features, settings.GameBalances })
                    {
                        foreach (var prop in subObj.GetType().GetProperties())
                        {
                            if (pairs.ContainsKey(prop.Name))
                            {
                                object? val = prop.GetValue(subObj);
                                pairs[prop.Name] = SerializeIniValue(val, prop.Name);
                            }
                        }
                    }

                    lines[i] = prefix + string.Join(",", pairs.Select(p => $"{p.Key}={p.Value}")) + suffix;
                    File.WriteAllLines(iniPath, lines);
                    break;
                }
            }
            catch
            {
            }
        }

        private static Dictionary<string, string> ParseIniPairs(string content)
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
                    i++;
                    start = i;
                    while (i < content.Length && content[i] != '"') i++;
                    val = content.Substring(start, i - start);
                    i++;
                }
                else if (i < content.Length && content[i] == '(')
                {
                    int depth = 1;
                    start = i;
                    i++;
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

                if (!string.IsNullOrEmpty(key))
                    result[key] = val;

                i++;
            }

            return result;
        }

        private static bool IsIniEnumField(string propertyName)
        {
            return propertyName is "Difficulty" or "DeathPenalty" or "RandomizerType" or "LogFormatType";
        }

        private static string SerializeIniValue(object? value, string propertyName = "")
        {
            if (value == null) return "";
            if (value is double d) return d.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
            if (value is float f) return f.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
            if (value is bool b) return b ? "True" : "False";
            if (value is string str)
            {
                if (str.Length == 0) return "";
                if (IsIniEnumField(propertyName)) return str;
                if (str.StartsWith('(') && str.EndsWith(')')) return str;
                if (Regex.IsMatch(str, @"^[A-Za-z_]\w*\(.*\)$")) return str;
                if (propertyName is "ServerName" or "RandomizerSeed" or "ServerDescription" or "AdminPassword" or "ServerPassword" or "PublicIP" or "Region" or "BanListURL")
                    return $"\"{str}\"";
                return str;
            }
            return value.ToString() ?? "";
        }

        #endregion

        #region Profile Switching

        private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        #endregion

        #region Tech Count

        private void TechItem_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TechItem.IsChecked))
            {
                UpdateTechCountText();
            }
        }

        internal static Dictionary<string, TechItemData> CreateTechData()
        {
            return new Dictionary<string, TechItemData>
            {
                { "Accessory_AirDash1",                       new TechItemData { Name = "Air Dash Boots",                     NameCn = "空中冲刺靴",           Description = "装备后可使玩家获得空中冲刺能力的饰品。空中冲刺+1" } },
                { "Accessory_AirDash2",                       new TechItemData { Name = "Double Air Dash Boots",              NameCn = "空中二段冲刺靴",       Description = "装备后可使玩家获得空中二段冲刺能力的饰品。空中冲刺+2" } },
                { "Accessory_AirDash3",                       new TechItemData { Name = "Triple Air Dash Boots",              NameCn = "空中三段冲刺靴",       Description = "装备后可使玩家获得空中三段冲刺能力的饰品。空中冲刺+3" } },
                { "Accessory_JumpCount_Increase1",            new TechItemData { Name = "Double Jump Boots",                  NameCn = "二段跳靴",             Description = "装备后可使玩家获得二段跳跃能力的饰品。连续跳跃次数+1" } },
                { "Accessory_JumpCount_Increase2",            new TechItemData { Name = "Triple Jump Boots",                  NameCn = "三段跳靴",             Description = "装备后可使玩家获得三段跳跃能力的饰品。连续跳跃次数+2" } },
                { "Accessory_JumpPower_Increase",             new TechItemData { Name = "Anti-Gravity Belt",                  NameCn = "反重力腰带",           Description = "装备后可提升玩家跳跃力的饰品。跳跃力强化" } },
                { "Accessory_Nonkilling",                     new TechItemData { Name = "Ring of Mercy",                      NameCn = "慈悲戒指",             Description = "和平主义者的戒指。\n\n装备该戒指者无法将攻击对象的生命值降低到1以下。手下留情" } },
                { "Accessory_TalentChecker",                  new TechItemData { Name = "Ability Glasses",                    NameCn = "伯乐眼镜",             Description = "能够确认帕鲁隐藏才能的眼镜。\n\n生命值，攻击力和防御力的潜力会以满分100分显示出来。" } },
                { "AdditionalInventory_001",                  new TechItemData { Name = "Small Pouch",                        NameCn = "小型扩展背包",         Description = "能夠额外携带物品的小型背包。\n\n持有此道具时，可增加背包栏位。" } },
                { "AdditionalInventory_002",                  new TechItemData { Name = "Medium Pouch",                       NameCn = "中型扩展背包",         Description = "能夠额外携带物品的普通背包。\n\n持有此道具时，可增加背包栏位。" } },
                { "AdditionalInventory_003",                  new TechItemData { Name = "Large Pouch",                        NameCn = "大型扩展背包",         Description = "能夠额外携带物品的大背包。\n\n持有此道具时，可增加背包栏位。" } },
                { "AdditionalInventory_004",                  new TechItemData { Name = "Giant Pouch",                        NameCn = "巨大扩展背包",         Description = "能夠额外携带物品的巨大背包。\n\n持有此道具时，可增加背包栏位。" } },
                { "Altar",                                    new TechItemData { Name = "Summoning Altar",                    NameCn = "召唤的祭坛",           Description = "通过献上画着帕鲁的石板，\n\n就能在据点召唤强大的帕鲁。\n\n做好充分的战斗准备吧。" } },
                { "Arrow",                                    new TechItemData { Name = "Arrow",                              NameCn = "箭",                   Description = "弓专用的箭。" } },
                { "Arrow_Fire",                               new TechItemData { Name = "Fire Arrow",                         NameCn = "火箭",                 Description = "「火之弓」与「火箭十字弓」等用的箭。\n\n可对目标造成火属性伤害。" } },
                { "Arrow_Poison",                             new TechItemData { Name = "Poison Arrow",                       NameCn = "毒箭",                 Description = "「毒之弓」与「毒箭十字弓」等用的箭。\n\n可以让被射中的目标中毒。" } },
                { "AssaultRifleBullet",                       new TechItemData { Name = "Assault Rifle Ammo",                 NameCn = "突击步枪子弹",         Description = "用于突击步枪等武器的子弹。" } },
                { "AutoMealPouch_Tier1",                      new TechItemData { Name = "Small Feed Bag",                     NameCn = "小饲料袋",             Description = "装食物的小口袋。\n\n可开放1个自动进食装备槽。\n\n饥饿时可自动消耗槽内的食物。" } },
                { "AutoMealPouch_Tier2",                      new TechItemData { Name = "Average Feed Bag",                   NameCn = "普通饲料袋",           Description = "装食物的中型口袋。\n\n可开放2个自动进食装备槽。\n\n饥饿时可自动消耗槽内的食物。" } },
                { "AutoMealPouch_Tier3",                      new TechItemData { Name = "Large Feed Bag",                     NameCn = "大饲料袋",             Description = "装食物的大口袋。\n\n可开放3个自动进食装备槽。\n\n饥饿时可自动消耗槽内的食物。" } },
                { "AutoMealPouch_Tier4",                      new TechItemData { Name = "Huge Feed Bag",                      NameCn = "巨大饲料袋",           Description = "装食物的特大口袋。\n\n可开放4个自动进食装备槽。\n\n饥饿时可自动消耗槽内的食物。" } },
                { "AutoMealPouch_Tier5",                      new TechItemData { Name = "Giant Feed Bag",                     NameCn = "特大饲料袋",           Description = "用来装食物的最大号袋子。\n\n可解锁5个自动进食专用的装备栏位。\n\n帕鲁肚子饿了就会自动吃掉该栏位内的食物。" } },
                { "BaseCampBattleDirector",                   new TechItemData { Name = "Alarm Bell",                         NameCn = "警钟",                 Description = "用于更改据点内帕鲁警戒状态的钟。\n\n可随时于「迎击外敌」及「专心工作」之间切换。" } },
                { "BaseCampItemDispenser",                    new TechItemData { Name = "Item Retrieval Machine",             NameCn = "道具存取机",           Description = "可以自由取出设置的据点内\n\n所有箱子中道具的装置。" } },
                { "BaseCampWorkHard",                         new TechItemData { Name = "Monitoring Stand",                   NameCn = "监控台",               Description = "可对据点内的帕鲁下达工作指示。\n\n请注意不要让它们劳动过度了。" } },
                { "BaseCampWorkerExtraStation",               new TechItemData { Name = "Palbox Control Device",              NameCn = "帕鲁终端控制台",       Description = "可远程操作帕鲁终端的设备。\n\n将它配置在你想要部署帕鲁的位置，自由地打造据点吧。" } },
                { "Battle_Armor_Grade_01_Cloth",              new TechItemData { Name = "Cloth Outfit",                       NameCn = "布衣服",               Description = "以布制成的衣服。\n\n可以抵御夜间的寒意。耐寒Lv1" } },
                { "Battle_Armor_Grade_01_Cloth_Cold",         new TechItemData { Name = "Tundra Outfit",                      NameCn = "寒冷地区民族服饰",     Description = "在寒冷地区生产的布衣服。\n\n能够抵御使身体冻僵的寒意。耐寒Lv2" } },
                { "Battle_Armor_Grade_01_Cloth_Heat",         new TechItemData { Name = "Tropical Outfit",                    NameCn = "热带地区民族服饰",     Description = "在炎热地区生产的布衣服。\n\n具备傲人的耐热性能。耐暑Lv2" } },
                { "Battle_Armor_Grade_02_Fur",                new TechItemData { Name = "Pelt Armor",                         NameCn = "毛皮盔甲",             Description = "以皮革制成的盔甲。\n\n耐久度和防御力都得到了提升。耐寒Lv1" } },
                { "Battle_Armor_Grade_02_Fur_Cold",           new TechItemData { Name = "Cold Resistant Pelt Armor",          NameCn = "耐寒毛皮盔甲",         Description = "在寒冷地区生产的毛皮盔甲。\n\n能够抵御使身体冻僵的寒意。耐寒Lv2" } },
                { "Battle_Armor_Grade_02_Fur_Heat",           new TechItemData { Name = "Heat Resistant Pelt Armor",          NameCn = "耐热毛皮盔甲",         Description = "在炎热地区生产的毛皮盔甲。\n\n具备傲人的耐热性能。耐暑Lv2" } },
                { "Battle_Armor_Grade_03_Copper",             new TechItemData { Name = "Metal Armor",                        NameCn = "金属盔甲",             Description = "以金属制成的盔甲。\n\n有一定防御力，也有一定重量。耐寒Lv1 耐暑Lv1" } },
                { "Battle_Armor_Grade_03_Copper_Cold",        new TechItemData { Name = "Cold Resistant Metal Armor",         NameCn = "耐寒金属盔甲",         Description = "经过改良的金属盔甲。\n\n能够抵御使身体冻僵的寒意。耐寒Lv2 耐暑Lv1" } },
                { "Battle_Armor_Grade_03_Copper_Heat",        new TechItemData { Name = "Heat Resistant Metal Armor",         NameCn = "耐热金属盔甲",         Description = "经过改良的金属盔甲。\n\n具备傲人的耐热性能。耐暑Lv2 耐寒Lv1" } },
                { "Battle_Armor_Grade_03_Iron",               new TechItemData { Name = "Refined Metal Armor",                NameCn = "精炼金属盔甲",         Description = "以优质金属制成的盔甲。\n\n防御力极优，但也相当沉重。耐寒Lv1 耐暑Lv1" } },
                { "Battle_Armor_Grade_03_Iron_Cold",          new TechItemData { Name = "Cold Resistant Refined Metal Armor", NameCn = "耐寒精炼金属盔甲",     Description = "经过改良的精炼金属盔甲。\n\n能够抵御使身体冻僵的寒意。耐寒Lv2 耐暑Lv1" } },
                { "Battle_Armor_Grade_03_Iron_Heat",          new TechItemData { Name = "Heat Resistant Refined Metal Armor", NameCn = "耐热精炼金属盔甲",     Description = "经过改良的精炼金属盔甲。\n\n具备傲人的耐热性能。耐暑Lv2 耐寒Lv1" } },
                { "Battle_Armor_Grade_04_Steal",              new TechItemData { Name = "Pal Metal Armor",                    NameCn = "帕鲁金属盔甲",         Description = "以帕鲁金属锭制成的盔甲。\n\n有着惊人的防御力，但是十分沉重。耐寒Lv1 耐暑Lv1" } },
                { "Battle_Armor_Grade_04_Steal_Cold",         new TechItemData { Name = "Cold Resistant Pal Metal Armor",     NameCn = "耐寒帕鲁金属盔甲",     Description = "经过改良的帕鲁金属盔甲。\n\n能够抵御使身体冻僵的寒意。耐寒Lv2 耐暑Lv1" } },
                { "Battle_Armor_Grade_04_Steal_Heat",         new TechItemData { Name = "Heat Resistant Pal Metal Armor",     NameCn = "耐热帕鲁金属盔甲",     Description = "经过改良的帕鲁金属盔甲。\n\n具备傲人的耐热性能。耐暑Lv2 耐寒Lv1" } },
                { "Battle_Armor_Grade_05_Plastic",            new TechItemData { Name = "Plasteel Armor",                     NameCn = "塑钢盔甲",             Description = "以塑钢制成的盔甲。\n\n在增强防御力的同时，实现了轻量化。耐寒Lv1 耐暑Lv1" } },
                { "Battle_Armor_Grade_05_Plastic_Cold",       new TechItemData { Name = "Cold Resistant Plasteel Armor",      NameCn = "耐寒塑钢盔甲",         Description = "经过改良的塑钢盔甲。\n\n能够抵御使身体冻僵的寒意。耐寒Lv2 耐暑Lv1" } },
                { "Battle_Armor_Grade_05_Plastic_Heat",       new TechItemData { Name = "Heat Resistant Plasteel Armor",      NameCn = "耐热塑钢盔甲",         Description = "经过改良的塑钢盔甲。\n\n具备傲人的耐热性能。耐寒Lv1 耐暑Lv2" } },
                { "Battle_Armor_Grade_05_Plastic_Weight",     new TechItemData { Name = "Lightweight Plasteel Armor",         NameCn = "轻便塑钢盔甲",         Description = "经过改良的塑钢盔甲。\n\n能增加玩家的负重上限。耐寒Lv1 耐暑Lv1 提升负重上限Lv2" } },
                { "Battle_Cloth",                             new TechItemData { Name = "Cloth",                              NameCn = "布",                   Description = "编织羊毛制成的布。\n\n是制作防具时必不可少的。" } },
                { "Battle_Defense_BowGun",                    new TechItemData { Name = "Mounted Crossbow",                   NameCn = "固定式十字弓",         Description = "防御设施，请在此部署帕鲁，为战斗做好准备。\n\n使用时会消耗箭。\n\n把弹药放入箱子内，部署在此的帕鲁便会主动进行装填。" } },
                { "Battle_Defense_MachineGun",                new TechItemData { Name = "Mounted Machine Gun",                NameCn = "固定式机关枪",         Description = "防御设施，请在此部署帕鲁，为战斗做好准备。\n\n使用时会消耗步枪子弹。\n\n把弹药放入箱子内，部署在此的帕鲁便会主动进行装填。" } },
                { "Battle_Defense_Missile",                   new TechItemData { Name = "Mounted Missile Launcher",           NameCn = "固定式导弹发射器",     Description = "防御设施，请在此部署帕鲁，为战斗做好准备。\n\n使用时会消耗火箭弹。\n\n把弹药放入箱子内，部署在此的帕鲁便会主动进行装填。" } },
                { "Battle_Glider_Grade_01",                   new TechItemData { Name = "Normal Parachute",                   NameCn = "普通滑翔伞",           Description = "展开后即可在空中滑翔的滑翔伞。\n\n构造简单，无法高速飞行。" } },
                { "Battle_Glider_Grade_02",                   new TechItemData { Name = "Mega Glider",                        NameCn = "高级滑翔伞",           Description = "展开后即可在空中滑翔的滑翔伞。\n\n此为量产款式，有一定的飞行速度。" } },
                { "Battle_Glider_Grade_03",                   new TechItemData { Name = "Giga Glider",                        NameCn = "优质滑翔伞",           Description = "展开后即可在空中滑翔的滑翔伞。\n\n此款式性能有所提升，能以相当快的速度飞行。" } },
                { "Battle_Glider_Grade_04",                   new TechItemData { Name = "Hyper Glider",                       NameCn = "特级滑翔伞",           Description = "展开后可在空中滑翔的滑翔伞。\n\n性能得到进一步提升，速度和升力都提高了。" } },
                { "Battle_GunPowder_Grade_02",                new TechItemData { Name = "Gunpowder",                          NameCn = "火药",                 Description = "用于发射子弹的火药。\n\n是制作子弹时必不可少的。" } },
                { "Battle_Helm_Grade_02_Fur",                 new TechItemData { Name = "Feathered Hair Band",                NameCn = "羽毛发饰",             Description = "美丽的羽毛发饰。\n\n也许是心理作用，感觉可以防御头部的致命伤。" } },
                { "Battle_Helm_Grade_03_Copper",              new TechItemData { Name = "Metal Helm",                         NameCn = "金属头盔",             Description = "以金属制成的头盔。\n\n安全可靠，能守护你的大脑。" } },
                { "Battle_Helm_Grade_04_Iron",                new TechItemData { Name = "Refined Metal Helm",                 NameCn = "精炼金属头盔",         Description = "以优质金属制成的头盔。\n\n精心打磨后耀眼的金属光泽提高了它的价值。" } },
                { "Battle_Helm_Grade_05_Steal",               new TechItemData { Name = "Pal Metal Helm",                     NameCn = "帕鲁金属头盔",         Description = "以帕鲁金属锭制成的头盔。\n\n拥有人人都会羡慕的绝佳品质，是会选择其穿戴者的战士证明。" } },
                { "Battle_Helm_Grade_06_Plastic",             new TechItemData { Name = "Plasteel Helmet",                    NameCn = "塑钢头盔",             Description = "以塑钢制成的头盔。\n\n在增强防御力的同时，实现了轻量化。" } },
                { "Battle_MeleeWeapon_Axe_Steal",             new TechItemData { Name = "Pal Metal Axe",                      NameCn = "帕鲁金属斧",           Description = "砍树用的斧子。\n\n以帕鲁金属制造使其獲得惊人的锋利度。" } },
                { "Battle_MeleeWeapon_Bat",                   new TechItemData { Name = "Wooden Club",                        NameCn = "木棒",                 Description = "用于近距离战斗的木棒。\n\n也许能够用来打倒小型帕鲁。" } },
                { "Battle_MeleeWeapon_Bat2",                  new TechItemData { Name = "Bat",                                NameCn = "棒球棒",               Description = "这根棒球棒也能用于近距离战斗。\n\n但拿帕鲁当球可能有点太大了。" } },
                { "Battle_MeleeWeapon_Pickaxe_Steal",         new TechItemData { Name = "Pal Metal Pickaxe",                  NameCn = "帕鲁金属十字镐",       Description = "采矿用的十字镐。\n\n以帕鲁金属制造使其獲得惊人的效率。" } },
                { "Battle_RangeWeapon_AssaultRifle",          new TechItemData { Name = "Assault Rifle",                      NameCn = "突击步枪",             Description = "以压倒性的连射能力镇压敌人的突击步枪。\n\n火力优秀，是与强敌对战时的首选。" } },
                { "Battle_RangeWeapon_Bow1",                  new TechItemData { Name = "Old Bow",                            NameCn = "陈旧的弓",             Description = "原始的远距离攻击武器。\n\n因为是东拼西凑而成的，威力较弱。" } },
                { "Battle_RangeWeapon_Bow3",                  new TechItemData { Name = "Three Shot Bow",                     NameCn = "三连弓",               Description = "改良后的弓，可同时发射3支箭。\n\n拥有不可思议的力量，每次射击只消耗1支箭。" } },
                { "Battle_RangeWeapon_BowGun",                new TechItemData { Name = "Crossbow",                           NameCn = "十字弓",               Description = "不用使劲即可射出箭。\n\n虽然填装很费时，但可使出强劲一击。" } },
                { "Battle_RangeWeapon_BowGun_Fire",           new TechItemData { Name = "Fire Arrow Crossbow",                NameCn = "火箭十字弓",           Description = "可造成火属性伤害的十字弓。\n\n利用火箭让帕鲁着火，变得更容易捕捉。\n\n需要「火箭」才能使用。" } },
                { "Battle_RangeWeapon_BowGun_Poison",         new TechItemData { Name = "Poison Arrow Crossbow",              NameCn = "毒箭十字弓",           Description = "可让攻击对象中毒的可怕十字弓。\n\n帕鲁中毒以后，会变得更容易捕捉。\n\n需要「毒箭」才能使用。" } },
                { "Battle_RangeWeapon_Bow_Fire",              new TechItemData { Name = "Fire Bow",                           NameCn = "火之弓",               Description = "可造成火属性伤害的弓。\n\n利用火箭让帕鲁着火，变得更容易捕捉。\n\n需要「火箭」才能使用。" } },
                { "Battle_RangeWeapon_Bow_Poison",            new TechItemData { Name = "Poison Bow",                         NameCn = "毒之弓",               Description = "可让攻击对象中毒的可怕毒弓。\n\n帕鲁中毒以后，会变得更容易捕捉。\n\n需要「毒箭」才能使用。" } },
                { "Battle_RangeWeapon_CompoundBow",           new TechItemData { Name = "Compound Bow",                       NameCn = "复合弓",               Description = "经过现代化改良的弓。\n\n能够射出威力更强大的箭矢。" } },
                { "Battle_RangeWeapon_FlameThrower",          new TechItemData { Name = "Flamethrower",                       NameCn = "火焰喷射器",           Description = "能够向远处喷出火舌的火焰喷射器。\n\n可以使攻击目标陷入点燃状态。" } },
                { "Battle_RangeWeapon_GatlingGun",            new TechItemData { Name = "Gatling Gun",                        NameCn = "加特林机枪",           Description = "可通过高速扫射实现压制战场的加特林机枪。\n\n压制射击能够将敌人射成蜂窝。" } },
                { "Battle_RangeWeapon_GrenadeLauncher",       new TechItemData { Name = "Grenade Launcher",                   NameCn = "榴弹发射器",           Description = "能够发射造成大范围爆炸的榴弹。\n\n对付大群敌人时很有用。" } },
                { "Battle_RangeWeapon_GuidedMissileLauncher", new TechItemData { Name = "Guided Missile Launcher",            NameCn = "追踪导弹发射器",       Description = "能够发射自动追踪敌人的导弹。\n\n导弹在命中后将爆炸，造成大范围伤害。" } },
                { "Battle_RangeWeapon_HandGun",               new TechItemData { Name = "Handgun",                            NameCn = "手枪",                 Description = "子弹容纳量及连射性能经过强化的手枪。\n\n每秒火力比劣质手枪更强。" } },
                { "Battle_RangeWeapon_HomingSphereLauncher",  new TechItemData { Name = "Homing Sphere Launcher",             NameCn = "自动追踪帕鲁球发射器", Description = "用于发射帕鲁球的发射器。\n\n用它射出的帕鲁球会追踪帕鲁。" } },
                { "Battle_RangeWeapon_LaserRifle",            new TechItemData { Name = "Laser Rifle",                        NameCn = "激光步枪",             Description = "能够发射激光的武器。\n\n威力高又易于使用。" } },
                { "Battle_RangeWeapon_OldRevolver",           new TechItemData { Name = "Old Revolver",                       NameCn = "古旧左轮手枪",         Description = "略显陈旧的左轮手枪。\n\n单发威力优于手枪。" } },
                { "Battle_RangeWeapon_Rifle",                 new TechItemData { Name = "Single-Shot Rifle",                  NameCn = "单发步枪",             Description = "单发型来福枪，得花费时间装填子弹。\n\n填装数量少，但单发威力强劲。" } },
                { "Battle_RangeWeapon_RocketLauncher",        new TechItemData { Name = "Rocket Launcher",                    NameCn = "火箭发射器",           Description = "可远距离进行强劲轰炸的火箭发射器。" } },
                { "Battle_RangeWeapon_SemiAutoRifle",         new TechItemData { Name = "Semi-Auto Rifle",                    NameCn = "半自动步枪",           Description = "具备一定连射能力的步枪。\n\n易于瞄准且火力强大，适合中距离战斗。" } },
                { "Battle_RangeWeapon_SemiAutoShotgun",       new TechItemData { Name = "Semi-Auto Shotgun",                  NameCn = "半自动霰弹枪",         Description = "连射能力优异的霰弹枪。\n\n拥有首屈一指的近距离DPS能力。" } },
                { "Battle_RangeWeapon_ShotGun",               new TechItemData { Name = "Double-Barreled Shotgun",            NameCn = "双管霰弹枪",           Description = "近距离战斗能力强劲的霰弹枪，可以连射2发。\n\n弹容量虽少，但火力很强。" } },
                { "Battle_RangeWeapon_ShotGun_Multi",         new TechItemData { Name = "Pump-Action Shotgun",                NameCn = "泵动式霰弹枪",         Description = "增加填装数量，更加强劲的霰弹枪。 \n\n拥有屈指可数的近距离DPS能力。" } },
                { "Battle_RangeWeapon_SphereLauncher",        new TechItemData { Name = "Scatter Sphere Launcher",            NameCn = "扩散型帕鲁球发射器",   Description = "可同时发射大量帕鲁球的新型发射器。\n\n可用来捕捉成群的帕鲁。" } },
                { "Battle_RangeWeapon_SphereLauncher_Once",   new TechItemData { Name = "Single-Shot Sphere Launcher",        NameCn = "单发型帕鲁球发射器",   Description = "用于发射帕鲁球的发射器。\n\n用它就能捕捉到更远处的帕鲁。" } },
                { "Battle_RangeWeapon_SubmachineGun",         new TechItemData { Name = "SMG",                                NameCn = "冲锋枪",               Description = "连射能力优异的冲锋枪。\n\n轻巧灵活，适合近距离战斗。" } },
                { "BeamSword",                                new TechItemData { Name = "Beam Sword",                         NameCn = "光剑",                 Description = "近战用的光剑。\n\n高能激光可瞬间融断敌人。" } },
                { "BlastFurnace4",                            new TechItemData { Name = "Gigantic Furnace",                   NameCn = "巨大熔炉",             Description = "可用来冶炼六棱晶锭。\n\n扩大了建筑面积让复数帕鲁可以同时进行冶炼。\n\n需要火系帕鲁点火。" } },
                { "BreedFarm",                                new TechItemData { Name = "Breeding Farm",                      NameCn = "配种牧场",             Description = "分派♂♀帕鲁各一只，\n\n即可让它们产下帕鲁蛋。\n\n需在设施中放入蛋糕才会顺利生蛋。" } },
                { "BuildableGoddessStatue",                   new TechItemData { Name = "Statue of Power",                    NameCn = "力量石像",             Description = "帕洛斯群岛的传说中提到的石像。\n\n将翠叶鼠雕像献给石像就可以\n\n获得不可思议的力量。" } },
                { "CarbonFiber",                              new TechItemData { Name = "Carbon Fiber",                       NameCn = "碳纤维",               Description = "轻盈坚硬的优质素材。\n\n可用于制作防具等。" } },
                { "Cauldron",                                 new TechItemData { Name = "Witch Cauldron",                     NameCn = "魔女之釜",             Description = "如配置在据点，\n\n可以提升「制药」的工作速度。\n\n即使配置复数个，其效果也不会叠加。" } },
                { "CeilingLamp",                              new TechItemData { Name = "Ceiling Lamp",                       NameCn = "天花板吊灯",           Description = "可在黑夜照亮据点的光源。\n\n可设置在天花板上。\n\n需要电力，但可大范围照明。" } },
                { "Cement",                                   new TechItemData { Name = "Cement",                             NameCn = "水泥",                 Description = "可成为建筑物素材的水泥。\n\n可在优质作业台制作。" } },
                { "ChargeLaserRifle",                         new TechItemData { Name = "Charge Rifle",                       NameCn = "充能步枪",             Description = "采用高端技术研制的充能型光束步枪。\n\n蓄能后可发射威力巨大的一击。" } },
                { "ChargeLaserRifleBullet",                   new TechItemData { Name = "Charge Rifle Ammo",                  NameCn = "充能步枪子弹",         Description = "用于充能步枪等武器的弹药。" } },
                { "Cloth2",                                   new TechItemData { Name = "High Quality Cloth",                 NameCn = "优质的布",             Description = "编织大量羊毛制成的优质的布。\n\n是制作优质防具时必不可少的。" } },
                { "CompositeDesk",                            new TechItemData { Name = "Drafting Table",                     NameCn = "制图桌",               Description = "通过组合复数设计图，\n\n可以制成更高等级设计图的设施。" } },
                { "Cooler",                                   new TechItemData { Name = "Cooler",                             NameCn = "降温器",               Description = "输送冷风，降低据点温度的装置。\n\n虽然不是很凉爽，但可抵御少许炎热。\n\n需要冰系帕鲁冷却。" } },
                { "CoolerBox",                                new TechItemData { Name = "Cooler Box",                         NameCn = "保冷箱",               Description = "小型食物储存库。\n\n将冰系帕鲁指派到这里工作时，\n\n可以使里面的食物不会轻易腐败。" } },
                { "CoolerPalFoodBox",                         new TechItemData { Name = "Cold Food Box",                      NameCn = "低温保鲜饲料箱",       Description = "可以冷冻的饲料箱。\n\n将冰系帕鲁指派到这里工作时，\n\n可以使里面的食物不会轻易腐败。" } },
                { "Crusher",                                  new TechItemData { Name = "Crusher",                            NameCn = "粉碎机",               Description = "可以将石头或木材粉碎之后\n\n转换成其他素材的设施。\n\n需要有具备「浇水」适应性的帕鲁来使水车转动。" } },
                { "CrystalPit",                               new TechItemData { Name = "Hexolite Quartz Mine",               NameCn = "六棱晶矿采矿场",       Description = "生产六棱晶矿的设施。\n\n挖掘六棱晶矿的工作需要大量体力，相当严苛。\n\n把这些工作交给擅长挖掘的帕鲁吧。" } },
                { "DamagedScarecrow",                         new TechItemData { Name = "Training Dummy",                     NameCn = "受损的稻草人",         Description = "模仿企丸丸制作的训练用稻草人。\n\n只能放置在较宽敞的地方。" } },
                { "DefenseWait",                              new TechItemData { Name = "Sandbag",                            NameCn = "沙袋",                 Description = "保护据点不受敌人攻击的防御设施。\n\n部署于此的帕鲁不会执行其他工作，\n\n并总是保持警惕，随时准备好应对可能会发生的战斗。" } },
                { "DefenseWall",                              new TechItemData { Name = "Defensive Wall",                     NameCn = "防御墙",               Description = "防止外敌入侵的巨大防御墙。\n\n以石头制成，还算坚固。" } },
                { "DefenseWall_Metal",                        new TechItemData { Name = "Metal Defensive Wall",               NameCn = "金属防御墙",           Description = "防止外敌入侵的巨大防御墙。\n\n以金属制成，非常坚固。" } },
                { "DefenseWall_Wood",                         new TechItemData { Name = "Wooden Defensive Wall",              NameCn = "木制防御墙",           Description = "防止外敌入侵的巨大防御墙。\n\n以木头制成，相当脆弱。" } },
                { "DimensionPalStorage",                      new TechItemData { Name = "Dimensional Pal Storage",            NameCn = "帕鲁次元仓库",         Description = "可以大量存放捕获帕鲁的设施。\n\n只要是公会成员，谁都可以进行帕鲁的存取。\n\n改为私密锁定时，则只能存放自己的帕鲁。" } },
                { "DismantlingConveyor",                      new TechItemData { Name = "Pal Disassembly Conveyor",           NameCn = "帕鲁解体传送带",       Description = "能够将放入的帕鲁自动解体的，梦幻般的设备。" } },
                { "DisplayCharacter",                         new TechItemData { Name = "Viewing Cage",                       NameCn = "观赏笼",               Description = "放置抓来的帕鲁的观赏用笼子。\n\n在里头的帕鲁无法战斗或进行作业，\n\n但也不会肚子饿。" } },
                { "ElecBaton",                                new TechItemData { Name = "Stun Baton",                         NameCn = "电棍",                 Description = "敲下去就会触电的近战武器。\n\n帕鲁在触电状态下比较容易捕获。" } },
                { "ElectricCooler",                           new TechItemData { Name = "Electric Cooler",                    NameCn = "电气降温器",           Description = "输送冷风，降低据点温度的装置。\n\n使用电力，可大幅降低温度。\n\n需要冰系帕鲁冷却。" } },
                { "ElectricGenerator_Large",                  new TechItemData { Name = "Large Power Generator",              NameCn = "大型发电机",           Description = "储存雷系帕鲁生产的电的设施。\n\n更加大型，发电效率也更高。" } },
                { "ElectricHeater",                           new TechItemData { Name = "Electric Heater",                    NameCn = "电气取暖器",           Description = "输送暖风，提升据点温度的装置。\n\n使用电力，可大幅提升温度。\n\n需要火系帕鲁点火。" } },
                { "EnergyLauncherBullet",                     new TechItemData { Name = "Plasma Cartridge",                   NameCn = "电浆能量盒",           Description = "用于等离子炮等武器的弹药。" } },
                { "EnergyRocketLauncher",                     new TechItemData { Name = "Plasma Cannon",                      NameCn = "等离子炮",             Description = "能够发射能量弹。\n\n着弹后会引发能量爆炸。" } },
                { "EnergyShotgun",                            new TechItemData { Name = "Energy Shotgun",                     NameCn = "能量霰弹枪",           Description = "可以发射能量弹的霰弹枪。\n\n拥有优秀的连射性能，近距离火力异常强劲。" } },
                { "EnergyShotgunBullet",                      new TechItemData { Name = "Energy Shotgun Ammo",                NameCn = "能量霰弹枪子弹",       Description = "用于能量霰弹枪等武器的弹药。" } },
                { "EnergyStorage_Electric",                   new TechItemData { Name = "Accumulator",                        NameCn = "蓄电器",               Description = "可以储存发电机发电的多余电力的设施。" } },
                { "Expedition",                               new TechItemData { Name = "Pal Expedition Station",             NameCn = "帕鲁远征所",           Description = "可以派遣帕鲁到地下城等地方远征的设施。\n\n被派遣的帕鲁会在各地寻找物品或进行战斗，\n\n并将资源带回据点。" } },
                { "Factory_Hard_04",                          new TechItemData { Name = "Advanced Workshop",                  NameCn = "高等文明作业工厂",     Description = "用于制作物品及护甲的工厂。\n\n引入了高性能机器，可以快速制作。\n\n需有能进行手工作业的帕鲁。" } },
                { "Factory_Money",                            new TechItemData { Name = "Gold Coin Assembly Line",            NameCn = "金币制造工厂",         Description = "生产金币的工厂。\n\n因为精神负担大，工作时SAN值会降低。\n\n需有能进行手工作业的帕鲁。" } },
                { "Farm_SkillFruits",                         new TechItemData { Name = "Skillfruit Orchard",                 NameCn = "技能果树园",           Description = "可以种植技能果实的农园。\n\n将果实作为原种植入地面，\n\n就可以收获多个相同种类的果实。" } },
                { "FishingBait_2",                            new TechItemData { Name = "High Quality Bait",                  NameCn = "优质钓饵",             Description = "用来吸引帕鲁上钩的钓饵。\n\n经过改良，更容易诱导帕鲁咬钩。" } },
                { "FishingBait_3",                            new TechItemData { Name = "Deluxe Bait",                        NameCn = "奢华钓饵",             Description = "用来吸引帕鲁上钩的钓饵。\n\n经过改良，能使小游戏的判定条稍微变大。" } },
                { "FishingBait_3_A",                          new TechItemData { Name = "Alluring Bait",                      NameCn = "魅惑钓饵",             Description = "用来吸引帕鲁上钩的钓饵。\n\n经过改良，能使小游戏的判定条变大，\n\n垂钓时能获得更多道具。" } },
                { "FishingPond1",                             new TechItemData { Name = "Fishing Pond",                       NameCn = "垂钓池",               Description = "配备垂钓工具的垂钓池。\n\n分派帕鲁后，它会悠闲地帮你垂钓。\n\n需有可手工作业的帕鲁。" } },
                { "FishingPond2",                             new TechItemData { Name = "Large Fishing Pond",                 NameCn = "大型垂钓池",           Description = "配备垂钓工具的大型垂钓池。\n\n水域更加广阔，能钓上更大型的帕鲁。\n\n需有可手工作业的帕鲁。" } },
                { "FishingRod_02_1",                          new TechItemData { Name = "Intermediate Fishing Rod (Cattiva)", NameCn = "中级钓竿（捣蛋猫）",   Description = "用来钓帕鲁的钓竿。\n\n在携带钓饵的状态下前往有鱼影的水边使用钓竿即可开始垂钓。\n\n性能进一步提升，垂钓成功率稍微上升。" } },
                { "FishingRod_03_1",                          new TechItemData { Name = "Advanced Fishing Rod (Pengullet)",   NameCn = "高级钓竿（企丸丸）",   Description = "用来钓帕鲁的钓竿。\n\n在携带钓饵的状态下前往有鱼影的水边使用钓竿即可开始垂钓。\n\n性能进一步提升，垂钓成功率明显上升。" } },
                { "FlamethrowerBullet",                       new TechItemData { Name = "Flamethrower Fuel",                  NameCn = "火焰喷射器燃料",       Description = "用于火焰喷射器等武器的燃料。" } },
                { "FlourMill",                                new TechItemData { Name = "Mill",                               NameCn = "磨粉机",               Description = "可以将小麦磨碎之后\n\n生产出的面粉设施。\n\n需要有具备「浇水」适应性的帕鲁来使水车转动。" } },
                { "FlowerBed",                                new TechItemData { Name = "Flower Bed",                         NameCn = "花坛",                 Description = "如配置在据点，\n\n可以提升「采集」的工作速度。\n\n即使配置复数个，其效果也不会叠加。" } },
                { "Fountain",                                 new TechItemData { Name = "Water Fountain",                     NameCn = "喷泉",                 Description = "如配置在据点，\n\n可以提升「浇水」的工作速度。\n\n即使配置复数个，其效果也不会叠加。" } },
                { "FragGrenade",                              new TechItemData { Name = "Frag Grenade",                       NameCn = "破片手榴弹",           Description = "可在中距离战斗时发挥作用的破片手榴弹。\n\n投掷数秒后引爆，给敌人造成伤害。" } },
                { "FragGrenade_Dark",                         new TechItemData { Name = "Dark Grenade",                       NameCn = "暗黑手榴弹",           Description = "利于中等距离战斗的暗黑手榴弹。\n\n投掷数秒后引爆，给敌人造成暗属性伤害。" } },
                { "FragGrenade_Dragon",                       new TechItemData { Name = "Dragon Grenade",                     NameCn = "龙击手榴弹",           Description = "利于中等距离战斗的龙击手榴弹。\n\n投掷数秒后引爆，给敌人造成龙属性伤害。" } },
                { "FragGrenade_Elec",                         new TechItemData { Name = "Shock Grenade",                      NameCn = "电击手榴弹",           Description = "利于中等距离战斗的电击手榴弹。\n\n投掷数秒后引爆，对附近放出电击。" } },
                { "FragGrenade_Fire",                         new TechItemData { Name = "Incendiary Grenade",                 NameCn = "烧夷手榴弹",           Description = "利于中等距离战斗的烧夷手榴弹。\n\n投掷数秒后引爆，在周围燃起烈火。" } },
                { "FragGrenade_Ground",                       new TechItemData { Name = "Ground Grenade",                     NameCn = "大地手榴弹",           Description = "利于中等距离战斗的大地手榴弹。\n\n投掷数秒后引爆，给敌人造成地属性伤害。" } },
                { "FragGrenade_Ice",                          new TechItemData { Name = "Ice Grenade",                        NameCn = "冷冻手榴弹",           Description = "利于中等距离战斗的冷冻手榴弹。\n\n投掷数秒后引爆，冻结周围一切。" } },
                { "FragGrenade_Leaf",                         new TechItemData { Name = "Grass Grenade",                      NameCn = "草木手榴弹",           Description = "利于中等距离战斗的草木手榴弹。\n\n投掷数秒后引爆，给敌人造成草属性伤害。" } },
                { "FragGrenade_Super",                        new TechItemData { Name = "Frag Grenade Mk2",                   NameCn = "破片手榴弹Mk2",        Description = "改良后的破片手榴弹。\n\n投掷数秒后引爆，给大范围的敌人造成伤害。" } },
                { "FragGrenade_Water",                        new TechItemData { Name = "Water Grenade",                      NameCn = "水流手榴弹",           Description = "利于中等距离战斗的水流手榴弹。\n\n投掷数秒后引爆，给敌人造成水属性伤害。" } },
                { "GatlingBullet",                            new TechItemData { Name = "Gatling Gun Ammo",                   NameCn = "加特林子弹",           Description = "用于加特林机枪等武器的弹药。" } },
                { "GlobalPalStorage",                         new TechItemData { Name = "Global Palbox",                      NameCn = "跨界帕鲁终端",         Description = "具有保存帕鲁的基因序列，\n\n以及通过基因序列复原帕鲁的功能的设施。\n\n保存的基因序列能够带到其他世界使用。" } },
                { "GrapplingGun",                             new TechItemData { Name = "Grappling Gun",                      NameCn = "爪钩枪",               Description = "用于快速移动的爪钩枪。\n\n能将爪钩射向目标地点，再将使用者牵引过去。\n\n可在有高低差的地方轻松移动。" } },
                { "GrapplingGun2",                            new TechItemData { Name = "Mega Grappling Gun",                 NameCn = "高级爪钩枪",           Description = "用于快速移动的爪钩枪。\n\n能将爪钩射向目标地点，再将使用者牵引过去。\n\n经过改良，射程变得更远。" } },
                { "GrapplingGun3",                            new TechItemData { Name = "Giga Grappling Gun",                 NameCn = "优质爪钩枪",           Description = "用于快速移动的爪钩枪。\n\n能将爪钩射向目标地点，再将使用者牵引过去。\n\n在试验品数据的基础上进行改良，进一步提升了性能。" } },
                { "GrapplingGun4",                            new TechItemData { Name = "Hyper Grappling Gun",                NameCn = "特级爪钩枪",           Description = "用于快速移动的爪钩枪。\n\n能将爪钩射向目标地点，再将使用者牵引过去。\n\n经过研究改良，灵活性可谓无与伦比。" } },
                { "GrapplingGun5",                            new TechItemData { Name = "Ultra Grappling Gun",                NameCn = "超级爪钩枪",           Description = "用于快速移动的爪钩枪。\n\n能将爪钩射向目标地点，再将使用者牵引过去。\n\n采用了新型材料，大幅缩短了散热时间。" } },
                { "GrenadeBullet",                            new TechItemData { Name = "Grenade Ammo",                       NameCn = "榴弹",                 Description = "用于榴弹发射器等武器的弹药。" } },
                { "GuildChest",                               new TechItemData { Name = "Guild Chest",                        NameCn = "公会箱子",             Description = "可以瞬间透过亚空间传送箱子内容的箱子。\n\n这个箱子内的道具会在各据点之间共享。" } },
                { "HandTorch",                                new TechItemData { Name = "Mounted Torch",                      NameCn = "固定式火把",           Description = "可在黑夜照亮据点的光源。\n\n需要火系帕鲁点火。" } },
                { "HandgunBullet",                            new TechItemData { Name = "Handgun Ammo",                       NameCn = "手枪子弹",             Description = "用于手枪等武器的子弹。" } },
                { "Headstone",                                new TechItemData { Name = "Tombstone",                          NameCn = "墓碑",                 Description = "可以书写文字的墓碑。\n\n用作留下记录。" } },
                { "Heater",                                   new TechItemData { Name = "Heater",                             NameCn = "取暖器",               Description = "输送暖风，提升据点温度的装置。\n\n虽然不是很暖，但可抵御少许寒冷。\n\n需要火系帕鲁点火。" } },
                { "Homeward",                                 new TechItemData { Name = "Homeward Thundercloud",              NameCn = "归途雷云",             Description = "使用后可以移动至最近的据点。\n\n无法在地下城里使用。" } },
                { "HugeKitchen",                              new TechItemData { Name = "Large-Scale Stone Oven",             NameCn = "大型厨房",             Description = "烹制食材时的必要设施。\n\n扩大了占地面积让复数帕鲁可以同时进行烹调。\n\n需要火系帕鲁点火。" } },
                { "IceCrusher",                               new TechItemData { Name = "Refrigerated Crusher",               NameCn = "冷冻粉碎机",           Description = "可粉碎金属矿石以转换成其他材料的设施。\n\n虽然要使其工作需要电力供应，以及有「冷却」适应性的帕鲁，但是效率极佳。" } },
                { "Infra_ElectricGenerator_Grade_01",         new TechItemData { Name = "Power Generator",                    NameCn = "发电机",               Description = "储存雷系帕鲁生产的电的设施。\n\n没有它的话，电力设施无法运作。" } },
                { "Infra_ElectronicCircuit",                  new TechItemData { Name = "Circuit Board",                      NameCn = "电路板",               Description = "制作精密机器时必不可少的电路板。\n\n可在作业流水线工厂制作。" } },
                { "Infra_ItemChest_Grade_01",                 new TechItemData { Name = "Wooden Chest",                       NameCn = "木箱",                 Description = "可用于收纳道具。\n\n以木头制成，相当脆弱。\n\n拿来存放贵重物品不太让人放心。" } },
                { "Infra_ItemChest_Grade_02",                 new TechItemData { Name = "Metal Chest",                        NameCn = "金属箱",               Description = "可收纳物品。\n\n用金属加强后有一定强度。\n\n体积变大后，可收纳的容量也增加了。" } },
                { "Infra_ItemChest_Grade_03",                 new TechItemData { Name = "Refined Metal Chest",                NameCn = "精炼金属箱",           Description = "可收纳物品。\n\n因为是精炼金属制的，非常坚固。\n\n体积巨大，可作为安全的仓库使用。" } },
                { "Infra_MachineParts",                       new TechItemData { Name = "Nail",                               NameCn = "钉子",                 Description = "建造大量设施时必不可少的部件。\n\n可在原始的作业台制作。" } },
                { "Infra_PalBed_Grade_01",                    new TechItemData { Name = "Straw Pal Bed",                      NameCn = "稻草帕鲁床",           Description = "帕鲁用的稻草床。\n\n可供帕鲁在受伤时或夜晚睡觉。\n\n虽然很硬，但总比没有强。" } },
                { "Infra_PalBed_Grade_02",                    new TechItemData { Name = "Fluffy Pal Bed",                     NameCn = "松软帕鲁床",           Description = "帕鲁用的软床，可睡一个好觉。\n\n可供帕鲁在受伤时或夜晚睡觉。\n\n松松软软的，帕鲁也一定很开心。" } },
                { "Infra_PlayerBed_Grade_01",                 new TechItemData { Name = "Shoddy Bed",                         NameCn = "劣质的床",             Description = "人类用的破床。\n\n可供人类在受伤时或夜晚睡觉。\n\n只有在屋顶下才能安心入睡。" } },
                { "Infra_PlayerBed_Grade_02",                 new TechItemData { Name = "Fine Bed",                           NameCn = "优质的床",             Description = "人类用的软床，可睡一个好觉。\n\n可供人类在受伤时或夜晚睡觉。\n\n只有在屋顶下才能安心入睡。" } },
                { "ItemBooth",                                new TechItemData { Name = "Flea Market (Items)",                NameCn = "跳蚤市场（道具）",     Description = "出售物品的设施。\n\n可以将物品出售给其他玩家。\n\n将冰系帕鲁指派到这里工作时，\n\n可以使里面的食物不会轻易腐败。" } },
                { "ItemChest_04",                             new TechItemData { Name = "Advanced Chest",                     NameCn = "高等文明箱子",         Description = "可用于收纳道具。\n\n非常坚固，存放空间也很大。\n\n可以当作巨大且安全的仓库使用。" } },
                { "Katana",                                   new TechItemData { Name = "Katana",                             NameCn = "太刀",                 Description = "近战用的太刀。\n\n刀锋凌厉，斩断一切。" } },
                { "Lab",                                      new TechItemData { Name = "Pal Labor Research Laboratory",      NameCn = "帕鲁工作研究所",       Description = "对帕鲁的各种工作进行研究的设施。\n\n在这个设施中让帕鲁工作，\n\n可以进行研究，获得各种效果。" } },
                { "Lamp",                                     new TechItemData { Name = "Lamp",                               NameCn = "固定式电灯",           Description = "可在黑夜照亮据点的光源。\n\n需要电力，但可大范围照明。" } },
                { "Lantern",                                  new TechItemData { Name = "Hip Lantern",                        NameCn = "腰挂提灯",             Description = "挂在腰上的提灯。 \n\n到了夜晚会自动点亮。 \n\n也可以切换照明模式使它常亮。\n\n带1个在身上就够用了。" } },
                { "Lantern_High",                             new TechItemData { Name = "Enhanced Hip Lantern",               NameCn = "强化腰挂提灯",         Description = "挂在腰间的大范围照明提灯。\n\n到了夜晚会自动点亮。 \n\n也可以切换照明模式使它常亮。\n\n带1个在身上就够用了。" } },
                { "LargeCeilingLamp",                         new TechItemData { Name = "Large Ceiling Lamp",                 NameCn = "大型天花板吊灯",       Description = "可在黑夜照亮据点的光源。\n\n可设置在天花板上。\n\n需要更多的电力，但可更大范围照明。" } },
                { "LargeLamp",                                new TechItemData { Name = "Large Mounted Lamp",                 NameCn = "大型固定式灯具",       Description = "可在黑夜照亮据点的光源。\n\n需要更多的电力，但可更大范围照明。" } },
                { "LaserBullet",                              new TechItemData { Name = "Energy Cartridge",                   NameCn = "能量盒",               Description = "用于激光步枪等武器的弹药。" } },
                { "LaserGatlingBullet",                       new TechItemData { Name = "Laser Gatling Cartridge",            NameCn = "加特林激光能量盒",     Description = "用于激光加特林等武器的弹药。" } },
                { "LaserGatlingGun",                          new TechItemData { Name = "Laser Gatling Gun",                  NameCn = "激光加特林",           Description = "能够高速连射激光束。\n\n猛烈的火力能瞬间压制复数敌人。" } },
                { "LauncherBullet",                           new TechItemData { Name = "Rocket Ammo",                        NameCn = "火箭弹",               Description = "用于火箭发射器等武器的弹药。" } },
                { "Launcher_Meteor",                          new TechItemData { Name = "Meteor Launcher",                    NameCn = "陨石发射器",           Description = "加工陨石制作而成的发射器。\n\n命中时会像陨石一样引发爆炸。" } },
                { "MakeshiftAssaultRifle",                    new TechItemData { Name = "Makeshift Assault Rifle",            NameCn = "破旧的突击步枪",       Description = "用废料手工制作的突击步枪。\n\n连射能力优秀且射程远，但单发威力较低。" } },
                { "MakeshiftHandgun",                         new TechItemData { Name = "Makeshift Handgun",                  NameCn = "劣质手枪",             Description = "手工制造的破旧手枪。\n\n较适合用于近距离战斗，但每次只能射出一发子弹。" } },
                { "MakeshiftShotgun",                         new TechItemData { Name = "Makeshift Shotgun",                  NameCn = "破旧的霰弹枪",         Description = "用废料手工制作的霰弹枪。\n\n射程短，但近距离战斗能力强。" } },
                { "MakeshiftSubmachineGun",                   new TechItemData { Name = "Makeshift SMG",                      NameCn = "破旧的冲锋枪",         Description = "用废料手工制作的冲锋枪。\n\n连射能力佳，但单发威力较低。" } },
                { "ManganeseIngot",                           new TechItemData { Name = "Coralum Ingot",                      NameCn = "珊瑚锭",               Description = "由珊瑚矿石和金属矿石、\n\n石炭加工制成的合金。\n\n具备很高的能量耐受性，用于制作武器等物品的材料。" } },
                { "ManualElectricGenerator",                  new TechItemData { Name = "Human-Powered Generator",            NameCn = "人力发电机",           Description = "利用旋转动能转换为电能的设施。\n\n效率不佳，而且工作的帕鲁会降低SAN值，\n\n但可以获得经验值。" } },
                { "MeatCutterKnife",                          new TechItemData { Name = "Meat Cleaver",                       NameCn = "切肉刀",               Description = "可用于肢解召唤出的帕鲁的菜刀。\n\n装备此道具时「抚摸」选项会转变为「肢解」。\n\n帕鲁一经解体后将无法再度复生。" } },
                { "MedicalPalBed_04",                         new TechItemData { Name = "Large Pal Bed",                      NameCn = "大型帕鲁床",           Description = "帕鲁用的大床，可睡一个好觉。\n\n可供帕鲁在受伤时或夜晚睡觉。\n\n即使是大型帕鲁也能在这张大床上睡个好觉。" } },
                { "MedicalPalBed_05",                         new TechItemData { Name = "Pal Pod",                            NameCn = "高等文明帕鲁胶囊",     Description = "帕鲁用的回复胶囊。\n\n帕鲁在晚上或受伤时可以睡在这里。\n\n因为技术革新，恢复量提高了。" } },
                { "MedicineFacility_03",                      new TechItemData { Name = "Advanced Medicine Workbench",        NameCn = "高等文明制药台",       Description = "用于制作帕鲁治疗药的设备。\n\n透过未来技术，可制作未知的药物。\n\n交给可制药的帕鲁来操作吧。" } },
                { "MetalDetector",                            new TechItemData { Name = "Metal Detector",                     NameCn = "金属探测器",           Description = "拿在手上即可探测附近隐藏的矿石。\n\n靠近矿石时，即会标记金属位置。\n\n一旦远离标记就会消失。" } },
                { "Metal_Gate",                               new TechItemData { Name = "Iron Gate",                          NameCn = "铁制大门",             Description = "稍大体型的帕鲁也能通过的大门。\n\n以金属制成，非常坚固。" } },
                { "MiningTool",                               new TechItemData { Name = "Mining Cart",                        NameCn = "十字镐和安全帽",       Description = "如配置在据点，\n\n可以提升「挖掘」的工作速度。\n\n即使配置复数个，其效果也不会叠加。" } },
                { "MissileBullet",                            new TechItemData { Name = "Missile Ammo",                       NameCn = "导弹",                 Description = "用于追踪导弹发射器等武器的弹药。" } },
                { "MonsterFarm",                              new TechItemData { Name = "Ranch",                              NameCn = "家畜牧场",             Description = "可以放牧羊或鸡等类型帕鲁的牧场。\n\n如将具备特殊伙伴技能的帕鲁\n\n分派到牧场，可以自动生产道具。" } },
                { "MultiElectricHatchingPalEgg",              new TechItemData { Name = "Large-Scale Electric Egg Incubator", NameCn = "大型电能帕鲁蛋孵化器", Description = "用于孵化帕鲁蛋的装置。\n\n需要电力供应才能运作，\n\n但可自动保持内部在最佳温度，并同时孵化多个蛋。" } },
                { "MultiHatchingPalEgg",                      new TechItemData { Name = "Large Incubator",                    NameCn = "大型帕鲁蛋孵化器",     Description = "用于孵化帕鲁蛋的装置。\n\n可自动维持大致适宜的温度，\n\n并同时孵化多个蛋。" } },
                { "Musket",                                   new TechItemData { Name = "Musket",                             NameCn = "鸟枪",                 Description = "古旧又简朴的枪。\n\n每一发均威力惊人，但装填子弹较花时间。" } },
                { "OilPump",                                  new TechItemData { Name = "Crude Oil Extractor",                NameCn = "原油提炼机",           Description = "从油田提取原油所需的设备。\n\n它需要大量电能供给才能运行。" } },
                { "OlympicCauldron",                          new TechItemData { Name = "Flame Cauldron",                     NameCn = "圣火台",               Description = "如配置在据点，\n\n可以提升「生火」的工作速度。\n\n即使配置复数个，其效果也不会叠加。" } },
                { "OperatingTable",                           new TechItemData { Name = "Pal Surgery Table",                  NameCn = "帕鲁手术台",           Description = "可以对帕鲁进行手术，\n\n改变帕鲁的性别和被动技能的设施。" } },
                { "OverheatRifle",                            new TechItemData { Name = "Overheat Rifle",                     NameCn = "过热步枪",             Description = "无需重复装弹即可实现连续射击的突击步枪。\n\n但长时间连射容易引发过热，\n\n导致出现巨大破绽，请务必注意。" } },
                { "OverheatRifleBullet",                      new TechItemData { Name = "Overheat Rifle Ammo",                NameCn = "过热步枪子弹",         Description = "用于过热步枪等武器的弹药。" } },
                { "PALBOX",                                   new TechItemData { Name = "Palbox",                             NameCn = "帕鲁终端",             Description = "用来存放抓到的帕鲁的设施。\n\n箱子里的帕鲁会逐渐回复生命值。\n\n此设施周围的区域即为据点。" } },
                { "PalBooth",                                 new TechItemData { Name = "Flea Market (Pals)",                 NameCn = "跳蚤市场（帕鲁）",     Description = "出售帕鲁的设施。\n\n可以将帕鲁出售给其他玩家。\n\n将冰系帕鲁指派到这里工作时，\n\n可以使里面的食物不会轻易腐败。" } },
                { "PalDopingShot",                            new TechItemData { Name = "Boost Gun",                          NameCn = "强化枪",               Description = "向伙伴帕鲁射击，\n\n会在一定时间内强化该帕鲁的攻击力和防御力。\n\n向敌人射击则会造成伤害。" } },
                { "PalDopingShotBullet",                      new TechItemData { Name = "Boost Gun Ammo",                     NameCn = "强化枪子弹",           Description = "用于强化枪等武器的弹药。" } },
                { "PalDopingShot_2",                          new TechItemData { Name = "Megaboost Gun",                      NameCn = "高级强化枪",           Description = "向伙伴帕鲁射击可强化该帕鲁的攻击力与防御力，\n\n向敌人射击则会造成伤害。经过改良后，\n\n强化效果、持续时间及对敌伤害均有所提升。" } },
                { "PalFoodBox",                               new TechItemData { Name = "Feed Box",                           NameCn = "饲料箱",               Description = "用于存放帕鲁饲料的箱子。\n\n肚子饿了的帕鲁会来这裡吃饲料。\n\n记得随时补充饲料，别让箱子空了。" } },
                { "PalHealingGrenade",                        new TechItemData { Name = "Pal Recovery Grenade",               NameCn = "帕鲁恢复手榴弹",       Description = "具有恢复效果的破片手榴弹。\n\n命中后会立即爆裂，\n\n为伙伴帕鲁恢复生命值。" } },
                { "PalMedicineBox",                           new TechItemData { Name = "Medicine Rack",                      NameCn = "药品柜",               Description = "用来存放帕鲁的药物的柜子。\n\n生病的帕鲁会来这里吃药。\n\n记得随时补充药物，别让柜子空了。" } },
                { "PalRevive",                                new TechItemData { Name = "Revival Potion",                     NameCn = "复活药",               Description = "使受伤而无法动弹的帕鲁复活的药品。" } },
                { "PalSphere_Exotic",                         new TechItemData { Name = "Exotic Sphere",                      NameCn = "超限帕鲁球",           Description = "可扔出并捕捉帕鲁的道具。\n\n拥有超凡绝伦的性能，连超规格的帕鲁也能捕获。" } },
                { "Plastic",                                  new TechItemData { Name = "Plasteel",                           NameCn = "塑钢",                 Description = "由原油和金属加工制成的素材。\n\n可在电气炉制作。" } },
                { "Polymer",                                  new TechItemData { Name = "Polymer",                            NameCn = "聚合物",               Description = "加工油脂后制成的聚合物。\n\n是制作枪支时必须用到的素材。" } },
                { "Potion_Extreme",                           new TechItemData { Name = "Advanced Recovery Meds",             NameCn = "至高恢复药",           Description = "需要时间才能治疗身体伤口的药水。\n\n品质卓越，能够显著地恢复生命值。" } },
                { "Product_Axe_Grade_01",                     new TechItemData { Name = "Stone Axe",                          NameCn = "石头斧",               Description = "砍树用的斧子。\n\n因为是石头做的，感觉不怎么锋利。" } },
                { "Product_Axe_Grade_02",                     new TechItemData { Name = "Metal Axe",                          NameCn = "金属斧头",             Description = "砍树用的斧子。 金属制的，更为锋利。" } },
                { "Product_Axe_Grade_03",                     new TechItemData { Name = "Refined Metal Axe",                  NameCn = "精炼金属斧头",         Description = "砍树用的斧子。\n\n耐久度与锋利度均有更进一步的提升。" } },
                { "Product_CoalPit",                          new TechItemData { Name = "Coal Mine",                          NameCn = "石炭采矿场",           Description = "生产石炭的设施。\n\n挖掘石炭的工作需要大量体力，相当严苛。\n\n把这些工作交给擅长挖掘的帕鲁吧。" } },
                { "Product_Cooking_Grade_01",                 new TechItemData { Name = "Campfire",                           NameCn = "篝火",                 Description = "烹制食材时的必要设施。\n\n只能烧制普通食材。\n\n需要火系帕鲁点火。" } },
                { "Product_Cooking_Grade_02",                 new TechItemData { Name = "Cooking Pot",                        NameCn = "料理锅",               Description = "烹制食材时的必要设施。\n\n使用锅子增加了可烹制范围。\n\n需要火系帕鲁点火。" } },
                { "Product_Cooking_Grade_03",                 new TechItemData { Name = "Electric Kitchen",                   NameCn = "电气厨房",             Description = "烹制食材时的必要设施。\n\n需要电力，但可快速烹制许多食材。\n\n需要火系帕鲁点火。" } },
                { "Product_CopperPit",                        new TechItemData { Name = "Ore Mining Site",                    NameCn = "金属采矿场",           Description = "生产金属矿石的设施。\n\n挖掘金属矿石的工作需要大量体力，相当严苛。\n\n把这些工作交给擅长挖掘的帕鲁吧。" } },
                { "Product_CopperPit_2",                      new TechItemData { Name = "Ore Mining Site II",                 NameCn = "金属采矿场Ⅱ",          Description = "可大量生产金属矿石的设施。\n\n挖掘金属矿石的工作需要大量体力，相当严苛。\n\n把这些工作交给擅长挖掘的帕鲁吧。" } },
                { "Product_Factory_Hard_Grade_01",            new TechItemData { Name = "High-Quality Workbench",             NameCn = "优质作业台",           Description = "用于制作物品及护甲的优质作业台。\n\n操作台比较小，不可快速制作。\n\n需有能进行手工作业的帕鲁。" } },
                { "Product_Factory_Hard_Grade_02",            new TechItemData { Name = "Production Assembly Line",           NameCn = "作业流水线工厂",       Description = "用于制作物品及护甲的工厂。\n\n通过分工，可用一定速度完成制作。\n\n需有能进行手工作业的帕鲁。" } },
                { "Product_Factory_Hard_Grade_03",            new TechItemData { Name = "Production Assembly Line II",        NameCn = "作业流水线工厂 II",    Description = "用于制作物品及护甲的工厂。\n\n通过详细分工，可用很快速度完成制作。\n\n需有能进行手工作业的帕鲁。" } },
                { "Product_Farm_Berries",                     new TechItemData { Name = "Berry Plantation",                   NameCn = "野莓农园",             Description = "可培养红色野莓的农园。\n\n收获所需的时间较短，但不怎么能饱腹。\n\n为了收获，需要数只帕鲁进行播种与浇水。" } },
                { "Product_Farm_Carrot",                      new TechItemData { Name = "Carrot Plantation",                  NameCn = "胡萝卜园",             Description = "可培育胡萝卜的农园。\n\n此作物播种至收成所需时间较长，但可让料理更加丰富多元。\n\n需有数名帕鲁负责播种、灌溉及收获。" } },
                { "Product_Farm_Lettuce",                     new TechItemData { Name = "Lettuce Plantation",                 NameCn = "生菜农园",             Description = "可培养生菜的农园。\n\n收获所需的时间较长，但可增加料理的变化。\n\n为了收获，需要数只帕鲁进行播种与浇水。" } },
                { "Product_Farm_Onion",                       new TechItemData { Name = "Onion Plantation",                   NameCn = "洋葱园",               Description = "可培育洋葱的农园。\n\n此作物播种至收成所需时间较长，但可让料理更加丰富多元。\n\n需有数名帕鲁负责播种、灌溉及收获。" } },
                { "Product_Farm_Potato",                      new TechItemData { Name = "Potato Plantation",                  NameCn = "土豆园",               Description = "可培育土豆的农园。\n\n此作物播种至收成所需时间较长，但可让料理更加丰富多元。\n\n需有数名帕鲁负责播种、灌溉及收获。" } },
                { "Product_Farm_tomato",                      new TechItemData { Name = "Tomato Plantation",                  NameCn = "番茄农园",             Description = "可培养番茄的农园。\n\n收获所需的时间较长，但可增加料理的变化。\n\n为了收获，需要数只帕鲁进行播种与浇水。" } },
                { "Product_Farm_wheat",                       new TechItemData { Name = "Wheat Plantation",                   NameCn = "小麦农园",             Description = "可培养小麦的农园。\n\n收获所需的时间比较一般。\n\n为了收获，需要数只帕鲁进行播种与浇水。" } },
                { "Product_Ingot_Grade_01_Copper",            new TechItemData { Name = "Primitive Furnace",                  NameCn = "原始的炉子",           Description = "可冶炼金属锭。\n\n品质不佳，速度慢。\n\n需要火系帕鲁点火。" } },
                { "Product_Ingot_Grade_02_Iron",              new TechItemData { Name = "Improved Furnace",                   NameCn = "改善后的炉子",         Description = "可冶炼精炼金属锭。\n\n品质稍有提升，但速度仍然不足。\n\n需要火系帕鲁点火。" } },
                { "Product_Ingot_Grade_03_Steal",             new TechItemData { Name = "Electric Furnace",                   NameCn = "电气炉",               Description = "可冶炼帕鲁金属锭。\n\n需要电力，但可以更快完成冶炼。\n\n需要火系帕鲁点火。" } },
                { "Product_Medicine_Grade_01",                new TechItemData { Name = "Medieval Medicine Workbench",        NameCn = "中世纪制药台",         Description = "用于制作帕鲁治疗药的设备。\n\n只能制作简单的药品。\n\n交给可制药的帕鲁来操作吧。" } },
                { "Product_Medicine_Grade_02",                new TechItemData { Name = "Electric Medicine Workbench",        NameCn = "电气制药台",           Description = "用于制作帕鲁治疗药的设备。\n\n需要电力，但可制作高等药品。\n\n交给可制药的帕鲁来操作吧。" } },
                { "Product_Pickaxe_Grade_01",                 new TechItemData { Name = "Stone Pickaxe",                      NameCn = "石制十字镐",           Description = "采矿用的十字镐。\n\n因为是石头做的，效率不怎么样。" } },
                { "Product_Pickaxe_Grade_02",                 new TechItemData { Name = "Metal Pickaxe",                      NameCn = "金属十字镐",           Description = "采矿用的十字镐。\n\n以金属制成，耐久度和效率都得到了提升。" } },
                { "Product_Pickaxe_Grade_03",                 new TechItemData { Name = "Refined Metal Pickaxe",              NameCn = "精炼金属十字镐",       Description = "采矿用的十字镐。\n\n耐久度和效率均有更进一步的提升。" } },
                { "Product_QuartzPit",                        new TechItemData { Name = "Pure Quartz Mine",                   NameCn = "纯水晶采矿场",         Description = "生产纯水晶的设施。\n\n挖掘纯水晶的工作需要大量体力，相当严苛。\n\n把这些工作交给擅长挖掘的帕鲁吧。" } },
                { "Product_StationDeforest",                  new TechItemData { Name = "Logging Site",                       NameCn = "伐木场",               Description = "用于生产木材的设施。\n\n砍树非常辛苦，需要体力。\n\n交给擅长伐木的帕鲁吧。" } },
                { "Product_StonePit",                         new TechItemData { Name = "Stone Pit",                          NameCn = "采石场",               Description = "用于生产石头的设施。\n\n挖掘石头非常辛苦，需要体力。\n\n交给擅长挖掘的帕鲁吧。" } },
                { "Product_SulfurPit",                        new TechItemData { Name = "Sulfur Mine",                        NameCn = "硫磺采矿场",           Description = "生产硫磺的设施。\n\n挖掘硫磺的工作需要大量体力，相当严苛。\n\n把这些工作交给擅长挖掘的帕鲁吧。" } },
                { "Product_WeaponFactory_Dirty_Grade_01",     new TechItemData { Name = "Weapon Workbench",                   NameCn = "武器制作台",           Description = "生产武器及弹药的制作台。\n\n作业场规模较小，无法制作高级武器。\n\n需有能进行手工作业的帕鲁。" } },
                { "Product_WeaponFactory_Dirty_Grade_02",     new TechItemData { Name = "Weapon Assembly Line",               NameCn = "武器流水线工厂",       Description = "生产武器及弹药的工厂。\n\n经过分工后，能制作的武器种类也更厉害了一点。\n\n需有能进行手工作业的帕鲁。" } },
                { "Product_WeaponFactory_Dirty_Grade_03",     new TechItemData { Name = "Weapon Assembly Line II",            NameCn = "武器流水线工厂II",     Description = "用于生产武器及弹药的工厂。\n\n通过详细分工，可用很快速度制作高等武器。\n\n需有能进行手工作业的帕鲁。" } },
                { "Product_WorkBench_SkillUnlock",            new TechItemData { Name = "Pal Gear Workbench",                 NameCn = "帕鲁装置制作台",       Description = "制作帕鲁所使用的道具的原始的作业台。\n\n可制作座垫并骑乘帕鲁，\n\n或制作枪供帕鲁射击。" } },
                { "Refrigerator",                             new TechItemData { Name = "Refrigerator",                       NameCn = "冰箱",                 Description = "大型食物储存库。\n\n将冰系帕鲁指派到这里工作时，\n\n可以使里面的食物不会轻易腐败。" } },
                { "ReinforcedArrow",                          new TechItemData { Name = "Reinforced Arrow",                   NameCn = "强化箭矢",             Description = "复合弓专用的强化箭矢。" } },
                { "RepairBench",                              new TechItemData { Name = "Repair Bench",                       NameCn = "修理台",               Description = "可以修理损坏道具的工作台。\n\n修理需要消耗素材。" } },
                { "RepairKit",                                new TechItemData { Name = "Repair Kit",                         NameCn = "修理套装",             Description = "用于修理建筑物的道具。\n\n可用于修补损坏的建筑物。" } },
                { "RifleBullet",                              new TechItemData { Name = "Rifle Ammo",                         NameCn = "步枪子弹",             Description = "用于步枪等武器的子弹。" } },
                { "RoughBullet",                              new TechItemData { Name = "Coarse Ammo",                        NameCn = "劣质弹药",             Description = "用于鸟枪和劣质手枪等武器的子弹。" } },
                { "SFArmor",                                  new TechItemData { Name = "Hexolite Armor",                     NameCn = "六棱晶盔甲",           Description = "以六棱晶制成的盔甲。\n\n具有压倒性的耐久度和防御力。耐寒Lv1 耐暑Lv1" } },
                { "SFArmorCold",                              new TechItemData { Name = "Cold Resistant Hexolite Armor",      NameCn = "耐寒六棱晶盔甲",       Description = "经过改良的六棱晶盔甲。\n\n能够抵御使身体冻僵的寒意。耐寒Lv2 耐暑Lv1" } },
                { "SFArmorHeat",                              new TechItemData { Name = "Heat Resistant Hexolite Armor",      NameCn = "耐热六棱晶盔甲",       Description = "经过改良的六棱晶盔甲。\n\n具备傲人的耐热性能。耐寒Lv1 耐暑Lv2" } },
                { "SFArmorWeight",                            new TechItemData { Name = "Lightweight Hexolite Armor",         NameCn = "轻便六棱晶盔甲",       Description = "经过改良的六棱晶盔甲。\n\n能增加玩家的负重上限。耐寒Lv1 耐暑Lv1 提升负重上限Lv2" } },
                { "SFArrow",                                  new TechItemData { Name = "Advanced Arrow",                     NameCn = "卓越箭矢",             Description = "卓越弓专用的六棱晶素材箭矢。" } },
                { "SFBow",                                    new TechItemData { Name = "Advanced Bow",                       NameCn = "卓越弓",               Description = "以超高科技重新打造的弓。\n\n能够射出威力极强的箭矢。" } },
                { "SFHelmet",                                 new TechItemData { Name = "Hexolite Helmet",                    NameCn = "六棱晶头盔",           Description = "以六棱晶制成的头盔。\n\n具有难以置信的强度和轻便性。" } },
                { "Salvage_TreasureBoxKey02",                 new TechItemData { Name = "Powerful Fishing Magnet",            NameCn = "强力渔用磁铁",         Description = "进行精密打捞时所需的强力磁铁。\n\n可利用强大的磁力将沉在海底的资源和物资吸附上来。" } },
                { "SanityDecrease1",                          new TechItemData { Name = "Alpha Wave Generator",               NameCn = "α波发生器",           Description = "发出α波让帕鲁放松的装置。\n\n减缓据点内帕鲁的SAN值下降速度。\n\n即使配置复数个，其效果也不会叠加。" } },
                { "Shield_01",                                new TechItemData { Name = "Common Shield",                      NameCn = "普通护盾",             Description = "以帕鲁矿技术制成的神奇护盾。\n\n一定时间不受攻击时可自动修复。" } },
                { "Shield_02",                                new TechItemData { Name = "Mega Shield",                        NameCn = "高级护盾",             Description = "以帕鲁矿技术制成的神奇护盾。\n\n一定时间不受攻击时可自动修复。\n\n改良原型设计，提高了防御力。" } },
                { "Shield_03",                                new TechItemData { Name = "Giga Shield",                        NameCn = "优质护盾",             Description = "以帕鲁矿技术制成的神奇护盾。\n\n一定时间不受攻击时可自动修复。\n\n经过改良，进一步提升了性能。" } },
                { "Shield_04",                                new TechItemData { Name = "Hyper Shield",                       NameCn = "特级护盾",             Description = "以帕鲁矿技术制成的神奇护盾。\n\n一定时间不受攻击时可自动修复。\n\n经过多次实验，实现了最高峰的品质。" } },
                { "Shield_05",                                new TechItemData { Name = "Ultra Shield",                       NameCn = "超级护盾",             Description = "以帕鲁矿技术制成的神奇护盾。\n\n一定时间不受攻击时可自动修复。\n\n在进一步多次实验后，结果实现了究极的品质。" } },
                { "Shield_SF",                                new TechItemData { Name = "Advanced Shield",                    NameCn = "卓越护盾",             Description = "以超高科技打造的神奇护盾。\n\n一定时间不受攻击时可自动修复。\n\n品质达到完美无缺的境界。" } },
                { "ShotGunBullet",                            new TechItemData { Name = "Shotgun Shell",                      NameCn = "霰弹枪子弹",           Description = "用于霰弹枪等武器的子弹。" } },
                { "Signboard",                                new TechItemData { Name = "Sign",                               NameCn = "告示牌",               Description = "可以输入文字的告示牌。\n\n用于记录和交流。" } },
                { "Silo",                                     new TechItemData { Name = "Silo",                               NameCn = "筒仓",                 Description = "如配置在据点，\n\n可以提升「播种」的工作速度。\n\n即使配置复数个，其效果也不会叠加。" } },
                { "SkillUnlock_Alpaca",                       new TechItemData { Name = "Melpaca Saddle",                     NameCn = "美露帕的鞍具",         Description = "持有此道具时，\n\n即可骑乘美露帕。" } },
                { "SkillUnlock_AmaterasuWolf",                new TechItemData { Name = "Kitsun Saddle",                      NameCn = "苍焰狼的鞍具",         Description = "持有此道具时，\n\n即可骑乘苍焰狼。" } },
                { "SkillUnlock_AmaterasuWolf_Dark",           new TechItemData { Name = "Kitsun Noct Saddle",                 NameCn = "幽焰狼的鞍具",         Description = "持有此道具时，\n\n即可骑乘幽焰狼。" } },
                { "SkillUnlock_BadCatgirl",                   new TechItemData { Name = "Nyafia's Shotgun",                   NameCn = "妮瞅莎的霰弹枪",       Description = "妮瞅莎专用的霰弹枪。 发动伙伴技能后，妮瞅莎会进入射击模式， 猛烈攻击周围的敌人。" } },
                { "SkillUnlock_BirdDragon",                   new TechItemData { Name = "Vanwyrm Saddle",                     NameCn = "烽歌龙的鞍具",         Description = "持有此道具时，\n\n即可骑乘烽歌龙。" } },
                { "SkillUnlock_BirdDragon_Ice",               new TechItemData { Name = "Vanwyrm Cryst Saddle",               NameCn = "霜歌龙的鞍具",         Description = "持有此道具时，\n\n即可骑乘霜歌龙。" } },
                { "SkillUnlock_BlackCentaur",                 new TechItemData { Name = "Necromus Saddle",                    NameCn = "混沌骑士的鞍具",       Description = "持有此道具时，\n\n即可骑乘混沌骑士。" } },
                { "SkillUnlock_BlackGriffon",                 new TechItemData { Name = "Shadowbeak Saddle",                  NameCn = "异构格里芬的鞍具",     Description = "持有此道具时，\n\n即可骑乘异构格里芬。" } },
                { "SkillUnlock_BlackMetalDragon",             new TechItemData { Name = "Astegon Saddle",                     NameCn = "魔渊龙的鞍具",         Description = "持有此道具时，\n\n即可骑乘魔渊龙。" } },
                { "SkillUnlock_BlackPuppy",                   new TechItemData { Name = "Smokie's Harness",                   NameCn = "墨丸的背带",           Description = "墨丸的背带。 发动伙伴技能后，它会探测附近的铬铁矿位置。" } },
                { "SkillUnlock_BlueDragon",                   new TechItemData { Name = "Azurobe Saddle",                     NameCn = "碧海龙的鞍具",         Description = "持有此道具时，\n\n即可骑乘碧海龙。" } },
                { "SkillUnlock_BlueDragon_Ice",               new TechItemData { Name = "Azurobe Cryst Saddle",               NameCn = "碧月龙的鞍具",         Description = "持有此道具时，\n\n即可骑乘碧月龙。" } },
                { "SkillUnlock_BlueThunderHorse",             new TechItemData { Name = "Azurmane Saddle",                    NameCn = "驭雷马的鞍具",         Description = "持有此道具时，\n\n即可骑乘驭雷马。" } },
                { "SkillUnlock_Boar",                         new TechItemData { Name = "Rushoar Saddle",                     NameCn = "草莽猪的鞍具",         Description = "持有此道具时，\n\n即可骑乘草莽猪。" } },
                { "SkillUnlock_Carbunclo",                    new TechItemData { Name = "Lifmunk's Submachine Gun",           NameCn = "翠叶鼠的冲锋枪",       Description = "翠叶鼠专用的小型冲锋枪。 发动伙伴技能后，它会坐在玩家的头上， 配合玩家的攻击进行额外射击。" } },
                { "SkillUnlock_ColorfulBird",                 new TechItemData { Name = "Tocotoco's Gloves",                  NameCn = "炸蛋鸟的手套",         Description = "用来轻轻抓住炸蛋鸟的手套。 发动伙伴技能后，即可抱着炸蛋鸟， 发射爆炸蛋来进行攻击。" } },
                { "SkillUnlock_DarkMechaDragon",              new TechItemData { Name = "Xenolord Saddle",                    NameCn = "杰诺多兰的鞍具",       Description = "持有此道具时，\n\n即可骑乘杰诺多兰。" } },
                { "SkillUnlock_Deer",                         new TechItemData { Name = "Eikthyrdeer Saddle",                 NameCn = "紫霞鹿的鞍具",         Description = "持有此道具时，\n\n即可骑乘紫霞鹿。" } },
                { "SkillUnlock_Deer_Ground",                  new TechItemData { Name = "Eikthyrdeer Terra Saddle",           NameCn = "祇岳鹿的鞍具",         Description = "持有此道具时，\n\n即可骑乘祇岳鹿。" } },
                { "SkillUnlock_DreamDemon",                   new TechItemData { Name = "Daedream's Necklace",                NameCn = "寐魔的项圈",           Description = "寐魔的项圈。 持有此道具时，队伍中的寐魔会 一直待在场上，并配合玩家的攻击发动额外攻击。" } },
                { "SkillUnlock_DrillGame",                    new TechItemData { Name = "Digtoise's Headband",                NameCn = "碎岩龟的头巾",         Description = "碎岩龟的决胜头带。 发动伙伴技能后，碎岩龟会进入高速旋转状态， 能够高效率钻凿岩石。" } },
                { "SkillUnlock_Eagle",                        new TechItemData { Name = "Galeclaw's Gloves",                  NameCn = "天擒鸟的手套",         Description = "持有此道具且天擒鸟在队伍中时， 会改变滑翔伞的性能。" } },
                { "SkillUnlock_ElecPanda",                    new TechItemData { Name = "Grizzbolt's Minigun",                NameCn = "暴电熊的机关枪",       Description = "持有此道具时，即可骑乘暴电熊， 并用机关枪进行猛烈射击。" } },
                { "SkillUnlock_FairyDragon",                  new TechItemData { Name = "Elphidran Saddle",                   NameCn = "精灵龙的鞍具",         Description = "持有此道具时，\n\n即可骑乘精灵龙。" } },
                { "SkillUnlock_FairyDragon_Water",            new TechItemData { Name = "Elphidran Aqua Saddle",              NameCn = "水灵龙的鞍具",         Description = "持有此道具时，\n\n即可骑乘水灵龙。" } },
                { "SkillUnlock_FeatherOstrich",               new TechItemData { Name = "Dazemu Saddle",                      NameCn = "战冠雀的鞍具",         Description = "持有此道具时，\n\n即可骑乘战冠雀。" } },
                { "SkillUnlock_FengyunDeeper",                new TechItemData { Name = "Fenglope Saddle",                    NameCn = "云海鹿的鞍具",         Description = "持有此道具时，\n\n即可骑乘云海鹿。" } },
                { "SkillUnlock_FengyunDeeper_Electric",       new TechItemData { Name = "Fenglope Lux Saddle",                NameCn = "雷隐鹿的鞍具",         Description = "持有此道具时，\n\n即可骑乘雷隐鹿。" } },
                { "SkillUnlock_FireKirin",                    new TechItemData { Name = "Pyrin Saddle",                       NameCn = "火麒麟的鞍具",         Description = "持有此道具时，\n\n即可骑乘火麒麟。" } },
                { "SkillUnlock_FireKirin_Dark",               new TechItemData { Name = "Pyrin Noct Saddle",                  NameCn = "邪麒麟的鞍具",         Description = "持有此道具时，\n\n即可骑乘邪麒麟。" } },
                { "SkillUnlock_FlameBuffalo",                 new TechItemData { Name = "Arsox Saddle",                       NameCn = "炽焰牛的鞍具",         Description = "持有此道具时，\n\n即可骑乘炽焰牛。" } },
                { "SkillUnlock_FlowerDinosaur",               new TechItemData { Name = "Dinossom Saddle",                    NameCn = "花冠龙的鞍具",         Description = "持有此道具时，\n\n即可骑乘花冠龙。" } },
                { "SkillUnlock_FlowerDinosaur_Electric",      new TechItemData { Name = "Dinossom Lux Saddle",                NameCn = "雷龙怪的鞍",           Description = "可骑在雷龙怪的背上移动。 骑乘期间可采集蛋。" } },
                { "SkillUnlock_FlowerRabbit",                 new TechItemData { Name = "Flopie's Necklace",                  NameCn = "波娜兔的项圈",         Description = "波娜兔的项圈。 持有此道具时，队伍中的波娜兔会 一直待在场上，并自动拾取附近的道具。" } },
                { "SkillUnlock_FlyingManta",                  new TechItemData { Name = "Celaray's Gloves",                   NameCn = "鲁米儿的手套",         Description = "持有此道具且鲁米儿在队伍中时， 会改变滑翔伞的性能。" } },
                { "SkillUnlock_FlyingManta_Thunder",          new TechItemData { Name = "Celaray Lux's Gloves",               NameCn = "雷米儿的手套",         Description = "持有此道具，且雷米儿在队伍中时， 会改变滑翔伞的性能。" } },
                { "SkillUnlock_Garm",                         new TechItemData { Name = "Direhowl's Saddled Harness",         NameCn = "猎狼的挽具鞍具组合",   Description = "持有此道具时， 即可骑乘猎狼。" } },
                { "SkillUnlock_GhostAnglerfish",              new TechItemData { Name = "Ghangler Saddle",                    NameCn = "冥灯鱼的鞍具",         Description = "持有此道具时，\n\n即可骑乘冥灯鱼。" } },
                { "SkillUnlock_GhostAnglerfish_Fire",         new TechItemData { Name = "Ghangler Ignis Saddle",              NameCn = "炙灯鱼的鞍具",         Description = "持有此道具时，\n\n即可骑乘炙灯鱼。" } },
                { "SkillUnlock_GhostBeast",                   new TechItemData { Name = "Maraith Saddle",                     NameCn = "噬魂兽的鞍具",         Description = "持有此道具时，\n\n即可骑乘噬魂兽。" } },
                { "SkillUnlock_GoldenHorse",                  new TechItemData { Name = "Gildane Saddle",                     NameCn = "金驰兽的鞍具",         Description = "持有此道具时，\n\n即可骑乘金驰兽。" } },
                { "SkillUnlock_GrassMammoth",                 new TechItemData { Name = "Mammorest Saddle",                   NameCn = "森猛犸的鞍具",         Description = "持有此道具时，\n\n即可骑乘森猛犸。" } },
                { "SkillUnlock_GrassMammoth_Ice",             new TechItemData { Name = "Mammorest Cryst Saddle",             NameCn = "雪猛犸的鞍具",         Description = "持有此道具时，\n\n即可骑乘雪猛犸。" } },
                { "SkillUnlock_GrassPanda",                   new TechItemData { Name = "Mossanda's Grenade Launcher",        NameCn = "叶胖达的榴弹发射器",   Description = "持有此道具时， 即可骑乘叶胖达，并用榴弹发射器进行猛烈射击。" } },
                { "SkillUnlock_GrassPanda_Electric",          new TechItemData { Name = "Mossanda Lux's Grenade Launcher",    NameCn = "雷胖达的榴弹发射器",   Description = "持有此道具时， 即可骑乘雷胖达，并用榴弹发射器进行猛烈轰炸。" } },
                { "SkillUnlock_GuardianDog",                  new TechItemData { Name = "Yakumo Saddle",                      NameCn = "八云犬的鞍具",         Description = "持有此道具时，\n\n即可骑乘八云犬。" } },
                { "SkillUnlock_HadesBird",                    new TechItemData { Name = "Helzephyr Saddle",                   NameCn = "雷冥鸟的鞍具",         Description = "持有此道具时，\n\n即可骑乘雷冥鸟。" } },
                { "SkillUnlock_HadesBird_Electric",           new TechItemData { Name = "Helzephyr Lux Saddle",               NameCn = "雷鸣鸟的鞍具",         Description = "持有此道具时，\n\n即可骑乘雷鸣鸟。" } },
                { "SkillUnlock_HawkBird",                     new TechItemData { Name = "Nitewing Saddle",                    NameCn = "疾风隼的鞍具",         Description = "持有此道具时，\n\n即可骑乘疾风隼。" } },
                { "SkillUnlock_Hedgehog",                     new TechItemData { Name = "Jolthog's Gloves",                   NameCn = "电棘鼠的手套",         Description = "用来抓住電棘鼠的橡胶手套。 发动伙伴技能后，即可将電棘鼠 当作电击型手榴弹投掷出去。" } },
                { "SkillUnlock_Hedgehog_Ice",                 new TechItemData { Name = "Jolthog Cryst's Gloves",             NameCn = "冰刺鼠的手套",         Description = "用来抓住冰刺鼠的隔热手套。 发动伙伴技能后，即可将冰刺鼠 当作冷冻型手榴弹投掷出去。" } },
                { "SkillUnlock_Horus",                        new TechItemData { Name = "Faleris Saddle",                     NameCn = "荷鲁斯的鞍具",         Description = "持有此道具时，\n\n即可骑乘荷鲁斯。" } },
                { "SkillUnlock_Horus_Water",                  new TechItemData { Name = "Faleris Aqua Saddle",                NameCn = "伊西斯的鞍具",         Description = "持有此道具时，\n\n即可骑乘伊西斯。" } },
                { "SkillUnlock_IceDeer",                      new TechItemData { Name = "Reindrix Saddle",                    NameCn = "严冬鹿的鞍具",         Description = "持有此道具时，\n\n即可骑乘严冬鹿。" } },
                { "SkillUnlock_IceHorse",                     new TechItemData { Name = "Frostallion Saddle",                 NameCn = "唤冬兽的鞍具",         Description = "持有此道具时，\n\n即可骑乘唤冬兽。" } },
                { "SkillUnlock_IceHorse_Dark",                new TechItemData { Name = "Frostallion Noct Saddle",            NameCn = "唤夜兽的鞍具",         Description = "持有此道具时，\n\n即可骑乘唤夜兽。" } },
                { "SkillUnlock_IceNarwhal",                   new TechItemData { Name = "Whalaska Saddle",                    NameCn = "凉晶鲸的鞍具",         Description = "持有此道具时，\n\n即可骑乘凉晶鲸。" } },
                { "SkillUnlock_IceNarwhal_Fire",              new TechItemData { Name = "Whalaska Ignis Saddle",              NameCn = "桃晶鲸的鞍具",         Description = "持有此道具时，\n\n即可骑乘桃晶鲸。" } },
                { "SkillUnlock_IceSeal",                      new TechItemData { Name = "Polapup Saddle",                     NameCn = "香草豹冰的鞍具",       Description = "持有此道具时，\n\n即可骑乘香草豹冰。" } },
                { "SkillUnlock_JetDragon",                    new TechItemData { Name = "Jetragon's Missile Launcher",        NameCn = "空涡龙的导弹发射器",   Description = "持有此道具时， 即可骑乘空涡龙，并用导弹进行猛烈轰炸。" } },
                { "SkillUnlock_KingAlpaca",                   new TechItemData { Name = "Kingpaca Saddle",                    NameCn = "君王美露帕的鞍具",     Description = "持有此道具时，\n\n即可骑乘君王美露帕。" } },
                { "SkillUnlock_KingAlpaca_Ice",               new TechItemData { Name = "Kingpaca Cryst Saddle",              NameCn = "冰帝美露帕的鞍具",     Description = "持有此道具时，\n\n即可骑乘冰帝美露帕。" } },
                { "SkillUnlock_KingBahamut",                  new TechItemData { Name = "Blazamut Saddle",                    NameCn = "焰煌的鞍具",           Description = "持有此道具时，\n\n即可骑乘焰煌。" } },
                { "SkillUnlock_KingBahamut_Dragon",           new TechItemData { Name = "Blazamut Ryu Saddle",                NameCn = "殁殃的鞍具",           Description = "持有此道具时，\n\n即可骑乘殁殃。" } },
                { "SkillUnlock_Kirin",                        new TechItemData { Name = "Univolt Saddle",                     NameCn = "雷角马的鞍具",         Description = "持有此道具时，\n\n即可骑乘雷角马。" } },
                { "SkillUnlock_Kitsunebi",                    new TechItemData { Name = "Foxparks's Harness",                 NameCn = "火绒狐的背带",         Description = "用来抱住火绒狐的背带。 发动伙伴技能后，即可将火绒狐 抱在身上当作火焰喷射器进行攻击。" } },
                { "SkillUnlock_Kitsunebi_Ice",                new TechItemData { Name = "Foxparks Cryst's Harness",           NameCn = "雪绒狐的背带",         Description = "用来抱住雪绒狐的背带。 发动伙伴技能后，即可将雪绒狐 抱在身上当作冷冻喷射器进行攻击。" } },
                { "SkillUnlock_LazyDragon",                   new TechItemData { Name = "Relaxaurus's Missile Launcher",      NameCn = "佩克龙的导弹发射器",   Description = "持有此道具时，即可骑乘佩克龙， 并用导弹发射器进行猛烈射击。" } },
                { "SkillUnlock_LazyDragon_Electric",          new TechItemData { Name = "Relaxaurus Lux's Missile Launcher",  NameCn = "派克龙的导弹发射器",   Description = "持有此道具时， 即可骑乘派克龙，并用导弹发射器进行猛烈轰炸。" } },
                { "SkillUnlock_LeafMomonga",                  new TechItemData { Name = "Herbil's Harness",                   NameCn = "达鼠泥的背带",         Description = "达鼠泥的背带。 持有此道具时，达鼠泥会在玩家陷入濒死状态时 通过疗愈能力让玩家复活。" } },
                { "SkillUnlock_Manticore",                    new TechItemData { Name = "Blazehowl Saddle",                   NameCn = "狱焰王的鞍具",         Description = "持有此道具时，\n\n即可骑乘狱焰王。" } },
                { "SkillUnlock_Manticore_Dark",               new TechItemData { Name = "Blazehowl Noct Saddle",              NameCn = "狱阎王的鞍具",         Description = "持有此道具时，\n\n即可骑乘狱阎王。" } },
                { "SkillUnlock_Monkey",                       new TechItemData { Name = "Tanzee's Assault Rifle",             NameCn = "新叶猿的突击步枪",     Description = "新叶猿专用的小型突击步枪。 发动伙伴技能后，新叶猿会进入射击模式， 猛烈扫射周围的敌人。" } },
                { "SkillUnlock_MoonQueen",                    new TechItemData { Name = "Selyne Saddle",                      NameCn = "辉月伊的鞍具",         Description = "持有此道具时，\n\n即可骑乘辉月伊。" } },
                { "SkillUnlock_MopKing",                      new TechItemData { Name = "Sweepa Saddle",                      NameCn = "毛老爹的鞍具",         Description = "持有此道具时，\n\n即可骑乘毛老爹。" } },
                { "SkillUnlock_MushroomDragon",               new TechItemData { Name = "Shroomer Saddle",                    NameCn = "菇咚的鞍具",           Description = "持有此道具时，\n\n即可骑乘菇咚。" } },
                { "SkillUnlock_MushroomDragon_Dark",          new TechItemData { Name = "Shroomer Noct Saddle",               NameCn = "菇波的鞍具",           Description = "持有此道具时，\n\n即可骑乘菇波。" } },
                { "SkillUnlock_NaughtyCat",                   new TechItemData { Name = "Grintale Saddle",                    NameCn = "笑魇猫的鞍具",         Description = "持有此道具时，\n\n即可骑乘笑魇猫。" } },
                { "SkillUnlock_NegativeOctopus",              new TechItemData { Name = "Killamari's Gloves",                 NameCn = "勾魂鱿的手套",         Description = "持有此道具且勾魂鱿在队伍中时， 会改变滑翔伞的性能。" } },
                { "SkillUnlock_NegativeOctopus_Neutral",      new TechItemData { Name = "Killamari Primo's Gloves",           NameCn = "蚀魂鱿的手套",         Description = "持有此道具，且蚀魂鱿在队伍中时， 会改变滑翔伞的性能。" } },
                { "SkillUnlock_NightBlueHorse",               new TechItemData { Name = "Starryon Saddle",                    NameCn = "夜冥驹的鞍具",         Description = "持有此道具时，\n\n即可骑乘夜冥驹。" } },
                { "SkillUnlock_Penguin",                      new TechItemData { Name = "Pengullet Rocket Launcher",          NameCn = "企丸丸的火箭发射器",   Description = "用来发射企丸丸的炮筒。\n\n发动伙伴技能后，即可将企丸丸射出\n\n对敌人进行攻击。随后，企丸丸会进入濒死状态。" } },
                { "SkillUnlock_Penguin_Electric",             new TechItemData { Name = "Pengullet Lux's Rocket Launcher",    NameCn = "闪丸丸的火箭发射器",   Description = "用来发射闪丸丸的炮筒。 发动伙伴技能后，即可将闪丸丸射出， 对敌人进行攻击。随后，闪丸丸会进入濒死状态。" } },
                { "SkillUnlock_Plesiosaur",                   new TechItemData { Name = "Braloha Saddle",                     NameCn = "梁叶龙的鞍具",         Description = "持有此道具时，\n\n即可骑乘梁叶龙。" } },
                { "SkillUnlock_PoseidonOrca",                 new TechItemData { Name = "Neptilius Saddle",                   NameCn = "海皇鲸的鞍具",         Description = "持有此道具时，\n\n即可骑乘海皇鲸。" } },
                { "SkillUnlock_PurpleSpider",                 new TechItemData { Name = "Tarantriss Saddle",                  NameCn = "桃蛛娘的鞍具",         Description = "持有此道具时，\n\n即可骑乘桃蛛娘。" } },
                { "SkillUnlock_RaijinDaughter",               new TechItemData { Name = "Dazzi's Necklace",                   NameCn = "雷鸣童子的项圈",       Description = "雷鸣童子的项圈。 持有此道具时，队伍中的雷鸣童子会 一直待在场上，并配合玩家的攻击发动额外攻击。" } },
                { "SkillUnlock_RaijinDaughter_Water",         new TechItemData { Name = "Dazzi Noct's Necklace",              NameCn = "天阴童子的项圈",       Description = "天阴童子的项圈。 持有此道具时，队伍中的天阴童子会 一直待在场上，并配合玩家的攻击发动额外攻击。" } },
                { "SkillUnlock_RedArmorBird",                 new TechItemData { Name = "Ragnahawk Saddle",                   NameCn = "燧火鸟的鞍具",         Description = "持有此道具时，\n\n即可骑乘燧火鸟。" } },
                { "SkillUnlock_SaintCentaur",                 new TechItemData { Name = "Paladius Saddle",                    NameCn = "圣光骑士的鞍具",       Description = "持有此道具时，\n\n即可骑乘圣光骑士。" } },
                { "SkillUnlock_SakuraSaurus",                 new TechItemData { Name = "Broncherry Saddle",                  NameCn = "连理龙的鞍具",         Description = "持有此道具时，\n\n即可骑乘连理龙。" } },
                { "SkillUnlock_SakuraSaurus_Water",           new TechItemData { Name = "Broncherry Aqua Saddle",             NameCn = "海誓龙的鞍具",         Description = "持有此道具时，\n\n即可骑乘海誓龙。" } },
                { "SkillUnlock_Serpent",                      new TechItemData { Name = "Surfent Saddle",                     NameCn = "滑水蛇的鞍具",         Description = "持有此道具时，\n\n即可骑乘滑水蛇。" } },
                { "SkillUnlock_Serpent_Ground",               new TechItemData { Name = "Surfent Terra Saddle",               NameCn = "流沙蛇的鞍具",         Description = "持有此道具时，\n\n即可骑乘流沙蛇。" } },
                { "SkillUnlock_SkyDragon",                    new TechItemData { Name = "Quivern Saddle",                     NameCn = "天羽龙的鞍具",         Description = "持有此道具时，\n\n即可骑乘天羽龙。" } },
                { "SkillUnlock_SkyDragon_Grass",              new TechItemData { Name = "Quivern Botan Saddle",               NameCn = "翠羽龙的鞍具",         Description = "持有此道具时，\n\n即可骑乘翠羽龙。" } },
                { "SkillUnlock_SnowTigerBeastman",            new TechItemData { Name = "Bastigor's Hammer",                  NameCn = "霜牙王的巨锤",         Description = "持有此道具时， 即可骑乘霜牙王。" } },
                { "SkillUnlock_Suzaku",                       new TechItemData { Name = "Suzaku Saddle",                      NameCn = "朱雀的鞍具",           Description = "持有此道具时，\n\n即可骑乘朱雀。" } },
                { "SkillUnlock_Suzaku_Water",                 new TechItemData { Name = "Suzaku Aqua Saddle",                 NameCn = "清雀的鞍具",           Description = "持有此道具时，\n\n即可骑乘清雀。" } },
                { "SkillUnlock_ThunderBird",                  new TechItemData { Name = "Beakon Saddle",                      NameCn = "迅雷鸟的鞍",           Description = "可骑在迅雷鸟的背上移动。 骑乘期间可进行二段跳跃。" } },
                { "SkillUnlock_ThunderDog",                   new TechItemData { Name = "Rayhound Saddle",                    NameCn = "霹雳犬的鞍具",         Description = "持有此道具时，\n\n即可骑乘霹雳犬。" } },
                { "SkillUnlock_TropicalOstrich",              new TechItemData { Name = "Palumba Saddle",                     NameCn = "咕咕桑葩的鞍具",       Description = "持有此道具时，\n\n即可骑乘咕咕桑葩。" } },
                { "SkillUnlock_Umihebi",                      new TechItemData { Name = "Jormuntide Saddle",                  NameCn = "覆海龙的鞍具",         Description = "持有此道具时，\n\n即可骑乘覆海龙。" } },
                { "SkillUnlock_Umihebi_Fire",                 new TechItemData { Name = "Jormuntide Ignis Saddle",            NameCn = "腾炎龙的鞍具",         Description = "持有此道具时，\n\n即可骑乘腾炎龙。" } },
                { "SkillUnlock_VolcanicMonster",              new TechItemData { Name = "Reptyro Saddle",                     NameCn = "熔岩兽的鞍具",         Description = "持有此道具时，\n\n即可骑乘熔岩兽。" } },
                { "SkillUnlock_VolcanicMonster_Ice",          new TechItemData { Name = "Reptyro Cryst Saddle",               NameCn = "寒霜兽的鞍具",         Description = "持有此道具时，\n\n即可骑乘寒霜兽。" } },
                { "SkillUnlock_WeaselDragon",                 new TechItemData { Name = "Chillet Saddle",                     NameCn = "疾旋鼬的鞍具",         Description = "持有此道具时，\n\n即可骑乘疾旋鼬。" } },
                { "SkillUnlock_WeaselDragon_Fire",            new TechItemData { Name = "Chillet Ignis Saddle",               NameCn = "桃旋鼬的鞍具",         Description = "持有此道具时，\n\n即可骑乘桃旋鼬。" } },
                { "SkillUnlock_WhiteAlienDragon",             new TechItemData { Name = "Xenogard Saddle",                    NameCn = "杰诺路达的鞍具",       Description = "持有此道具时，\n\n即可骑乘杰诺路达。" } },
                { "SkillUnlock_WhiteDeer",                    new TechItemData { Name = "Celesdir Saddle",                    NameCn = "净世鹿的鞍具",         Description = "持有此道具时，\n\n即可骑乘净世鹿。" } },
                { "SkillUnlock_WhiteShieldDragon",            new TechItemData { Name = "Silvegis Saddle",                    NameCn = "艾基鲁迦的鞍具",       Description = "持有此道具时，\n\n即可骑乘艾基鲁迦。" } },
                { "SkillUnlock_WindChimes",                   new TechItemData { Name = "Hangyu's Gloves",                    NameCn = "吊缚灵的手套",         Description = "持有此道具且吊缚灵在队伍中时， 会改变滑翔伞的性能。" } },
                { "SkillUnlock_WindChimes_Ice",               new TechItemData { Name = "Hangyu Cryst's Glove",               NameCn = "冰缚灵的手套",         Description = "持有此道具且冰缚灵在队伍中时， 会改变滑翔伞的性能。" } },
                { "SkillUnlock_Yeti",                         new TechItemData { Name = "Wumpo Saddle",                       NameCn = "白绒雪怪的鞍具",       Description = "持有此道具时，\n\n即可骑乘白绒雪怪。" } },
                { "SkillUnlock_Yeti_Grass",                   new TechItemData { Name = "Wumpo Botan Saddle",                 NameCn = "绿苔绒怪的鞍具",       Description = "持有此道具时，\n\n即可骑乘绿苔绒怪。" } },
                { "SkinChange",                               new TechItemData { Name = "Pal Dressing Facility",              NameCn = "帕鲁装扮机",           Description = "用于改变外观的设施。\n\n可以为帕鲁更换特别的外观。\n\n通过游戏外的其他方式获得的外观也能使用。" } },
                { "Snowman",                                  new TechItemData { Name = "Snowman",                            NameCn = "雪人",                 Description = "如配置在据点，\n\n可以提升「冷却」的工作速度。\n\n即使配置复数个，其效果也不会叠加。" } },
                { "Spa",                                      new TechItemData { Name = "Hot Spring",                         NameCn = "温泉",                 Description = "供疲惫的据点帕鲁休息的设施。\n\n不仅可以消除劳动之后的疲劳感，也可以恢复SAN值。" } },
                { "Spa2",                                     new TechItemData { Name = "High Quality Hot Spring",            NameCn = "优质温泉",             Description = "供疲惫的据点帕鲁休息的设施。\n\n比一般设施更加舒适，所以SAN值的恢复效果也更加显著。" } },
                { "Spa3",                                     new TechItemData { Name = "Japanese-Style Hot Spring",          NameCn = "和风温泉",             Description = "供疲惫的据点帕鲁休息的设施。\n\n洋溢着浓浓和风的疗愈空间，所以SAN值的恢复效果也更加显著。" } },
                { "Spear",                                    new TechItemData { Name = "Stone Spear",                        NameCn = "石头长矛",             Description = "可在近战时发挥作用的石头长矛。\n\n射程很长，可在战斗时保持一定距离。" } },
                { "Spear_2",                                  new TechItemData { Name = "Metal Spear",                        NameCn = "金属长矛",             Description = "可在近战时发挥作用的石头长矛。\n\n用金属制成，提升了攻击力。\n\n射程很长，可在战斗时保持一定距离。" } },
                { "Spear_3",                                  new TechItemData { Name = "Refined Metal Spear",                NameCn = "精炼金属长矛",         Description = "可在近战时发挥作用的石头长矛。\n\n用优质金属制成，提升了攻击力。\n\n射程很长，可在战斗时保持一定距离。" } },
                { "Spear_ForestBoss",                         new TechItemData { Name = "Lily's Spear",                       NameCn = "莉莉之矛",             Description = "莉莉爱用的长矛。 她会用它来制裁蔑视帕鲁的人。" } },
                { "Special_ElectricHatchingPalEgg",           new TechItemData { Name = "Electric Egg Incubator",             NameCn = "电能帕鲁蛋孵化器",     Description = "用于孵化帕鲁蛋的装置。\n\n需要电力供应才能运作，\n\n但可自动将该孵化器的内部保持在最佳温度。" } },
                { "Special_HatchingPalEgg",                   new TechItemData { Name = "Egg Incubator",                      NameCn = "帕鲁蛋孵化器",         Description = "用于孵化帕鲁蛋的装置。\n\n放置帕鲁蛋后，经过一定时间可自动孵化。" } },
                { "Special_PalRankUp",                        new TechItemData { Name = "Pal Essence Condenser",              NameCn = "帕鲁浓缩机",           Description = "可提升帕鲁阶级的设施。\n\n将提取的帕鲁精华浓缩并注入同种类的帕鲁中，\n\n可突破肉体极限。" } },
                { "Special_PalSphere_Grade_01",               new TechItemData { Name = "Pal Sphere",                         NameCn = "帕鲁球",               Description = "可扔出并捕捉帕鲁的道具。\n\n此为量产款式，只对低等级的帕鲁有效。" } },
                { "Special_PalSphere_Grade_02",               new TechItemData { Name = "Mega Sphere",                        NameCn = "高级帕鲁球",           Description = "可扔出并捕捉帕鲁的道具。\n\n此款式性能得到提升，可捕捉的帕鲁范围更广。" } },
                { "Special_PalSphere_Grade_03",               new TechItemData { Name = "Giga Sphere",                        NameCn = "优质帕鲁球",           Description = "可扔出并捕捉帕鲁的道具。\n\n此款式可捕捉有一定强度的帕鲁。" } },
                { "Special_PalSphere_Grade_04",               new TechItemData { Name = "Hyper Sphere",                       NameCn = "特级帕鲁球",           Description = "可扔出并捕捉帕鲁的道具。\n\n此款式可捕捉相当强大的帕鲁。" } },
                { "Special_PalSphere_Grade_05",               new TechItemData { Name = "Ultra Sphere",                       NameCn = "大师帕鲁球",           Description = "可扔出并捕捉帕鲁的道具。\n\n此款式性能相当好，没多少帕鲁能逃得掉。" } },
                { "Special_PalSphere_Grade_06",               new TechItemData { Name = "Legendary Sphere",                   NameCn = "传奇帕鲁球",           Description = "可扔出并捕捉帕鲁的道具。\n\n此款式性能极高，几乎没有帕鲁能逃得掉。" } },
                { "Special_PalSphere_Grade_07",               new TechItemData { Name = "Ultimate Sphere",                    NameCn = "究极帕鲁球",           Description = "可扔出并捕捉帕鲁的道具。\n\n拥有压倒性的性能，几乎可以抓住任何帕鲁。" } },
                { "Special_SphereFactory_Black_Grade_01",     new TechItemData { Name = "Sphere Workbench",                   NameCn = "帕鲁球制作台",         Description = "捕捉帕鲁用的帕鲁球的制作台。\n\n作业场规模较小，无法迅速制作帕鲁球。\n\n需有能进行手工作业的帕鲁。" } },
                { "Special_SphereFactory_Black_Grade_02",     new TechItemData { Name = "Sphere Assembly Line",               NameCn = "帕鲁球流水线工厂",     Description = "捕捉帕鲁用的帕鲁球的工厂。\n\n经过分工后，制作速度也有了一定的水准。\n\n需有能进行手工作业的帕鲁。" } },
                { "Special_SphereFactory_Black_Grade_03",     new TechItemData { Name = "Sphere Assembly Line II",            NameCn = "帕鲁球流水线工厂II",   Description = "捕捉帕鲁用的帕鲁球的工厂。\n\n通过详细分工，可用很快速度制作帕鲁球。\n\n需有能进行手工作业的帕鲁。" } },
                { "SphereFactory_Black_04",                   new TechItemData { Name = "Advanced Sphere Assembly Line",      NameCn = "高等文明帕鲁球工厂",   Description = "用于制作帕鲁球的设备。 大幅提升帕鲁球的制作速度。 需有能进行手工作业的帕鲁。" } },
                { "SphereModule_Curve",                       new TechItemData { Name = "Curve Module",                       NameCn = "曲线模块",             Description = "装备后能在帕鲁球上增添曲线旋转。\n\n容易从死角命中帕鲁，并提高捕获力。投掷曲线+1 捕获力强化+2" } },
                { "SphereModule_Curve2",                      new TechItemData { Name = "Slider Module",                      NameCn = "滑球模块",             Description = "装备后能在帕鲁球上增添滑球旋转。\n\n帕鲁球能以较大角度急剧弯曲，\n\n更容易出其不意，并提高捕获力。滑球模块 捕获力强化+3" } },
                { "SphereModule_Heavy",                       new TechItemData { Name = "Heavy Weight Module",                NameCn = "重量模块",             Description = "装备后会增加帕鲁球的重量，\n\n虽然投掷距离会缩短，但能提高捕获力。帕鲁球重量+1 捕获力强化+1" } },
                { "SphereModule_Homing",                      new TechItemData { Name = "Homing Module",                      NameCn = "追踪模块",             Description = "装备后帕鲁球会自动追踪帕鲁，\n\n并且提高捕获力。追踪模块 捕获力强化+3" } },
                { "SphereModule_Sniper",                      new TechItemData { Name = "Sniper Module",                      NameCn = "狙击模块",             Description = "装备后会帕鲁球的投掷距离会增加，\n\n能够精准锁定帕鲁，并提高捕获力。投掷距离+1 捕获力强化+2" } },
                { "SphereModule_Sniper2",                     new TechItemData { Name = "Sniper Module Ⅱ",                   NameCn = "狙击模块Ⅱ",           Description = "装备后会帕鲁球的投掷距离会大幅增加，\n\n能够精准锁定帕鲁，并提高捕获力。投掷距离+2 捕获力强化+3" } },
                { "StainlessSteel",                           new TechItemData { Name = "Hexolite",                           NameCn = "六棱晶锭",             Description = "由铬铁矿和金属矿石、\n\n六棱晶矿加工制成的合金。\n\n用于制作先进装备或建筑的材料。" } },
                { "Stone_Gate",                               new TechItemData { Name = "Stone Gate",                         NameCn = "石制大门",             Description = "稍大体型的帕鲁也能通过的大门。\n\n以石头制成，还算坚固。" } },
                { "Stump",                                    new TechItemData { Name = "Stump and Axe",                      NameCn = "树桩和斧头",           Description = "如配置在据点，\n\n可以提升「采伐」的工作速度。\n\n即使配置复数个，其效果也不会叠加。" } },
                { "TableDresser01_Stone",                     new TechItemData { Name = "Antique Dresser",                    NameCn = "古典式化妆台",         Description = "装饰用的古典式化妆台。\n\n" } },
                { "ToolBoxV1",                                new TechItemData { Name = "Large Toolbox",                      NameCn = "大型工具箱",           Description = "如配置在据点，\n\n可以提升「手工作业」的工作速度。\n\n即使配置复数个，其效果也不会叠加。" } },
                { "Torch",                                    new TechItemData { Name = "Mounted Torch",                      NameCn = "固定式火把",           Description = "可在黑夜照亮据点的光源。\n\n需要火系帕鲁点火。" } },
                { "TransmissionTower",                        new TechItemData { Name = "Electric Pylon",                     NameCn = "输电塔",               Description = "如配置在据点，\n\n可以提升「发电」的工作速度。\n\n即使配置复数个，其效果也不会叠加。" } },
                { "Trap_LegHold",                             new TechItemData { Name = "Bear Trap (Small)",                  NameCn = "捕兽夹（小）",         Description = "这种陷阱能让踩到的帕鲁困住。\n\n受困的帕鲁更易被帕鲁球捕获。\n\n只对小型帕鲁有效。" } },
                { "Trap_LegHold_Big",                         new TechItemData { Name = "Bear Trap (Large)",                  NameCn = "捕兽夹（大）",         Description = "这种捕兽夹可夹住大型帕鲁，使之无法动弹。\n\n受困的帕鲁更易被帕鲁球捕获。\n\n只对大型帕鲁有效，小型帕鲁不会触发机关。" } },
                { "Trap_MineAttack",                          new TechItemData { Name = "Mine",                               NameCn = "地雷",                 Description = "这种陷阱被帕鲁踩到便会爆炸，\n\n并对大范围的目标造成伤害。" } },
                { "Trap_MineElecShock",                       new TechItemData { Name = "Electric Mine",                      NameCn = "电击地雷",             Description = "这种陷阱能让踩到的帕鲁触电。\n\n触电时的帕鲁更易被帕鲁球捕获。" } },
                { "Trap_MineFreeze",                          new TechItemData { Name = "Ice Mine",                           NameCn = "结冰地雷",             Description = "这种陷阱能让踩到的帕鲁冻结。\n\n被冻结的帕鲁更易被帕鲁球捕获。" } },
                { "Trap_Noose",                               new TechItemData { Name = "Hanging Trap",                       NameCn = "套索陷阱",             Description = "这种陷阱能抓住途经的帕鲁。\n\n受困的帕鲁更易被帕鲁球捕获。\n\n只对小型帕鲁有效。" } },
                { "Unlock_Picking_Tier1",                     new TechItemData { Name = "Lockpicking Tool v1",                NameCn = "简易开锁工具",         Description = "持有此道具时，能够通过撬锁打开\n\n需要铜钥匙才能打开的宝箱。\n\n即使多次使用也不会坏。" } },
                { "Unlock_Picking_Tier2",                     new TechItemData { Name = "Lockpicking Tool v2",                NameCn = "高级开锁工具",         Description = "持有此道具时，能够通过撬锁打开\n\n需要银钥匙才能打开的宝箱。\n\n即使多次使用也不会坏。" } },
                { "Unlock_Picking_Tier3",                     new TechItemData { Name = "Lockpicking Tool v3",                NameCn = "专家开锁工具",         Description = "持有此道具时，能够通过撬锁打开\n\n需要金钥匙才能打开的宝箱。\n\n即使多次使用也不会坏。" } },
                { "WallSignboard",                            new TechItemData { Name = "Wall-Mounted Sign",                  NameCn = "壁挂看板",             Description = "可以输入文字的告示牌。\n\n可以悬挂在墙壁上。" } },
                { "WallTorch",                                new TechItemData { Name = "Wall Torch",                         NameCn = "壁挂火把",             Description = "可在黑夜照亮据点的光源。\n\n可设置在墙壁上。\n\n需要火系帕鲁点火。" } },
                { "WeaponFactory_Dirty_04",                   new TechItemData { Name = "Advanced Weapon Assembly Line",      NameCn = "高等文明武器工厂",     Description = "用于生产武器及弹药的工厂。\n\n引入了高性能机器，可以快速制作武器。\n\n需有能进行手工作业的帕鲁。" } },
                { "Wood_Gate",                                new TechItemData { Name = "Wooden Gate",                        NameCn = "木制大门",             Description = "稍大体型的帕鲁也能通过的大门。\n\n以木头制成，相当脆弱。" } },
                { "Wooden_ladder",                            new TechItemData { Name = "Ladder",                             NameCn = "梯子",                 Description = "可以用来爬上高处的梯子。" } },
                { "WorkSpeedIncrease1",                       new TechItemData { Name = "Beta Wave Generator",                NameCn = "β波发生器",           Description = "发出β波让帕鲁活动更活跃的装置。\n\n提升据点内帕鲁的工作速度。\n\n即使配置复数个，其效果也不会叠加。" } },
                { "Workbench",                                new TechItemData { Name = "Primitive Workbench",                NameCn = "原始的作业台",         Description = "用于制作简单物品的原始的作业台。\n\n需有能进行手工作业的帕鲁。" } },
            };
        }

        public class TechItemData
        {
            public string Name { get; set; } = "";
            public string NameCn { get; set; } = "";
            public string Description { get; set; } = "";
        }

        #endregion
    }
}