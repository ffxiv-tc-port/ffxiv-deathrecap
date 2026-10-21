using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;

namespace DeathRecap.UI;

public class ConfigWindow : Window {
    private readonly DeathRecapPlugin plugin;

    // 列舉值與屬性執行期不會變：第一次畫時建好就一直用。
    private XivChatType[]? chatTypes;
    private string[]? chatTypeNames;

    public ConfigWindow(DeathRecapPlugin plugin) : base("死亡回顧設定", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize) {
        this.plugin = plugin;

        Size = new Vector2(580, 400);
    }

    public override void Draw() {
        var conf = plugin.Configuration;

        ImGui.TextUnformatted("記錄設定");
        ImGui.Separator();
        ImGui.Columns(3);
        foreach (var (k, v) in conf.EnumCaptureConfigs()) {
            ImGui.PushID(k);
            var bCapture = v.Capture;
            var who = k switch { "Self" => "自己", "Party" => "小隊成員", "Others" => "其他人", _ => k };
            if (ImGui.Checkbox($"記錄{who}", ref bCapture)) {
                v.Capture = bCapture;
                conf.Save();
            }

            if (k == "Self")
                SelfCaptureTooltip();

            var notificationStyle = (int)v.NotificationStyle;
            ImGui.TextUnformatted("死亡時");
            if (ImGui.Combo("##2", ref notificationStyle, ["不執行任何動作", "傳送聊天訊息", "顯示彈出視窗", "開啟死亡回顧"])) {
                v.NotificationStyle = (NotificationStyle)notificationStyle;
                conf.Save();
            }

            var bOnlyInstances = v.OnlyInstances;
            if (ImGui.Checkbox("僅限副本內", ref bOnlyInstances)) {
                v.OnlyInstances = bOnlyInstances;
                conf.Save();
            }

            OnlyInInstancesTooltip();

            var bDisableInPvp = v.DisableInPvp;
            if (ImGui.Checkbox("在 PvP 中停用", ref bDisableInPvp)) {
                v.DisableInPvp = bDisableInPvp;
                conf.Save();
            }

            ImGui.PopID();
            ImGui.NextColumn();
        }

        ImGui.Columns();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextUnformatted("一般設定");
        ImGui.Spacing();
        var chatTypes = this.chatTypes ??= Enum.GetValues<XivChatType>();
        var chatTypeNames = this.chatTypeNames ??=
            chatTypes.Select(t => t.GetAttribute<XivChatTypeInfoAttribute>()?.FancyName ?? t.ToString()).ToArray();
        var chatType = Array.IndexOf(chatTypes, conf.ChatType);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("聊天訊息類型");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
        if (ImGui.Combo("##3", ref chatType, chatTypeNames, 10)) {
            conf.ChatType = chatTypes[chatType];
            conf.Save();
        }

        ChatMessageTypeTooltip();

        var bShowTip = conf.ShowTip;
        if (ImGui.Checkbox("顯示聊天提示", ref bShowTip)) {
            conf.ShowTip = bShowTip;
            conf.Save();
        }

        ChatTipTooltip();

        var bAutoClose = conf.AutoCloseAutoShownWindow;
        if (ImGui.Checkbox("自動顯示的視窗在指定秒數後自動關閉###autoCloseAutoShown", ref bAutoClose)) {
            conf.AutoCloseAutoShownWindow = bAutoClose;
            conf.Save();
        }

        AutoCloseTooltip();

        if (!conf.AutoCloseAutoShownWindow)
            ImGui.BeginDisabled();
        var autoCloseSeconds = conf.AutoCloseSeconds;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("自動關閉秒數");
        ImGui.SameLine(ImGuiHelpers.GlobalScale * 140);
        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 150);
        if (ImGui.InputInt("##6", ref autoCloseSeconds, 1)) {
            conf.AutoCloseSeconds = Math.Max(0, autoCloseSeconds);
            conf.Save();
        }

        AutoCloseSecondsTooltip();
        if (!conf.AutoCloseAutoShownWindow)
            ImGui.EndDisabled();

        var keepEventsFor = conf.KeepCombatEventsForSeconds;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("保留事件（秒）");
        ImGui.SameLine(ImGuiHelpers.GlobalScale * 140);
        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 150);
        if (ImGui.InputInt("##4", ref keepEventsFor, 10)) {
            conf.KeepCombatEventsForSeconds = keepEventsFor;
            conf.Save();
        }

        var keepDeathsFor = conf.KeepDeathsForMinutes;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("保留死亡記錄（分）");
        ImGui.SameLine(ImGuiHelpers.GlobalScale * 140);
        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 150);
        if (ImGui.InputInt("##5", ref keepDeathsFor, 10)) {
            conf.KeepDeathsForMinutes = keepDeathsFor;
            conf.Save();
        }
    }

    private static void ChatMessageTypeTooltip() {
        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip("篩選「聊天訊息」死亡通知所使用的頻道。\n" +
                             "無論聊天分頁如何設定，「偵錯」訊息都會顯示。\n" +
                             "這只會影響你看到通知的方式，其他玩家永遠不會看見。");
        }
    }

    private static void ChatTipTooltip() {
        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip("第一次關閉死亡回顧時，在聊天欄顯示重新開啟視窗的指令。");
        }
    }

    private static void AutoCloseTooltip() {
        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip("只影響死亡時「開啟死亡回顧」自動彈出的視窗。\n" +
                             "自己用 /dr、點聊天連結或點彈出視窗開啟的不會被自動關閉。\n" +
                             "倒數期間在視窗上點一下滑鼠（或滾動）就會取消自動關閉。");
        }
    }

    private static void AutoCloseSecondsTooltip() {
        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip("自動顯示的視窗要在幾秒後自動關閉。\n設為 0 等同停用自動關閉。");
        }
    }

    private static void SelfCaptureTooltip() {
        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip("記錄自己受到的傷害、治療與狀態，死亡時才有死亡回顧可看。\n" +
                             "自己死亡時寫進 dalamud.log 的死因摘要也跟著記錄設定：\n" +
                             "關掉這個、而且「記錄其他人」沒開、也沒有在小隊中被「記錄小隊成員」涵蓋時，那一行不寫。");
        }
    }

    private static void OnlyInInstancesTooltip() {
        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip("只有在副本內（例如迷宮）才顯示死亡通知。");
        }
    }
}
