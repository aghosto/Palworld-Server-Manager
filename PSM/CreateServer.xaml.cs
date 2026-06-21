using ModernWpf.Controls;
using PalworldServerManager;
using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Forms;

namespace PalworldServerManager
{
    /// <summary>
    /// Interaction logic for CreateServer.xaml
    /// </summary>
    public partial class CreateServer : Window
    {
        Server newServer = new Server();
        MainSettings settings;

        public JsonSerializerOptions serializerOptions = new JsonSerializerOptions { WriteIndented = true };

        public CreateServer(MainSettings mainSettings)
        {
            InitializeComponent();
            settings = mainSettings;

            DataContext = newServer;
        }

        private void ServerPathButton_Click(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                RootFolder = Environment.SpecialFolder.Desktop,
                SelectedPath = newServer.Path
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && dialog.SelectedPath != "")
            {
                newServer.Path = dialog.SelectedPath;
            }
        }

        private async void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            string newServerSettingsPath = Path.Combine(newServer.Path, "SaveData", "Settings");

            foreach (Server server in settings.Servers)
            {
                if (server.ssmServerName == newServer.ssmServerName)
                {
                    ContentDialog closeFileDialog = new()
                    {
                        Content = "已存在一个同名的服务器，请输入不同的服务器名！",
                        PrimaryButtonText = "是",
                    };
                    await closeFileDialog.ShowAsync();
                    return;
                }
            }

            if (!Directory.Exists(newServer.Path))
                Directory.CreateDirectory(newServer.Path);

            if (File.Exists(newServer.Path + @"\WSServer.exe"))
            {
                ContentDialog yesNoDialog = new()
                {
                    Content = "似乎已经有另外的服务器文件在此文件夹，建议选择另外的文件夹以避免不必要的错误。\r\r不再理会，继续操作？",
                    PrimaryButtonText = "是",
                    SecondaryButtonText = "否"
                };
                if (await yesNoDialog.ShowAsync() is ContentDialogResult.Secondary)
                    return;
            }

            if (!Directory.Exists(newServerSettingsPath))
                Directory.CreateDirectory(newServerSettingsPath);

            if (!File.Exists(Path.Combine(newServerSettingsPath, "ServerSettings.json")))
                UnifiedSettingsEditor.CreateDefaultSettingsFile(newServer);

            settings.Servers.Add(newServer);
            MainSettings.Save(settings);
            Close();
        }

        private void DirectoryCopy(string sourceDir, string destDir, bool copySubDirs)
        {
            DirectoryInfo dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists) return;

            DirectoryInfo[] dirs = dir.GetDirectories();
            Directory.CreateDirectory(destDir);

            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string path = Path.Combine(destDir, file.Name);
                file.CopyTo(path, true);
            }

            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string path = Path.Combine(destDir, subdir.Name);
                    DirectoryCopy(subdir.FullName, path, copySubDirs);
                }
            }
        }
    }
}
