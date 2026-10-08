using Orc.Cards;
using Orc.Core;

namespace Orc.Game.Cards;

/// <summary>
/// 词条效果装配结果（生产装配报告）：行为引用注册数 ＋ 库装载数 ＋ 库文件失败明细
/// （单文件失败＝隔离记录——报告承载；见 <see cref="KeywordEffectAssembly.Assemble"/>）。
/// </summary>
/// <param name="HandlersRegistered">行为引用注册数（键 → 委托）。</param>
/// <param name="PrefabsLoaded">词条效果库成功注册的预制体条数。</param>
/// <param name="LibraryFailures">库文件失败明细（文件名 + 原因；隔离——不阻断其余）。</param>
public sealed record KeywordEffectAssemblyResult(
    int HandlersRegistered, int PrefabsLoaded, IReadOnlyList<string> LibraryFailures);

/// <summary>
/// 词条效果生产装配（批 4 数据化）：单一装配入口——①行为引用注册（键 → C# 委托）＋②词条效果库装载
/// （预制体注册）＋③绑定核验（词条声明绑定 → 库键；配置错误 fail-fast）。
/// 挂于对局初始化链（<c>Match.Initialize</c>——管理器群装配段、卡牌加载之前）：正常对局初始化后，
/// 任何词条装载时行为引用与库键均已就绪/可解析（生产路径**不变量**）。
/// 每对局（＝每引擎实例）恰一次装配；「注册即配置、重复拒绝」——幂等以生命周期结构达成（初始化点恰一次）。
/// 失败语义：库文件层＝单文件失败隔离记录、不阻断其余（报告＋引擎总流留痕）；
/// 绑定层（键缺失）＝装配期 fail-fast（异常上抛——对局初始化失败）。
/// 行为引用键与效果预制体标识共用键名（本类为键一致性登记处；数据壳 JSON 内以字面量引用）。
/// </summary>
public static class KeywordEffectAssembly
{
    /// <summary>闪击·部署置位——效果预制体标识（＝数据壳 JSON 的 root.id 与 assemblyKey）。</summary>
    public const string BlitzDeploySetPrefabId = "keyword.blitz.deploy-set";

    /// <summary>动员·回合累积——效果预制体标识（＝数据壳 JSON 的 root.id 与 assemblyKey）。</summary>
    public const string MobilizeAccrualPrefabId = "keyword.mobilize.accrual";

    /// <summary>动员·受伤失去——效果预制体标识（＝数据壳 JSON 的 root.id 与 assemblyKey）。</summary>
    public const string MobilizeLossPrefabId = "keyword.mobilize.loss";

    // ---------- csx 行为引用样本（补全点 1；与生产试点并存——本单仅登记与装载，绑定维持 assemblyKey 版） ----------
    // 制品＝KeywordPrefabs/*.csx.prefab.json（csx 来源，无程序集键；解析经 LogicEngine.ScriptEvaluator）。
    // 后续迁移建议：绑定（KeywordRegistry.DeclareEffectBindings）切至下列标识、RegisterHandlers 对应项退役。

    /// <summary>闪击·部署置位（csx 行为引用样本）——效果预制体标识。</summary>
    public const string CsxBlitzDeploySetPrefabId = "keyword.blitz.deploy-set.csx";

    /// <summary>动员·回合累积（csx 行为引用样本）——效果预制体标识。</summary>
    public const string CsxMobilizeAccrualPrefabId = "keyword.mobilize.accrual.csx";

    /// <summary>动员·受伤失去（csx 行为引用样本）——效果预制体标识。</summary>
    public const string CsxMobilizeLossPrefabId = "keyword.mobilize.loss.csx";

    /// <summary>闪击·部署置位——效果名（卡容器检索面契约；实例化时随数据壳装载）。</summary>
    public const string BlitzDeploySetEffectName = "闪击·部署置位";

    /// <summary>动员·回合累积——效果名（卡容器检索面契约；实例化时随数据壳装载）。</summary>
    public const string MobilizeAccrualEffectName = "动员·回合累积";

    /// <summary>动员·受伤失去——效果名（卡容器检索面契约；实例化时随数据壳装载）。</summary>
    public const string MobilizeLossEffectName = "动员·受伤失去";

    /// <summary>
    /// 生产装配（对局/引擎装配段调用——单一时序点承担行为引用注册与库装载，随后绑定核验）：
    /// ①行为引用注册（键 → 委托；注册即配置、重复拒绝）；②库装载（单文件失败隔离、不阻断——报告）；
    /// ③绑定核验（全部绑定引用的库键须可解析；缺失＝装配期 fail-fast）。
    /// </summary>
    /// <param name="engine">对局引擎（预制体面＝<see cref="LogicEngine.Prefabs"/>）。</param>
    /// <param name="libraryDirectory">库目录（缺省＝<see cref="KeywordPrefabLibrary.DefaultDirectory"/>；测试以临时目录取证）。</param>
    /// <exception cref="ArgumentNullException">engine 为 null。</exception>
    /// <exception cref="InvalidOperationException">绑定核验失败（键缺失——配置错误）。</exception>
    public static KeywordEffectAssemblyResult Assemble(LogicEngine engine, string? libraryDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(engine);

        // ① 行为引用注册（程序集来源——显式；键→委托。与数据壳 JSON 的 assemblyKey 一致）。
        var handlers = RegisterHandlers(engine.Prefabs);

        // ② 词条效果库装载（预制体注册；单文件失败＝隔离记录、不阻断其余——报告＋总流留痕）。
        var report = KeywordPrefabLibrary.LoadInto(engine.Prefabs, libraryDirectory);
        foreach (var failure in report.Failures)
        {
            engine.RootStream.WriteLog(
                "词条效果库",
                $"预制体装载失败（隔离：不阻断其余）——{failure}",
                LogLevel.Error,
                new[] { "keyword", "prefab", "error" });
        }

        // ③ 绑定核验（词条声明绑定 → 库键；缺失＝配置错误——装配期 fail-fast，不得静默跳过）。
        VerifyBindings(engine.Prefabs);

        return new KeywordEffectAssemblyResult(handlers, report.Loaded, report.Failures);
    }

    /// <summary>行为引用注册（键 → 委托；注册即配置、重复拒绝——同一引擎实例重复装配被拒绝）。</summary>
    private static int RegisterHandlers(PrefabManager prefabs)
    {
        prefabs.RegisterHandler(
            BlitzDeploySetPrefabId,
            (Func<CardEventView, Context, CancellationToken, Task>)BlitzDeployEffect.HandleDeploySetAsync);
        prefabs.RegisterHandler(
            MobilizeAccrualPrefabId,
            (Func<CardEventView, Context, CancellationToken, Task>)MobilizeAccrualEffect.HandleAccrualAsync);
        prefabs.RegisterHandler(
            MobilizeLossPrefabId,
            (Func<CardEventView, Context, CancellationToken, Task>)MobilizeLossEffect.HandleLossAsync);
        return 3;
    }

    /// <summary>绑定核验（全部绑定：词条声明绑定 → 库键可解析；缺失清单非空＝fail-fast）。</summary>
    private static void VerifyBindings(PrefabManager prefabs)
    {
        var missing = new List<string>();
        foreach (var (keyword, binding) in KeywordRegistry.EnumerateEffectBindings())
        {
            if (!prefabs.TryGetPrefab(binding.PrefabId, out _))
            {
                missing.Add($"词条 '{keyword}' → 效果预制体 '{binding.PrefabId}'");
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "词条效果库绑定核验失败（配置错误——装配期 fail-fast）："
                + string.Join("；", missing)
                + "。");
        }
    }
}
