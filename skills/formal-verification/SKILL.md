---
name: formal-verification
description: >
  C# レガシーコードから吸い出した claims（実装の主張）を形式化し、全列挙 / Z3 / TLA+ で
  反例を探索するスキル。純粋決定関数 predict() の抽出、invariant の検査実装、
  self-check と broken-variant による「効いている検証」の維持までを担う。
  spec-mining ワークフローの PHASE 2–3。「この条件はどんな入力でも成り立つか確かめたい」
  「並行実行で壊れないか検査したい」「Z3/TLA+ でモデル検査したい」際に使用する。
---

# Formal Verification — モデル化して機械に攻撃させる

入力: `spec-mining/<target-id>/claims.yaml`
出力: `model/`（predict.py, checks/, broken/）、`counterexamples.yaml`、ledger.yaml の verdict

**成立を証明したいのか、反例が欲しいのか**を invariant ごとに意識して立てる
（check の `EXPECTED` がそれ）。証明と反例は同じ道具の裏表。

## 手順

```
1. ツール選定      claim ごとに 全列挙 / Z3 / TLA+ を判定
2. predict() 抽出  実装の配管を剥がし、純粋関数として Python に転記（行番号アノテーション必須）
3. check 実装      invariant ごとに checks/chk_*.py。EXPECTED を宣言
4. broken 実装     EXPECTED: HOLDS の check にはガードを壊した variant を用意
5. 実行            run_harness.py。反例は counterexamples.yaml へ（trace.status: pending）
```

## 1. ツール選定（判定フロー）

上から順に問う。**ツールを持ち出す前に「有限か? 決定的か?」を問う**こと。

1. **状態空間が有限で決定的か?**（同値類×境界の直積が数万以下）
   → **素の全列挙**が一番安い。model↔code ギャップも最小 → `references/enumeration-recipes.md`
2. **「どんな入力でも成り立つか?」型の述語か?**（区間+集合+等式+線形算術の decidable fragment）
   → **Z3**。矛盾設定(UNSAT)・内包・被覆の穴・リファクタ等価性・差分影響
   → `references/z3-recipes.md`
3. **「どの順番で起きても / 同時に来たら / クラッシュ後は?」型か?**（状態遷移+非決定性）
   → **TLA+ (TLC)**。ただしアクター2×操作3程度なら Python での interleaving 全列挙でも足りる
   → `references/tlaplus-recipes.md`
4. どれでもない（確率的・収束系・brute-force 済み）→ 形式化しない。
   ledger に `verdict: untested` + 理由、または claims の `verify_with: skip`

どのバグパターンにどのツールが効くかの対応表 → `references/bug-catalog-winforms.md`

## 2. predict() の抽出 — 転記の規律

観測可能な入力 → 結果の**純粋関数**を、実装の配管（イベント引数、MessageBox、DB アクセス、
`this.Close()`）を剥がして書く。これが仕様の数学的表現になる。

```python
# model/predict.py
def predict(state: dict) -> dict:
    """order-qty-guard の決定関数。
    state: qty_text(str), express(bool), stock(int|None: None=取得エラー)
    returns: {registered: bool, qty: int, reason: str}
    """
    ok, qty = try_parse_int(state["qty_text"])   # OrderForm.cs:143
    # 転記注意: TryParse 失敗時も qty=0 で続行する（return しない） OrderForm.cs:143
    if state["express"] and qty > 100:           # OrderForm.cs:145
        return {"registered": False, "qty": qty, "reason": "express-cap"}
    over = is_over_stock(qty, state["stock"])    # OrderForm.cs:150 -> OrderService.cs:85
    if not over:                                 # OrderForm.cs:150 （否定に注意）
        return {"registered": True, "qty": qty, "reason": "ok"}
    return {"registered": False, "qty": qty, "reason": "over-stock"}

def is_over_stock(qty, stock):
    if stock is None:                            # OrderService.cs:88 catch → false (fail-open)
        return False
    return qty > stock                           # OrderService.cs:87
```

**規律（GATE V で機械チェックされるものを含む）:**

- **全分岐に `# File.cs:行` アノテーション**。転記ミスは偽の証明を生む最大の敵。
  アノテーションできない行 = 実装を読んでいない行 = 書いてはいけない行
