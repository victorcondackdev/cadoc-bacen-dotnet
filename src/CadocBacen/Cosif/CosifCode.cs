using System.Globalization;
using System.Reflection;

namespace CadocBacen.Cosif;

/// <summary>A malformed Cosif code.</summary>
public sealed class CosifFormatException : FormatException
{
    /// <summary>Creates the exception.</summary>
    public CosifFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public CosifFormatException()
        : this("Invalid Cosif code.")
    {
    }

    /// <summary>Creates the exception.</summary>
    public CosifFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Side on which an account's balance is normally carried.</summary>
public enum Nature
{
    /// <summary>Devedora.</summary>
    Debit,

    /// <summary>Credora.</summary>
    Credit,
}

/// <summary>
/// A Cosif account code: nine digits in six levels (1, 1, 1, 2, 2, 2) plus a check digit,
/// written 1.1.0.00.00.00-2 or, in XML documents, 1100000002.
/// </summary>
public readonly struct CosifCode : IEquatable<CosifCode>, IComparable<CosifCode>
{
    private static readonly int[] Weights = [1, 7, 3, 1, 7, 3, 1, 7, 3];
    private static readonly int[] LevelEnd = [1, 2, 3, 5, 7, 9];
    private readonly string? _body;

    private CosifCode(string body) => _body = body;

    /// <summary>The nine digits without check digit.</summary>
    public string Body => _body ?? "000000000";

    /// <summary>Check digit of a nine-digit body: digits weighted 1, 7, 3 from the left, complement to the next ten.</summary>
    public static char CheckDigit(string body)
    {
        if (body is null || body.Length != 9 || !body.All(char.IsAsciiDigit))
        {
            throw new CosifFormatException("cosif: code must be 9 digits plus check digit (1.1.0.00.00.00-2 or 1100000002)");
        }

        int sum = 0;
        for (int i = 0; i < 9; i++)
        {
            sum += (body[i] - '0') * Weights[i];
        }

        return (char)('0' + ((10 - (sum % 10)) % 10));
    }

    /// <summary>Parses "1.1.0.00.00.00-2", "1100000002" or the nine-digit body.</summary>
    public static CosifCode Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        string digits = s.Replace(".", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        if (digits.Length is not (9 or 10))
        {
            throw new CosifFormatException("cosif: code must be 9 digits plus check digit (1.1.0.00.00.00-2 or 1100000002)");
        }

        char dv = CheckDigit(digits[..9]);
        if (digits.Length == 10 && digits[9] != dv)
        {
            throw new CosifFormatException("cosif: invalid check digit: " + s + " should end in " + dv);
        }

        return new CosifCode(digits[..9]);
    }

    /// <summary>Parses without throwing.</summary>
    public static bool TryParse(string s, out CosifCode code)
    {
        try
        {
            code = Parse(s);
            return true;
        }
        catch (CosifFormatException)
        {
            code = default;
            return false;
        }
    }

    /// <summary>The check digit.</summary>
    public char Digit => CheckDigit(Body);

    /// <summary>The ten-digit XML form.</summary>
    public string Compact => Body + Digit;

    /// <summary>Dotted form 1.1.0.00.00.00-2.</summary>
    public override string ToString()
    {
        string b = Body;
        return string.Create(CultureInfo.InvariantCulture, $"{b[0]}.{b[1]}.{b[2]}.{b[3..5]}.{b[5..7]}.{b[7..9]}-{Digit}");
    }

    /// <summary>1 (group) to 6 (second-degree subtitle).</summary>
    public int Level
    {
        get
        {
            string b = Body;
            for (int l = 5; l >= 1; l--)
            {
                if (b[LevelEnd[l - 1]..LevelEnd[l]].Trim('0').Length > 0)
                {
                    return l + 1;
                }
            }

            return 1;
        }
    }

    /// <summary>The first digit.</summary>
    public int Group => Body[0] - '0';

    /// <summary>The code one level up, or null for a group.</summary>
    public CosifCode? Parent
    {
        get
        {
            int l = Level;
            if (l == 1)
            {
                return null;
            }

            char[] c = Body.ToCharArray();
            for (int i = LevelEnd[l - 2]; i < 9; i++)
            {
                c[i] = '0';
            }

            return new CosifCode(new string(c));
        }
    }

    /// <summary>Ancestors from the group down.</summary>
    public IReadOnlyList<CosifCode> Ancestors
    {
        get
        {
            var output = new List<CosifCode>();
            for (CosifCode? p = Parent; p is not null; p = p.Value.Parent)
            {
                output.Insert(0, p.Value);
            }

            return output;
        }
    }

    /// <summary>Whether this is a strict ancestor of d.</summary>
    public bool IsAncestorOf(CosifCode d) => d.Ancestors.Contains(this);

    /// <summary>Nature of a group: 1, 2, 3, 8 debit; others credit.</summary>
    public static Nature GroupNature(int group) => group is 1 or 2 or 3 or 8 ? Nature.Debit : Nature.Credit;

    /// <inheritdoc />
    public bool Equals(CosifCode other) => Body == other.Body;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CosifCode c && Equals(c);

    /// <inheritdoc />
    public override int GetHashCode() => Body.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public int CompareTo(CosifCode other) => string.CompareOrdinal(Body, other.Body);

    /// <summary>Equality.</summary>
    public static bool operator ==(CosifCode a, CosifCode b) => a.Equals(b);

    /// <summary>Inequality.</summary>
    public static bool operator !=(CosifCode a, CosifCode b) => !a.Equals(b);

    /// <summary>Ordering.</summary>
    public static bool operator <(CosifCode a, CosifCode b) => a.CompareTo(b) < 0;

    /// <summary>Ordering.</summary>
    public static bool operator >(CosifCode a, CosifCode b) => a.CompareTo(b) > 0;

    /// <summary>Ordering.</summary>
    public static bool operator <=(CosifCode a, CosifCode b) => a.CompareTo(b) <= 0;

    /// <summary>Ordering.</summary>
    public static bool operator >=(CosifCode a, CosifCode b) => a.CompareTo(b) >= 0;
}

/// <summary>A rubric of a chart of accounts.</summary>
/// <param name="Code">Code.</param>
/// <param name="Name">Name.</param>
/// <param name="Contra">Contra account (retificadora): nature opposite to its group.</param>
public sealed record CosifAccount(CosifCode Code, string Name, bool Contra = false)
{
    /// <summary>The account's normal balance side.</summary>
    public Nature Nature
    {
        get
        {
            Nature n = CosifCode.GroupNature(Code.Group);
            return Contra ? (n == Nature.Debit ? Nature.Credit : Nature.Debit) : n;
        }
    }
}

/// <summary>A chart of accounts.</summary>
public sealed class CosifChart
{
    private readonly Dictionary<CosifCode, CosifAccount> _accounts = [];

