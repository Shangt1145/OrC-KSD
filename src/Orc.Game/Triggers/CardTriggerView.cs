using Orc.Core;
using Orc.Game.Board;
using Orc.Game.Cards;

namespace Orc.Game.Triggers;

/// <summary>
/// 卡牌触发器视图（游戏层触发器上下文）：承载一次卡牌侧触发（预打出 / 打出 / 使用反制 / 部署 / 加入 / 单位化等）的载荷引用。
/// 全可选（可从任意载荷绑定）；Card / Player 为载荷对象的弱类型引用（Card＝卡牌实例、Player＝玩家对象）。
/// 2A 用于触发器声明位与注册表示例；2B 起链内容填入（属性加性扩展：Position＝目标槽位、CaptureBox＝预打出捕获箱、Argument＝打出参数）。
/// 数据键约定＝属性名（与 GameUpdates.PayloadCard / PayloadPlayer / PayloadPosition 字面值一致）。
/// </summary>
[ContextView]
public class CardTriggerView
{
    /// <summary>卡牌引用（可选）。</summary>
    [Optional]
    [Read]
    public virtual object? Card { get; set; }

    /// <summary>玩家引用（可选）。</summary>
    [Optional]
    [Read]
    public virtual object? Player { get; set; }

    /// <summary>目标槽位（可选；2B 加性扩展）：部署 / 加入 / 单位化链的目标空槽位（Slot 对象）。</summary>
    [Optional]
    [Read]
    public virtual Slot? Position { get; set; }

    /// <summary>预打出捕获箱（可选；2B 加性扩展）：指令预打出 handler 经此提交捕获值（由打出链创建并随触发数据注入）。</summary>
    [Optional]
    [Read]
    public virtual CardCaptureBox? CaptureBox { get; set; }

    /// <summary>打出参数（可选；2B 加性扩展）：打出触发器承载的 object? 参数（单引用 / 列表 / targeter 皆可；由预打出捕获结果传入）。</summary>
    [Optional]
    [Read]
    public virtual object? Argument { get; set; }
}
