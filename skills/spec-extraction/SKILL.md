---
name: spec-extraction
description: >
  C# / WinForms レガシーコードから仕様を吸い出すスキル。実装を読み、「宣言された仕様」と
  「暗黙に決めている挙動」を分けて claims.yaml（実装の主張の台帳）に抽出する。
  spec-mining ワークフローの PHASE 1 を担う。「このコードの仕様を抽出したい」
  「実装が何を保証しているか知りたい」「暗黙の挙動を洗い出したい」際に使用する。
  単独でも使えるが、通常は skills/spec-mining/SKILL.md から呼ばれる。
---

# Spec Extraction — 宣言された仕様と暗黙の挙動を吸い出す

実装を読むとき、**「宣言された仕様」と「暗黙の挙動」を必ず分けて**記録する。
成果物は `spec-mining/<target-id>/claims.yaml`
（スキーマ → `skills/spec-mining/references/ledger-format.md`）。

**鉄則: 実装を読まずに記憶・要約から claim を書かない。** 全 claim に evidence
（path + lines）が必須なのはこのため。読んでいない行を根拠にした claim は捏造である。

## 手順

```
1. 入口から決定ロジックの全体を追う（イベントハンドラ→サービス→DB 層）
2. 「宣言された仕様」を拾う（下記の在り処リスト）→ kind: declared
3. 各機構に問いを機械的に投げる（8つの共通の問い + WinForms の6つの問い）
   → 答えがコードで確定しているのに文書化されていないもの = kind: implicit
4. claims.yaml に書き、validate_ledger.py を通す
```

### 1. 決定ロジックを追う

- 入口はイベントハンドラ（`btnOK_Click` 等）。そこから呼ばれる先を
  roslyn-query（コール階層・定義ジャンプ）か Grep で末端まで追う
- **同じ判定に関与する全ハンドラを列挙する**こと。WinForms では1つの検証が
  `Validating` / `TextChanged` / `Click` / `FormClosing` に分散しているのが常態。
  1つのハンドラだけ読んで「仕様」と結論しない
- Designer ファイル（`*.Designer.cs`）のイベント購読 (`+=`) と初期値
  （`Enabled`, `Visible`, `MaxLength`, `CausesValidation`）も読む。初期状態は仕様の一部

### 2. 宣言された仕様の在り処（→ kind: declared）

- **コメント**、特に「なぜ」を語るもの。`// 上限を超えないかチェック` は仕様の宣言。
  「元のコードがそうなっていた」「理由不明」系は**化石(fossil)** — declared だが
  `confidence: low` にして要確認マークにする
- **テスト名・アサーション**（あれば）: 境界仕様そのもの
- **ガード節・throw・MessageBox でのエラー表示**: 前提条件と fail-mode の宣言
- **enum 定義・定数・DB スキーマ制約**（NOT NULL, CHECK, 桁数）: 取りうる値と意味の対応
- **MaxLength や Validating などの UI 制約**: クライアント側だけの検証は
  `category: trust` の claim も同時に立てる（DB 直叩き・別画面から破れるか?）

### 3. 問いを機械的に投げる（→ kind: implicit）

各述語・各機構に対して以下を**すべて**問う。答えが自明でも記録する
（自明さは属人的で、そこにバグが眠る）。該当しない問いは claims.yaml の
`questions_applied` に含めなければよい。

**共通の8つの問い（プレイブック §3）:**

1. 空/未設定のとき何が起きる?
2. エラー/取得失敗のとき true か false か例外か? (fail-open/close)
3. 否定 (NOT/除外/inverse) をかけると、上の答えはどう化ける?
4. 境界はどっち向き? (`>` か `>=` か、端点を含むか)
5. 同時に2つ来たら? 片方の結果がもう片方に間に合うか?
6. この値は誰が決める? 信頼できない相手が差し替えられるか?
7. このループ/引き上げ/リトライは何で止まる? 止めているのは本質的条件か、たまたまか?
8. 2つのシステムがこのデータで合意しているか? (レイアウト・enum・単位・書式)

**WinForms の6つの問い（9〜14）:**

9. このハンドラは**再入**し得るか? (`DoEvents` / モーダル / Timer / 二重クリック)
10. このフォームフィールド/コントロールは**誰がいつ**設定するか?
    Load 前アクセス・Dispose 後アクセス・null のままの経路はあるか?
11. **イベント発火順序**に依存しているか? その順序は全操作経路で保証されるか?
    （×ボタンで閉じると Validating は走らない、等）
12. プログラム的な値設定でも `TextChanged` 等が発火することを**前提/失念**していないか?
13. UI スレッド外から Control に触る経路はないか? `InvokeRequired` 分岐は網羅的か?
14. UI 表示値 ↔ 保存値の変換は**全単射**か?
    (Format/Parse、DBNull ↔ null ↔ ""、カルチャ、エンコーディング)

WinForms 固有の落とし穴の詳細（発火順序表・DBNull 三値・スレッド規則）
→ `references/winforms-probes.md`

### 4. claim の書き方

- **statement は肯定形・決定的・ドメイン語彙**で書く。「TryParse が false を返すと…」
  ではなく「数量欄が空のまま登録すると数量 0 として扱われる」。
  実装の内部語彙はできるだけ隠す（「箱を閉じる」）が、evidence で行番号に紐づける
- 1 claim = 1 つの検証可能な主張。「A かつ B」は A と B に割る（verdict が別々になり得る）
- 検証したい性質には `invariant` に**名前を付けて**宣言する
  （例 `NeverOverCap == served <= cap`）。名前が付くとレビューで会話できる
- `verify_with` の初期判断: 有限で決定的 → `enumeration`、全入力の述語 → `z3`、
  順序・並行 → `tla`。確率的・収束系・brute-force 済み → `skip` + skip_reason
- C# → claim の実例集 → `references/claim-examples.md`

## 完了条件（GATE E）

```bash
python skills/spec-mining/scripts/validate_ledger.py spec-mining/<target-id>/claims.yaml
```

- exit 0 であること
- declared / implicit の両方が存在すること（片方しか無い場合、読み方が浅い可能性が高い。
  本当に無いなら最終報告にその旨を明記）
- 対象の決定ロジックに関与する**全ファイル**が source に列挙されていること

## 非対話実行時の規約

subagent / Codex として非対話で実行される場合:
- 判断に迷った claim は `confidence: low` にして進む（止まらない）
- コードが追えない参照（動的呼び出し・リフレクション・外部 DLL）に当たったら、
  その旨を claim（`category: other`, `confidence: low`）として記録する
- 最終報告には claims.yaml のパス、claim 件数（declared/implicit 別）、
  confidence: low の一覧、読めなかった箇所を含める
