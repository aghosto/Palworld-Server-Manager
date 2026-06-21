using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using static System.Windows.Forms.DataFormats;

namespace PalworldServerManager
{

    //[Serializable]
    //public class ServerSettings
    //{
    //    public string Mods { get; set; } = "";
    //    public string ServerPresetSettings { get; set; } = "\u65E0";
    //    public PerformancesSettings Performances { get; set; } = new PerformancesSettings();
    //    public ServerManagementSettings ServerManagement { get; set; } = new ServerManagementSettings();
    //    public FeaturesSettings Features { get; set; } = new FeaturesSettings();
    //    public GameBalanceSettings GameBalances { get; set; } = new GameBalanceSettings();
    //}

    [Serializable]
    public class ServerGameSettings
    {
        public PerformancesSettings Performances { get; set; } = new PerformancesSettings();
        public FeaturesSettings Features { get; set; } = new FeaturesSettings();
        public GameBalanceSettings GameBalances { get; set; } = new GameBalanceSettings();
    }

    public class CombinedServerSettings
    {
        public ServerManagementSettings HostSettings { get; set; } = new ServerManagementSettings();
        public PerformancesSettings Performances { get; set; } = new PerformancesSettings();
        public FeaturesSettings Features { get; set; } = new FeaturesSettings();
        public GameBalanceSettings GameBalances { get; set; } = new GameBalanceSettings();
    }

    /// 性能相关配置
    [Serializable]
    public class PerformancesSettings
    {
        /// 据点最大数量（每个玩家）
        public int BaseCampMaxNum { get; set; } = 128;
        /// 每个公会最大据点数量。默认: 4 (最大 10)。提高此值会增加处理负载。
        public int BaseCampMaxNumInGuild { get; set; } = 4;
        /// 每个据点最大帕鲁数量 (最大 50)。提高此值会增加处理负载。
        public int BaseCampWorkerMaxNum { get; set; } = 15;
        /// 掉落物品存活最大时间（小时）
        public double DropItemAliveMaxHours { get; set; } = 1.0;
        /// 掉落物品最大数量
        public int DropItemMaxNum { get; set; } = 3000;
        /// 掉落物品最大数量（粪便）
        public int DropItemMaxNum_UNKO { get; set; } = 100;
        /// 容器UI打开时强制重新同步的间隔（秒）
        public double ItemContainerForceMarkDirtyInterval { get; set; } = 1.0;
        /// 每名玩家建筑数量上限（0 = 无限制）
        public int MaxBuildingLimitNum { get; set; } = 0;
        /// 帕鲁与玩家同步距离（厘米）。最小 5000 – 最大 15000。
        public int ServerReplicatePawnCullDistance { get; set; } = 15000;
        /// 自动保存间隔（秒）
        public double AutoSaveSpan { get; set; } = 30.0;
        /// 启用多线程性能优化
        public bool bUseMultiThreadPerformance { get; set; } = false;
        /// 多线程进程数（CPU线程数-1）
        public int NumberOfWorkerThreadsServer { get; set; } = 4;
    }

