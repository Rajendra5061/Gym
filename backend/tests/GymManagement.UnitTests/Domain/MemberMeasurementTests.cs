using FluentAssertions;
using GymManagement.Domain.Entities;
using Xunit;

namespace GymManagement.UnitTests.Domain;

/// <summary>The derived BMI on a measurement snapshot (supports case M-16).</summary>
public class MemberMeasurementTests
{
    private static MemberMeasurement Measurement(decimal? weightKg, decimal? heightCm) =>
        new() { MemberId = 1, MeasuredOn = new DateTime(2026, 8, 17), WeightKg = weightKg, HeightCm = heightCm };

    [Fact(DisplayName = "Bmi is weight divided by height in metres squared, rounded to 2dp")]
    public void Bmi_WithHeightAndWeight_IsCorrect()
    {
        var measurement = Measurement(70m, 175m);

        measurement.Bmi.Should().Be(22.86m);
    }

    [Theory(DisplayName = "Bmi is computed for a range of realistic body measurements")]
    [InlineData(60, 160, 23.44)]
    [InlineData(100, 180, 30.86)]
    [InlineData(50, 150, 22.22)]
    [InlineData(80, 200, 20.00)]
    public void Bmi_IsRoundedToTwoDecimalPlaces(double weight, double height, double expected)
    {
        var measurement = Measurement((decimal)weight, (decimal)height);

        measurement.Bmi.Should().Be((decimal)expected);
    }

    [Fact(DisplayName = "Bmi is null when the weight is missing")]
    public void Bmi_WithoutWeight_IsNull()
    {
        Measurement(null, 175m).Bmi.Should().BeNull();
    }

    [Fact(DisplayName = "Bmi is null when the height is missing")]
    public void Bmi_WithoutHeight_IsNull()
    {
        Measurement(70m, null).Bmi.Should().BeNull();
    }

    [Fact(DisplayName = "Bmi is null when both values are missing")]
    public void Bmi_WithNeitherValue_IsNull()
    {
        Measurement(null, null).Bmi.Should().BeNull();
    }

    [Fact(DisplayName = "Bmi is null rather than dividing by zero when the height is zero")]
    public void Bmi_WithZeroHeight_IsNullAndDoesNotThrow()
    {
        var measurement = Measurement(70m, 0m);

        decimal? bmi = null;
        var act = () => bmi = measurement.Bmi;

        act.Should().NotThrow();
        bmi.Should().BeNull();
    }

    [Fact(DisplayName = "Bmi is null when the weight is zero")]
    public void Bmi_WithZeroWeight_IsNull()
    {
        Measurement(0m, 175m).Bmi.Should().BeNull();
    }

    [Fact(DisplayName = "Bmi is null for negative values rather than returning a nonsense figure")]
    public void Bmi_WithNegativeValues_IsNull()
    {
        Measurement(-70m, 175m).Bmi.Should().BeNull();
        Measurement(70m, -175m).Bmi.Should().BeNull();
    }

    [Fact(DisplayName = "Bmi is a decimal so that no floating point error creeps into the chart data")]
    public void Bmi_IsADecimal()
    {
        var measurement = Measurement(70m, 175m);

        measurement.Bmi.Should().BeOfType(typeof(decimal));
    }
}
