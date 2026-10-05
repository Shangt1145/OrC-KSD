using Orc.Cards;
using Orc.Core;

namespace Orc.Game.Cards;

// ─────────────────────────────────────────────────────────────────────────────
// 第 2 批·A4 亡计体系（口径依据《需求文档（A4）》Q&A-1/2/6）：
// 亡计词条组件（内容型：内部效果＝死亡结算动作〔复用 Effect 体系〕；「获得亡计」＝挂载词条、内容经内嵌效果通道）；
// 亡计内容效果（动作型：执行面直接驱动——不经订阅面）；统一执行上下文（宿主卡＋引擎引用）；
// 亡计执行面（单源：死亡结算与再触发共用同一执行面——解析宿主卡亡计词条 → 驱动其内部效果动作一次）。
// 「亡计结算」步＝统一死亡流程（CommandManager.ProcessDeathAsync）置毁之后、词条死亡注销之前（插入点定稿见 Q&A-1）。
// 再触发（发布/接收/驱动）与部署重放见 RetriggerSystem.cs；部署逻辑登记/生成面见 DeploymentLogicData.cs。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 亡计词条组件（内容型；A4）：内部效果＝死亡结算动作（内嵌效果通道承载——授予装载/移除卸载/授予失败回滚，
/// 生命周期全部继承内嵌效果既有口径）。
/// 内容载荷只经运行时授予通道提供（「内容装载点」注入——<see cref="KeywordManager.GrantWithContentAsync"/>；
/// 定义期声明面与亡计内容无关）；空内容（声明或授予）＝合法静默（结算时无操作）。
/// 行为面：无主动运行逻辑（Mount/Unmount 不覆写）；触发由执行面驱动（<see cref="DeathrattleRules.ExecuteAsync"/>——
/// 死亡结算/再触发共用），不经订阅面。参值位无语义（内容型词条）；不打对战词条标。
/// </summary>
public sealed class DeathrattleKeywordComponent : KeywordComponent
{
    /// <summary>创建亡计词条组件（空内容形态——内容经授予通道的「内容装载点」注入）。</summary>
    public DeathrattleKeywordComponent()
        : base(KeywordIds.Deathrattle)
    {
    }

    /// <inheritdoc />
    internal override void AttachContent(Effect? content)
    {
        if (content is not null)
        {
            EmbedEffect(content); // 内容＝死亡结算动作（实例为基准；装载/卸载随词条组件生灭）
        }
    }
}

/// <summary>
/// 亡计内容效果（动作型；A4）：内部效果的约定基类——自含「死亡结算动作」（<see cref="OnExecuteAsync"/>），
/// 由亡计执行面在死亡结算/再触发时驱动一次（<see cref="ExecuteAsync"/>——独立于订阅面；不自设订阅自触发）。
/// 按效果体系装载（继承 <see cref="PassiveEffect"/>：被动装载语境——挂生命周期触发器＋托管登记，由内嵌效果通道自动完成）。
/// 动作约定：无交互、可同步完成（越界使用由效果作者承担——机制不设专用防护）。
/// 宿主状态随语境：死亡结算＝已置毁、位置字段可读（清理前结算）；再触发＝该卡当前（存活）状态。
/// </summary>
public abstract class DeathrattleEffect : PassiveEffect
{
    /// <summary>创建亡计内容效果（动作型；被动装载语境——生命周期完整）。</summary>
    protected DeathrattleEffect(string name)
        : base(name)
    {
    }

    /// <summary>
    /// 亡计动作（作者覆写；执行面在死亡结算/再触发时驱动一次）：接收统一上下文（宿主卡＋引擎引用）。
    /// 约定无交互、可同步完成；异常由执行面隔离记录（不中断死亡流程/不中断遍历）。
    /// </summary>
    protected abstract Task OnExecuteAsync(DeathrattleContext context, CancellationToken ct);

    /// <summary>动作入口（执行面调用；框架面——作者覆写 <see cref="OnExecuteAsync"/>）。</summary>
    internal Task ExecuteAsync(DeathrattleContext context, CancellationToken ct)
        => OnExecuteAsync(context, ct);
}

