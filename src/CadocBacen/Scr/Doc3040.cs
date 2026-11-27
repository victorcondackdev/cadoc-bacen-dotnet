using System.Globalization;
using System.Text;
using CadocBacen.Contabil;
using CadocBacen.Internal;

namespace CadocBacen.Scr;

/// <summary>An invalid 3040 input.</summary>
public sealed class Doc3040Exception : Exception
{
    /// <summary>Creates the exception; the message gets the "doc3040: invalid: " prefix.</summary>
    public Doc3040Exception(string detail)
        : base("doc3040: invalid: " + detail)
    {
    }

    /// <summary>Creates the exception.</summary>
    public Doc3040Exception()
        : this("")
    {
    }

    /// <summary>Creates the exception.</summary>
    public Doc3040Exception(string detail, Exception innerException)
        : base("doc3040: invalid: " + detail, innerException)
    {
    }
}

/// <summary>An unpaid amount of an operation.</summary>
/// <param name="Due">Due date.</param>
/// <param name="Amount">Amount in centavos.</param>
public sealed record Installment(DateOnly Due, long Amount);

/// <summary>A credit operation (&lt;Op&gt;).</summary>
public sealed record Operation
{
    /// <summary>Contrt, 1–40 characters.</summary>
    public required string Contract { get; init; }

    /// <summary>Mod, 4 digits.</summary>
    public required string Modality { get; init; }

    /// <summary>OrigemRec (default 0101).</summary>
    public string Origin { get; init; } = "";

    /// <summary>Indx (default 11).</summary>
    public string Index { get; init; } = "";

    /// <summary>PercIndx (default 0).</summary>
    public string IndexPct { get; init; } = "";

    /// <summary>VarCamb (default 790).</summary>
    public string Currency { get; init; } = "";

    /// <summary>CEP, 8 digits.</summary>
    public required string Cep { get; init; }

    /// <summary>TaxEft, e.g. "24.50".</summary>
    public string Rate { get; init; } = "";

    /// <summary>DtContr.</summary>
    public DateOnly Contracted { get; init; }

    /// <summary>VlrContr in centavos.</summary>
    public long Value { get; init; }

    /// <summary>NatuOp (default 01).</summary>
    public string Nature { get; init; } = "";

    /// <summary>ProvConsttd in centavos.</summary>
    public long Provision { get; init; }

    /// <summary>CaracEspecial codes.</summary>
    public IReadOnlyList<string> Special { get; init; } = [];

    /// <summary>Unpaid installments.</summary>
    public IReadOnlyList<Installment> Installments { get; init; } = [];

    /// <summary>Write-off date; the balance then goes to buckets 310–330.</summary>
    public DateOnly? WrittenOff { get; init; }
}

/// <summary>A borrower (&lt;Cli&gt;).</summary>
public sealed record Client
{
    /// <summary>1 PF (CPF), 2 PJ (CNPJ).</summary>
    public int Type { get; init; }

    /// <summary>CPF (11) or full CNPJ (14).</summary>
    public required string Document { get; init; }

    /// <summary>Autorzc.</summary>
    public bool Authorized { get; init; }

    /// <summary>PorteCli.</summary>
    public int Size { get; init; }

    /// <summary>IniRelactCli.</summary>
    public DateOnly Since { get; init; }

    /// <summary>FatAnual in centavos.</summary>
    public long Income { get; init; }

    /// <summary>Operations.</summary>
    public IReadOnlyList<Operation> Operations { get; init; } = [];

    /// <summary>Cd: the CPF, or the CNPJ root.</summary>
    public string Code => Type == 2 && Document.Length == 14 ? Document[..8] : Document;
}

/// <summary>The &lt;Doc3040&gt; header.</summary>
/// <param name="Cnpj">Institution CNPJ root.</param>
/// <param name="DataBase">AAAA-MM.</param>
/// <param name="Remessa">Submission number.</param>
/// <param name="Parte">Part number.</param>
/// <param name="NomeResp">Responsible person.</param>
/// <param name="EmailResp">Their e-mail.</param>
/// <param name="TelResp">DD + 8 digits.</param>
public sealed record Doc3040Header(string Cnpj, string DataBase, int Remessa, int Parte, string NomeResp, string EmailResp, string TelResp);

/// <summary>
/// The core of SCR document 3040: Cli, Op and Venc with vencimento buckets computed from the
/// data-base as the BCB instructions define. Guarantees, Agreg, Inf and Res. 4.966 blocks are
/// not generated.
/// </summary>
public static class Doc3040
{
    private static readonly (int Max, int Bucket)[] ToFall = [(30, 110), (60, 120), (90, 130), (180, 140), (360, 150), (720, 160), (1080, 165), (1440, 170), (1800, 175), (5400, 180)];
    private static readonly (int Max, int Bucket)[] Overdue = [(14, 205), (30, 210), (60, 220), (90, 230), (120, 240), (150, 245), (180, 250), (240, 255), (300, 260), (360, 270), (540, 280)];

