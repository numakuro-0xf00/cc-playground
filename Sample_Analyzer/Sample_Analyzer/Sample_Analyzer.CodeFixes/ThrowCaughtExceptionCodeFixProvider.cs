using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sample_Analyzer
{
    /// <summary>
    /// LEG001 のコードフィックス: `throw ex;` を `throw;` に置換する。
    /// アナライザー側で「catch 変数そのものの再送出かつ再代入なし」を保証済みのため、
    /// この置換はあらゆる文脈で等価変換（元のスタックトレースが保存される）。
    /// 一括修正が目的のルールなので FixAll (BatchFixer) を有効化している。
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ThrowCaughtExceptionCodeFixProvider)), Shared]
    public sealed class ThrowCaughtExceptionCodeFixProvider : CodeFixProvider
    {
        private const string CodeFixTitle = "'throw;' で再送出する（スタックトレースを保存）";

        public sealed override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create(ThrowCaughtExceptionAnalyzer.DiagnosticId);

        public sealed override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostic = context.Diagnostics.First();

            var throwStatement = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent
                .AncestorsAndSelf()
                .OfType<ThrowStatementSyntax>()
                .FirstOrDefault();
            if (throwStatement == null)
            {
                return;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: CodeFixTitle,
                    createChangedDocument: c => ReplaceWithBareRethrowAsync(context.Document, throwStatement, c),
                    equivalenceKey: CodeFixTitle),
                diagnostic);
        }

        private static async Task<Document> ReplaceWithBareRethrowAsync(
            Document document,
            ThrowStatementSyntax throwStatement,
            CancellationToken cancellationToken)
        {
            // `throw;` は C# 1.0 から存在する構文なので、対象プロジェクトの LanguageVersion を問わず安全。
            var bareRethrow = SyntaxFactory.ThrowStatement().WithTriviaFrom(throwStatement);

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var newRoot = root.ReplaceNode(throwStatement, bareRethrow);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
