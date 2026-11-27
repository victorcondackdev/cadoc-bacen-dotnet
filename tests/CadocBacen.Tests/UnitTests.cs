using CadocBacen.Contabil;
using CadocBacen.Cosif;
using CadocBacen.Documents;
using CadocBacen.Scr;

namespace CadocBacen.Tests;

public class UnitTests
{
    [Theory]
    [InlineData("1000000009")]
    [InlineData("1100000002")]
    [InlineData("1130000003")]
    [InlineData("1139000004")]
    [InlineData("4.1.0.00.00.00-9")]
    [InlineData("4.1.1.00.00.00-6")]
    [InlineData("1.2.0.00.00.00-5")]
    [InlineData("9.0.9.61.00.00-1")]
    public void PublishedCodesHaveValidCheckDigits(string code) => Assert.True(CosifCode.TryParse(code, out _));

    [Fact]
    public void WrongCheckDigitIsRejected()
    {
        var e = Assert.Throws<CosifFormatException>(() => CosifCode.Parse("1100000003"));
        Assert.Contains("should end in 2", e.Message);
    }

    [Fact]
    public void Hierarchy()
    {
        CosifCode c = CosifCode.Parse("1.1.3.90.00.00");
        Assert.Equal(new[] { "1000000009", "1100000002", "1130000003" }, c.Ancestors.Select(a => a.Compact));
        Assert.Equal(4, c.Level);
        Assert.Null(CosifCode.Parse("1000000009").Parent);
    }

    [Fact]
    public void SampleChartContraAccounts()
    {
        CosifChart chart = CosifChart.Sample();
        CosifAccount provision = chart[CosifCode.Parse("161900000")]!;
        Assert.True(provision.Contra);
        Assert.Equal(Nature.Credit, provision.Nature);
    }

    [Fact]
    public void FourteenSixteenNeedsClosedResultAndSemester()
    {
        CosifChart chart = CosifChart.Sample();
        var leaves = new Dictionary<CosifCode, long>
        {
            [CosifCode.Parse("113000000")] = 100_00,
            [CosifCode.Parse("720000000")] = -100_00,
        };
        Balancete b = Balancete.FromLeaves(chart, leaves);
        Assert.Contains("encerre o resultado", Assert.Throws<Doc4010Exception>(() => Doc4010.Build(Doc4010.Balanco, "12345678", "2026-12", "I", b)).Message);
        Balancete closed = b.CloseResult(CosifCode.Parse("641000000"), CosifCode.Parse("649000000"));
        Assert.Contains("semestral", Assert.Throws<Doc4010Exception>(() => Doc4010.Build(Doc4010.Balanco, "12345678", "2026-11", "I", closed)).Message);
        Doc4010 d = Doc4010.Build(Doc4010.Balanco, "12345678", "2026-12", "I", closed);
        Assert.DoesNotContain(d.Lines, l => l.Code.Group is 7 or 8);
    }

    [Theory]
    [InlineData("4010", "2026-10", "2026-11-18")]
    [InlineData("4010", "2026-03", "2026-04-20")]
    [InlineData("4016", "2026-06", "2026-07-31")]
    [InlineData("4016", "2026-12", "2027-01-29")]
    public void Deadline(string code, string dataBase, string expected)
        => Assert.Equal(expected, Internal.BankDays.Format(Doc4010.Deadline(code, dataBase)));

    [Theory]
    [InlineData("2026-11-05", 110)]
    [InlineData("2026-12-01", 120)]
    [InlineData("2026-10-30", 205)]
    [InlineData("2026-10-16", 210)]
    [InlineData("2025-04-01", 290)]
    public void ScrBuckets(string due, int bucket)
        => Assert.Equal(bucket, Doc3040.BucketFor(new DateOnly(2026, 10, 31), DateOnly.Parse(due, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Documents()
    {
        Assert.True(Doc3040.ValidCpf("12345678909"));
        Assert.False(Doc3040.ValidCpf("12345678900"));
        Assert.True(Doc3040.ValidCnpj("11222333000181"));
        Assert.False(Doc3040.ValidCnpj("11111111111111"));
    }

    [Fact]
    public void ParseRejectsBadSaldo()
    {
        const string xml = "<documento codigoDocumento=\"4010\" cnpj=\"12345678\" dataBase=\"2026-10\" tipoRemessa=\"I\"><contas><conta codigoConta=\"1100000002\" saldo=\"-1.00\"/></contas></documento>";
        Assert.Contains("fora do padrão", Assert.Throws<Doc4010Exception>(() => Doc4010.Parse(xml)).Message);
    }
}
