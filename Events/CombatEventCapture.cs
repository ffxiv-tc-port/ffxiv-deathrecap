using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using DeathRecap.Game;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Common.Math;
using Action = Lumina.Excel.Sheets.Action;
using Status = Lumina.Excel.Sheets.Status;
using TerritoryType = Lumina.Excel.Sheets.TerritoryType;

namespace DeathRecap.Events;

public class CombatEventCapture : IDisposable {
    private readonly Dictionary<ulong, List<CombatEvent>> combatEvents = new();
    private readonly DeathRecapPlugin plugin;

    private unsafe delegate void ProcessPacketActionEffectDelegate(
        uint casterEntityId, Character* casterPtr, Vector3* targetPos, ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds);

    private delegate void ProcessPacketActorControlDelegate(
        uint entityId, ActorControlCategory type, uint param1, uint param2, uint param3, uint param4, uint param5, uint param6, ulong param7, byte isReplay);

    private delegate void ProcessPacketEffectResultDelegate(uint targetId, IntPtr actionIntegrityData, byte isReplay);

    // 三個掛鉤都可能是 null：遊戲改版後特徵碼對不上的那一個不建立（建構子各印一行 Warning），
    // 其餘照常，不讓整個插件載入失敗。detour 只會在自己的掛鉤存在時被呼叫。
    private readonly Hook<ProcessPacketActionEffectDelegate>? processPacketActionEffectHook;

    [Signature("E8 ?? ?? ?? ?? 0F B7 0B 83 E9 64", DetourName = nameof(ProcessPacketActorControlDetour), Fallibility = Fallibility.Fallible)]
    private readonly Hook<ProcessPacketActorControlDelegate>? processPacketActorControlHook = null;

    [Signature("48 8B C4 44 88 40 18 89 48 08", DetourName = nameof(ProcessPacketEffectResultDetour), Fallibility = Fallibility.Fallible)]
    private readonly Hook<ProcessPacketEffectResultDelegate>? processPacketEffectResultHook = null;

    /// <summary>有掛鉤沒建立時給死亡回顧視窗顯示的一行說明；三個都在時是 null。</summary>
    public string? MissingNotice { get; }

    public unsafe CombatEventCapture(DeathRecapPlugin plugin) {
        this.plugin = plugin;

        try {
            Service.GameInteropProvider.InitializeFromAttributes(this);
        } catch (Exception e) {
            Service.PluginLog.Error(e, "[DeathRecap] 特徵碼初始化中途失敗，沒建立的掛鉤見下面的警告");
        }

        try {
            processPacketActionEffectHook =
                Service.GameInteropProvider.HookFromSignature<ProcessPacketActionEffectDelegate>(ActionEffectHandler.Addresses.Receive.String,
                    ProcessPacketActionEffectDetour);
        } catch (Exception e) {
            Service.PluginLog.Warning(e, "[DeathRecap] ActionEffectHandler.Receive 的掛鉤沒建立（遊戲改版後特徵碼對不上時會這樣，原因見例外）：死亡回顧不會有傷害與治療事件");
        }

        if (processPacketActorControlHook == null)
            Service.PluginLog.Warning("[DeathRecap] 特徵碼對不上 ProcessPacketActorControl（遊戲改版後需要更新）：偵測不到死亡，死亡回顧與死亡紀錄都不會有新的一筆（DoT／HoT 事件也沒有）");
        if (processPacketEffectResultHook == null)
            Service.PluginLog.Warning("[DeathRecap] 特徵碼對不上 ProcessPacketEffectResult（遊戲改版後需要更新）：死亡回顧不會有增益／減益效果事件");

        MissingNotice = BuildMissingNotice(processPacketActionEffectHook != null, processPacketActorControlHook != null,
            processPacketEffectResultHook != null);

        processPacketActionEffectHook?.Enable();
        processPacketActorControlHook?.Enable();
        processPacketEffectResultHook?.Enable();
    }

