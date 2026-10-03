namespace Orc.Game.Targeting;

/// <summary>
/// Targeter（指示器／请求对象）：承载一次目标选择的参数（筛选器、槽位〔可缺省——缺省＝单一选择〕）；
/// 唯一发起入口＝<see cref="Targeting"/>（异步：内部自动经 <see cref="TargeterManager"/> 排队与桥接等待、产出引用）。
/// 复用：允许串行多次使用（每次独立执行、独立结果；并发/未完成时再发＝排队〔不拒绝〕，结局与产出互不干扰）。
/// 请求对象本身不持执行期可变状态（请求状态随每次执行独立创建），复用无需内部状态重置。
/// 槽位声明随请求对象、构造期固定（无中途修改语义；传参与复用不改变声明）。
/// </summary>
public sealed class Targeter
{
    private readonly TargeterManager _manager;

    /// <summary>创建请求对象（请求对象本身不规定获取路径——管理器读取/闭包捕获等均可；范式用例演示可行路径）。</summary>
    /// <param name="manager">所属目标选择管理器（其队列与桥接承载本次 targeting 的执行）。</param>
    /// <param name="filter">筛选器（两级均可缺省；可只给其一；null＝两级全通过）。</param>
    /// <param name="slots">槽位声明（可缺省——缺省＝单一选择语义；声明构造期固定）。</param>
    /// <exception cref="ArgumentNullException">manager 为 null。</exception>
    /// <exception cref="ArgumentException">槽位声明含 null 元素；槽位名重复（同名归并＝非法声明，构造期拒绝）。</exception>
    public Targeter(TargeterManager manager, TargetFilter? filter = null, IEnumerable<TargetSlot>? slots = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
        Filter = filter;

        var list = new List<TargetSlot>();
        if (slots is not null)
        {
            foreach (var slot in slots)
            {
                if (slot is null)
                {
                    throw new ArgumentException("槽位声明含 null 元素。", nameof(slots));
                }

                list.Add(slot);
            }
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in list)
        {
            if (!names.Add(slot.Name))
            {
                throw new ArgumentException(
                    $"槽位名 '{slot.Name}' 重复（名字互不重复为声明合法性要求；省略/空白名归缺省槽位）。", nameof(slots));
            }
        }

        Slots = list.ToArray();
    }

    /// <summary>筛选器（两级均可缺省；构造期固定、无中途修改语义）。</summary>
    public TargetFilter? Filter { get; }

    /// <summary>槽位声明（声明序只读快照；缺省＝空＝单一选择语义）。</summary>
    public IReadOnlyList<TargetSlot> Slots { get; }

    /// <summary>
    /// 执行（异步）：经所属管理器排队 → 出队执行（候选收集 → 规范化 → 粗筛 → 细筛 → 交互等待）→ 产出统一结果对象。
    /// 结局三态（成功/取消/失败）均经结果对象表达、不抛异常；失败/取消不使队列崩溃（队列继续出队下一条）。
    /// </summary>
    public Task<TargetingResult> Targeting() => _manager.Enqueue(this);

    /// <summary>所属管理器（框架内部读面）。</summary>
    internal TargeterManager Manager => _manager;
}
