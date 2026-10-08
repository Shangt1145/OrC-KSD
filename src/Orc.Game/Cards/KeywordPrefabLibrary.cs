using Orc.Cards;

namespace Orc.Game.Cards;

/// <summary>
/// 词条效果库（批 4 数据化）：词条效果的数据壳制品集——既有预制体体系的新来源/注册面（不另造平行格式/通道）。
/// 制品形态＝效果快照 JSON（<c>*.prefab.json</c>——<see cref="PrefabManager.PrefabFilePattern"/>）：
/// 触发器/hooks 结构面 ＋ 行为引用（<c>assemblyKey</c> → C# 委托）；落点＝<c>Cards/KeywordPrefabs/</c>
/// （随程序集输出——沿效果解析资产 <c>EffectTemplateLoader</c> 先例）。
/// 装载经既有通道 <see cref="PrefabManager.LoadDirectory"/>：单文件失败＝隔离记录、不阻断其余（报告承载）。
/// 绑定关系（词条 → 数据壳效果清单）声明于注册面（<see cref="KeywordRegistry"/>）；
/// 生产装配（行为引用注册 ＋ 库装载 ＋ 绑定核验）见 <see cref="KeywordEffectAssembly"/>。
/// 失败语义分层（沿需求：文件层隔离 vs 绑定层 fail-fast）：库文件坏且无引用者＝隔离记录＋报告；
/// 有引用者＝绑定核验落到 fail-fast（<see cref="KeywordEffectAssembly"/>）。
/// </summary>
public static class KeywordPrefabLibrary
{
    /// <summary>默认库目录（相对程序集输出目录——沿 <c>EffectTemplateLoader.DefaultDirectory</c> 先例）。</summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "Cards", "KeywordPrefabs");

    /// <summary>
    /// 装载库目录到预制体管理器（预制体注册面）：目录缺省＝<see cref="DefaultDirectory"/>；
    /// 目录不存在＝空报告；单文件失败＝隔离记录、不阻断其余（返回报告承载——沿 <see cref="PrefabManager.LoadDirectory"/> 口径）。
    /// </summary>
    /// <exception cref="ArgumentNullException">prefabs 为 null。</exception>
    public static PrefabLoadReport LoadInto(PrefabManager prefabs, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(prefabs);
        return prefabs.LoadDirectory(directory ?? DefaultDirectory);
    }
}
