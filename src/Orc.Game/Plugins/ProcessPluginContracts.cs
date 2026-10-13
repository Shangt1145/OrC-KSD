using Orc.Cards;
using Orc.Core;
using Orc.Game.Cards;
using Orc.Game.Cards.Data;

namespace Orc.Game.Plugins;

/// <summary>
/// 全局（进程级）注册面（P11 甲·<see cref="AssetScope.Process"/>）：静态注册面（词条／数据组件）的受控登记——
/// **转发**到既有静态注册表（<c>KeywordRegistry</c>／<c>CardComponentRegistry</c>），不造第二注册面。
/// 命名注：不采用 "Process" 作为成员名——csx 沙箱的屏蔽词含 `Process`（为挡 `System.Diagnostics.Process`），
/// 成员名若含该子串会使**任何**使用本面的脚本被文本级过滤拒绝。
/// </summary>
public interface IGlobalRegistrationServices
{
    /// <summary>注册词条（转发到 <see cref="KeywordRegistry.Register"/>）。</summary>
    void RegisterKeyword(string keyword, Func<Card, int?, KeywordComponent> factory, bool isBattleKeyword = false);

    /// <summary>声明词条效果绑定（转发到 <see cref="KeywordRegistry.DeclareEffectBindings"/>）。</summary>
    void DeclareKeywordEffectBinding(string keyword, KeywordEffectBinding binding);

    /// <summary>注册数据组件（转发到 <see cref="CardComponentRegistry.Register{TDefinition}"/> 的条目面——按注册项整体登记）。</summary>
    void RegisterCardComponent(CardComponentEntry entry);
}

/// <summary>
/// 全局（进程级）插件上下文（P11 甲）：在内核受控注册面之上暴露 <see cref="IGlobalRegistrationServices"/>，
/// 使 `scope: process` 的插件脚本可在**冷加载期**装配静态注册面资产。
/// </summary>
public interface IGlobalPluginContext : IPluginRegistrationContext
{
    /// <summary>全局注册面（宿主未提供＝null）。</summary>
    IGlobalRegistrationServices? Global { get; }
}

/// <summary>全局（进程级）装配结果。</summary>
/// <param name="ActivatedPackages">已装配包（<c>id@version</c>；装配序）。</param>
/// <param name="Failures">失败明细（隔离整包）。</param>
public sealed record GlobalActivationResult(
    IReadOnlyList<string> ActivatedPackages,
    IReadOnlyList<string> Failures);
