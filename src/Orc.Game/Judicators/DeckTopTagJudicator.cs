using Orc.Core;
using Orc.Game.Cards;
using Orc.Game.Players;

namespace Orc.Game.Judicators;

/// <summary>
/// 卡组顶特点判定器（J3 示范①；判定器机制两示范之一）：「卡组顶是否具有指定特点」的可改写判定服务。
/// 签名（强类型面）＝（玩家，特点标识）→ bool；「特点」＝<see cref="TagData"/> 开放 tag。
/// 默认求值：读取该玩家卡组顶（条目索引 0）的加载实例 → 其 TagData 开放 tag 判定（只读、不移除条目）。
/// 边界语义（三态）：
/// ① 无条目（空卡组）＝不满足（false 降级、不抛错）；
/// ② 条目在但实例未挂（未加载态）＝明确错误（不归 false 降级——「不可知≠false」）；
/// ③ 条目与实例齐＝读 TagData 求值。
/// 输入契约：玩家参数为 null＝fail-fast（参数契约错误——动作主体缺失）；
/// 特点标识 null/空白＝false、不抛错（沿用底层 <see cref="TagData.ContainsTag"/> 存在性查询口径）；匹配＝ordinal 逐字相等。
/// 改写＝moding（恒真/恒假注入——全局生效）；注销回退。无状态：不持有对局状态，全部经参数（玩家对象）只读读取。
/// 注册途径＝既有装配期注册面（外部装配段/测试装配注册）；名称＝<see cref="JudicatorNames.DeckTopTag"/>（冻结契约）。
/// </summary>
public sealed class DeckTopTagJudicator : Judicator<DeckTopTagJudicator.DeckTopTagLogic>
{
    /// <summary>
    /// 强类型 delegate（该判定器签名）：玩家＋特点标识 → 是否满足「卡组顶具有该特点」。
    /// </summary>
    /// <param name="player">玩家（其卡组顶为读取对象；不得为 null——参数契约错误，fail-fast 族）。</param>
    /// <param name="tag">特点标识（TagData 开放 tag；null/空白＝false——存在性查询口径）。</param>
    /// <returns>卡组顶是否具有该特点。</returns>
    public delegate bool DeckTopTagLogic(Player player, string? tag);

    /// <inheritdoc />
    protected override Func<object[]?, object[]?> Adapt(DeckTopTagLogic handler)
        => args => new object[] { handler(Unpack<Player>(args, 0), Unpack<string>(args, 1)) };

    /// <inheritdoc />
    public override object[]? Invoke(object[]? args) => Adapt(EvaluateLogic)(args);

    /// <summary>
    /// 默认求值逻辑（moding 生效时被栈顶替换逻辑整体取代——纯替换，本逻辑不再执行）。
    /// </summary>
    /// <exception cref="ArgumentNullException">player 为 null（参数契约错误——fail-fast）。</exception>
    /// <exception cref="InvalidOperationException">顶条目未装配加载实例（未加载态＝明确错误，不归 false 降级）。</exception>
    private static bool EvaluateLogic(Player player, string? tag)
    {
        ArgumentNullException.ThrowIfNull(player);

        var deck = player.Deck;
        if (deck.Count == 0)
        {
            return false; // 空卡组＝不满足（false 降级、不抛错）
        }

        var top = deck.PeekInstance(0); // 只读读取卡组顶加载实例；未装配实例＝明确错误（读面契约）
        // tag 可为 null/空白（存在性查询口径——null/空白＝false、不抛错；ContainsTag 内部即以此处置）
        return top.GetData<TagData>().ContainsTag(tag!);
    }
}
