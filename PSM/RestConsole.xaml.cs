using PalworldServerManager.REST;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;

namespace PalworldServerManager
{
    public partial class RestConsole : Window
    {
        private Server _server;

        public RestConsole(Server server)
        {
            InitializeComponent();
            _server = server;

            var settings = UnifiedSettingsEditor.LoadServerSettings(
                System.IO.Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json"));

            PortBox.Value = settings?.HostSettings?.RESTAPIPort ?? 8212;
            IpAddressBox.Text = server.RestServerSettings?.IPAddress ?? "127.0.0.1";

            Log("REST API 客户端已就绪，输入服务器地址后点击操作按钮即可调用。");
        }

        private void Log(string output)
        {
            Dispatcher.Invoke(() =>
            {
                RestConsoleOutput.AppendText("\r\r" + output);
                RestConsoleOutput.ScrollToEnd();
            });
        }

        private string GetAdminPassword()
        {
            var settings = UnifiedSettingsEditor.LoadServerSettings(
                System.IO.Path.Combine(_server.Path, "SaveData", "Settings", "ServerSettings.json"));
            return settings?.HostSettings?.AdminPassword ?? "";
        }

        private async Task<T?> CallApiAsync<T>(Func<RestApiClient, Task<T>> action)
        {
            string adminPassword = GetAdminPassword();
            if (string.IsNullOrEmpty(adminPassword))
            {
                Log("错误：未设置管理员密码，请在服务器设置中配置 AdminPassword。");
                return default;
            }

            using var client = new RestApiClient();
            client.SetAuth(IpAddressBox.Text, (int)PortBox.Value, adminPassword);

            try
            {
                return await action(client);
            }
            catch (HttpRequestException ex)
            {
                Log($"HTTP 请求失败: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                Log("请求超时，请检查服务器地址和端口是否正确。");
            }
            catch (Exception ex)
            {
                Log($"错误: {ex.Message}");
            }
            return default;
        }

        private async Task CallApiAsync(Func<RestApiClient, Task> action)
        {
            string adminPassword = GetAdminPassword();
            if (string.IsNullOrEmpty(adminPassword))
            {
                Log("错误：未设置管理员密码，请在服务器设置中配置 AdminPassword。");
                return;
            }

            using var client = new RestApiClient();
            client.SetAuth(IpAddressBox.Text, (int)PortBox.Value, adminPassword);

            try
            {
                await action(client);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Log("认证失败（401），请检查管理员密码是否正确。");
            }
            catch (HttpRequestException ex)
            {
                Log($"HTTP 请求失败: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                Log("请求超时，请检查服务器地址和端口是否正确。");
            }
            catch (Exception ex)
            {
                Log($"错误: {ex.Message}");
            }
        }

        private async void GetInfoButton_Click(object sender, RoutedEventArgs e)
        {
            var info = await CallApiAsync(client => client.GetServerInfoAsync());
            if (info != null)
                Log($"服务器信息：\r名称={info.ServerName}\r版本={info.Version}\r描述={info.Description}\rWorldGUID={info.WorldGuid}");
        }

        private async void GetPlayersButton_Click(object sender, RoutedEventArgs e)
        {
            var players = await CallApiAsync(client => client.GetPlayerListAsync());
            if (players?.Players != null)
            {
                Log($"在线玩家 ({players.Players.Count}):");
                foreach (var p in players.Players)
                    Log($"  {p.Name} (UID: {p.UserId} | PID: {p.PlayerId} | IP: {p.Ip} | Ping: {p.Ping} | 等级: {p.Level} | 建筑数: {p.BuildingCount})");
            }
        }

        private async void GetMetricsButton_Click(object sender, RoutedEventArgs e)
        {
            var metrics = await CallApiAsync(client => client.GetMetricsAsync());
            if (metrics != null)
                Log($"服务器指标：\rFPS={metrics.ServerFps}\r玩家={metrics.CurrentPlayerNum}/{metrics.MaxPlayerNum}\r帧时间={metrics.ServerFrameTime}ms\r运行时间={metrics.UpTime}s\r据点={metrics.BaseCampNum}\r游戏天数={metrics.Days}");
        }

        private async void GetSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = await CallApiAsync(client => client.GetServerSettingsAsync());
            if (!string.IsNullOrEmpty(settings))
                Log($"服务器设置:\r{settings}");
        }

        private async void SaveWorldButton_Click(object sender, RoutedEventArgs e)
        {
            await CallApiAsync(async client =>
            {
                bool ok = await client.SaveWorldAsync();
                Log(ok ? "世界已保存。" : "保存世界失败。");
            });
        }

        private async void ForceStopButton_Click(object sender, RoutedEventArgs e)
        {
            await CallApiAsync(async client =>
            {
                bool ok = await client.ForceStopAsync();
                Log(ok ? "服务器已强制停止。" : "强制停止失败。");
            });
        }

        private async void AnnounceButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(AnnounceTextBox.Text))
            {
                Log("请输入公告消息。");
                return;
            }
            await CallApiAsync(async client =>
            {
                bool ok = await client.AnnounceAsync(AnnounceTextBox.Text);
                Log(ok ? "公告已发送。" : "发送公告失败。");
                if (ok) AnnounceTextBox.Clear();
            });
        }

        private async void KickButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PlayerIdTextBox.Text))
            {
                Log("请输入玩家 UserId。");
                return;
            }
            await CallApiAsync(async client =>
            {
                string reason = ReasonTextBox.Text;
                bool ok = await client.KickPlayerAsync(PlayerIdTextBox.Text, string.IsNullOrEmpty(reason) ? null : reason);
                Log(ok ? $"玩家 {PlayerIdTextBox.Text} 已被踢出。" : "踢出玩家失败。");
            });
        }

        private async void BanButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PlayerIdTextBox.Text))
            {
                Log("请输入玩家 UserId。");
                return;
            }
            await CallApiAsync(async client =>
            {
                string reason = ReasonTextBox.Text;
                bool ok = await client.BanPlayerAsync(PlayerIdTextBox.Text, string.IsNullOrEmpty(reason) ? null : reason);
                Log(ok ? $"玩家 {PlayerIdTextBox.Text} 已被封禁。" : "封禁玩家失败。");
            });
        }

        private async void UnbanButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PlayerIdTextBox.Text))
            {
                Log("请输入玩家 UserId。");
                return;
            }
            await CallApiAsync(async client =>
            {
                bool ok = await client.UnbanPlayerAsync(PlayerIdTextBox.Text);
                Log(ok ? $"玩家 {PlayerIdTextBox.Text} 已被解封。" : "解封玩家失败。");
            });
        }

        private async void ShutdownButton_Click(object sender, RoutedEventArgs e)
        {
            await CallApiAsync(async client =>
            {
                int wait = (int)ShutdownWaitBox.Value;
                bool ok = await client.ShutdownAsync(wait);
                Log(ok ? $"服务器将在 {wait} 秒后关闭。" : "关闭服务器失败。");
            });
        }
    }
}
