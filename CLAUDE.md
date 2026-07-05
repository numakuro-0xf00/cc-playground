# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository Overview

This is a playground repository for Claude Code experimentation and testing.

## Skills

The `skills/` directory contains Claude Code skills that extend capabilities:

### skill-creator

A skill for creating new skills. Located at `skills/skill-creator/`.

**Scripts:**
- `scripts/init_skill.py <name> --path <dir>` - Initialize a new skill with template structure
- `scripts/quick_validate.py <skill-dir>` - Validate a skill's structure and frontmatter
- `scripts/package_skill.py <skill-dir> [output-dir]` - Package a skill into a distributable .skill file

**References:**
- `references/workflows.md` - Sequential and conditional workflow patterns
- `references/output-patterns.md` - Template and example patterns for consistent output

### spec-mining（仕様吸い出し・バグ洗い出しワークフロー）

C# / WinForms レガシーシステムの実装を「事実上の仕様」とみなし、仕様を吸い出して
形式検証（全列挙 / Z3 / TLA+）でバグを洗い出すワークフロー。
mizchi 氏のプレイブック（gist: db7817e6fc077d567c41cd9d41bb1c53）の C#/WinForms 翻案。

- `skills/spec-mining/` — オーケストレーター。台帳スキーマとゲートスクリプト
  （`scripts/validate_ledger.py`, `scripts/run_harness.py`, `scripts/setup_env.sh`）
- `skills/spec-extraction/` — PHASE 1: 宣言された仕様 / 暗黙の挙動の吸い出し
- `skills/formal-verification/` — PHASE 2–3: モデル化と反例探索
- `.claude/agents/spec-extractor.md` / `formal-verifier.md` / `counterexample-triager.md`
  — 各フェーズを担う subagent
- `.claude/workflows/spec-mining.js` — 複数 target の一括パイプライン
- `docs/spec-mining-workflow.md` — モデル非依存ランブック（Codex 等での実行手順・対象リポジトリへの導入方法）

## Development Approach

This is a playground environment for experimentation and prototyping.
