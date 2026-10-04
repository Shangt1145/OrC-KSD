namespace Orc.Core;

/// <summary>
/// 内置更新字符串常量集（便利常量；S3）。总线为开放集合——不做更新类型注册/白名单，
/// 使用者自定义字符串与常量平等；常量仅用于规避拼写/大小写问题。
/// 更新字符串同一性：ordinal 序数、大小写敏感、逐字符原样比较（不做文化敏感比较、不做大小写折叠、不做归一化）。
/// </summary>
public static class Updates
{
    /// <summary>
    /// 卡牌放置（"card.placed"；载荷＝{ Card }——卡牌对象引用）：放置驱动装载信号。
    /// 发射点（W3-A3）：部署链/加入链——单位化成功后、完成信号（unit.deployed / unit.joined）之前（每次成功驱动恰一次）；
    /// 加载时点不发射（放置＝「上场」语义）。消费＝装载链放置处理器（初始化＋效果装载幂等兜底；策略 1 效果可订阅作上场驱动）。
    /// </summary>
    public const string CardPlaced = "card.placed";

    /// <summary>卡牌销毁（"card.destroyed"）。</summary>
    public const string CardDestroyed = "card.destroyed";

    /// <summary>卡牌数据变更（"card.data"）。</summary>
    public const string CardData = "card.data";

    /// <summary>
    /// 效果移除（"effect.removed"；载荷＝{ Card, Effect }——卡牌＋效果对象引用）。
    /// 发射点（W3-A3）：移除路径——成功事务的实际移除命中恰发射一次（显式移除〔含复装前置〕/死亡链逐效果/词条撤销内嵌卸载/销毁批量清理）；
    /// 幂等无操作与回滚静默路径不发射。消费＝装载链清理处理器＋效果主触发器（同一清理模板幂等收敛；源头即信号、防自循环）。
    /// </summary>
    public const string EffectRemoved = "effect.removed";
}
