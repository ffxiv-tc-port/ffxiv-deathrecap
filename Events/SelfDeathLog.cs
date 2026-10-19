using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DeathRecap.Events;

/// <summary>本機玩家死亡時寫進插件 log 的一行。純函式：不碰遊戲狀態，狀態名稱由呼叫端查表傳入。</summary>
/// <remarks>
/// 只給本機玩家用；來源是玩家的傷害一律寫「玩家」不寫名字（個資）。
/// 值為「?」＝不知道（沒擷取到），「無」＝擷取到了但確實沒有，兩者不可混用。
/// </remarks>
public static class SelfDeathLog {
    public const string Prefix = "[DeathRecap][死亡]";
    public const int MaxHits = 3;
    public const int MaxStatusNames = 8;
    public const int HealWindowSeconds = 10;
    public const string PlayerSource = "玩家";

    // 死亡封包與 HP 歸零未必同一幀到；太早看到 HP>0 會把同一次死亡當成已復活。
    public static readonly TimeSpan AliveCheckDelay = TimeSpan.FromSeconds(3);

    public readonly record struct StatusInfo(string Name, StatusCategory Category, bool IsFcBuff);

    public static bool IsTargetMitigation(uint statusId) =>
        // 1203 Addle, 1195 Feint, 1193 Reprisal, 860 Dismantled,
        // 1715 Malodorous (BLU Bad Breath), 2115 Conked (BLU Magic Hammer), 3642 Candy Cane (BLU)
        statusId is 1203 or 1195 or 1193 or 860 or 1715 or 2115 or 3642;

    public static bool SeenAliveAfterDeath(DateTime loggedAt, DateTime now, uint currentHp) =>
        currentHp > 0 && now - loggedAt >= AliveCheckDelay;

    public static string Format(
        uint territoryId, string? territoryName, uint maxHp, DateTime timeOfDeath,
        IReadOnlyList<CombatEvent>? events, Func<uint, StatusInfo?> lookupStatus) {
        var sb = new StringBuilder(320);
        sb.Append(Prefix);
        sb.Append(" 區域=").Append(Num(territoryId)).Append('(').Append(Text(territoryName)).Append(')');
        sb.Append(" 最大HP=").Append(maxHp > 0 ? Num(maxHp) : "?");
        sb.Append(" 事件=").Append(Num((uint)(events?.Count ?? 0)));

        if (events is not { Count: > 0 }) {
            sb.Append(" 傷害=? ").Append(Num((uint)HealWindowSeconds)).Append("秒內治療=? 狀態=?");
            return sb.ToString();
        }

        AppendHits(sb, events, timeOfDeath);
        AppendHealCount(sb, events, timeOfDeath);
        AppendStatuses(sb, events, timeOfDeath, lookupStatus);
        return sb.ToString();
    }

    private static void AppendHits(StringBuilder sb, IReadOnlyList<CombatEvent> events, DateTime timeOfDeath) {
        var hits = 0;
        for (var i = events.Count - 1; i >= 0 && hits < MaxHits; i--) {
            string source, action;
            uint amount;
            switch (events[i]) {
                case CombatEvent.DamageTaken { Amount: > 0 } dt:
                    source = dt.SourceIsPlayer ? PlayerSource : Text(dt.Source);
                    action = Text(dt.Action);
                    amount = dt.Amount;
                    break;
                case CombatEvent.DoT { Amount: > 0 } dot:
                    source = "?";
                    action = "持續傷害";
                    amount = dot.Amount;
                    break;
                default:
                    continue;
            }

            hits++;
            var snap = events[i].Snapshot;
            sb.Append(" 傷害").Append(Num((uint)hits)).Append("=(")
                .Append(Seconds(snap.Time - timeOfDeath))
                .Append(" 來源=").Append(source)
                .Append(" 技能=").Append(action)
                .Append(" 量=").Append(Num(amount))
                .Append(" 前HP=").Append(HpPercent(snap.CurrentHp, snap.MaxHp));
            if (snap.BarrierPercent > 0)
                sb.Append("+盾").Append(Num(snap.BarrierPercent)).Append('%');
            sb.Append(')');
        }

        if (hits == 0)
            sb.Append(" 傷害=無");
    }

    private static void AppendHealCount(StringBuilder sb, IReadOnlyList<CombatEvent> events, DateTime timeOfDeath) {
        var since = timeOfDeath - TimeSpan.FromSeconds(HealWindowSeconds);
        uint heals = 0;
        foreach (var e in events) {
            if (e.Snapshot.Time >= since && e is CombatEvent.Healed { Amount: > 0 } or CombatEvent.HoT { Amount: > 0 })
                heals++;
        }

        sb.Append(' ').Append(Num((uint)HealWindowSeconds)).Append("秒內治療=").Append(Num(heals));
    }

    private static void AppendStatuses(
        StringBuilder sb, IReadOnlyList<CombatEvent> events, DateTime timeOfDeath, Func<uint, StatusInfo?> lookupStatus) {
        CombatEvent.EventSnapshot? snap = null;
        for (var i = events.Count - 1; i >= 0 && snap == null; i--) {
            if (events[i].Snapshot.StatusEffects != null)
                snap = events[i].Snapshot;
        }

        if (snap?.StatusEffects is not { } statuses) {
            sb.Append(" 狀態=?");
            return;
        }

        var buffs = new List<string>();
        var debuffs = new List<string>();
        var targetMitigation = new List<string>();
        foreach (var s in statuses) {
            var info = lookupStatus(s.Id);
            if (IsTargetMitigation(s.Id)) {
                AddDistinct(targetMitigation, info is { Name.Length: > 0 } t ? t.Name : Num(s.Id));
                continue;
            }

            if (info is not { Name.Length: > 0, IsFcBuff: false } known)
                continue;
            if (known.Category == StatusCategory.Beneficial)
                AddDistinct(buffs, known.Name);
            else if (known.Category == StatusCategory.Detrimental)
                AddDistinct(debuffs, known.Name);
        }

        sb.Append(" 快照=").Append(Seconds(snap.Time - timeOfDeath));
        sb.Append(" 增益=").Append(JoinNames(buffs));
        sb.Append(" 減益=").Append(JoinNames(debuffs));
        sb.Append(" 敵減傷=").Append(JoinNames(targetMitigation));
    }

    private static void AddDistinct(List<string> list, string name) {
        if (!list.Contains(name))
            list.Add(name);
    }

    private static string JoinNames(List<string> names) {
        if (names.Count == 0)
            return "無";
        if (names.Count <= MaxStatusNames)
            return string.Join(',', names);
        return string.Join(',', names.GetRange(0, MaxStatusNames)) + "(+" + Num((uint)(names.Count - MaxStatusNames)) + ")";
    }

    // 有血不寫 0%、沒滿不寫 100%：四捨五入會把「還剩一點」與「已滿」寫成誤導的端點值。
    private static string HpPercent(uint current, uint max) {
        if (max == 0)
            return "?";
        var pct = (uint)Math.Round(current * 100.0 / max, MidpointRounding.AwayFromZero);
        if (current > 0 && pct == 0)
            pct = 1;
        else if (current < max && pct >= 100)
            pct = 99;
        return Num(pct) + "%";
    }

    private static string Seconds(TimeSpan delta) => delta.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";

    private static string Num(uint value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "?" : value.Trim();
}
