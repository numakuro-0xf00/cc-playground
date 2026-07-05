# 全列挙レシピ — 一番安い形式検証

状態空間が**有限で決定的**なら、直積を回すだけの全列挙が最速・最も model↔code ギャップが
小さい。ツールを持ち出す前に必ずここから検討する。

## 1. 同値類と境界の設計

全入力は無限でも、**判定が変わる同値類と境界**は有限。入力ごとに列挙集合を定義する:

```python
# 数量テキスト: パース可否 × 境界
QTY_TEXTS = ["", " ", "abc", "-1", "0", "1", "99", "100", "101", "999", "1000"]
#            ^^^^ 空・空白・非数値・負数は必ず入れる
#            境界 cap にたいして cap-1, cap, cap+1 を必ず入れる

# エラーを入力の一値に昇格させる（fail-open/close を列挙対象にする）
STOCKS = [None, 0, 1, 50, 999999]     # None = DB 取得エラー

# 状態フラグは全組合せ
FLAGS = [False, True]
```

- 境界 (cap-1, cap, cap+1) は**必ず**入れる。`>` vs `>=` の取り違えはここでしか出ない
- 同値類の設計自体が仕様理解。claims.yaml の evidence にある分岐条件から機械的に導出する:
  条件に現れる各定数 c について {c-1, c, c+1} を入れるのが最低ライン

## 2. 述語の全列挙（C1/C2/C3/C7 型）

```python
from itertools import product
from predict import predict

def run():
    for qty_text, express, stock in product(QTY_TEXTS, FLAGS, STOCKS):
        state = {"qty_text": qty_text, "express": express, "stock": stock}
        r = predict(state)
        if stock is None and r["registered"]:      # invariant: FailClosed
            return "VIOLATES", {**state, "result": r}
    return "HOLDS", None
```

## 3. 等価性の全列挙（A5 型: コピペ判定の突き合わせ）

コピペされた2つの判定（例: OrderForm と QuickOrderForm）をそれぞれ predict_a / predict_b に
転記し、全入力で一致を検査する。**差分が出た入力が、そのまま「どちらが正しいか」の
ドメイン質問になる。**

```python
def run():
    for state in all_states():
        a, b = predict_a(state), predict_b(state)
        if a != b:
            return "VIOLATES", {"input": state, "OrderForm": a, "QuickOrderForm": b}
    return "HOLDS", None
```

## 4. 状態機械の全列挙（画面の活性制御・遷移）

`Enabled/Visible` の代入を集めて `step(state, action) -> state` に転記し、
到達可能状態を BFS で全列挙する。

```python
# state: frozenset や tuple にして set に入れる（hashable に）
INITIAL = ("empty", "btn_disabled")
ACTIONS = ["type_text", "clear_text", "click_save", "close_x", "timer_tick"]

def step(state, action):  # 各遷移に # File.cs:NNN
    ...

def run():
    seen, frontier = {INITIAL}, [INITIAL]
    while frontier:
        s = frontier.pop()
        for a in ACTIONS:
            t = step(s, a)
            if t is None:            # そのアクションが不可能な状態
                continue
            if violates_invariant(t):    # 例: 「保存ボタン活性 ∧ 必須項目が空」
                return "VIOLATES", {"state": t, "via": a, "from": s}
            if t not in seen:
                seen.add(t); frontier.append(t)
    return "HOLDS", None
```

## 5. interleaving の全列挙（小さい並行なら TLA+ 不要）

アクター2×各3ステップ程度なら、全 interleaving を Python で回せる（B1/B2/B8 型）。

```python
from itertools import permutations

# 各アクターのステップ列。read/insert の分離を忠実に写す
def actor_steps(actor_id):
    return [("read", actor_id), ("check", actor_id), ("insert", actor_id)]

def interleavings(a, b):
    # a, b のステップ順序を保ったままマージした全順序
    if not a: yield b; return
    if not b: yield a; return
    for rest in interleavings(a[1:], b): yield [a[0]] + rest
    for rest in interleavings(a, b[1:]): yield [b[0]] + rest

def run():
    CAP = 1
    for order in interleavings(actor_steps("A"), actor_steps("B")):
        db, local = 0, {}
        for op, who in order:
            if op == "read":   local[who] = db                    # InvoiceForm.cs:202
            elif op == "check": local[f"{who}_ok"] = local[who] < CAP
            elif op == "insert" and local.get(f"{who}_ok"): db += 1  # InvoiceForm.cs:210
        if db > CAP:                                               # NeverOverCap
            return "VIOLATES", {"order": order, "issued": db, "cap": CAP}
    return "HOLDS", None
```

**対比モデルを併設する**: 「条件付き書き込み（判定と insert を atomic に）なら防げる」を
別 check（EXPECTED: HOLDS）として並べると、反例が**設計の問題**であることを示せる。
これが修正提案の根拠になる。

## 6. サイズの目安

- 直積 10^6 まで: そのまま回す（数秒）
- 10^6〜10^8: 同値類を削る（本当に判定に効く軸だけ残す）
- それ以上 or 非決定性が本質: Z3 / TLA+ へ
