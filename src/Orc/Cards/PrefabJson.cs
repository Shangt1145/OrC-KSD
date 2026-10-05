using System.Text.Json;
using System.Text.Json.Serialization;
using Orc.Core;

namespace Orc.Cards;

/// <summary>
/// 效果快照 JSON 序列化器（S-C5/S-C10）：把 <see cref="EffectSnapshot"/>（嵌套三层预制体）导出/读回。
/// 形态：{ schemaVersion, root: { id, version, mainTrigger, otherTriggers[], modings[] } }；
/// 触发器＝{ id, stableKey, kind("active"/"passive"), viewType, hooks[], events[] }；
/// 事件＝{ id, entry, csx?, assemblyKey?, downstream[], version }；moding＝{ target, replacement }。
/// 版本（S-C10）：读出时 <c>schemaVersion</c> 与本版不符＝结构化失败（不静默降级）。
/// 明文约定：输入输出均为 UTF-8 文本（社区明文储存）。
/// </summary>
public static class PrefabJson
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>导出效果快照为 JSON 字符串。</summary>
    /// <exception cref="ArgumentNullException">snapshot 为 null。</exception>
    public static string Serialize(EffectSnapshot snapshot, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var dto = new SnapshotDto
        {
            SchemaVersion = snapshot.Version,
            Root = ToDto(snapshot.Root),
        };

        return JsonSerializer.Serialize(dto, indented ? IndentedOptions : WriteOptions);
    }

    /// <summary>读回效果快照（失败＝抛，fail-fast）。</summary>
    /// <exception cref="ArgumentNullException">json 为 null。</exception>
    /// <exception cref="FormatException">JSON 非法、schema 版本不符或结构缺项。</exception>
    public static EffectSnapshot Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        SnapshotDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<SnapshotDto>(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"效果快照 JSON 非法：{ex.Message}", ex);
        }

        if (dto?.Root is null)
        {
            throw new FormatException("效果快照缺少 root。");
        }

        if (dto.SchemaVersion != EffectSnapshot.SchemaVersion)
        {
            throw new FormatException(
                $"效果快照 schema 版本不符：期望 {EffectSnapshot.SchemaVersion}，实际 {dto.SchemaVersion}（不静默降级）。");
        }

        return new EffectSnapshot(FromDto(dto.Root), dto.SchemaVersion);
    }

    /// <summary>读回效果快照（结构化：失败不抛）。</summary>
    /// <exception cref="ArgumentNullException">json 为 null。</exception>
    public static bool TryDeserialize(string json, out EffectSnapshot? snapshot, out string? error)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            snapshot = Deserialize(json);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or ArgumentNullException or ArgumentOutOfRangeException)
        {
            snapshot = null;
            error = ex.Message;
            return false;
        }
    }

    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    // ---------- 领域 → DTO ----------

    private static EffectPrefabDto ToDto(EffectPrefab prefab) => new()
    {
        Id = prefab.Id,
        Version = prefab.Version,
        MainTrigger = ToDto(prefab.MainTrigger),
        OtherTriggers = prefab.OtherTriggers.Select(ToDto).ToList(),
        Modings = prefab.Modings.Select(m => new ModingDto
        {
            Target = m.TargetEventId,
            Replacement = ToDto(m.Replacement),
        }).ToList(),
    };

    private static TriggerPrefabDto ToDto(TriggerPrefab prefab) => new()
    {
        Id = prefab.Id,
        StableKey = prefab.StableKey,
        Kind = prefab.Kind == TriggerKind.Passive ? "passive" : "active",
        ViewType = prefab.ViewTypeName,
        Hooks = prefab.Hooks.ToList(),
        Events = prefab.Events.Select(ToDto).ToList(),
        Version = prefab.Version,
    };

    private static EventPrefabDto ToDto(EventPrefab prefab) => new()
    {
        Id = prefab.Id,
        Entry = prefab.EntryName,
        Csx = prefab.CsxSource,
        AssemblyKey = prefab.AssemblyKey,
        Downstream = prefab.Downstream.ToList(),
        Version = prefab.Version,
    };

    // ---------- DTO → 领域 ----------

    private static EffectPrefab FromDto(EffectPrefabDto dto)
    {
        if (dto.MainTrigger is null)
        {
            throw new FormatException("效果预制体缺少 mainTrigger。");
        }

        var others = (dto.OtherTriggers ?? new List<TriggerPrefabDto>()).Select(FromDto).ToList();
        var modings = (dto.Modings ?? new List<ModingDto>()).Select(m =>
        {
            if (m.Replacement is null)
            {
                throw new FormatException("moding 缺少 replacement。");
            }

            return new ModingPrefab(m.Target, FromDto(m.Replacement));
        }).ToList();

        return new EffectPrefab(dto.Id ?? string.Empty, FromDto(dto.MainTrigger), others, modings, dto.Version <= 0 ? 1 : dto.Version);
    }

    private static TriggerPrefab FromDto(TriggerPrefabDto dto)
    {
        if (dto.Events is null)
        {
            throw new FormatException($"触发器预制体 '{dto.Id}' 缺少 events。");
        }

        var kind = string.Equals(dto.Kind, "passive", StringComparison.OrdinalIgnoreCase)
            ? TriggerKind.Passive
            : TriggerKind.Active;

        return new TriggerPrefab(
            dto.Id ?? string.Empty,
            dto.StableKey ?? string.Empty,
            kind,
            dto.Hooks,
            dto.Events.Select(FromDto),
            dto.Version <= 0 ? 1 : dto.Version,
            string.IsNullOrWhiteSpace(dto.ViewType) ? "Orc.Cards.CardEventView" : dto.ViewType!);
    }

    private static EventPrefab FromDto(EventPrefabDto dto) => new(
        dto.Id ?? string.Empty,
        string.IsNullOrWhiteSpace(dto.Entry) ? "HandleAsync" : dto.Entry!,
        dto.Csx,
        dto.AssemblyKey,
        dto.Downstream,
        dto.Version <= 0 ? 1 : dto.Version);

    // ---------- DTO 定义（可空字段容忍缺项，结构校验在领域构造期完成） ----------

    private sealed class SnapshotDto
    {
        public int SchemaVersion { get; set; }
        public EffectPrefabDto? Root { get; set; }
    }

    private sealed class EffectPrefabDto
    {
        public string? Id { get; set; }
        public int Version { get; set; }
        public TriggerPrefabDto? MainTrigger { get; set; }
        public List<TriggerPrefabDto>? OtherTriggers { get; set; }
        public List<ModingDto>? Modings { get; set; }
    }

    private sealed class TriggerPrefabDto
    {
        public string? Id { get; set; }
        public string? StableKey { get; set; }
        public string? Kind { get; set; }
        public string? ViewType { get; set; }
        public List<string>? Hooks { get; set; }
        public List<EventPrefabDto>? Events { get; set; }
        public int Version { get; set; }
    }

    private sealed class EventPrefabDto
    {
        public string? Id { get; set; }
        public string? Entry { get; set; }
        public string? Csx { get; set; }
        public string? AssemblyKey { get; set; }
        public List<string>? Downstream { get; set; }
        public int Version { get; set; }
    }

    private sealed class ModingDto
    {
        public string Target { get; set; } = string.Empty;
        public EventPrefabDto? Replacement { get; set; }
    }
}