    /// <summary>Builds a chart; every ancestor must exist and contra accounts must be leaves.</summary>
    public CosifChart(IEnumerable<CosifAccount> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        List<CosifAccount> list = accounts.ToList();
        foreach (CosifAccount a in list)
        {
            if (!_accounts.TryAdd(a.Code, a))
            {
                throw new ArgumentException("cosif: duplicate account " + a.Code);
            }
        }

        foreach (CosifAccount a in list)
        {
            foreach (CosifCode p in a.Code.Ancestors)
            {
                if (!_accounts.TryGetValue(p, out CosifAccount? pa))
                {
                    throw new ArgumentException("cosif: " + a.Code + " has no parent " + p + " in the chart");
                }

                if (pa.Contra)
                {
                    throw new ArgumentException("cosif: contra account " + p + " cannot have children");
                }
            }
        }
    }

    /// <summary>An account by code, or null.</summary>
    public CosifAccount? this[CosifCode code] => _accounts.TryGetValue(code, out CosifAccount? a) ? a : null;

    /// <summary>All accounts ordered by code.</summary>
    public IReadOnlyList<CosifAccount> Accounts => _accounts.Values.OrderBy(a => a.Code).ToList();

    /// <summary>Whether no account has <paramref name="code"/> as ancestor.</summary>
    public bool IsLeaf(CosifCode code) => !_accounts.Keys.Any(k => code.IsAncestorOf(k));

    /// <summary>Reads a CSV chart: code;name[;R], '#' comments.</summary>
    public static CosifChart Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var accounts = new List<CosifAccount>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string[] f = line.Split(';');
            if (f.Length < 2)
            {
                throw new FormatException("cosif: line '" + line + "': want code;name[;R]");
            }

            accounts.Add(new CosifAccount(CosifCode.Parse(f[0].Trim()), f[1].Trim(), f.Length > 2 && f[2].Trim().Equals("R", StringComparison.OrdinalIgnoreCase)));
        }

        return new CosifChart(accounts);
    }

    /// <summary>The illustrative sample chart (not the official elenco de contas).</summary>
    public static CosifChart Sample()
    {
        using Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("CadocBacen.sample_chart.csv")!;
        using var r = new StreamReader(s);
        return Read(r);
    }
}
