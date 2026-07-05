---
name: spec-mining
description: >
  C# / WinForms レガシーシステムの実装コードを「事実上の仕様」とみなし、仕様を吸い出して
  形式検証（全列挙 / Z3 / TLA+）でバグを洗い出すワークフローのオーケストレーター。
  「仕様書がない」「実装から仕様を復元したい」「テストで踏めないバグを見つけたい」
  「レガシーの挙動を形式化したい」「反例を出してドメインと認識合わせしたい」
  などのリクエストでは必ずこのスキルを使用すること。最終成果物は
  「実装の主張 / 機械検査の結果 / ドメインへの確認質問」の台帳（ledger）である。
---

# Spec Mining — 実装から仕様を吸い出してバグを払い出す

mizchi 氏のプレイブック
（[実装コードから仕様を吸い出して Z3 / TLA+ でバグを払い出す](https://gist.github.com/mizchi/db7817e6fc077d567c41cd9d41bb1c53)）
を C# / WinForms レガシーシステムに適用するためのワークフロー。

## 基本姿勢

仕様書ではなく **実装が現に何をしているか** を仕様の源にする。やることは3段:

1. **吸い出し (extract)** — コードから「宣言された仕様」と「暗黙に決めている挙動」を分けて抜く
2. **形式化して反例探索 (refute)** — 主張をモデル化し、全入力/全順序で成り立つかを機械に攻撃させる
3. **突き合わせ (reconcile)** — 反例を「これは意図か?」とドメイン知識のある人にぶつける。
   意図なら**仕様として明文化**、意図でないなら**バグ**

反例は「バグ報告」であると同時に**認識合わせの会話の起点**になる。
最終成果物は証明の山ではなく、**台帳（claims / verdicts / questions）**である。

## スキル・エージェント構成

| レイヤー | ファイル | 役割 |
|---|---|---|
| 本スキル | `skills/spec-mining/SKILL.md` | フェーズ進行・ゲート・台帳管理（戦略レイヤー） |
| 吸い出し | `skills/spec-extraction/SKILL.md` | コードから claims.yaml を作る手順 |
| 検証 | `skills/formal-verification/SKILL.md` | モデル化・ツール選定・反例探索・ハーネス |
| agent | `.claude/agents/spec-extractor.md` | PHASE 1 を担当する subagent |
| agent | `.claude/agents/formal-verifier.md` | PHASE 2–3 を担当する subagent |
| agent | `.claude/agents/counterexample-triager.md` | PHASE 4 を担当する subagent |

Claude Code では各フェーズを対応する subagent に委譲してよい（対象が複数なら並列に）。
subagent が使えない環境（Codex 等）では、同じ SKILL.md を順に読んで単独で実行する。
手順・成果物・ゲートはどちらの経路でも**完全に同一**でなければならない。

コード調査には `roslyn-query` スキル（定義ジャンプ・参照検索・コール階層）が利用可能なら
Grep より優先して使う。

## 成果物ディレクトリ規約（対象リポジトリ側）

```
spec-mining/
  targets.md                  # PHASE 0: 対象領域の選定結果と根拠
  <target-id>/                # 対象領域ごと（例: order-entry, master-sync）
    claims.yaml               # PHASE 1: 実装の主張（宣言/暗黙を区別）
    model/
      predict.py              # PHASE 2: 純粋決定関数（C# 行番号アノテーション必須）
      checks/chk_*.py         # PHASE 3: 検査（EXPECTED 宣言付き）
      broken/brk_*.py         # PHASE 3: broken-variant（検査が効いている証明）
      *.tla / *.cfg           # TLA+ を使う場合
    counterexamples.yaml      # PHASE 3–4: 反例と witness
    questions.md              # PHASE 4: ドメインへの確認質問
    ledger.yaml               # 集約: claim × verdict × disposition
```

スキーマの詳細と記入例 → `references/ledger-format.md`

## ワークフロー全体像

```
PHASE 0  対象選定    バグカタログで「ありそう」な領域に当たりをつける
PHASE 1  吸い出し    宣言された仕様 / 暗黙の挙動を claims.yaml に抽出   [GATE E]
PHASE 2  形式化      predict() と invariant を書く。証明したいのか反例が欲しいのか決める
PHASE 3  検証        全列挙 / Z3 / TLA+ で回す。self-check + broken-variant  [GATE V]
PHASE 4  突き合わせ  反例→ドメイン質問、成立→契約ロック。台帳を完成させる  [GATE T]
PHASE 5  固定化      ハーネスを CI 回帰ガードに載せる
PHASE 6  終了判定    限界効用を見て止める。台帳を引き継ぐ
```

各 GATE は `scripts/` のスクリプトで**機械的に**判定する。GATE を通らずに次フェーズへ
進んではならない。非対話実行時も同様（ゲート失敗時は修正してリトライし、諦める場合は
その旨を台帳と最終報告に明記する）。

---

## PHASE 0: 対象選定

目的: 「形式化する価値のある領域」を選ぶ。全部はやらない。

1. `skills/formal-verification/references/bug-catalog-winforms.md` のカタログを読み、
   対象システムで「ありそう」なパターンを推定する
2. Grep / roslyn-query で候補領域の当たりをつける（検索クエリ例はカタログに併記）
3. 選定基準（優先度順）:
   - 事故ると業務影響が大きい判定ロジック（金額・数量・権限・締め処理）
   - 境界・エッジケースが多い（上限、期間、状態遷移）
   - 並行性がある（Timer / BackgroundWorker / 二重起動 / 排他）
   - クロス境界契約（DB スキーマ ↔ DataSet、ファイル連携、外部システム、単位・書式）
   - 「同じ判定」が複数箇所に重複実装されている（等価性検証の対象）
4. 対象ごとに `target-id`（kebab-case）を付け、`spec-mining/targets.md` に
   「領域 / 入口ファイル / 疑うカタログパターン / 選定根拠」を記録する

WinForms でどこを狙うかの詳細 → `references/winforms-target-selection.md`

**1 target の粒度**: 1つの決定関数（またはひとかたまりの状態遷移）に収まる大きさ。
「フォーム1枚まるごと」は大きすぎる。「この保存ボタンが通る条件」「この画面の
活性制御」「このカウンタの増減」程度に割る。

## PHASE 1: 吸い出し

`skills/spec-extraction/SKILL.md` の手順に従う（subagent 委譲時は spec-extractor に
target-id と入口ファイルを渡す）。成果物は `spec-mining/<target-id>/claims.yaml`。

**GATE E** — 以下をすべて満たすこと:

```bash
python skills/spec-mining/scripts/validate_ledger.py spec-mining/<target-id>/claims.yaml
```

- バリデータが exit 0
- 全 claim に evidence（path + lines）がある。**実装を読まずに記憶・要約から書いた claim は禁止**
- `kind: declared`（コメント・テスト名・ガード節・enum が宣言している仕様）と
  `kind: implicit`（default 値・エラー時分岐・順序依存など、どこにも書かれていない確定挙動）
  が区別されている
- 各機構に対して 8+6 の問い（spec-extraction 参照）への回答が claim または n/a として存在する

## PHASE 2–3: 形式化と検証

`skills/formal-verification/SKILL.md` の手順に従う（subagent 委譲時は formal-verifier に
claims.yaml のパスを渡す）。

ツール判定の要点（詳細は formal-verification 側）:

- 状態空間が**有限で決定的** → 素の全列挙が一番安い。まずこれを問う
- 「どんな入力でも成り立つ?」区間+集合+等式の述語 → **Z3**
- 「どの順番で起きても / 同時に来たら / クラッシュ後は?」 → **TLA+**（または小さければ interleaving 全列挙）

**GATE V** — 以下をすべて満たすこと:

```bash
python skills/spec-mining/scripts/run_harness.py spec-mining/<target-id>/model
```

- ランナーが exit 0（全 check が EXPECTED 通り、全 broken-variant が赤）
- `EXPECTED: HOLDS` の check には対応する broken-variant が最低1つある。
  **broken-variant の無い緑は「何も検証していない緑」であり成果と認めない**
- `predict.py` の全分岐に C# の `# File.cs:行` アノテーションがある（転記ミス対策）

## PHASE 4: 突き合わせ

`.claude/agents/counterexample-triager.md`（または本人）が実施:

1. 各反例について、witness の値を**実際の C# コードに手でトレース**し、
   モデルではなく実装で本当にその挙動になることを file:line ステップ付きで
   `counterexamples.yaml` の `trace` に記録する（model↔code ギャップ対策）。
   ソリューションがビルド可能なら、witness を入力にした最小再現ハーネスの実行が最強
2. 実装で再現しない反例は「モデルの虚偽」— predict.py を直して PHASE 3 へ戻る
3. 再現する反例は `questions.md` に「これは意図ですか?」形式の質問として起票する
   （witness・業務影響・意図だった場合/バグだった場合の帰結を併記）
4. 成立した invariant は `disposition: contract-locked` として台帳に記録
5. `ledger.yaml` を完成させる

**GATE T**:

```bash
python skills/spec-mining/scripts/validate_ledger.py spec-mining/<target-id>/ledger.yaml
```

- 全 claim に verdict がある（`untested` には理由必須）
- 全反例に trace（実装での再現確認）と disposition がある
- `counterexample` の verdict はすべて questions.md の質問 ID か bug-candidate に紐づく

## PHASE 5: CI 固定化

- `run_harness.py` を CI ジョブに載せる（人間可読な出力 + 機械可読な exit code を両方出す）
- 依存を宣言的に固める: `scripts/setup_env.sh` を使い、z3-solver / TLC のバージョンを固定。
  ローカルと CI が**同一のハーネス**を叩くこと（実行経路を1つにする）
- contract-locked された invariant は、将来実装かモデルのどちらかが変わったら赤くなる
  回帰ガードとして機能する

## PHASE 6: 終了判定（やめ時）

**向かないもの・やめ時**（見つけたら形式化せず台帳に `verify_with: skip` + 理由で記録）:

- 確率的性質（期待値・分布）
- 既に単体テストで brute-force 済みのアルゴリズム再検証
- 大物を出し切った後の確認的なだけの命題の量産（CI を重くするだけ）
- 主要な不変条件・危険な穴・クロス境界契約を押さえたら、
  **新規形式化より、溜まった questions.md をドメインと捌く方がレバレッジが高い**

**anti-pattern（禁止事項）**:

- 直列依存を無理に並行モデル化する
- 実装を読まずに要約・記憶からモデルを組む（転記を1つ取り違えると偽の証明になる）
- broken-variant を用意せず「緑だから OK」とする
- ネットワーク/手動セットアップに依存した検証

## 最終報告の形式

ワークフロー終了時（または中断時）は必ず以下を報告する:

1. 対象領域と台帳ファイルのパス一覧
2. **バグ候補**（実装で再現確認済みの反例）: witness と業務影響つき
3. **確認質問**: questions.md のサマリ（ドメインに渡せる形）
4. **契約化された不変条件**: contract-locked の一覧
5. 検証しなかったもの・諦めたものとその理由
6. 判断に迷った点（非対話実行時に自己判断した箇所）
