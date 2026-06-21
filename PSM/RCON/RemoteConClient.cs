using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RCONServerLib.Utils;

namespace PalworldServerManager.RCON
{
    public class RemoteConClient
    {
        private const int CONNECT_TIMEOUT_MS = 5_000;
        private const int READ_TIMEOUT_MS = 1_00;

        public delegate void AuthEventHandler(bool success);
        public delegate void CommandResult(string result);
        public delegate void ConnectionEventHandler(ConnectionStateChange type);
        public delegate void LogEventHandler(string message);

        public enum ConnectionStateChange
        {
            Connected,
            Disconnected,
            NoConnection,
            ConnectionTimeout,
            ConnectionLost
        }

        private const int MaxAllowedPacketSize = 4096;
        private readonly Dictionary<int, CommandResult> _requestedCommands;
        private byte[] _buffer;
        private TcpClient _client;
        private NetworkStream _ns;
        private int _packetId;
        public bool Authenticated;

        private static readonly HttpClient _httpClient = new HttpClient();

        public RemoteConClient()
        {
            _client = new TcpClient();
            _packetId = 0;
            _requestedCommands = new Dictionary<int, CommandResult>();
            UseUtf8 = true;
        }

        public bool Connected => _client.Connected;
        public bool UseUtf8 { get; set; }

        public event AuthEventHandler OnAuthResult;
        public event LogEventHandler OnLog;
        public event ConnectionEventHandler OnConnectionStateChange;

        #region REST API Methods

        private static string GetBaseUrl(string host, int port) => $"http://{host}:{port}";

        private static StringContent JsonContent(object data) =>
            new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");

