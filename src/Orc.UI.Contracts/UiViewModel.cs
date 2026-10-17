namespace Orc.UI.Contracts;

// ─────────────────────────────────────────────────────────────────────────────
// 中立 UI 契约（前端资产迁移 S1）：从 `Kards.Ui.Contracts` 上移的 **view 三件** ＋ 依赖枚举。
// 形状**逐字保持不变**（前端 `proto/ui/**` 依赖字段名与语义）；仅命名空间改为 `Orc.UI.Contracts`。
// 上移理由：投影由引擎侧插件（`Orc.UI`）构造，故 DTO 须对后端可见；中立程序集避免反向依赖。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>卡面可见性等级（前端据其选择卡背/剪影/全貌呈现）。</summary>
public enum Visibility
{
    Full, Silhouette, Hidden
}

/// <summary>对局结果（封闭值域；引擎当前无平局路径，None＝未终局）。</summary>
public enum UiMatchOutcome
{
    None, Win, Lose
}

/// <summary>认输方（仅终局原因为认输时非 None）。</summary>
public enum UiConcedingSide
{
    None, Self, Enemy
}

/// <summary>DTO 类别判别（序列化/缓存用）。</summary>
public enum DtoKind
{
    CardDefinition, CardInstance
}

/// <summary>带类别判别标记的 DTO。</summary>
public interface IDtoKind
{
    DtoKind Kind
    {
        get;
    }
}

/// <summary>Definition-only catalogue data. Null stats mean unknown/not applicable, never zero.</summary>
public sealed record UiCardDefinition : IDtoKind
{
    public DtoKind Kind => DtoKind.CardDefinition;
    public required string CardId
    {
        get; init;
    }
    public string Name { get; init; } = "";
    public string NameEn { get; init; } = "";
    public string CardType { get; init; } = "unknown";
    public string UnitType { get; init; } = "";
    public string Set { get; init; } = "";
    public string Rarity { get; init; } = "common";
    public string ArtPath { get; init; } = "";
    public string Text { get; init; } = "";
    public string Flavor { get; init; } = "";
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, int> KeywordValues { get; init; } = new Dictionary<string, int>();
    public int? Cost
    {
        get; init;
    }
    public int? BaseAttack
    {
        get; init;
    }
    public int? BaseDefense
    {
        get; init;
    }
    public int? BaseOpCost
    {
        get; init;
    }
    public bool IsToken
    {
        get; init;
    }
    public bool HasVariableStats
    {
        get; init;
    }
}

/// <summary>Engine projection only. All Effective* values include modifiers and auras.</summary>
public sealed record UiCardView : IDtoKind
{
    public DtoKind Kind => DtoKind.CardInstance;
    public required string Uid
    {
        get; init;
    }
    public required UiCardDefinition Definition
    {
        get; init;
    }
    public string CardId => Definition.CardId;
    public int? EffectiveCost
    {
        get; init;
    }
    public int? EffectiveAttack
    {
        get; init;
    }
    public int? EffectiveDefense
    {
        get; init;
    }
    public int? EffectiveOpCost
    {
        get; init;
    }
    public int? Health
    {
        get; init;
    }
    public string Zone { get; init; } = "hand";
    public int SlotIndex { get; init; } = -1;
    public string OwnerSide { get; init; } = "self";
    public Visibility Visibility { get; init; } = Visibility.Full;
    public bool IsHq
    {
        get; init;
    }
    public bool IsVeteran
    {
        get; init;
    }
    /// <summary>Engine covert marker: effect protection, not concealed card identity.</summary>
    public bool IsCovert { get; init; }
    public IReadOnlyList<string> UnitTypes { get; init; } = Array.Empty<string>();
    public bool CanPlayCard
    {
        get; init;
    }
    public bool CanAttack
    {
        get; init;
    }
    public bool CanMoveAndAttack
    {
        get; init;
    }
    public bool CanBeTargeted
    {
        get; init;
    }
    public IReadOnlyList<string> BlockedReasons { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, int> KeywordValues { get; init; } = new Dictionary<string, int>();
    public bool IsSuppressed
    {
        get; init;
    }
    public bool IsSilenced
    {
        get; init;
    }
    public bool IsCounterArmed { get; init; }
    public bool IsToken
    {
        get; init;
    }
}

/// <summary>整盘只读投影（单视角）：由投影提供者每帧产出，前端只消费。</summary>
public sealed record UiMatchView
{
    public required string MatchId
    {
        get; init;
    }
    public int Turn
    {
        get; init;
    }
    public string SelfPlayerName { get; init; } = "我方";
    public string EnemyPlayerName { get; init; } = "对手";
    public string Phase { get; init; } = "mulligan";
    public string? ResultTitle { get; init; }
    /// <summary>Why the match ended ("hq-zero" / "concede"), straight from the engine. Null while in play.</summary>
    public string? ResultReason { get; init; }
    /// <summary>对局结果（结构化真值）：UI 据此选表现，不得用 ResultTitle 文本反推。</summary>
    public UiMatchOutcome Outcome { get; init; }
    /// <summary>认输方：仅终局原因为认输时非 None；其余终局＝None。</summary>
    public UiConcedingSide ConcedingSide { get; init; }
    /// <summary>How many turns the match ran; null when the match is still live.</summary>
    public int? FinalTurn { get; init; }
    public string ActivePlayerSide { get; init; } = "self";
    public int SelfKredits
    {
        get; init;
    }
    public int SelfMaxKredits
    {
        get; init;
    }
    public int? EnemyKredits
    {
        get; init;
    }
    public int? EnemyMaxKredits
    {
        get; init;
    }
    public int SelfHandCount
    {
        get; init;
    }
    public int SelfDeckCount
    {
        get; init;
    }
    public int? EnemyHandCount { get; init; }
    public int? EnemyDeckCount { get; init; }
    public int SelfCounterCount
    {
        get; init;
    }
    public IReadOnlyList<UiCardView> SelfHand { get; init; } = Array.Empty<UiCardView>();
    public IReadOnlyList<UiCardView> SelfLine { get; init; } = Array.Empty<UiCardView>();

    /// <summary>
    /// How many slots the front and support lines actually have, straight from the engine. The board
    /// draws a card only for an occupied slot, so a drop has to be resolved against the line's real
    /// width — counting rendered cards gives the wrong index and collapses to zero on an empty line.
    /// </summary>
    public int FrontLineSlotCount { get; init; }
    public int SupportLineSlotCount { get; init; }

    public UiCardView? SelfHq
    {
        get; init;
    }
    public IReadOnlyList<UiCardView> EnemyLine { get; init; } = Array.Empty<UiCardView>();
    public UiCardView? EnemyHq
    {
        get; init;
    }
    public UiCardView? SelectedTarget
    {
        get; init;
    }
    public IReadOnlyList<UiCardView> PendingChoices { get; init; } = Array.Empty<UiCardView>();
    /// <summary>Prompt text supplied with pending choices. The responder still decides what the selection means.</summary>
    public string? PendingChoiceCaption { get; init; }
}
