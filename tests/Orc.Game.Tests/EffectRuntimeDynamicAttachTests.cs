using Orc.Cards;
using Orc.Core;
using Orc.Game;
using Orc.Game.Cards;
using Orc.Game.EffectParsing.Compilation;
using Orc.Game.EffectParsing.Dsl;
using Orc.Game.EffectParsing.Parsing;
using Orc.Game.EffectParsing.Templates;
using Orc.Game.Effects;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// csx 动态效果能力（批 4）——受控挂载/卸载验收：
/// ①csx 视角端到端（运行时生成/挂/卸 → 对局行为断言，含「临时获得某能力」与「csx 调门面」样例）；
/// ②失败路径（无效快照/未注册预制体/跨卡归属/卸载不存在/装载失败/服务不可用/参数层）；
/// ③托管与信号（effect.removed 恰一次含载荷；失败回滚零发射；随卡销毁撤销；复装；多实例并存；主动形态边界）。
/// 断言口径：黑盒行为为主（手牌/世界状态），失败/托管/信号辅以中间态（效果列表/类别/信号计数）。
/// </summary>
public class EffectRuntimeDynamicAttachTests
{
    // ---------- ① csx 视角端到端 ----------

    /// <summary>
    /// 「临时获得某能力」样例（被动形态）：运行时生成的 csx 效果经快照挂载 → 行为生效；
    /// 卸载 → 恰一次 effect.removed（含载荷）→ **行为确实停止**（反向断言）。
    /// </summary>
    [Fact]
    public async Task 快照挂载_行为生效_卸载后行为停止()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(host);
        Assert.NotNull(runtime);

        var removed = new List<(object? Card, object? Effect)>();
        using var probe = CollectEffectRemoved(match, removed);

        var snapshot = CompileFromText("友方单位加入时，抽一张牌。", "effect.temp.ability");
        var attach = await runtime!.AttachSnapshotAsync(host, PrefabJson.Serialize(snapshot));

        Assert.True(attach.Success, attach.FailureReason);
        Assert.NotNull(attach.Credential);
        Assert.Equal("effect.temp.ability", attach.EffectName);
        var attachedEffect = Assert.Single(host.Effects);
        Assert.Empty(removed); // 挂载过程零发射（挂载成功不新增专用信号）

        var handBefore = playerA.Hand.Count;
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        Assert.Equal(handBefore + 1, playerA.Hand.Count); // 行为生效（csx 真执行）

        var detach = await runtime.DetachEffectAsync(host, attach.Credential!);
        Assert.True(detach.Success, detach.FailureReason);
        Assert.Empty(host.Effects);

        // 卸载成功＝恰一次 effect.removed（含载荷 {Card, Effect}）
        var signal = Assert.Single(removed);
        Assert.Same(host, signal.Card);
        Assert.Same(attachedEffect, signal.Effect);

