# Z3 レシピ — 「どんな入力でも成り立つか?」を解く

`pip install z3-solver`（バージョンは `scripts/setup_env.sh` で固定）。
Z3 は**全入力に対して、ある一瞬**の性質を解く。check モジュール内から使い、
結果は必ず `("HOLDS"|"VIOLATES", witness)` に変換して返す（実行経路を run_harness.py に一本化）。

## 基本パターン: 反例探索は「否定して SAT を訊く」

「∀x: P(x)」を証明したい → 「∃x: ¬P(x)」を Z3 に訊く。
SAT なら反例（witness = モデル）、UNSAT なら証明成立。

```python
from z3 import Int, Bool, And, Or, Not, Implies, If, Solver, sat

CHECK_ID = "INV-010"
CLAIMS = ["CLM-001"]
EXPECTED = "HOLDS"

def run():
    qty, stock = Int("qty"), Int("stock")
    express = Bool("express")
    # predict の Z3 転記（predict.py と同じソースアノテーションを付ける）
    passes_express = Or(Not(express), qty <= 100)      # OrderForm.cs:145
    registered = And(passes_express, qty <= stock)     # OrderService.cs:87 (エラー系は別check)

    inv = Implies(express, Implies(registered, qty <= 100))   # ExpressCap
    s = Solver()
    s.add(qty >= 0, qty <= 9999, stock >= 0)           # 入力ドメイン（DB 桁数等から）
    s.add(Not(inv))                                     # 否定して反例を探す
    if s.check() == sat:
        m = s.model()
        return "VIOLATES", {str(d): str(m[d]) for d in m.decls()}
    return "HOLDS", None
```

## A1: 矛盾設定 (dead config) — UNSAT で検出

設定・条件の**連言が充足不能**なら、その設定はどんな入力でも通らない。

```python
# 設定画面が許す条件の組: 対象区分 in {A} ∧ 除外区分 in {A} ∧ 期間 [start, end]
s = Solver()
s.add(kind == KIND_A, Not(kind == KIND_A))        # 実データから変換層で流し込む
s.add(start <= d, d <= end, start > end)
if s.check() == unsat:
    return "VIOLATES", {"config": config_id, "reason": "満たす入力が存在しない (dead config)"}
```

**対象がデータなら gap は狭い**: app.config / マスタテーブルの**実データ**を読んで
Z3 制約に変換する層を書けば、証明対象と本番がほぼ一致する。設定保存時 validator
としてそのまま流用できる（このワークフローで最も費用対効果が高い成果物の一つ）。

## A2: 内包 (subsumption) — 効いていない設定

```python
# 「cond_A があるのに cond_B が全部拒否している」= ∀x: cond_B(x) ⇒ ¬cond_A(x) 到達不能
s.add(cond_A)          # A にマッチする入力で
s.add(cond_B_passes)   # B も通るものが
if s.check() == unsat:  # 存在しない → A は dead / B が支配
    ...
```

## A3: 被覆の穴 — どの枝にもマッチしない入力

```python
s.add(Not(Or(branch1, branch2, branch3)))   # 全分岐の否定
if s.check() == sat:                         # 穴が存在
    return "VIOLATES", model_to_witness(s.model())
```

## A4: 差分影響 — 設定変更で判定が変わる入力の摘出

```python
s.add(old_decision != new_decision)
# sat なら witness が「この人/この伝票の判定が変わる」の具体例
```

## A5: リファクタ・コピペ等価性

```python
s.add(impl_a != impl_b)   # 両実装を Z3 式に転記して不一致入力を探す
# UNSAT = 全入力で等価（リファクタの安全証明）
```

## C# 意味論の写像に注意

| C# | Z3 での正しい写像 |
|---|---|
| `int`（オーバーフローが効き得る計算） | `Int` でなく `BitVec(32)` + 符号付き演算。金額集計・乗算があるなら必須 |
| `int` 除算 `/` | BitVec なら機械除算。`Int` を使うなら C# の 0 方向切り捨てと Z3 の floor 除算の差に注意（負数で違う） |
| `int.TryParse` 失敗 | パース可否を `Bool` で別変数化し、失敗時 `qty == 0` を制約に追加 |
| `null` / DBNull / 取得エラー | 「エラーか否か」の `Bool` を昇格。Option 型を模倣する |
| `decimal` / `double` | 丸めが論点なら Real + 明示丸め関数、または列挙に切り替える |
| `string` の桁数・書式 | 内容が論点でなければ長さの `Int` に抽象化。書式契約は roundtrip（C5）で |

## C5: 表現契約の roundtrip 証明

```python
# encode: 画面値 → DB 値, decode: DB 値 → 画面値 を両方転記して
s.add(decode(encode(x)) != x)     # sat なら往復で壊れる値が存在
```

## 運用の注意

- `s.check()` が `unknown` を返したら、非線形・文字列制約が混ざっている。
  ドメインを有界化するか全列挙に切り替える（unknown を HOLDS 扱いにしない）
- witness は `m[d]` の生値でなく**ドメイン語彙の dict** に変換してから返す
- Z3 側の式も predict.py と同じく**ソースアノテーション必須**。predict.py（列挙用）と
  Z3 式の二重転記になる場合、少数のランダム入力で predict と Z3 式の一致を assert する
  cross-check を check 内に入れると転記ミスを検出できる
