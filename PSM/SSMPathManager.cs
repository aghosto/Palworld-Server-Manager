using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PalworldServerManager
{
    public class SSMPathManager
    {
        private readonly string _rootDir;
        private readonly string _serverPath;

        public string SettingsFile => Path.Combine(_rootDir, "PSMSettings.json");
        public string ServerFilesDir => _serverPath;
        public string SavedDir => Path.Combine(ServerFilesDir, "Pal", "Saved");
        public string ModDir => Path.Combine(ServerFilesDir, "Pal", "Mods");
        public string PluginDir => Path.Combine(ServerFilesDir, "Pal", "Plugins");
        public string ServerSettings => Path.Combine(ServerFilesDir, "SaveData", "Settings", "ServerSettings.json");
        public string DedicatedPath => Path.Combine(SavedDir, "SaveGames");
        public string LogsDir => Path.Combine(SavedDir, "Logs");
        public string LogsPath => Path.Combine(LogsDir, "Pal.log");
        public string ConfigDir => Path.Combine(SavedDir, "Config", "WindowsServer");
        public string GameIniPath => Path.Combine(ConfigDir, "Game.ini");
        public string EngineIniPath => Path.Combine(ConfigDir, "Engine.ini");
        public string GameplaySettingsPath => Path.Combine(SavedDir, "GameplaySettings", "GameXishu.json");
        //public string GameplayDefaultsPath => Path.Combine(SavedDir, "GameplaySettings", "GameXishu.default.json");
        public string GameplayTemplatePath => Path.Combine(ServerFilesDir, "WS", "Config", "GameplaySettings", "GameXishu_Template.json");
        //public string GameplayPresetsPath => Path.Combine(_rootDir, "gameplay_presets.json");
        public string BanListPath => Path.Combine(SavedDir, "SaveGames", "banlist.txt");
        public string MuteListPath => Path.Combine(SavedDir, "BanSpeek.txt");
        public string ServerExePath => Path.Combine(ServerFilesDir, "Pal", "Binaries", "Win64", "PalServer-Win64-Shipping-Cmd.exe");
        //public string BanCachePath => Path.Combine(_rootDir, "banned_names.json");
        //public string MuteCachePath => Path.Combine(_rootDir, "muted_names.json");

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public SSMPathManager(string rootDir, Server server)
        {
            _rootDir = rootDir;
            _serverPath = server.Path;
        }
    }
}
