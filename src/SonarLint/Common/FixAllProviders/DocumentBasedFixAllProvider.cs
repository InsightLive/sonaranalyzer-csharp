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

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.CodeAnalysis.CodeActions;
using System.Collections.Generic;
using System;

namespace SonarLint.Common
{
    internal class DocumentBasedFixAllProvider : FixAllProvider
    {
        #region Singleton implementation

        private static readonly DocumentBasedFixAllProvider instance = new DocumentBasedFixAllProvider();

        // Explicit static constructor to tell C# compiler
        // not to mark type as beforefieldinit
        static DocumentBasedFixAllProvider()
        {
        }

        private DocumentBasedFixAllProvider()
        {
        }

        public static DocumentBasedFixAllProvider Instance => instance;

        #endregion

        private const string titleSolutionPattern = "Fix all '{0}' in Solution";
        private const string titleScopePattern = "Fix all '{0}' in '{1}'";
        private const string titleFixAll = "Fix all '{0}'";

        private static string GetFixAllTitle(FixAllContext fixAllContext)
        {
            var diagnosticIds = fixAllContext.DiagnosticIds;
            string diagnosticId;
            diagnosticId = diagnosticIds.Count() == 1
                ? diagnosticIds.Single()
                : string.Join(",", diagnosticIds.ToArray());

            switch (fixAllContext.Scope)
            {
                case FixAllScope.Document:
                    return string.Format(titleScopePattern, diagnosticId, fixAllContext.Document.Name);
                case FixAllScope.Project:
                    return string.Format(titleScopePattern, diagnosticId, fixAllContext.Project.Name);
                case FixAllScope.Solution:
                    return string.Format(titleSolutionPattern, diagnosticId);
                default:
                    return titleFixAll;
            }
        }

        public override Task<CodeAction> GetFixAsync(FixAllContext fixAllContext)
        {
            var title = GetFixAllTitle(fixAllContext);

            switch (fixAllContext.Scope)
            {
                case FixAllScope.Document:
                    return Task.FromResult(CodeAction.Create(title,
                        async ct => fixAllContext.Document.WithSyntaxRoot(
                            await GetFixedDocument(fixAllContext, fixAllContext.Document).ConfigureAwait(false))));
                case FixAllScope.Project:
                    return Task.FromResult(CodeAction.Create(title,
                        ct => GetFixedProject(fixAllContext, fixAllContext.Project)));
                case FixAllScope.Solution:
                    return Task.FromResult(CodeAction.Create(title,
                        ct => GetFixedSolution(fixAllContext)));
                default:
                    return null;
            }
        }

        private static async Task<Solution> GetFixedSolution(FixAllContext fixAllContext)
        {
            var newSolution = fixAllContext.Solution;
            foreach (var projectId in newSolution.ProjectIds)
            {
                newSolution = await GetFixedProject(fixAllContext, newSolution.GetProject(projectId))
                    .ConfigureAwait(false);
            }
            return newSolution;
        }

        private static async Task<Solution> GetFixedProject(FixAllContext fixAllContext, Project project)
        {
            var solution = project.Solution;
            var newDocuments = project.Documents.ToDictionary(d => d.Id, d => GetFixedDocument(fixAllContext, d));
            await Task.WhenAll(newDocuments.Values).ConfigureAwait(false);
            foreach (var newDoc in newDocuments)
            {
                solution = solution.WithDocumentSyntaxRoot(newDoc.Key, newDoc.Value.Result);
            }
            return solution;
        }

