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

namespace SonarLint.RuleDescriptors
{
    public class SqaleRemediationProperty
    {
        internal const string RemediationFunctionKey = "remediationFunction";
        internal const string ConstantRemediationFunctionValue = "CONSTANT_ISSUE";
        internal const string OffsetKey = "offset";

        public string Key { get; set; }
        public string Text { get; set; }
        public string Value { get; set; }
    }
}