    /// <summary>The last day of the data-base month.</summary>
    public static DateOnly LastDay(string dataBase)
    {
        if (dataBase is null || dataBase.Length != 7 || !DateOnly.TryParseExact(dataBase + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly first))
        {
            throw new Doc3040Exception("doc3040: data-base \"" + dataBase + "\" must be AAAA-MM");
        }

        return first.AddMonths(1).AddDays(-1);
    }

    /// <summary>The vencimento code of an installment, days counted from <paramref name="reference"/>.</summary>
    public static int BucketFor(DateOnly reference, DateOnly due)
    {
        int d = reference.DaysUntil(due);
        if (d >= 0)
        {
            foreach ((int max, int b) in ToFall)
            {
                if (d <= max)
                {
                    return b;
                }
            }

            return 190;
        }

        foreach ((int max, int b) in Overdue)
        {
            if (-d <= max)
            {
                return b;
            }
        }

        return 290;
    }

    /// <summary>310 (up to 12 months), 320 (12 to 48) or 330 (over 48) since the write-off.</summary>
    public static int WriteOffBucket(DateOnly reference, DateOnly writtenOff)
    {
        bool After(int months) => reference > writtenOff.AddMonths(months);
        return !After(12) ? 310 : !After(48) ? 320 : 330;
    }

    /// <summary>The bucketed balance of an operation.</summary>
    public static SortedDictionary<int, long> Buckets(Operation op, DateOnly reference)
    {
        ArgumentNullException.ThrowIfNull(op);
        var v = new SortedDictionary<int, long>();
        if (op.WrittenOff is { } wo)
        {
            v[WriteOffBucket(reference, wo)] = op.Installments.Sum(i => i.Amount);
            return v;
        }

        foreach (Installment i in op.Installments)
        {
            int b = BucketFor(reference, i.Due);
            v[b] = v.GetValueOrDefault(b) + i.Amount;
        }

        return v;
    }

    /// <summary>DiaAtraso: days overdue of the oldest installment.</summary>
    public static int DaysLate(Operation op, DateOnly reference)
    {
        ArgumentNullException.ThrowIfNull(op);
        int worst = 0;
        foreach (Installment i in op.Installments)
        {
            worst = Math.Max(worst, i.Due.DaysUntil(reference));
        }

        return worst;
    }

