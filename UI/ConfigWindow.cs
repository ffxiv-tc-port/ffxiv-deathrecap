using System;
using System.Collections.Immutable;
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

    public ConfigWindow(DeathRecapPlugin plugin) : base("死亡回顧設定", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize) {
        this.plugin = plugin;

        Size = new Vector2(580, 340);
    }

    public override void Draw() {
        var conf = plugin.Configuration;

        ImGui.TextUnformatted("記錄設定");
        ImGui.Separator();
        ImGui.Columns(3);
        foreach (var (k, v) in conf.EnumCaptureConfigs()) {
            ImGui.PushID(k);
            var bCapture = v.Capture;
            if (ImGui.Checkbox($"記錄{k}", ref bCapture)) {
                v.Capture = bCapture;
                conf.Save();
            }

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
        var chatTypes = Enum.GetValues<XivChatType>();
        var chatType = Array.IndexOf(chatTypes, conf.ChatType);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("聊天訊息類型");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
        if (ImGui.Combo("##3", ref chatType, chatTypes.Select(t => t.GetAttribute<XivChatTypeInfoAttribute>()?.FancyName ?? t.ToString()).ToImmutableList(),
                10)) {
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

    private static void OnlyInInstancesTooltip() {
        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip("只有在副本內（例如迷宮）才顯示死亡通知。");
        }
    }
}
