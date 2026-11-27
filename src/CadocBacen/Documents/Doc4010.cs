using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using CadocBacen.Contabil;
using CadocBacen.Cosif;
using CadocBacen.Internal;

namespace CadocBacen.Documents;

/// <summary>An invalid 4010/4016 document.</summary>
public sealed class Doc4010Exception : Exception
{
    /// <summary>Creates the exception; the message gets the "doc4010: invalid document: " prefix.</summary>
    public Doc4010Exception(string detail)
        : base("doc4010: invalid document: " + detail)
    {
        Detail = detail;
    }

    /// <summary>Creates the exception.</summary>
    public Doc4010Exception()
        : this("")
    {
    }

    /// <summary>Creates the exception.</summary>
    public Doc4010Exception(string detail, Exception innerException)
        : base("doc4010: invalid document: " + detail, innerException)
    {
        Detail = detail;
    }

    /// <summary>The detail in Portuguese.</summary>
    public string Detail { get; } = "";
}

/// <summary>One &lt;conta&gt;.</summary>
/// <param name="Code">Account.</param>
/// <param name="Saldo">Absolute balance in centavos.</param>
public sealed record Doc4010Line(CosifCode Code, long Saldo);

/// <summary>A difference between two submissions.</summary>
/// <param name="Code">Account.</param>
/// <param name="Old">Previous balance.</param>
/// <param name="New">New balance.</param>
/// <param name="Kind">"alterada", "incluída" or "excluída".</param>
public sealed record Doc4010Change(CosifCode Code, long Old, long New, string Kind);

/// <summary>
/// Documents 4010 (Balancete Patrimonial Analítico) and 4016 (Balanço Patrimonial Analítico)
/// in the BCB XML layout.
/// </summary>
public sealed partial class Doc4010
{
    /// <summary>Monthly trial balance.</summary>
    public const string Balancete = "4010";

    /// <summary>Semiannual balance sheet.</summary>
    public const string Balanco = "4016";

    /// <summary>First submission for a data-base.</summary>
    public const string Inclusion = "I";

    /// <summary>Replaces an accepted document.</summary>
    public const string Substitution = "S";

    /// <summary>"4010" or "4016".</summary>
    public required string Code { get; init; }

    /// <summary>8-digit CNPJ root.</summary>
    public required string Cnpj { get; init; }

    /// <summary>AAAA-MM.</summary>
    public required string DataBase { get; init; }

    /// <summary>I or S.</summary>
    public required string Remessa { get; init; }

    /// <summary>Accounts.</summary>
    public List<Doc4010Line> Lines { get; init; } = [];

    [GeneratedRegex("^[0-9]{8}$")]
    private static partial Regex CnpjRe();

