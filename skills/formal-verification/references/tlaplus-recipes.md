# TLA+ / TLC レシピ — 「どの順番で起きても大丈夫か?」を解く

TLA+ は**時間を通じて、全順序・全 interleaving** を総当たりする。WinForms では
「UI イベント × Timer × BackgroundWorker × 複数端末」の非決定性がある claim に使う。
アクター2×操作3程度なら `enumeration-recipes.md` の interleaving 全列挙で足りる —
TLC を持ち出すのは、状態が育つ（キュー・カウンタ・リトライ）か crash/restart を含むとき。

実行には java + tla2tools.jar が必要（`scripts/setup_env.sh` が固定バージョンを配置し、
パスを `TLA2TOOLS_JAR` として案内する）。

## テンプレート: 上限カウンタのレース（B1/B2 型）

`spec-mining/<target-id>/model/InvoiceCap.tla`:

```tla
---------------------------- MODULE InvoiceCap ----------------------------
(* InvoiceForm.cs btnIssue_Click (L202-204) + Worker_DoWork (L210) の抽象 *)
EXTENDS Naturals

CONSTANTS Clients, Cap          \* 端末集合と日次上限
VARIABLES issued,               \* DB の発行済み件数 (L210 の insert 先)
          pc,                   \* 各端末の進行位置: "idle" | "checked"
          snapshot              \* 各端末が読んだ件数 (L202 の CountToday 結果)

vars == <<issued, pc, snapshot>>

Init == /\ issued = 0
        /\ pc = [c \in Clients |-> "idle"]
        /\ snapshot = [c \in Clients |-> 0]

\* L202: 読んで判定 (read と judge が insert から分離している = 実装の忠実な写し)
Check(c) == /\ pc[c] = "idle"
            /\ snapshot' = [snapshot EXCEPT ![c] = issued]
            /\ snapshot'[c] < Cap                  \* CountToday() >= cap なら return
            /\ pc' = [pc EXCEPT ![c] = "checked"]
            /\ UNCHANGED issued

\* L210: 記録は後で非同期
Insert(c) == /\ pc[c] = "checked"
             /\ issued' = issued + 1
             /\ pc' = [pc EXCEPT ![c] = "idle"]
             /\ UNCHANGED snapshot

Next == \E c \in Clients : Check(c) \/ Insert(c)
Spec == Init /\ [][Next]_vars

NeverOverCap == issued <= Cap                       \* CLM-004 の invariant
=============================================================================
```

`InvoiceCap.cfg`:

```
CONSTANTS
    Clients = {c1, c2}
    Cap = 1
INVARIANT NeverOverCap
```

**対比モデルを必ず併設する**: `CheckAndInsert(c)`（判定と加算を1アクションに = 条件付き
書き込み）に置き換えた `InvoiceCapAtomic.tla` は NeverOverCap が成立する。
「naive は破れる / atomic なら防げる」の対比が修正提案の根拠になる。
broken-variant はこの逆（atomic 版のガードを外す）で作れる。

## check モジュールから TLC を叩く（実行経路の一本化）

```python
# model/checks/chk_010_never_over_cap.py
import os, re, subprocess, pathlib

CHECK_ID = "INV-010"
CLAIMS = ["CLM-004", "CLM-005"]
EXPECTED = "VIOLATES"    # 反例志向: naive 実装は破れるはずと主張する

HERE = pathlib.Path(__file__).resolve().parents[1]

def run():
    jar = os.environ.get("TLA2TOOLS_JAR", ".spec-mining-tools/tla2tools-1.8.0.jar")
    p = subprocess.run(
        ["java", "-XX:+UseParallelGC", "-cp", jar, "tlc2.TLC",
         "-config", "InvoiceCap.cfg", "-workers", "auto", "InvoiceCap.tla"],
        cwd=HERE, capture_output=True, text=True, timeout=300)
    out = p.stdout
    if "Invariant NeverOverCap is violated" in out:
        # エラートレース（State 1..N）を witness として要約する
        states = re.findall(r"State (\d+).*?\n(.*?)(?=\nState |\n\n)", out, re.S)
        return "VIOLATES", {"trace_len": len(states),
                            "last_state": states[-1][1].strip() if states else out[-500:]}
    if "Model checking completed. No error has been found" in out:
        return "HOLDS", None
    raise RuntimeError(f"TLC 異常終了 (exit {p.returncode}):\n{out[-2000:]}\n{p.stderr[-500:]}")
```

TLC の生トレースは counterexamples.yaml に貼らず、**ドメイン語彙の手順**に翻訳して
witness / summary に書く（「端末1と端末2が同時に残数1を見て、両方発行した」）。

## WinForms の抽象化パターン

| 実装要素 | TLA+ での写像 |
|---|---|
| ボタン連打・複数端末 | `Clients` 定数集合のプロセス。二重クリックは同一 client の再 `Check` を許す遷移 |
| BackgroundWorker | `pc` に "working" 状態を足し、完了を別アクションに（UI と worker の interleaving が出る） |
| `async void` の await 境界 | await の前後でアクションを割る。await 中に他イベントの遷移を許す |
| Timer | いつでも発火できるアクション（ガードなし `Tick`） |
| クラッシュ・強制終了 | `Crash == pc' = [c |-> "idle"] /\ メモリ状態だけ初期化`（DB 変数は残す）。B7 の edge/level-trigger 検証はこれで |
| フォームクローズ | worker が "working" のまま UI 変数を無効化する遷移 → ObjectDisposed 相当の invariant |

## 状態爆発の抑え方

- 定数は最小から: `Clients = {c1, c2}`, `Cap = 1`。**小モデルで反例が出ればそれで十分**
  （反例の大半は 2 プロセス・小さい上限で出る）
- 数値は上限でクリップ（`issued <= Cap + 2` を型不変条件に）
- 5分 / 10^7 状態を超えたら定数を縮め、`untested_reason` に「Clients=2, Cap=1 の
  小モデルで成立」と正直に書く（全域の証明とは言わない）
- liveness（「いつか必ず反映される」B7）は `PROPERTY` + 公平性（WF_vars）が必要で
  検査コストが跳ねる。まず safety（悪いことは起きない）だけで反例を狩る

## model↔code ギャップを詰める（trace-checking）

命令型コードの手抽象は乖離が残る。強い一手は **trace-checking**:
実システムの操作ログ（監査ログ・DB の更新履歴・追加したデバッグログ）からイベント列を採取し、
「その観測列はモデルの正当な振る舞いか」を TLC の
trace validation（観測列を制約として流す小さな spec）で検査する。
「実装は naive / atomic のどちらの仕様を refine しているか」を実データで確定できる。
レガシーで書けない場合は、PHASE 4 の witness 手動トレース（counterexamples.yaml の
trace.steps）が最低限の代替。
