using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PalworldServerManager.REST
{
    public class RestApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;

        public string BaseUrl { get; private set; } = "";

        public RestApiClient()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public void SetAuth(string ip, int port, string adminPassword)
        {
            BaseUrl = $"http://{ip}:{port}/v1/api";
            string creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"admin:{adminPassword}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
        }

        public async Task<ServerInfoResponse?> GetServerInfoAsync()
        {
            string json = await _httpClient.GetStringAsync($"{BaseUrl}/info");
            return JsonSerializer.Deserialize<ServerInfoResponse>(json);
        }

        public async Task<PlayerListResponse?> GetPlayerListAsync()
        {
            string json = await _httpClient.GetStringAsync($"{BaseUrl}/players");
            return JsonSerializer.Deserialize<PlayerListResponse>(json);
        }

        public async Task<string?> GetServerSettingsAsync()
        {
            return await _httpClient.GetStringAsync($"{BaseUrl}/settings");
        }

        public async Task<bool> AnnounceAsync(string message)
        {
            var body = new StringContent(
                JsonSerializer.Serialize(new AnnounceRequest { Message = message }),
                Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{BaseUrl}/announce", body);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> KickPlayerAsync(string userId, string? reason = null)
        {
            var req = new KickBanRequest { UserId = userId, Message = reason };
            var body = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{BaseUrl}/kick", body);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> BanPlayerAsync(string userId, string? reason = null)
        {
            var req = new KickBanRequest { UserId = userId, Message = reason };
            var body = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{BaseUrl}/ban", body);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> UnbanPlayerAsync(string userId)
        {
            var body = new StringContent(
                JsonSerializer.Serialize(new UnbanRequest { UserId = userId }),
                Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{BaseUrl}/unban", body);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> SaveWorldAsync()
        {
            var response = await _httpClient.PostAsync($"{BaseUrl}/save", null);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> ShutdownAsync(int waitTime, string? message = null)
        {
            var req = new ShutdownRequest { WaitTime = waitTime, Message = message };
            var body = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{BaseUrl}/shutdown", body);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> ForceStopAsync()
        {
            var response = await _httpClient.PostAsync($"{BaseUrl}/stop", null);
            return response.IsSuccessStatusCode;
        }

        public async Task<ServerMetricsResponse?> GetMetricsAsync()
        {
            string json = await _httpClient.GetStringAsync($"{BaseUrl}/metrics");
            return JsonSerializer.Deserialize<ServerMetricsResponse>(json);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
