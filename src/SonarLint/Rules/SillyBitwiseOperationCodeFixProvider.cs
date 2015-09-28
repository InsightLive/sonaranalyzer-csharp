﻿/*
 * SonarLint for Visual Studio
 * Copyright (C) 2015 SonarSource
 * sonarqube@googlegroups.com
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 3 of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with this program; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02
 */

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using SonarLint.Common;

namespace SonarLint.Rules
{
    [ExportCodeFixProvider(LanguageNames.CSharp)]
    public class SillyBitwiseOperationCodeFixProvider : CodeFixProvider
    {
        internal const string Title = "Remove bitwise operation";
        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get
            {
                return ImmutableArray.Create(SillyBitwiseOperation.DiagnosticId);
            }
        }

        private static FixAllProvider FixAllProviderInstance = new DocumentBasedFixAllProvider<SillyBitwiseOperation>(
            Title,
            (root, node, diagnostic) => CalculateNewRoot(root, node, diagnostic));

        public sealed override FixAllProvider GetFixAllProvider()
        {
            return FixAllProviderInstance;
        }

        public override sealed async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var syntaxNode = root.FindNode(diagnosticSpan);

            var statement = syntaxNode as StatementSyntax;
            var assignment = syntaxNode as AssignmentExpressionSyntax;
            var binary = syntaxNode as BinaryExpressionSyntax;
            if (statement != null ||
                assignment != null ||
                binary != null)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        Title,
                        c =>
                        {
                            var newRoot = CalculateNewRoot(root, diagnostic, statement, assignment, binary);
                            return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                        }),
                    context.Diagnostics);
            }
        }

        private static SyntaxNode CalculateNewRoot(SyntaxNode root, SyntaxNode current, Diagnostic diagnostic)
        {
            var statement = current as StatementSyntax;
            var assignment = current as AssignmentExpressionSyntax;
            var binary = current as BinaryExpressionSyntax;
            if (statement == null &&
                assignment == null &&
                binary == null)
            {
                return root;
            }

            return CalculateNewRoot(root, diagnostic, statement, assignment, binary);
        }

        private static SyntaxNode CalculateNewRoot(SyntaxNode root, Diagnostic diagnostic,
            StatementSyntax currentAsStatement, AssignmentExpressionSyntax currentAsAssignment,
            BinaryExpressionSyntax currentAsBinary)
        {
            if (currentAsStatement != null)
            {
                return root.RemoveNode(currentAsStatement, SyntaxRemoveOptions.KeepNoTrivia);
            }

            if (currentAsAssignment != null)
            {
                return root.ReplaceNode(
                    currentAsAssignment,
                    currentAsAssignment.Left.WithAdditionalAnnotations(Formatter.Annotation));
            }

            var isReportingOnLeft = bool.Parse(diagnostic.Properties[SillyBitwiseOperation.IsReportingOnLeftKey]);
            return root.ReplaceNode(
                currentAsBinary,
                (isReportingOnLeft ? currentAsBinary.Right : currentAsBinary.Left).WithAdditionalAnnotations(Formatter.Annotation));
        }
    }
}

