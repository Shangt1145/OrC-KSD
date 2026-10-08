using Orc.Cards;
using Orc.Core;
using Orc.Game.Board;
using Orc.Game.Cards;
using Orc.Game.EffectParsing.Compilation;
using Orc.Game.EffectParsing.Dsl;
using Orc.Game.EffectParsing.Templates;

namespace Orc.Game.Effects;

// ─────────────────────────────────────────────────────────────────────────────
// csx 动态效果受控面（批 4；「局中动态效果」能力）：
//   ①受控挂载/卸载：从已注册预制体 / 从快照 JSON 实例化并挂载（Add 即装载）；按凭据或效果引用卸载；
//   ②运行时编译挂载：DSL（DslJson）→ EffectCompiler（模板/ops 资产）→ 快照 → 实例化＋挂载；
//   ③统一结果体系：单一结果结构＋单一类别枚举族（贯穿挂/卸/编译挂载）。
// 语义基线（需求-Grill 裁定）：统一装载链（Add 即装载；失败＝完整回滚、零残留、零发射——「防未授权复活」）；
//   托管随卡销毁撤销；卸载成功恰发一次 effect.removed；复装＝新实例新凭据；多实例并存；目标域＝任意 Card。
// 受控入口定位：本面是 csx handler 的游戏层**受控入口**（与门面既有收敛面同源）——csx 创作者经
//   EffectRuntime.ResolveFor(self) 取得门面后调用；弱沙箱前提：csx＝程序集级白名单的**弱沙箱**
//   （同进程、非安全边界）；嵌套 csx＝含 csx 的快照被动态挂载后，其 handler 与「直接书写的 csx」处于
//   **同一沙箱/权限域**（走既有求值器——不引入新权限面）；本批不受理**主动形态的施放/触发**（施放留后续批次；
//   主动形态可挂载/卸载——与内核「列表进出」语义一致）。
// ─────────────────────────────────────────────────────────────────────────────

public sealed partial class EffectRuntime
{
    private readonly Func<LogicEngine?>? _engine;
    private readonly Func<bool>? _isActionAllowed;
    private EffectAssetSource? _assetSource;

    // ---------- 受控挂载 ----------

    /// <summary>
    /// **从已注册预制体挂载**（受控挂载·csx 面；「临时获得某能力」的挂载入口）：按 <paramref name="prefabId"/>
    /// 从引擎预制体库取快照 → 实例化动态效果 → 挂到 <paramref name="target"/>（「Add 即装载」统一装载链）。
    /// <para>目标域＝任意 <see cref="Card"/>（单位/指令/反制/HQ 均可）。成功＝实例化＋生效路径成功
    /// （被动：装载成功；主动：入列表成功）＋凭据有效；失败＝完整回滚（对卡零残留、零发射）——失败类别见
    /// <see cref="EffectAttachStatus"/>（未注册 prefabId＝<see cref="EffectAttachStatus.SourceMissing"/>）。</para>
    /// <para>**受控入口约定（N3 文档化）**：本面仅供 csx 创作者使用（弱沙箱前提——非安全边界）；
    /// 主动形态可挂载/卸载，但**本批不受理施放**（留后续批次）；同一来源可多次挂载（多个独立实例、各持独立凭据）；
    /// 复装（卸载后重挂）＝新实例新凭据。</para>
    /// </summary>
    /// <param name="target">挂载目标卡（须在本对局内）。</param>
    /// <param name="prefabId">已注册效果预制体 id。</param>
    /// <returns>统一结果（成功＝凭据＋效果名；失败＝类别＋原因）。</returns>
    /// <exception cref="ArgumentNullException">target 为 null。</exception>
    /// <exception cref="ArgumentException">prefabId 空白（参数层＝抛；语义层失败＝结构化结果）。</exception>
    public async Task<EffectAttachResult> AttachPrefabAsync(Card target, string prefabId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefabId);

        if (RequireAttachAvailable(target) is { } unavailable)
        {
            return unavailable;
        }

        if (_engine?.Invoke() is not { } engine)
        {
            return EffectAttachResult.Failed(EffectAttachStatus.ServiceUnavailable, UnavailableNoEngine);
        }

