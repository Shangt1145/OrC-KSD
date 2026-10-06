using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orc.Game.EffectParsing.Dsl;

/// <summary>
/// 效果 DSL 的 JSON 读写（转换段 S1）：明文 UTF-8、camelCase；
/// 反序列化 fail-fast（缺 template／空 op 序列／未登记原语＝抛 <see cref="FormatException"/>）。
/// </summary>
public static class DslJson
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions WriteIndentedOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>序列化 DSL 实例。</summary>
    /// <exception cref="ArgumentNullException">instance 为 null。</exception>
    public static string Serialize(DslEffectInstance instance, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return JsonSerializer.Serialize(ToDto(instance), indented ? WriteIndentedOptions : WriteOptions);
    }

    private static InstanceDto ToDto(DslEffectInstance instance) => new()
    {
        Template = instance.Template,
        Fills = instance.Fills.ToDictionary(
            pair => pair.Key,
            pair => new SlotFillDto { Ops = pair.Value.Ops.Select(ToDto).ToList() },
            StringComparer.Ordinal),
    };

    /// <summary>反序列化 DSL 实例（fail-fast）。</summary>
    /// <exception cref="ArgumentNullException">json 为 null。</exception>
    /// <exception cref="FormatException">JSON 非法或结构/原语不合规。</exception>
    public static DslEffectInstance Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        InstanceDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<InstanceDto>(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"效果 DSL 的 JSON 非法：{ex.Message}", ex);
        }

        if (dto is null)
        {
            throw new FormatException("效果 DSL 为空。");
        }

        return FromDto(dto);
    }

    private static DslEffectInstance FromDto(InstanceDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Template))
        {
            throw new FormatException("效果 DSL 缺少 template。");
        }

        var fills = new Dictionary<string, DslSlotFill>(StringComparer.Ordinal);
        foreach (var pair in dto.Fills ?? new Dictionary<string, SlotFillDto>())
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                throw new FormatException("效果 DSL 含空白槽位名。");
            }

            var ops = (pair.Value?.Ops ?? new List<OpDto>()).Select(FromDto).ToList();
            fills[pair.Key] = new DslSlotFill(ops);
        }

        return new DslEffectInstance(dto.Template, fills);
    }

    /// <summary>反序列化 DSL 实例（结构化：失败不抛）。</summary>
    /// <exception cref="ArgumentNullException">json 为 null。</exception>
    public static bool TryDeserialize(string json, out DslEffectInstance? instance, out string? error)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            instance = Deserialize(json);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or ArgumentNullException or ArgumentOutOfRangeException)
        {
            instance = null;
            error = ex.Message;
            return false;
        }
    }

    private static OpDto ToDto(DslOp op) => new()
    {
        Op = op.Op,
        Target = op.Target is null ? null : new SelectorDto
        {
            Sel = op.Target.Sel,
            Side = op.Target.Side,
            Zone = op.Target.Zone,
            Filter = ToDto(op.Target.Filter),
            Count = op.Target.Count,
        },
        Filter = ToDto(op.Filter),
        Amount = op.Amount,
        Count = op.Count,
        Attack = op.Attack,
        Defense = op.Defense,
        Keyword = op.Keyword,
        Zone = op.Zone,
        Script = op.Script,
        Condition = op.Condition is null ? null : new ConditionDto { Kind = op.Condition.Kind, Raw = op.Condition.Raw },
        Name = op.Name,
        Nested = op.Nested?.Select(ToDto).ToList(),
    };

    private static FilterDto? ToDto(DslFilter? filter) =>
        filter is null ? null : new FilterDto { UnitType = filter.UnitType, Keyword = filter.Keyword };

    private static DslOp FromDto(OpDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Op))
        {
            throw new FormatException("效果 DSL 含没有名称的 op。");
        }

        try
        {
            var op = new DslOp(
                dto.Op,
                dto.Target is null
                    ? null
                    : new DslSelector(dto.Target.Sel ?? string.Empty, dto.Target.Side, dto.Target.Zone, FromDto(dto.Target.Filter), dto.Target.Count),
                FromDto(dto.Filter),
                dto.Amount,
                dto.Count,
                dto.Attack,
                dto.Defense,
                dto.Keyword,
                dto.Zone,
                dto.Script,
                dto.Condition is null ? null : new DslCondition(dto.Condition.Kind ?? string.Empty, dto.Condition.Raw),
                dto.Name,
                dto.Nested?.Select(FromDto).ToList());

            var errors = DslOpRegistry.Validate(op);
            if (errors.Count > 0)
            {
                throw new FormatException($"效果 DSL 的 op 非法：{string.Join(" ", errors)}");
            }

            return op;
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"效果 DSL 的 op 非法：{ex.Message}", ex);
        }
    }

    private static DslFilter? FromDto(FilterDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        try
        {
            return new DslFilter(dto.UnitType, dto.Keyword);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"效果 DSL 的 filter 非法：{ex.Message}", ex);
        }
    }

    private sealed class InstanceDto
    {
        public string? Template { get; set; }

        public Dictionary<string, SlotFillDto>? Fills { get; set; }
    }

    private sealed class SlotFillDto
    {
        public List<OpDto>? Ops { get; set; }
    }

    private sealed class OpDto
    {
        public string? Op { get; set; }

        public SelectorDto? Target { get; set; }

        public FilterDto? Filter { get; set; }

        public int? Amount { get; set; }

        public int? Count { get; set; }

        public int? Attack { get; set; }

        public int? Defense { get; set; }

        public string? Keyword { get; set; }

        public string? Zone { get; set; }

        public string? Script { get; set; }

        public ConditionDto? Condition { get; set; }

        public string? Name { get; set; }

        /// <summary>内嵌效果（递归 DSL）。</summary>
        public List<InstanceDto>? Nested { get; set; }
    }

    private sealed class ConditionDto
    {
        public string? Kind { get; set; }

        public string? Raw { get; set; }
    }

    private sealed class SelectorDto
    {
        public string? Sel { get; set; }

        public string? Side { get; set; }

        public string? Zone { get; set; }

        public FilterDto? Filter { get; set; }

        public int? Count { get; set; }
    }

    private sealed class FilterDto
    {
        public string? UnitType { get; set; }

        public string? Keyword { get; set; }
    }
}
