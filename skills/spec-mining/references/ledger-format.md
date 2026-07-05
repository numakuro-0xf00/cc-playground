# 台帳フォーマット仕様

spec-mining の全成果物のスキーマ。`scripts/validate_ledger.py` がこの仕様を機械的に検証する。
人間可読かつ機械可読であること（YAML + 定型キー）。**このスキーマにないキーを追加してもよい**が、
必須キーの欠落・列挙値の逸脱はゲート失敗になる。

---

## claims.yaml — 実装の主張

```yaml
target: order-entry                # target-id (kebab-case)
source:
  - path: src/Forms/OrderForm.cs   # 読んだファイル（リポジトリ相対）
    revision: a1b2c3d              # git rev-parse --short HEAD
claims:
  - id: CLM-001                    # CLM-NNN 連番
    kind: declared                 # declared | implicit
    category: boundary             # 下の分類表から1つ
    statement: >
      注文数量は 1〜999 のみ受理される（btnOK_Click のガード）。
      境界は両端を含む（>= 1 && <= 999）。
    evidence:                      # 必須・1件以上。実装を読まずに書いた claim は無効
      - path: src/Forms/OrderForm.cs
        lines: "142-156"
        note: "btnOK_Click 内の if ガード"
    questions_applied: [1, 4]      # spec-extraction の問い番号（該当するもの）
    confidence: high               # high | medium | low
    verify_with: enumeration       # enumeration | z3 | tla | manual | skip
    skip_reason: null              # verify_with: skip のとき必須
    invariant: "QtyInRange == 1 <= qty /\\ qty <= 999"   # 任意。名前付きで
```

### category の分類表

| 値 | 意味 | 典型例 |
|---|---|---|
| `boundary` | 境界・範囲・上限 | `>` vs `>=`、cap、期間 |
| `error-handling` | エラー時の倒れ方 | fail-open/close、catch の畳み込み |
| `empty-default` | 空・未設定・null の意味 | 空文字、DBNull、未選択(-1) |
| `ordering` | 順序・並行・再入 | イベント発火順、Timer、二重クリック |
| `trust` | 信頼境界 | ユーザー編集可能値の無検証採用 |
| `cross-boundary` | 表現契約 | DB↔DataSet 型、書式、単位、エンコーディング |
| `state` | 状態遷移・活性制御 | Enabled/Visible の制御、画面フロー |
| `idempotency` | 冪等性 | リトライ、再送、二重起動 |
| `equivalence` | 重複実装の等価性 | コピペされた同種判定 |
| `other` | 上記以外 | |

### kind の判定基準

- `declared` — コメント（特に「なぜ」を語るもの）、テスト名・アサーション、ガード節・
  例外送出、enum 定義・定数・スキーマ制約が**宣言している**仕様。
  「元のコードがそうなっていた」系コメントは化石（fossil）— declared だが confidence: low
- `implicit` — どこにも書いてないが実装が確定させている挙動:
  default 値、空/null の扱い、エラー時分岐、順序依存と短絡、値の決定箇所（フォールスルー優先
  順位）、クロス境界変換、誰が制御できる入力か

---

## counterexamples.yaml — 反例と witness

```yaml
target: order-entry
counterexamples:
  - id: CEX-001
    check: INV-002                 # checks/chk_*.py の CHECK_ID
    claim: CLM-003                 # 破られた claim
    witness:                       # 具体値。ツール出力をそのまま貼らず、意味のある変数名で
      qty_text: ""
      retry: true
    summary: >
      数量が空文字のままリトライフラグが立つと、TryParse 失敗(qty=0)が
      在庫チェックを素通りして 0 件注文が登録される。
    trace:                         # 必須（PHASE 4 で記入）: 実装での再現確認
      status: reproduced           # reproduced | model-artifact | pending
      steps:
        - "OrderForm.cs:148 int.TryParse(txtQty.Text, out qty) → false, qty=0"
        - "OrderForm.cs:151 エラーでも return せず継続（catch なし分岐）"
        - "OrderService.cs:88 qty <= stock は 0 <= stock で常に真"
      note: "ビルド可能だったため ReproHarness.csx でも確認"
    disposition: bug-candidate     # bug-candidate | question-open | spec-confirmed
    question: QST-002              # questions.md の ID（disposition が question-open / bug-candidate のとき必須）
```

`trace.status: model-artifact` は「モデルの虚偽」= predict.py の転記ミス。
その場合 disposition は不要、predict.py を修正して再検証すること。

---

## ledger.yaml — 集約台帳（最終成果物）

```yaml
target: order-entry
updated: "2026-07-05"
entries:
  - claim: CLM-001
    verdict: proved                # proved | counterexample | untested | not-applicable
    check: INV-001                 # verdict が proved / counterexample のとき必須
    counterexample: null           # verdict: counterexample のとき CEX-NNN 必須
    disposition: contract-locked   # contract-locked | bug-candidate | question-open |
                                   # spec-confirmed | dropped
    untested_reason: null          # verdict: untested のとき必須
questions_file: questions.md
harness: model/                    # run_harness.py に渡すディレクトリ
```

### verdict × disposition の整合規則（バリデータが強制）

| verdict | 許される disposition |
|---|---|
| `proved` | `contract-locked`（原則）、`spec-confirmed` |
| `counterexample` | `bug-candidate`、`question-open`、`spec-confirmed`（ドメインが意図と回答済み） |
| `untested` | `dropped`（untested_reason 必須） |
| `not-applicable` | `dropped` |

---

## questions.md — ドメインへの確認質問

機械検証しない自由記述だが、以下のテンプレートを1質問1セクションで使うこと:

```markdown
## QST-002: 数量が空のままリトライすると 0 件注文が登録されるのは意図ですか?

- **関連**: CLM-003 / CEX-001
- **現象** (witness): 数量欄が空 + リトライ操作 → `TryParse` 失敗で qty=0 のまま
  在庫チェックを通過し、0 件の注文レコードが作成される
- **再現**: OrderForm.cs:148 → 151 → OrderService.cs:88（counterexamples.yaml CEX-001 参照）
- **意図だった場合**: 「空欄=0件注文は許容」を仕様として明文化し、contract 化します
- **バグだった場合**: TryParse 失敗時に即エラー表示して return する修正を提案します
- **業務影響**: 0 件注文が下流の出荷バッチでどう扱われるか未調査（要確認）
```

質問は**肯定形・決定的・ドメイン語彙**で書く（実装の内部語彙を隠す =「箱を閉じる」）。
「TryParse が false を返す」ではなく「数量欄が空のまま登録すると」と書く。

---

## model/ ディレクトリ規約

- `predict.py` — 純粋決定関数。全分岐に `# File.cs:NNN` アノテーション必須
- `checks/chk_<連番>_<名前>.py` — 各検査。必須のモジュール属性:
  - `CHECK_ID`（例 `"INV-001"`）、`CLAIMS`（対象 claim ID のリスト）
  - `EXPECTED` — `"HOLDS"`（成立を証明したい）| `"VIOLATES"`（反例が欲しい）
  - `def run() -> tuple[str, dict | None]` — `("HOLDS", None)` または `("VIOLATES", witness)`
- `broken/brk_<check連番>_<壊し方>.py` — broken-variant。必須属性:
  - `GUARDS`（守る対象の CHECK_ID）、`def run()`（同シグネチャ）
  - わざと壊した predict（ガード除去・境界反転など）で **VIOLATES が返ること**をランナーが検証する
- TLA+ を使う check は、モジュール内から TLC を subprocess で叩いて結果を上記形式に変換する
  （実行経路を run_harness.py の1本に保つため）