    private static string? BuildMissingNotice(bool hasActionEffect, bool hasActorControl, bool hasEffectResult) {
        // 死亡本身是 ActorControl 掛鉤偵測的：少了它就不會有任何新紀錄，其餘兩項不必再列。
        if (!hasActorControl)
            return "遊戲改版後特徵碼對不上，偵測不到死亡：暫時不會有新的死亡記錄（等插件更新）。";
        if (hasActionEffect && hasEffectResult)
            return null;
        var missing = !hasActionEffect && !hasEffectResult ? "傷害與治療、增益／減益效果" : !hasActionEffect ? "傷害與治療" : "增益／減益效果";
        return $"遊戲改版後特徵碼對不上，死亡記錄暫時缺少：{missing}（等插件更新）。";
    }

    private unsafe void ProcessPacketActionEffectDetour(
        uint casterEntityId, Character* casterPtr, Vector3* targetPos, ActionEffectHandler.Header* effectHeader, ActionEffectHandler.TargetEffects* effectArray,
        GameObjectId* targetEntityIds) {
        processPacketActionEffectHook!.Original(casterEntityId, casterPtr, targetPos, effectHeader, effectArray, targetEntityIds);

        try {
            if (effectHeader->NumTargets == 0)
                return;

            var actionId = effectHeader->ActionType switch {
                ActionType.Mount => 0xD000000 + effectHeader->ActionId,
                ActionType.Item => 0x2000000 + effectHeader->ActionId,
                _ => effectHeader->SpellId
            };
            Action? action = null;
            string? source = null;
            var sourceIsPlayer = false;
            List<uint>? additionalStatus = null;

            for (var i = 0; i < effectHeader->NumTargets; i++) {
                var actionTargetId = (uint)(targetEntityIds[i] & uint.MaxValue);
                if (!plugin.ConditionEvaluator.ShouldCapture(actionTargetId))
                    continue;
                if (Service.ObjectTable.SearchById(actionTargetId) is not IPlayerCharacter p)
                    continue;
                for (var j = 0; j < 8; j++) {
                    ref var actionEffect = ref effectArray[i].Effects[j];
                    if (actionEffect.Type == 0)
                        continue;
                    uint amount = actionEffect.Value;
                    if ((actionEffect.Param4 & 0x40) == 0x40)
                        amount += (uint)actionEffect.Param3 << 16;

                    action ??= Service.DataManager.GetExcelSheet<Action>().GetRowOrDefault(actionId);
                    // casterPtr can legitimately be null for source-less effects; a null deref here would be an
                    // uncatchable AccessViolationException (the try/catch below cannot catch it).
                    if (source == null) {
                        source = casterPtr != null ? casterPtr->NameString : "";
                        sourceIsPlayer = casterPtr != null && casterPtr->ObjectKind == ObjectKind.Pc;
                    }

                    switch ((ActionEffectType)actionEffect.Type) {
                        case ActionEffectType.Miss:
                        case ActionEffectType.Damage:
                        case ActionEffectType.BlockedDamage:
                        case ActionEffectType.ParriedDamage:
                            if (additionalStatus == null) {
                                var statusManager = casterPtr != null ? casterPtr->GetStatusManager() : null;
                                additionalStatus = [];
                                if (statusManager != null) {
                                    foreach (ref var status in statusManager->Status) {
                                        if (SelfDeathLog.IsTargetMitigation(status.StatusId))
                                            additionalStatus.Add(status.StatusId);
                                    }
                                }
                            }

                            combatEvents.AddEntry(actionTargetId,
                                new CombatEvent.DamageTaken {
                                    Snapshot = p.Snapshot(true, additionalStatus),
                                    Source = source,
                                    SourceIsPlayer = sourceIsPlayer,
                                    Amount = amount,
                                    Action = action?.ActionCategory.RowId == 1 ? "Auto-attack" : action?.Name.ExtractText() ?? "",
                                    Icon = action?.Icon,
                                    Crit = (actionEffect.Param0 & 0x20) == 0x20,
                                    DirectHit = (actionEffect.Param0 & 0x40) == 0x40,
                                    DamageType = (DamageType)(actionEffect.Param1 & 0xF),
                                    Parried = actionEffect.Type == (int)ActionEffectType.ParriedDamage,
                                    Blocked = actionEffect.Type == (int)ActionEffectType.BlockedDamage,
                                    DisplayType = effectHeader->ActionType
                                });
                            break;
                        case ActionEffectType.Heal:
                            combatEvents.AddEntry(actionTargetId,
                                new CombatEvent.Healed {
                                    Snapshot = p.Snapshot(true),
                                    Source = source,
                                    Amount = amount,
                                    Action = action?.Name.ExtractText() ?? "",
                                    Icon = action?.Icon,
                                    Crit = (actionEffect.Param1 & 0x20) == 0x20
                                });
                            break;
                    }
                }
            }
        } catch (Exception e) {
            Service.PluginLog.Error(e, "Caught unexpected exception");
        }
    }

