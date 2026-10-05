using System.Text.Json;

namespace Orc.Game.Cards.Data;

/// <summary>
/// 数据体组件定义（"可反序列化组件"接口；P3＝b）：要求可放入数据体的组件实现本接口——
/// 它是"可入数据体"的标记 ＋ 自报名（<see cref="ComponentName"/>）。
/// 反序列化入口由定义类型的静态 <c>Read(JsonElement)</c> 承载、经注册面登记（见 <see cref="CardComponentRegistry"/>）——
/// 与 loader（装配动作，<see cref="CardComponentLoader"/>）分离：定义负责"反序列化 + 自校验（fail-fast）"，
/// loader 负责"装配到卡上（生成数据组件／填充字段／挂效果）"。
/// 先例：<c>Orc.Cards.ISerializableEffect</c>（效果侧同构标记面）；<c>CardDefinition</c>→<c>CardBase</c>、<c>EffectPrefab</c>→<c>DynamicEffect</c> 的定义/实例分离。
/// </summary>
public interface ICardDataComponentDefinition
{
    /// <summary>数据体里的组件类型名（如 "factionCost"；与注册名一致——单源）。</summary>
    string ComponentName { get; }
}

/// <summary>组件加载相位（P4a）：构造期（卡实例构造时）／加载期（<c>CardBase.LoadAsync</c> 内）。</summary>
public enum CardComponentPhase
{
    /// <summary>构造期（如 factionCost／battleStats——既有构造期装配方式保留）。</summary>
    Construction = 0,

    /// <summary>加载期（加载链内、先于 <c>card.load</c> 广播）。</summary>
    Load = 1,
}

/// <summary>
/// 组件 loader（定义 → 装配到卡）：生成数据组件／填充字段／挂效果。
/// 由卡牌加载器按相位与顺序调用（第一段＝注册序、第二段＝数据体声明序）。
/// </summary>
/// <param name="card">宿主卡实例。</param>
/// <param name="definition">本组件的数据体定义（具体类型由注册面保证，loader 内可安全向下转型）。</param>
/// <param name="context">加载上下文（受控服务集合，见 <see cref="CardComponentLoadContext"/>）。</param>
public delegate Task CardComponentLoader(
    CardBase card, ICardDataComponentDefinition definition, CardComponentLoadContext context);
