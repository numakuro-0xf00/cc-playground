# .NET Framework レガシーコード頻出パターンカタログ

調査（PHASE 1）の観点および、ルール設計（PHASE 2）の出発点として使う。
各パターンに: 破られている不変条件 / 検出戦略 / 分類（A=機械修正可, B=検出のみ, C=ルール化不適）/ 誤検知の注意。

調査用 Grep パターンはあくまで「当たりをつける」ため。アナライザー実装は必ず構文/意味レベルで行う。

## 一覧

| # | パターン | 分類 | 調査用Grep |
|---|---|---|---|
| 1 | 空 catch による例外の握りつぶし | B | `catch.*\{\s*\}` |
| 2 | `throw ex;` によるスタックトレース破壊 | A | `throw [a-z]` |
| 3 | SQL 文字列の動的組み立て | B | `CommandText`, `"SELECT`, `"UPDATE` |
| 4 | IDisposable の Dispose 漏れ | B | `new SqlConnection`, `new StreamReader` |
| 5 | 非ジェネリックコレクション | B(一部A) | `ArrayList`, `Hashtable` |
| 6 | `DateTime.Now` の直接使用 | B | `DateTime.Now` |
| 7 | カルチャ未指定の文字列操作 | A(多くは) | `ToUpper()`, `ToLower()`, `.ToString()` |
| 8 | `async void` メソッド | B | `async void` |
| 9 | static 可変状態 | B | `public static` フィールド |
| 10 | God メソッド / 巨大クラス | C | （メトリクス） |

## 詳細

### 1. 空 catch（例外の握りつぶし）
- **不変条件**: 例外は観測可能であること（ログ・再送出・意図の明示コメントのいずれか）
- **検出**: `RegisterSyntaxNodeAction(SyntaxKind.CatchClause)` — Block.Statements が空、かつコメントもない
- **分類 B の理由**: 正しい修正（ログ？再送出？握りつぶしが正解？）は文脈依存。
  「コメントすらない空 catch」だけを検出対象にすると誤検知が激減する
- **誤検知注意**: `// 意図的に無視: <理由>` コメント付きは許容する設計にする

### 2. `throw ex;`（スタックトレース破壊）
- **不変条件**: 再送出は元のスタックトレースを保存すること
- **検出**: `SyntaxKind.ThrowStatement` — 式が catch 句の例外変数そのものの識別子
- **分類 A**: `throw ex;` → `throw;` は catch 変数を投げ直す場合に限り常に等価変換。FixAll 対応可
- **誤検知注意**: `throw new WrapperException(..., ex)` は対象外。ラップは正当

### 3. SQL 文字列の動的組み立て
- **不変条件**: SQL への外部入力はパラメータ化されること
- **検出**: `CommandText` への代入・`SqlCommand`/`OleDbCommand` コンストラクタ第1引数について、
  `SemanticModel.GetConstantValue` が失敗する式を検出（連結・Format・補間をまとめて捕捉できる）
- **分類 B**: パラメータ化への書き換えは引数設計が要るため人間の仕事。severity は高め（warning〜error）に置く価値がある

### 4. IDisposable の Dispose 漏れ
- **不変条件**: ローカルに生成した IDisposable はスコープ内で破棄されること
- **検出**: ローカル変数への `ObjectCreationExpression` で型が IDisposable 実装、かつ using 文でも
  try-finally Dispose でもない。厳密にやるならフロー解析だが、初版は「using でないローカル生成」に限定し
  「メソッド外へ返す／フィールドへ代入」は早期リターンで除外する
- **分類 B**（using 化はスコープ変更を伴い挙動に影響しうる。単純なケースに限れば A も可能）
- **誤検知注意**: 戻り値として返すファクトリ、フィールド保持（ライフサイクル管理が別にある）、
  `MemoryStream` など Dispose 不要と分かっている型を除外リスト化するか初版から検討

### 5. 非ジェネリックコレクション（ArrayList / Hashtable）
- **不変条件**: コレクションは要素型を静的に表明すること
- **検出**: 型シンボルが `System.Collections.ArrayList` 等の変数宣言・フィールド・引数（意味レベル。エイリアス対策）
- **分類**: 検出は容易（B）。ローカル変数で挿入型が単一と証明できる場合のみ CodeFix（A）を出す
- **誤検知注意**: 公開 API のシグネチャに現れる場合、修正は破壊的変更。フィールド/引数は検出のみに留める

### 6. `DateTime.Now`
- **不変条件**: 時刻取得は集約点（IClock 等）を経由する、または UTC を使うこと
- **検出**: `SyntaxKind.SimpleMemberAccessExpression` でシンボルが `System.DateTime.Now`
- **分類 B**: `UtcNow` への置換は挙動変更（既存データ・表示との整合）。自動修正してはならない
- **誤検知注意**: ログ出力・画面表示用途は許容する運用も多い。チームの方針を先に確認

### 7. カルチャ未指定の文字列操作
- **不変条件**: 文字列変換・比較はカルチャを明示すること
- **検出**: `ToUpper()`/`ToLower()`/引数なし `ToString()`（IFormattable 実装型）/ `string.Compare` の
  culture 引数なしオーバーロード呼び出し（シンボルのオーバーロード解決で判定）
- **分類 A（多くは）**: `ToUpperInvariant()` や `ToString(CultureInfo.InvariantCulture)` への置換。
  ただし**表示用文字列は CurrentCulture が正しい**ので、シリアライズ/キー生成の文脈に限定するか B に落とす判断もある

### 8. `async void`
- **不変条件**: 非同期メソッドの例外は呼び出し元から観測可能であること（イベントハンドラを除く）
- **検出**: `MethodDeclaration` — async 修飾子 + void 戻り値。イベントハンドラシグネチャ
  `(object, EventArgs派生)` は除外
- **分類 A**: `async Task` への変更 + 呼び出し側の追跡が必要なため、実際は B 寄り。宣言だけ直すと
  呼び出し側で await されず警告が出る。初版は検出のみを推奨

### 9. static 可変状態
- **不変条件**: グローバル状態は変更不能であるか、同期が保証されること
- **検出**: `SymbolKind.Field` — static かつ非 readonly かつ public/internal
- **分類 B**: readonly 化・DI 化は設計判断

### 10. God メソッド / 巨大クラス
- **分類 C**: 閾値が恣意的で、機械的な分割は不可能。アナライザー化するなら info 止まり。
  むしろ「他のルールの違反密度が高いファイル」として改善対象の優先順位付けに使う

## このカタログの使い方

これは出発点であって網羅ではない。**対象コードベース固有のパターン**
（社内フレームワークの誤用、コピペされた独自イディオム）のほうが価値が高いことが多い。
PHASE 1 の調査では、このカタログの確認と並行して「このコードベースで繰り返されている固有の形」を探すこと。
