// Copyright (C) 2026 Bharath K A (bkumarbk9)
// SPDX-License-Identifier: AGPL-3.0-only
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License version 3
// as published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace BK_CloudSpreadsheet.Sheets;

/// <summary>
/// Sheet names come from the browser and become file names, so only a safe set is accepted:
/// 1-64 letters, digits, "-" and "_", starting with a letter or digit. Names are case-insensitive.
/// </summary>
public static partial class SheetName
{
    public const string Rule = "1-64 letters, digits, '-' or '_', starting with a letter or digit";

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidName();

    public static bool IsValid([NotNullWhen(true)] string? name) => name is not null && ValidName().IsMatch(name);

    /// <summary>Validates a name and returns its lower-case form.</summary>
    public static string Normalize(string? name)
    {
        if (!IsValid(name))
        {
            throw new ArgumentException($"Invalid sheet name. Use {Rule}.", nameof(name));
        }
        return name.ToLowerInvariant();
    }
}
