namespace Orc.Game.Targeting;

/// <summary>
/// 手牌选择槽位：候选＝己方手牌卡引用（<b>允许集语义</b>——由后端请求构造方提供、
/// 请求期快照固定；框架不内建域读取〔不自动读 Player.Hand——由构造方从对局状态构造〕；不走前端收集通道）。
/// 数量可配置（min..max；含"恰选 1"；min=0 允许空选语义适用）；产出＝卡引用（沿用 Ref 读面）。
/// 域判定面（如"仍在手牌"）：<b>请求构造期强制绑定</b>（每次请求独立提供、请求结束即弃；
/// 槽位声明只含结构〔名/数量约束〕、不携带判定面）；缺失＝请求构造期拒绝（fail-fast）。
/// 终局校验＝「∈允许集（快照）∧ 域判定（动态成员性，如"仍在手牌"）∧ 既有校验（IsAlive）」叠加；
/// 域判定为终局校验单点调用（交付前端的允许子集照快照交付、不经判定过滤）。
/// 专属呈现：呈现形态标注（<see cref="TargetSlotPresentation.HandSelect"/>）随请求描述交付前端
/// （区别于"场上目标点选"；呈现提示、非策略指令）。
/// 空允许集＝失败（不进交互——对齐"必须非空槽位空集＝失败"；min=0 的空集边角对齐既有 MultiSelect 语义、不新增特例）。
/// "己方"由构造方约定（允许集隐含界定）——框架不引入玩家/归属语义（槽位声明不携带归属标识）。
/// 可派生（库内特化）：<see cref="MulliganSelectSlot"/>（开局换牌——附加专属 Kind/呈现与动画标注）。
/// </summary>
public class HandSelectSlot : TargetSlot
{
    /// <summary>创建手牌选择槽位。</summary>
    /// <param name="min">至少须选到的个数（≥0；0＝允许空选完成）。</param>
    /// <param name="max">至多可选的个数（≥1）。</param>
    /// <param name="name">槽位名（可省略；省略/空白＝归缺省槽位）。</param>
    /// <exception cref="ArgumentOutOfRangeException">min 为负；或 max 非正整数。</exception>
    /// <exception cref="ArgumentException">min 大于 max（关系在构造期校验）。</exception>
    public HandSelectSlot(int min, int max, string? name = null)
        : base(name)
    {
        if (min < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(min), min, "min 不能为负（min=0 表示允许空选完成；上限见 max）。");
        }

        if (max < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(max), max, "max 须为正整数（≥1）。");
        }

        if (min > max)
        {
            throw new ArgumentException($"min（{min}）不能大于 max（{max}）（槽位约束关系在构造期校验）。", nameof(min));
        }

        Min = min;
        Max = max;
    }

    /// <summary>至少须选到的个数（≥0；0＝允许空选完成）。</summary>
    public int Min { get; }

    /// <summary>至多可选的个数（≥1）。</summary>
    public int Max { get; }

    internal override TargetSlotKind Kind => TargetSlotKind.HandSelect;

    internal override int MinSelection => Min;

    internal override int MaxSelection => Max;

    internal override bool IsReferenceKind => true;

    internal override TargetSlotPresentation Presentation => TargetSlotPresentation.HandSelect;
}
