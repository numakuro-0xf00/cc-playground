# WinForms レガシーでどこを狙うか — 対象選定ガイド

PHASE 0 で使う。バグカタログ（`skills/formal-verification/references/bug-catalog-winforms.md`）の
パターンが WinForms コードベースの**どこに巣を作るか**の地図と、当たりをつける検索クエリ集。

## 狙い目マップ

| コードの様相 | 疑うカタログパターン | 検証手段の目安 |
|---|---|---|
| 判定ロジックがイベントハンドラに散在（Validating / TextChanged / Click / FormClosing） | 矛盾設定、被覆の穴、順序依存 | 全列挙 / Z3 |
| 保存・登録ボタンのガード条件（金額・数量・権限・日付範囲） | 境界の向き、エラー畳み込み×否定 | Z3 / 全列挙 |
| Timer / BackgroundWorker / `async void` / `Control.BeginInvoke` | TOCTOU、判定と記録の分離、staleness | TLA+ / interleaving 全列挙 |
| `Application.DoEvents()` / モーダルダイアログ中の継続処理 | 再入（reentrancy）レース | TLA+ |
| ボタン活性制御・画面遷移（Enabled / Visible / ReadOnly の切替） | 状態遷移の被覆の穴、dead state | 全列挙（状態機械） / TLA+ |
| DataSet / DataTable ↔ DB の読み書き、`DBNull` 処理 | クロス境界契約、staleness、空の意味 | Z3 / 全列挙 |
| app.config / INI / レジストリ / 共有ファイルの設定読み込み | 矛盾設定、冗長・内包、torn read | Z3（実データを直接食わせる） |
| `catch { }` / `catch (Exception) { return false; }` | fail-open/close の非一貫、エラー畳み込み | 全列挙（エラーを入力の一値に昇格） |
| 締め処理・採番・カウンタ・連番発行 | 冪等性、カウンタ不変条件、レース | TLA+ / 全列挙 |
| 二重起動防止（Mutex）・二重クリック対策 | 冪等性、TOCTOU | TLA+ |
| CSV / 固定長ファイル連携、外部システム I/F | 表現契約（単位・書式・エンコーディング・全単射性） | Z3（roundtrip 証明）/ 全列挙 |
| 同種の判定がコピペで複数フォームに存在 | 表現の等価性（リファクタ前後・重複間） | Z3 / 全列挙で等価性検証 |

## 当たりをつける検索クエリ

roslyn-query が使えるなら参照検索・コール階層を優先。Grep で始めるなら:

```
# 並行性の入口
grep -rn "BackgroundWorker\|System.Timers\|Windows.Forms.Timer\|BeginInvoke\|async void\|DoEvents" --include=*.cs

# エラーの畳み込み・fail-open
grep -rn "catch\s*{\s*}\|catch\s*(Exception\|return false;\s*}" --include=*.cs
grep -rn "TryParse" --include=*.cs        # 失敗時に out 値 0/default をそのまま使う箇所

# 信頼境界（ユーザーが自由に書ける値）
grep -rn "\.Text\b\|Clipboard\.\|Environment.GetCommandLineArgs\|ConfigurationManager" --include=*.cs

# クロス境界
grep -rn "DBNull\|Convert\.To\|ToString(\"\|DateTime.Parse\|Encoding\." --include=*.cs

# 状態・活性制御
grep -rn "\.Enabled\s*=\|\.Visible\s*=\|\.ReadOnly\s*=" --include=*.cs

# 冪等性・採番
grep -rn "Max(\|+ 1\b.*番号\|連番\|COUNT(\*)" --include=*.cs
```

ヒット件数ではなく「業務影響 × カタログ一致度」で選ぶ。件数が多いだけの箇所は
custom-analyzer-specialist（アナライザー化）の領分であり、spec-mining の対象は
**判定の正しさ自体が疑わしい/未文書の箇所**。

## target の切り方

1 target = 1つの決定関数 or ひとかたまりの状態遷移。良い例:

- `order-qty-guard` — 注文登録ボタンが通る条件（入力検証の全条件の連言）
- `invoice-numbering` — 請求書番号の採番（並行・リトライ込み）
- `master-sync-staleness` — マスタキャッシュと DB の整合
- `form-close-guard` — 未保存データがあるときのクローズ制御（FormClosing / Validating の順序）

悪い例: `order-form`（フォーム全体 — 大きすぎて predict() が書けない）

## targets.md の記入例

```markdown
# spec-mining 対象領域

## order-qty-guard
- 入口: src/Forms/OrderForm.cs (btnOK_Click, txtQty_Validating), src/Services/OrderService.cs
- 疑うパターン: エラー畳み込み×否定（TryParse）、境界の向き（在庫比較）、被覆の穴
- 根拠: 数量判定が 3 イベントハンドラに分散。TryParse 失敗時の分岐なし（OrderForm.cs:148）
- 優先度: 高（受注登録は業務影響大）
```
