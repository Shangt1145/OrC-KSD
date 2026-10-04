using Orc.Core;
using Orc.Game.Players;

namespace Orc.Game.Cards;

/// <summary>
/// 卡牌基类骨架（三大类卡牌体系；2A 结构层）：单位 / 指令 / 反制的公共基座。
/// 继承引擎薄容器 <see cref="Orc.Cards.Card"/>——数据组件经既有组件体系挂载（AddData/GetData）、效果经 AddEffect 体系，
/// 与既有 CardDefinition / CardLibrary / 集合类衔接形态不变。
/// ①实例化路径数据组件装配（按类别差异化，需求原文 E 区）：全类别＝【指挥点花费】；
///   单位＝另加【对战】（<see cref="UnitCard"/> 构造装配）；指令＝花费（无其他）；反制＝花费（激活状态组件类就绪、挂载属后续批次）。
///   四合一 CardStatsData 已退役、无双真源。
/// ②对局开始卡牌加载模板（<see cref="LoadAsync"/>）：固定链＝【重建/装配（可重写扩展点、默认空实现）→ 词条装载（2C 加性）→ 广播 card.load】；
///   持久化重建本批仅显式预留空位（无任何重建逻辑；总装阶段实现）。
/// ③触发器声明位：预打出 / 打出 / 使用反制——对象在位于具体子类（构造期创建、装配点就绪）；链内容属打出链批次（2B）。
/// 加载失败（未注册 id、加载模板异常等）＝初始化 fail-fast（不进入进行态、明确上抛——加载路径保证）。
/// </summary>
public abstract class CardBase : Orc.Cards.Card
{
    private readonly LogicEngine _engine;

    /// <summary>创建卡牌实例（引擎绑定＋名称取自定义＋全类别基础数据组件装配〔指挥点花费〕）。</summary>
    /// <exception cref="ArgumentNullException">engine 或 definition 为 null（name 校验沿用 Entity）。</exception>
    protected CardBase(LogicEngine engine, CardDefinition definition)
        : base(engine, DefinitionName(definition))
    {
        _engine = engine;
        Definition = definition;

        // 实例化路径数据组件装配（旧 → 新：原四合一 CardStatsData → 指挥点花费〔全类别〕＋对战〔单位专属，见 UnitCard〕）。
        AddData(new CommandPointCostData(definition.DeployCost));
    }

    /// <summary>本卡的定义（代码注册形态；名称/类别/四项数值可读）。</summary>
    public CardDefinition Definition { get; }

    /// <summary>
    /// 所属玩家（2B 加性面；加载时装配——<see cref="LoadAsync"/> 记录；未加载＝null）。
    /// 打出链的验证（指挥点）、扣费与离手（自手牌移除）以归属为前提；未加载卡不可驱动打出链。
    /// </summary>
    public Player? Owner { get; private set; }

    /// <summary>
    /// 回合上下文（2B 加性面；internal）：当前行动方读取提供器——反制「仅己方回合」验证所需的对局上下文；
    /// 由对局加载路径经卡牌库注入（延迟读取、随对局回合推进取当前值）；独立构造（脱离对局）＝null
    /// （反制使用将无法通过回合验证——拒绝、可观测）。
    /// </summary>
    internal Func<Player?>? TurnPlayerProvider { get; set; }

    /// <summary>
    /// 词条装载上下文提供器（2C 加性面；internal）：加载时（<see cref="LoadAsync"/> 的词条装载步骤）装载主动词条逻辑
    /// 所需的对局级服务（如「造成攻击伤害」触发器——伏击挂载点）；由对局加载路径经卡牌库注入（延迟读取）；
    /// 独立构造（脱离对局）＝null（主动词条逻辑装载跳过注册、不抛错——功能不可用、加载不失败）。
    /// </summary>
    internal Func<KeywordLoadContext?>? KeywordLoadContextProvider { get; set; }

    /// <summary>
    /// 对局开始卡牌加载（模板方法；由加载路径逐张调用）：
    /// ① 装配归属（Owner＝所属卡组玩家；2B 加性：打出链验证/扣费/离手依据）；
    /// ② 重建/装配步骤（可重写扩展点 <see cref="RebuildFromPersistence"/>；默认空实现——持久化重建后置、本批无重建逻辑）；
    /// ③ 词条装载（2C 加性：从定义读词条 → 逐条登记至 KeywordData ＋ 装载主动词条逻辑〔伏击挂到「造成攻击伤害」〕；
    ///    无词条卡＝无副作用；登记/装载先于 card.load 广播——消费者查询不到未就绪状态）；
    /// ④ 广播 card.load（载荷 {Card, Player}——Card＝本实例、Player＝所属卡组玩家）。
    /// 加载路径保证每卡恰一次调用；失败直接上抛（初始化 fail-fast）。
    /// </summary>
    /// <exception cref="ArgumentNullException">owner 为 null。</exception>
    public async Task LoadAsync(Player owner, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(owner);

        Owner = owner;
        RebuildFromPersistence(owner);
        LoadKeywords();
        await GameUpdates.EmitCardLoad(_engine, owner, this, ct);
    }

    /// <summary>
    /// 词条装载（2C；加载时＝与 card.load 同时完成「登记＋效果装载」）：
    /// ① 词条登记（KeywordData＝卡牌固有属性——加载时挂载、卡组/手牌即可被读取查询；单位化不重复挂）；
    /// ② 词条运行态存储（KeywordRuntimeData——效果模块自维护；如奋战「本轮已攻次数」）；
    /// ③ 主动词条逻辑装载（闪击/伏击——除登记外作为逻辑组件；被动词条〔奋战/烟幕〕仅登记、由读取方按标识查询）。
    /// 无词条卡＝无副作用（不挂任何组件、不装载）。标识合法性与重复项已在定义期 fail-fast。
    /// </summary>
    private void LoadKeywords()
    {
        var keywords = Definition.Keywords;
        if (keywords.Count == 0)
        {
            return;
        }

        var keywordData = new KeywordData();
        foreach (var keyword in keywords)
        {
            keywordData.Add(keyword);
        }

        AddData(keywordData);
        AddData(new KeywordRuntimeData());

        var context = KeywordLoadContextProvider?.Invoke();
        var logics = new KeywordLogicData();
        foreach (var keyword in keywords)
        {
            var logic = KeywordLogicFactory.Create(keyword);
            if (logic is null)
            {
                continue; // 被动词条：仅登记、无逻辑组件
            }

            logic.Mount(this, context);
            logics.Add(logic);
        }

        if (logics.Logics.Count > 0)
        {
            AddData(logics);
        }
    }

    /// <summary>
    /// 持久化重建/装配（加载模板的扩展点；子类重写点预留）：总装阶段将从持久化文件重建 handler/效果/组件并装配于此。
    /// 默认空实现且不破坏流程（本批无任何重建逻辑）；重写须保持默认路径下的加载全流程可跑通。
    /// </summary>
    protected virtual void RebuildFromPersistence(Player owner)
    {
    }

    private static string DefinitionName(CardDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Name;
    }
}
