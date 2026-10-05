using Orc.Core;
using Orc.Game.Board;
using Orc.Game.Cards;
using Orc.Game.Collections;

namespace Orc.Game.Players;

/// <summary>
/// 玩家（对局数据类；本批不继承引擎 Entity——无引用/生命周期需求，实体化留后续批次评估）：
/// 资源（指挥点数 / 指挥点槽）＋ 卡组（名单）＋ 手牌（实例集）＋ HQ（总部实体）。
/// 读面直接可读（供测试断言与展示）；写面经管理器（资源结算＝资源管理器；卡组消耗/手牌装载＝玩家管理器；
/// HQ 数值变更＝HQ 数值路径〔伤害经指挥管理器调用 HQ 门户〕），无旁路修改入口。
/// W3-3 G11 受控变更：HQ 由「纯数据」实体化为独立总部实体（<see cref="Hq"/>；Entity＋组件容器＋
/// 词条/效果装载面，不入死亡/销毁链）——随 Player 创建并互持（「未绑定」在结构上不可达）；
/// 占位（支援线槽 0）由对局/战场装配完成（布局语义）；
/// <see cref="HqHealth"/> 为只读转发面（转发 HQ 有效血量；不构成第二真源——写入一律走 HQ 侧受控路径）；
/// 归零判定＝HQ 数值路径下游统一响应（终局：状态置结束＋胜者记录＝HQ 归零方之对手；其后动作入口拒绝）。
/// </summary>
public sealed class Player
{
    /// <summary>HQ 初始血量（20）。</summary>
    public const int InitialHqHealth = 20;

    /// <summary>
    /// 手牌上限（9）。G7 起启用——回合抽牌链路超限裁决：满手时新抽的牌不经手牌、直烧（移除＋销毁＋
    /// <c>card.discarded</c> 信号——KARDS 烧牌语义）；裁决点＝回合抽牌链路（起手装载不受裁决、维持静默）；
    /// 常量值与公开读面不变、超限判定为内部行为（不新增公开状态）。
    /// </summary>
    public const int HandLimit = 9;

    internal Player(int index, CardList deck, LogicEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        Index = index;
        Deck = deck;
        Hq = new Hq(this, engine); // W3-3：HQ 随 Player 创建（双向互持——未绑定状态在结构上不可达）
    }

    /// <summary>玩家索引（0＝玩家A/第一位玩家、1＝玩家B）。</summary>
    public int Index { get; }

    /// <summary>卡组（名单：剩余可抽的卡牌 id 序列；抽牌＝取首张并移除）。</summary>
    public CardList Deck { get; }

    /// <summary>手牌（卡牌实例集合）。</summary>
    public CardSet Hand { get; } = new();

    /// <summary>指挥点数（当前值；回合开始结算＝槽值〔设为〕；X3：回合结束与敌方回合内保留——不清零）。</summary>
    public int Points { get; internal set; }

    /// <summary>指挥点槽（回合开始 +1、至上限封顶；保留于回合结束）。</summary>
    public int PointSlots { get; internal set; }

    /// <summary>
    /// 总部实体（W3-3 G11：随 Player 创建并互持；Entity＋组件容器＋词条/效果装载面，不入死亡/销毁链；
    /// 占位/数值/目标承载等 HQ 面经本引用访问）。
    /// </summary>
    public Hq Hq { get; }

    /// <summary>
    /// HQ 血量（初始 20）。〔W3-3 受控变更〕转发读面：只读转发 HQ 有效血量
    /// （＝HQ 修饰机制链输出缓存——「始终读有效值」；不构成第二真源，写入走 HQ 侧受控路径）；
    /// 不依赖入槽（未入槽亦可读——初值 20）。
    /// </summary>
    public int HqHealth => Hq.Health;

    /// <summary>
    /// 游戏环境（W3-1 G4 加性面；internal）：对局装配期注入（战场创建后、卡加载前）——
    /// 「卡 → 玩家 → 对局/战场」读取路径的玩家环节（链/条件节/收集步骤经卡归属玩家的本引用取环境）；
    /// 脱局场景（未注入）＝null（无环境面——相关读取自然产出原值、不抛错）。
    /// </summary>
    internal GameEnvironment? Environment { get; private set; }

