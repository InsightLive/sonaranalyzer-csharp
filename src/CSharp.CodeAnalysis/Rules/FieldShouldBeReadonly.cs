﻿/*
 * SonarQube C# Code Analysis
 * Copyright (C) 2015 SonarSource
 * dev@sonar.codehaus.org
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
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SonarQube.CSharp.CodeAnalysis.Helpers;
using SonarQube.CSharp.CodeAnalysis.SonarQube.Settings;
using SonarQube.CSharp.CodeAnalysis.SonarQube.Settings.Sqale;

namespace SonarQube.CSharp.CodeAnalysis.Rules
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    [SqaleConstantRemediation("2min")]
    [SqaleSubCharacteristic(SqaleSubCharacteristic.Understandability)]
    [Rule(DiagnosticId, RuleSeverity, Description, IsActivatedByDefault)]
    [Tags("confusing")]
    public class FieldShouldBeReadonly : DiagnosticAnalyzer
    {
        internal const string DiagnosticId = "S2933";
        internal const string Description = "Fields that are only assigned in the constructor should be \"readonly\"";
        internal const string MessageFormat = "Make \"{0}\" \"readonly\".";
        internal const string Category = "SonarQube";
        internal const Severity RuleSeverity = Severity.Major;
        internal const bool IsActivatedByDefault = true;

        internal static readonly DiagnosticDescriptor Rule = 
            new DiagnosticDescriptor(DiagnosticId, Description, MessageFormat, Category, 
                RuleSeverity.ToDiagnosticSeverity(), IsActivatedByDefault,
                helpLinkUri: "http://nemo.sonarqube.org/coding_rules#rule_key=csharpsquid%3AS2933");
        
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterCompilationStartAction(analysisContext =>
            {
                var candidateFields = ImmutableHashSet<IFieldSymbol>.Empty;
                var assignedAsReadonly = ImmutableHashSet<IFieldSymbol>.Empty;
                var nonCandidateFields = ImmutableHashSet<IFieldSymbol>.Empty;

                analysisContext.RegisterSyntaxNodeAction(c =>
                {
                    var fieldDeclaration = (FieldDeclarationSyntax) c.Node;

                    foreach (var field in fieldDeclaration.Declaration.Variables
                        .Select(variableDeclaratorSyntax => new
                        {
                            Syntax = variableDeclaratorSyntax,
                            Symbol = c.SemanticModel.GetDeclaredSymbol(variableDeclaratorSyntax) as IFieldSymbol
                        })
                        .Where(f => f.Symbol != null)
                        .Where(f => FieldIsRelevant(f.Symbol)))
                    {
                        candidateFields = candidateFields.Add(field.Symbol);

                        if (field.Syntax.Initializer != null)
                        {
                            assignedAsReadonly = assignedAsReadonly.Add(field.Symbol);
                        }
                    }
                }, SyntaxKind.FieldDeclaration);

                analysisContext.RegisterSyntaxNodeAction(
                    c =>
                    {
                        var assignment = (AssignmentExpressionSyntax) c.Node;
                        var expression = assignment.Left;

                        ProcessExpressionChange(expression, c.SemanticModel, ref nonCandidateFields, ref assignedAsReadonly);
                    },
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxKind.AddAssignmentExpression,
                    SyntaxKind.SubtractAssignmentExpression,
                    SyntaxKind.MultiplyAssignmentExpression,
                    SyntaxKind.DivideAssignmentExpression,
                    SyntaxKind.ModuloAssignmentExpression,
                    SyntaxKind.AndAssignmentExpression,
                    SyntaxKind.ExclusiveOrAssignmentExpression,
                    SyntaxKind.OrAssignmentExpression,
                    SyntaxKind.LeftShiftAssignmentExpression,
                    SyntaxKind.RightShiftAssignmentExpression);

                analysisContext.RegisterSyntaxNodeAction(
                    c =>
                    {
                        var unary = (PrefixUnaryExpressionSyntax)c.Node;
                        var expression = unary.Operand;

                        ProcessExpressionChange(expression, c.SemanticModel, ref nonCandidateFields, ref assignedAsReadonly);
                    },
                    SyntaxKind.PreDecrementExpression, 
                    SyntaxKind.PreIncrementExpression);

                analysisContext.RegisterSyntaxNodeAction(
                    c =>
                    {
                        var unary = (PostfixUnaryExpressionSyntax)c.Node;
                        var expression = unary.Operand;

                        ProcessExpressionChange(expression, c.SemanticModel, ref nonCandidateFields, ref assignedAsReadonly);
                    },
                    SyntaxKind.PostDecrementExpression,
                    SyntaxKind.PostIncrementExpression);

                analysisContext.RegisterSyntaxNodeAction(c =>
                {
                    var argument = (ArgumentSyntax) c.Node;
                    if (argument.RefOrOutKeyword.IsKind(SyntaxKind.None))
                    {
                        return;
                    }

                    var fieldSymbol = c.SemanticModel.GetSymbolInfo(argument.Expression).Symbol as IFieldSymbol;
                    if (FieldIsRelevant(fieldSymbol))
                    {
                        nonCandidateFields = nonCandidateFields.Add(fieldSymbol);
                    }
                }, SyntaxKind.Argument);

                analysisContext.RegisterCompilationEndAction(c =>
                {
                    var fields = candidateFields.Except(nonCandidateFields);
                    fields = fields.Intersect(assignedAsReadonly);
                    foreach (var field in fields)
                    {
                        var declarationReference = field.DeclaringSyntaxReferences.FirstOrDefault();
                        if (declarationReference == null)
                        {
                            continue;
                        }
                        var fieldSyntax = declarationReference.GetSyntax() as VariableDeclaratorSyntax;
                        if (fieldSyntax == null)
                        {
                            continue;
                        }

                        c.ReportDiagnostic(Diagnostic.Create(Rule, fieldSyntax.Identifier.GetLocation(), fieldSyntax.Identifier.ValueText));
                    }
                });
            });
        }

        private static void ProcessExpressionChange(ExpressionSyntax expression, SemanticModel semanticModel,
            ref ImmutableHashSet<IFieldSymbol> nonCandidateFields, ref ImmutableHashSet<IFieldSymbol> assignedAsReadonly)
        {
            var fieldSymbol = semanticModel.GetSymbolInfo(expression).Symbol as IFieldSymbol;
            if (fieldSymbol== null || !FieldIsRelevant(fieldSymbol))
            {
                return;
            }

            var constructorSymbol = semanticModel.GetEnclosingSymbol(expression.SpanStart) as IMethodSymbol;
            if (constructorSymbol == null)
            {
                nonCandidateFields = nonCandidateFields.Add(fieldSymbol);
                return;
            }

            if (constructorSymbol.MethodKind == MethodKind.Constructor && 
                constructorSymbol.ContainingType.Equals(fieldSymbol.ContainingType))
            {
                assignedAsReadonly = assignedAsReadonly.Add(fieldSymbol);
            }
            else
            {
                nonCandidateFields = nonCandidateFields.Add(fieldSymbol);
            }
        }

        private static bool FieldIsRelevant(IFieldSymbol fieldSymbol)
        {
            return fieldSymbol != null &&
                   !fieldSymbol.IsStatic &&
                   !fieldSymbol.IsConst &&
                   !fieldSymbol.IsReadOnly &&
                   fieldSymbol.DeclaredAccessibility == Accessibility.Private;
        }
    }
}