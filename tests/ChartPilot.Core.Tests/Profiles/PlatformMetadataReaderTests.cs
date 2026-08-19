using ChartPilot.Core.Profiles;
using ChartPilot.Core.Values;

namespace ChartPilot.Core.Tests.Profiles;

public class PlatformMetadataReaderTests
{
    private readonly PlatformMetadataReader _reader = new();

    private static ValuesDocument Values(string yaml) => ValuesDocument.Parse(yaml, "values.yaml");

    [Theory]
    [InlineData("public", DataClassification.Public)]
    [InlineData("internal", DataClassification.Internal)]
    [InlineData("confidential", DataClassification.Confidential)]
    [InlineData("sensitive-personal-data", DataClassification.SensitivePersonalData)]
    public void KebabCaseClassificationsMapOntoTheEnum(string raw, DataClassification expected)
    {
        var values = Values($"platform:\n  dataClassification: {raw}\n");

        Assert.Equal(expected, _reader.ReadClassification(values));
    }

    [Theory]
    [InlineData("SensitivePersonalData")]
    [InlineData("sensitive_personal_data")]
    [InlineData("Sensitive-Personal-Data")]
    public void SeparatorAndCaseVariantsAreAccepted(string raw)
        => Assert.Equal(
            DataClassification.SensitivePersonalData,
            _reader.ReadClassification(Values($"platform:\n  dataClassification: {raw}\n")));

    [Fact]
    public void AnAbsentClassificationIsUnclassified()
        => Assert.Equal(DataClassification.Unclassified, _reader.ReadClassification(Values("replicaCount: 1\n")));

    [Fact]
    public void AnUnknownClassificationIsUnclassified()
        => Assert.Equal(
            DataClassification.Unclassified,
            _reader.ReadClassification(Values("platform:\n  dataClassification: top-secret\n")));

    [Theory]
    [InlineData("internal", Exposure.Internal)]
    [InlineData("public", Exposure.Public)]
    [InlineData("external", Exposure.Public)]
    [InlineData("somewhere", Exposure.Unknown)]
    public void ExposureIsRead(string raw, Exposure expected)
        => Assert.Equal(expected, _reader.ReadExposure(Values($"platform:\n  exposure: {raw}\n")));

    [Fact]
    public void AnAbsentExposureIsUnknown()
        => Assert.Equal(Exposure.Unknown, _reader.ReadExposure(Values("replicaCount: 1\n")));

    [Fact]
    public void ClassificationsRoundTripThroughTheirYamlSpelling()
    {
        foreach (var classification in Enum.GetValues<DataClassification>())
        {
            var yaml = PlatformMetadataReader.ToYamlValue(classification);
            var parsed = PlatformMetadataReader.ParseClassification(yaml);

            Assert.Equal(classification, parsed);
        }
    }
}