        var handAfterDetach = playerA.Hand.Count;
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        Assert.Equal(handAfterDetach, playerA.Hand.Count); // 反向断言：卸载后行为确实停止
    }

    /// <summary>「从已注册预制体挂载」成功路径：注册快照 → 按 prefabId 挂载 → 行为 → 卸载。</summary>
    [Fact]
    public async Task 预制体挂载_行为生效_卸载成功()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var snapshot = CompileFromText("友方单位加入时，抽一张牌。", "effect.prefab.attach");
        match.Engine.Prefabs.RegisterPrefab(snapshot);
        var runtime = EffectRuntime.ResolveFor(host)!;

        var attach = await runtime.AttachPrefabAsync(host, "effect.prefab.attach");
        Assert.True(attach.Success, attach.FailureReason);
        Assert.Equal("effect.prefab.attach", attach.EffectName);
        Assert.Single(host.Effects);

        var handBefore = playerA.Hand.Count;
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        Assert.Equal(handBefore + 1, playerA.Hand.Count);

        var detach = await runtime.DetachEffectAsync(host, attach.Credential!);
        Assert.True(detach.Success, detach.FailureReason);
        Assert.Empty(host.Effects);
    }

    /// <summary>
    /// **「csx 调门面」样例**（硬性）：某 csx 效果（A）在其 handler 内调用受控方法（<c>CompileAttachAsync</c>）
    /// 把另一效果（B，同样由 csx 承载）挂到宿主卡——证明受控面对创作者的真实可达性与闭环；且 A 为
    /// **csx 挂 csx** 的嵌套样例（N3 评估证据：嵌套 csx 与直接书写 csx 同沙箱同求值器）。
    /// </summary>
    [Fact]
    public async Task csx调门面_在handler内编译挂载另一效果()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(host)!;

        // B：被 A 在运行时编译挂载的效果（csx 承载——join 监听 + 抽牌）。
        const string bEffectId = "effect.csx.nested.b";
        const string bAlreadyGuard = "effect.csx.nested.b";
        var dslBJson = DslJsonOfText("友方单位加入时，抽一张牌。");
        var quotedB = System.Text.Json.JsonSerializer.Serialize(dslBJson);

        // A：csx 手写（受控面可达性样例）——加入触发时（首次）经门面编译挂载 B。
        var csxA = $$"""
            Func<Orc.Cards.CardEventView, Context, CancellationToken, Task> HandleAsync = async (view, ctx, ct) =>
            {
                var self = view.Host as Orc.Cards.Card;
                if (self is null) { return; }
                var runtime = Orc.Game.Effects.EffectRuntime.ResolveFor(self);
                if (runtime is null) { return; }
                var already = false;
                foreach (var e in self.Effects) { if (e.Name == "{{bAlreadyGuard}}") { already = true; break; } }
                if (!already)
                {
                    await runtime.CompileAttachAsync(self, {{quotedB}}, "{{bEffectId}}");
                }
            };
            """;

        var snapshotA = new EffectSnapshot(new EffectPrefab(
            "effect.csx.nested.a",
            new TriggerPrefab(
                "t.a", "test.csx.nested.a", TriggerKind.Passive,
                hooks: new[] { "unit.joined" },
                events: new[] { new EventPrefab("e1", csxSource: csxA) })));

        var attachA = await runtime.AttachSnapshotAsync(host, PrefabJson.Serialize(snapshotA));
        Assert.True(attachA.Success, attachA.FailureReason);

        // 触发 A：加入一个单位 → A 的 csx 执行 → 经门面把 B 编译挂载到宿主。
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        Assert.Contains(host.Effects, e => e.Name == bEffectId);

        // B 的行为生效：再加一个单位 → B 触发 → 手牌 +1；A 的查重防护保证不会重复挂载。
        var handBefore = playerA.Hand.Count;
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        Assert.Equal(handBefore + 1, playerA.Hand.Count);

        // 收尾：B 经效果引用卸载（创作者的收口路径）。
        var effectB = host.Effects.First(e => e.Name == bEffectId);
        var detachB = await runtime.DetachEffectAsync(host, effectB);
        Assert.True(detachB.Success, detachB.FailureReason);
        Assert.DoesNotContain(host.Effects, e => e.Name == bEffectId);
    }

    // ---------- ② 失败路径与幂等语义 ----------

    /// <summary>重复挂载：两次挂载均成功、两凭据独立、分别卸载互不影响（多实例并存）。</summary>
    [Fact]
    public async Task 重复挂载_独立凭据_分别卸载互不影响()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(host)!;
        var json = PrefabJson.Serialize(CompileFromText("友方单位加入时，抽一张牌。", "effect.multi.instance"));

        var first = await runtime.AttachSnapshotAsync(host, json);
        var second = await runtime.AttachSnapshotAsync(host, json);
        Assert.True(first.Success, first.FailureReason);
        Assert.True(second.Success, second.FailureReason);
        Assert.NotSame(first.Credential, second.Credential);
        Assert.Equal(2, host.Effects.Count);
        Assert.NotSame(host.Effects[0], host.Effects[1]); // 两个独立实例

        var before = playerA.Hand.Count;
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        Assert.Equal(before + 2, playerA.Hand.Count); // 两实例并存：各触发一次 = +2

        Assert.True((await runtime.DetachEffectAsync(host, first.Credential!)).Success);
        Assert.Single(host.Effects);
        var mid = playerA.Hand.Count;
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        Assert.Equal(mid + 1, playerA.Hand.Count); // 卸一后剩一：+1

        Assert.True((await runtime.DetachEffectAsync(host, second.Credential!)).Success);
        Assert.Empty(host.Effects);
        var last = playerA.Hand.Count;
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        Assert.Equal(last, playerA.Hand.Count); // 卸二后：+0
    }

    /// <summary>复装：卸载后重挂＝新凭据、行为恢复；旧凭据失效（Detach＝未命中/已失效）。</summary>
    [Fact]
    public async Task 复装_新凭据_行为恢复_旧凭据失效()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(host)!;
        var json = PrefabJson.Serialize(CompileFromText("友方单位加入时，抽一张牌。", "effect.remount.probe"));

        var first = await runtime.AttachSnapshotAsync(host, json);
        Assert.True(first.Success, first.FailureReason);
        var before1 = playerA.Hand.Count;
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        Assert.Equal(before1 + 1, playerA.Hand.Count);

        Assert.True((await runtime.DetachEffectAsync(host, first.Credential!)).Success);
        var stopped = playerA.Hand.Count;
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        Assert.Equal(stopped, playerA.Hand.Count); // 行为停止

        var second = await runtime.AttachSnapshotAsync(host, json);
        Assert.True(second.Success, second.FailureReason);
        Assert.NotSame(first.Credential, second.Credential); // 新实例新凭据
        var restored = playerA.Hand.Count;
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        Assert.Equal(restored + 1, playerA.Hand.Count); // 行为恢复

        var stale = await runtime.DetachEffectAsync(host, first.Credential!);
        Assert.Equal(EffectAttachStatus.NotFound, stale.Status); // 旧凭据失效（未命中/已失效）
        Assert.Single(host.Effects);
    }

    /// <summary>「卡上已有者」经**效果引用**指认卸载（同名重载成功路径）。</summary>
    [Fact]
    public async Task 卸载_经效果引用指认_成功()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(host)!;
        var json = PrefabJson.Serialize(CompileFromText("友方单位加入时，抽一张牌。", "effect.by.reference"));

        var attach = await runtime.AttachSnapshotAsync(host, json);
        Assert.True(attach.Success, attach.FailureReason);
        var attached = Assert.Single(host.Effects);

        var detach = await runtime.DetachEffectAsync(host, attached); // 不传凭据——引用指认
        Assert.True(detach.Success, detach.FailureReason);
        Assert.Empty(host.Effects);
    }

    /// <summary>失败路径：无效快照 JSON ＝ 数据无效；零残留、零发射、不抛。</summary>
    [Fact]
    public async Task 失败路径_无效快照JSON_数据无效_零残留()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(host)!;

        var removed = new List<(object? Card, object? Effect)>();
        using var probe = CollectEffectRemoved(match, removed);

        var result = await runtime.AttachSnapshotAsync(host, "{ this is not a valid snapshot");
        Assert.Equal(EffectAttachStatus.InvalidData, result.Status);
        Assert.False(result.Success);
        Assert.Null(result.Credential);
        Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
        Assert.Empty(host.Effects);
        Assert.Empty(removed);
    }

    /// <summary>失败路径：未注册 prefabId ＝ 来源未命中。</summary>
    [Fact]
    public async Task 失败路径_未注册预制体_来源未命中()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(host)!;

        var result = await runtime.AttachPrefabAsync(host, "prefab.not.registered");
        Assert.Equal(EffectAttachStatus.SourceMissing, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
        Assert.Empty(host.Effects);
    }

    /// <summary>失败路径：跨卡归属 ＝ 结构化类别、零副作用（目标效果不受影响、无信号发射）。</summary>
    [Fact]
    public async Task 失败路径_跨卡归属_结构化结果_零副作用()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var hostA = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var hostB = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(hostA)!;
        var json = PrefabJson.Serialize(CompileFromText("友方单位加入时，抽一张牌。", "effect.cross.card"));

        var attach = await runtime.AttachSnapshotAsync(hostA, json);
        Assert.True(attach.Success, attach.FailureReason);

        var removed = new List<(object? Card, object? Effect)>();
        using var probe = CollectEffectRemoved(match, removed);

        var result = await runtime.DetachEffectAsync(hostB, attach.Credential!); // 卡 B 指认挂在卡 A 上的效果
        Assert.Equal(EffectAttachStatus.CrossCardOwnership, result.Status);
        Assert.Single(hostA.Effects); // 零副作用：卡 A 上效果不受影响
        Assert.Empty(hostB.Effects);
        Assert.Empty(removed); // 零发射

        var before = playerA.Hand.Count;
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        Assert.Equal(before + 1, playerA.Hand.Count); // 目标效果行为照常
    }

    /// <summary>失败路径：卸载不存在/已失效（旧凭据重复卸载）＝ 未命中。</summary>
    [Fact]
    public async Task 失败路径_已失效凭据_未命中()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(host)!;
        var json = PrefabJson.Serialize(CompileFromText("友方单位加入时，抽一张牌。", "effect.stale.credential"));

        var attach = await runtime.AttachSnapshotAsync(host, json);
        Assert.True(attach.Success, attach.FailureReason);
        Assert.True((await runtime.DetachEffectAsync(host, attach.Credential!)).Success);

        var stale = await runtime.DetachEffectAsync(host, attach.Credential!);
        Assert.Equal(EffectAttachStatus.NotFound, stale.Status);
        Assert.False(string.IsNullOrWhiteSpace(stale.FailureReason));
    }

    /// <summary>
    /// 失败路径：**装载失败**（实例化成功、装载链抛）＝ 完整回滚——对卡零残留、零发射、宿主与其它效果不受影响、
    /// 后续操作不受阻（「防未授权复活」硬防线：失败效果不留列表、不被幂等兜底静默复活）。
    /// </summary>
    [Fact]
    public async Task 失败路径_装载失败_完整回滚_零残留_零发射()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(host)!;

        // 先挂一个正常效果（「宿主与其它效果不受影响」对照）。
        var okJson = PrefabJson.Serialize(CompileFromText("友方单位加入时，抽一张牌。", "effect.load.ok"));
        Assert.True((await runtime.AttachSnapshotAsync(host, okJson)).Success);

        // 构造装载会失败的快照：主触发器正常挂载，但「其它触发器」无 hooks（总线拒绝挂载 ⇒ 装载失败）。
        var failing = new EffectSnapshot(new EffectPrefab(
            "effect.load.fail",
            new TriggerPrefab(
                "t.main", "test.load.fail.main", TriggerKind.Passive,
                hooks: new[] { "test.load.fail.hook" },
                events: Array.Empty<EventPrefab>()),
            otherTriggers: new[]
            {
                new TriggerPrefab(
                    "t.other", "test.load.fail.other", TriggerKind.Passive,
                    hooks: Array.Empty<string>(),
                    events: Array.Empty<EventPrefab>()),
            }));

        var removed = new List<(object? Card, object? Effect)>();
        using var probe = CollectEffectRemoved(match, removed);

        var result = await runtime.AttachSnapshotAsync(host, PrefabJson.Serialize(failing));
        Assert.Equal(EffectAttachStatus.MountFailed, result.Status);
        Assert.Null(result.Credential);
        Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
        Assert.Single(host.Effects); // 零残留（仅既有 ok 效果）
        Assert.Empty(removed); // 失败回滚零发射（静默清理，不发 effect.removed）

        // 宿主与其它效果不受影响：ok 效果照常触发。
        var before = playerA.Hand.Count;
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        Assert.Equal(before + 1, playerA.Hand.Count);

        // 后续操作不受阻 + 失败回滚幂等：重复失败仍干净，且不影响成功路径。
        var again = await runtime.AttachSnapshotAsync(host, PrefabJson.Serialize(failing));
        Assert.Equal(EffectAttachStatus.MountFailed, again.Status);
        Assert.Single(host.Effects);
        var followUp = await runtime.AttachSnapshotAsync(host, okJson);
        Assert.True(followUp.Success, followUp.FailureReason);
        Assert.Equal(2, host.Effects.Count);
    }

    /// <summary>失败路径：服务不可达/未装配（独立构造门面——无引擎上下文）＝ 服务不可用。</summary>
    [Fact]
    public async Task 失败路径_服务不可用_未装配门面()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var json = PrefabJson.Serialize(CompileFromText("友方单位加入时，抽一张牌。", "effect.no.engine"));

        var bare = new EffectRuntime(() => null, () => null, () => null, () => null);
        var attach = await bare.AttachSnapshotAsync(host, json);
        Assert.Equal(EffectAttachStatus.ServiceUnavailable, attach.Status);

        // 真门面挂一个效果后，用 bare 门面卸载 → 同样服务不可用（引擎上下文缺失）。
        var runtime = EffectRuntime.ResolveFor(host)!;
        var attachReal = await runtime.AttachSnapshotAsync(host, json);
        Assert.True(attachReal.Success, attachReal.FailureReason);
        var detach = await bare.DetachEffectAsync(host, attachReal.Credential!);
        Assert.Equal(EffectAttachStatus.ServiceUnavailable, detach.Status);
    }

    /// <summary>失败路径：非本对局引用 ＝ 服务不可用（挂载与卸载两个方向）。</summary>
    [Fact]
    public async Task 失败路径_非本对局引用_服务不可用()
    {
        var match1 = await CreateMatchAsync();
        var match2 = await CreateMatchAsync();
        var card1 = await CommandTestKit.PrepareOnSupportAsync(match1, match1.Players[0], CommandTestKit.InfantryId, 0);
        var card2 = await CommandTestKit.PrepareOnSupportAsync(match2, match2.Players[0], CommandTestKit.InfantryId, 0);
        var runtime1 = EffectRuntime.ResolveFor(card1)!;
        var json = PrefabJson.Serialize(CompileFromText("友方单位加入时，抽一张牌。", "effect.foreign.target"));

        var attach = await runtime1.AttachSnapshotAsync(card2, json);
        Assert.Equal(EffectAttachStatus.ServiceUnavailable, attach.Status);
        Assert.Empty(card2.Effects);

        var okAttach = await runtime1.AttachSnapshotAsync(card1, json);
        Assert.True(okAttach.Success, okAttach.FailureReason);
        var detach = await runtime1.DetachEffectAsync(card2, okAttach.Credential!);
        Assert.Equal(EffectAttachStatus.ServiceUnavailable, detach.Status);
        Assert.Single(card1.Effects);
    }

    /// <summary>
    /// 失败路径：**已销毁卡**＝引用类无效（未命中/已失效）——销毁链不清 Owner、门面解析仍可达，
    /// 故挂载面须独立拦截：Attach 到已销毁卡＝失败类别＋零副作用（不挂载、不发射）。
    /// </summary>
    [Fact]
    public async Task 失败路径_已销毁卡_挂载拒斥_零副作用()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(host)!;
        var json = PrefabJson.Serialize(CompileFromText("友方单位加入时，抽一张牌。", "effect.destroyed.target"));

        await match.Engine.DestroyCard(host);
        Assert.False(host.Life.IsAlive);

        var removed = new List<(object? Card, object? Effect)>();
        using var probe = CollectEffectRemoved(match, removed);

        var attach = await runtime.AttachSnapshotAsync(host, json);
        Assert.Equal(EffectAttachStatus.NotFound, attach.Status); // 已失效目标：不挂载
        Assert.Null(attach.Credential);
        Assert.False(string.IsNullOrWhiteSpace(attach.FailureReason));
        Assert.Empty(host.Effects); // 零副作用（零残留）
        Assert.Empty(removed); // 零发射

        var prefab = await runtime.AttachPrefabAsync(host, "effect.destroyed.target");
        Assert.Equal(EffectAttachStatus.NotFound, prefab.Status); // 三门面同一判据
        Assert.Empty(host.Effects);
    }

    /// <summary>参数层边界：null/空白参数＝抛（语义层失败才走结构化结果）。</summary>
    [Fact]
    public async Task 失败路径_参数层_null与空白_抛出()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(host)!;

        await Assert.ThrowsAsync<ArgumentNullException>(() => runtime.AttachSnapshotAsync(null!, "{}"));
        await Assert.ThrowsAsync<ArgumentException>(() => runtime.AttachSnapshotAsync(host, "  "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => runtime.AttachPrefabAsync(host, null!));
        await Assert.ThrowsAsync<ArgumentException>(() => runtime.AttachPrefabAsync(host, " "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => runtime.CompileAttachAsync(host, null!));
        await Assert.ThrowsAsync<ArgumentException>(() => runtime.CompileAttachAsync(host, " "));
        await Assert.ThrowsAsync<ArgumentException>(() => runtime.CompileAttachAsync(host, "{}", " "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => runtime.DetachEffectAsync(host, (EffectAttachCredential)null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => runtime.DetachEffectAsync(host, (Effect)null!));

        Assert.Throws<ArgumentNullException>(() => runtime.UseEffectAssets((IReadOnlyList<EffectTemplate>)null!, null!));
        Assert.Throws<ArgumentException>(() => runtime.UseEffectAssets(" "));
    }

    // ---------- ③ 托管与信号 ----------

    /// <summary>托管：随卡销毁自动撤销——行为停止＋凭据 Detach＝未命中（幂等无害）。</summary>
    [Fact]
    public async Task 托管_随卡销毁_行为停止_凭据卸载未命中()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(host)!;
        var json = PrefabJson.Serialize(CompileFromText("友方单位加入时，抽一张牌。", "effect.destroyed.host"));

        var attach = await runtime.AttachSnapshotAsync(host, json);
        Assert.True(attach.Success, attach.FailureReason);
        Assert.True(attach.Credential is not null);

        var before = playerA.Hand.Count;
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        Assert.Equal(before + 1, playerA.Hand.Count); // 行为生效

        await match.Engine.DestroyCard(host); // 销毁链：效果随卡自动撤销（托管）

        var afterDestroy = playerA.Hand.Count;
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        Assert.Equal(afterDestroy, playerA.Hand.Count); // 行为停止

        var detach = await runtime.DetachEffectAsync(host, attach.Credential!);
        Assert.Equal(EffectAttachStatus.NotFound, detach.Status); // 凭据已失效（未命中）
    }

    /// <summary>形态边界：主动快照可挂载/卸载（入列表成功——不误判为装载失败）；主动卸载同样恰一次 effect.removed。</summary>
    [Fact]
    public async Task 形态_主动快照_可挂载可卸载_不误判装载失败()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(host)!;

        var activeSnapshot = new EffectSnapshot(new EffectPrefab(
            "effect.active.probe",
            new TriggerPrefab(
                "t.active", "test.active.probe.main", TriggerKind.Active,
                hooks: null,
                events: Array.Empty<EventPrefab>())));

        var removed = new List<(object? Card, object? Effect)>();
        using var probe = CollectEffectRemoved(match, removed);

        var attach = await runtime.AttachSnapshotAsync(host, PrefabJson.Serialize(activeSnapshot));
        Assert.True(attach.Success, attach.FailureReason); // 成功判据＝实例化＋入列表成功（主动不装载）
        var activeEffect = Assert.Single(host.Effects);
        Assert.Equal(TriggerKind.Active, activeEffect.Kind);

        var detach = await runtime.DetachEffectAsync(host, attach.Credential!);
        Assert.True(detach.Success, detach.FailureReason);
        Assert.Empty(host.Effects);
        Assert.Single(removed);
        Assert.Same(activeEffect, removed[0].Effect);
    }

    /// <summary>终局门禁：终局后挂载＝服务不可用；卸载＝按失效结果（不执行、零副作用、不抛）。</summary>
    [Fact]
    public async Task 终局后_挂载拒斥_卸载按失效结果()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(host)!;
        var json = PrefabJson.Serialize(CompileFromText("友方单位加入时，抽一张牌。", "effect.terminal.probe"));

        var attach = await runtime.AttachSnapshotAsync(host, json);
        Assert.True(attach.Success, attach.FailureReason);

        var concede = match.Concede(playerA);
        Assert.True(concede.IsAccepted);
        Assert.Equal(MatchState.Ended, match.State);

        var denied = await runtime.AttachSnapshotAsync(host, json);
        Assert.Equal(EffectAttachStatus.ServiceUnavailable, denied.Status);

        var detach = await runtime.DetachEffectAsync(host, attach.Credential!);
        Assert.Equal(EffectAttachStatus.NotFound, detach.Status); // 按失效结果处理（不抛）
        Assert.Single(host.Effects); // 零副作用：效果未被执行卸载
    }

    // ---------- 辅助 ----------

    private static async Task<Match> CreateMatchAsync()
    {
        var match = CommandTestKit.CreateCommandMatch();
        await match.Initialize();
        return match;
    }

    private static DslEffectInstance ParseSingle(string text)
    {
        var parser = EffectParser.CreateDefault(out var lexFailures);
        Assert.Empty(lexFailures);
        var parse = parser.Parse(text);
        Assert.Empty(parse.Unresolved);
        return Assert.Single(parse.Effects);
    }

    private static EffectSnapshot CompileFromText(string text, string effectId)
    {
        var dsl = ParseSingle(text);
        var templates = EffectTemplateLoader.LoadDirectory(EffectTemplateLoader.DefaultDirectory);
        Assert.Empty(templates.Failures);
        var ops = OpTemplateCatalog.LoadDirectory(OpTemplateCatalog.DefaultDirectory, out var opFailures);
        Assert.Empty(opFailures);
        return new EffectCompiler(templates.Templates, ops).Compile(dsl, effectId);
    }

    private static string DslJsonOfText(string text) => DslJson.Serialize(ParseSingle(text));

    private static IDisposable CollectEffectRemoved(Match match, List<(object? Card, object? Effect)> removed)
        => match.Engine.Subscribe((type, payload, _) =>
        {
            if (type == Updates.EffectRemoved && payload is not null)
            {
                removed.Add((payload[PayloadKeys.Card], payload[PayloadKeys.Effect]));
            }

            return Task.CompletedTask;
        });
}
