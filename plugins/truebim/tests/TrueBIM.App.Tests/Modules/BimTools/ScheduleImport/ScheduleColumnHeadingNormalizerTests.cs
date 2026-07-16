using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleImport;

public sealed class ScheduleColumnHeadingNormalizerTests
{
    [Theory]
    [InlineData("Код завода-\nизготовителя", "Код завода-изготовителя")]
    [InlineData("Коли-\nчество", "Количество")]
    [InlineData("Единица\nизмерен\nия", "Единица измерения")]
    [InlineData("Тип марка оборудования\nобозначение документа\nопросного листа", "Тип марка оборудования обозначение документа опросного листа")]
    [InlineData("Масса\nединицы,\nкг", "Масса единицы, кг")]
    public void Normalize_ReconstructsLogicalHeading(string source, string expected)
    {
        Assert.Equal(expected, ScheduleColumnHeadingNormalizer.Normalize(source));
    }
}
