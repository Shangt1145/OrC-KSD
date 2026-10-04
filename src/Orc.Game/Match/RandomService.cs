using Orc.Game.Cards;
using Orc.Game.Players;

namespace Orc.Game;

// ─────────────────────────────────────────────────────────────────────────────
// 第 2 批 G8（效果级随机服务）：对局级随机服务与确定性 PRNG。
// ①随机服务（对局级）：数值原语（Next）＋取样原语（PickOne / PickN）——供效果运行期取用；
//   服务只对给定序列取样（候选筛选与合法性由调用方负责）；「随机分配」类＝调用方循环取样组合（服务不建分配机制）。
// ②确定性：固定种子显式化（沿用 Match.Seed——可传入＋只读可查）＋确定性 PRNG（自实现固定算法——
//   消除 System.Random 的平台/版本不确定性；为在线同步预留）；单流＋顺序确定消费
//   （全对局一个流——洗切与效果取样共用；按确定性流程顺序消费、无并发/异步乱序——P2 同步语义）。
// ③洗切接入：集合洗切（CardList / CardSet）改经「受控源形态」（IRandomSource）消费——对局路径经本服务驱动；
//   初始化链（准备态）的洗切消费合法（对局内部链路）；洗牌具体序列因 PRNG 更换而变化属预期
//   （语义级兼容：就地打乱＋多重集不变＋同随机源可复现）。
// ④访问门禁（区分两层）：对局内部链路（初始化洗切等）＝经服务本体直接消费（不受对外门禁限制——准备态合法）；
//   对外取用面＝仅"进行"态可用——准备态对外请求明确拒绝（抛错）、结束态（终局）拒绝（风格与既有管理器门禁对齐）。
// ⑤接入面：效果运行期经「卡 → 玩家 → 服务」读取路径取用（MatchRandomService.ResolveFor——与
//   GameEnvironment.ResolveFor 同构）；装配期由 Match 注入各玩家（显式、可测试；无隐藏全局单例）。
// ⑥脱局降级：ResolveFor 解析不到（卡未加载 / 独立构造）＝null——调用侧按「功能不可用、不抛错、不失败」处置。
// ⑦消费纪律：对局内随机消费统一经本服务（无旁路）；集合洗切经受控源形态（IRandomSource）消费同一流。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 确定性随机源（受控消费原语；对局内部机制经其消费——如集合洗切的 Fisher–Yates）。
/// 语义：仅提供「数值」消费（<see cref="Next"/>）；确定性由实现方保证（同种子、同调用序列 → 同结果）。
/// 访问门禁不在本面（内部机制消费路径）——对外取用面的门禁由对局受控动作面与
/// <see cref="MatchRandomService"/> 对外原语承担。
/// </summary>
public interface IRandomSource
{
    /// <summary>取 [0, max) 内的均匀随机值（max ≥ 1；max ≤ 0 ＝明确拒绝——<see cref="ArgumentOutOfRangeException"/>）。</summary>
    int Next(int max);
}

/// <summary>
/// 对局级随机服务（G8）：单流（全对局一个流——洗切与效果取样共用）、顺序确定（按确定性流程顺序消费；
/// 无并发/异步乱序——P2 同步语义）的确定性随机设施。公开面＝数值与取样原语：
/// ①数值：<see cref="Next"/>（[0, max)）；②单取样：<see cref="PickOne{T}"/>；③多取样：<see cref="PickN{T}"/>
/// （不放回——返回 n 个互异元素；返回顺序＝逐次选取序〈随机序〉；同种子下结果确定复现）。
/// 原语为泛型形态、对任意元素类型可用（服务不依赖元素语义）；服务只对给定序列取样（筛选/合法性由调用方负责）。
/// 确定性：构造种子 → 自实现固定算法（SplitMix64——平台/版本无关）；「固定种子显式化」经 <see cref="Match.Seed"/>
/// （可传入＋只读可查）。消费纪律：对局内随机消费统一经本服务；每次原语调用按调用序消费样本（拒绝采样可能
/// 多消费——同种子完全确定）。「随机分配」类语义＝调用方循环取样组合（本服务不建分配专门机制）。
/// 访问门禁：对外取用面仅"进行"态可用——准备态对外请求、终局后（结束态）取用＝明确拒绝
/// （<see cref="InvalidOperationException"/>；门禁由对局装配注入的状态读取器提供）；独立构造
/// （公开构造——无对局生命周期）＝无门禁约束（显式、可测试的独立随机源）。
/// 边界行为（定义并测试覆盖）：<see cref="Next"/> 的 max ≤ 0 ＝明确拒绝；<see cref="PickOne{T}"/>
/// 的空候选集 ＝明确拒绝、null ＝拒绝；<see cref="PickN{T}"/> 的 n &lt; 0 ＝拒绝、n ＞ 候选数 ＝拒绝、
/// n ＝ 0 ＝空列表（零消费）。
/// 接入面：效果运行期经 <see cref="ResolveFor"/>（「卡 → 玩家 → 服务」）取用；装配期由对局注入各玩家
/// （显式、可测试——无隐藏全局单例）。
/// </summary>
public sealed class MatchRandomService : IRandomSource
{
    private readonly Func<MatchState>? _stateProvider;
    private ulong _state;

