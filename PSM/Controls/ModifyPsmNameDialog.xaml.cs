using ModernWpf.Controls;
using System.Linq;
using System.Windows;

namespace PalworldServerManager.Controls
{
    public partial class ModifyPsmNameDialog : ContentDialog
    {
        private readonly Server _server;
        private readonly MainSettings _settings;

        public ModifyPsmNameDialog(Server server, MainSettings settings)
        {
            _server = server;
            _settings = settings;
            DataContext = server;
            InitializeComponent();
            ModifyName.Text = server.ssmServerName;
            ModifyName.SelectAll();
            ModifyName.Focus();
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            string newName = ModifyName.Text?.Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                args.Cancel = true;
                return;
            }

            string uniqueId = _server.UniqueId;
            if (_settings.Servers.Any(s => s.UniqueId != uniqueId && s.ssmServerName == newName))
            {
                DuplicateWarning.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }
            DuplicateWarning.Visibility = Visibility.Collapsed;

            Server target = _settings.Servers.FirstOrDefault(s => s.UniqueId == uniqueId);
            if (target != null)
                target.ssmServerName = newName;
            else
                _server.ssmServerName = newName;

            MainSettings.Save(_settings);
        }
    }
}
