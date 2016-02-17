﻿/*
 * SonarLint for Visual Studio
 * Copyright (C) 2015-2016 SonarSource SA
 * mailto:contact@sonarsource.com
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
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SonarLint.Common;
using SonarLint.Common.Sqale;
using SonarLint.Helpers;
using System.Linq;

namespace SonarLint.Rules.CSharp
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    [SqaleConstantRemediation("2min")]
    [SqaleSubCharacteristic(SqaleSubCharacteristic.Understandability)]
    [Rule(DiagnosticId, RuleSeverity, Title, IsActivatedByDefault)]
    [Tags(Tag.Suspicious)]
    public class OptionalParameterWithDefaultValue : SonarDiagnosticAnalyzer
    {
        internal const string DiagnosticId = "S3451";
        internal const string Title = "\"[DefaultValue]\" should not be used when \"[DefaultParameterValue]\" is meant";
        internal const string Description =
            "The use of \"[DefaultValue]\" with \"[Optional]\" has no more effect than \"[Optional]\" alone. That's because \"[DefaultValue]\" doesn't " +
            "actually do anything; it merely indicates the intent for the value. More than likely, \"[DefaultValue]\" was used in confusion instead of " +
            "\"[DefaultParameterValue]\".";
        internal const string MessageFormat = "Use \"[DefaultParameterValue]\" instead.";
        internal const string Category = SonarLint.Common.Category.Maintainability;
        internal const Severity RuleSeverity = Severity.Major;
        internal const bool IsActivatedByDefault = true;

        internal static readonly DiagnosticDescriptor Rule =
            new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category,
                RuleSeverity.ToDiagnosticSeverity(), IsActivatedByDefault,
                helpLinkUri: DiagnosticId.GetHelpLink(),
                description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        protected override void Initialize(SonarAnalysisContext context)
        {
            context.RegisterSyntaxNodeActionInNonGenerated(
                c =>
                {
                    var parameter = (ParameterSyntax)c.Node;
                    if (!parameter.AttributeLists.Any())
                    {
                        return;
                    }

                    var attributes = AttributeSyntaxSymbolMapping.GetAttributesForParameter(parameter, c.SemanticModel).ToList();

                    var hasNoOptional = attributes.All(attr => !attr.Symbol.IsInType(KnownType.System_Runtime_InteropServices_OptionalAttribute));
                    if (hasNoOptional)
                    {
                        return;
                    }

                    var hasDefaultParameterValue = attributes.Any(attr =>
                        attr.Symbol.IsInType(KnownType.System_Runtime_InteropServices_DefaultParameterValueAttribute));
                    if (hasDefaultParameterValue)
                    {
                        return;
                    }

                    var defaultValueAttribute = attributes
                        .FirstOrDefault(a => a.Symbol.IsInType(KnownType.System_ComponentModel_DefaultValueAttribute));

                    if (defaultValueAttribute != null)
                    {
                        c.ReportDiagnostic(Diagnostic.Create(Rule, defaultValueAttribute.SyntaxNode.GetLocation()));
                    }
                },
                SyntaxKind.Parameter);
        }
    }
}
