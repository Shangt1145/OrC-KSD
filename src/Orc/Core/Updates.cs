namespace Orc.Core;

/// <summary>
/// 内置更新字符串常量集（便利常量；S3）。总线为开放集合——不做更新类型注册/白名单，
/// 使用者自定义字符串与常量平等；常量仅用于规避拼写/大小写问题。
/// 更新字符串同一性：ordinal 序数、大小写敏感、逐字符原样比较（不做文化敏感比较、不做大小写折叠、不做归一化）。
/// </summary>
public static class Updates
{
    /// <summary>卡牌放置（"card.placed"）。</summary>
    public const string CardPlaced = "card.placed";

    /// <summary>卡牌销毁（"card.destroyed"）。</summary>
    public const string CardDestroyed = "card.destroyed";

    /// <summary>卡牌数据变更（"card.data"）。</summary>
    public const string CardData = "card.data";

    /// <summary>效果移除（"effect.removed"）。</summary>
    public const string EffectRemoved = "effect.removed";
}
