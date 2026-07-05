# 形式手法が炙り出すバグのカタログ — C# / WinForms 版

プレイブック §2 の汎用カタログを WinForms レガシーの具体的なイディオムに写像したもの。
**チェックリストとして使う**: PHASE 0 の対象選定、PHASE 1 の claim 立て、PHASE 3 の
invariant 設計の全部で参照する。

凡例 — 検証: `列挙`=全列挙 / `Z3` / `TLA+`（小さければ interleaving 列挙で代替可）

## A. 純粋述語・設定系（Z3 / 全列挙向き）

| # | パターン | WinForms での典型的な姿 | invariant の型 | 検証 |
|---|---|---|---|---|
| A1 | **矛盾設定 (dead config)** | app.config / INI / マスタテーブルの条件組合せが充足不能。「対象区分=Aのみ ∧ Aを除外」「開始日 > 終了日」を許す設定画面 | 条件の連言が SAT であること（UNSAT なら dead） | Z3 |
| A2 | **冗長・内包 (subsumption)** | 上限が複数箇所にある（画面の MaxLength、コードのガード、DB の桁数）。きつい方が支配し、緩い方の「設定」が効いていない | ∀x: cond_A(x) ⇒ cond_B(x) の含意検査。「実効値」の算出 | Z3 |
| A3 | **被覆の穴 / dead branch** | if-else if の連鎖・switch にどれにもマッチしない入力がある / 到達しない枝がある。`_suppressEvents` フラグの set/reset 漏れ経路 | ∀x: ∃branch: matches(x)（穴）/ ∃x: matches_i(x)（dead） | Z3 / 列挙 |
| A4 | **変更の差分影響** | 「この設定値を変えたら誰の判定が変わるか」。マスタ変更の影響範囲が読めない | ∃x: old(x) ≠ new(x) の witness 摘出 | Z3 |
| A5 | **表現の等価性** | 同じ判定がコピペで複数フォームに存在（等価のはずが微妙に違う）。リファクタ前後・VB6 移植前後 | ∀x: impl_A(x) = impl_B(x) | Z3 / 列挙 |

## B. 並行・時間系（TLA+ / interleaving 列挙向き）

| # | パターン | WinForms での典型的な姿 | invariant の型 | 検証 |
|---|---|---|---|---|
| B1 | **read-modify-write レース / TOCTOU** | `CountToday() >= cap` で判定してから Insert。複数端末・ボタン連打・`async void` の await 中再操作で二重突破 | NeverOverCap == issued ≤ cap | TLA+ |
| B2 | **判定と記録の分離** | 判定は Click 時、Insert は BackgroundWorker / 後続バッチ。判定時点で記録がまだ無い | 同上（分離を step に忠実に写す） | TLA+ |
| B3 | **結果整合 (staleness)** | 起動時ロードのマスタ DataSet を見て判定、DB は別端末が更新済み。**並行が無くても**破れる | 判定に使う値の鮮度条件 | TLA+ / 列挙 |
| B4 | **原子性 / torn read** | 共有フォルダの CSV/INI を in-place 上書き、読み手が途中状態を読む。「別名で書いて File.Move」との対比 | NoTornRead == 読んだ内容は常に単一版 | TLA+ |
| B5 | **カウンタ不変条件** | 採番テーブル `SELECT MAX+1`、在庫引当の増減。重複・負値 | counter ≥ 0、番号の一意性 | TLA+ |
| B6 | **収束 / runaway** | リトライループ・再接続・再帰的な画面更新が有界で止まるか。止めているのがタイムアウトだけ（load-bearing） | 停止性（有界ステップで終了状態へ） | TLA+ |
| B7 | **伝搬 / 最終整合** | ローカル保存→後で同期、のキューがクラッシュ・強制終了で失われる（edge-trigger）。再起動で追いつくか（level-trigger） | 最終的に必ず反映される（liveness） | TLA+ |
| B8 | **冪等性** | 通信タイムアウト後の再送で二重登録。伝票番号を「送信毎に採番」していると重複記録。二重起動（Mutex 漏れ） | retry しても効果は1回分 | TLA+ / 列挙 |

## C. 境界・信頼系（どちらでも）

| # | パターン | WinForms での典型的な姿 | invariant の型 | 検証 |
|---|---|---|---|---|
| C1 | **fail-open/close の非一貫** | ある catch は処理中断、別の catch は `return false` で続行。権限チェックとロックチェックで倒れ方が逆 | 機構ごとの error ⇒ 結果 を列挙し一貫性を検査 | 列挙 |
| C2 | **エラーを boolean に畳む × 否定** | `TryParse` 失敗→0、`catch { return false; }` を `!` で使う。**壊れた入力ほど通る**。最も見落とされやすい | error ⇒ ¬(通過) | 列挙 |
| C3 | **空集合の意味** | `DataView.RowFilter = ""` は**全件**、条件リスト空で AND=真（常時マッチ backdoor）。チェックボックス全 OFF の意味が画面ごとに違う | 空のときの規約を明示し全機構で一致 | 列挙 / Z3 |
| C4 | **信頼境界と詐称** | ComboBox(DropDown) の Text、グリッド編集値、Clipboard 貼り付け、INI/レジストリ値が無検証で SQL・判定に採用 | 信頼されない入力は検証を経由する | 列挙 |
| C5 | **クロス境界の表現契約** | DB の NULL ↔ DBNull ↔ null ↔ ""、日付書式のカルチャ、Shift_JIS/UTF-8、enum の int 値が DB に生で入っている、片方が税込/片方が税抜 | 変換の全単射性（roundtrip: decode(encode(x)) = x） | Z3 / 列挙 |
| C6 | **クライアント側だけの検証** | MaxLength・Validating で弾くが、DB 制約なし。別画面・ストアド・直接 UPDATE で不正状態が作れる | 不変条件が永続層でも強制される | 列挙 + 手動確認 |
| C7 | **欠けた前提条件** | `SelectedIndex == -1` のまま参照、Load 前/ Dispose 後アクセス、空 DataTable の `Rows[0]`、負数・0 件でのループ | ガードの存在（呼び出し側でなく定義側に） | 列挙 |

## 使い方

- **PHASE 0**: この表を上から眺め、対象システムに「ありそう」な行に当たりをつける
  （検索クエリは `skills/spec-mining/references/winforms-target-selection.md`）
- **PHASE 1**: 該当パターンの claim を立てるとき、この表の「invariant の型」を
  `invariant` フィールドの雛形にする
- **PHASE 3**: 「検証」列でツールを選び、対応する recipes ファイルへ:
  `enumeration-recipes.md` / `z3-recipes.md` / `tlaplus-recipes.md`