        private static async Task<T?> GetAsync<T>(string host, int port, string endpoint, string password)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, GetBaseUrl(host, port) + endpoint);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"admin:{password}")));

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<T>(json);
            }
            catch
            {
                return default;
            }
        }

        private static async Task<bool> PostAsync(string host, int port, string endpoint, object? body, string password)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, GetBaseUrl(host, port) + endpoint);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"admin:{password}")));

                if (body != null)
                    request.Content = JsonContent(body);

                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>GET /info - 获取服务器信息</summary>
        public static async Task<ServerInfo?> GetServerInfoAsync(string host, int port, string password)
        {
            return await GetAsync<ServerInfo>(host, port, "/info", password);
        }

        /// <summary>GET /players - 获取玩家列表</summary>
        public static async Task<PlayerListResponse?> GetPlayersRestAsync(string host, int port, string password)
        {
            return await GetAsync<PlayerListResponse>(host, port, "/players", password);
        }

        /// <summary>POST /announce - 发送公告消息</summary>
        public static Task<bool> AnnounceAsync(string host, int port, string password, string message)
        {
            return PostAsync(host, port, "/announce", new { message }, password);
        }

        /// <summary>POST /kick - 踢出玩家</summary>
        public static Task<bool> KickPlayerRestAsync(string host, int port, string password, string userId, string? reason = null)
        {
            var body = new Dictionary<string, object> { ["userid"] = userId };
            if (!string.IsNullOrEmpty(reason))
                body["message"] = reason;
            return PostAsync(host, port, "/kick", body, password);
        }

        /// <summary>POST /ban - 封禁玩家</summary>
        public static Task<bool> BanPlayerRestAsync(string host, int port, string password, string userId, string? reason = null)
        {
            var body = new Dictionary<string, object> { ["userid"] = userId };
            if (!string.IsNullOrEmpty(reason))
                body["message"] = reason;
            return PostAsync(host, port, "/ban", body, password);
        }

        /// <summary>POST /unban - 解封玩家</summary>
        public static Task<bool> UnbanPlayerRestAsync(string host, int port, string password, string userId)
        {
            return PostAsync(host, port, "/unban", new { userid = userId }, password);
        }

        /// <summary>POST /save - 保存世界</summary>
        public static Task<bool> SaveWorldRestAsync(string host, int port, string password)
        {
            return PostAsync(host, port, "/save", null, password);
        }

        /// <summary>POST /shutdown - 关闭服务器</summary>
        public static Task<bool> ShutdownRestAsync(string host, int port, string password, int waittime, string? message = null)
        {
            var body = new Dictionary<string, object> { ["waittime"] = waittime };
            if (!string.IsNullOrEmpty(message))
                body["message"] = message;
            return PostAsync(host, port, "/shutdown", body, password);
        }

        /// <summary>POST /stop - 强制停止服务器</summary>
        public static Task<bool> ForceStopRestAsync(string host, int port, string password)
        {
            return PostAsync(host, port, "/stop", null, password);
        }

        /// <summary>GET /metrics - 获取服务器指标</summary>
        public static async Task<ServerMetrics?> GetMetricsAsync(string host, int port, string password)
        {
            return await GetAsync<ServerMetrics>(host, port, "/metrics", password);
        }

        #endregion

        #region EchoPort Legacy Methods

        public async Task<List<PlayerDisplayInfo>?> GetPlayersAsync(string host, int port)
        {
            var response = await ExecuteAsync(host, port, "lp");
            return response == null ? null : ParsePlayers(response);
        }

        public Task<bool> SaveWorldAsync(string host, int port) =>
            ExecuteAndCheck(host, port, "SaveWorld 0");

        public Task<bool> ShutdownAsync(string host, int port, int delaySeconds = 10) =>
            ExecuteAndCheck(host, port, $"shutdown {delaySeconds}");

        public Task<bool> CancelShutdownAsync(string host, int port) =>
            ExecuteAndCheck(host, port, "cc");

        public Task<bool> BanPlayerAsync(string host, int port, string steamId) =>
            ExecuteAndCheck(host, port, $"usp 1 1 {steamId}");

        public Task<bool> UnbanPlayerAsync(string host, int port, string steamId) =>
            ExecuteAndCheck(host, port, $"usp 1 0 {steamId}");

        public Task<bool> MutePlayerAsync(string host, int port, string steamId) =>
            ExecuteAndCheck(host, port, $"usp 4 1 {steamId}");

        public Task<bool> UnmutePlayerAsync(string host, int port, string steamId) =>
            ExecuteAndCheck(host, port, $"usp 4 0 {steamId}");

        public async Task<bool> KickPlayerAsync(string host, int port, string steamId)
        {
            await ExecuteAsync(host, port, $"usp 1 1 {steamId}");
            await Task.Delay(500);
            await ExecuteAsync(host, port, $"usp 1 0 {steamId}");
            return true;
        }

        public async Task SendRestartAnnounceToSingleServer(Server server, string msg)
        {
            try
            {
                var settings = UnifiedSettingsEditor.LoadServerSettings(
                    Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json"));
                await SendRestartAnnounceToSingleServer("127.0.0.1",
                    settings.HostSettings.RESTAPIPort,
                    settings.HostSettings.AdminPassword, msg);
            }
            catch { }
        }

        private async Task<bool> SendRestartAnnounceToSingleServer(string host, int port, string password, string text)
        {
            return await AnnounceAsync(host, port, password, text);
        }

        public async Task<string?> ExecuteAsync(string host, int port, string command)
        {
            try
            {
                using var client = await ConnectAsync(host, port);
                var stream = client.GetStream();
                stream.WriteTimeout = READ_TIMEOUT_MS;

                byte[] cmd = Encoding.UTF8.GetBytes(command + "\r\n");
                await stream.WriteAsync(cmd);
                await stream.FlushAsync();

                var buf = new byte[4096];
                var sb = new StringBuilder();
                using var cts = new CancellationTokenSource(READ_TIMEOUT_MS);
                try
                {
                    int n;
                    while ((n = await stream.ReadAsync(buf, cts.Token)) > 0)
                        sb.Append(Encoding.UTF8.GetString(buf, 0, n));
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }

                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> ExecuteAndCheck(string host, int port, string cmd)
        {
            var result = await ExecuteAsync(host, port, cmd);
            return result != null;
        }

        #endregion

        #region Legacy RCON TCP Methods

        private async Task<TcpClient> ConnectAsync(string host, int port)
        {
            var client = new TcpClient { NoDelay = true };
            var connectTask = client.ConnectAsync(host, port);
            if (await Task.WhenAny(connectTask, Task.Delay(CONNECT_TIMEOUT_MS)) != connectTask)
            {
                client.Dispose();
                throw new TimeoutException($"EchoPort connection to {host}:{port} timed out.");
            }
            if (connectTask.IsFaulted)
            {
                client.Dispose();
                throw connectTask.Exception?.InnerException ?? new IOException("EchoPort connection failed.");
            }
            return client;
        }

        public void Connect(string hostname, int port)
        {
            Log(string.Format("正在连接 {0}:{1}", hostname, port));
            try
            {
                IAsyncResult asyncResult = null;
                try
                {
                    asyncResult = _client.BeginConnect(hostname, port, null, null);
                }
                catch (ObjectDisposedException)
                {
                    _client = new TcpClient();
                    try { asyncResult = _client.BeginConnect(hostname, port, null, null); }
                    catch { Log("未知错误。"); }
                }

                if (asyncResult == null) { Log("异步连接失败！"); return; }

                asyncResult.AsyncWaitHandle.WaitOne(2000);
                if (!asyncResult.IsCompleted)
                {
                    OnConnectionStateChange?.Invoke(ConnectionStateChange.NoConnection);
                    _client.Client.Close();
                }
            }
            catch (SocketException)
            {
                OnConnectionStateChange?.Invoke(ConnectionStateChange.ConnectionTimeout);
                _client.Client.Close();
                return;
            }

            if (!_client.Connected) return;
            _ns = _client.GetStream();
            _buffer = new byte[MaxAllowedPacketSize];
            _ns.BeginRead(_buffer, 0, MaxAllowedPacketSize, OnPacket, null);
            Log("已连接");
            OnConnectionStateChange?.Invoke(ConnectionStateChange.Connected);
        }

        public void Disconnect()
        {
            if (_client.Connected)
            {
                _client.Client.Disconnect(false);
                OnConnectionStateChange?.Invoke(ConnectionStateChange.Disconnected);
            }
            _client.Close();
        }

        public void Authenticate(string password)
        {
            _packetId++;
            var packet = new RemoteConPacket(_packetId, RemoteConPacket.PacketType.Auth, password, UseUtf8);
            SendPacket(packet);
        }

        public void SendCommand(string command, CommandResult resultFunc)
        {
            if (!_client.Connected) return;
            if (!Authenticated) throw new NotAuthenticatedException();

            _packetId++;
            _requestedCommands.Add(_packetId, resultFunc);
            var packet = new RemoteConPacket(_packetId, RemoteConPacket.PacketType.ExecCommand, command, UseUtf8);
            SendPacket(packet);
        }

        private void SendPacket(RemoteConPacket packet)
        {
            if (_client == null || !_client.Connected) throw new Exception("Not connected.");
            var packetBytes = packet.GetBytes();
            try
            {
                _ns.BeginWrite(packetBytes, 0, packetBytes.Length - 1, ar => { _ns.EndWrite(ar); }, null);
            }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
        }

        private void OnPacket(IAsyncResult result)
        {
            try
            {
                var bytesRead = _ns.EndRead(result);
                if (!_client.Connected)
                {
                    OnConnectionStateChange?.Invoke(ConnectionStateChange.ConnectionLost);
                    return;
                }
                if (bytesRead == 0)
                {
                    _buffer = new byte[MaxAllowedPacketSize];
                    _ns.BeginRead(_buffer, 0, MaxAllowedPacketSize, OnPacket, null);
                    return;
                }
                Array.Resize(ref _buffer, bytesRead);
                ParsePacket(_buffer);
                if (!_client.Connected)
                {
                    OnConnectionStateChange?.Invoke(ConnectionStateChange.ConnectionLost);
                    return;
                }
                _buffer = new byte[MaxAllowedPacketSize];
                _ns.BeginRead(_buffer, 0, MaxAllowedPacketSize, OnPacket, null);
            }
            catch (IOException)
            {
                OnConnectionStateChange?.Invoke(ConnectionStateChange.ConnectionLost);
                Disconnect();
            }
            catch (ObjectDisposedException)
            {
                OnConnectionStateChange?.Invoke(ConnectionStateChange.ConnectionLost);
                Disconnect();
            }
            catch (Exception e)
            {
                Log(e.ToString());
            }
        }

        private void ParsePacket(byte[] rawPacket)
        {
            try
            {
                var packet = new RemoteConPacket(rawPacket, UseUtf8);
                if (!Authenticated)
                {
                    if (packet.Type == RemoteConPacket.PacketType.ExecCommand)
                    {
                        if (packet.Id == -1) { Log("验证失败。"); Authenticated = false; }
                        else { Log("验证成功。"); Authenticated = true; }
                        OnAuthResult?.Invoke(Authenticated);
                    }
                    return;
                }
                if (_requestedCommands.ContainsKey(packet.Id) &&
                    packet.Type == RemoteConPacket.PacketType.ResponseValue)
                    _requestedCommands[packet.Id](packet.Payload);
                else
                    Log("带有无效ID的数据包 " + packet.Id);
            }
            catch (Exception e)
            {
                Log(e.ToString());
            }
        }

        private void Log(string message) => OnLog?.Invoke(message);

        #endregion

        private static List<PlayerDisplayInfo> ParsePlayers(string response)
        {
            var players = new List<PlayerDisplayInfo>();
            if (string.IsNullOrWhiteSpace(response)) return players;

            bool foundHeader = false;
            foreach (var line in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith('|')) continue;
                var cols = trimmed.Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (cols.Length < 2) continue;
                if (cols[0].Trim().Equals("Account", StringComparison.OrdinalIgnoreCase))
                { foundHeader = true; continue; }
                if (!foundHeader) continue;
                string SteamId = cols[0].Trim();
                string CharacterName = cols[1].Trim().Trim('\'');
                players.Add(new PlayerDisplayInfo { CharacterName = CharacterName, SteamId = SteamId });
            }
            return players;
        }
    }

    #region REST API DTOs

    public class ServerInfo
    {
        public string version { get; set; } = "";
        public string servername { get; set; } = "";
        public string description { get; set; } = "";
        public string worldguid { get; set; } = "";
    }

    public class PlayerListResponse
    {
        public List<PlayerEntry> players { get; set; } = new();
    }

    public class PlayerEntry
    {
        public string name { get; set; } = "";
        public string accountName { get; set; } = "";
        public string playerId { get; set; } = "";
        public string userId { get; set; } = "";
        public string ip { get; set; } = "";
        public float ping { get; set; }
        public float location_x { get; set; }
        public float location_y { get; set; }
        public int level { get; set; }
        public int building_count { get; set; }
    }

    public class ServerMetrics
    {
        public int serverfps { get; set; }
        public int currentplayernum { get; set; }
        public float serverframetime { get; set; }
        public int maxplayernum { get; set; }
        public int uptime { get; set; }
        public int basecampnum { get; set; }
        public int days { get; set; }
    }

    #endregion
}
