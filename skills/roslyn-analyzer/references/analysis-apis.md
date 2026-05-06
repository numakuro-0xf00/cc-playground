# Roslyn 解析API リファレンス

## SemanticModel の主要メソッド

### GetConstantValue
初期化子がコンパイル時定数かどうかを確認する。

```csharp
Optional<object> constantValue = context.SemanticModel
    .GetConstantValue(initializer.Value, context.CancellationToken);

if (!constantValue.HasValue)
{
    return; // コンパイル時定数でない → constにできない
}
```

### AnalyzeDataFlow
変数がスコープ外で書き換えられるかどうかを確認する。

```csharp
DataFlowAnalysis dataFlow = context.SemanticModel
    .AnalyzeDataFlow(localDeclaration);

ISymbol symbol = context.SemanticModel
    .GetDeclaredSymbol(variable, context.CancellationToken);

if (dataFlow.WrittenOutside.Contains(symbol))
{
    return; // 外で書き換えられる → constにできない
}
```

### GetDeclaredSymbol
宣言からシンボル情報を取得する。

```csharp
ISymbol symbol = context.SemanticModel
    .GetDeclaredSymbol(variableDeclarator, context.CancellationToken);

INamedTypeSymbol typeSymbol = context.SemanticModel
    .GetDeclaredSymbol(classDeclaration, context.CancellationToken);
```

### GetTypeInfo
式の型情報を取得する。

```csharp
TypeInfo typeInfo = context.SemanticModel.GetTypeInfo(expression);
ITypeSymbol type = typeInfo.Type;

if (type.SpecialType == SpecialType.System_String)
{
    // string型の場合
}
```

### GetSymbolInfo
式が参照しているシンボルを取得する（メソッド呼び出しなど）。

```csharp
SymbolInfo symbolInfo = context.SemanticModel.GetSymbolInfo(invocationExpression);
IMethodSymbol method = symbolInfo.Symbol as IMethodSymbol;
```

## DataFlowAnalysis のプロパティ

| プロパティ | 意味 |
|---|---|
| `WrittenInside` | スコープ内で書き込まれる変数 |
| `WrittenOutside` | スコープ外で書き込まれる変数 |
| `ReadInside` | スコープ内で読み込まれる変数 |
| `AlwaysAssigned` | 必ず代入される変数 |
| `Captured` | クロージャにキャプチャされる変数 |

## SyntaxNode の便利メソッド

```csharp
// 最初のトークンを取得
SyntaxToken firstToken = node.GetFirstToken();

// 子ノードを型でフィルタ
var variables = localDeclaration.Declaration.Variables;

// 親ノードを型で探す
var methodDecl = node.AncestorsAndSelf()
    .OfType<MethodDeclarationSyntax>().FirstOrDefault();

// 場所情報
Location location = node.GetLocation();
```

## SyntaxFactory でノードを作る

```csharp
// キーワードトークンの作成（Triviaを付ける）
SyntaxToken constToken = SyntaxFactory.Token(
    leadingTrivia,           // インデントなど
    SyntaxKind.ConstKeyword,
    SyntaxFactory.TriviaList(SyntaxFactory.ElasticMarker) // 末尾スペース
);

// 識別子トークンの作成
SyntaxToken identifier = SyntaxFactory.Identifier("newName");
```
