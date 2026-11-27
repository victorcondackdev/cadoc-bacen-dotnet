using System.Globalization;
using CadocBacen.Contabil;
using CadocBacen.Cosif;
using CadocBacen.Documents;
using CadocBacen.Scr;

namespace CadocBacen.Tests.Parity;

/// <summary>Outputs must match the Go implementation byte for byte.</summary>
public class ParityTests
{
    private static readonly CosifChart Chart = CosifChart.Sample();

    private static long L(string s) => long.Parse(s, CultureInfo.InvariantCulture);

    private static DateOnly D(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public void Codes()
    {
        foreach (var v in GoVectors.Codes)
        {
            CosifCode c = CosifCode.Parse(v.Body);
            Assert.Equal(v.Dotted, c.ToString());
            Assert.Equal(v.Compact, c.Compact);
            Assert.Equal(v.Level, c.Level);
            Assert.Equal(v.Parent, c.Parent?.Compact ?? "");
            Assert.Equal(c, CosifCode.Parse(v.Dotted));
        }
    }

    private static Balancete Leaves(string encoded)
    {
        var m = new Dictionary<CosifCode, long>();
        foreach (string part in encoded.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kv = part.Split('=');
            m[CosifCode.Parse(kv[0])] = L(kv[1]);
        }

        return Balancete.FromLeaves(Chart, m);
    }

    private static string Try(Func<string> f)
    {
        try
        {
            return f();
        }
        catch (Doc4010Exception e)
        {
            return "ERR " + e.Message;
        }
    }

    [Fact]
    public void TrialBalancesAnd4010()
    {
        int i = 0;
        foreach (var v in GoVectors.Balances)
        {
            Balancete b = Leaves(v.Leaves);
            Assert.Equal(v.Issues, string.Join("\n", b.Check().Select(x => x.ToString())));
            bool includeZero = i % 3 == 0;
            Assert.Equal(v.Xml4010, Try(() => Doc4010.Build(Doc4010.Balancete, "12345678", "2026-10", Doc4010.Inclusion, b, includeZero).ToXml()));
            Balancete closed = b.CloseResult(CosifCode.Parse("641000000"), CosifCode.Parse("649000000"));
            Assert.Equal(v.Xml4016, Try(() => Doc4010.Build(Doc4010.Balanco, "12345678", "2026-12", Doc4010.Substitution, closed).ToXml()));
            Assert.Equal(v.Closed, Try(() => Doc4010.Build(Doc4010.Balanco, "12345678", "2026-06", Doc4010.Inclusion, b).ToXml()));
            if (!v.Xml4010.StartsWith("ERR", StringComparison.Ordinal))
            {
                Doc4010 parsed = Doc4010.Parse(v.Xml4010);
                Assert.Equal(v.Xml4010, parsed.ToXml());
                Assert.Empty(parsed.Validate(Chart));
            }

            i++;
        }
    }

    [Fact]
    public void ValidationFindings()
    {
        Assert.NotEmpty(GoVectors.Tampered);
        foreach (var v in GoVectors.Tampered)
        {
            Assert.Equal(v.Problems, string.Join("\n", Doc4010.Parse(v.Xml).Validate(Chart)));
        }
    }

    [Fact]
    public void Deadlines()
    {
        foreach (var v in GoVectors.Deadlines)
        {
            Assert.Equal(v.Deadline4010, Internal.BankDays.Format(Doc4010.Deadline(Doc4010.Balancete, v.DataBase)));
            if (v.Deadline4016.Length > 0)
            {
                Assert.Equal(v.Deadline4016, Internal.BankDays.Format(Doc4010.Deadline(Doc4010.Balanco, v.DataBase)));
            }
        }
    }

    [Fact]
    public void Buckets()
    {
        foreach (var v in GoVectors.Buckets)
        {
            Assert.Equal(v.Bucket, Doc3040.BucketFor(D(v.Reference), D(v.Due)));
            Assert.Equal(v.WriteOff, Doc3040.WriteOffBucket(D(v.Reference), D(v.Due)));
        }
    }

    [Fact]
    public void Scr3040()
    {
        foreach (var v in GoVectors.Scr)
        {
            string[] blocks = v.Clients.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n#\n");
            string[] h = blocks[0].Split('|');
            var header = new Doc3040Header(h[0], h[1], int.Parse(h[2], CultureInfo.InvariantCulture), int.Parse(h[3], CultureInfo.InvariantCulture), h[4], h[5], h[6]);
            var clients = new List<Client>();
            foreach (string block in blocks.Skip(1))
            {
                string[] lines = block.Split('\n');
                string[] c = lines[0].Split('|');
                var ops = new List<Operation>();
                foreach (string line in lines.Skip(1))
                {
                    string[] o = line.Split('|');
                    ops.Add(new Operation
                    {
                        Contract = o[0],
                        Modality = o[1],
                        Cep = o[2],
                        Rate = o[3],
                        Contracted = D(o[4]),
                        Value = L(o[5]),
                        Provision = L(o[6]),
                        Special = o[7].Length == 0 ? [] : o[7].Split(','),
                        WrittenOff = o[8].Length == 0 ? null : D(o[8]),
                        Installments = o[9].Split(',').Select(x => x.Split('=')).Select(x => new Installment(D(x[0]), L(x[1]))).ToList(),
                    });
                }

                clients.Add(new Client
                {
                    Type = int.Parse(c[0], CultureInfo.InvariantCulture),
                    Document = c[1],
                    Authorized = c[2] == "true",
                    Size = int.Parse(c[3], CultureInfo.InvariantCulture),
                    Since = D(c[4]),
                    Income = L(c[5]),
                    Operations = ops,
                });
            }

            string got;
            try
            {
                got = Doc3040.Build(header, clients);
            }
            catch (Doc3040Exception e)
            {
                got = "ERR " + e.Message;
            }

            Assert.Equal(v.Xml.Replace("\r\n", "\n", StringComparison.Ordinal), got);
        }
    }
}