- **配管はモデルに持ち込まない**。DB・I/O は「その結果の値」を state の入力に昇格させる。
  **エラーも入力の一値に昇格**させる（`stock: None` = 取得エラー）。これで fail-open/close が
  全列挙の対象になる
- **C# の意味論を保存する**: `int` 除算は切り捨て（Python の `//` だが負数で挙動が違う →
  `int(a/b)` 相当に注意）、`int.TryParse` 失敗時は out=0、`&&`/`||` の短絡、
  文字列比較のカルチャ、`int` オーバーフロー。怪しければ Z3 の BitVec か明示関数でモデル化
- 状態遷移系では predict でなく `step(state, action) -> state` を書く（recipes 参照）

## 3–4. check と broken-variant の実装

モジュール契約は `skills/spec-mining/references/ledger-format.md` の「model/ ディレクトリ規約」
に従う。実装の要点:

```python
# model/checks/chk_001_fail_closed.py
from itertools import product
import sys, pathlib
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
from predict import predict

CHECK_ID = "INV-001"
CLAIMS = ["CLM-003"]
EXPECTED = "VIOLATES"   # 反例が欲しい: fail-open の疑いを witness 付きで確定させる

QTY_TEXTS = ["", "abc", "0", "1", "100", "101", "999"]   # 同値類+境界。cap-1, cap, cap+1 必須
STOCKS = [None, 0, 50, 1000]                             # None = 取得エラー

def run():
    for qty_text, express, stock in product(QTY_TEXTS, [False, True], STOCKS):
        r = predict({"qty_text": qty_text, "express": express, "stock": stock})
        if stock is None and r["registered"]:            # FailClosed == stock_error => !registered
            return "VIOLATES", {"qty_text": qty_text, "express": express, "stock": "ERROR"}
    return "HOLDS", None
```

```python
# model/broken/brk_001_remove_error_case.py
# 「在庫エラーを正常在庫 1000 として扱う」ように壊したモデルでも INV-001 が
# VIOLATES を出すか…ではなく、この check は EXPECTED=VIOLATES なので、
# broken は EXPECTED=HOLDS の check（例: chk_002_express_cap）に対して作る。
```

**broken-variant の作り方**（`EXPECTED: HOLDS` の check ごとに最低1つ）:

- predict のガードを1つ外す / 境界を反転する（`>` → `>=`）/ 否定を落とす、を**コピーした
  broken 用 predict** で行い、同じ検査ロジックを流して VIOLATES になることを確認する
- 壊し方は「実際にありそうな退行」を選ぶ（将来のリファクタで入り込みそうな変更）
- broken が緑のままなら、その check は**何も検証していない**。入力集合か述語を疑う

## 5. 実行と反例の記録

```bash
python skills/spec-mining/scripts/run_harness.py spec-mining/<target-id>/model
```

- 反例（EXPECTED=HOLDS が VIOLATES / EXPECTED=VIOLATES で witness 取得）は
  `counterexamples.yaml` に記録する。witness は**意味のある変数名の具体値**で書く
  （ツールの生出力を貼らない）。`trace.status: pending` で PHASE 4 に引き渡す
- EXPECTED 通りに HOLDS した invariant は ledger の `verdict: proved` 候補
- **モデルを直して反例を消さない**こと。反例が実装と食い違う（モデルの虚偽）と
  確認されたときだけ predict を修正してよい（それは PHASE 4 の trace で判定する)

## 完了条件（GATE V）

- `run_harness.py` exit 0
- 全 `EXPECTED: HOLDS` check に broken-variant あり（ランナーが強制）
- predict.py / step.py の全分岐にソースアノテーションあり
- 反例はすべて counterexamples.yaml に witness 付きで記録済み

## 非対話実行時の規約

- ツール（z3 / java）が環境に無ければ `scripts/setup_env.sh` を実行する。
  それでも入らなければ、その claim は `verdict: untested` + 理由で台帳に残し、
  全列挙で代替できるものは代替する（止まらない）
- TLC の状態爆発（>10^7 状態 or 5分超）は定数を縮める（cap=3 等の小モデル）。
  小モデルで反例が出ればそれで十分。出なければ「小モデルで成立」と untested_reason に明記
- 最終報告には check/broken の件数、反例一覧、proved 一覧、モデル化を諦めた claim を含める
