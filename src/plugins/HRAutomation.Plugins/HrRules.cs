using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HRAutomation.Plugins
{
    /// <summary>
    /// Pure business rules with no Dataverse dependency, so they can be unit tested.
    /// </summary>
    public static class HrRules
    {
        private static readonly Regex PanPattern = new Regex("^[A-Z]{5}[0-9]{4}[A-Z]$");
        private static readonly Regex IfscPattern = new Regex("^[A-Z]{4}0[A-Z0-9]{6}$");
        private static readonly Regex AadhaarLast4Pattern = new Regex("^[0-9]{4}$");
        private static readonly Regex UanPattern = new Regex("^[0-9]{12}$");

        public static string BuildFullName(string first, string middle, string last)
        {
            var parts = new List<string>();
            foreach (var part in new[] { first, middle, last })
            {
                if (!string.IsNullOrWhiteSpace(part)) parts.Add(part.Trim());
            }
            var result = string.Join(" ", parts);
            return result.Length > 160 ? result.Substring(0, 160) : result;
        }

        /// <summary>Upper-cases and removes spaces and hyphens, for ID numbers typed in different styles.</summary>
        public static string NormalizeId(string value)
        {
            if (value == null) return null;
            var cleaned = Regex.Replace(value, "[\\s-]", string.Empty).ToUpperInvariant();
            return cleaned.Length == 0 ? null : cleaned;
        }

        public static bool IsValidPan(string value) => value == null || PanPattern.IsMatch(value);
        public static bool IsValidIfsc(string value) => value == null || IfscPattern.IsMatch(value);
        public static bool IsValidAadhaarLast4(string value) => value == null || AadhaarLast4Pattern.IsMatch(value);
        public static bool IsValidUan(string value) => value == null || UanPattern.IsMatch(value);

        public static decimal YearsOfService(DateTime dateOfJoining, DateTime today)
        {
            if (today <= dateOfJoining) return 0m;
            var years = (decimal)(today.Date - dateOfJoining.Date).TotalDays / 365.25m;
            return Math.Round(years, 1, MidpointRounding.AwayFromZero);
        }

        /// <summary>Gratuity becomes payable after 5 years of continuous service.</summary>
        public static bool IsGratuityEligible(decimal yearsOfService) => yearsOfService >= 5m;

        /// <summary>
        /// Picks the employment history change type for a set of changed fields, or null when nothing
        /// worth recording changed. The most significant change wins.
        /// </summary>
        public static int? ClassifyChange(bool department, bool designation, bool location, bool manager, bool ctc)
        {
            if (designation) return ChangeType.Promotion;
            if (department || location) return ChangeType.Transfer;
            if (manager) return ChangeType.ManagerChange;
            if (ctc) return ChangeType.SalaryRevision;
            return null;
        }
    }
}
