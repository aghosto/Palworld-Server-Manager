using Microsoft.Win32;
using PalworldServerManager.Services;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PalworldServerManager
{
    public partial class SaveEditorWindow : Window
    {
        private readonly SaveToolsService _saveTools;

        public SaveEditorWindow()
        {
            InitializeComponent();
            _saveTools = new SaveToolsService();

            if (!_saveTools.IsAvailable)
            {
                StatusBarText.Text = "警告: 未检测到 Python，转换功能不可用";
                StatusBarText.Foreground = Brushes.Orange;
                ConvertToJsonButton.IsEnabled = false;
                ConvertToSavButton.IsEnabled = false;
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "存档文件 (*.sav;*.json)|*.sav;*.json|所有文件 (*.*)|*.*",
                Title = "选择存档文件"
            };

            if (dialog.ShowDialog() == true)
            {
                FilePathBox.Text = dialog.FileName;
                LoadFile(dialog.FileName);
            }
        }

        private async void ConvertToJson_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(FilePathBox.Text)) return;
            SetBusy(true);
            StatusBarText.Text = "正在转换 SAV → JSON...";

            var result = await _saveTools.ConvertSavToJson(FilePathBox.Text);

            if (result.Success)
            {
                var jsonPath = FilePathBox.Text + ".json";
                if (File.Exists(jsonPath))
                {
                    LoadFile(jsonPath);
                    StatusBarText.Text = $"转换成功: {jsonPath}";
                    StatusBarText.Foreground = Brushes.Green;
                }
                else
                {
                    StatusBarText.Text = "转换完成，但未找到输出文件";
                    StatusBarText.Foreground = Brushes.Orange;
                }
            }
            else
            {
                StatusBarText.Text = $"转换失败: {result.ErrorMessage}";
                StatusBarText.Foreground = Brushes.Red;
                MessageBox.Show(result.ErrorMessage, "转换失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            SetBusy(false);
        }

        private async void ConvertToSav_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(FilePathBox.Text)) return;
            SetBusy(true);
            StatusBarText.Text = "正在转换 JSON → SAV...";

            var result = await _saveTools.ConvertJsonToSav(FilePathBox.Text);

            if (result.Success)
            {
                var savPath = FilePathBox.Text.Replace(".json", "").Replace(".sav.json", ".sav");
                if (File.Exists(savPath))
                {
                    LoadFile(savPath);
                    StatusBarText.Text = $"转换成功: {savPath}";
                    StatusBarText.Foreground = Brushes.Green;
                }
                else
                {
                    StatusBarText.Text = "转换完成";
                    StatusBarText.Foreground = Brushes.Green;
                }
            }
            else
            {
                StatusBarText.Text = $"转换失败: {result.ErrorMessage}";
                StatusBarText.Foreground = Brushes.Red;
                MessageBox.Show(result.ErrorMessage, "转换失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            SetBusy(false);
        }

        private void OpenServerSaves_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择 Palworld 服务器存档目录 (通常为 .../WS/Saved/Worlds/Dedicated)",
                UseDescriptionForTitle = true
            };

            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow?.ServerTabControl?.SelectedItem is Server server && Directory.Exists(server.Path))
            {
                var defaultPath = Path.Combine(server.Path, "WS", "Saved", "Worlds", "Dedicated");
                if (Directory.Exists(defaultPath))
                    dialog.SelectedPath = defaultPath;
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var dir = dialog.SelectedPath;
                var savFiles = Directory.GetFiles(dir, "*.sav", SearchOption.AllDirectories);

                if (savFiles.Length == 0)
                {
                    var allFiles = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
                    savFiles = Array.FindAll(allFiles, f => f.EndsWith(".sav") || f.EndsWith(".json"));
                }

                if (savFiles.Length == 0)
                {
                    MessageBox.Show("该目录下未找到 .sav 或 .json 文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                ShowFilePicker(savFiles);
            }
        }

        private void ShowFilePicker(string[] files)
        {
            var pickDialog = new Window
            {
                Title = "选择存档文件",
                Width = 500,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };
            var stack = new StackPanel { Margin = new Thickness(12) };
            var listBox = new ListBox { Height = 280 };
            foreach (var f in files)
                listBox.Items.Add(f);
            stack.Children.Add(new Label { Content = "请选择要打开的存档文件：" });
            stack.Children.Add(listBox);
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var okBtn = new Button { Content = "打开", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancelBtn = new Button { Content = "取消", Width = 80, IsCancel = true };
            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            stack.Children.Add(btnPanel);
            pickDialog.Content = stack;

            okBtn.Click += (s, args) => { pickDialog.DialogResult = true; pickDialog.Close(); };
            cancelBtn.Click += (s, args) => { pickDialog.DialogResult = false; pickDialog.Close(); };

            if (pickDialog.ShowDialog() == true && listBox.SelectedItem != null)
            {
                var path = listBox.SelectedItem.ToString();
                FilePathBox.Text = path;
                LoadFile(path);
            }
        }

        private void LoadFile(string path)
        {
            try
            {
                if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var text = File.ReadAllText(path);
                    try
                    {
                        var doc = JsonDocument.Parse(text);
                        using var ms = new MemoryStream();
                        using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });
                        doc.WriteTo(writer);
                        writer.Flush();
                        ContentBox.Text = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                    }
                    catch
                    {
                        ContentBox.Text = text;
                    }

                    ConvertToSavButton.IsEnabled = _saveTools.IsAvailable;
                    ConvertToJsonButton.IsEnabled = false;
                }
                else if (path.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
                {
                    ContentBox.Text = $"SAV 文件: {path}\n\n{new FileInfo(path).Length:N0} bytes\n\n点击「转为 JSON」查看内容";
                    ConvertToJsonButton.IsEnabled = _saveTools.IsAvailable;
                    ConvertToSavButton.IsEnabled = false;
                }
                else
                {
                    ContentBox.Text = $"文件: {path}\n\n{new FileInfo(path).Length:N0} bytes";
                    ConvertToJsonButton.IsEnabled = false;
                    ConvertToSavButton.IsEnabled = false;
                }

                StatusText.Visibility = Visibility.Collapsed;
                StatusBarText.Text = $"已加载: {path}";
                StatusBarText.Foreground = Brushes.Gray;
            }
            catch (Exception ex)
            {
                ContentBox.Text = $"加载失败: {ex.Message}";
            }
        }

        private void SetBusy(bool busy)
        {
            IsEnabled = !busy;
            StatusBarText.Text = busy ? "处理中..." : "就绪";
        }
    }
}