    /// 服务器管理相关配置
    [Serializable]
    public class ServerManagementSettings
    {
        /// 服务器名称
        public string ServerName { get; set; } = "My Palworld Server";
        /// 服务器描述
        public string ServerDescription { get; set; } = "";
        /// 可加入服务器的最大玩家数
        public int ServerPlayerMaxNum { get; set; } = 32;
        /// 服务器监听端口
        public int Port { get; set; } = 8211;
        /// 显示在社群服务器列表
        public bool PublicLobby { get; set; } = false;
        /// 手动指定社群公共IP
        public bool bUseManualPublicIP { get; set; } = false;
        /// 手动指定社群公共端口
        public bool bUseManualPublicPort { get; set; } = false;
        /// 封禁列表URL
        public string BanListURL { get; set; } = "https://b.palworldgame.com/api/banlist.txt";
        /// 允许已启用模组的玩家加入服务器
        public bool bAllowClientMod { get; set; } = true;
        /// 是否启用多人游戏
        public bool bIsMultiplay { get; set; } = false;
        /// 在专用服务器上显示玩家加入/离开的游戏内消息
        public bool bIsShowJoinLeftMessage { get; set; } = true;
        /// 启用世界备份。启用会增加磁盘负载。
        public bool bIsUseBackupSaveData { get; set; } = true;
        /// 启用认证
        public bool bUseAuth { get; set; } = true;
        /// 每分钟允许的聊天消息最大数量
        public int ChatPostLimitPerMinute { get; set; } = 30;
        /// 合作模式最大玩家数
        public int CoopPlayerMaxNum { get; set; } = 4;
        /// 允许连接服务器的平台。默认: (Steam,Xbox,PS5,Mac)
        public string CrossplayPlatforms { get; set; } = "(Steam,Xbox,PS5,Mac)";
        /// 游戏难度
        public string Difficulty { get; set; } = "None";
        /// 日志格式: Text 或 Json
        public string LogFormatType { get; set; } = "Text";
        /// （社区服务器）明确指定外部公网IP
        public string PublicIP { get; set; } = "";
        /// （社区服务器）明确指定外部公网端口（不改变服务器监听端口）
        public int PublicPort { get; set; } = 8211;
        /// 启用RCON
        public bool RCONEnabled { get; set; } = false;
        /// RCON使用的端口号
        public int RCONPort { get; set; } = 25575;
        /// 服务器区域
        public string Region { get; set; } = "";
        /// 启用REST API
        public bool RESTAPIEnabled { get; set; } = false;
        /// REST API监听端口（默认: 8212）
        public int RESTAPIPort { get; set; } = 8212;
        /// 登录服务器所需的密码
        public string ServerPassword { get; set; } = "";
        /// 用于在服务器上获取管理员权限的密码
        public string AdminPassword { get; set; } = "";

    }

