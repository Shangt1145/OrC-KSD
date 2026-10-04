using Orc.Core;

namespace Orc.Cards;

/// <summary>
/// 卡牌事件视图（S4 框架内部载体）：移除类更新（effect.removed / card.destroyed）与放置更新（card.placed）的通用装载视图。
/// 全可选（可从任意载荷绑定）；属性为载荷对象的弱类型引用（Card＝卡牌对象、Effect＝效果对象）。
/// </summary>
[ContextView]
public class CardEventView
{
    [Optional]
    [Read]
    public virtual object? Card { get; set; }

    [Optional]
    [Read]
    public virtual object? Effect { get; set; }
}

/// <summary>S4 更新与流程载荷键名约定（card.placed / effect.removed / card.destroyed；攻击/伤害结算流程视图属性同名）。</summary>
public static class PayloadKeys
{
    /// <summary>卡牌载荷键："Card"（值＝<see cref="Card"/> 对象引用；销毁类更新携带——对象引用而非 Ref，销毁后仍可读）。</summary>
    public const string Card = "Card";

    /// <summary>效果载荷键："Effect"（值＝<see cref="Effect"/> 对象引用；effect.removed 携带）。</summary>
    public const string Effect = "Effect";

    /// <summary>来源载荷键："Source"（攻击/伤害结算流程视图属性名）。</summary>
    public const string Source = "Source";

    /// <summary>目标载荷键："Target"。</summary>
    public const string Target = "Target";

    /// <summary>数量载荷键："Amount"。</summary>
    public const string Amount = "Amount";

    /// <summary>指令载荷键："Order"（值＝<see cref="ICastAction"/> 指令接线面；指令流程（S5）携带）。</summary>
    public const string Order = "Order";
}

/// <summary>装载链留痕与错误记录工具（内部）：写「当前执行者流（无则总流）」，与会话内留痕一致冒泡。</summary>
internal static class CardsLog
{
    internal static void Write(
        LogicEngine engine, string source, string message, LogLevel level, IReadOnlyList<string> keywords)
    {
        var stream = ExecutionFrame.Current?.Stream ?? engine.RootStream;
        stream.WriteLog(source, message, level, keywords);
    }
}

/// <summary>
/// 卡牌装载链（S4；框架模板骨架）：放置驱动装载（幂等）与清理模板（收敛点）。
/// 装载链＝【挂主触发器 → OnMount → 装载完成动作（装载管线扩展点；如游戏层托管登记）】——
/// 任何装载入口（加载时点/放置驱动/Add 即时/词条内嵌）统一经此、复装自动重建（W3-A3）。
/// 清理模板被多个入口共用（RemoveEffect 容器入口 / effect.removed 驱动 / card.destroyed 驱动 / 主触发器模板事件），
/// 内部幂等——重复调用收敛为无操作，行为终态一致（不再生效、注入撤销、总线卸载、宿主引用清除、从 Effects 列表移除）；
/// 成功事务的实际移除命中恰发射一次 effect.removed（W3-A3；静默路径除外——未成功事务回滚与「源头即信号」的更新驱动）。
/// </summary>
internal static class CardLoadout
{
    /// <summary>
    /// 装载一个被动效果：主触发器挂载至总线 → 执行 OnMount（注入）→ 执行「装载完成动作」
    /// （装载管线扩展点，如游戏层托管登记——任何装载入口统一生效、复装自动重建；可缺省）。
    /// 幂等（已装载＝跳过）。失败（OnMount 抛 / 装载完成动作抛）：记录进事件流（source 含效果标识与钩子名/动作名）、
    /// 回滚装载（撤销注入、卸载主触发器、清除卸载清理登记残留——效果视为未生效、「不登记残留」）、不阻断其它效果与宿主。
    /// </summary>
    /// <returns>true＝本次完成装载；false＝跳过（已装载）或失败（已记录并回滚）。</returns>
    internal static bool MountEffect(Card card, Effect effect)
    {
        if (effect.IsMounted)
        {
            return false; // 幂等：重复放置/重复 Add 不重复装载
        }

        try
        {
            effect.ExecuteMount(card.Engine.Bus);
        }
        catch (Exception ex)
        {
            effect.RollbackMount();
            CardsLog.Write(
                card.Engine,
                $"{effect.Name}/OnMount",
                ex.Message,
                LogLevel.Error,
                new[] { "loadout", "error", $"exception:{ex.GetType().Name}", effect.Name });
            return false;
        }

        try
        {
            card.Engine.RunCardMountCompletedActions(card, effect);
        }
        catch (Exception ex)
        {
            // 「装载成功」的定义包含「自动动作完成」——动作异常归入装载失败口径：
            // 记录＋回滚为未生效＋不登记残留（回滚清除本次装载期间的全部卸载清理登记）。
            effect.RollbackMount();
            CardsLog.Write(
                card.Engine,
                $"{effect.Name}/MountCompleted",
                ex.Message,
                LogLevel.Error,
                new[] { "loadout", "error", $"exception:{ex.GetType().Name}", effect.Name });
            return false;
        }

        CardsLog.Write(
            card.Engine,
            "loadout",
            $"效果 '{effect.Name}' 已装载（注入完成）。",
            LogLevel.Info,
            new[] { "loadout", "mount", card.Name, effect.Name });
        return true;
    }