        if (!engine.Prefabs.TryGetPrefab(prefabId, out var snapshot))
        {
            return EffectAttachResult.Failed(
                EffectAttachStatus.SourceMissing, $"未注册效果预制体 '{prefabId}'（来源未命中）。");
        }

        return await AttachAndMountAsync(target, snapshot, prefabId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// **从快照 JSON 挂载**（受控挂载·csx 面；支持运行时生成/携带的效果数据）：
    /// 经 <see cref="PrefabJson.Deserialize"/> 读回快照 → 实例化动态效果 → 挂到 <paramref name="target"/>
    /// （「Add 即装载」统一装载链）。
    /// <para>语义同 <c>AttachPrefabAsync</c>：任意 Card 目标域；成功判据（被动＝装载成功、主动＝入列表）＋凭据；
    /// 失败＝完整回滚（零残留、零发射）——JSON 无效＝<see cref="EffectAttachStatus.InvalidData"/>；
    /// 实例化失败（视图类型/处理器解析/csx 编译等）＝<see cref="EffectAttachStatus.InstantiateFailed"/>；
    /// 装载失败＝<see cref="EffectAttachStatus.MountFailed"/>（已回滚；如需重试请重新 Attach）。</para>
    /// <para>**受控入口约定（N3 文档化）**：弱沙箱前提（非安全边界）；快照携带的 csx 与直接书写的 csx
    /// **同一沙箱/权限域**（嵌套 csx 求值走既有求值器）；主动施放不在本批（主动形态可挂/卸）。</para>
    /// </summary>
    /// <param name="target">挂载目标卡（须在本对局内）。</param>
    /// <param name="snapshotJson">效果快照 JSON 文本（<see cref="PrefabJson"/> 形态）。</param>
    /// <returns>统一结果（成功＝凭据＋效果名；失败＝类别＋原因）。</returns>
    /// <exception cref="ArgumentNullException">target 为 null。</exception>
    /// <exception cref="ArgumentException">snapshotJson 为 null/空白（参数层＝抛）。</exception>
    public async Task<EffectAttachResult> AttachSnapshotAsync(Card target, string snapshotJson, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);

        if (RequireAttachAvailable(target) is { } unavailable)
        {
            return unavailable;
        }

        if (!PrefabJson.TryDeserialize(snapshotJson, out var snapshot, out var error))
        {
            return EffectAttachResult.Failed(EffectAttachStatus.InvalidData, $"效果快照 JSON 无效（数据无效）：{error}");
        }

        return await AttachAndMountAsync(target, snapshot!, snapshot!.Root.Id, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// **运行时编译挂载**（受控挂载·csx 面；N2b——DSL → 编译 → 快照 → 实例化 → 挂载）：
    /// <c>DslJson</c> 解析 → <see cref="EffectCompiler"/>（模板/op 资产加载）→ 快照 → 统一挂载链。
    /// <para>资产来源：默认＝程序集输出目录 <c>EffectParsing/Templates</c> 约定（惰性加载、开箱即用）；
    /// 可经 <c>UseEffectAssets</c> 覆盖（指定目录/内存注入）。编译缓存＝进程级共享（键＝DSL 文本＋资产指纹＋身份；
    /// 同输入不重复全量编译、资产变更必反映——「同 DSL 二次编译行为等价」）。</para>
    /// <para>效果身份：<paramref name="effectId"/>＝调用时指定（用于快照 root.id／挂载效果名／审查链检索）；
    /// 缺省＝自动生成（确定性派生，便捷路径——不依赖唯一性语义，同名不同实例允许）。</para>
    /// <para>失败分类：DSL 解析失败＝<see cref="EffectAttachStatus.InvalidData"/>；模板/op 资产缺失＝
    /// <see cref="EffectAttachStatus.SourceMissing"/>；渲染/编译层失败＝<see cref="EffectAttachStatus.CompileFailed"/>；
    /// 其后同挂载链（实例化/装载失败类别）。**本批不受理主动施放**（主动形态可挂/卸）。</para>
    /// </summary>
    /// <param name="target">挂载目标卡（须在本对局内）。</param>
    /// <param name="dslJson">效果 DSL JSON 文本（<see cref="DslJson"/> 形态）。</param>
    /// <param name="effectId">效果身份（可选；缺省＝自动生成）。</param>
    /// <returns>统一结果（成功＝凭据＋效果名；失败＝类别＋原因）。</returns>
    /// <exception cref="ArgumentNullException">target 为 null。</exception>
    /// <exception cref="ArgumentException">dslJson 为 null/空白；effectId 给出但空白（参数层＝抛）。</exception>
    public async Task<EffectAttachResult> CompileAttachAsync(
        Card target, string dslJson, string? effectId = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(dslJson);
        if (effectId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        }

        if (RequireAttachAvailable(target) is { } unavailable)
        {
            return unavailable;
        }

        if (!DslJson.TryDeserialize(dslJson, out var dsl, out var parseError))
        {
            return EffectAttachResult.Failed(EffectAttachStatus.InvalidData, $"效果 DSL 无效（数据无效）：{parseError}");
        }

        EffectAssetBundle bundle;
        try
        {
            bundle = EffectAssetResolver.Resolve(_assetSource);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 防御兜底（目录不可读/权限类/IO 安全异常等——语义层失败一律结构化返回，不外抛）：
            // 相关请求按「来源未命中」失败（承接第 3 轮 1b：资产不可用＝按类别失败、不阻断）。
            return EffectAttachResult.Failed(
                EffectAttachStatus.SourceMissing, $"资产解析失败（来源未命中）：{ex.Message}");
        }

        if (!bundle.Templates.Any(t => string.Equals(t.Id, dsl!.Template, StringComparison.Ordinal)))
        {
            return EffectAttachResult.Failed(
                EffectAttachStatus.SourceMissing, $"模板效果 '{dsl!.Template}' 不在资产目录（来源未命中——模板资产缺失）。");
        }

        if (FindMissingOp(dsl!.Fills, bundle.Ops) is { } missingOp)
        {
            return EffectAttachResult.Failed(
                EffectAttachStatus.SourceMissing, $"op 语句模板 '{missingOp}' 缺失（来源未命中——op 资产缺失）。");
        }

        var resolvedId = effectId ?? AutoEffectId(dslJson);
        EffectSnapshot compiled;
        try
        {
            var cacheKey = $"{bundle.Fingerprint}|{resolvedId}|{StableHash.Fnv1a64(dslJson):x16}";
            compiled = EffectCompilationCache.GetOrCompile(
                cacheKey, () => new EffectCompiler(bundle.Templates, bundle.Ops).Compile(dsl, resolvedId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return EffectAttachResult.Failed(EffectAttachStatus.CompileFailed, $"DSL 编译失败（编译失败）：{ex.Message}");
        }

        return await AttachAndMountAsync(target, compiled, resolvedId, ct).ConfigureAwait(false);
    }

    // ---------- 受控卸载 ----------

    /// <summary>
    /// **按挂载凭据卸载**（受控卸载·csx 面）：卸载由本面挂载、凭据指认的效果实例
    /// （凭据由挂载方法同次返回；「持有并回传」即可）。
    /// <para>语义层失败一律结构化返回（不抛）：凭据对应效果不存在/已清理/旧凭据失效＝
    /// <see cref="EffectAttachStatus.NotFound"/>；效果属于另一张卡（跨卡归属）＝
    /// <see cref="EffectAttachStatus.CrossCardOwnership"/>（零副作用）；对局终局后＝按失效结果处理
    /// （<see cref="EffectAttachStatus.NotFound"/>）。成功＝实际移除命中——恰发射一次 <c>effect.removed</c>。</para>
    /// <para>**受控入口约定（N3 文档化）**：弱沙箱前提（非安全边界）；主动施放不在本批（主动形态可挂/卸）。</para>
    /// </summary>
    /// <param name="target">卸载目标卡（须与挂载时的目标一致）。</param>
    /// <param name="credential">挂载凭据（挂载方法返回值）。</param>
    /// <returns>统一结果（成功＝效果名；失败＝类别＋原因）。</returns>
    /// <exception cref="ArgumentNullException">target 或 credential 为 null。</exception>
    public async Task<EffectAttachResult> DetachEffectAsync(
        Card target, EffectAttachCredential credential, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(credential);

        return await DetachEffectAsync(target, credential.Effect, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// **按效果引用卸载**（受控卸载·csx 面；同名重载——「卸载这个效果」一种语义）：
    /// 指认「卡上已有者」——装载期效果／词条内嵌／他人挂载等（csx 可经公开读面 <see cref="Card.Effects"/>
    /// 取得实例引用）；与本面挂载者凭据指认**语义一致**（同一结果体系与副作用语义）。
    /// <para>成功＝实际移除命中——恰发射一次 <c>effect.removed</c>（载荷＝{Card, Effect}）；幂等无害：
    /// 不存在/已清理＝<see cref="EffectAttachStatus.NotFound"/>、非本卡＝<see cref="EffectAttachStatus.CrossCardOwnership"/>
    /// （结构化结果、不抛、零副作用）。</para>
    /// </summary>
    /// <param name="target">卸载目标卡。</param>
    /// <param name="effect">效果实例引用（来自 <see cref="Card.Effects"/> 读面等）。</param>
    /// <returns>统一结果（成功＝效果名；失败＝类别＋原因）。</returns>
    /// <exception cref="ArgumentNullException">target 或 effect 为 null。</exception>
    public async Task<EffectAttachResult> DetachEffectAsync(Card target, Effect effect, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(effect);

        if (RequireDetachAvailable(target) is { } unavailable)
        {
            return unavailable;
        }

        var belongs = ContainsEffect(target, effect);
        var host = TryGetHost(effect);
        if (belongs || ReferenceEquals(host, target))
        {
            try
            {
                target.RemoveEffect(effect); // 成功事务：实际移除命中恰发射一次 effect.removed（既有语义）
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return EffectAttachResult.Failed(
                    EffectAttachStatus.NotFound, $"效果 '{effect.Name}' 卸载失败（按已失效处理）：{ex.Message}");
            }

            return EffectAttachResult.Ok(null, effect.Name);
        }

        if (host is not null)
        {
            return EffectAttachResult.Failed(
                EffectAttachStatus.CrossCardOwnership,
                $"效果 '{effect.Name}' 属于另一张卡牌（跨卡归属——不能经本卡卸载）。");
        }

        return EffectAttachResult.Failed(
            EffectAttachStatus.NotFound, $"效果 '{effect.Name}' 不在卡牌 '{target.Name}' 上（未命中或已失效）。");
    }

    // ---------- 编译资产配置（显式覆盖入口） ----------

    /// <summary>
    /// **显式覆盖编译资产目录**（默认＝程序集输出目录约定；测试自含/工具链自定义资产集用）：
    /// 目录约定——根含 <c>*.tpl.json</c>（模板效果）、<c>ops</c> 子目录含 <c>*.csx.tpl</c>（op 语句模板）。
    /// 可重设（后设覆盖先设）；资产按内容指纹缓存——目录内容变化必反映。
    /// </summary>
    /// <exception cref="ArgumentException">directory 为 null/空白。</exception>
    public void UseEffectAssets(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _assetSource = EffectAssetSource.ForDirectory(directory);
    }

    /// <summary>
    /// **显式覆盖编译资产（内存注入）**（测试自含/工具链用）：直接注入已加载的模板集合与 op 目录对象
    /// （不经目录扫描）。可与 <c>UseEffectAssets(string)</c> 交替重设。
    /// </summary>
    /// <exception cref="ArgumentNullException">templates 或 ops 为 null。</exception>
    public void UseEffectAssets(IReadOnlyList<EffectTemplate> templates, OpTemplateCatalog ops)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(ops);
        _assetSource = EffectAssetSource.ForMemory(templates, ops);
    }

    // ---------- 内部：可用性校验与挂载/回滚核心 ----------

    private const string UnavailableNoEngine = "效果运行时未装配（无引擎上下文——服务不可用）。";

    private const string UnavailableNotOperable = "对局不在可操作相位（未进行/终局——服务不可用）。";

    private const string UnavailableForeignTarget = "目标卡不在本对局（非本对局引用——服务不可用）。";

    private const string UnavailableDestroyedTarget = "目标卡已销毁（已失效——不构成有效宿主）。";

    /// <summary>
    /// 挂载面可用性校验（服务不可达/未装配＝结构化失败；null＝可用）：
    /// ①引擎上下文就绪；②对局处于可操作相位（终局/未进行＝拒斥）；③目标卡归属本对局（已装配）；
    /// ④目标卡存活（已销毁＝引用类无效——「未命中/已失效」：销毁链不清 Owner、ResolveFor 仍可达，
    /// 故须独立判据拦截，防效果挂上死卡）。
    /// </summary>
    private EffectAttachResult? RequireAttachAvailable(Card target)
    {
        if (_engine?.Invoke() is null)
        {
            return EffectAttachResult.Failed(EffectAttachStatus.ServiceUnavailable, UnavailableNoEngine);
        }

        if (_isActionAllowed is { } allowed && !allowed())
        {
            return EffectAttachResult.Failed(EffectAttachStatus.ServiceUnavailable, UnavailableNotOperable);
        }

        if (ResolveFor(target) is not { } resolved || !ReferenceEquals(resolved, this))
        {
            return EffectAttachResult.Failed(EffectAttachStatus.ServiceUnavailable, UnavailableForeignTarget);
        }

        if (!target.Life.IsAlive)
        {
            return EffectAttachResult.Failed(EffectAttachStatus.NotFound, UnavailableDestroyedTarget);
        }

        return null;
    }

    /// <summary>
    /// 卸载面可用性校验（null＝可用）：对局终局/非可操作相位＝**按失效结果处理**
    /// （<see cref="EffectAttachStatus.NotFound"/>——终局门禁先例：对外面拒绝、零副作用、不抛）；
    /// 目标卡已销毁＝按失效结果处理（引用类无效——不执行卸载、幂等无害）；
    /// 未加载卡（无归属解析）不拦截（按效果面自然判定「未命中」）。
    /// </summary>
    private EffectAttachResult? RequireDetachAvailable(Card target)
    {
        if (_engine?.Invoke() is null)
        {
            return EffectAttachResult.Failed(EffectAttachStatus.ServiceUnavailable, UnavailableNoEngine);
        }

        if (_isActionAllowed is { } allowed && !allowed())
        {
            return EffectAttachResult.Failed(
                EffectAttachStatus.NotFound, "对局已结束或未在可操作相位（卸载按失效结果处理）。");
        }

        if (ResolveFor(target) is { } resolved && !ReferenceEquals(resolved, this))
        {
            return EffectAttachResult.Failed(EffectAttachStatus.ServiceUnavailable, UnavailableForeignTarget);
        }

        if (!target.Life.IsAlive)
        {
            return EffectAttachResult.Failed(EffectAttachStatus.NotFound, UnavailableDestroyedTarget);
        }

        return null;
    }

    /// <summary>
    /// 挂载核心（三门面共用）：实例化 → Add 即装载 → 失败检测与完整回滚。
    /// 成功判据（形态普适）：被动＝实例化＋装载成功；主动＝实例化＋入列表成功；两者凭据同样有效。
    /// </summary>
    private async Task<EffectAttachResult> AttachAndMountAsync(
        Card target, EffectSnapshot snapshot, string name, CancellationToken ct)
    {
        if (_engine?.Invoke() is not { } engine)
        {
            return EffectAttachResult.Failed(EffectAttachStatus.ServiceUnavailable, UnavailableNoEngine);
        }

        var instantiation = DynamicEffectFactory.Instantiate(engine, snapshot, name);
        if (!instantiation.Success)
        {
            return EffectAttachResult.Failed(
                EffectAttachStatus.InstantiateFailed,
                $"效果 '{name}' 实例化失败（{instantiation.ErrorCategory}）：{instantiation.Error}");
        }

        var effect = instantiation.Effect!;
        try
        {
            target.AddEffect(effect);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return EffectAttachResult.Failed(
                EffectAttachStatus.MountFailed, $"效果 '{name}' 装载失败（已回滚、零残留）：{ex.Message}");
        }

        if (effect.Kind == TriggerKind.Passive && !effect.IsMounted)
        {
            await RollbackFailedMountAsync(target, effect, ct).ConfigureAwait(false);
            return EffectAttachResult.Failed(
                EffectAttachStatus.MountFailed,
                $"效果 '{name}' 装载失败（已完整回滚、对卡零残留；如需重试请重新 Attach）。");
        }

        return EffectAttachResult.Ok(new EffectAttachCredential(effect), name);
    }

    /// <summary>
    /// 装载失败**完整回滚**（受控面契约——与内核「留列表待兜底」语义的差异点）：
    /// ①托管残留清理（对齐内核装载失败清理口径：光环声明注销 → 按来源撤销修饰器 → 受益侧衔接；异常隔离）；
    /// ②容器**静默移除**（<see cref="Card.RemoveEffectSilently"/>——零发射：不发射 <c>effect.removed</c>；
    /// 防「未授权复活」：失败效果不留列表、不被放置驱动的幂等兜底静默复活）。
    /// </summary>
    private async Task RollbackFailedMountAsync(Card target, Effect effect, CancellationToken ct)
    {
        var environment = GameEnvironment.ResolveFor(target);
        var auraRemoved = 0;
        if (environment is not null)
        {
            try
            {
                auraRemoved = environment.Auras.RemoveBySource(effect);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                WriteRollbackError(effect, "光环残留清理", ex);
            }
        }

        if (target is CardBase cardBase)
        {
            try
            {
                await cardBase.Modifiers.RemoveBySourceAsync(effect, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                WriteRollbackError(effect, "修饰器残留清理", ex);
            }
        }

        if (environment is not null && auraRemoved > 0)
        {
            try
            {
                await environment.RerunAllCardsAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                WriteRollbackError(effect, "光环衔接", ex);
            }
        }

        target.RemoveEffectSilently(effect); // 零发射；未装载路径＝容器面移除＋清宿主引用
    }

    private void WriteRollbackError(Effect effect, string phase, Exception ex)
    {
        _engine?.Invoke()?.RootStream.WriteLog(
            "效果动态挂载",
            $"装载失败回滚（{phase}）异常（隔离：继续完成回滚）——'{effect.Name}'：{ex.Message}",
            LogLevel.Error,
            new[] { "effect", "dynamic", "error", $"exception:{ex.GetType().Name}", effect.Name },
            new Dictionary<string, object?>
            {
                ["exceptionType"] = ex.GetType().FullName,
                ["message"] = ex.Message,
            });
    }

    /// <summary>卡效果列表包含判定（引用同一性——遍历；公开只读列表面）。</summary>
    private static bool ContainsEffect(Card target, Effect effect)
    {
        foreach (var candidate in target.Effects)
        {
            if (ReferenceEquals(candidate, effect))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>读取效果宿主（未挂载/引用已清理＝null——防御：宿主访问在未添加上抛明确异常）。</summary>
    private static Card? TryGetHost(Effect effect)
    {
        try
        {
            return effect.Host;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>自动效果 id（缺省身份——确定性派生：同 DSL 文本同 id；不依赖唯一性语义，同名不同实例允许）。</summary>
    private static string AutoEffectId(string dslJson) => "effect.runtime." + StableHash.Fnv1a64(dslJson).ToString("x16");

    /// <summary>
    /// 递归查找缺失 op（DSL 填写的任一 op 无 csx 语句模板＝来源未命中；含嵌套 DSL）。
    /// 批 5 起可见性放宽〔private→internal，加性〕供离线编译驱动复用（「来源未命中」预检单一真源）。
    /// </summary>
    internal static string? FindMissingOp(IReadOnlyDictionary<string, DslSlotFill> fills, OpTemplateCatalog ops)
    {
        foreach (var fill in fills.Values)
        {
            foreach (var op in fill.Ops)
            {
                if (!ops.Has(op.Op))
                {
                    return op.Op;
                }

                if (op.Nested is { Count: > 0 })
                {
                    foreach (var nested in op.Nested)
                    {
                        if (FindMissingOp(nested.Fills, ops) is { } missing)
                        {
                            return missing;
                        }
                    }
                }
            }
        }

        return null;
    }
}