    /// <summary>IPOC: institution CNPJ root, modality, client type, client code, contract.</summary>
    public static string Ipoc(string cnpj, Client client, Operation op)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(op);
        return cnpj + op.Modality + client.Type.ToString(CultureInfo.InvariantCulture) + client.Code + op.Contract;
    }

    private static bool Digits(string? s, int n) => s is not null && s.Length == n && s.All(char.IsAsciiDigit);

    /// <summary>CPF check digits.</summary>
    public static bool ValidCpf(string s)
    {
        if (!Digits(s, 11) || s.All(c => c == s[0]))
        {
            return false;
        }

        for (int n = 9; n <= 10; n++)
        {
            int sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += (s[i] - '0') * (n + 1 - i);
            }

            if (sum * 10 % 11 % 10 != s[n] - '0')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Numeric CNPJ check digits.</summary>
    public static bool ValidCnpj(string s)
    {
        if (!Digits(s, 14) || s.All(c => c == s[0]))
        {
            return false;
        }

        int[] w = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        for (int n = 12; n <= 13; n++)
        {
            int sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += (s[i] - '0') * w[w.Length - n + i];
            }

            int d = sum % 11;
            d = d < 2 ? 0 : 11 - d;
            if (d != s[n] - '0')
            {
                return false;
            }
        }

        return true;
    }

    private static string Or(string s, string def) => s.Length == 0 ? def : s;

    private static string Esc(string s) => s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Validates the clients and renders the document.</summary>
    public static string Build(Doc3040Header h, IEnumerable<Client> clients)
    {
        ArgumentNullException.ThrowIfNull(h);
        ArgumentNullException.ThrowIfNull(clients);
        if (!Digits(h.Cnpj, 8))
        {
            throw new Doc3040Exception("CNPJ da instituição deve ser a raiz de 8 dígitos");
        }

        DateOnly reference;
        try
        {
            reference = LastDay(h.DataBase);
        }
        catch (Doc3040Exception e)
        {
            throw new Doc3040Exception("doc3040: data-base \"" + h.DataBase + "\" must be AAAA-MM", e);
        }

        if (h.Remessa < 1 || h.Parte < 1)
        {
            throw new Doc3040Exception("Remessa e Parte começam em 1");
        }

        if (!Digits(h.TelResp, 10) || h.NomeResp.Length == 0 || !h.EmailResp.Contains('@', StringComparison.Ordinal))
        {
            throw new Doc3040Exception("responsável: nome, e-mail e telefone (DD + 8 dígitos) são obrigatórios");
        }

        var exposed = new HashSet<string>(StringComparer.Ordinal);
        var contracts = new HashSet<string>(StringComparer.Ordinal);
        var body = new StringBuilder();
        foreach (Client c in clients.OrderBy(c => c.Type).ThenBy(c => c.Code, StringComparer.Ordinal))
        {
            if (c.Type == 1 && !ValidCpf(c.Document))
            {
                throw new Doc3040Exception("CPF " + c.Document + " inválido");
            }

            if (c.Type == 2 && !ValidCnpj(c.Document))
            {
                throw new Doc3040Exception("CNPJ " + c.Document + " inválido");
            }

            if (c.Type is not (1 or 2))
            {
                throw new Doc3040Exception("tipo de cliente " + I(c.Type) + " não suportado (1 PF, 2 PJ)");
            }

            if (c.Size < 0 || (c.Type == 1 && c.Size > 8) || (c.Type == 2 && c.Size > 4))
            {
                throw new Doc3040Exception("PorteCli " + I(c.Size) + " inválido para o tipo " + I(c.Type));
            }

            if (c.Size == 0 && c.Income > 100)
            {
                throw new Doc3040Exception("PorteCli 0 só é permitido com FatAnual até R$ 1,00");
            }

            body.Append("  <Cli Tp=\"").Append(I(c.Type)).Append("\" Cd=\"").Append(c.Code).Append("\" Autorzc=\"").Append(c.Authorized ? "S" : "N")
                .Append("\" PorteCli=\"").Append(I(c.Size)).Append("\" IniRelactCli=\"").Append(BankDays.Format(c.Since))
                .Append("\" FatAnual=\"").Append(Centavos.Decimal(c.Income)).Append("\">\n");
            foreach (Operation op in c.Operations)
            {
                if (!Digits(op.Modality, 4))
                {
                    throw new Doc3040Exception("modalidade \"" + op.Modality + "\" deve ter 4 dígitos");
                }

                if (op.Contract.Length is 0 or > 40)
                {
                    throw new Doc3040Exception("contrato deve ter de 1 a 40 caracteres");
                }

                if (!contracts.Add(I(c.Type) + "/" + c.Code + "/" + op.Modality + "/" + op.Contract))
                {
                    throw new Doc3040Exception("contrato " + op.Contract + " repetido para o cliente e a modalidade");
                }

                if (op.Installments.Count == 0)
                {
                    throw new Doc3040Exception("operação " + op.Contract + " sem parcelas em aberto");
                }

                if (!Digits(op.Cep, 8))
                {
                    throw new Doc3040Exception("CEP da operação " + op.Contract + " deve ter 8 dígitos");
                }

                SortedDictionary<int, long> v = Buckets(op, reference);
                if (v.Values.Sum() > 0)
                {
                    exposed.Add(I(c.Type) + "/" + c.Code);
                }

                DateOnly last = op.Installments.Max(i => i.Due);
                body.Append("    <Op IPOC=\"").Append(Ipoc(h.Cnpj, c, op)).Append("\" Contrt=\"").Append(op.Contract).Append("\" Mod=\"").Append(op.Modality)
                    .Append("\" OrigemRec=\"").Append(Or(op.Origin, "0101")).Append("\" Indx=\"").Append(Or(op.Index, "11"))
                    .Append("\" PercIndx=\"").Append(Or(op.IndexPct, "0")).Append("\" VarCamb=\"").Append(Or(op.Currency, "790"))
                    .Append("\" CEP=\"").Append(op.Cep).Append("\" TaxEft=\"").Append(op.Rate).Append("\" DtContr=\"").Append(BankDays.Format(op.Contracted))
                    .Append("\" VlrContr=\"").Append(Centavos.Decimal(op.Value)).Append("\" NatuOp=\"").Append(Or(op.Nature, "01"))
                    .Append("\" DtVencOp=\"").Append(BankDays.Format(last)).Append("\" ProvConsttd=\"").Append(Centavos.Decimal(op.Provision)).Append('"');
                int late = DaysLate(op, reference);
                if (late > 0)
                {
                    body.Append(" DiaAtraso=\"").Append(I(late)).Append('"');
                }

                if (op.Special.Count > 0)
                {
                    body.Append(" CaracEspecial=\"").Append(string.Join(';', op.Special)).Append('"');
                }

                body.Append(">\n      <Venc");
                foreach ((int b, long amount) in v)
                {
                    body.Append(" v").Append(I(b)).Append("=\"").Append(Centavos.Decimal(amount)).Append('"');
                }

                body.Append("/>\n    </Op>\n");
            }

            body.Append("  </Cli>\n");
        }

        var output = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        output.Append("<Doc3040 CNPJ=\"").Append(h.Cnpj).Append("\" DtBase=\"").Append(h.DataBase).Append("\" Remessa=\"").Append(I(h.Remessa))
            .Append("\" Parte=\"").Append(I(h.Parte)).Append("\" TpArq=\"F\" NomeResp=\"").Append(Esc(h.NomeResp)).Append("\" EmailResp=\"").Append(Esc(h.EmailResp))
            .Append("\" TelResp=\"").Append(h.TelResp).Append("\" TotalCli=\"").Append(I(exposed.Count)).Append("\">\n");
        output.Append(body).Append("</Doc3040>\n");
        return output.ToString();
    }
}
