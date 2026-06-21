using ModernWpf.Controls;
using PalworldServerManager;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace PalworldServerManager
{
    /// <summary>
    /// ModManagerWindows.xaml 的交互逻辑
    /// </summary>
    public partial class ModManagerWindows : Window
    {
        MainWindow mainWindow = Application.Current.MainWindow as MainWindow;

        private const int AppId = 2646460;
        private static readonly HttpClient _http = new HttpClient();
        private List<SteamMod> _allMods = new List<SteamMod>();

        private int _currentPage = 1;
        private int _pageSize = 30; 
        private int _totalPage = 1;

        private string _currentSearchText = "";
        private bool _isSearchMode = false;

        MainSettings SsmSettings = new();
        SteamModsList SteamMods = new();

        // 拖动排序相关字段
        private InstalledModViewModel _draggedMod;
        private Point _dragStartPoint;
        private bool _isDragging = false;
        private int _originalIndex = -1;
        private int _lastPlaceholderIndex = -1;
        private int _finalInsertIndex = -1;
        private InstalledModViewModel _placeholderMod;

        private Dictionary<string, string> _modNameCache = new Dictionary<string, string>();

        public ModManagerWindows(MainSettings mainSettings)
        {
            SsmSettings = mainSettings;
            InitializeComponent();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            SteamServerComboBox.DataContext = SsmSettings;

            if (SsmSettings.Servers.Count > 0)
            {
                SteamServerComboBox.SelectedIndex = 0;
            }
            Loaded += async (s, e) => await LoadPageAsync(1);
        }

        private void DgMods_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgMods.SelectedItem is SteamMod mod)
            {
                try { ModImage.Source = new BitmapImage(new Uri(mod.PreviewImage)); }
                catch { ModImage.Source = null; }
            }
        }

        private async void ServerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Server server = (Server)SteamServerComboBox.SelectedItem;

            if (server.SubscribedMods.Count > 0)
            {
                for (int i = 0; i < server.SubscribedMods.Count; i++)
                {
                    foreach (SteamMod mod in SteamMods.ModList)
                    {
                        if (server.SubscribedMods.Contains(mod.Id))
                            mod.IsChecked = true;
                        else
                            mod.IsChecked = false;
                    }
                }
                RefreshInstalledModGridAsync();
            }
            else
            {
                foreach (SteamMod mod in SteamMods.ModList)
                    mod.IsChecked = false;
            }
            await LoadPageAsync(1);
        }

        private async void ModSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                await SteamSearchAsync(ModSearchBox.Text);
            }
        }

        private async Task SteamSearchAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                _isSearchMode = false;
                _currentSearchText = "";
                await LoadPageAsync(1);
                return;
            }

            _isSearchMode = true;
            _currentSearchText = keyword.Trim();

            await LoadPageAsync(1);
        }

        public static List<SteamMod> ParseMods(string html)
        {
            var mods = new List<SteamMod>();

            var itemMatches = Regex.Matches(html,
                @"<div.*?class=""workshopItem"">(.*?)</script>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            foreach (Match match in itemMatches)
            {
                string itemHtml = match.Groups[1].Value;
                var mod = new SteamMod();

                var idMatch = Regex.Match(itemHtml, @"data-publishedfileid=""(\d+)""");
                if (idMatch.Success) mod.Id = idMatch.Groups[1].Value;

                var imgMatch = Regex.Match(itemHtml, @"<img class=""workshopItemPreviewImage.*?src=""(.*?)""");
                if (imgMatch.Success) mod.PreviewImage = imgMatch.Groups[1].Value;

                var titleMatch = Regex.Match(itemHtml, @"<div class=""workshopItemTitle.*?"">(.*?)</div>");
                if (titleMatch.Success)
                {
                    mod.Title = HttpUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());
                }

                var authorMatch = Regex.Match(itemHtml, @"by&nbsp;<a class=""workshop_author_link"" href=""(.*?)"">(.*?)</a>");
                if (authorMatch.Success)
                {
                    mod.AuthorUrl = authorMatch.Groups[1].Value;
                    mod.Author = HttpUtility.HtmlDecode(authorMatch.Groups[2].Value.Trim());
                }

                var scriptMatch = Regex.Match(itemHtml, @"SharedFileBindMouseHover.*?description"":""(.*?)"",""user_subscribed""");
                if (scriptMatch.Success)
                {
                    string desc = scriptMatch.Groups[1].Value;
                    desc = Regex.Unescape(desc);          
                    desc = HttpUtility.HtmlDecode(desc);  
                    desc = desc.Replace("\\\"", "\"");    
                    mod.Description = $"    {desc}";
                }

                var appIdMatch = Regex.Match(itemHtml, @"data-appid=""(\d+)""");
                if (appIdMatch.Success) mod.AppId = appIdMatch.Groups[1].Value;

                if (!string.IsNullOrEmpty(mod.Id))
                    mods.Add(mod);
            }
            return mods;
        }

        public static List<SteamMod> ParseAuthorMods(string html)
        {
            var mods = new List<SteamMod>();

            string authorName = "未知作者";
            var authorMatch = Regex.Match(
                html,
                @"<span id=""HeaderUserInfoName"">.*?<a[^>]*>(.*?)</a>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (authorMatch.Success)
            {
                authorName = authorMatch.Groups[1].Value.Trim();
            }

            var itemMatches = Regex.Matches(html,
                @"<div.*?class=""workshopItem"">(.*?)</script>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            foreach (Match match in itemMatches)
            {
                string itemHtml = match.Groups[1].Value;
                var mod = new SteamMod();

                var idMatch = Regex.Match(itemHtml, @"data-publishedfileid=""(\d+)""");
                if (idMatch.Success) 
                    mod.Id = idMatch.Groups[1].Value;

                var imgMatch = Regex.Match(itemHtml, @"<img class=""workshopItemPreviewImage.*?src=""(.*?)""");
                if (imgMatch.Success) 
                    mod.PreviewImage = imgMatch.Groups[1].Value;

                var titleMatch = Regex.Match(itemHtml, @"<div class=""workshopItemTitle.*?"">(.*?)</div>");
                if (titleMatch.Success)
                {
                    mod.Title = HttpUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());
                }

                mod.Author = authorName;

                var scriptMatch = Regex.Match(itemHtml, @"SharedFileBindMouseHover.*?description"":""(.*?)"",""user_subscribed""");
                if (scriptMatch.Success)
                {
                    string desc = scriptMatch.Groups[1].Value;
                    desc = Regex.Unescape(desc);          
                    desc = HttpUtility.HtmlDecode(desc);  
                    desc = desc.Replace("\\\"", "\"");    
                    mod.Description = $"    {desc}";
                }

                var appIdMatch = Regex.Match(itemHtml, @"data-appid=""(\d+)""");
                if (appIdMatch.Success) mod.AppId = appIdMatch.Groups[1].Value;

                if (!string.IsNullOrEmpty(mod.Id))
                    mods.Add(mod);
            }
            return mods;
        }

        private async Task LoadPageAsync(int page)
        {
            btnPrev.IsEnabled = false;
            btnNext.IsEnabled = false;
            txtStatus.Text = $"正在加载第 {page} 页...";

            try
            {
                string url;
                if (_isSearchMode && !string.IsNullOrWhiteSpace(_currentSearchText))
                {
                    string searchText = Uri.EscapeDataString(_currentSearchText);
                    //url = $"https://steamcommunity.com/workshop/browse/?appid={AppId}&searchtext={searchText}&actualsort=textsearch&p={page}&numperpage=30";
                    url = $"https://steamcommunity.com/workshop/browse/?appid={AppId}&searchtext={searchText}&childpublishedfileid=0&browsesort=textsearch&section=readytouseitems&created_date_range_filter_start=0&created_date_range_filter_end=0&updated_date_range_filter_start=0&updated_date_range_filter_end=0";
                    
                }
                else
                    url = $"https://steamcommunity.com/workshop/browse/?appid={AppId}&browsesort=trend&section=&actualsort=trend&p={page}&days=-1";

                string html = await _http.GetStringAsync(url);
                var mods = ParseMods(html);
                dgMods.ItemsSource = mods;

                _totalPage = ParseTotalPageCount(html);
                _currentPage = page;

                txtPageInfo.Text = $"第 {_currentPage} 页 / 共 {_totalPage} 页";
                txtStatus.Text = $"加载完成：第 {page} 页，共 {mods.Count} 个Mod";

                AutoCheckSubscribedMods(mods);
                RefreshInstalledModGridAsync();
            }
            catch (Exception ex)
            {
                txtStatus.Text = "加载失败";
                MessageBox.Show($"加载第 {page} 页失败：{ex.Message}");
            }
            finally
            {
                btnPrev.IsEnabled = _currentPage > 1;
                btnNext.IsEnabled = _currentPage < _totalPage;
            }
        }

        private async Task LoadSearchPageAsync(int page)
        {
            btnPrev.IsEnabled = false;
            btnNext.IsEnabled = false;
            txtStatus.Text = $"正在加载第 {page} 页...";

            try
            {
                string url = $"https://steamcommunity.com/workshop/browse/?appid={AppId}&browsesort=trend&section=&actualsort=trend&p={page}&days=-1";
                string html = await _http.GetStringAsync(url);

                var mods = ParseMods(html);
                dgMods.ItemsSource = mods;

                _totalPage = ParseTotalPageCount(html);
                _currentPage = page;

                txtPageInfo.Text = $"第 {_currentPage} 页 / 共 {_totalPage} 页";
                txtStatus.Text = $"第 {page} 页 {mods.Count} 个Mod";

                AutoCheckSubscribedMods(mods);
            }
            catch (Exception ex)
            {
                txtStatus.Text = "加载失败";
                MessageBox.Show($"加载第 {page} 页失败：{ex.Message}");
            }
            finally
            {
                btnPrev.IsEnabled = _currentPage > 1;
                btnNext.IsEnabled = _currentPage < _totalPage;
            }
        }

        private void AutoCheckSubscribedMods(IEnumerable<SteamMod> currentMods)
        {
            //Server server = SteamServerComboBox.SelectedItem as Server;
            //if (server == null || server.SubscribedMods == null) return;

            //ServerManagementSettings serverSettings = ServerSettingsEditor.LoadServerHostSettings(Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json"));

            //HashSet<string> installed = new HashSet<string>(server.SubscribedMods);
            //if (!string.IsNullOrWhiteSpace(serverSettings.Mods))
            //{
            //    var modsFromSettings = serverSettings.Mods.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            //    foreach (var modId in modsFromSettings)
            //    {
            //        string id = modId.Trim();

            //        if (!string.IsNullOrEmpty(id) && !server.SubscribedMods.Contains(id))
            //        {
            //            server.SubscribedMods.Add(id);
            //        }
            //    }
            //}

            //foreach (var mod in currentMods)
            //{
            //    mod.IsChecked = installed.Contains(mod.Id);
            //}
        }

        private int ParseTotalPageCount(string html)
        {
            try
            {
                var matches = Regex.Matches(html, @"<a class=""pagelink""[^>]*?p=(\d+)[^>]*?>(\d+)</a>");

                int maxPage = 1;
                foreach (Match m in matches)
                {
                    if (int.TryParse(m.Groups[1].Value, out int page))
                    {
                        if (page > maxPage) maxPage = page;
                    }
                }

                return maxPage;
            }
            catch
            {
                return 1;
            }
        }

        private void SafeCopyToClipboard(string text)
        {
            try
            {
                System.Windows.IDataObject data = new DataObject(DataFormats.UnicodeText, text);
                System.Windows.Clipboard.SetDataObject(data, copy: true);
                return;
            }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == -2147221040) 
            {
                //System.Threading.Thread.Sleep(50);
            }
        }

        private async void RefreshInstalledModGridAsync()
        {
            Server server = SteamServerComboBox.SelectedItem as Server;
            if (server == null || server.SubscribedMods == null)
            {
                installedMods.ItemsSource = new ObservableCollection<InstalledModViewModel>();
                return;
            }

            var modIds = server.SubscribedMods.ToList();
            await FetchModNamesAsync(modIds);

            var observableList = new ObservableCollection<InstalledModViewModel>();

            foreach (var id in modIds)
            {
                observableList.Add(new InstalledModViewModel
                {
                    Id = id,
                    Title = _modNameCache.TryGetValue(id, out var name) ? name : $"MOD {id}"
                });
            }

            InstallModsText.Text = $"服务器已订阅Mods：{modIds.Count.ToString()} 个";
            installedMods.ItemsSource = observableList;
        }

        private async Task FetchModNamesAsync(List<string> ids)
        {
            if (ids == null || ids.Count == 0)
                return;

            var needFetch = ids.Where(id => !_modNameCache.ContainsKey(id)).ToList();
            if (needFetch.Count == 0)
                return;

            try
            {
                var formData = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("itemcount", needFetch.Count.ToString())
                };
                for (int i = 0; i < needFetch.Count; i++)
                {
                    formData.Add(new KeyValuePair<string, string>($"publishedfileids[{i}]", needFetch[i]));
                }

                var response = await _http.PostAsync(
                    "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
                    new FormUrlEncodedContent(formData));

                if (!response.IsSuccessStatusCode)
                    return;

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                //if (!File.Exists("./SubscribedMods.json"))
                //    File.Create("./SubscribedMods.json");

                //File.WriteAllText("./SubscribedMods.json", json);

                var details = doc.RootElement
                    .GetProperty("response")
                    .GetProperty("publishedfiledetails");

                foreach (var d in details.EnumerateArray())
                {
                    if (!d.TryGetProperty("publishedfileid", out var idProp)) 
                        continue;
                    string id = idProp.GetString() ?? "";
                    if (string.IsNullOrEmpty(id)) 
                        continue;

                    if (d.TryGetProperty("title", out var titleProp))
                        _modNameCache[id] = titleProp.GetString() ?? "Unknown";
                    else
                        _modNameCache[id] = "Unknown";

                    //if (d.TryGetProperty("time_updated", out var timeProp))
                    //    _modSteamTimestamp[id] = timeProp.GetInt64();
                }
                return;
            }
            catch
            {
                foreach (var id in needFetch)
                {
                    if (!_modNameCache.ContainsKey(id))
                        _modNameCache[id] = "Unknown";
                }
            }
        }

        private void InstalledMods_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _draggedMod = GetDataGridItemFromPoint(e.GetPosition(installedMods)) as InstalledModViewModel;

            if (_draggedMod != null && _draggedMod.IsPlaceholder)
                _draggedMod = null;
        }

        private void InstalledMods_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed &&
                _draggedMod != null &&
                !_isDragging &&
                installedMods.ItemsSource is ObservableCollection<InstalledModViewModel> mods)
            {
                Point currentPos = e.GetPosition(null);
                Vector diff = _dragStartPoint - currentPos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    _originalIndex = mods.IndexOf(_draggedMod);

                    if (_originalIndex != -1)
                    {
                        mods.RemoveAt(_originalIndex);
                        DragDrop.DoDragDrop(installedMods, _draggedMod, DragDropEffects.Move);
                    }
                }
            }
        }

        private void InstalledMods_DragOver(object sender, DragEventArgs e)
        {
            if (_isDragging &&
                e.Data.GetDataPresent(typeof(InstalledModViewModel)) &&
                installedMods.ItemsSource is ObservableCollection<InstalledModViewModel> mods)
            {
                Point point = e.GetPosition(installedMods);
                InstalledModViewModel targetMod = GetDataGridItemFromPoint(point) as InstalledModViewModel;

                int insertIndex = 0;

                if (targetMod != null)
                {
                    insertIndex = mods.IndexOf(targetMod);
                    DataGridRow row = GetDataGridRowFromPoint(point);
                    if (row != null)
                    {
                        Point rowPoint = e.GetPosition(row);
                        if (rowPoint.Y > row.ActualHeight)
                        {
                            insertIndex++;
                        }
                    }
                }
                else if(point.Y < 5)
                {
                    //insertIndex = 0;
                }
                else
                {
                    //insertIndex = mods.Count;
                }

                _finalInsertIndex = insertIndex;

                UpdatePlaceholder(insertIndex, mods);
            }
        }

        private void InstalledMods_Drop(object sender, DragEventArgs e)
        {
            if (!_isDragging) return;

            try
            {
                if (e.Data.GetDataPresent(typeof(InstalledModViewModel)) &&
                    installedMods.ItemsSource is ObservableCollection<InstalledModViewModel> mods)
                {
                    RemovePlaceholder(mods);

                    InstalledModViewModel droppedMod = e.Data.GetData(typeof(InstalledModViewModel)) as InstalledModViewModel;

                    if (droppedMod != null && droppedMod == _draggedMod)
                    {
                        int newIndex = _finalInsertIndex;

                        if (newIndex < 0 || newIndex > mods.Count)
                        {
                            newIndex = _originalIndex;
                        }

                        newIndex = Math.Max(0, Math.Min(newIndex, mods.Count));
                        mods.Insert(newIndex, droppedMod);
                    }
                    else
                    {
                        //if (_originalIndex != -1 && _draggedMod != null)
                        //{
                        //    _originalIndex = Math.Max(0, Math.Min(_originalIndex, mods.Count));
                        //    mods.Insert(_originalIndex, _draggedMod);
                        //}
                    }
                }
            }
            finally
            {
                // 重置所有
                _isDragging = false;
                _draggedMod = null;
                _originalIndex = -1;
                _finalInsertIndex = -1;
            }
        }

        private void UpdatePlaceholder(int insertIndex, ObservableCollection<InstalledModViewModel> mods)
        {
            RemovePlaceholder(mods);

            if (_draggedMod == null) return;

            _placeholderMod = new InstalledModViewModel
            {
                Id = _draggedMod.Id,
                Title = _draggedMod.Title,
                IsPlaceholder = true
            };

            insertIndex = Math.Max(0, Math.Min(insertIndex, mods.Count));
            mods.Insert(insertIndex, _placeholderMod);
            _lastPlaceholderIndex = insertIndex;
        }

        private void RemovePlaceholder(ObservableCollection<InstalledModViewModel> mods)
        {
            if (_placeholderMod != null && mods.Contains(_placeholderMod))
            {
                mods.Remove(_placeholderMod);
            }
            _placeholderMod = null;
            _lastPlaceholderIndex = -1;
        }

        private object GetDataGridItemFromPoint(Point point)
        {
            HitTestResult hit = VisualTreeHelper.HitTest(installedMods, point);
            if (hit == null) return null;

            DependencyObject obj = hit.VisualHit;
            while (obj != null && !(obj is DataGridRow))
            {
                obj = VisualTreeHelper.GetParent(obj);
            }

            return (obj as DataGridRow)?.Item;
        }

        private DataGridRow GetDataGridRowFromPoint(Point point)
        {
            HitTestResult hit = VisualTreeHelper.HitTest(installedMods, point);
            if (hit == null) return null;

            DependencyObject obj = hit.VisualHit;
            while (obj != null && !(obj is DataGridRow))
            {
                obj = VisualTreeHelper.GetParent(obj);
            }

            return obj as DataGridRow;
        }

        private void SaveModsList(Server server)
        {
            //ServerSettings serverSettings = ServerSettingsEditor.LoadServerSettings(Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json"));

            //var currentPageMods = dgMods.ItemsSource as IEnumerable<SteamMod>;
            //if (currentPageMods == null)
            //{
            //    MessageBox.Show("没有可保存的MOD");
            //    return;
            //}

            //var currentInstalledMods = installedMods.ItemsSource as ObservableCollection<InstalledModViewModel>
            //                            ?? new ObservableCollection<InstalledModViewModel>();

            //Dictionary<string, SteamMod> currentPageModDict = currentPageMods.ToDictionary(m => m.Id);
            //List<string> finalOrderedIds = new List<string>();

            //foreach (var installedMod in currentInstalledMods)
            //{
            //    if (installedMod.IsMarkedForDelete) continue;

            //    if (currentPageModDict.TryGetValue(installedMod.Id, out var correspondingMod))
            //    {
            //        if (correspondingMod.IsChecked)
            //            finalOrderedIds.Add(installedMod.Id);
            //    }
            //    else
            //        finalOrderedIds.Add(installedMod.Id);
            //}

            //foreach (var installedMod in currentInstalledMods)
            //{
            //    if (currentPageModDict.TryGetValue(installedMod.Id, out var correspondingMod))
            //    {
            //        if (correspondingMod.IsChecked)
            //        {
            //            finalOrderedIds.Add(installedMod.Id);
            //        }
            //    }
            //    else
            //    {
            //        finalOrderedIds.Add(installedMod.Id);
            //    }
            //}

            //HashSet<string> alreadyAdded = new HashSet<string>(finalOrderedIds);
            //foreach (var mod in currentPageMods)
            //{
            //    if (mod.IsChecked && !alreadyAdded.Contains(mod.Id))
            //        finalOrderedIds.Add(mod.Id);
            //}

            //server.SubscribedMods.Clear();
            //server.SubscribedMods.AddRange(finalOrderedIds);
            //serverSettings.Mods = string.Join(",", server.SubscribedMods);
            //ServerSettingsEditor.SaveServerSettings(server, serverSettings);
            //MainSettings.Save(SsmSettings);
        }

        private async void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            _isSearchMode = false;
            _currentSearchText = "";
            ModSearchBox.Text = ""; 
            await LoadPageAsync(1);
        }
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            Server server = (Server)SteamServerComboBox.SelectedItem;
            SaveModsList(server);
            RefreshInstalledModGridAsync();
        }

        private async void UnInstallAllMod_Click(object sender, RoutedEventArgs e)
        {
            //Server server = (Server)SteamServerComboBox.SelectedItem;
            //ServerSettings serverSettings = ServerSettingsEditor.LoadServerSettings(Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json"));

            //if (server.SubscribedMods.Count != 0)
            //{
            //    var contentDialog = new ContentDialog()
            //    {
            //        Title = "警告",
            //        Content = "确定取消订阅所有MOD？此操作可能导致服务器有关Mod的物品出错，请谨慎选择！",
            //        PrimaryButtonText = "确定",
            //        SecondaryButtonText = "取消"
            //    };

            //    if (await contentDialog.ShowAsync() is ContentDialogResult.Primary)
            //    {
            //        server.SubscribedMods.Clear();
            //        serverSettings.Mods = "";

            //        if (dgMods.ItemsSource is IEnumerable<SteamMod> currentMods)
            //        {
            //            foreach (var mod in currentMods)
            //            {
            //                mod.IsChecked = false;
            //            }
            //        }
            //        ServerSettingsEditor.SaveServerSettings(server, serverSettings);
            //        MainSettings.Save(SsmSettings);
            //        RefreshInstalledModGridAsync();

            //    }
            //    return;
            //}
            //else
            //{
            //    var contentDialog = new ContentDialog()
            //    {
            //        Title = "提示",
            //        Content = "没有可取消订阅的Mod！",
            //        PrimaryButtonText = "确定",
            //        SecondaryButtonText = "取消"
            //    }.ShowAsync();
            //}
        }

        private void BtnAddManualModId_Click(object sender, RoutedEventArgs e)
        {
            string modId = TxtManualModId.Text.Trim();
            if (string.IsNullOrEmpty(modId) || !long.TryParse(modId, out _)) return;

            Server server = SteamServerComboBox.SelectedItem as Server;
            if (server == null) return;

            if (server.SubscribedMods.Contains(modId)) return;

            if (dgMods.ItemsSource is IEnumerable<SteamMod> modList)
            {
                var findMod = modList.FirstOrDefault(m => m.Id == modId);
                if (findMod != null)
                {
                    findMod.IsChecked = true;
                    dgMods.Items.Refresh();
                }
            }

            server.SubscribedMods.Add(modId);
            TxtManualModId.Clear();
            RefreshInstalledModGridAsync();
        }

        private async void SearchIcon_Click(object sender, RoutedEventArgs e)
        {
            await SteamSearchAsync(ModSearchBox.Text);
        }

        private void Hyperlink_RequestNavigate_Click(object sender, RequestNavigateEventArgs e)
        {
            Hyperlink hyperlink = (Hyperlink)sender;
            Process.Start(new ProcessStartInfo { FileName = hyperlink.NavigateUri.ToString(), UseShellExecute = true });
        }

        private void OpenWorkshopPage_Click(object sender, RoutedEventArgs e)
        {
            if (dgMods.SelectedItem is SteamMod mod)
            {
                Process.Start(new ProcessStartInfo { FileName = "https://steamcommunity.com/sharedfiles/filedetails/?id=" + mod.Id, UseShellExecute = true });
            }
        }
        
        // 使用反射获取名字和id，tag和item的对应属性要一样
        private void CopyCommon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem) return;
            string propName = menuItem.Tag as string;

            if (menuItem.Parent is not ContextMenu ctx || ctx.PlacementTarget is not DataGrid dg)
                return;

            var item = dg.SelectedItem;
            if (item == null || string.IsNullOrEmpty(propName)) return;

            var prop = item.GetType().GetProperty(propName);
            if (prop == null) return;

            string text = prop.GetValue(item)?.ToString() ?? "";

            if (!string.IsNullOrWhiteSpace(text))
                SafeCopyToClipboard(text);
        }

        private void DeleteCommon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem) return;
            if (menuItem.Parent is not ContextMenu ctx || ctx.PlacementTarget is not DataGrid dg)
                return;

            var selectedMod = dg.SelectedItem as InstalledModViewModel;
            if (selectedMod == null) return;

            selectedMod.IsMarkedForDelete = true;
            dg.Items.Refresh();

            if (dgMods.ItemsSource is IEnumerable<SteamMod> allPageMods)
            {
                var modInList = allPageMods.FirstOrDefault(m => m.Id == selectedMod.Id);
                if (modInList != null)
                {
                    modInList.IsChecked = false;
                    dgMods.Items.Refresh();
                }
            }

        }

        private async void ShowOnlyThisAuthor_Click(object sender, RoutedEventArgs e)
        {
            if (dgMods.SelectedItem is not SteamMod mod) return;
            if (string.IsNullOrWhiteSpace(mod.AuthorUrl)) return;

            string authorUrl = $"{mod.AuthorUrl}&p=1&numperpage=30";

            txtStatus.Text = $"正在加载 {mod.Author} 的所有MOD...";

            try
            {
                string html = await _http.GetStringAsync(authorUrl);

                var mods = ParseAuthorMods(html);
                dgMods.ItemsSource = mods;

                AutoCheckSubscribedMods(mods);

                txtStatus.Text = $"已加载 {mod.Author} 的 {mods.Count} 个MOD";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载失败：{ex.Message}");
            }
            finally
            {
            }
        }

        private async void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                await LoadPageAsync(_currentPage - 1);
            }
        }

        private async void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPage)
            {
                await LoadPageAsync(_currentPage + 1);
            }
        }
    }
}
