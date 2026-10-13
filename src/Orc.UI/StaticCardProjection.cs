using Orc.Game.Cards;
using Orc.UI.Contracts;

namespace Orc.UI;

/// <summary>
/// **静态卡定义投影**（S8 待定项 2 的裁决）：把引擎 `CardDefinition` 投影为 <see cref="UiCardDefinition"/>——
/// 供**图鉴/构筑**这类"无对局上下文"的场景使用（对局内的实例投影见 <see cref="UiProjectionProvider"/>）。
/// 与 D1"插件从卡生成投影"同源：定义投影也归后端 `.ui` 插件，前端不再自造定义。
/// </summary>
public static class StaticCardProjection
{
    /// <summary>投影一枚卡定义。</summary>
    /// <param name="cardId">注册 id（图鉴/构筑用的键）。</param>
    /// <param name="definition">引擎卡定义。</param>
    /// <param name="artLookup">卡 id → 美术路径（引擎无美术概念；前端自持该表）。</param>
    /// <param name="definitionLookup">卡 id → 前端丰富定义（美术/文本/flavor；可空＝按引擎定义构造）。</param>
    /// <param name="text">卡面文本（如卡包 `texts.json` 的条目；可空）。</param>
    /// <exception cref="ArgumentNullException">cardId／definition 为 null。</exception>
    public static UiCardDefinition Project(
        string cardId,
        CardDefinition definition,
        Func<string, string>? artLookup = null,
        Func<string, UiCardDefinition?>? definitionLookup = null,
        string? text = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
        ArgumentNullException.ThrowIfNull(definition);

        if (definitionLookup?.Invoke(cardId) is { } rich)
        {
            return rich;
        }

        return new UiCardDefinition
        {
            CardId = cardId,
            Name = definition.Name,
            CardType = definition.Category == CardCategory.Counter ? "counter"
                : definition.Category == CardCategory.Command ? "order" : "unit",
            UnitType = definition.UnitTypes.Count > 0 ? definition.UnitTypes[0].ToLowerInvariant() : "",
            Set = definition.Faction,
            Rarity = definition.Rarity,
            ArtPath = artLookup?.Invoke(cardId) ?? "",
            Text = text ?? "",
            Cost = definition.DeployCost,
            BaseAttack = definition.Attack,
            BaseDefense = definition.Defense,
            BaseOpCost = definition.OperateCost,
            IsToken = false,
        };
    }
}
