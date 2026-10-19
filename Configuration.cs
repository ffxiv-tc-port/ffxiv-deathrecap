using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Game.Text;
using Dalamud.Plugin;
using DeathRecap.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DeathRecap;

[Serializable]
public class Configuration : IPluginConfiguration {
    [NonSerialized]
    private IDalamudPluginInterface pluginInterface = null!;

    public CaptureConfig Self { get; set; } =
        new() { Capture = true, NotificationStyle = NotificationStyle.Popup, OnlyInstances = false, DisableInPvp = false };

    public CaptureConfig Party { get; set; } =
        new() { Capture = true, NotificationStyle = NotificationStyle.Chat, OnlyInstances = false, DisableInPvp = false };

    public CaptureConfig Others { get; set; } =
        new() { Capture = false, NotificationStyle = NotificationStyle.Chat, OnlyInstances = true, DisableInPvp = true };

    public bool ShowTip { get; set; } = true;
    public int KeepCombatEventsForSeconds { get; set; } = 60;
    public int KeepDeathsForMinutes { get; set; } = 60;
    public XivChatType ChatType { get; set; } = XivChatType.SystemMessage;
    public EventFilter EventFilter { get; set; } = EventFilter.Default;
    public bool ShowCombatHistogram { get; set; } = false;

    /// <summary>
    /// 自動顯示（死亡時自動開啟死亡回顧）的視窗，是否在倒數結束後自動關閉。
    /// 只影響自動顯示的情況；手動用 /dr、點聊天連結或點彈出視窗開啟的不受影響。
    /// </summary>
    public bool AutoCloseAutoShownWindow { get; set; } = true;

    /// <summary>
    /// 自動顯示的視窗在幾秒後自動關閉。0 代表不自動關閉。
    /// </summary>
    public int AutoCloseSeconds { get; set; } = 10;

    public int Version { get; set; } = 2;

    [JsonExtensionData]
    public IDictionary<string, JToken> AdditionalData { get; set; } = new Dictionary<string, JToken>();

    public static Configuration Get(IDalamudPluginInterface pluginInterface) {
        var config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        config.pluginInterface = pluginInterface;
        config.Migrate();

        return config;
    }

    public IEnumerable<(string, CaptureConfig)> EnumCaptureConfigs() {
        yield return ("Self", Self);
        yield return ("Party", Party);
        yield return ("Others", Others);
    }

    public void Migrate() {
        if (Version == 0) {
            foreach (var (k, v) in EnumCaptureConfigs()) {
                v.Capture = AdditionalData[$"Capture{k}"].ToObject<bool>();
                v.NotificationStyle = AdditionalData[$"{k}Notification"].ToObject<NotificationStyle>();
                v.OnlyInstances = AdditionalData[$"{k}NotificationOnlyInstances"].ToObject<bool>();
            }

            AdditionalData.Clear();
            Version = 1;
            Save();
        }

        if (Version == 1) {
            if (ChatType == XivChatType.Debug) {
                ChatType = XivChatType.SystemMessage;
            }

            Version = 2;
            Save();
        }
    }

    public void Save() {
        pluginInterface.SavePluginConfig(this);
    }

    [Serializable]
    public class CaptureConfig {
        public bool Capture { get; set; }
        public NotificationStyle NotificationStyle { get; set; }
        public bool OnlyInstances { get; set; }
        public bool DisableInPvp { get; set; }
    }
}
