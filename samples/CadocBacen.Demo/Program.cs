// A month of a small digital bank: journal entries → trial balance → document 4010,
// then the semiannual 4016 after closing the result, and an SCR 3040 for two borrowers.
using CadocBacen.Contabil;
using CadocBacen.Cosif;
using CadocBacen.Documents;
using CadocBacen.Internal;
using CadocBacen.Scr;

static CosifCode C(string s) => CosifCode.Parse(s);

CosifChart chart = CosifChart.Sample();
var entries = new List<Entry>();
void Add(string debit, string credit, long cents)
{
    entries.Add(new Entry(C(debit), cents, 0));
    entries.Add(new Entry(C(credit), 0, cents));
}

Add("113000000", "611000000", 800_000_00);  // capital paid in
Add("619000000", "611000000", 200_000_00);  // capital to be paid in
Add("113000000", "411000000", 500_000_00);  // demand deposits
Add("113000000", "412000000", 120_000_00);  // savings
Add("161100000", "113000000", 300_000_00);  // personal loans
Add("161200000", "113000000", 150_000_00);  // payroll loans
Add("820000000", "161900000", 13_500_00);   // expected loss provision
Add("161100000", "711000000", 9_870_45);    // interest
Add("161200000", "711000000", 2_310_10);    // interest
Add("113000000", "720000000", 3_455_90);    // fees
Add("830000000", "113000000", 41_220_37);   // administrative expenses
Add("810000000", "412000000", 610_00);      // savings interest
Add("191000000", "491000000", 2_000_00);    // Pix in transit
Add("830000000", "492000000", 1_234_56);    // taxes payable
Add("111000000", "113000000", 5_000_00);    // cash to the vault
Add("310000000", "910000000", 50_000_00);   // guarantees received

Balancete b = Balancete.FromEntries(chart, entries);
Console.WriteLine($"checks: {b.Check().Count} issue(s)");
Doc4010 d = Doc4010.Build(Doc4010.Balancete, "12345678", "2026-10", Doc4010.Inclusion, b);
Console.Write(d.ToXml());
Console.WriteLine($"4010 due {BankDays.Format(Doc4010.Deadline(Doc4010.Balancete, "2026-10"))}; validates: {Doc4010.Parse(d.ToXml()).Validate(chart).Count == 0}");

Balancete closed = b.CloseResult(C("641000000"), C("649000000"));
Doc4010 d16 = Doc4010.Build(Doc4010.Balanco, "12345678", "2026-12", Doc4010.Inclusion, closed);
Console.WriteLine($"4016: {d16.Lines.Count} accounts, due {BankDays.Format(Doc4010.Deadline(Doc4010.Balanco, "2026-12"))}");

static IReadOnlyList<Installment> Monthly(DateOnly first, int n, long cents) => Enumerable.Range(0, n).Select(i => new Installment(first.AddMonths(i), cents)).ToList();

string scr = Doc3040.Build(
    new Doc3040Header("12345678", "2026-10", 1, 1, "Victor Condack", "scr@banco.example", "2133334444"),
    [
        new Client
        {
            Type = 1, Document = "12345678909", Authorized = true, Size = 5, Since = new DateOnly(2024, 3, 15), Income = 8_500_00,
            Operations = [new Operation { Contract = "CP-7781", Modality = "0202", Cep = "26255000", Rate = "28.40", Contracted = new DateOnly(2026, 1, 10), Value = 15_000_00, Provision = 420_00, Installments = Monthly(new DateOnly(2026, 9, 10), 16, 1_062_50) }],
        },
        new Client
        {
            Type = 2, Document = "11222333000181", Authorized = true, Size = 2, Since = new DateOnly(2023, 5, 10), Income = 3_600_000_00,
            Operations = [new Operation { Contract = "CG-2026-0042", Modality = "0216", Cep = "20760005", Rate = "21.90", Contracted = new DateOnly(2026, 4, 15), Value = 240_000_00, Provision = 3_100_00, Installments = Monthly(new DateOnly(2026, 11, 15), 18, 13_333_33) }],
        },
    ]);
Console.Write(scr);
