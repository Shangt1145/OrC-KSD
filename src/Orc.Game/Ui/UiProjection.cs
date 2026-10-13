using Orc.Game.Players;
using Orc.UI.Contracts;

namespace Orc.Game.Ui;

// ─────────────────────────────────────────────────────────────────────────────
// 投影契约（前端资产迁移 S7·P7a）：投影由插件提供、由前端每帧拉取。
// 落点＝游戏层（沿 `Orc.Game.Targeting` 的 `ITargeterBridge`/`SelectorPresentation` 先例——游戏层本就托管
// UI 面向契约）；DTO 居 `Orc.UI.Contracts`（零依赖、位于本层之下）⇒ 依赖方向 `Orc ← Orc.UI.Contracts ← Orc.Game`，无环。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// **投影提供者**（S7·D2/D6）：把当前对局（活 <c>Match</c>）按**单一视角**投影为整盘只读视图
/// <see cref="UiMatchView"/>。由插件注册工厂产出、前端每帧调用；实现方不做规则判定（只读引擎面）。
/// </summary>
public interface IUiProjectionProvider
{
    /// <summary>产出一帧投影。抛错＝该帧投影失败（调用方按**运行期隔离**处理：保留上一帧）。</summary>
    /// <param name="match">当前对局（活对象）。</param>
    /// <param name="viewer">视角玩家。</param>
    UiMatchView Project(Match match, Player viewer);

    /// <summary>
    /// 单卡投影：给"不在整盘视图中"的卡（打出的指令、被弃/爆掉的牌等）取一枚卡视图——
    /// 表现层（段→演出）消费；与 <see cref="Project"/> 同源，不另造第二套卡读面。
    /// </summary>
    /// <param name="match">当前对局。</param>
    /// <param name="card">目标卡实例。</param>
    /// <param name="viewer">视角玩家。</param>
    /// <param name="zone">呈现区（`hand`/`frontline`/`support`）。</param>
    /// <param name="slotIndex">区内序号（不适用＝0）。</param>
    UiCardView ReadCard(Match match, Orc.Cards.Card card, Player viewer, string zone, int slotIndex);
}

/// <summary>
/// 投影提供者**工厂**（S7·P6f）：前端以美术解析器（artLookup）构造 provider 实例并自持
/// （引擎无美术概念，art 属前端边界）。
/// </summary>
public interface IUiProjectionProviderFactory
{
    /// <summary>
    /// 以对局 id 与美术解析器构造 provider（artLookup 可空＝卡面无图，走文本回退）。
    /// `matchId` 由前端提供：引擎侧无对局 id 概念，而 <see cref="UiMatchView.MatchId"/> 是缓存/多局隔离的必需键。
    /// `definitionLookup`（可选）：卡 id → **前端富定义**（美术/文本/flavor）——前端自持的界面数据，
    /// 引擎不参与渲染；缺省＝按引擎定义构造。
    /// </summary>
    IUiProjectionProvider Create(
        Func<string, string>? artLookup,
        string matchId,
        Func<string, UiCardDefinition?>? definitionLookup = null);
}

/// <summary>
/// 对局级**投影注册面**（S7）：单个 provider 工厂（整盘视图的唯一产出者——多来源会互相打架）。
/// 由 <see cref="Match.UiProjections"/> 暴露；插件在装配期注册，前端运行期取用。
/// </summary>
public sealed class UiProjectionRegistry
{
    private IUiProjectionProviderFactory? _factory;

    /// <summary>是否已注册工厂。</summary>
    public bool HasProvider => _factory is not null;

    /// <summary>注册工厂（单个；重复注册＝拒绝）。</summary>
    /// <exception cref="ArgumentNullException">factory 为 null。</exception>
    /// <exception cref="InvalidOperationException">已有工厂（单个投影提供者——重复注册被拒绝）。</exception>
    public void Register(IUiProjectionProviderFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (_factory is not null)
        {
            throw new InvalidOperationException("本对局已注册投影提供者（单个——重复注册被拒绝）。");
        }

        _factory = factory;
    }

    /// <summary>以对局 id 与美术解析器构造 provider（未注册＝null）。</summary>
    public IUiProjectionProvider? CreateProvider(
        Func<string, string>? artLookup,
        string matchId,
        Func<string, UiCardDefinition?>? definitionLookup = null)
        => _factory?.Create(artLookup, matchId, definitionLookup);
}

/// <summary>UI 投影服务面（S7·P7b）：经 <c>IMatchPluginContext.Ui</c> 暴露给插件。</summary>
public interface IUiPluginServices
{
    /// <summary>对局级投影注册表。</summary>
    UiProjectionRegistry Projections { get; }
}

/// <summary>默认实现（宿主/对局装配构造）。</summary>
public sealed class UiPluginServices : IUiPluginServices
{
    /// <summary>创建服务面。</summary>
    /// <exception cref="ArgumentNullException">projections 为 null。</exception>
    public UiPluginServices(UiProjectionRegistry projections)
    {
        ArgumentNullException.ThrowIfNull(projections);
        Projections = projections;
    }

    /// <inheritdoc />
    public UiProjectionRegistry Projections { get; }
}