    /// 功能相关配置
    [Serializable]
public class FeaturesSettings
    {
        /// 启用PvP
        public bool bIsPvP { get; set; } = false;
        /// 公会无在线玩家时触发自动重置的离线时长（小时）。仅在 bAutoResetGuildNoOnlinePlayers 为 true 时生效。
        public double AutoResetGuildTimeNoOnlinePlayers { get; set; } = 72.0;
        /// 启用粪便掉落
        public bool bActiveUNKO { get; set; } = false;
        /// 允许分配属性点到攻击
        public bool bAllowEnhanceStat_Attack { get; set; } = true;
        /// 允许分配属性点到生命值
        public bool bAllowEnhanceStat_Health { get; set; } = true;
        /// 允许分配属性点到耐力
        public bool bAllowEnhanceStat_Stamina { get; set; } = true;
        /// 允许分配属性点到负重
        public bool bAllowEnhanceStat_Weight { get; set; } = true;
        /// 允许分配属性点到工作速度
        public bool bAllowEnhanceStat_WorkSpeed { get; set; } = true;
        /// 允许保存到全局帕鲁箱子
        public bool bAllowGlobalPalboxExport { get; set; } = true;
        /// 允许从全局帕鲁箱子加载
        public bool bAllowGlobalPalboxImport { get; set; } = false;
        /// 无公会成员登录时，自动删除建筑和基地帕鲁
        public bool bAutoResetGuildNoOnlinePlayers { get; set; } = false;
        /// 禁止在传送点等建筑附近建造
        public bool bBuildAreaLimit { get; set; } = false;
        /// 能否拾取其他公会的死亡掉落物品
        public bool bCanPickupOtherGuildDeathPenaltyDrop { get; set; } = false;
        /// 硬核模式死亡后是否可重建角色
        public bool bCharacterRecreateInHardcore { get; set; } = false;
        /// 在地图上显示各基地的PvP专属物品数量
        public bool bDisplayPvPItemNumOnWorldMap_BaseCamp { get; set; } = false;
        /// 在地图上显示玩家位置和PvP专属物品数量
        public bool bDisplayPvPItemNumOnWorldMap_Player { get; set; } = false;
        /// 启用键盘瞄准辅助
        public bool bEnableAimAssistKeyboard { get; set; } = false;
        /// 启用手柄瞄准辅助
        public bool bEnableAimAssistPad { get; set; } = true;
        /// 启用防御其他公会玩家
        public bool bEnableDefenseOtherGuildPlayer { get; set; } = false;
        /// 启用快速旅行
        public bool bEnableFastTravel { get; set; } = true;
        /// 仅限基地之间快速旅行
        public bool bEnableFastTravelOnlyBaseCamp { get; set; } = false;
        /// 启用友军伤害
        public bool bEnableFriendlyFire { get; set; } = false;
        /// 启用入侵者
        public bool bEnableInvaderEnemy { get; set; } = true;
        /// 启用未登录惩罚
        public bool bEnableNonLoginPenalty { get; set; } = true;
        /// 启用玩家间伤害
        public bool bEnablePlayerToPlayerDamage { get; set; } = false;
        /// 登出后玩家是否在当前位置进入睡眠状态
        public bool bExistPlayerAfterLogout { get; set; } = false;
        /// 启用硬核模式（死亡后无法复活）
        public bool bHardcore { get; set; } = false;
        /// 显示其他公会基地范围边界
        public bool bInvisibleOtherGuildBaseCampAreaFX { get; set; } = false;
        /// 若为 true，野生帕鲁等级完全随机；若为 false，在各区域预期范围内随机
        public bool bIsRandomizerPalLevelRandom { get; set; } = false;
        /// 是否允许玩家选择起始位置
        public bool bIsStartLocationSelectByMap { get; set; } = true;
        /// 在ESC菜单中显示玩家列表
        public bool bShowPlayerList { get; set; } = false;
        /// 启用捕食者Boss帕鲁
        public bool EnablePredatorBossPal { get; set; } = true;
        /// 帕鲁随机生成种子值
        public string RandomizerSeed { get; set; } = "";
        /// 帕鲁随机生成模式: None=不随机, Region=按区域随机, All=完全随机
        public string RandomizerType { get; set; } = "None";
    }

