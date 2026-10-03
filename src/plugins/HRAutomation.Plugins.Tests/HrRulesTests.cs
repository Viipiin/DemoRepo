using HRAutomation.Plugins;
using Xunit;

public class HrRulesTests
{
    [Theory]
    [InlineData("Asha", null, "Rao", "Asha Rao")]
    [InlineData(" Asha ", "K", " Rao", "Asha K Rao")]
    [InlineData("Asha", "  ", "Rao", "Asha Rao")]
    public void BuildFullName_JoinsNonEmptyParts(string first, string middle, string last, string expected)
    {
        Assert.Equal(expected, HrRules.BuildFullName(first, middle, last));
    }

    [Theory]
    [InlineData("abcde1234f", "ABCDE1234F")]
    [InlineData("ABCDE 1234 F", "ABCDE1234F")]
    [InlineData("   ", null)]
    public void NormalizeId_UppercasesAndStripsSpaces(string input, string expected)
    {
        Assert.Equal(expected, HrRules.NormalizeId(input));
    }

    [Theory]
    [InlineData("ABCDE1234F", true)]
    [InlineData("ABCD1234F", false)]
    [InlineData("12345ABCDE", false)]
    [InlineData(null, true)]
    public void IsValidPan(string pan, bool expected) => Assert.Equal(expected, HrRules.IsValidPan(pan));

    [Theory]
    [InlineData("HDFC0001234", true)]
    [InlineData("HDFC1001234", false)]
    [InlineData("HDF0001234", false)]
    public void IsValidIfsc(string ifsc, bool expected) => Assert.Equal(expected, HrRules.IsValidIfsc(ifsc));

    [Theory]
    [InlineData("1234", true)]
    [InlineData("123456789012", false)]
    [InlineData("12a4", false)]
    public void IsValidAadhaarLast4_RejectsFullNumber(string value, bool expected) => Assert.Equal(expected, HrRules.IsValidAadhaarLast4(value));

    [Fact]
    public void IsValidUan_RequiresTwelveDigits()
    {
        Assert.True(HrRules.IsValidUan("100200300400"));
        Assert.False(HrRules.IsValidUan("10020030040"));
    }

    [Fact]
    public void YearsOfService_AndGratuity()
    {
        var joined = new DateTime(2020, 4, 1);
        Assert.Equal(5.0m, HrRules.YearsOfService(joined, new DateTime(2025, 4, 1)));
        Assert.True(HrRules.IsGratuityEligible(HrRules.YearsOfService(joined, new DateTime(2025, 4, 1))));
        Assert.False(HrRules.IsGratuityEligible(HrRules.YearsOfService(joined, new DateTime(2025, 3, 1))));
        Assert.Equal(0m, HrRules.YearsOfService(joined, new DateTime(2019, 1, 1)));
    }

    [Fact]
    public void ClassifyChange_MostSignificantWins()
    {
        Assert.Equal(ChangeType.Promotion, HrRules.ClassifyChange(true, true, true, true, true));
        Assert.Equal(ChangeType.Transfer, HrRules.ClassifyChange(false, false, true, true, true));
        Assert.Equal(ChangeType.ManagerChange, HrRules.ClassifyChange(false, false, false, true, true));
        Assert.Equal(ChangeType.SalaryRevision, HrRules.ClassifyChange(false, false, false, false, true));
        Assert.Null(HrRules.ClassifyChange(false, false, false, false, false));
    }
}
