using Orc.Cards;

namespace Orc.Game.Cards;

/// <summary>
/// 运行期注册的一枚卡牌（前端资产迁移 S4·R5/D7）：定义 ＋ 其效果声明（预制体 id）＋ 内联效果预制体。
/// **只追加不覆盖**（同 id 重复＝调用方拒绝）；实例化时经对局级 ID 水位线自动判为"构筑之外"。
/// 本类型居游戏层——保证 `Orc.Game` 不依赖宿主工程（依赖方向单向）。
/// </summary>
/// <param name="Id">卡定义注册 id（全局唯一；调用方给定）。</param>
/// <param name="Definition">卡定义。</param>
/// <param name="PrefabIds">该卡声明的效果预制体 id 清单（可空）。</param>
/// <param name="Prefabs">该卡的内联效果预制体（随注册一并登记进预制体管理器；可空）。</param>
public sealed record RuntimeCardRegistration(
    string Id,
    CardDefinition Definition,
    IReadOnlyList<string> PrefabIds,
    IReadOnlyList<EffectSnapshot> Prefabs);