/// <summary>
/// 亡计统一执行上下文（A4；执行面向内部效果提供的可见信息——两条路径同构）：
/// 宿主卡引用（<see cref="Unit"/>——死亡结算＝已置毁/位置字段可读；再触发＝存活状态）＋引擎引用（<see cref="Engine"/>，写效果所需）。
/// 由执行面在驱动内容效果时构造（每次执行独立实例）。
/// </summary>
public sealed class DeathrattleContext
{
    internal DeathrattleContext(UnitCard unit, LogicEngine engine)
    {
        Unit = unit;
        Engine = engine;
    }

    /// <summary>宿主单位卡（本次执行所属；状态随语境——死亡结算/再触发）。</summary>
    public UnitCard Unit { get; }

    /// <summary>引擎引用（写效果所需：发射更新 / 触发子触发器）。</summary>
    public LogicEngine Engine { get; }
}

/// <summary>
/// 亡计规则服务（A4）：授予面（「获得亡计」）＋亡计执行面（单源共享——死亡结算与再触发）。
/// </summary>
public static class DeathrattleRules
{
    /// <summary>
    /// 获得亡计（授予路径）：挂载「亡计」词条（携带内容载荷＝死亡结算动作；经内嵌效果通道）。
    /// 空内容＝合法（空内容授予——结算时无操作）；重复授予＝幂等（无操作、返回 false——既有词条口径）；
    /// 对已死亡卡＝拒绝（明确异常）。内容生命周期随词条组件生灭（授予装载/移除卸载/授予失败回滚无残留）。
    /// 授予对象＝卡牌（<see cref="CardBase"/>——单位卡为实际语义承载；纯内核卡无词条面）。
    /// </summary>
    /// <exception cref="ArgumentNullException">card 为 null。</exception>
    /// <exception cref="InvalidOperationException">对已死亡卡授予（终态拒绝）。</exception>
    public static Task<bool> GrantAsync(CardBase card, DeathrattleEffect? content)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.Keywords.GrantWithContentAsync(KeywordIds.Deathrattle, content);
    }

    /// <summary>
    /// 亡计执行面（单源；死亡结算与再触发共用）：解析宿主卡亡计词条（行为态——死亡注销后不可见、天然跳过）→
    /// 驱动其内部效果动作一次。
    /// 无词条面/无亡计词条/行为态未在/空内容＝无操作（合法静默、零副作用）；内部效果逐条异常隔离
    /// （记录并继续——对齐死亡链/部署链健壮性口径；取消类异常穿透）。
    /// 单源化（硬性口径）：两路径行为一致——同一内容不因触发源不同而执行差异。
    /// </summary>
    /// <returns>true＝至少驱动一个内容效果动作；false＝无操作（静默跳过）。</returns>
    internal static async Task<bool> ExecuteAsync(UnitCard unit, LogicEngine engine, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(engine);

        if (KeywordRules.TryGetKeywordManager(unit) is not { } keywords
            || !keywords.TryGetActiveComponent<DeathrattleKeywordComponent>(KeywordIds.Deathrattle, out var rattle))
        {
            return false; // 无词条面/无亡计词条/行为态未在（如死亡注销后）：静默跳过
        }

        var content = rattle.EmbeddedEffects;
        if (content.Count == 0)
        {
            return false; // 空内容＝合法静默（结算时无操作）
        }

        var context = new DeathrattleContext(unit, engine);
        var executed = false;
        foreach (var effect in content)
        {
            if (effect is not DeathrattleEffect action)
            {
                engine.RootStream.WriteLog(
                    "亡计",
                    $"内容效果 '{effect.Name}' 非动作型（约定：内容＝动作型效果；越界使用由效果作者承担）——跳过驱动。",
                    LogLevel.Warning,
                    new[] { "deathrattle", "content-skip" });
                continue;
            }

            try
            {
                await action.ExecuteAsync(context, ct);
                executed = true;
            }
            catch (OperationCanceledException)
            {
                throw; // 取消类异常不隔离（沿用引擎口径）
            }
            catch (Exception ex)
            {
                engine.RootStream.WriteLog(
                    "亡计",
                    $"亡计动作执行异常（隔离：继续结算/遍历）：'{effect.Name}'——{ex.Message}",
                    LogLevel.Error,
                    new[] { "deathrattle", "error", $"exception:{ex.GetType().Name}" });
            }
        }

        return executed;
    }
}
