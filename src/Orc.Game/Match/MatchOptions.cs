using Orc.Game.Managers;

namespace Orc.Game;

/// <summary>对局级规则/开局配置（创建参数形态：配置对象；至少含"指挥点上限"）。</summary>
public sealed class MatchOptions
{
    /// <summary>指挥点上限（默认 12；须为正整数，非法配置于创建期抛参数校验异常）。</summary>
    public int MaxPointSlots { get; init; } = ResourceManager.DefaultMaxPointSlots;

    /// <summary>
    /// 跳过开局换牌（mulligan）相位（缺省 false＝按 A1 序列：<c>Initialize</c> 完成装配与起手装载后置"换牌"、
    /// 先手第 1 回合延后至双方确认）。true＝<c>Initialize</c> 末尾直接执行先手第 1 回合并置"进行"
    /// （既有序列；供测试基建与"无换牌"变体装配）。
    /// </summary>
    public bool SkipMulligan { get; init; }
}
