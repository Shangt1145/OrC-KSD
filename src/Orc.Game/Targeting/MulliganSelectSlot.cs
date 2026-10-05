namespace Orc.Game.Targeting;

/// <summary>
/// 换牌选择槽位（开局 mulligan 专用；M1=b 特制槽位）：<see cref="HandSelectSlot"/> 的**特化**——
/// 候选＝己方手牌卡引用（允许集由请求构造方提供＝该方手牌快照）、域判定面强制（"仍在手牌"）、数量 min..max
/// （min=0＝不换牌、合法）；产出＝卡引用（沿用 Ref 读面）。
/// 独占 <see cref="TargetSlotKind.MulliganSelect"/> 与 <see cref="TargetSlotPresentation.MulliganSelect"/>，
/// 供前端**识别"开局换牌"并播放专属动画**（区别于常规手牌选择）。
/// 其余契约（允许集非空、终局校验叠加、呈现标注非策略指令）与 <see cref="HandSelectSlot"/> 一致。
/// </summary>
public sealed class MulliganSelectSlot : HandSelectSlot
{
    /// <summary>创建换牌选择槽位。</summary>
    /// <param name="min">至少须选到的个数（≥0；0＝允许空选〔不换牌〕完成）。</param>
    /// <param name="max">至多可选的个数（≥1；对局路径＝该方手牌数）。</param>
    /// <param name="name">槽位名（可省略；省略/空白＝归缺省槽位）。</param>
    /// <exception cref="ArgumentOutOfRangeException">min 为负；或 max 非正整数。</exception>
    /// <exception cref="ArgumentException">min 大于 max（关系在构造期校验）。</exception>
    public MulliganSelectSlot(int min, int max, string? name = null)
        : base(min, max, name)
    {
    }

    internal override TargetSlotKind Kind => TargetSlotKind.MulliganSelect;

    internal override TargetSlotPresentation Presentation => TargetSlotPresentation.MulliganSelect;
}
