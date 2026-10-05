// ─────────────────────────────────────────────────────────────────────────────
// 【已退役】反击豁免判定表（后置项 A；K2 收编退役）——本文件为弃用壳（纯注释、无功能成员）。
//
// 迁移去向：combat.counter.eligibility 判定器（src/Orc.Game/Judicators/CombatCounterEligibilityJudicator.cs）
//           语义唯一真源：四条款、豁免优先（目标轰炸机永不反击／攻击者炮兵不受任何反击／
//           攻击者轰炸机不受反击〔例外＝目标战斗机〕／其余正常）；类型判定经 CombatTypeGroups 共表
//           （轰炸机/战斗机复用既有判定、炮兵加性补充）。
// 迁移来源：K2（A 档 C5）收编——原 CounterAttackRules.CanCounterAttack 逐字迁移至判定器；
//           私有 HasUnitType 一并移除（收敛至 CombatTypeGroups 共表——消除散落实现）。
// 退役状态：零引用、无第二真源（全局引用核查无旁路结论——见 K2 实现记录）；
//           调用点＝默认互伤区（CommandManager.HandleDefaultAttackDamageAsync）与伏击资格（AmbushKeywordComponent）
//           均经判定器条目通道（对局＝注册表条目句柄；独立构造＝内置默认——不直调、无旁路）。
// 保留原因：文件不删除（当前阶段硬约束）；不引入 [Obsolete]（避免编译/使用处警告——本项目以 0 警告为验收标准）。
// ─────────────────────────────────────────────────────────────────────────────