    private void ProcessPacketActorControlDetour(
        uint entityId, ActorControlCategory type, uint param1, uint param2, uint param3, uint param4, uint param5, uint param6, ulong param7, byte isReplay) {
        processPacketActorControlHook!.Original(entityId, type, param1, param2, param3, param4, param5, param6, param7, isReplay);

        try {
            if (!plugin.ConditionEvaluator.ShouldCapture(entityId))
                return;

            if (Service.ObjectTable.SearchById(entityId) is not IPlayerCharacter p)
                return;

            switch (type) {
                case ActorControlCategory.DoT: combatEvents.AddEntry(entityId, new CombatEvent.DoT { Snapshot = p.Snapshot(), Amount = param2 }); break;
                case ActorControlCategory.HoT:
                    if (param1 != 0) {
                        var sourceName = Service.ObjectTable.SearchById(entityId)?.Name.TextValue;
                        var status = Service.DataManager.GetExcelSheet<Status>().GetRowOrDefault(param1);
                        combatEvents.AddEntry(entityId,
                            new CombatEvent.Healed {
                                Snapshot = p.Snapshot(),
                                Source = sourceName,
                                Amount = param2,
                                Action = status?.Name.ExtractText() ?? "",
                                Icon = status?.Icon,
                                Crit = param4 == 1
                            });
                    } else {
                        combatEvents.AddEntry(entityId, new CombatEvent.HoT { Snapshot = p.Snapshot(), Amount = param2 });
                    }

                    break;
                case ActorControlCategory.Death: {
                    combatEvents.Remove(entityId, out var events);
                    var timeOfDeath = DateTime.Now;
                    if (Service.ObjectTable.LocalPlayer?.EntityId == entityId)
                        LogSelfDeath(p.MaxHp, timeOfDeath, events);

                    if (events != null) {
                        var death = new Death { PlayerId = entityId, PlayerName = p.Name.TextValue, TimeOfDeath = timeOfDeath, Events = events };
                        plugin.DeathsPerPlayer.AddEntry(entityId, death);
                        plugin.NotificationHandler.DisplayDeath(death);
                    }

                    break;
                }
            }
        } catch (Exception e) {
            Service.PluginLog.Error(e, "Caught unexpected exception");
        }
    }

    private unsafe void ProcessPacketEffectResultDetour(uint targetId, IntPtr actionIntegrityData, byte isReplay) {
        processPacketEffectResultHook!.Original(targetId, actionIntegrityData, isReplay);

        try {
            // actionIntegrityData is a raw game-supplied pointer; a zero here would AVE-crash on deref below
            // (uncatchable in .NET, so the surrounding try/catch cannot help).
            if (actionIntegrityData == IntPtr.Zero)
                return;
            var message = (AddStatusEffect*)actionIntegrityData;
            if (!plugin.ConditionEvaluator.ShouldCapture(targetId))
                return;

            if (Service.ObjectTable.SearchById(targetId) is not IPlayerCharacter p)
                return;

            var effects = (StatusEffectAddEntry*)message->Effects;
            var effectCount = Math.Min(message->EffectCount, 4u);
            for (uint j = 0; j < effectCount; j++) {
                var effect = effects[j];
                var effectId = effect.EffectId;
                if (effectId <= 0)
                    continue;
                // negative durations will remove effect
                if (effect.Duration < 0)
                    continue;
                var source = Service.ObjectTable.SearchById(effect.SourceActorId)?.Name.TextValue;
                var status = Service.DataManager.GetExcelSheet<Status>().GetRowOrDefault(effectId);

                combatEvents.AddEntry(targetId,
                    new CombatEvent.StatusEffect {
                        Snapshot = p.Snapshot(),
                        Id = effectId,
                        StackCount = effect.StackCount <= status?.MaxStacks ? effect.StackCount : 0u,
                        Icon = status?.Icon,
                        Status = status?.Name.ExtractText(),
                        Description = status?.Description.ExtractText(),
                        Category = (StatusCategory)(status?.StatusCategory ?? 0),
                        Source = source,
                        Duration = effect.Duration
                    });
            }
        } catch (Exception e) {
            Service.PluginLog.Error(e, "Caught unexpected exception");
        }
    }