        private static async Task<SyntaxNode> GetFixedDocument(FixAllContext fixAllContext, Document document)
        {
            var annotationKind = Guid.NewGuid().ToString();

            var diagnostics = await fixAllContext.GetDocumentDiagnosticsAsync(document).ConfigureAwait(false);
            var root = await document.GetSyntaxRootAsync(fixAllContext.CancellationToken).ConfigureAwait(false);
            var elementDiagnosticPairs = diagnostics
                .Select(d => new KeyValuePair<SyntaxNodeOrToken, Diagnostic>(GetReportedElement(d, root), d))
                .Where(n => !n.Key.IsMissing)
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            var diagnosticAnnotationPairs = new BidirectionalDictionary<Diagnostic, SyntaxAnnotation>();
            CreateAnnotationForDiagnostics(diagnostics, annotationKind, diagnosticAnnotationPairs);
            root = GetRootWithAnnotatedElements(root, elementDiagnosticPairs, diagnosticAnnotationPairs);

            var currentDocument = document.WithSyntaxRoot(root);
            var annotatedElements = root.GetAnnotatedNodesAndTokens(annotationKind);

            while(annotatedElements.Any())
            {
                var element = annotatedElements.First();
                var annotation = element.GetAnnotations(annotationKind).First();
                var diagnostic = diagnosticAnnotationPairs.GetByB(annotation);
                var location = root.GetAnnotatedNodesAndTokens(annotation).FirstOrDefault().GetLocation();
                if (location == null)
                {
                    //annotation is already removed from the tree
                    continue;
                }

                var newDiagnostic = Diagnostic.Create(
                    diagnostic.Descriptor,
                    location,
                    diagnostic.AdditionalLocations,
                    diagnostic.Properties);

                var fixes = new List<CodeAction>();
                var context = new CodeFixContext(currentDocument, newDiagnostic, (a, d) =>
                {
                    lock (fixes)
                    {
                        fixes.Add(a);
                    }
                }, fixAllContext.CancellationToken);
                await fixAllContext.CodeFixProvider.RegisterCodeFixesAsync(context).ConfigureAwait(false);

                var action = fixes.FirstOrDefault(fix => fix.EquivalenceKey == fixAllContext.CodeActionEquivalenceKey);
                if (action != null)
                {
                    var operations = await action.GetOperationsAsync(fixAllContext.CancellationToken);
                    var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
                    currentDocument = solution.GetDocument(document.Id);
                    root = await currentDocument.GetSyntaxRootAsync(fixAllContext.CancellationToken).ConfigureAwait(false);
                }
                root = RemoveAnnotationIfExists(root, annotation);
                currentDocument = document.WithSyntaxRoot(root);
                annotatedElements = root.GetAnnotatedNodesAndTokens(annotationKind);
            }

            return await currentDocument.GetSyntaxRootAsync(fixAllContext.CancellationToken).ConfigureAwait(false);
        }

        private static SyntaxNodeOrToken GetReportedElement(Diagnostic d, SyntaxNode root)
        {
            var token = root.FindToken(d.Location.SourceSpan.Start);
            var exactMatch = token.Span == d.Location.SourceSpan;
            return exactMatch
                ? (SyntaxNodeOrToken)token
                : root.FindNode(d.Location.SourceSpan, getInnermostNodeForTie: true);
        }

        private static SyntaxNode RemoveAnnotationIfExists(SyntaxNode root, SyntaxAnnotation annotation)
        {
            var elements = root.GetAnnotatedNodesAndTokens(annotation);
            if (!elements.Any())
            {
                return root;
            }

            var element = elements.First();

            if (element.IsNode)
            {
                var node = element.AsNode();
                return root.ReplaceNode(
                    node,
                    node.WithoutAnnotations(annotation));
            }

            var token = element.AsToken();
            return root.ReplaceToken(
                token,
                token.WithoutAnnotations(annotation));
        }

        private static SyntaxNode GetRootWithAnnotatedElements(SyntaxNode root,
            Dictionary<SyntaxNodeOrToken, Diagnostic> elementDiagnosticPairs,
            BidirectionalDictionary<Diagnostic, SyntaxAnnotation> diagnosticAnnotationPairs)
        {
            var nodes = elementDiagnosticPairs.Keys.Where(k => k.IsNode).Select(k => k.AsNode());
            var tokens = elementDiagnosticPairs.Keys.Where(k => k.IsToken).Select(k => k.AsToken());

            return root.ReplaceSyntax(
                nodes,
                (original, rewritten) =>
                {
                    var annotation = diagnosticAnnotationPairs.GetByA(elementDiagnosticPairs[original]);
                    return rewritten.WithAdditionalAnnotations(annotation);
                },
                tokens,
                (original, rewritten) =>
                {
                    var annotation = diagnosticAnnotationPairs.GetByA(elementDiagnosticPairs[original]);
                    return rewritten.WithAdditionalAnnotations(annotation);
                },
                null, null);
        }

        private static void CreateAnnotationForDiagnostics(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics,
            string annotationKind,
            BidirectionalDictionary<Diagnostic, SyntaxAnnotation> diagnosticAnnotationPairs)
        {
            foreach (var diagnostic in diagnostics)
            {
                diagnosticAnnotationPairs.Add(diagnostic, new SyntaxAnnotation(annotationKind));
            }
        }
    }
}

