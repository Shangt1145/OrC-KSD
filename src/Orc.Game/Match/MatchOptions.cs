using Orc.Game.Managers;

namespace Orc.Game;

/// <summary>对局级规则/开局配置（创建参数形态：配置对象；至少含"指挥点上限"）。</summary>
public sealed class MatchOptions
{
    /// <summary>指挥点上限（默认 12；须为正整数，非法配置于创建期抛参数校验异常）。</summary>
    public int MaxPointSlots { get; init; } = ResourceManager.DefaultMaxPointSlots;
}
