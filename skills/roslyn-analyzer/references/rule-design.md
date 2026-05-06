# ルール設計ガイド

## SyntaxKind の選び方

よく使う SyntaxKind 一覧：

| SyntaxKind | 対象コード例 |
|---|---|
| `LocalDeclarationStatement` | `int x = 0;` |
| `MethodDeclaration` | `void Foo() {}` |
| `ClassDeclaration` | `class MyClass {}` |
| `InvocationExpression` | `Console.WriteLine()` |
| `IfStatement` | `if (x) {}` |
| `ReturnStatement` | `return x;` |
| `ObjectCreationExpression` | `new Foo()` |
| `PropertyDeclaration` | `public int X { get; set; }` |
| `ParameterList` | メソッドの引数リスト |

Syntax Visualizer（Visual Studio拡張）を使うと、コードの構文ツリーを視覚的に確認できる。

## Syntax解析 vs Semantic解析

### Syntax解析だけで済むケース
- キーワードの有無チェック（`const`, `static`, `readonly` など）
- 命名規則チェック（大文字小文字など）
- 構文パターンの検出（`if (x == null)` など）

### Semantic解析が必要なケース
- 型情報が必要（`int` かどうか、インターフェースを実装しているかなど）
- データフロー解析（変数が後で書き換えられるか）
- コンパイル時定数かどうかの確認
- シンボルの参照関係

## 設計のコツ

### 早期リターンで高速化
アナライザーはコード編集のたびに呼ばれるため、できるだけ早くリターンすることが重要。

```csharp
private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
{
    var node = (LocalDeclarationStatementSyntax)context.Node;

    // 最も単純な条件を先にチェック（コストが低い順）
    if (node.Modifiers.Any(SyntaxKind.ConstKeyword)) return;  // ① Syntax
    if (node.Declaration.Variables.Count != 1) return;         // ② Syntax
    // Semantic解析は後回し（コストが高い）
    var model = context.SemanticModel;
    ...
}
```

### よくある設計パターン

**パターン1: キーワード強制**
「このキーワードを使うべき場所で使われていない」を検出

**パターン2: 命名規則**
「名前がXXXで始まっていない/終わっていない」を検出

**パターン3: 禁止パターン**
「このAPIを使ってはいけない」を検出

**パターン4: 複雑度チェック**
「メソッドの行数が多すぎる」「ネストが深すぎる」を検出
