using DBUDoctor.Core.Config;

namespace DBUDoctor.Core.Reporting;

/// <summary>Evaluates <c>fail_on</c> against a bundle report (exit code 1 when anything is violated).</summary>
public static class PolicyEvaluator
{
    public static void Evaluate(BundleReport report, FailOnSettings failOn)
    {
        var currency = report.Currency;
        if (failOn.MonthlyIncrease is { } limit && report.HeadlineDelta > limit)
        {
            report.Violations.Add(new PolicyViolation(
                "fail_on.monthly_increase",
                $"monthly increase {Format.SignedMoney(report.HeadlineDelta, currency)} is above the limit of {Format.Money(limit, currency)}."));
        }

        if (failOn.PercentIncrease is { } percent)
        {
            if (report.HeadlinePercent is { } actual)
            {
                if (actual * 100 > percent)
                {
                    report.Violations.Add(new PolicyViolation(
                        "fail_on.percent_increase",
                        $"monthly increase of {Format.SignedPercent(actual)} on changed resources is above the limit of {Format.Number(percent)}%."));
                }
            }
            else if (report.HeadlineRows.Any())
            {
                report.PolicyNotes.Add("fail_on.percent_increase was not evaluated: the base total of changed resources is 0.");
            }
        }

        foreach (var finding in report.Findings.Where(f => !f.Existing))
        {
            if (failOn.Severity is { } minimum && finding.Severity >= minimum)
            {
                report.Violations.Add(new PolicyViolation(
                    "fail_on.severity",
                    $"{finding.RuleId} is reported at {EnumNames.Of(finding.Severity)} for {finding.ResourceType}.{finding.ResourceKey}."));
            }
            else if (failOn.Rules.Contains(finding.RuleId, StringComparer.Ordinal))
            {
                report.Violations.Add(new PolicyViolation(
                    "fail_on.rules",
                    $"{finding.RuleId} is reported for {finding.ResourceType}.{finding.ResourceKey}."));
            }
        }

        if (failOn.UnknownCost)
        {
            foreach (var row in report.UnknownRows)
            {
                report.Violations.Add(new PolicyViolation(
                    "fail_on.unknown_cost",
                    $"the cost of {row.Type}.{row.Key} is unknown: {string.Join("; ", row.UnknownReasons)}."));
            }
        }
    }
}
