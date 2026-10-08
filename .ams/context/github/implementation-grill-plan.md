# Implementation Grill Plan — 交接文档更新与 GitHub 托管业务永久交接

> 本 grill 聚焦：`.gitignore` 规则形态、文档改动顺序与可逆点、验证证据、提交拆分。

## Unresolved Questions

### Q1: 过程物规则形态 Completed
- 结论（2026-10-09，用户选 B）：`.ams-docs` 用**默认拒绝 + 仅放行 `*.md`**（`/.ams-docs/**` ＋ `!/.ams-docs/**/` ＋ `!/.ams-docs/**/*.md`），辅以目录名纵深规则。理由：6 天内已见 8 种目录名变体＋松散脚本，按名字列举无法根治。
- 已验证：`check-ignore` 对 md 返回"不被忽略"、对 `_probe/`·`_tmp/`·松散 `.py` 返回忽略；未跟踪项 182 → 85，且 `.ams-docs` 剩余项**全部为 `.md`**。

### Q2: 是否需 `git rm --cached` Completed
- 结论：**不需要**——实测已跟踪文件中无过程物，故加规则即可闭合（`.gitignore` 对已跟踪文件无效，若将来发现需 `git rm -r --cached`，已写入文档）。

### Q3: 文档改动顺序与可逆点 Completed
- 结论：先落 `.gitignore`（可逆：撤销一次 `chore:` 提交）→ 再改文档 → 再验证 → 再提交。文档改动纯文本，任何一步失败都可 `git reset --mixed HEAD~1`（未推送时）。

### Q4: 验证证据 Completed
- 结论：① `check-ignore` 四点抽查（md 放行 / `_probe` / `_tmp` / 松散脚本）；② 未跟踪项计数与 `.ams-docs` 非 md 归零；③ 推送后 `ls-remote` = `HEAD`。

### Q5: 提交拆分 Completed
- 结论：`chore:`（`.gitignore`）＋ `docs:`（`交接说明.md` ＋ 本目录 4 份文档）。

### Q6: 不做的事 Completed
- 结论：不改 `OrcEngine.slnx`；不改 `docs/` 正文；不删除 D1 扣下项；不改写已推送历史；不 force push。
