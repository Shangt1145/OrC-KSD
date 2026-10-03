namespace Orc.Cards;

/// <summary>
/// 生命数据组件（S4）：通用流程（伤害结算）读写的最小数据约定（Hp 记账）。
/// 数据组件键＝类型（每卡每类型恰一份）；实例由外部构造后经 <see cref="Card.AddData"/> 添加；
/// <see cref="Card.GetData{T}"/> 返回实例引用（引用共享：外部可直接修改其字段）。
/// </summary>
public sealed class HealthData
{
    /// <summary>当前生命值。</summary>
    public int Hp { get; set; }
}

/// <summary>
/// 护盾数据组件（S4）：伤害结算「护盾检查结算」的最小数据约定。
/// 承受方无该组件 ＝ 护盾 0（直接伤害）；有该组件时伤害先由护盾吸收（护盾扣减、余量作用于生命）。
/// </summary>
public sealed class ShieldData
{
    /// <summary>当前护盾值（吸收伤害、扣减于此）。</summary>
    public int Shield { get; set; }
}
