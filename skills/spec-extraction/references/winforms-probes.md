# WinForms 固有の暗黙挙動プローブ集

WinForms では**フレームワーク自体が暗黙の仕様を大量に持ち込む**。実装コードに書かれていなくても
挙動を確定させている要素を、吸い出し時に確認するためのリスト。
断定できない項目は claim を立てて `confidence: low` にし、検証（PHASE 3）か
ドメイン質問（PHASE 4）に回す。

## 1. イベント発火順序と「発火しない経路」

発火順序・発火の有無は**操作経路（Tab / マウス / ×ボタン / ショートカット / プログラム操作）で
異なる**。「この検証は必ず通る」という claim を見つけたら、必ず全経路を問うこと。

| 罠 | 何が起きるか | 立てるべき claim の例 |
|---|---|---|
| `ToolStripButton` / `ToolStripMenuItem` はフォーカスを奪わない | 通常の `Button` と違い、クリックしても直前のコントロールの `Validating` が**走らない** | 「保存はツールバーからも実行でき、その経路では入力検証がスキップされる」(implicit / ordering) |
| `CausesValidation = false` | そのコントロールへのフォーカス移動では `Validating` が走らない（キャンセルボタン等の定石だが、設定漏れ/過剰設定がバグ源） | Designer.cs の値を evidence にする |
| フォームを×ボタンで閉じる | フォーカス中コントロールの検証が走るかは `AutoValidate` と .NET バージョンに依存。**経路によって未検証のまま `FormClosing` に到達し得る** | 「×ボタン経由では未検証の値が保存され得る」(implicit / ordering) |
| `Validating` で `e.Cancel = true` | フォーカスがロックされる。`FormClosing` との相互作用で「閉じられない画面」や「検証スキップして閉じる」が生まれる | 状態機械として PHASE 3 (tla/全列挙) 向き |
| `Form.Load` 内の例外 | 32bit プロセスを 64bit OS で動かすと OS のコールバック境界で**例外が握りつぶされる**有名問題。初期化失敗が「無言で中途半端に初期化された画面」になる | 「Load 失敗時は未初期化フィールドのまま操作可能」(implicit / error-handling) |

## 2. プログラム的操作でもイベントは発火する

- `txt.Text = "..."` でも `TextChanged` が**発火する**。初期化コードが検証ハンドラを
  誤爆させる／ハンドラ内での再代入が再帰する
- `SelectedIndexChanged` は `Items.Clear()` / データバインディングでも発火し得る
  （クリア時に SelectedIndex が -1 に変わる）
- `CheckedChanged` は `Checked = true` の代入でも発火
- 対策としてよく見る `_isLoading` / `_suppressEvents` フラグは**手動の排他制御**であり、
  設定し忘れ経路・戻し忘れ経路が被覆の穴になる。フラグの set/reset 全箇所を evidence 化する

## 3. スレッドと再入

| 機構 | 実行スレッド | 典型バグ |
|---|---|---|
| `System.Windows.Forms.Timer` | UI スレッド | 再入は無い（メッセージポンプ依存）が、`Tick` 内の `DoEvents` / モーダル表示で再入する |
| `System.Timers.Timer` / `System.Threading.Timer` | スレッドプール | UI 直接操作でクロススレッド例外（.NET 2.0 以降）。ただし**リリースビルドでは検出されず静かに壊れる**ことがある |
| `BackgroundWorker` | `DoWork`=別スレッド / `RunWorkerCompleted`=UI スレッド | 完了前にフォームが閉じられて `ObjectDisposedException`、または閉じたフォームのフィールドに書く |
| `async void` ハンドラ | await 後は UI スレッドに戻る | await 中にユーザーが再操作できる（**ボタン連打・状態変更**）。await 前後で this の状態が変わっている |
| `Application.DoEvents()` | その場でメッセージポンプ | 処理中に同じハンドラが**再入**する。進捗表示目的の DoEvents はほぼ確実に再入バグを持つ |

問うこと: 「読んで判定して書く」の間に、別のイベント/スレッドが同じ状態に触れるか?
（→ TOCTOU。触れるなら PHASE 3 で TLA+/interleaving 全列挙）

## 4. DBNull / null / 空文字の三値問題

`category: empty-default` / `cross-boundary` の主戦場。変換の各段で意味が変わる:

- `DataRow["col"]` は null ではなく `DBNull.Value` を返す。`row["col"] as string` → null、
  `Convert.ToString(DBNull.Value)` → `""`、`row["col"].ToString()` → `""`、
  `(string)row["col"]` → **InvalidCastException**
- `Convert.ToInt32(DBNull.Value)` → 例外、`Convert.ToInt32(null)` → **0**（例外ではない!）
- DataSet の型付き列: `row.IsColNull()` を確認しない getter は例外
- 逆方向: `""` を書くか `DBNull.Value` を書くかで DB の NULL 制約・検索条件（`IS NULL` vs `= ''`）
  の挙動が割れる

問うこと: 「DB の NULL / 空文字 / 未入力の3つは、UI→DB→UI の往復で保存されるか?（全単射性）」

## 5. カルチャ・書式・エンコーディング

- `DateTime.Parse` / `double.Parse` / `ToString("d")` はカルチャ依存。
  和暦設定・小数点カンマ環境で挙動が変わる。`CultureInfo` 明示の有無を evidence 化
- `string.Compare` / `ToUpper` のカルチャ依存（トルコ語 I 問題ほか）。
  キー比較に使われていたら `cross-boundary` claim
- ファイル連携の `Encoding` 未指定 → OS 既定（日本語環境なら Shift_JIS のことが多いが保証なし）。
  外部システムとの表現契約として claim 化
- `int` 除算は切り捨て、`int` オーバーフローは既定 unchecked でラップ。
  金額計算の `double` 使用は丸め誤差 claim（ただし確率的でなく決定的なら Z3 で扱える）

## 6. 信頼境界（WinForms 版）

Web と違い「ユーザー入力=フォーム」だけではない。無検証採用の経路を問う:

- `ComboBox` の `DropDown` スタイル: `Text` は**自由入力**。`SelectedItem` が null でも
  `Text` に値がある状態が作れる。どちらを読んでいるかで信頼境界が変わる
- `DataGridView` の編集セル、`Clipboard` からの貼り付け（`MaxLength` を貫通する経路の有無）
- コマンドライン引数、レジストリ、app.config / INI（運用者が書き換えられる = 設定間の矛盾があり得る）
- 共有フォルダのファイル（他プロセス・他ユーザーが書ける → torn read も問う）
- **クライアント側だけの検証**: UI が弾いても、ストアドや別画面・別 EXE が同じテーブルに
  書けるなら不正状態は作れる。DB 制約の有無まで確認して claim 化する

## 7. 状態・活性制御

- `Enabled` / `Visible` / `ReadOnly` の代入箇所を全部集めると、それが**状態機械の遷移表**になる。
  「この状態の組合せは到達可能か」「操作可能なのに前提が壊れている状態はあるか」が
  PHASE 3 の全列挙/TLA+ の invariant になる
- ボタン連打対策の `btn.Enabled = false` は、**Click ハンドラ先頭に置いても
  最初の Disable が効くまでの二重クリックを防げない場合がある**（メッセージキュー済みのクリック）。
  「二重実行され得るか」は ordering claim として必ず立てる
- `Form.ShowDialog` 中も Timer / BackgroundWorker 完了は動く。モーダル=世界停止ではない
