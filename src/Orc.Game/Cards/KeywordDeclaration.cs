namespace Orc.Game.Cards;

/// <summary>
/// 词条声明（2C-A1 加性；单一声明类型）：标识＋可选参值（如 ("重甲", 2)）。
/// 「仅标识」＝参值位空形态（<see cref="Value"/> 为 null）——同一类型、同一解析归一（非第二套形态；
/// 旧纯字符串形态作为独立形态取消、经本类型统一承载）。
/// 定义期校验（<see cref="CardDefinition"/> 承载）：空白标识 / 未注册标识 / 同标识重复（不论参值）＝fail-fast 拒绝；
/// 合法标识集来源＝词条注册面（<see cref="KeywordRegistry"/>）内容（单源）。
/// </summary>
public readonly record struct KeywordDeclaration
{
    /// <summary>创建词条声明（存值形态；合法性校验归定义期统一执行）。</summary>
    /// <param name="id">词条标识（「仅标识」形态＝仅提供此项）。</param>
    /// <param name="value">可选参值（缺省＝null＝参值位空）。</param>
    public KeywordDeclaration(string id, int? value = null)
    {
        Id = id;
        Value = value;
    }

    /// <summary>词条标识。</summary>
    public string Id { get; }

    /// <summary>可选参值（null＝未提供——「仅标识」形态；数据面以（标识, 参值?）承载）。</summary>
    public int? Value { get; }
}
