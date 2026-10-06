using Orc.Core;
using Orc.Game.Managers;
using Orc.Game.Players;
using Orc.Game.Targeting;
using Orc.Game.Triggers;

namespace Orc.Game.Cards;

/// <summary>
/// 指令卡（三大类之一）：打出即生效的主动效果载体（2B 打出链就绪）。
/// 数据组件装配（E 区差异化）：＝阵营〔国籍〕＋部署费合并组件（基类）；「指令无其他组件」——不装配对战/单位/指挥组件。
/// 触发器（2B）：
/// ①预打出触发器（费用校验）：默认无交互、零更新；装配方可经 <see cref="AddPrePlayHandler"/> 加 handler 捕获引用
///   （以 targeter 产出为主要场景、不强制唯一；捕获经 <see cref="CardTriggerView.CaptureBox"/> 提交；
///   可经捕获箱 CancelPrePlay 请求取消预打出——预打出段失败/取消 ⇒ 打出不发生）；
/// ②打出触发器（费用校验——外层复验）：默认链事件＝【打出宣告〔card.played〕→（主动 handler 集，装配期注册）
///   → 打出收尾〔扣费→离手〕】；object? 参数（<see cref="CardTriggerView.Argument"/>）承载预打出捕获结果
///   （单引用/列表/targeter 皆可）；主动 handler 集经 <see cref="AddActiveHandler"/> 装配注册
///   （登记序触发；异常沿用引擎隔离——记录并继续）。
/// 加载模板与其余装配沿用基类（<see cref="CardBase"/>）；持久化重建经基类扩展点。
/// </summary>
public class CommandCard : CardBase
{
    /// <summary>打出收尾事件优先级（默认区段）：主动 handler 用默认 0——位于「打出宣告」与「打出收尾」之间。</summary>
    private const int PlayFinalizePriority = 100;

    private readonly LogicEngine _chainEngine;

    /// <summary>创建指令卡（触发器与默认链事件在构造期装配；
    /// 费用校验触发器按名绑定费用检查判定器——解析器缺省＝内置默认〔独立构造即可用〕）。</summary>
    /// <param name="engine">引擎（发射/触发）。</param>
    /// <param name="definition">卡牌定义。</param>
    /// <param name="validationJudicatorResolver">验证判定器解析器（按名解析——对局路径＝注册表解析；
    /// 缺省＝null＝独立构造路径——内置默认解析）。</param>
    /// <exception cref="ArgumentNullException">engine 或 definition 为 null。</exception>
    public CommandCard(
        LogicEngine engine,
        CardDefinition definition,
        Func<string, JudicatorBinding>? validationJudicatorResolver = null)
        : base(engine, definition)
    {
        _chainEngine = engine;

        PrePlayTrigger = new CostCheckTrigger("预打出触发器", this, validationJudicatorResolver);
        PlayTrigger = new CostCheckTrigger("打出触发器", this, validationJudicatorResolver);

        // 默认链事件：宣告（优先 0）→（主动 handler 集按注册序）→ 收尾（优先 100）。
        PlayTrigger.Register("打出宣告", HandlePlayAnnounceAsync);
        PlayTrigger.Register("打出收尾", HandlePlayFinalizeAsync, PlayFinalizePriority);
    }

    /// <summary>预打出触发器（费用校验：指挥点验证；装配方 handler 捕获集经此挂载）。</summary>
    public Trigger<CardTriggerView> PrePlayTrigger { get; }

    /// <summary>打出触发器（费用校验＋默认链：复验＋宣告＋主动 handler 集＋收尾）。</summary>
    public Trigger<CardTriggerView> PlayTrigger { get; }

    /// <summary>
    /// 手牌起始指向槽位声明（S2：由指令卡自行声明——基类不持 targeter）：指令＝手牌打出时指向单位/HQ
    /// （候选域由打出/交互路径填充）；槽位参数＝本卡（起始卡牌）。声明固定、随实例复用。
    /// </summary>
    public IReadOnlyList<TargetSlot> HandOriginSlots { get; } =
        new TargetSlot[] { new SingleSelectSlot(SelectorSlots.HandOrigin) };

    /// <summary>
    /// 预打出 handler 装配入口（增强入口；装配/加载阶段注册——本批不设运行期动态注册）：
    /// 注册为预打出触发器事件（登记序触发；异常沿用引擎隔离）；handler 经视图 <c>CaptureBox</c> 提交捕获值
    /// （或经 targeter 交互后提交）。返回注册句柄（可用于撤销）。
    /// </summary>
    /// <exception cref="ArgumentNullException">handler 为 null。</exception>
    /// <exception cref="ArgumentException">name 为 null/空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException">priority 越界。</exception>
    public TriggerRegistration AddPrePlayHandler(
        string name, Func<CardTriggerView, Context, CancellationToken, Task> handler, int priority = 0)
        => PrePlayTrigger.Register(name, handler, priority);

    /// <summary>
    /// 主动 handler 集装配入口（增强入口；装配/加载阶段注册——本批不设运行期动态注册）：
    /// 注册为打出触发器事件（登记序触发；默认优先级——位于「打出宣告」与「打出收尾」之间；
    /// 异常沿用引擎隔离〔记录并继续〕）；handler 经视图 <c>Argument</c> 读取预打出捕获结果。
    /// 返回注册句柄（可用于撤销）。
    /// </summary>
    /// <exception cref="ArgumentNullException">handler 为 null。</exception>
    /// <exception cref="ArgumentException">name 为 null/空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException">priority 越界。</exception>
    public TriggerRegistration AddActiveHandler(
        string name, Func<CardTriggerView, Context, CancellationToken, Task> handler, int priority = 0)
        => PlayTrigger.Register(name, handler, priority);

    /// <summary>打出宣告（默认链首步）：发 card.played（W4-1 升级版载荷＝{ Card, Player }——被使用卡实例＋使用方；先于主动 handler 集）。</summary>
    private async Task HandlePlayAnnounceAsync(CardTriggerView view, Context ctx, CancellationToken ct)
    {
        if (view.Card is not CommandCard card || view.Player is not Player player)
        {
            ctx.Interrupt(); // 载荷缺失（结构性错误）：宣告不发生
            return;
        }

        await GameUpdates.EmitCardPlayed(_chainEngine, card, player, ct);
    }

    /// <summary>打出收尾（默认链末步）：扣费（恰一次；E1-25 后续：经点数通用入口发 point.changed）→ 离手（扣费之后；失败/取消时留手——由链前验证保证）。</summary>
    private async Task HandlePlayFinalizeAsync(CardTriggerView view, Context ctx, CancellationToken ct)
    {
        if (view.Card is not CommandCard card || view.Player is not Player player)
        {
            ctx.Interrupt(); // 载荷缺失（结构性错误）：收尾不发生
            return;
        }

        // W3-2 G5：扣费读「有效部署费」（修饰贡献叠加后的链输出——无修饰时＝基准；与校验/复验同口径）
        var cost = card.Modifiers.GetEffectiveValue(CardStatFields.DeployCost);
        if (ResourceManager.ResolveFor(card) is { } manager)
        {
            await manager.ChangePointsAsync(player, -cost, PointChangeKind.Add, ct).ConfigureAwait(false);
        }
        else
        {
            player.Points -= cost; // 脱局兜底（未装配资源管理器）：保持既有直写语义
        }

        player.Hand.Remove(card);
    }
}