    // 封包掛鉤與 Framework.Update 都在框架執行緒，這兩個欄位不必加鎖。
    private bool selfDeathLogged;
    private DateTime selfDeathLoggedAt;

    private void LogSelfDeath(uint maxHp, DateTime timeOfDeath, List<CombatEvent>? events) {
        // 同一次死亡只寫一行：寫過之後要等 TickSelfAlive 看到活著才再寫。
        if (selfDeathLogged)
            return;
        selfDeathLogged = true;
        selfDeathLoggedAt = timeOfDeath;

        try {
            var territoryId = Service.ClientState.TerritoryType;
            var territoryName = Service.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId)?.PlaceName.ValueNullable?.Name.ExtractText();
            var statusSheet = Service.DataManager.GetExcelSheet<Status>();
            var line = SelfDeathLog.Format(territoryId, territoryName, maxHp, timeOfDeath, events,
                id => statusSheet.GetRowOrDefault(id) is { } s
                    ? new SelfDeathLog.StatusInfo(s.Name.ExtractText(), (StatusCategory)s.StatusCategory, s.IsFcBuff)
                    : null);
            // 整行當訊息範本送出，名稱裡的大括號要跳脫，否則 Serilog 會當成佔位符吃掉。
            Service.PluginLog.Information(line.Replace("{", "{{").Replace("}", "}}"));
        } catch (Exception e) {
            Service.PluginLog.Error(e, "[DeathRecap][死亡] 組不出死亡紀錄");
        }
    }

    public void TickSelfAlive() {
        if (!selfDeathLogged)
            return;
        if (Service.ObjectTable.LocalPlayer is { } self && SelfDeathLog.SeenAliveAfterDeath(selfDeathLoggedAt, DateTime.Now, self.CurrentHp))
            selfDeathLogged = false;
    }

    public void CleanCombatEvents() {
        try {
            var entriesToRemove = new List<ulong>();
            foreach (var (id, events) in combatEvents) {
                if (events.Count == 0 || (DateTime.Now - events.Last().Snapshot.Time).TotalSeconds > plugin.Configuration.KeepCombatEventsForSeconds) {
                    entriesToRemove.Add(id);
                    continue;
                }

                var cutOffTime = DateTime.Now - TimeSpan.FromSeconds(plugin.Configuration.KeepCombatEventsForSeconds);
                for (var i = 0; i < events.Count; i++)
                    if (events[i].Snapshot.Time > cutOffTime) {
                        events.RemoveRange(0, i);
                        break;
                    }
            }

            foreach (var entry in entriesToRemove)
                combatEvents.Remove(entry);

            entriesToRemove.Clear();

            foreach (var (id, death) in plugin.DeathsPerPlayer) {
                if (death.Count == 0 || (DateTime.Now - death.Last().TimeOfDeath).TotalMinutes > plugin.Configuration.KeepDeathsForMinutes) {
                    entriesToRemove.Add(id);
                    continue;
                }

                var cutOffTime = DateTime.Now - TimeSpan.FromMinutes(plugin.Configuration.KeepDeathsForMinutes);
                for (var i = 0; i < death.Count; i++)
                    if (death[i].TimeOfDeath > cutOffTime) {
                        death.RemoveRange(0, i);
                        break;
                    }
            }

            foreach (var entry in entriesToRemove)
                plugin.DeathsPerPlayer.Remove(entry);
        } catch (Exception e) {
            Service.PluginLog.Error(e, "Error while clearing events");
        }
    }

    public void Dispose() {
        processPacketActionEffectHook?.Dispose();
        processPacketEffectResultHook?.Dispose();
        processPacketActorControlHook?.Dispose();
    }
}