    /// <summary>
    /// 清理模板（收敛点；幂等）：对「确属该卡」的效果执行——从 Effects 列表移除；已装载 → OnUnmount → 撤销登记 → 总线卸载 → 清宿主引用；
    /// 未装载 → 仅容器面移除（清宿主引用，无运行态清理动作）。
    /// 归属校验：效果不在该卡列表且宿主引用不指向该卡 → 无操作返回 false（防跨卡误清；「移除不存在或已移除的效果＝幂等」）。
    /// 作者清理逻辑失败（OnUnmount 抛）：记录（source 含效果标识与钩子名）、不阻断其它效果的清理；框架撤销/卸载照常完成（「清理未完成，以实况计」）。
    /// 发射（W3-A3 加性面）：成功事务的「实际移除命中」＝恰发射一次 effect.removed（先清理〔落定〕后发射；
    /// 载荷＝{Card, Effect}；幂等无操作＝不发射）。emitRemoved=false＝静默清理路径（未成功事务的回滚专用——「无痕」；
    /// 亦用于「源头即信号」的 effect.removed 更新驱动的自身清理——不重复发射、防循环）。
    /// </summary>
    /// <returns>true＝执行了清理；false＝无操作（幂等/非本卡效果）。</returns>
    internal static bool CleanupEffect(Card card, Effect effect, bool emitRemoved = true)
    {
        var contains = card.ContainsEffect(effect);
        var hostMatch = ReferenceEquals(effect.HostOrNull, card);
        if (!contains && !hostMatch)
        {
            return false; // 非本卡效果/已清理：幂等无操作
        }

        card.RemoveEffectCore(effect);

        if (effect.IsMounted)
        {
            var failure = effect.ExecuteUnmount();
            if (failure is not null)
            {
                CardsLog.Write(
                    card.Engine,
                    $"{effect.Name}/OnUnmount",
                    failure.Message,
                    LogLevel.Error,
                    new[] { "loadout", "error", $"exception:{failure.GetType().Name}", effect.Name });
                CardsLog.Write(
                    card.Engine,
                    "loadout",
                    $"效果 '{effect.Name}' 已卸载（作者清理失败、框架撤销/卸载已完成；详见错误记录）。",
                    LogLevel.Info,
                    new[] { "loadout", "cleanup", card.Name, effect.Name });
            }
            else
            {
                CardsLog.Write(
                    card.Engine,
                    "loadout",
                    $"效果 '{effect.Name}' 已卸载（清理完成）。",
                    LogLevel.Info,
                    new[] { "loadout", "cleanup", card.Name, effect.Name });
            }
        }
        else
        {
            effect.ClearHost();
            CardsLog.Write(
                card.Engine,
                "loadout",
                $"效果 '{effect.Name}' 已从容器移除（未装载，无运行态清理）。",
                LogLevel.Debug,
                new[] { "loadout", "detach", card.Name, effect.Name });
        }

        if (emitRemoved)
        {
            // 先清理（落定）后发射（对外通知）——发射在清理模板完成之后；发射链异常＝沿用引擎总线既有语义
            //（取消类穿透、其余隔离记录；「发射失败不影响清理」——已落定）。
            EmitEffectRemoved(card, effect);
        }

        return true;
    }

    /// <summary>发射 effect.removed（载荷＝{ Card, Effect }——卡牌＋效果对象引用；经引擎总线 Emit、P2 同步）。</summary>
    private static void EmitEffectRemoved(Card card, Effect effect)
    {
        card.Engine.Emit(
            Updates.EffectRemoved,
            new Dictionary<string, object?>
            {
                [PayloadKeys.Card] = card,
                [PayloadKeys.Effect] = effect,
            }).GetAwaiter().GetResult();
    }
}

/// <summary>
/// 卡牌装载链处理器（S4；引擎级、懒挂载共享实例）：响应三个驱动更新，完成装载/清理编排。
/// 「卡牌放置处理器」（card.placed）：单位初始化（数据/状态就绪）先、效果注入后（Effects 列表序、幂等）；
/// 「卡牌清理处理器」（effect.removed / card.destroyed）：容器面移除＋卸载链（与效果主触发器的自我清理收敛于同一模板）。
/// 处理器仅响应载荷定位的卡牌/效果；对非相关载荷幂等无操作。
/// </summary>
internal sealed class CardLoadoutProcessor
{
    private readonly Trigger<CardEventView> _placedHandler;
    private readonly Trigger<CardEventView> _cleanupHandler;

