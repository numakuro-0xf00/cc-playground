# spec-mining ワークフロー実行ガイド（モデル非依存ランブック）

C# / WinForms レガシーシステムの実装から仕様を吸い出し、形式検証（全列挙 / Z3 / TLA+）で
バグを洗い出すワークフローの**実行手順書**。Claude Code（Sonnet 5 等）と
Codex 5.5 などの他エージェントのどちらでも、**同じ手順・同じ成果物・同じゲート**で
実行できるように書かれている。

元ネタ: [実装コードから仕様を吸い出して Z3 / TLA+ でバグを払い出す — 実践プレイブック](https://gist.github.com/mizchi/db7817e6fc077d567c41cd9d41bb1c53)（mizchi 氏）

## 0. 対象リポジトリへの導入

この playground から対象のレガシーリポジトリへ以下をコピーする:

```
skills/spec-mining/          # オーケストレーター + 台帳スキーマ + ゲートスクリプト
skills/spec-extraction/      # PHASE 1: 吸い出し
skills/formal-verification/  # PHASE 2-3: 検証
.claude/agents/spec-extractor.md
.claude/agents/formal-verifier.md
.claude/agents/counterexample-triager.md
.claude/workflows/spec-mining.js   # (Claude Code のみ) 複数 target の一括実行
docs/spec-mining-workflow.md       # 本ファイル
```

環境準備（ローカルと CI で同一のコマンド）:

```bash
bash skills/spec-mining/scripts/setup_env.sh   # pyyaml / z3-solver / tla2tools を固定バージョンで
```

Codex 5.5 で使う場合は、対象リポジトリの `AGENTS.md` に以下を追記する:

```markdown
## spec-mining（仕様吸い出し・バグ洗い出し）
「仕様を吸い出す」「実装から仕様を復元」「形式検証でバグを洗い出す」系のタスクでは、
docs/spec-mining-workflow.md の手順に従うこと。各 PHASE で指定された SKILL.md を
**作業開始前に必ず読み**、GATE スクリプトが exit 0 になるまで次へ進まないこと。
```

## 1. 実行モデル

| 環境 | 実行のしかた |
|---|---|
| Claude Code | 各 PHASE を `.claude/agents/` の subagent に委譲（複数 target は並列可）。まとめて回すなら `.claude/workflows/spec-mining.js` |
| Codex 5.5 / その他 | 単独エージェントが PHASE 0→6 を順に実行。各 PHASE の冒頭で対応する SKILL.md を読む |

どちらの経路でも成果物とゲートは同一。**エージェントの記憶や要約に頼らず、
`spec-mining/<target-id>/` のファイル群だけを引き継ぎ媒体にする**こと
（フェーズ間・モデル間の再現性はこれで担保される）。

## 2. フェーズ実行手順

### PHASE 0: 対象選定
1. 読む: `skills/spec-mining/SKILL.md`（全体像）と
   `skills/spec-mining/references/winforms-target-selection.md`
2. `skills/formal-verification/references/bug-catalog-winforms.md` を見ながら
   Grep / roslyn-query で当たりをつける
3. 書く: `spec-mining/targets.md`（target-id / 入口 / 疑うパターン / 根拠 / 優先度）

### PHASE 1: 吸い出し（target ごと）
1. 読む: `skills/spec-extraction/SKILL.md` + `skills/spec-mining/references/ledger-format.md`
2. 実装を読み、宣言された仕様 / 暗黙の挙動を分けて claim 化（共通8問 + WinForms 6問）
3. 書く: `spec-mining/<target-id>/claims.yaml`
4. **GATE E**: `python skills/spec-mining/scripts/validate_ledger.py spec-mining/<id>/claims.yaml`

### PHASE 2–3: 形式化と検証（target ごと）
1. 読む: `skills/formal-verification/SKILL.md` + 該当レシピ（enumeration / z3 / tlaplus）
2. predict()/step() を C# から転記（全分岐にソース行アノテーション）、
   checks/ と broken/ を実装
3. 書く: `spec-mining/<target-id>/model/` と `counterexamples.yaml`（trace: pending）
4. **GATE V**: `python skills/spec-mining/scripts/run_harness.py spec-mining/<id>/model`

### PHASE 4: 突き合わせ（target ごと）
1. 読む: `skills/spec-mining/SKILL.md` の PHASE 4 節 + ledger-format.md
2. 各反例を実装に手トレース（可能なら最小再現ハーネス実行）→ 質問化 / バグ候補化 / 差し戻し
3. 書く: `counterexamples.yaml`（trace 完成）、`questions.md`、`ledger.yaml`
4. **GATE T**: `python skills/spec-mining/scripts/validate_ledger.py spec-mining/<id>/counterexamples.yaml spec-mining/<id>/ledger.yaml`

### PHASE 5: CI 固定化
- CI に `setup_env.sh` → `run_harness.py`（全 target 分）を載せる
- proved を contract-locked として運用開始（実装かモデルが変わったら赤くなる）

### PHASE 6: 終了判定と報告
- やめ時の基準は `skills/spec-mining/SKILL.md` PHASE 6 節
- 最終報告: バグ候補（witness + 再現 trace + 業務影響）/ 確認質問 / contract-locked /
  未検証とその理由 / 自己判断した点

## 3. 再現性を守る鉄則（全モデル共通）

1. **ゲートを飛ばさない** — 3つの GATE スクリプトの exit 0 が唯一の通過条件。
   「だいたいできた」で次へ進まない
2. **読んでいない行を根拠にしない** — evidence / ソースアノテーションの無い claim・分岐は捏造
3. **broken-variant の無い緑は成果ではない** — run_harness.py が機械的に強制する
4. **反例を消すためにモデルを直さない** — 直してよいのは実装との食い違いが trace で
   確認されたときだけ
5. **ファイルが引き継ぎ媒体** — フェーズをまたぐ情報はすべて `spec-mining/<id>/` の
   YAML/md に書く。会話コンテキストに置き去りにしない
6. **止まらない** — 非対話実行では、判断に迷ったら安全側（confidence: low / untested + 理由）
   に倒して進み、最終報告に列挙する
