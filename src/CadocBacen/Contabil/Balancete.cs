using System.Globalization;
using CadocBacen.Cosif;

namespace CadocBacen.Contabil;

/// <summary>Formatting of centavo amounts.</summary>
public static class Centavos
{
    /// <summary>Absolute value as "1234.56", the 4010 XML format.</summary>
    public static string Decimal(long cents)
    {
        long m = Math.Abs(cents);
        return string.Create(CultureInfo.InvariantCulture, $"{m / 100}.{m % 100:D2}");
    }

    /// <summary>Signed "-1234.56".</summary>
    public static string Signed(long cents) => (cents < 0 ? "-" : "") + Decimal(cents);

    /// <summary>"devedora" / "credora".</summary>
    public static string Pt(this Nature n) => n == Nature.Debit ? "devedora" : "credora";
}

/// <summary>A journal entry line: exactly one of Debit and Credit is positive (centavos).</summary>
/// <param name="Account">Leaf account.</param>
/// <param name="Debit">Debit in centavos.</param>
/// <param name="Credit">Credit in centavos.</param>
public sealed record Entry(CosifCode Account, long Debit, long Credit);

/// <summary>One line of a trial balance.</summary>
/// <param name="Account">Account.</param>
/// <param name="Signed">Signed balance, debit positive.</param>
/// <param name="Saldo">Absolute value, as reported.</param>
/// <param name="Side">Side of the balance.</param>
/// <param name="Inverted">Non-zero balance opposite to the account's nature.</param>
public sealed record BalanceteRow(CosifAccount Account, long Signed, long Saldo, Nature Side, bool Inverted);

/// <summary>A finding of <see cref="Balancete.Check"/>.</summary>
/// <param name="Severity">"erro" or "aviso".</param>
/// <param name="Message">Description in Portuguese.</param>
public sealed record Issue(string Severity, string Message)
{
    /// <inheritdoc />
    public override string ToString() => Severity + ": " + Message;
}

/// <summary>A Cosif trial balance with signed balances (debit positive) at every level.</summary>
public sealed class Balancete
{
    private readonly Dictionary<CosifCode, long> _balances;

    private Balancete(CosifChart chart, Dictionary<CosifCode, long> balances)
    {
        Chart = chart;
        _balances = balances;
    }

    /// <summary>The chart.</summary>
    public CosifChart Chart { get; }

    /// <summary>Builds from signed leaf balances.</summary>
    public static Balancete FromLeaves(CosifChart chart, IReadOnlyDictionary<CosifCode, long> leaves)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(leaves);
        var b = new Dictionary<CosifCode, long>();
        foreach (CosifCode c in leaves.Keys.OrderBy(k => k))
        {
            if (chart[c] is null)
            {
                throw new ArgumentException("balancete: account not in chart: " + c);
            }

            if (!chart.IsLeaf(c))
            {
                throw new ArgumentException("balancete: postings must go to leaf accounts: " + c);
            }

            long v = leaves[c];
            b[c] = b.GetValueOrDefault(c) + v;
            foreach (CosifCode a in c.Ancestors)
            {
                b[a] = b.GetValueOrDefault(a) + v;
            }
        }

        return new Balancete(chart, b);
    }

    /// <summary>Builds from journal entries.</summary>
    public static Balancete FromEntries(CosifChart chart, IEnumerable<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var leaves = new Dictionary<CosifCode, long>();
        foreach (Entry e in entries)
        {
            if ((e.Debit > 0) == (e.Credit > 0) || e.Debit < 0 || e.Credit < 0)
            {
                throw new ArgumentException("balancete: entry must have exactly one positive side (" + e.Account + ")");
            }

            leaves[e.Account] = leaves.GetValueOrDefault(e.Account) + e.Debit - e.Credit;
        }

        return FromLeaves(chart, leaves);
    }

    /// <summary>Signed balance of any account.</summary>
    public long Signed(CosifCode code) => _balances.GetValueOrDefault(code);

    /// <summary>Accounts with non-zero balance (or all), ordered by code.</summary>
    public IReadOnlyList<BalanceteRow> Rows(bool all = false)
    {
        var output = new List<BalanceteRow>();
        foreach (CosifAccount a in Chart.Accounts)
        {
            long v = Signed(a.Code);
            if (v == 0 && !all)
            {
                continue;
            }

            Nature side = v < 0 ? Nature.Credit : Nature.Debit;
            output.Add(new BalanceteRow(a, v, Math.Abs(v), side, v != 0 && side != a.Nature));
        }

        return output;
    }

    private static CosifCode GroupCode(int g) => CosifCode.Parse(g.ToString(CultureInfo.InvariantCulture) + "00000000");

    /// <summary>
    /// Double entry, memorandum offset, warnings for analytical accounts with balances contrary
    /// to their nature, and negative equity. Synthetic accounts may net to either side.
    /// </summary>
    public IReadOnlyList<Issue> Check()
    {
        var issues = new List<Issue>();
        long total = 0;
        for (int g = 1; g <= 9; g++)
        {
            total += Signed(GroupCode(g));
        }

        if (total != 0)
        {
            issues.Add(new Issue("erro", "débitos e créditos não fecham: diferença de " + Centavos.Signed(total)));
        }

        long memo = Signed(GroupCode(3)) + Signed(GroupCode(9));
        if (memo != 0)
        {
            issues.Add(new Issue("erro", "compensação ativa (3) e passiva (9) não se anulam: diferença de " + Centavos.Signed(memo)));
        }

        foreach (BalanceteRow r in Rows())
        {
            if (r.Inverted && Chart.IsLeaf(r.Account.Code))
            {
                issues.Add(new Issue("aviso", r.Account.Code + " " + r.Account.Name + " com saldo " + r.Side.Pt() + ", contrário à natureza " + r.Account.Nature.Pt()));
            }
        }

        long pl = Signed(GroupCode(6));
        if (pl > 0)
        {
            issues.Add(new Issue("aviso", "patrimônio líquido negativo: " + Centavos.Signed(-pl)));
        }

        return issues;
    }

    /// <summary>Whether groups 7 or 8 carry balances.</summary>
    public bool HasResult => Signed(GroupCode(7)) != 0 || Signed(GroupCode(8)) != 0;

    /// <summary>
    /// A new trial balance with groups 7 and 8 closed into equity: a profit into
    /// <paramref name="profit"/>, a loss into <paramref name="loss"/> (normally a contra account).
    /// </summary>
    public Balancete CloseResult(CosifCode profit, CosifCode loss)
    {
        foreach (CosifCode c in new[] { profit, loss })
        {
            if (c.Group != 6)
            {
                throw new ArgumentException("balancete: result must close into group 6 accounts, not " + c);
            }
        }

        var leaves = new Dictionary<CosifCode, long>();
        long result = 0;
        foreach (CosifAccount a in Chart.Accounts)
        {
            if (!Chart.IsLeaf(a.Code))
            {
                continue;
            }

            long v = Signed(a.Code);
            if (a.Code.Group is 7 or 8)
            {
                result += v;
                continue;
            }

            if (v != 0)
            {
                leaves[a.Code] = v;
            }
        }

        if (result < 0)
        {
            leaves[profit] = leaves.GetValueOrDefault(profit) + result;
        }
        else if (result > 0)
        {
            leaves[loss] = leaves.GetValueOrDefault(loss) + result;
        }

        return FromLeaves(Chart, leaves);
    }
}
