using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 情报静态面的并行隔离 collection：本组用例围绕进程级静态面
/// <see cref="Orc.Game.Cards.IntelligenceRules.PendingRevealHandler"/> 展开——
/// 写者用例（<see cref="KeywordBatch2Tests"/>、<see cref="KeywordEffectBatch1Tests"/>）在验证窗口内把该静态替换为
/// 本地桩（「触发结构＋待办环节」验证所必需；try/finally 恢复），触发路径（「具有情报的卡被使用时」）按当前
/// 静态值调用；触发者用例（<see cref="EffectRuntimeEndToEndMoreTests"/> 的「事件卡词条守卫」用例会真实使用情报卡）
/// 在同进程内同样命中该静态面。若三类彼此并行：写者窗口内触发者的调用会被劫持进写者桩、污染其捕获列表
/// （写者之间亦互相劫持）＝偶发 flaky。为避免与其它测试类的触发/替换并行交错，本 collection 声明为不与
/// 其它 collection 并行执行（沿「KeywordEffectBatch4Serial」先例：进程级静态面在测试窗口内被临时改变 ⇒ 隔离带）。
/// </summary>
[CollectionDefinition("IntelligenceStaticSerial", DisableParallelization = true)]
public sealed class IntelligenceStaticSerialCollection
{
}