    /// <summary>
    /// 创建独立随机服务（显式、可测试的独立随机源；无对局生命周期——无访问门禁约束）。
    /// 对局装配路径经内部构造建立（由 <see cref="Match"/> 创建并注入门禁）；独立构造用于测试/工具语境。
    /// </summary>
    /// <param name="seed">随机种子（确定性起点——同种子、同调用序列 → 同结果序列）。</param>
    public MatchRandomService(int seed)
        : this(seed, stateProvider: null)
    {
    }

    /// <summary>
    /// 创建对局随机服务（装配内部路径）：附加对局状态读取器——对外取用面门禁（仅"进行"态可用）；
    /// stateProvider 为 null ＝独立构造（无门禁）。由对局装配期调用（一次性）。
    /// </summary>
    internal MatchRandomService(int seed, Func<MatchState>? stateProvider)
    {
        _stateProvider = stateProvider;
        _state = unchecked((ulong)(long)seed);
    }

    // ---------- 对外原语（数值 / 单取样 / 多取样；对外取用面——仅"进行"态可用） ----------

    /// <summary>
    /// 数值原语：取 [0, max) 内的均匀随机值。每次成功调用消费样本（拒绝采样可能多消费——同种子确定复现）。
    /// </summary>
    /// <param name="max">取值范围上界（须为正整数）。</param>
    /// <exception cref="ArgumentOutOfRangeException">max ≤ 0（明确拒绝）。</exception>
    /// <exception cref="InvalidOperationException">对局语境下非"进行"态（准备态对外请求 / 终局后——明确拒绝）。</exception>
    public int Next(int max)
    {
        EnsureExternalAccess();
        return NextCore(max);
    }

    /// <summary>
    /// 单取样原语：从候选序列中随机取一个元素（均匀——经数值原语索引合成）。
    /// 服务只对给定序列取样；候选筛选与合法性由调用方负责（服务的候选职责边界）。
    /// </summary>
    /// <param name="candidates">候选序列（非 null、非空——空候选集明确拒绝）。</param>
    /// <exception cref="ArgumentNullException">candidates 为 null。</exception>
    /// <exception cref="InvalidOperationException">空候选集（明确拒绝）；或对局语境下非"进行"态（明确拒绝）。</exception>
    public T PickOne<T>(IReadOnlyList<T> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        EnsureExternalAccess();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("候选集为空：无法取样（空候选集被明确拒绝）。");
        }