    /// <summary>「放置处理」触发器（内置事件：放置处理；供 moding（逻辑替换）等经句柄寻址）。</summary>
    internal Trigger<CardEventView> PlacedTrigger => _placedHandler;

    /// <summary>「清理处理」触发器（内置事件：清理处理；供 moding（逻辑替换）等经句柄寻址）。</summary>
    internal Trigger<CardEventView> CleanupTrigger => _cleanupHandler;

    internal CardLoadoutProcessor(LogicEngine engine)
    {
        _placedHandler = new Trigger<CardEventView>(
            "卡牌放置处理器",
            TriggerKind.Passive,
            events: new[]
            {
                new TriggerEvent<CardEventView>("放置处理", OnCardPlaced),
            },
            hooks: new[] { Updates.CardPlaced });

        _cleanupHandler = new Trigger<CardEventView>(
            "卡牌清理处理器",
            TriggerKind.Passive,
            events: new[]
            {
                new TriggerEvent<CardEventView>("清理处理", OnCleanup),
            },
            hooks: new[] { Updates.EffectRemoved, Updates.CardDestroyed });

        engine.Bus.Mount(_placedHandler);
        engine.Bus.Mount(_cleanupHandler);
    }

    /// <summary>放置处理：初始化（幂等；先）→ 逐效果装载（Effects 列表序；被动且未装载者）；重复放置＝幂等（不重复装载/不重复 OnMount）。</summary>
    private Task OnCardPlaced(CardEventView view, Context ctx, CancellationToken ct)
    {
        var card = view.Card as Card;
        if (card is null)
        {
            return Task.CompletedTask; // 载荷无卡牌：无操作（防误用；总线隔离已保证不抛）
        }

        var wasPlaced = card.IsPlaced;
        card.MarkPlaced();
        if (!wasPlaced)
        {
            CardsLog.Write(
                card.Engine,
                "loadout",
                $"卡牌 '{card.Name}' 放置初始化完成（数据/状态就绪）。",
                LogLevel.Info,
                new[] { "loadout", "init", card.Name });
        }

        var mountedNow = 0;
        foreach (var effect in card.EffectsSnapshot())
        {
            if (effect.Kind != TriggerKind.Passive)
            {
                continue; // 主动效果不装载（等待施放）
            }

            if (CardLoadout.MountEffect(card, effect))
            {
                mountedNow++;
            }
        }

        if (wasPlaced && mountedNow == 0)
        {
            CardsLog.Write(
                card.Engine,
                "loadout",
                $"卡牌 '{card.Name}' 重复放置（幂等：不重复装载）。",
                LogLevel.Debug,
                new[] { "loadout", "skip", card.Name });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 清理处理：effect.removed（定位单个效果）/ card.destroyed（该卡全部效果）——均收敛到同一清理模板（幂等）。
    /// 发射语义（W3-A3）：effect.removed 驱动＝「源头即信号」——清理不重复发射（防重复/自循环）；
    /// card.destroyed 驱动的批量清理＝逐效果实际移除命中发 effect.removed（对称可观察——弃置/销毁未经逐一移除的场景亦可观察效果级移除）。
    /// </summary>
    private Task OnCleanup(CardEventView view, Context ctx, CancellationToken ct)
    {
        var effect = view.Effect as Effect;
        if (effect is not null)
        {
            var card = view.Card as Card;
            if (card is null)
            {
                return Task.CompletedTask; // 载荷契约：effect.removed 携带卡牌＋效果；无卡牌＝无操作
            }

            CardLoadout.CleanupEffect(card, effect, emitRemoved: false); // 源头即信号：不重复发射
            return Task.CompletedTask;
        }

        var destroyed = view.Card as Card;
        if (destroyed is null)
        {
            return Task.CompletedTask;
        }

        destroyed.Engine.UnregisterCard(destroyed); // S5：销毁清理响应时移除卡牌登记（一步式与两步（杀+发更新）路径均收敛于此；幂等）

        var cleaned = 0;
        foreach (var item in destroyed.EffectsSnapshot())
        {
            if (CardLoadout.CleanupEffect(destroyed, item)) // 逐效果：实际移除命中＝恰发射一次 effect.removed
            {
                cleaned++;
            }
        }

        if (cleaned > 0)
        {
            CardsLog.Write(
                destroyed.Engine,
                "loadout",
                $"卡牌 '{destroyed.Name}' 销毁处理完成（清理 {cleaned} 个效果）。",
                LogLevel.Info,
                new[] { "loadout", "destroy", destroyed.Name });
        }

        return Task.CompletedTask;
    }
}