    /// 游戏平衡相关配置
    [Serializable]
    public class GameBalanceSettings
    {
        /// PvP模式下击杀玩家额外掉落的物品ID
        public string AdditionalDropItemWhenPlayerKillingInPvPMode { get; set; } = "Champion's Emblem";
        /// PvP模式下击杀玩家额外掉落物品的数量
        public int AdditionalDropItemNumWhenPlayerKillingInPvPMode { get; set; } = 1;
        /// 是否在PvP启用时掉落特殊物品
        public bool bAdditionalDropItemWhenPlayerKillingInPvPMode { get; set; } = false;
        /// 死亡后复活冷却时间（秒）
        public double BlockRespawnTime { get; set; } = 5.0;
        /// 死亡时永久失去帕鲁
        public bool bPalLost { get; set; } = false;
        /// 对建筑造成的伤害倍率
        public double BuildObjectDamageRate { get; set; } = 1.0;
        /// 建筑腐朽速度倍率
        public double BuildObjectDeteriorationDamageRate { get; set; } = 1.0;
        /// 建筑生命值倍率
        public double BuildObjectHpRate { get; set; } = 1.0;
        /// 可采集物品掉落倍率
        public double CollectionDropRate { get; set; } = 1.0;
        /// 可采集物体生命值倍率
        public double CollectionObjectHpRate { get; set; } = 1.0;
        /// 可采集物体重生速度倍率
        public double CollectionObjectRespawnSpeedRate { get; set; } = 1.0;
        /// 白天时间流逝速度
        public double DayTimeSpeedRate { get; set; } = 1.0;
        /// 死亡惩罚: None=无掉落, Item=掉落物品(除装备), ItemAndEquipment=掉落所有物品, All=掉落所有物品和队伍帕鲁
        public string DeathPenalty { get; set; } = "All";
        /// 禁用特定科技列表。指定 Technology IDs，例如: DenyTechnologyList=("PALBOX", "RepairBench")
        public string DenyTechnologyList { get; set; } = "";
        /// 敌人掉落物品数量倍率
        public double EnemyDropItemRate { get; set; } = 1.0;
        /// 装备耐久损耗倍率
        public double EquipmentDurabilityDamageRate { get; set; } = 1.0;
        /// 经验获取倍率
        public double ExpRate { get; set; } = 1.0;
        /// 公会最大玩家数
        public int GuildPlayerMaxNum { get; set; } = 20;
        /// 公会重新加入冷却时间（分钟）
        public int GuildRejoinCooldownMinutes { get; set; } = 0;
        /// 物品腐败速度倍率
        public double ItemCorruptionMultiplier { get; set; } = 1.0;
        /// 物品重量倍率
        public double ItemWeightRate { get; set; } = 1.0;
        /// 夜晚时间流逝速度
        public double NightTimeSpeedRate { get; set; } = 1.0;
        /// 帕鲁自然生命恢复倍率
        public double PalAutoHPRegeneRate { get; set; } = 1.0;
        /// 帕鲁睡眠中（帕鲁箱子）生命恢复倍率
        public double PalAutoHpRegeneRateInSleep { get; set; } = 1.0;
        /// 捕获率倍率
        public double PalCaptureRate { get; set; } = 1.0;
        /// 帕鲁造成伤害倍率
        public double PalDamageRateAttack { get; set; } = 1.0;
        /// 帕鲁承受伤害倍率
        public double PalDamageRateDefense { get; set; } = 1.0;
        /// 巨大蛋孵化所需时间（小时）。其他蛋也需要孵蛋时间。
        public double PalEggDefaultHatchingTime { get; set; } = 72.0;
        /// 帕鲁生成数量倍率（影响性能）
        public double PalSpawnNumRate { get; set; } = 1.0;
        /// 帕鲁耐力消耗倍率
        public double PalStaminaDecreaceRate { get; set; } = 1.0;
        /// 帕鲁饥饿速度倍率
        public double PalStomachDecreaceRate { get; set; } = 1.0;
        /// 玩家自然生命恢复倍率
        public double PlayerAutoHPRegeneRate { get; set; } = 1.0;
        /// 玩家睡眠中生命恢复倍率
        public double PlayerAutoHpRegeneRateInSleep { get; set; } = 1.0;
        /// 玩家造成伤害倍率
        public double PlayerDamageRateAttack { get; set; } = 1.0;
        /// 玩家承受伤害倍率
        public double PlayerDamageRateDefense { get; set; } = 1.0;
        /// 玩家耐力消耗倍率
        public double PlayerStaminaDecreaceRate { get; set; } = 1.0;
        /// 玩家饥饿速度倍率
        public double PlayerStomachDecreaceRate { get; set; } = 1.0;
        /// 应用复活惩罚倍率的生存时间阈值（秒）
        public double RespawnPenaltyDurationThreshold { get; set; } = 0.0;
        /// 复活冷却时间的倍率
        public double RespawnPenaltyTimeScale { get; set; } = 2.0;
        /// 陨石/空投补给间隔（分钟）
        public int SupplyDropSpan { get; set; } = 180;
        /// 工作速度倍率
        public double WorkSpeedRate { get; set; } = 1.0;
    }

    public class TechItem : System.ComponentModel.INotifyPropertyChanged
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string NameCn { get; set; } = "";
        public string Description { get; set; } = "";
        public string DisplayName => !string.IsNullOrEmpty(NameCn) ? NameCn : Name;
        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; OnPropertyChanged(nameof(IsChecked)); }
        }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}
