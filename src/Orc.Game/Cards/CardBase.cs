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
/// ②对局开始卡牌加载模板（<see cref="LoadAsync"/>）：固定链＝【重建/装配（可重写扩展点、默认空实现）→ 对局级 ID 分配（W1-1 加性）→ 元数据装配（W1-1 加性：TagData）→ 词条装载（2C 加性）→ 效果装载（X2 加性）→ 广播 card.load】；
///   持久化重建本批仅显式预留空位（无任何重建逻辑；总装阶段实现）。
/// ③触发器声明位：预打出 / 打出 / 使用反制——对象在位于具体子类（构造期创建、装配点就绪）；链内容属打出链批次（2B）。
/// ④修饰器组件（W2a G3 加性）：所有卡类构造期常驻持有 <see cref="Modifiers"/>（轻量伴生容器——非数据组件、不进
///   装配/加载/快照语义；空状态零行为负担）；挂载/注销由效果装载链托管属后续批次（W2c）、本单先提供机制与装配接口。
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

        // W2a G3 修饰机制：卡侧修饰器组件构造期常驻（所有卡类；轻量伴生、非数据组件——不进装配/加载/快照语义）。
        Modifiers = new CardModifierComponent(this, engine);

        // 2C-A1 词条组件化：卡上词条管理组件构造期常驻（所有卡类；轻量伴生、非数据组件——空管理面零行为负担、可寻址不抛）。
        Keywords = new KeywordManager(this, engine);
    }

    /// <summary>本卡的定义（代码注册形态；名称/类别/四项数值可读）。</summary>
    public CardDefinition Definition { get; }

    /// <summary>
    /// 卡侧修饰器组件（W2a G3 修饰机制）：每实例构造期常驻的专用轻量容器
    /// （修饰器集＋更新检测组件名单＋各组件缓存＋全量重跑链与「每轮管线」）。
    /// 空状态零行为负担（无修饰不发任何更新、不影响既有装配/加载/快照行为）；集中查询面＝<see cref="CardModifierComponent.All"/>。
    /// 不纳入 AddData 数据组件体系（轻量伴生；不进快照导出）。
    /// </summary>
    public CardModifierComponent Modifiers { get; }

    /// <summary>
    /// 词条管理组件（2C-A1；每实例构造期常驻的卡上伴生——轻量伴生、非数据组件，对齐 <see cref="Modifiers"/> 先例）：
    /// 词条组件的注册与索引（有无/参值查询）＋授予/移除/参值改写统一读写口。加载时词条装载经其授予链完成
    /// （<see cref="LoadKeywords"/>——先于 card.load）；运行时动态授予（含「无词条卡 → 有」）经其操作面。
    /// 空管理面（无词条卡）零行为负担、可寻址（含无词条卡不抛——统一根写法：<c>card.Keywords.Has(...)</c>）。
    /// </summary>
    public KeywordManager Keywords { get; }

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
    /// 词条装载上下文提供器（2C-A1 加性面；internal；A2 加性：存储内聚至词条管理组件——本属性为其转发面）：
    /// 加载时（<see cref="LoadAsync"/> 的词条装载步骤）与运行时授予时装载词条组件运行逻辑所需的对局级服务
    /// （如「造成攻击伤害」触发器——伏击/免疫/重甲挂载点；钳击/压制所需的对局服务）；由对局加载路径经卡牌库注入（延迟读取）；
    /// 独立构造（脱离对局）＝null（词条组件运行逻辑装载跳过注册、不抛错——功能不可用、加载不失败）。
    /// </summary>
    internal Func<KeywordLoadContext?>? KeywordLoadContextProvider
    {
        get => Keywords.LoadContextProvider;
        set => Keywords.LoadContextProvider = value;
    }

    /// <summary>
    /// 效果装载上下文提供器（X2 加性面；internal）：加载时（<see cref="LoadAsync"/> 的效果装载步骤）装载卡牌效果
    /// 所需的对局装载语境与装配源（效果声明查询＋工厂解析；效果源可空——无注册表时声明为空、装载照常）；
    /// 由对局加载路径经卡牌库注入（延迟读取）；独立构造（脱离对局——不经卡牌库）＝null（效果装载整链跳过——不抛错、加载不失败、功能不可用）。
    /// </summary>
    internal Func<CardEffectLoadContext?>? EffectLoadContextProvider { get; set; }

    /// <summary>
    /// 部署逻辑装载语境提供器（A4 加性面；internal）：加载时（<see cref="LoadAsync"/> 的「部署逻辑生成」步骤）
    /// 为单位卡生成并挂载部署逻辑组件（<see cref="DeploymentLogicData"/>）所需的装配源查询面；
    /// 由对局加载路径经卡牌库注入（延迟读取）；独立构造（脱离对局——不经卡牌库）＝null（生成步骤跳过——不抛错、加载不失败、功能不可用）。
    /// </summary>
    internal Func<DeploymentLogicLoadContext?>? DeploymentLogicLoadContextProvider { get; set; }

    /// <summary>
    /// 对局级卡牌 ID（W1-1 加性面；internal）：加载时分配——由 <see cref="MatchCardIdProvider"/> 取新值；
    /// 未分配（未加载 / 独立构造 / 无提供器）＝null。既有 <see cref="Orc.Core.Entity.Id"/> 为 Guid 不可比较，
    /// 本整数序号承载「构筑外判定」（ID ＞ 初始化水位线）。
    /// 「同一实例同一 ID」：重复加载不重分配（幂等——判定不失真）。
    /// </summary>
    internal int? MatchCardId { get; private set; }

    /// <summary>
    /// 对局级卡牌 ID 提供器（W1-1 加性面；internal）：加载时（<see cref="LoadAsync"/> 的 ID 分配步骤）调用以获取下一个自增 ID；
    /// 由对局加载路径经卡牌库注入（延迟读取——对局装配顺序下提供器目标在实例化后才就绪）；
    /// 独立构造（脱离对局）＝null（不分配 ID——无对局上下文，判定侧以「未分配」明确拒绝）。
    /// </summary>
    internal Func<int>? MatchCardIdProvider { get; set; }

    /// <summary>
    /// 对局开始卡牌加载（模板方法；由加载路径逐张调用）：
    /// ① 装配归属（Owner＝所属卡组玩家；2B 加性：打出链验证/扣费/离手依据）；
    /// ② 重建/装配步骤（可重写扩展点 <see cref="RebuildFromPersistence"/>；默认空实现——持久化重建后置、本批无重建逻辑）；
    /// ③ 对局级卡牌 ID 分配（W1-1 加性：经提供器分配自增 ID；幂等——已分配保持原值；先于 card.load 广播）；
    /// ④ 元数据装配（W1-1 加性：从定义读国籍/稀有度/开放 tag → 装配 TagData；先于 card.load 广播——消费者查询不到未就绪状态）；
    /// ⑤ 部署逻辑生成（A4 加性：数据装配组收尾——仅单位卡、经装配源查询〔无登记＝不生成〕，
    ///    为单位卡生成并挂载部署逻辑组件；先于 card.load 广播——「加载时生成部署组件」；生成失败＝记录、不阻断加载）；
    /// ⑥ 词条装载（2C-A1 加性：从定义读词条声明〔标识＋可选参值〕→ 逐条经词条管理组件的授予链挂载
    ///    〔存在性置位 → 运行逻辑装载＋内嵌效果装载 → OnGrant；伏击在运行逻辑装载时挂到「造成攻击伤害」〕；
    ///    无词条卡＝无副作用；装载先于 card.load 广播——消费者查询不到未就绪状态）；
    /// ⑦ 效果装载（X2 加性：从装配源解析效果声明 → 构造＋登记＋装载被动效果〔挂主触发器＋OnMount；含监听 handler 注册〕
    ///    ＋托管登记〔卸载自动按来源撤销修饰器〕；无装配源＝跳过；先于 card.load 广播——消费者查询不到未就绪状态）；
    /// ⑧ 广播 card.load（载荷 {Card, Player}——Card＝本实例、Player＝所属卡组玩家）。
    /// 加载路径保证每卡恰一次调用；失败直接上抛（初始化 fail-fast）。
    /// </summary>
    /// <exception cref="ArgumentNullException">owner 为 null。</exception>
    public async Task LoadAsync(Player owner, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(owner);

        Owner = owner;
        RebuildFromPersistence(owner);
        LoadMatchCardId();
        LoadTagData();
        GenerateDeploymentLogic();
        LoadKeywords();
        await LoadEffectsAsync(ct);
        await GameUpdates.EmitCardLoad(_engine, owner, this, ct);
    }

    /// <summary>
    /// 对局级卡牌 ID 分配（W1-1；加载时）：未分配且提供器在位＝经提供器取新 ID；已分配＝保持原值
    /// （幂等——「同一实例同一 ID」；重复加载不重分配、判定不失真）。提供器缺席（独立构造）＝不分配。
    /// </summary>
    private void LoadMatchCardId()
    {
        if (MatchCardId is null && MatchCardIdProvider is not null)
        {
            MatchCardId = MatchCardIdProvider();
        }
    }

    /// <summary>
    /// 元数据装配（W1-1；加载时＝与 card.load 同步完成）：从定义读国籍/稀有度（必填槽位）与开放 tag →
    /// 装配 <see cref="TagData"/>（全类别覆盖；无开放 tag 卡＝空集合装配——槽位恒在）。登记先于 card.load 广播——
    /// 消费者查询不到未就绪状态（与词条装配流先例一致）。重复加载＝重复装配被拒绝（AddData 契约：每类型恰一份）。
    /// </summary>
    private void LoadTagData()
    {
        var data = new TagData(Definition.Faction, Definition.Rarity);
        foreach (var tag in Definition.Tags)
        {
            data.AddTag(tag);
        }

        AddData(data);
    }

    /// <summary>
    /// 部署逻辑生成（A4；加载时＝与 card.load 同时完成——「加载时生成部署组件」）：
    /// 仅单位卡（部署链为单位路径；非单位卡不生成/不消费）；无装配源（context 为 null）＝跳过；
    /// 无登记条目＝不生成（缺省——消费端已「无组件＝跳过」）；生成＝挂载 <see cref="DeploymentLogicData"/>
    /// 组件＋按登记序逐条 Add（登记序＝触发顺序依据）；装配期生成失败＝记录、不阻断加载（对齐加载链防御口径）。
    /// 相遇语义（对齐容器语义）：卡上已有同类型组件（手动登记先遇）＝生成环节跳过并申报（记录）；
    /// 手动登记（组件层 <see cref="DeploymentLogicData.Add"/>）继续合法、并存。
    /// </summary>
    private void GenerateDeploymentLogic()
    {
        if (this is not UnitCard)
        {
            return; // 生成面只为单位卡生成（非单位卡不生成/不消费）
        }

        var context = DeploymentLogicLoadContextProvider?.Invoke();
        if (context is null)
        {
            return; // 无装配源（独立构造/未注入）：跳过（不抛错、加载不失败）
        }

        try
        {
            var entries = context.Entries;
            if (entries.Count == 0)
            {
                return; // 无部署效果卡＝无组件（生成面缺省——不生成）
            }

            if (TryGetData<DeploymentLogicData>(out _))
            {
                _engine.RootStream.WriteLog(
                    "部署逻辑生成",
                    $"卡牌 '{Name}' 已存在部署逻辑组件（手动登记先遇）——生成环节跳过（容器「每类型恰一份」语义）。",
                    LogLevel.Info,
                    new[] { "deployment-logic", "generate-skip" });
                return; // 相遇语义：生成环节幂等跳过（申报）
            }

            var data = new DeploymentLogicData();
            foreach (var entry in entries)
            {
                data.Add(entry.Name, entry.Handler); // 按登记序逐条搬运
            }

            AddData(data);
        }
        catch (Exception ex)
        {
            _engine.RootStream.WriteLog(
                "部署逻辑生成",
                $"卡牌 '{Name}' 的部署逻辑生成失败（隔离：不阻断加载）：{ex.Message}",
                LogLevel.Error,
                new[] { "deployment-logic", "error", $"exception:{ex.GetType().Name}" });
        }
    }

    /// <summary>
    /// 词条装载（2C-A1；加载时＝与 card.load 同时完成）：
    /// 逐条经词条管理组件的授予链挂载（存在性置位 → 运行逻辑装载〔如伏击注册改写〕＋内嵌效果装载 → OnGrant）——
    /// 与运行时动态授予同一机制；词条组件经注册面（<see cref="KeywordRegistry"/>）构造。
    /// 卡组/手牌即可被读取查询（先于 card.load 广播——消费者查询不到未就绪状态）。
    /// 无词条卡＝无副作用（不注册词条组件、不装载逻辑；词条面可寻址、空内容）。
    /// 标识合法性与重复项已在定义期 fail-fast（合法集＝注册面内容）；装载链内失败＝fail-fast（上抛——加载失败、回滚无残留）。
    /// </summary>
    private void LoadKeywords()
    {
        var keywords = Definition.Keywords;
        if (keywords.Count == 0)
        {
            return;
        }

        var context = KeywordLoadContextProvider?.Invoke();
        foreach (var declaration in keywords)
        {
            Keywords.GrantCore(declaration.Id, declaration.Value, context);
        }
    }

    /// <summary>
    /// 效果装载（X2；加载时＝与 card.load 同时完成「声明解析＋构造＋登记＋装载」）：
    /// 经效果装载链（<see cref="CardEffectLoader"/>）执行——声明校验与工厂解析 fail-fast（未知标识/工厂返回 null 上抛）、
    /// 构造执行异常隔离（跳过该效果）、被动效果装载（挂主触发器＋OnMount——含监听 handler 注册；失败回滚、不阻断）、
    /// 托管登记（卸载自动按来源撤销修饰器）；无装配源（独立构造）＝整链跳过、不抛错、加载不失败；无声明卡＝无副作用。
    /// </summary>
    private async Task LoadEffectsAsync(CancellationToken ct)
    {
        var context = EffectLoadContextProvider?.Invoke();
        await CardEffectLoader.LoadAsync(this, _engine, context, ct);
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
