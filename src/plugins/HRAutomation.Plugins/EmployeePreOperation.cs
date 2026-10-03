using System;
using Microsoft.Xrm.Sdk;

namespace HRAutomation.Plugins
{
    /// <summary>
    /// P-01. hra_employee Create and Update, PreOperation (stage 20), synchronous.
    /// Builds the full name, validates and normalises Indian ID numbers, and sets defaults on create
    /// (notice period, probation end date, default leave policy).
    /// Update step needs a PreImage named "PreImage" with first, middle and last name.
    /// </summary>
    public class EmployeePreOperation : PluginBase
    {
        protected override void ExecuteInternal(LocalContext local)
        {
            var target = local.Target;
            if (target == null || target.LogicalName != Employee.EntityName) return;

            if (local.IsCreate || target.Contains(Employee.FirstName) || target.Contains(Employee.MiddleName) || target.Contains(Employee.LastName))
            {
                target[Employee.FullName] = HrRules.BuildFullName(
                    local.Merged<string>(Employee.FirstName),
                    local.Merged<string>(Employee.MiddleName),
                    local.Merged<string>(Employee.LastName));
            }

            ValidateId(target, Employee.Pan, HrRules.IsValidPan, "PAN must be 10 characters like ABCDE1234F.");
            ValidateId(target, Employee.Ifsc, HrRules.IsValidIfsc, "IFSC must be 11 characters like HDFC0001234.");
            ValidateId(target, Employee.AadhaarLast4, HrRules.IsValidAadhaarLast4, "Enter only the last 4 digits of the Aadhaar number. Never store the full number.");
            ValidateId(target, Employee.Uan, HrRules.IsValidUan, "UAN must be 12 digits.");

            if (local.IsCreate)
            {
                if (!target.Contains(Employee.NoticePeriodDays) || target[Employee.NoticePeriodDays] == null)
                {
                    target[Employee.NoticePeriodDays] = Settings.DefaultNoticePeriodDays;
                }

                if (!target.Contains("hra_leavepolicy") || target["hra_leavepolicy"] == null)
                {
                    var policy = new LeaveService(local).GetDefaultPolicy();
                    if (policy != null) target["hra_leavepolicy"] = policy;
                }

                var joining = target.GetAttributeValue<DateTime?>(Employee.DateOfJoining);
                if (joining.HasValue && (!target.Contains(Employee.ProbationEndDate) || target[Employee.ProbationEndDate] == null))
                {
                    target[Employee.ProbationEndDate] = joining.Value.AddMonths(ProbationMonths(local));
                }
            }

            if (local.IsCreate || target.Contains(Employee.DateOfJoining))
            {
                var joining = local.Merged<DateTime?>(Employee.DateOfJoining);
                if (joining.HasValue)
                {
                    var years = HrRules.YearsOfService(joining.Value, DateTime.UtcNow);
                    target[Employee.YearsOfService] = years;
                    target[Employee.GratuityEligible] = HrRules.IsGratuityEligible(years);
                }
            }
        }

        private static void ValidateId(Entity target, string attribute, Func<string, bool> isValid, string message)
        {
            if (!target.Contains(attribute)) return;
            var normalised = HrRules.NormalizeId(target.GetAttributeValue<string>(attribute));
            if (!isValid(normalised)) throw new InvalidPluginExecutionException(message);
            target[attribute] = normalised;
        }

        private static int ProbationMonths(LocalContext local)
        {
            var raw = local.GetEnvironmentVariable(Settings.ProbationMonthsVariable);
            return int.TryParse(raw, out var months) && months >= 0 && months <= 24 ? months : Settings.DefaultProbationMonths;
        }
    }
}
