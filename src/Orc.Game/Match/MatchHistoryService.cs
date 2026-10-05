using Orc.Cards;
using Orc.Core;
using Orc.Game.Cards;
using Orc.Game.Players;

namespace Orc.Game;

// ─────────────────────────────────────────────────────────────────────────────
// S10（G14补）对局历史读取服务：「事件流读取能力落地」——最小历史读取面。
// ①判据：除机制层既有可枚举面外，提供「按条目类型（信号）筛取＋时序取用」的公共历史读取能力——
//   「仅测试枚举手筛」不满足「能力落地」判据（本面即受支持的能力面）。
// ②历史来源＝事件流（引擎总流 <see cref="LogicEngine.RootStream"/>）：读取窗口＝对局自创建以来、
//   写入时序的全部相关条目（现状「不设裁剪与上限」——全程可读）；不建专门记录结构、不新增信号。
// ③查询范围＝全部信号类型（按类型参数化筛取——不做「仅 card.died」的窄面）。
// ④「上1个」＝写入时序的最后一条（最近）；返回条目可直接供消费（条目数据含对象引用——
//   如 card.died 条目的卡引用可继续参与后续操作〔S9 转换等组合〕）。
// ⑤承载层级＝对局域薄封装：历史作用域＝对局（随对局回收、跨对局不共享）；经既有「卡 → 玩家 → 服务」
//   同构路径可达（<see cref="ResolveFor"/>——与 GameEnvironment / MatchRandomService / MatchCardService 同构）；
//   脱局场景（未注入/无对局）＝null（降级不抛错——沿用服务注入先例）。
// ⑥拉取式按需读取（不引入常驻预计算）；空结果＝空列表/null、不抛错；读取面不接入环境重跑体系
//   （读取为拉取式、不新增重跑触发源）。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 对局历史读取服务（S10：最小历史读取面——「按条目类型（信号）筛取＋时序取用」；
/// 详见文件头注释）。对外面：<see cref="GetEntries"/>（按类型筛取，写入时序）／
/// <see cref="GetLastEntry(string)"/>（最近一条）／<see cref="GetLastEntry(string, Func{LogEntry, bool})"/>
/// （最近一条＋谓词过滤——如「上1个被消灭的友方单位」的友方过滤＝消费方组合）；
/// 卡上的接入＝<see cref="ResolveFor"/>（「卡 → 玩家 → 服务」）。
/// </summary>
public sealed class MatchHistoryService
{
    private readonly LogicEngine _engine;

    /// <summary>创建历史读取服务（对局装配路径经 <see cref="Match"/> 创建——每对局一份；读源＝引擎总流）。</summary>
    /// <exception cref="ArgumentNullException">engine 为 null。</exception>
    public MatchHistoryService(LogicEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
    }

    /// <summary>
    /// 按条目类型（信号）筛取——全部相关条目（顺序＝写入时序；快照拷贝——其后新增条目不影响已返回结果）。
    /// 空结果＝空列表（不抛错）。筛取口径＝更新条目 ∧ 条目关键词恰含该信号字面值（总线写入契约；ordinal 同一性）。
    /// </summary>
    /// <param name="updateType">条目类型（信号名——如 <see cref="GameUpdates.CardDied"/>）。</param>
    /// <exception cref="ArgumentException">updateType 为 null/空白（调用方错误——fail-fast）。</exception>
    public IReadOnlyList<LogEntry> GetEntries(string updateType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updateType);
        var result = new List<LogEntry>();
        foreach (var entry in _engine.RootStream.Entries)
        {
            if (IsSignalEntry(entry, updateType))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    /// <summary>
    /// 时序取用——该类型的最后一条（最近；写入时序）。无符合＝null（不抛错）。
    /// 「上1个被消灭的友方单位」等「上1个」语义＝本读取面＋消费方过滤的组合（见带谓词重载）。
    /// </summary>
    /// <param name="updateType">条目类型（信号名）。</param>
    /// <exception cref="ArgumentException">updateType 为 null/空白（调用方错误——fail-fast）。</exception>
    public LogEntry? GetLastEntry(string updateType) => GetLastEntry(updateType, static _ => true);

    /// <summary>
    /// 时序取用（带谓词过滤）——从最近向远逐条判定，返回首条「类型匹配 ∧ 谓词成立」的条目
    /// （谓词＝消费方组合语义：如友方过滤「死亡卡归属玩家==读取方」、单位过滤等）；无符合＝null（不抛错）。
    /// </summary>
    /// <param name="updateType">条目类型（信号名）。</param>
    /// <param name="predicate">条目谓词（对载荷/来源的组合判定；非 null）。</param>
    /// <exception cref="ArgumentException">updateType 为 null/空白（调用方错误——fail-fast）。</exception>
    /// <exception cref="ArgumentNullException">predicate 为 null。</exception>
    public LogEntry? GetLastEntry(string updateType, Func<LogEntry, bool> predicate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updateType);
        ArgumentNullException.ThrowIfNull(predicate);

        var entries = _engine.RootStream.Entries;
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if (IsSignalEntry(entry, updateType) && predicate(entry))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// 卡 → 历史服务解析（读取路径「卡 → 玩家 → 服务」的收敛点；与 MatchCardService.ResolveFor 同构）：
    /// 卡经归属玩家取服务；未加载（无归属）/独立构造（未注入）/非卡实体＝null
    /// （不可达——调用侧按「功能不可用、不抛错、不失败」处置；脱局降级）。
    /// </summary>
    /// <exception cref="ArgumentNullException">card 为 null（防御拒绝——调用方错误口径）。</exception>
    public static MatchHistoryService? ResolveFor(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card switch
        {
            Hq hq => hq.Owner.HistoryService,
            CardBase cardBase => cardBase.Owner?.HistoryService,
            _ => null,
        };
    }

    /// <summary>信号条目判定：更新条目 ∧ 关键词恰含该信号字面值（总线写入契约：keywords＝恰单元素 [updateType]；ordinal 同一性）。</summary>
    private static bool IsSignalEntry(LogEntry entry, string updateType)
        => entry.Kind == LogEntryKind.Update
            && entry.Keywords.Count == 1
            && string.Equals(entry.Keywords[0], updateType, StringComparison.Ordinal);
}