        return candidates[NextCore(candidates.Count)];
    }

    /// <summary>
    /// 多取样原语：从候选序列中不放回地取 n 个互异元素（部分 Fisher–Yates——逐次从剩余中选取；
    /// 返回顺序＝逐次选取序〈随机序〉、同种子下结果确定复现）。
    /// 服务只对给定序列取样；候选筛选与合法性由调用方负责。
    /// </summary>
    /// <param name="candidates">候选序列（非 null；n &gt; 候选数 ＝明确拒绝）。</param>
    /// <param name="n">取样个数（n ＝ 0 ＝空列表、零消费；n &gt; 候选数 ＝明确拒绝——不放回取样不可满足）。</param>
    /// <exception cref="ArgumentNullException">candidates 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">n &lt; 0；或 n &gt; 候选数。</exception>
    /// <exception cref="InvalidOperationException">对局语境下非"进行"态（明确拒绝）。</exception>
    public IReadOnlyList<T> PickN<T>(IReadOnlyList<T> candidates, int n)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfNegative(n);
        EnsureExternalAccess();
        if (n > candidates.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(n), n, $"取样数超过候选数（{candidates.Count}）——不放回取样不可满足（明确拒绝）。");
        }

        if (n == 0)
        {
            return Array.Empty<T>(); // 零取样＝空列表（零消费）
        }

        var buffer = new T[candidates.Count];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = candidates[i];
        }

        for (var i = 0; i < n; i++)
        {
            var j = i + NextCore(candidates.Count - i); // 从剩余区间 [i, count) 中选取
            (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
        }

        return buffer[..n];
    }

    // ---------- 受控源面（内部机制消费——无对外门禁；供集合洗切等对局内部链路） ----------

    /// <summary>
    /// 受控源消费（显式接口实现——无对外门禁）：供集合洗切等对局内部机制使用（如初始化链在准备态的洗切消费）。
    /// 对局状态约束由上层受控动作面负责（如 <see cref="Match.ShuffleDeckAsync"/> 的既有门禁）。
    /// </summary>
    int IRandomSource.Next(int max) => NextCore(max);

    // ---------- 接入面解析（卡 → 玩家 → 服务；与 GameEnvironment.ResolveFor 同构） ----------

    /// <summary>
    /// 卡 → 随机服务解析（读取路径「卡 → 玩家 → 服务」的收敛点）：
    /// 卡经归属玩家取服务；未加载（无归属）/独立构造（未注入）/非卡实体＝null
    /// （不可达——调用侧按「功能不可用、不抛错、不失败」处置）。
    /// </summary>
    /// <exception cref="ArgumentNullException">card 为 null。</exception>
    public static MatchRandomService? ResolveFor(Orc.Cards.Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card switch
        {
            Hq hq => hq.Owner.RandomService,
            CardBase cardBase => cardBase.Owner?.RandomService,
            _ => null,
        };
    }

    // ---------- 内部：门禁与 PRNG（确定性实现） ----------

    /// <summary>
    /// 对外取用面门禁（仅"进行"态可用）：准备态对外请求 / 终局拒绝＝明确抛错（风格与既有管理器门禁对齐）；
    /// 独立构造（无状态读取器）＝无门禁约束。
    /// </summary>
    private void EnsureExternalAccess()
    {
        if (_stateProvider is null)
        {
            return; // 独立构造（无对局生命周期）：无门禁约束
        }

        var state = _stateProvider();
        if (state == MatchState.InProgress)
        {
            return;
        }

        throw new InvalidOperationException(
            state == MatchState.Ended
                ? "对局已结束（终局），随机服务取用被拒绝（对外取用面仅'进行'态可用）。"
                : "对局尚未进入'进行'态：随机服务对外取用不可用（须先成功完成 Initialize）。");
    }

    /// <summary>
    /// 数值核心（[0, max)；无门禁——对外原语与受控源面共用）：拒绝采样消除除法偏差
    /// （阈值＝2^64 mod bound；拒绝概率 &lt; 2^-33——拒绝序列同种子确定复现）。
    /// </summary>
    private int NextCore(int max)
    {
        if (max < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(max), max, "取值范围须为正整数（≥1；max ≤ 0 被明确拒绝）。");
        }

        var bound = (ulong)max;
        var threshold = unchecked((ulong)(-(long)bound)) % bound; // ＝2^64 mod bound（拒绝采样阈值）
        ulong sample;
        do
        {
            sample = NextUInt64();
        }
        while (sample < threshold);

        return (int)(sample % bound);
    }

    /// <summary>
    /// 确定性 PRNG 单步（SplitMix64——固定算法；平台/版本无关；为在线同步预留的确定性实现）：
    /// 每步推进状态常数＋固定混合函数；同种子 → 同序列（逐位确定）。
    /// </summary>
    private ulong NextUInt64()
    {
        _state = unchecked(_state + 0x9E3779B97F4A7C15UL);
        var z = _state;
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }
}