    /// <summary>
    /// 装配期注入游戏环境（W3-1 G4；由对局装配路径调用——一次性注入；重复注入＝明确拒绝（fail-fast）。
    /// 时序：须先于任何卡加载（效果装载注册与链读取依赖环境就绪）。
    /// </summary>
    /// <exception cref="ArgumentNullException">environment 为 null。</exception>
    /// <exception cref="InvalidOperationException">环境已注入（重复注入被拒绝）。</exception>
    internal void ConfigureEnvironment(GameEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (Environment is not null)
        {
            throw new InvalidOperationException("玩家环境已注入（重复注入被拒绝）。");
        }

        Environment = environment;
    }

    /// <summary>
    /// 对局随机服务（第 2 批 G8 加性面；internal）：对局装配期注入——「卡 → 玩家 → 服务」读取路径的玩家环节
    /// （效果运行期经 <see cref="MatchRandomService.ResolveFor"/> 取用）；脱局场景（未注入）＝null（无服务面——
    /// 相关解析自然产出 null、不抛错）。
    /// </summary>
    internal MatchRandomService? RandomService { get; private set; }

    /// <summary>
    /// 装配期注入对局随机服务（第 2 批 G8；由对局装配路径调用——一次性注入；重复注入＝明确拒绝（fail-fast）。
    /// 时序：与装配一致（先于卡加载；显式、可测试——无隐藏全局单例）。
    /// </summary>
    /// <exception cref="ArgumentNullException">service 为 null。</exception>
    /// <exception cref="InvalidOperationException">随机服务已注入（重复注入被拒绝）。</exception>
    internal void ConfigureRandomService(MatchRandomService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (RandomService is not null)
        {
            throw new InvalidOperationException("玩家随机服务已注入（重复注入被拒绝）。");
        }

        RandomService = service;
    }

    /// <summary>
    /// 再触发服务（A4 加性面；internal）：对局装配期注入——「卡 → 玩家 → 服务」读取路径的玩家环节
    /// （效果运行期经 <see cref="RetriggerSystem.ResolveFor"/> 取用）；
    /// 脱局场景（未注入）＝null（无服务面——解析自然产出 null、不抛错）。
    /// </summary>
    internal RetriggerSystem? RetriggerService { get; private set; }

    /// <summary>
    /// 装配期注入再触发服务（A4；由对局装配路径调用——一次性注入；重复注入＝明确拒绝（fail-fast）。
    /// 时序：与装配一致（先于卡加载；显式、可测试——无隐藏全局单例）。
    /// </summary>
    /// <exception cref="ArgumentNullException">service 为 null。</exception>
    /// <exception cref="InvalidOperationException">再触发服务已注入（重复注入被拒绝）。</exception>
    internal void ConfigureRetriggerService(RetriggerSystem service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (RetriggerService is not null)
        {
            throw new InvalidOperationException("玩家再触发服务已注入（重复注入被拒绝）。");
        }

        RetriggerService = service;
    }

    /// <summary>
    /// 对局卡牌服务（S9 加性面；internal）：对局装配期注入——「卡 → 玩家 → 服务」读取路径的玩家环节
    /// （效果运行期经 <see cref="MatchCardService.ResolveFor"/> 取用——生成/复制/转换的「卡牌工厂＋放置面」）；
    /// 脱局场景（未注入）＝null（无服务面——解析自然产出 null、不抛错）。
    /// </summary>
    internal MatchCardService? CardService { get; private set; }

    /// <summary>
    /// 装配期注入对局卡牌服务（S9；由对局装配路径调用——一次性注入；重复注入＝明确拒绝（fail-fast）。
    /// 时序：与装配一致（先于卡加载；显式、可测试——无隐藏全局单例）。
    /// </summary>
    /// <exception cref="ArgumentNullException">service 为 null。</exception>
    /// <exception cref="InvalidOperationException">卡牌服务已注入（重复注入被拒绝）。</exception>
    internal void ConfigureCardService(MatchCardService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (CardService is not null)
        {
            throw new InvalidOperationException("玩家卡牌服务已注入（重复注入被拒绝）。");
        }

        CardService = service;
    }
}
