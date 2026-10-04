namespace Orc.Game.Triggers;

/// <summary>
/// 触发器分层分类（第二批 H 项；游戏层轻量标注机制的登记必填信息）：
/// 底层＝引擎初始化注册、与 UI 沟通并触发更新；外部＝业务层触发器。
/// 「有且仅有底层触发器在合适时机触发更新」——机制不做强拦截（靠自觉）；分类为登记时携带的义务声明。
/// </summary>
public enum TriggerLayer
{
    /// <summary>底层（引擎初始化注册；与 UI 沟通＋触发更新）。</summary>
    LowLevel = 0,

    /// <summary>外部（业务层）。</summary>
    External = 1,
}