    [GeneratedRegex("^[0-9]{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex DataBaseRe();

    [GeneratedRegex(@"^\d{1,16}\.\d{2}$")]
    private static partial Regex SaldoRe();

    [GeneratedRegex("^[0-9]{10}$")]
    private static partial Regex ContaRe();

    private void CheckHeader()
    {
        if (Code is not (Balancete or Balanco))
        {
            throw new Doc4010Exception("codigoDocumento deve ser 4010 ou 4016");
        }

        if (!CnpjRe().IsMatch(Cnpj))
        {
            throw new Doc4010Exception("cnpj deve ter 8 dígitos (raiz)");
        }

        if (!DataBaseRe().IsMatch(DataBase))
        {
            throw new Doc4010Exception("dataBase deve estar no formato AAAA-MM");
        }

        if (Remessa is not (Inclusion or Substitution))
        {
            throw new Doc4010Exception("tipoRemessa deve ser I ou S");
        }

        if (Code == Balanco && !DataBase.EndsWith("-06", StringComparison.Ordinal) && !DataBase.EndsWith("-12", StringComparison.Ordinal))
        {
            throw new Doc4010Exception("o 4016 é semestral: data-base de junho ou dezembro");
        }
    }

    /// <summary>Builds a document from a trial balance.</summary>
    public static Doc4010 Build(string code, string cnpj, string dataBase, string remessa, Balancete balancete, bool includeZero = false)
    {
        ArgumentNullException.ThrowIfNull(balancete);
        var d = new Doc4010 { Code = code, Cnpj = cnpj, DataBase = dataBase, Remessa = remessa };
        d.CheckHeader();
        if (code == Balanco && balancete.HasResult)
        {
            throw new Doc4010Exception("o 4016 não pode ter saldos nos grupos 7 e 8; encerre o resultado no patrimônio líquido");
        }

        foreach (Issue issue in balancete.Check())
        {
            if (issue.Severity == "erro")
            {
                throw new Doc4010Exception("balancete inconsistente: " + issue.Message);
            }
        }

        IReadOnlyList<BalanceteRow> rows = balancete.Rows(includeZero);
        foreach (BalanceteRow r in rows)
        {
            // No sign in the layout: an analytical balance contrary to its nature must be reclassified.
            if (r.Inverted && balancete.Chart.IsLeaf(r.Account.Code))
            {
                throw new Doc4010Exception(r.Account.Code + " " + r.Account.Name + " com saldo " + r.Side.Pt() + ", contrário à natureza " + r.Account.Nature.Pt() + ": reclassifique antes de gerar o documento");
            }
        }

        foreach (BalanceteRow r in rows)
        {
            if (code == Balanco && r.Account.Code.Group is 7 or 8)
            {
                continue;
            }

            d.Lines.Add(new Doc4010Line(r.Account.Code, r.Saldo));
        }

        return d;
    }

    /// <summary>The XML exactly in the published layout.</summary>
    public string ToXml()
    {
        var b = new StringBuilder();
        b.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        b.Append("<documento codigoDocumento=\"").Append(Code).Append("\" cnpj=\"").Append(Cnpj).Append("\" dataBase=\"").Append(DataBase)
            .Append("\" tipoRemessa=\"").Append(Remessa).Append("\">\n  <contas>\n");
        foreach (Doc4010Line l in Lines)
        {
            b.Append("    <conta codigoConta=\"").Append(l.Code.Compact).Append("\" saldo=\"").Append(Centavos.Decimal(l.Saldo)).Append("\"/>\n");
        }

        b.Append("  </contas>\n</documento>\n");
        return b.ToString();
    }

    /// <summary>Parses and validates syntax.</summary>
    public static Doc4010 Parse(string xml)
    {
        var doc = new XmlDocument { XmlResolver = null };
        try
        {
            using var sr = new StringReader(xml);
            using var r = XmlReader.Create(sr, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            doc.Load(r);
        }
        catch (XmlException e)
        {
            throw new Doc4010Exception("XML: " + e.Message, e);
        }

        XmlElement root = doc.DocumentElement is { Name: "documento" } el ? el : throw new Doc4010Exception("XML: elemento raiz deve ser documento");
        var d = new Doc4010
        {
            Code = root.GetAttribute("codigoDocumento"),
            Cnpj = root.GetAttribute("cnpj"),
            DataBase = root.GetAttribute("dataBase"),
            Remessa = root.GetAttribute("tipoRemessa"),
        };
        d.CheckHeader();
        var seen = new HashSet<CosifCode>();
        int i = 0;
        foreach (XmlElement c in root.SelectNodes("contas/conta")!.Cast<XmlElement>())
        {
            i++;
            string codigo = c.GetAttribute("codigoConta");
            string saldo = c.GetAttribute("saldo");
            if (!ContaRe().IsMatch(codigo))
            {
                throw new Doc4010Exception(string.Create(CultureInfo.InvariantCulture, $"conta {i}: codigoConta deve ter 10 dígitos"));
            }

            CosifCode code;
            try
            {
                code = CosifCode.Parse(codigo);
            }
            catch (CosifFormatException e)
            {
                throw new Doc4010Exception("conta " + codigo + ": " + e.Message, e);
            }

            if (!SaldoRe().IsMatch(saldo))
            {
                throw new Doc4010Exception("conta " + codigo + ": saldo \"" + saldo + "\" fora do padrão \\d{1,16}\\.\\d{2}");
            }

            if (!seen.Add(code))
            {
                throw new Doc4010Exception("conta " + codigo + " repetida");
            }

            int dot = saldo.IndexOf('.', StringComparison.Ordinal);
            long cents = (long.Parse(saldo[..dot], CultureInfo.InvariantCulture) * 100) + long.Parse(saldo[(dot + 1)..], CultureInfo.InvariantCulture);
            d.Lines.Add(new Doc4010Line(code, cents));
        }

        return d;
    }

    /// <summary>
    /// Checks the document against a chart: accounts exist, no result accounts in a 4016, and each
    /// synthetic account equals its sub-accounts. Signs are rebuilt bottom-up (leaves carry their
    /// nature's sign, synthetic accounts the sign of their children's sum) and each synthetic is
    /// compared in absolute value against its direct children's reported values. Absent accounts
    /// count as zero.
    /// </summary>
    public IReadOnlyList<string> Validate(CosifChart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var problems = new List<string>();
        var lines = new Dictionary<CosifCode, long>();
        foreach (Doc4010Line l in Lines)
        {
            lines[l.Code] = l.Saldo;
            if (chart[l.Code] is null)
            {
                problems.Add("conta " + l.Code + " não existe no plano");
            }

            if (Code == Balanco && l.Code.Group is 7 or 8)
            {
                problems.Add("conta de resultado " + l.Code + " no 4016");
            }
        }

        IReadOnlyDictionary<CosifCode, long> reportedLines = lines;

        long Natural(CosifCode c, long v)
        {
            Nature n = chart[c]?.Nature ?? CosifCode.GroupNature(c.Group);
            return n == Nature.Credit ? -v : v;
        }

        var children = new Dictionary<CosifCode, List<CosifCode>>();
        foreach (CosifCode c in lines.Keys)
        {
            if (c.Parent is { } p)
            {
                if (!children.TryGetValue(p, out List<CosifCode>? list))
                {
                    list = [];
                    children[p] = list;
                }

                list.Add(c);
            }
        }

        var computed = new Dictionary<CosifCode, long>();
        long Compute(CosifCode c)
        {
            if (computed.TryGetValue(c, out long v))
            {
                return v;
            }

            if (children.TryGetValue(c, out List<CosifCode>? kids))
            {
                v = kids.Sum(Compute);
            }
            else
            {
                v = Natural(c, reportedLines.GetValueOrDefault(c));
            }

            computed[c] = v;
            return v;
        }

        long ReportedSigned(CosifCode c)
        {
            long v = reportedLines.GetValueOrDefault(c);
            long s = Compute(c);
            if (s == 0)
            {
                s = Natural(c, 1);
            }

            return s < 0 ? -v : v;
        }

        foreach (CosifCode p in children.Keys.OrderBy(k => k))
        {
            long sum = Math.Abs(children[p].Sum(ReportedSigned));
            bool present = lines.TryGetValue(p, out long got);
            if (!present && sum != 0)
            {
                problems.Add("conta sintética " + p + " ausente");
                continue;
            }

            if (sum != got)
            {
                problems.Add("conta " + p + ": saldo " + Centavos.Decimal(got) + " difere da soma das subcontas " + Centavos.Decimal(sum));
            }
        }

        return problems;
    }

    /// <summary>What a substitution changes relative to the previous submission.</summary>
    public static IReadOnlyList<Doc4010Change> Diff(Doc4010 previous, Doc4010 next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        Dictionary<CosifCode, long> a = previous.Lines.ToDictionary(l => l.Code, l => l.Saldo);
        Dictionary<CosifCode, long> b = next.Lines.ToDictionary(l => l.Code, l => l.Saldo);
        var output = new List<Doc4010Change>();
        foreach ((CosifCode c, long v) in a)
        {
            if (!b.TryGetValue(c, out long w))
            {
                output.Add(new Doc4010Change(c, v, 0, "excluída"));
            }
            else if (w != v)
            {
                output.Add(new Doc4010Change(c, v, w, "alterada"));
            }
        }

        foreach ((CosifCode c, long w) in b)
        {
            if (!a.ContainsKey(c))
            {
                output.Add(new Doc4010Change(c, 0, w, "incluída"));
            }
        }

        return output.OrderBy(x => x.Code).ToList();
    }

    /// <summary>Filing deadline: 4010 on the 18th of the next month (next business day); 4016 on the last business day of the next month.</summary>
    public static DateOnly Deadline(string code, string dataBase, IHolidays? holidays = null)
    {
        ArgumentNullException.ThrowIfNull(dataBase);
        if (!DataBaseRe().IsMatch(dataBase))
        {
            throw new Doc4010Exception("dataBase deve estar no formato AAAA-MM");
        }

        holidays ??= new NationalHolidays();
        DateOnly next = BankDays.ParseDate(dataBase + "-01").AddMonths(1);
        return code switch
        {
            Balancete => BankDays.NextBusinessDay(holidays, next.AddDays(17)),
            Balanco => BankDays.PreviousBusinessDay(holidays, next.AddMonths(1).AddDays(-1)),
            _ => throw new Doc4010Exception("codigoDocumento deve ser 4010 ou 4016"),
        };
    }
}
