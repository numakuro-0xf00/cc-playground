# コードフィックス実装パターン

## Triviaの扱い方（重要）

Triviaとはコンパイラが意味解釈に使わない要素（空白・インデント・改行・コメント）。
コードフィックスでノードを追加・置換するとき、Triviaを正しく移動しないとインデントが崩れる。

### パターン: 先頭にキーワードを追加する

```csharp
// 例: int x = 0; → const int x = 0;

// ① 既存の先頭トークンからTriviaを取り出す
SyntaxToken firstToken = localDeclaration.GetFirstToken();
SyntaxTriviaList leadingTrivia = firstToken.LeadingTrivia;

// ② 先頭トークンのTriviaを空にした新しい宣言を作る
LocalDeclarationStatementSyntax trimmedLocal = localDeclaration.ReplaceToken(
    firstToken,
    firstToken.WithLeadingTrivia(SyntaxTriviaList.Empty)
);

// ③ Triviaを持つ const トークンを作る
SyntaxToken constToken = SyntaxFactory.Token(
    leadingTrivia,
    SyntaxKind.ConstKeyword,
    SyntaxFactory.TriviaList(SyntaxFactory.ElasticMarker) // 後ろにスペース
);

// ④ modifiers の先頭に const を挿入
SyntaxTokenList newModifiers = trimmedLocal.Modifiers.Insert(0, constToken);
LocalDeclarationStatementSyntax newLocal = trimmedLocal
    .WithModifiers(newModifiers)
    .WithDeclaration(localDeclaration.Declaration);
```

### パターン: ノードを置換する

```csharp
// フォーマットアノテーションを付ける（C#スタイルに自動整形）
using Microsoft.CodeAnalysis.Formatting;

var formattedNode = newLocal.WithAdditionalAnnotations(Formatter.Annotation);

// ドキュメント全体のルートを取得して置換
SyntaxNode oldRoot = await document.GetSyntaxRootAsync(cancellationToken);
SyntaxNode newRoot = oldRoot.ReplaceNode(localDeclaration, formattedNode);

// 新しいドキュメントを返す
return document.WithSyntaxRoot(newRoot);
```

### パターン: 名前を変更する（リネーム）

```csharp
// シンボルのリネームはRenamer APIを使う
// （単純なトークン置換だと参照箇所が更新されないため）
var newSolution = await Renamer.RenameSymbolAsync(
    document.Project.Solution,
    typeSymbol,
    newName,
    solution.Options,
    cancellationToken
);
return newSolution; // Document ではなく Solution を返す点に注意
```

## createChangedDocument vs createChangedSolution

| 使う場面 | 返り値 |
|---|---|
| 単一ファイル内の変更（追加・削除・置換） | `Task<Document>` |
| リネームなど複数ファイルにまたがる変更 | `Task<Solution>` |

```csharp
// Document を返す場合
context.RegisterCodeFix(
    CodeAction.Create(
        title: "Fix title",
        createChangedDocument: c => ApplyFixAsync(document, node, c),
        equivalenceKey: "key"),
    diagnostic);

// Solution を返す場合
context.RegisterCodeFix(
    CodeAction.Create(
        title: "Fix title",
        createChangedSolution: c => ApplyRenameAsync(document, node, c),
        equivalenceKey: "key"),
    diagnostic);
```
