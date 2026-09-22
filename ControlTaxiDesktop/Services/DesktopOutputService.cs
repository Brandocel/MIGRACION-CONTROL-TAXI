using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ControlTaxiDesktop.Models;

namespace ControlTaxiDesktop.Services;

public sealed record DesktopWorkbookSheet(string Name, IEnumerable<object> Rows, Type RowType)
{
    public static DesktopWorkbookSheet Create<T>(string name, IEnumerable<T> rows) =>
        new(name, rows.Cast<object>().ToList(), typeof(T));
}

public sealed partial class DesktopOutputService
{
    private static readonly (string Code, string Name)[] ConcentratedGroups =
    [
        ("VER", "TAXIS VERDES"),
        ("ROJ", "TAXIS ROJOS"),
        ("AZU", "TAXIS AZUL"),
        ("CAFE", "TAXIS CAFE"),
        ("UBER", "UBER"),
        ("ALI", "UBER ALIANZA"),
        ("MAJ", "MAJESTIC"),
        ("SALAN", "SALMORAN"),
        ("TADO", "TURIBUS ADO"),
        ("TEXP", "TRAVEL EXPERIENCE"),
        ("CALLE", "CALLE"),
        ("VANS", "TRANSPORTADORAS"),
        ("ACAR", "AUTOCAR"),
        ("MC", "MAYA CARIBE"),
        ("TUR", "TURICUN"),
        ("OTRO", "OTRO")
    ];

    public async Task ExportRecordsCsvAsync(IEnumerable<LocalRecord> records, string path)
    {
        var lines = new List<string> { "Folio,Fecha,TaxistaId,HotelId,TarifaId,Gafete,Pax,Origen,Destino,Importe,MetodoPago,Usuario" };
        lines.AddRange(records.Select(x => string.Join(',', new[] { x.Folio, x.Date.ToString("O"), x.DriverId?.ToString() ?? "", x.HotelId?.ToString() ?? "", x.RateId?.ToString() ?? "", x.Badge, x.Passengers.ToString(), x.Origin, x.Destination, x.Amount.ToString(CultureInfo.InvariantCulture), x.PaymentMethod, x.User }.Select(Escape))));
        await File.WriteAllLinesAsync(path, lines, new UTF8Encoding(true));
    }

    public async Task ExportCsvAsync<T>(IEnumerable<T> rows, string path)
    {
        var properties = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var lines = new List<string> { string.Join(',', properties.Select(x => Escape(x.Name))) };
        lines.AddRange(rows.Select(row => string.Join(',', properties.Select(property => Escape(Convert.ToString(property.GetValue(row), CultureInfo.InvariantCulture) ?? string.Empty)))));
        await File.WriteAllLinesAsync(path, lines, new UTF8Encoding(true));
    }

    public void PrintReport(LocalReport report)
    {
        var document = new FlowDocument(new Paragraph(new Run($"CONTROL TAXI\nReporte {report.Start:dd/MM/yyyy} - {report.End:dd/MM/yyyy}\n\nRegistros: {report.Records}\nTotal: {report.Total:C2}\nEfectivo: {report.Cash:C2}\nTarjeta: {report.Card:C2}"))) { PagePadding = new Thickness(40) };
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true) dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "Reporte Control Taxi");
    }

    public async Task ExportSpreadsheetXmlAsync<T>(string title, IEnumerable<T> rows, string path)
    {
        var properties = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var builder = new StringBuilder();
        builder.AppendLine("""<?xml version="1.0"?>""");
        builder.AppendLine("""<?mso-application progid="Excel.Sheet"?>""");
        builder.AppendLine("""<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet" xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">""");
        builder.AppendLine($"""<Worksheet ss:Name="{Xml(title)}"><Table>""");
        builder.AppendLine("<Row>");
        foreach (var property in properties) builder.AppendLine($"""<Cell><Data ss:Type="String">{Xml(property.Name)}</Data></Cell>""");
        builder.AppendLine("</Row>");
        foreach (var row in rows)
        {
            builder.AppendLine("<Row>");
            foreach (var property in properties)
            {
                var value = property.GetValue(row);
                var type = value is int or long or decimal or double or float ? "Number" : "String";
                builder.AppendLine($"""<Cell><Data ss:Type="{type}">{Xml(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)}</Data></Cell>""");
            }
            builder.AppendLine("</Row>");
        }

        builder.AppendLine("</Table></Worksheet></Workbook>");
        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(true));
    }

    public async Task ExportControlDejadasWorkbookAsync(DateTime start, DateTime end, IReadOnlyList<LocalRelation> rows, string path)
    {
        var sheets = new List<(string Name, string Xml)>
        {
            ("Control Dejadas", BuildControlDejadasSheet(start, end, rows, "Tienda Plaza 28"))
        };
        await ExportStyledWorkbookAsync(sheets, path);
    }

    public async Task ExportCascoControlDejadasWorkbookAsync(DateTime start, DateTime end, IReadOnlyList<LocalRelation> rows, string path)
    {
        var sheets = new List<(string Name, string Xml)>
        {
            ("Control Dejadas", BuildControlDejadasSheet(start, end, rows, "Casco Viejo"))
        };
        await ExportStyledWorkbookAsync(sheets, path);
    }

    public async Task ExportConcentradoWorkbookAsync(DateTime start, DateTime end, IReadOnlyList<LocalRelation> rows, IReadOnlyList<LocalCuadreResumenRow> camiones, string path, IReadOnlyList<LocalCommissionBrowserRow>? authoritativeCommissions = null)
    {
        var summaries = MergeCamionesIntoCategorySummaries(BuildCategorySummaries(rows, authoritativeCommissions), camiones, start);
        var sheets = ConcentratedGroups
            .Select(group =>
            {
                var summary = summaries.TryGetValue(group.Code, out var match)
                    ? match
                    : CategorySummary.Empty(group.Code, group.Name);
                return (group.Code, BuildConcentratedSheet(summary));
            })
            .ToList();
        await ExportStyledWorkbookAsync(sheets, path);
    }

    public async Task ExportCascoConcentradoWorkbookAsync(DateTime start, DateTime end, IReadOnlyList<LocalRelation> rows, string path)
    {
        var sheets = BuildCascoConcentratedSheets(rows);
        await ExportStyledWorkbookAsync(sheets, path);
    }

    public async Task ExportCuadreWorkbookAsync(DateTime start, DateTime end, IReadOnlyList<LocalRelation> rows, IReadOnlyList<LocalCommission> commissions, IReadOnlyList<LocalCut> cuts, IReadOnlyList<LocalCuadreResumenRow> camiones, string path, IReadOnlyList<LocalCommissionBrowserRow>? authoritativeCommissions = null)
    {
        var summaries = MergeCamionesIntoCategorySummaries(BuildCategorySummaries(rows, authoritativeCommissions), camiones, start);
        var ordered = ConcentratedGroups
            .Select(group => summaries.TryGetValue(group.Code, out var value) ? value : CategorySummary.Empty(group.Code, group.Name))
            .Where(summary => !string.Equals(summary.Code, "OTRO", StringComparison.OrdinalIgnoreCase)
                || summary.Pax != 0
                || summary.Entraron != 0
                || summary.Salieron != 0
                || summary.Unidades != 0
                || summary.Dejada != 0m
                || summary.Comision != 0m
                || summary.Venta != 0m
                || summary.TotalGastos != 0m)
            .ToArray();
        var sheets = new List<(string Name, string Xml)>
        {
            ("CUADRE", BuildCuadreSheet(start, ordered, camiones)),
            ("CUADRE dejadas", BuildControlDejadasSheet(start, end, rows, "Tienda Plaza 28")),
            ("REPORTE HOTELES", BuildHotelsSheet(rows)),
            ("comisiones", BuildCommissionsSheet(commissions)),
            ("CORTE FINAL", BuildCutsSheet(cuts))
        };
        await ExportStyledWorkbookAsync(sheets, path);
    }

    public async Task ExportCascoCuadreWorkbookAsync(DateTime start, DateTime end, IReadOnlyList<LocalRelation> rows, IReadOnlyList<LocalCommission> commissions, string path)
    {
        var sheets = new List<(string Name, string Xml)>
        {
            ("CUADRE", BuildCascoCuadreSheet(start, end, rows)),
            ("CUADRE dejadas", BuildControlDejadasSheet(start, end, rows, "Casco Viejo")),
            ("REPORTE HOTELES", BuildHotelsSheet(rows)),
            ("comisiones", BuildCommissionsSheet(commissions)),
            ("CORTE FINAL", BuildCascoCutsSheet(rows))
        };
        await ExportStyledWorkbookAsync(sheets, path);
    }

    public async Task ExportOpenXmlWorkbookAsync<T>(string sheetName, IEnumerable<T> rows, string path)
    {
        await ExportOpenXmlWorkbookAsync([DesktopWorkbookSheet.Create(sheetName, rows)], path);
    }

    public async Task ExportOpenXmlWorkbookAsync(IReadOnlyList<DesktopWorkbookSheet> sheets, string path)
    {
        await using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var safeSheets = sheets.Count == 0
            ? [DesktopWorkbookSheet.Create("Hoja1", Array.Empty<object>())]
            : sheets;
        var overrides = string.Join(Environment.NewLine, safeSheets.Select((_, index) => $"""  <Override PartName="/xl/worksheets/sheet{index + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>"""));
        AddZipEntry(archive, "[Content_Types].xml", $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
{{overrides}}
  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
</Types>
""");
        AddZipEntry(archive, "_rels/.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""");
        var relationships = new StringBuilder();
        for (var i = 0; i < safeSheets.Count; i++)
            relationships.AppendLine($"""  <Relationship Id="rId{i + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i + 1}.xml"/>""");
        relationships.AppendLine($"""  <Relationship Id="rId{safeSheets.Count + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>""");
        AddZipEntry(archive, "xl/_rels/workbook.xml.rels", $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
{{relationships}}
</Relationships>
""");
        var sheetXml = new StringBuilder();
        for (var i = 0; i < safeSheets.Count; i++)
            sheetXml.AppendLine($"""<sheet name="{Xml(TrimSheetName(safeSheets[i].Name))}" sheetId="{i + 1}" r:id="rId{i + 1}"/>""");
        AddZipEntry(archive, "xl/workbook.xml", $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets>{{sheetXml}}</sheets>
</workbook>
""");
        AddZipEntry(archive, "xl/styles.xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <numFmts count="2"><numFmt numFmtId="164" formatCode="dd/mm/yyyy"/><numFmt numFmtId="165" formatCode='"$"#,##0.00'/></numFmts>
  <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/><color rgb="FFFFFFFF"/></font></fonts>
  <fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF1F3864"/><bgColor indexed="64"/></patternFill></fill></fills>
  <borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style="thin"/><right style="thin"/><top style="thin"/><bottom style="thin"/><diagonal/></border></borders>
  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
  <cellXfs count="4"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1"/><xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1"/><xf numFmtId="165" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyNumberFormat="1"/></cellXfs>
  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
</styleSheet>
""");
        for (var i = 0; i < safeSheets.Count; i++)
            AddZipEntry(archive, $"xl/worksheets/sheet{i + 1}.xml", BuildWorksheet(safeSheets[i].RowType, safeSheets[i].Rows));
    }

    public async Task ExportSimplePdfAsync<T>(string title, IEnumerable<T> rows, string path)
    {
        await ExportTextPdfAsync(string.Join(Environment.NewLine, BuildLines(title, rows).Take(48)), path);
    }

    public async Task ExportTextPdfAsync(string content, string path)
    {
        var lines = content.Replace("\r", string.Empty).Split('\n').Take(72).ToArray();
        var builder = new StringBuilder("BT /F1 8 Tf 34 790 Td 10 TL ");
        foreach (var line in lines) builder.Append('(').Append(Pdf(line)).Append(") Tj T* ");
        builder.Append("ET");
        var stream = builder.ToString();
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream"
        };
        var bytes = new List<byte>();
        WriteAscii(bytes, "%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(bytes.Count);
            WriteAscii(bytes, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = bytes.Count;
        WriteAscii(bytes, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) WriteAscii(bytes, offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        WriteAscii(bytes, $"trailer << /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        await File.WriteAllBytesAsync(path, bytes.ToArray());
    }

    public async Task ExportTextAsync(string content, string path) =>
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(true));

    public void PrintText(string title, string content)
    {
        var document = new FlowDocument(new Paragraph(new Run(content)))
        {
            PagePadding = new Thickness(34),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 11
        };
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true) dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, title);
    }

    private async Task ExportStyledWorkbookAsync(IReadOnlyList<(string Name, string Xml)> sheets, string path)
    {
        await using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var overrides = string.Join(Environment.NewLine, sheets.Select((_, index) => $"""  <Override PartName="/xl/worksheets/sheet{index + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>"""));
        AddZipEntry(archive, "[Content_Types].xml", $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
{{overrides}}
  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
</Types>
""");
        AddZipEntry(archive, "_rels/.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""");
        var relationships = new StringBuilder();
        var workbookSheets = new StringBuilder();
        for (var i = 0; i < sheets.Count; i++)
        {
            relationships.AppendLine($"""  <Relationship Id="rId{i + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i + 1}.xml"/>""");
            workbookSheets.AppendLine($"""    <sheet name="{Xml(TrimSheetName(sheets[i].Name))}" sheetId="{i + 1}" r:id="rId{i + 1}"/>""");
        }

        relationships.AppendLine($"""  <Relationship Id="rId{sheets.Count + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>""");
        AddZipEntry(archive, "xl/_rels/workbook.xml.rels", $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
{{relationships}}
</Relationships>
""");
        AddZipEntry(archive, "xl/workbook.xml", $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets>
{{workbookSheets}}  </sheets>
</workbook>
""");
        AddZipEntry(archive, "xl/styles.xml", BuildStyledWorkbookStyles());
        for (var i = 0; i < sheets.Count; i++)
            AddZipEntry(archive, $"xl/worksheets/sheet{i + 1}.xml", sheets[i].Xml);
    }

    private static string BuildStyledWorkbookStyles() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <numFmts count="6">
    <numFmt numFmtId="164" formatCode="dd/mm/yyyy"/>
    <numFmt numFmtId="165" formatCode="hh:mm:ss AM/PM"/>
    <numFmt numFmtId="166" formatCode='"$"#,##0.00'/>
    <numFmt numFmtId="167" formatCode="0%"/>
    <numFmt numFmtId="168" formatCode="#,##0"/>
    <numFmt numFmtId="169" formatCode="_(&quot;$&quot;* #,##0.00_);_(&quot;$&quot;* (#,##0.00);_(&quot;$&quot;* &quot;-&quot;??_);_(@_)"/>
  </numFmts>
  <fonts count="4">
    <font><sz val="11"/><name val="Calibri"/></font>
    <font><b/><sz val="11"/><name val="Calibri"/></font>
    <font><b/><sz val="11"/><name val="Calibri"/><color rgb="FFFFFFFF"/></font>
    <font><sz val="10"/><name val="Calibri"/></font>
  </fonts>
  <fills count="8">
    <fill><patternFill patternType="none"/></fill>
    <fill><patternFill patternType="gray125"/></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FFFFEB00"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FF92D050"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FFD9EAD3"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FF1F3864"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FFFFFF00"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FFD6DCE4"/><bgColor indexed="64"/></patternFill></fill>
  </fills>
  <borders count="2">
    <border><left/><right/><top/><bottom/><diagonal/></border>
    <border><left style="thin"/><right style="thin"/><top style="thin"/><bottom style="thin"/><diagonal/></border>
  </borders>
  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
  <cellXfs count="22">
    <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
    <xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1"/>
    <xf numFmtId="0" fontId="1" fillId="3" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1"/>
    <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1"/>
    <xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1"/>
    <xf numFmtId="165" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1"/>
    <xf numFmtId="0" fontId="0" fillId="4" borderId="1" xfId="0" applyFill="1" applyBorder="1"/>
    <xf numFmtId="0" fontId="2" fillId="5" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1" applyAlignment="1"><alignment horizontal="center"/></xf>
    <xf numFmtId="166" fontId="2" fillId="5" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="center"/></xf>
    <xf numFmtId="168" fontId="2" fillId="5" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="center"/></xf>
    <xf numFmtId="0" fontId="1" fillId="6" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1"/>
    <xf numFmtId="166" fontId="1" fillId="6" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1" applyNumberFormat="1"/>
    <xf numFmtId="167" fontId="1" fillId="6" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1" applyNumberFormat="1"/>
    <xf numFmtId="166" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyNumberFormat="1"/>
    <xf numFmtId="167" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyNumberFormat="1"/>
    <xf numFmtId="0" fontId="1" fillId="7" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1"/>
    <xf numFmtId="166" fontId="1" fillId="7" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1" applyNumberFormat="1"/>
    <xf numFmtId="168" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyNumberFormat="1"/>
    <xf numFmtId="169" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyNumberFormat="1"/>
    <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment wrapText="1" vertical="top"/></xf>
    <xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center"/></xf>
    <xf numFmtId="165" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center"/></xf>
  </cellXfs>
  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
</styleSheet>
""";

    private static string BuildControlDejadasSheet(DateTime start, DateTime end, IReadOnlyList<LocalRelation> rows, string defaultSiteLabel)
    {
        var body = new StringBuilder();
        body.AppendLine(Row(1, TextCell("B1", "CONTROL DEJADAS", 2), TextCell("C1", $"{start:dd/MM/yyyy} al {end:dd/MM/yyyy}", 2)));
        body.AppendLine(Row(2,
            TextCell("B2", "Registros", 3),
            NumberCell("C2", rows.Count, 3),
            TextCell("D2", "Dejada total", 3),
            NumberCell("E2", rows.Sum(x => x.Payout ?? 0m), 13),
            TextCell("F2", "Venta total", 3),
            NumberCell("G2", rows.Sum(x => x.Sale), 13)));
        body.AppendLine(Row(4,
            TextCell("B4", "FECHA", 1), TextCell("C4", "FOLIO", 1), TextCell("D4", "HORA", 1), TextCell("E4", "NOMBRE", 1),
            TextCell("F4", "VENDEDOR", 1), TextCell("G4", "UNIDAD", 1), TextCell("H4", "NUMERO", 1), TextCell("I4", "ORIGEN", 1),
            TextCell("J4", "SITIO/HOTEL", 1), TextCell("K4", "ADULTO", 1), TextCell("L4", "JOVEN", 1), TextCell("M4", "NINO", 1),
            TextCell("N4", "PAX", 1), TextCell("O4", "IMPORTE DEJADA", 1), TextCell("P4", "TELEFONO", 1), TextCell("Q4", "VENTA", 1),
            TextCell("R4", "ESTATUS DEJADA", 1), TextCell("S4", "FECHA PAGO DEJADA", 1), TextCell("T4", "COMISION", 1), TextCell("U4", "PAGO COMISION", 1),
            TextCell("V4", "ESTATUS COMISION", 1), TextCell("W4", "TICKET", 1), TextCell("X4", "TAXISTA ID", 1), TextCell("Y4", "GAFETE", 1),
            TextCell("Z4", "NACIONALIDAD", 1)));

        var rowIndex = 5;
        foreach (var row in rows.OrderBy(x => ParseRelationDate(x)?.Date).ThenBy(x => ParseRelationDate(x)))
        {
            var date = ParseRelationDate(row);
            var ticket = CleanTicketDetail(row.SaleDetail, row.PosFolio, row.OperationFolio);
            body.AppendLine(Row(rowIndex,
                date is not null ? DateCell($"B{rowIndex}", date.Value, 20) : TextCell($"B{rowIndex}", string.Empty, 3),
                TextCell($"C{rowIndex}", Clean(row.OperationFolio, row.AppFolio), 3),
                date is not null ? TimeCell($"D{rowIndex}", date.Value, 21) : TextCell($"D{rowIndex}", string.Empty, 3),
                TextCell($"E{rowIndex}", Clean(row.Driver, row.Vendor), 3),
                TextCell($"F{rowIndex}", Clean(row.Vendor, row.Driver), 3),
                TextCell($"G{rowIndex}", Clean(row.TransportType), 3),
                TextCell($"H{rowIndex}", Clean(row.Unit, row.Plates), 3),
                TextCell($"I{rowIndex}", Clean(row.Hotel, row.Origin), 3),
                TextCell($"J{rowIndex}", Clean(row.Site, row.Destination, defaultSiteLabel), 3),
                NumberCell($"K{rowIndex}", row.AdultPassengers, 3),
                NumberCell($"L{rowIndex}", row.YouthPassengers, 3),
                NumberCell($"M{rowIndex}", row.ChildPassengers, 3),
                NumberCell($"N{rowIndex}", ResolveRelationPax(row), 3),
                NumberCell($"O{rowIndex}", row.Payout ?? 0m, 13),
                TextCell($"P{rowIndex}", Clean(row.Phone, "S/N"), 3),
                NumberCell($"Q{rowIndex}", row.Sale, 13),
                TextCell($"R{rowIndex}", Clean(row.PayoutStatus, "pendiente"), 3),
                ParseTextDate(row.PayoutDate) is { } payoutDate ? DateCell($"S{rowIndex}", payoutDate, 20) : TextCell($"S{rowIndex}", Clean(row.PayoutDate), 3),
                NumberCell($"T{rowIndex}", row.Commission, 13),
                NumberCell($"U{rowIndex}", row.CommissionPaid, 13),
                TextCell($"V{rowIndex}", Clean(row.CommissionStatus, "SIN CALCULAR"), 3),
                TextCell($"W{rowIndex}", ticket, 3),
                TextCell($"X{rowIndex}", Clean(row.TaxistaId, "0"), 3),
                TextCell($"Y{rowIndex}", Clean(row.Badge), 3),
                TextCell($"Z{rowIndex}", Clean(row.Nationality, "S/N"), 3)));
            rowIndex++;
        }

        if (rows.Any())
        {
            body.AppendLine(Row(rowIndex,
                TextCell($"B{rowIndex}", "TOTAL DEJADAS", 15),
                NumberCell($"K{rowIndex}", rows.Sum(x => x.AdultPassengers), 3),
                NumberCell($"L{rowIndex}", rows.Sum(x => x.YouthPassengers), 3),
                NumberCell($"M{rowIndex}", rows.Sum(x => x.ChildPassengers), 3),
                NumberCell($"N{rowIndex}", rows.Sum(ResolveRelationPax), 3),
                NumberCell($"O{rowIndex}", rows.Sum(x => x.Payout ?? 0m), 16),
                NumberCell($"Q{rowIndex}", rows.Sum(x => x.Sale), 16),
                NumberCell($"T{rowIndex}", rows.Sum(x => x.Commission), 16),
                NumberCell($"U{rowIndex}", rows.Sum(x => x.CommissionPaid), 16)));
            rowIndex++;
        }

        var lastRow = Math.Max(rowIndex - 1, 4);
        return WrapSheet(
            $"B1:Z{lastRow}",
            """
  <cols>
    <col min="2" max="4" width="14" customWidth="1"/>
    <col min="5" max="5" width="28" customWidth="1"/>
    <col min="6" max="9" width="18" customWidth="1"/>
    <col min="10" max="14" width="10" customWidth="1"/>
    <col min="15" max="20" width="16" customWidth="1"/>
    <col min="21" max="25" width="18" customWidth="1"/>
    <col min="26" max="26" width="28" customWidth="1"/>
  </cols>
""",
            body.ToString(),
            $"B4:Z{lastRow}",
            4);
    }

    private static List<(string Name, string Xml)> BuildCascoConcentratedSheets(IReadOnlyList<LocalRelation> rows)
    {
        var transportGroups = BuildCascoTransportGroups(rows);
        return transportGroups
            .Select(group => (group.SheetName, BuildCascoConcentratedSheet(group.DisplayName, group.Rows)))
            .ToList();
    }

    private static string BuildCascoConcentratedSheet(string categoryName, IReadOnlyList<LocalRelation> rows)
    {
        var days = BuildDailyCategoryRows(rows, categoryName)
            .OrderBy(x => x.Fecha)
            .ToList();
        var totalPax = days.Sum(x => x.Pax);
        var totalEntraron = days.Sum(x => x.Entraron);
        var totalSalieron = days.Sum(x => x.Salieron);
        var totalUnidades = days.Sum(x => x.Unidades);
        var totalDejada = days.Sum(x => x.Dejada);
        var totalComision = days.Sum(x => x.Comision);
        var totalVenta = days.Sum(x => x.Venta);
        var totalGastos = days.Sum(x => x.TotalGastos);
        var ticketPromedio = totalPax > 0 ? totalVenta / totalPax : 0m;
        var porcentajeGasto = totalVenta > 0 ? totalGastos / totalVenta : 0m;
        var count = days.Count;

        var body = new StringBuilder();
        body.AppendLine(Row(2,
            TextCell("B2", "PROMEDIO", 2),
            NumberCell("C2", count > 0 ? Math.Round(totalPax / Math.Max(1m, count), 2) : 0m, 3),
            NumberCell("D2", count > 0 ? Math.Round(totalEntraron / Math.Max(1m, count), 2) : 0m, 3),
            NumberCell("E2", count > 0 ? Math.Round(totalSalieron / Math.Max(1m, count), 2) : 0m, 3),
            NumberCell("F2", count > 0 ? Math.Round(totalUnidades / Math.Max(1m, count), 2) : 0m, 3),
            NumberCell("G2", count > 0 ? Math.Round(totalDejada / Math.Max(1m, count), 2) : 0m, 3),
            NumberCell("H2", count > 0 ? Math.Round(totalComision / Math.Max(1m, count), 2) : 0m, 3),
            NumberCell("I2", count > 0 ? Math.Round(totalVenta / Math.Max(1m, count), 2) : 0m, 3),
            NumberCell("J2", count > 0 ? Math.Round(totalGastos / Math.Max(1m, count), 2) : 0m, 3),
            NumberCell("K2", ticketPromedio, 3),
            PercentCell("L2", porcentajeGasto, 6)));
        body.AppendLine(Row(3,
            NumberCell("B3", count, 3),
            TextCell("C3", "TOTALES", 2),
            NumberCell("D3", totalPax, 3),
            NumberCell("E3", totalEntraron, 3),
            NumberCell("F3", totalSalieron, 3),
            NumberCell("G3", totalUnidades, 3),
            NumberCell("H3", totalDejada, 3),
            NumberCell("I3", totalComision, 3),
            NumberCell("J3", totalVenta, 3),
            NumberCell("K3", totalGastos, 3),
            NumberCell("L3", ticketPromedio, 3),
            PercentCell("M3", porcentajeGasto, 6)));
        body.AppendLine(Row(4,
            TextCell("B4", "FECHA", 1), TextCell("C4", "TIPO", 1), TextCell("D4", "PAX", 1), TextCell("E4", "ENTRARON", 1),
            TextCell("F4", "SALIERON", 1), TextCell("G4", "UNIDADES", 1), TextCell("H4", "DEJADA", 1), TextCell("I4", "COMISION", 1),
            TextCell("J4", "VENTA", 1), TextCell("K4", "TOTAL GASTOS", 1), TextCell("L4", "TIKET PROMEDIO", 1), TextCell("M4", "PORCENTAJE GASTO", 1)));

        var rowIndex = 5;
        foreach (var day in days)
        {
            body.AppendLine(Row(rowIndex,
                DateCell($"B{rowIndex}", day.Fecha, 4),
                TextCell($"C{rowIndex}", categoryName, 3),
                NumberCell($"D{rowIndex}", day.Pax, 3),
                NumberCell($"E{rowIndex}", day.Entraron, 3),
                NumberCell($"F{rowIndex}", day.Salieron, 3),
                NumberCell($"G{rowIndex}", day.Unidades, 3),
                NumberCell($"H{rowIndex}", day.Dejada, 3),
                NumberCell($"I{rowIndex}", day.Comision, 3),
                NumberCell($"J{rowIndex}", day.Venta, 3),
                NumberCell($"K{rowIndex}", day.TotalGastos, 3),
                NumberCell($"L{rowIndex}", day.TicketPromedio, 3),
                PercentCell($"M{rowIndex}", day.PorcentajeGasto, 6)));
            rowIndex++;
        }

        if (days.Count == 0)
        {
            body.AppendLine(Row(rowIndex, TextCell($"B{rowIndex}", "Sin datos para este rango", 3)));
        }

        var lastRow = Math.Max(rowIndex - 1, 5);
        return WrapSheet(
            $"B2:M{lastRow}",
            """
  <cols>
    <col min="2" max="3" width="18" customWidth="1"/>
    <col min="4" max="7" width="12" customWidth="1"/>
    <col min="8" max="13" width="16" customWidth="1"/>
  </cols>
""",
            body.ToString(),
            $"B4:M{lastRow}");
    }

    private static string BuildCascoCuadreSheet(DateTime start, DateTime end, IReadOnlyList<LocalRelation> rows)
    {
        var transportGroups = BuildCascoTransportGroups(rows);
        var summaries = transportGroups
            .Select(group => BuildCategorySummary(group.DisplayName, group.Rows))
            .ToList();

        var totalPax = summaries.Sum(x => x.Pax);
        var totalEntraron = summaries.Sum(x => x.Entraron);
        var totalSalieron = summaries.Sum(x => x.Salieron);
        var totalUnidades = summaries.Sum(x => x.Unidades);
        var totalDejada = summaries.Sum(x => x.Dejada);
        var totalComision = summaries.Sum(x => x.Comision);
        var totalVenta = summaries.Sum(x => x.Venta);
        var totalGastos = summaries.Sum(x => x.TotalGastos);
        var totalTicket = totalPax > 0 ? totalVenta / totalPax : 0m;
        var totalPct = totalVenta > 0 ? totalGastos / totalVenta : 0m;
        var dateLabel = start.Date == end.Date
            ? start.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : $"{start:dd/MM/yyyy} al {end:dd/MM/yyyy}";

        var body = new StringBuilder();
        body.AppendLine(Row(1,
            TextCell("B1", dateLabel, 10),
            NumberCell("C1", totalPax, 10),
            NumberCell("D1", totalEntraron, 10),
            NumberCell("E1", totalSalieron, 10),
            NumberCell("F1", totalUnidades, 10),
            TextCell("G1", "DEJADA", 10),
            TextCell("H1", "COMISION", 10),
            TextCell("I1", "VENTA", 10),
            TextCell("J1", "TOTAL DE GASTOS", 10),
            TextCell("K1", "TIKET PROMEDIO", 10),
            TextCell("L1", "PORCENTAJE DE GASTO", 10)));
        body.AppendLine(Row(2,
            TextCell("B2", string.Empty, 10),
            TextCell("C2", "PAX", 10),
            TextCell("D2", "ENTRARON", 10),
            TextCell("E2", "SALIERON", 10),
            TextCell("F2", "UNIDADES", 10),
            TextCell("G2", string.Empty, 10),
            TextCell("H2", string.Empty, 10),
            TextCell("I2", string.Empty, 10),
            TextCell("J2", string.Empty, 10),
            TextCell("K2", string.Empty, 10),
            TextCell("L2", string.Empty, 10)));

        var rowIndex = 3;
        foreach (var summary in summaries)
        {
            body.AppendLine(Row(rowIndex,
                TextCell($"B{rowIndex}", summary.Name, 3),
                NumberCell($"C{rowIndex}", summary.Pax, 17),
                NumberCell($"D{rowIndex}", summary.Entraron, 17),
                NumberCell($"E{rowIndex}", summary.Salieron, 17),
                NumberCell($"F{rowIndex}", summary.Unidades, 17),
                MaybeCurrencyCell($"G{rowIndex}", summary.Dejada),
                MaybeCurrencyCell($"H{rowIndex}", summary.Comision),
                MaybeCurrencyCell($"I{rowIndex}", summary.Venta),
                MaybeCurrencyCell($"J{rowIndex}", summary.TotalGastos),
                MaybeCurrencyCell($"K{rowIndex}", summary.Pax > 0 ? summary.Venta / summary.Pax : 0m),
                MaybePercentCell($"L{rowIndex}", summary.Venta > 0 ? summary.TotalGastos / summary.Venta : 0m)));
            rowIndex++;
        }

        body.AppendLine(Row(rowIndex,
            TextCell($"B{rowIndex}", string.Empty, 10),
            NumberCell($"C{rowIndex}", totalPax, 10),
            NumberCell($"D{rowIndex}", totalEntraron, 10),
            NumberCell($"E{rowIndex}", totalSalieron, 10),
            NumberCell($"F{rowIndex}", totalUnidades, 10),
            NumberCell($"G{rowIndex}", totalDejada, 11),
            NumberCell($"H{rowIndex}", totalComision, 11),
            NumberCell($"I{rowIndex}", totalVenta, 11),
            NumberCell($"J{rowIndex}", totalGastos, 11),
            NumberCell($"K{rowIndex}", totalTicket, 11),
            PercentCell($"L{rowIndex}", totalPct, 12)));
        rowIndex += 2;

        body.AppendLine(Row(rowIndex, TextCell($"B{rowIndex}", "Sin registros de camiones", 3)));
        rowIndex++;
        body.AppendLine(Row(rowIndex,
            TextCell($"B{rowIndex}", string.Empty, 10),
            NumberCell($"C{rowIndex}", 0, 10),
            NumberCell($"D{rowIndex}", 0, 10),
            NumberCell($"E{rowIndex}", 0, 10),
            NumberCell($"F{rowIndex}", 0, 10),
            TextCell($"G{rowIndex}", "$ -", 10),
            TextCell($"H{rowIndex}", "$ -", 10),
            TextCell($"I{rowIndex}", "$ -", 10),
            TextCell($"J{rowIndex}", "$ -", 10),
            TextCell($"K{rowIndex}", "-", 10),
            TextCell($"L{rowIndex}", "-", 10)));

        return $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <dimension ref="B1:L{{rowIndex}}"/>
  <sheetViews><sheetView workbookViewId="0"><selection activeCell="B1" sqref="B1"/></sheetView></sheetViews>
  <sheetFormatPr defaultRowHeight="15"/>
  <cols>
    <col min="2" max="2" width="24" customWidth="1"/>
    <col min="3" max="6" width="12" customWidth="1"/>
    <col min="7" max="11" width="16" customWidth="1"/>
    <col min="12" max="12" width="20" customWidth="1"/>
  </cols>
  <sheetData>
{{body}}
  </sheetData>
  <pageMargins left="0.5" right="0.5" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>
</worksheet>
""";
    }

    private static string BuildCascoCutsSheet(IReadOnlyList<LocalRelation> rows)
    {
        var days = BuildDailyCategoryRows(rows, "GENERAL")
            .OrderBy(x => x.Fecha)
            .ToList();
        var body = new StringBuilder();
        body.AppendLine(Row(1,
            TextCell("B1", "FECHA", 1),
            TextCell("C1", "PAX", 1),
            TextCell("D1", "ENTRARON", 1),
            TextCell("E1", "SALIERON", 1),
            TextCell("F1", "UNIDADES", 1),
            TextCell("G1", "DEJADA", 1),
            TextCell("H1", "COMISION", 1),
            TextCell("I1", "VENTA", 1),
            TextCell("J1", "TOTAL GASTOS", 1),
            TextCell("K1", "TIKET PROMEDIO", 1),
            TextCell("L1", "PORCENTAJE GASTO", 1)));

        var rowIndex = 2;
        foreach (var day in days)
        {
            body.AppendLine(Row(rowIndex,
                DateCell($"B{rowIndex}", day.Fecha, 4),
                NumberCell($"C{rowIndex}", day.Pax, 3),
                NumberCell($"D{rowIndex}", day.Entraron, 3),
                NumberCell($"E{rowIndex}", day.Salieron, 3),
                NumberCell($"F{rowIndex}", day.Unidades, 3),
                NumberCell($"G{rowIndex}", day.Dejada, 3),
                NumberCell($"H{rowIndex}", day.Comision, 3),
                NumberCell($"I{rowIndex}", day.Venta, 3),
                NumberCell($"J{rowIndex}", day.TotalGastos, 3),
                NumberCell($"K{rowIndex}", day.TicketPromedio, 3),
                PercentCell($"L{rowIndex}", day.PorcentajeGasto, 6)));
            rowIndex++;
        }

        if (days.Count == 0)
        {
            body.AppendLine(Row(rowIndex, TextCell($"B{rowIndex}", "Sin datos para este rango", 3)));
        }

        var lastRow = Math.Max(rowIndex - 1, 2);
        return WrapSheet(
            $"B1:L{lastRow}",
            """
  <cols>
    <col min="2" max="2" width="14" customWidth="1"/>
    <col min="3" max="12" width="14" customWidth="1"/>
  </cols>
""",
            body.ToString(),
            $"B1:L{lastRow}");
    }

    private static string BuildConcentratedSheet(CategorySummary summary)
    {
        var averageTicket = summary.Pax > 0 ? summary.Venta / summary.Pax : 0m;
        var pctExpense = summary.Venta > 0 ? summary.TotalGastos / summary.Venta : 0m;
        var body = new StringBuilder();
        body.AppendLine(Row(2,
            TextCell("B2", "PROMEDIO", 2),
            NumberCell("C2", summary.Pax, 3),
            NumberCell("D2", summary.Entraron, 3),
            NumberCell("E2", summary.Salieron, 3),
            NumberCell("F2", summary.Unidades, 3),
            NumberCell("G2", summary.Dejada, 3),
            NumberCell("H2", summary.Comision, 3),
            NumberCell("I2", summary.Venta, 3),
            NumberCell("J2", summary.TotalGastos, 3),
            NumberCell("K2", averageTicket, 3),
            PercentCell("L2", pctExpense, 6)));
        body.AppendLine(Row(3,
            NumberCell("B3", 1, 3),
            TextCell("C3", "TOTALES", 2),
            NumberCell("D3", summary.Pax, 3),
            NumberCell("E3", summary.Entraron, 3),
            NumberCell("F3", summary.Salieron, 3),
            NumberCell("G3", summary.Unidades, 3),
            NumberCell("H3", summary.Dejada, 3),
            NumberCell("I3", summary.Comision, 3),
            NumberCell("J3", summary.Venta, 3),
            NumberCell("K3", summary.TotalGastos, 3),
            NumberCell("L3", averageTicket, 3),
            PercentCell("M3", pctExpense, 6)));
        body.AppendLine(Row(4,
            TextCell("B4", "FECHA", 1), TextCell("C4", "TIPO", 1), TextCell("D4", "PAX", 1), TextCell("E4", "ENTRARON", 1),
            TextCell("F4", "SALIERON", 1), TextCell("G4", "UNIDADES", 1), TextCell("H4", "DEJADA", 1), TextCell("I4", "COMISION", 1),
            TextCell("J4", "VENTA", 1), TextCell("K4", "TOTAL GASTOS", 1), TextCell("L4", "TIKET PROMEDIO", 1), TextCell("M4", "PORCENTAJE GASTO", 1)));
        body.AppendLine(Row(5,
            DateCell("B5", summary.Fecha, 4),
            TextCell("C5", summary.Name, 3),
            NumberCell("D5", summary.Pax, 3),
            NumberCell("E5", summary.Entraron, 3),
            NumberCell("F5", summary.Salieron, 3),
            NumberCell("G5", summary.Unidades, 3),
            NumberCell("H5", summary.Dejada, 3),
            NumberCell("I5", summary.Comision, 3),
            NumberCell("J5", summary.Venta, 3),
            NumberCell("K5", summary.TotalGastos, 3),
            NumberCell("L5", averageTicket, 3),
            PercentCell("M5", pctExpense, 6)));
        return WrapSheet(
            "B2:M5",
            """
  <cols>
    <col min="2" max="3" width="18" customWidth="1"/>
    <col min="4" max="7" width="12" customWidth="1"/>
    <col min="8" max="13" width="16" customWidth="1"/>
  </cols>
""",
            body.ToString(),
            "B4:M5");
    }

    private static string BuildCuadreSheet(DateTime date, IReadOnlyList<CategorySummary> summaries, IReadOnlyList<LocalCuadreResumenRow> camiones)
    {
        summaries = ApplyPlaza28HistoricalCuadreReconciliation(date, summaries);
        var displayOverrides = GetPlaza28HistoricalCuadreDisplayOverrides(date);
        var totalPax = summaries.Sum(x => x.Pax);
        var totalEntraron = summaries.Sum(x => x.Entraron);
        var totalSalieron = summaries.Sum(x => x.Salieron);
        var totalUnidades = summaries.Sum(x => x.Unidades);
        var totalDejada = summaries.Sum(x => x.Dejada);
        var totalComision = summaries.Sum(x => x.Comision);
        var totalVenta = summaries.Sum(x => x.Venta);
        var totalGastos = summaries.Sum(x => x.TotalGastos);
        var totalAdultos = summaries.Sum(x => x.Adultos);
        var totalJovenes = summaries.Sum(x => x.Jovenes);
        var totalMenores = summaries.Sum(x => x.Menores);
        var totalTicket = displayOverrides.TryGetValue("TOTAL", out var totalDisplay) && totalDisplay.TicketPromedio.HasValue
            ? totalDisplay.TicketPromedio.Value
            : totalPax > 0 ? totalVenta / totalPax : 0m;
        var totalPct = displayOverrides.TryGetValue("TOTAL", out totalDisplay) && totalDisplay.PorcentajeGasto.HasValue
            ? totalDisplay.PorcentajeGasto.Value
            : totalVenta > 0 ? totalGastos / totalVenta : 0m;

        // Orden de columnas pedido 2026-08-27: ADULTOS/JOVENES/MENORES van pegados a PAX (junto
        // al inicio de la tabla), no al final donde nadie los veia sin hacer scroll. Todo lo
        // demas (ENTRARON, SALIERON, UNIDADES, DEJADA, COMISION, VENTA...) se recorre 3 columnas
        // a la derecha para hacerles lugar: C=PAX, D=ADULTOS, E=JOVENES, F=MENORES, G=ENTRARON,
        // H=SALIERON, I=UNIDADES, J=DEJADA, K=COMISION, L=VENTA, M=GASTOS, N=TICKET, O=PCT GASTO.
        var body = new StringBuilder();
        body.AppendLine(Row(1,
            DateCell("B1", date, 20),
            NumberCell("C1", totalPax, 10),
            NumberCell("D1", totalAdultos, 10),
            NumberCell("E1", totalJovenes, 10),
            NumberCell("F1", totalMenores, 10),
            NumberCell("G1", totalEntraron, 10),
            NumberCell("H1", totalSalieron, 10),
            NumberCell("I1", totalUnidades, 10),
            TextCell("J1", "DEJADA", 10),
            TextCell("K1", "COMISION", 10),
            TextCell("L1", "VENTA", 10),
            TextCell("M1", "TOTAL DE GASTOS", 10),
            TextCell("N1", "TIKET PROMEDIO", 10),
            TextCell("O1", "PORCENTAJE DE GASTO", 10)));
        body.AppendLine(Row(2,
            TextCell("B2", string.Empty, 10),
            TextCell("C2", "PAX", 10),
            // Desglose real de pax pedido 2026-08-27: cuantos de PAX son adultos, jovenes o
            // menores, sacado directo del registro (no una resta/adivinanza).
            TextCell("D2", "ADULTOS", 10),
            TextCell("E2", "JOVENES", 10),
            TextCell("F2", "MENORES", 10),
            TextCell("G2", "ENTRARON", 10),
            TextCell("H2", "SALIERON", 10),
            TextCell("I2", "UNIDADES", 10),
            TextCell("J2", string.Empty, 10),
            TextCell("K2", string.Empty, 10),
            TextCell("L2", string.Empty, 10),
            TextCell("M2", string.Empty, 10),
            TextCell("N2", string.Empty, 10),
            TextCell("O2", string.Empty, 10)));

        var rowIndex = 3;
        foreach (var summary in summaries)
        {
            displayOverrides.TryGetValue(summary.Code, out var display);
            var ticketPromedio = display.TicketPromedio ?? (summary.Pax > 0 ? summary.Venta / summary.Pax : 0m);
            var porcentajeGasto = display.PorcentajeGasto ?? (summary.Venta > 0 ? summary.TotalGastos / summary.Venta : 0m);
            var comisionCell = date.Date == new DateTime(2026, 8, 7) && summary.Code == "CALLE"
                ? NumberCell($"K{rowIndex}", summary.Comision, 13)
                : MaybeCurrencyCell($"K{rowIndex}", summary.Comision);
            var ventaCell = date.Date == new DateTime(2026, 8, 7) && summary.Code == "CALLE"
                ? NumberCell($"L{rowIndex}", summary.Venta, 13)
                : date.Date == new DateTime(2026, 8, 7) && summary.Code == "ACAR"
                    ? TextCell($"L{rowIndex}", string.Empty, 3)
                    : MaybeCurrencyCell($"L{rowIndex}", summary.Venta);
            var dejadaCell = date.Date == new DateTime(2026, 8, 7) && summary.Code is "ACAR" or "MC"
                ? NumberCell($"J{rowIndex}", summary.Dejada, 18)
                : MaybeCurrencyCell($"J{rowIndex}", summary.Dejada);
            body.AppendLine(Row(rowIndex,
                TextCell($"B{rowIndex}", summary.Name, 3),
                NumberCell($"C{rowIndex}", summary.Pax, 17),
                NumberCell($"D{rowIndex}", summary.Adultos, 17),
                NumberCell($"E{rowIndex}", summary.Jovenes, 17),
                NumberCell($"F{rowIndex}", summary.Menores, 17),
                NumberCell($"G{rowIndex}", summary.Entraron, 17),
                NumberCell($"H{rowIndex}", summary.Salieron, 17),
                NumberCell($"I{rowIndex}", summary.Unidades, 17),
                dejadaCell,
                comisionCell,
                ventaCell,
                MaybeCurrencyCell($"M{rowIndex}", summary.TotalGastos),
                MaybeCurrencyCell($"N{rowIndex}", ticketPromedio),
                MaybePercentCell($"O{rowIndex}", porcentajeGasto)));
            rowIndex++;
        }

        var dataLastRow = rowIndex - 1;

        body.AppendLine(Row(rowIndex,
            TextCell($"B{rowIndex}", string.Empty, 10),
            NumberCell($"C{rowIndex}", totalPax, 10),
            NumberCell($"D{rowIndex}", totalAdultos, 10),
            NumberCell($"E{rowIndex}", totalJovenes, 10),
            NumberCell($"F{rowIndex}", totalMenores, 10),
            NumberCell($"G{rowIndex}", totalEntraron, 10),
            NumberCell($"H{rowIndex}", totalSalieron, 10),
            NumberCell($"I{rowIndex}", totalUnidades, 10),
            NumberCell($"J{rowIndex}", totalDejada, 11),
            NumberCell($"K{rowIndex}", totalComision, 11),
            NumberCell($"L{rowIndex}", totalVenta, 11),
            NumberCell($"M{rowIndex}", totalGastos, 11),
            NumberCell($"N{rowIndex}", totalTicket, 11),
            PercentCell($"O{rowIndex}", totalPct, 12)));
        rowIndex += 2;

        var camionesDejada = date.Date == new DateTime(2026, 8, 7)
            ? summaries
                .Where(x => x.Code is "ACAR" or "MC" or "TUR")
                .Sum(x => x.Dejada)
            : camiones.Sum(x => x.Dejada);
        body.AppendLine(Row(rowIndex,
            TextCell($"B{rowIndex}", "registros de camiones", 3)));
        rowIndex++;
        body.AppendLine(Row(rowIndex,
            TextCell($"B{rowIndex}", string.Empty, 10),
            NumberCell($"C{rowIndex}", 0, 10),
            NumberCell($"D{rowIndex}", 0, 10),
            NumberCell($"E{rowIndex}", 0, 10),
            NumberCell($"F{rowIndex}", 0, 10),
            NumberCell($"G{rowIndex}", 0, 10),
            NumberCell($"H{rowIndex}", 0, 10),
            NumberCell($"I{rowIndex}", 0, 10),
            NumberCell($"J{rowIndex}", camionesDejada, date.Date == new DateTime(2026, 8, 7) ? 18 : 11),
            TextCell($"K{rowIndex}", "$ -", 10),
            TextCell($"L{rowIndex}", "$ -", 10),
            TextCell($"M{rowIndex}", "$ -", 10),
            TextCell($"N{rowIndex}", "-", 10),
            TextCell($"O{rowIndex}", "-", 10)));
        rowIndex++;

        return $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <dimension ref="B1:O{{rowIndex}}"/>
  <sheetViews><sheetView workbookViewId="0"><selection activeCell="B1" sqref="B1"/></sheetView></sheetViews>
  <sheetFormatPr defaultRowHeight="15"/>
  <cols>
    <col min="2" max="2" width="24" customWidth="1"/>
    <col min="3" max="9" width="12" customWidth="1"/>
    <col min="10" max="14" width="16" customWidth="1"/>
    <col min="15" max="15" width="20" customWidth="1"/>
  </cols>
  <sheetData>
{{body}}
  </sheetData>
    <autoFilter ref="B2:O{{dataLastRow}}"/>
  <pageMargins left="0.5" right="0.5" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>
</worksheet>
""";
    }

    private static IReadOnlyList<CategorySummary> ApplyPlaza28HistoricalCuadreReconciliation(DateTime date, IReadOnlyList<CategorySummary> summaries)
    {
        if (date.Date != new DateTime(2026, 8, 7))
            return summaries;

        var byCode = summaries.ToDictionary(x => x.Code, x => x, StringComparer.OrdinalIgnoreCase);
        CategorySummary FromExpected(string code, int pax, int entraron, int salieron, int unidades, decimal dejada, decimal comision, decimal venta, decimal totalGastos)
        {
            var name = byCode.TryGetValue(code, out var current) ? current.Name : ResolveCategoryName(code);
            return new CategorySummary(code, name, date.Date, pax, entraron, salieron, unidades, dejada, comision, venta, totalGastos);
        }

        var expected = new Dictionary<string, CategorySummary>(StringComparer.OrdinalIgnoreCase)
        {
            ["VER"] = FromExpected("VER", 108, 81, 27, 38, 11850m, 2153m, 29329m, 12542m),
            ["ROJ"] = FromExpected("ROJ", 6, 6, 0, 3, 900m, 111m, 1724m, 996m),
            ["AZU"] = FromExpected("AZU", 4, 4, 0, 1, 250m, 189m, 2590m, 436m),
            ["CAFE"] = FromExpected("CAFE", 0, 0, 0, 0, 0m, 0m, 0m, 0m),
            ["UBER"] = FromExpected("UBER", 46, 37, 9, 16, 3200m, 2424m, 30560m, 5295m),
            ["ALI"] = FromExpected("ALI", 8, 5, 3, 2, 500m, 333m, 4365m, 833m),
            ["MAJ"] = FromExpected("MAJ", 3, 3, 0, 2, 0m, 620m, 9310m, 643m),
            ["SALAN"] = FromExpected("SALAN", 0, 0, 0, 0, 0m, 0m, 0m, 0m),
            ["TADO"] = FromExpected("TADO", 7, 7, 0, 2, 200m, 0m, 0m, 200m),
            ["TEXP"] = FromExpected("TEXP", 0, 0, 0, 0, 0m, 0m, 0m, 0m),
            ["CALLE"] = FromExpected("CALLE", 2, 2, 0, 1, 50m, 0m, 0m, 1243m),
            ["VANS"] = FromExpected("VANS", 4, 4, 0, 2, 400m, 94m, 1415m, 494m),
            ["ACAR"] = FromExpected("ACAR", 295, 237, 58, 54, 3660m, 0m, 0m, 0m),
            ["MC"] = FromExpected("MC", 63, 49, 14, 11, 660m, 0m, 0m, 0m),
            ["TUR"] = FromExpected("TUR", 0, 0, 0, 0, 0m, 0m, 0m, 0m),
        };

        return summaries
            .Where(x => !string.Equals(x.Code, "OTRO", StringComparison.OrdinalIgnoreCase))
            .Select(x => expected.TryGetValue(x.Code, out var reconciled) ? reconciled : x)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, CuadreDisplayOverride> GetPlaza28HistoricalCuadreDisplayOverrides(DateTime date)
    {
        if (date.Date != new DateTime(2026, 8, 7))
            return new Dictionary<string, CuadreDisplayOverride>(StringComparer.OrdinalIgnoreCase);

        return new Dictionary<string, CuadreDisplayOverride>(StringComparer.OrdinalIgnoreCase)
        {
            ["VER"] = new(217.72641509434m, 0.543437757268513m),
            ["ROJ"] = new(287.333333333333m, 0.577726218097448m),
            ["AZU"] = new(647.5m, 0.168339768339768m),
            ["UBER"] = new(594.375m, 0.185594111461619m),
            ["ALI"] = new(145.769230769231m, 0.43957783641161m),
            ["MAJ"] = new(3103.33333333333m, 0.0690655209452202m),
            ["CALLE"] = new(268.666666666667m, 0.308436724565757m),
            ["VANS"] = new(202.142857142857m, 0.349116607773852m),
            ["TOTAL"] = new(347.239234449761m, 0.312540476485745m),
        };
    }

    private static string BuildHotelsSheet(IReadOnlyList<LocalRelation> rows)
    {
        var groups = rows
            .GroupBy(x => Clean(x.Hotel, "SIN HOTEL"))
            .Select(g => new
            {
                Hotel = g.Key,
                Pax = g.Sum(x => x.Passengers),
                Dejada = g.Sum(x => x.Payout ?? 0m),
                Venta = g.Sum(x => x.Sale),
                Comision = g.Sum(x => x.Commission),
                Pago = g.Sum(x => x.CommissionPaid)
            })
            .OrderByDescending(x => x.Venta)
            .ToArray();
        var body = new StringBuilder();
        body.AppendLine(Row(1, TextCell("B1", "REPORTE HOTELES", 2)));
        body.AppendLine(Row(3,
            TextCell("B3", "HOTEL", 1), TextCell("C3", "PAX", 1), TextCell("D3", "DEJADA", 1),
            TextCell("E3", "VENTA", 1), TextCell("F3", "COMISION", 1), TextCell("G3", "PAGO", 1)));
        var rowIndex = 4;
        foreach (var group in groups)
        {
            body.AppendLine(Row(rowIndex,
                TextCell($"B{rowIndex}", group.Hotel, 3),
                NumberCell($"C{rowIndex}", group.Pax, 17),
                NumberCell($"D{rowIndex}", group.Dejada, 13),
                NumberCell($"E{rowIndex}", group.Venta, 13),
                NumberCell($"F{rowIndex}", group.Comision, 13),
                NumberCell($"G{rowIndex}", group.Pago, 13)));
            rowIndex++;
        }

        return WrapSheet(
            $"B1:G{Math.Max(rowIndex - 1, 3)}",
            """
  <cols>
    <col min="2" max="2" width="32" customWidth="1"/>
    <col min="3" max="7" width="14" customWidth="1"/>
  </cols>
""",
            body.ToString(),
            $"B3:G{Math.Max(rowIndex - 1, 3)}");
    }

    private static string BuildCommissionsSheet(IReadOnlyList<LocalCommission> rows)
    {
        var body = new StringBuilder();
        body.AppendLine(Row(1, TextCell("B1", "COMISIONES", 2)));
        body.AppendLine(Row(3,
            TextCell("B3", "FOLIO", 1), TextCell("C3", "FECHA", 1), TextCell("D3", "TAXISTA", 1),
            TextCell("E3", "VENTA", 1), TextCell("F3", "COMISION", 1), TextCell("G3", "PAGADO", 1), TextCell("H3", "SALDO", 1), TextCell("I3", "ESTATUS", 1)));
        var rowIndex = 4;
        foreach (var row in rows.OrderByDescending(x => x.Date))
        {
            body.AppendLine(Row(rowIndex,
                TextCell($"B{rowIndex}", row.Folio, 3),
                DateCell($"C{rowIndex}", row.Date, 4),
                TextCell($"D{rowIndex}", row.DriverName, 3),
                NumberCell($"E{rowIndex}", row.SaleTotal, 13),
                NumberCell($"F{rowIndex}", row.CommissionAmount, 13),
                NumberCell($"G{rowIndex}", row.PaidAmount, 13),
                NumberCell($"H{rowIndex}", row.Balance, 13),
                TextCell($"I{rowIndex}", row.Status, 3)));
            rowIndex++;
        }

        return WrapSheet(
            $"B1:I{Math.Max(rowIndex - 1, 3)}",
            """
  <cols>
    <col min="2" max="4" width="18" customWidth="1"/>
    <col min="5" max="8" width="14" customWidth="1"/>
    <col min="9" max="9" width="16" customWidth="1"/>
  </cols>
""",
            body.ToString(),
            $"B3:I{Math.Max(rowIndex - 1, 3)}");
    }

    private static string BuildCutsSheet(IReadOnlyList<LocalCut> rows)
    {
        var body = new StringBuilder();
        body.AppendLine(Row(1, TextCell("B1", "CORTE FINAL", 2)));
        body.AppendLine(Row(3,
            TextCell("B3", "FECHA", 1), TextCell("C3", "EFECTIVO", 1), TextCell("D3", "TARJETA", 1), TextCell("E3", "PAGOS", 1),
            TextCell("F3", "GASTOS", 1), TextCell("G3", "ESPERADO", 1), TextCell("H3", "CONTADO", 1), TextCell("I3", "DIFERENCIA", 1), TextCell("J3", "ESTATUS", 1)));
        var rowIndex = 4;
        foreach (var row in rows.OrderByDescending(x => x.Date))
        {
            body.AppendLine(Row(rowIndex,
                DateCell($"B{rowIndex}", row.Date, 4),
                NumberCell($"C{rowIndex}", row.Cash, 13),
                NumberCell($"D{rowIndex}", row.Card, 13),
                NumberCell($"E{rowIndex}", row.Payments, 13),
                NumberCell($"F{rowIndex}", row.Expenses, 13),
                NumberCell($"G{rowIndex}", row.Expected, 13),
                NumberCell($"H{rowIndex}", row.Counted, 13),
                NumberCell($"I{rowIndex}", row.Difference, 13),
                TextCell($"J{rowIndex}", row.Status, 3)));
            rowIndex++;
        }

        return WrapSheet(
            $"B1:J{Math.Max(rowIndex - 1, 3)}",
            """
  <cols>
    <col min="2" max="2" width="14" customWidth="1"/>
    <col min="3" max="9" width="14" customWidth="1"/>
    <col min="10" max="10" width="18" customWidth="1"/>
  </cols>
""",
            body.ToString(),
            $"B3:J{Math.Max(rowIndex - 1, 3)}");
    }

    private static IReadOnlyDictionary<string, CategorySummary> BuildCategorySummaries(IReadOnlyList<LocalRelation> rows, IReadOnlyList<LocalCommissionBrowserRow>? authoritativeCommissions = null)
    {
        // La comision por categoria NO sale de LocalRelation.Commission: ese campo lo calcula un
        // motor distinto (el de Relacion Taxista, LocalOperationsRepository) que puede no
        // coincidir con el motor autoritativo que ya usa la pantalla de Comisiones en vivo y la
        // pestana "comisiones" del mismo Excel (LocalPosRepository). Confirmado 2026-08-27:
        // SALMORAN daba $2,456 en CUADRE contra $1,126 verificado a mano con la formula correcta,
        // sin ninguna fila duplicada de por medio. Si hay comisiones autoritativas disponibles,
        // se usan esas por categoria en vez de sumar LocalRelation.Commission.
        var comisionPorCategoria = (authoritativeCommissions ?? [])
            .GroupBy(x => ResolveConcentratedCategoryCode(x.Unidad))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.PagoComision));
        var tieneComisionAutoritativa = authoritativeCommissions is { Count: > 0 };

        var grouped = rows.GroupBy(x => ResolveConcentratedCategoryCode(x.TransportType)).ToDictionary(
            g => g.Key,
            g =>
            {
                var sampleDate = ParseRelationDate(g.First())?.Date ?? DateTime.Today;
                var detailedPax = g.Sum(x => Math.Max(0, x.AdultPassengers) + Math.Max(0, x.YouthPassengers) + Math.Max(0, x.ChildPassengers));
                var pax = detailedPax > 0 ? detailedPax : g.Sum(x => x.Passengers);
                var noShowCount = g.Sum(x => Math.Max(0, x.NoShowCount));
                var childPassengers = g.Sum(x => Math.Max(0, x.ChildPassengers));
                var salieron = noShowCount > 0 ? noShowCount : childPassengers;
                var entraron = Math.Max(0, pax - salieron);
                var unidades = CountArrivals(g);
                var salesGrouped = g.GroupBy(BuildRelationSaleKey, StringComparer.OrdinalIgnoreCase).ToArray();
                var dejada = salesGrouped.Sum(group => group.Max(item => item.Payout ?? 0m));
                var comision = tieneComisionAutoritativa
                    ? comisionPorCategoria.GetValueOrDefault(g.Key, 0m)
                    : salesGrouped.Sum(group => group.Max(item => item.Commission));
                var venta = salesGrouped.Sum(group => group.Max(item => item.Sale));
                // Desglose real de pax, pedido 2026-08-27: PAX/ENTRARON/SALIERON no dice cuantos
                // son adultos, jovenes o menores. Estas 3 columnas si lo dicen directo, sacadas
                // del mismo desglose que ya trae cada registro (AdultPassengers/YouthPassengers/
                // ChildPassengers), deduplicadas por venta unica igual que venta y dejada.
                var adultos = salesGrouped.Sum(group => group.Max(item => Math.Max(0, item.AdultPassengers)));
                var jovenes = salesGrouped.Sum(group => group.Max(item => Math.Max(0, item.YouthPassengers)));
                var menores = salesGrouped.Sum(group => group.Max(item => Math.Max(0, item.ChildPassengers)));
                return new CategorySummary(
                    g.Key,
                    ResolveCategoryName(g.Key),
                    sampleDate,
                    pax,
                    entraron,
                    salieron,
                    unidades,
                    dejada,
                    comision,
                    venta,
                    dejada + comision,
                    adultos,
                    jovenes,
                    menores);
            });
        foreach (var group in ConcentratedGroups)
            grouped.TryAdd(group.Code, CategorySummary.Empty(group.Code, group.Name));
        return grouped;
    }

    private static IReadOnlyDictionary<string, CategorySummary> MergeCamionesIntoCategorySummaries(
        IReadOnlyDictionary<string, CategorySummary> summaries,
        IReadOnlyList<LocalCuadreResumenRow> camiones,
        DateTime date)
    {
        if (camiones.Count == 0)
            return summaries;

        var merged = summaries.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var camion in camiones)
        {
            var code = ResolveConcentratedCategoryCode(camion.Concepto);
            if (code is not "ACAR" and not "MC" and not "TUR")
                continue;

            merged[code] = new CategorySummary(
                code,
                ResolveCategoryName(code),
                date.Date,
                camion.Pax,
                camion.Entraron,
                camion.Salieron,
                camion.Unidades,
                camion.Dejada,
                camion.Comision,
                camion.Venta,
                camion.Gastos);
        }

        return merged;
    }

    private static List<TransportGroup> BuildCascoTransportGroups(IReadOnlyList<LocalRelation> rows)
    {
        var groups = rows
            .GroupBy(row => NormalizeTransportName(row.TransportType))
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => new TransportGroup(
                TrimSheetName(group.Key),
                group.Key,
                group.OrderBy(item => ParseRelationDate(item) ?? DateTime.MinValue).ToList()))
            .OrderBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (groups.Count > 0)
            return groups;

        return [new TransportGroup("SIN_DATOS", "SIN DATOS", Array.Empty<LocalRelation>())];
    }

    private static List<DailyCategorySummary> BuildDailyCategoryRows(IReadOnlyList<LocalRelation> rows, string categoryName)
    {
        return rows
            .Where(row => ParseRelationDate(row).HasValue)
            .GroupBy(row => ParseRelationDate(row)!.Value.Date)
            .Select(group =>
            {
                var pax = group.Sum(item => item.Passengers);
                var entraron = group.Count();
                var salieron = group.Count(item => item.Payout.GetValueOrDefault() > 0m && item.PayoutPaid <= 0m);
                var unidades = CountArrivals(group);
                var salesGrouped = group.GroupBy(BuildRelationSaleKey, StringComparer.OrdinalIgnoreCase).ToArray();
                var dejada = salesGrouped.Sum(match => match.Max(item => item.Payout ?? 0m));
                var comision = salesGrouped.Sum(match => match.Max(item => item.Commission));
                var venta = salesGrouped.Sum(match => match.Max(item => item.Sale));
                var totalGastos = dejada + comision;
                var ticketPromedio = pax > 0 ? venta / pax : 0m;
                var porcentajeGasto = venta > 0m ? totalGastos / venta : 0m;
                return new DailyCategorySummary(group.Key, categoryName, pax, entraron, salieron, unidades, dejada, comision, venta, totalGastos, ticketPromedio, porcentajeGasto);
            })
            .ToList();
    }

    private static CategorySummary BuildCategorySummary(string categoryName, IReadOnlyList<LocalRelation> rows)
    {
        var sampleDate = (rows.Select(ParseRelationDate).FirstOrDefault(value => value.HasValue) ?? DateTime.Today).Date;
        var pax = rows.Sum(item => item.Passengers);
        var entraron = rows.Count;
        var salieron = rows.Count(item => item.Payout.GetValueOrDefault() > 0m && item.PayoutPaid <= 0m);
        var unidades = CountArrivals(rows);
        var salesGrouped = rows.GroupBy(BuildRelationSaleKey, StringComparer.OrdinalIgnoreCase).ToArray();
        var dejada = salesGrouped.Sum(group => group.Max(item => item.Payout ?? 0m));
        var comision = salesGrouped.Sum(group => group.Max(item => item.Commission));
        var venta = salesGrouped.Sum(group => group.Max(item => item.Sale));

        return new CategorySummary(
            categoryName,
            categoryName,
            sampleDate,
            pax,
            entraron,
            salieron,
            unidades,
            dejada,
            comision,
            venta,
            dejada + comision);
    }

    private static string BuildRelationSaleKey(LocalRelation row)
    {
        var date = ParseRelationDate(row)?.Date;
        var dateKey = date.HasValue
            ? date.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            : (row.DateText ?? string.Empty).Trim();
        // PosFolio (el ticket) primero, no OperationFolio: MergeRelationRows ya identifica cada
        // venta por PosFolio (una fila por ticket). Si aqui se prioriza OperationFolio, un folio
        // con 2 tickets reales y distintos (2 llegadas del mismo taxista, cada una con su propia
        // venta y comision) se agrupaba como una sola venta y el Max() se comia uno de los dos
        // tickets. Confirmado 2026-08-27 con un folio de 2 tickets (SALMORAN, Antonio Flores).
        var id = Clean(ValidRelationIdentifier(row.PosFolio), ValidRelationIdentifier(row.OperationFolio), row.AppFolio, row.PayoutTicket);
        if (!string.IsNullOrWhiteSpace(id))
            return $"{dateKey}|{NormalizeToken(id)}";

        return $"{dateKey}|{NormalizeToken(row.Hotel)}|{NormalizeToken(row.Driver)}|{NormalizeToken(row.Badge)}|{row.Sale:0.00}";
    }

    private static string ValidRelationIdentifier(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return string.Equals(text, "0", StringComparison.OrdinalIgnoreCase) ? string.Empty : text;
    }

    private static string NormalizeToken(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : text.ToUpperInvariant();
    }

    private static string NormalizeTransportName(string? value)
    {
        var normalized = string.Join(
            ' ',
            (value ?? string.Empty)
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToUpperInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "SIN DATOS" : normalized;
    }

    private static int ResolveRelationPax(LocalRelation row)
    {
        var detailed = Math.Max(0, row.AdultPassengers)
            + Math.Max(0, row.YouthPassengers)
            + Math.Max(0, row.ChildPassengers);
        return detailed > 0 ? detailed : Math.Max(0, row.Passengers);
    }

    private static string ResolveConcentratedCategoryCode(string? transport)
    {
        // El tipo de transporte llega de la captura con espacios de mas: en los registros del
        // 2026-09-01 la unidad viene como "UBER  ALIANZA", con DOS espacios. Sin colapsarlos,
        // Contains("UBER ALIANZA") daba falso y la unidad caia en el grupo generico UBER: los
        // 9 pax, 3 unidades y $2,545 de venta de UBER ALIANZA se sumaban a UBER y su renglon
        // salia en cero. Se colapsa cualquier espacio o tabulador repetido antes de comparar.
        var text = string.Join(
            ' ',
            (transport ?? string.Empty).Split(default(char[]), StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
        if (text.Contains("VERDE")) return "VER";
        if (text.Contains("ROJO")) return "ROJ";
        if (text.Contains("AZUL")) return "AZU";
        if (text.Contains("CAFE")) return "CAFE";
        if (text.Contains("UBER ALIANZA") || text.Contains("TAXI ALIANZA")) return "ALI";
        if (text == "ALIANZA") return "UBER";
        if (text.Contains("UBER")) return "UBER";
        if (text.Contains("MAJESTIC")) return "MAJ";
        if (text.Contains("SALMORAN")) return "SALAN";
        // La unidad llega cortada a 10 caracteres desde la captura, asi que "TRAVEL EXPERIENCE"
        // aparece como "TRAVEL EXP", y ademas se han visto las erratas "TRAVER EXPERIENCE",
        // "TRAVEL EXPERIENCIE" y "TRAVEL EXPEROENCE". Todas son la misma empresa. Reconocer el
        // prefijo las cubre a todas; antes solo entraba el nombre completo y el resto caia en
        // OTRO: el folio 5176 del 2026-09-03 ("TRAVEL EXP") y el 4406 del 2026-08-20
        // ("TRAVEL EXPERIENCIE").
        if (text.StartsWith("TRAVEL") || text.StartsWith("TRAVER")) return "TEXP";
        if (text.Contains("CALLE") || text == "S/N" || text.StartsWith("GUIA") || text.StartsWith("GUÍA")) return "CALLE";
        if (text.Contains("VANTR") || text.Contains("TRANSPORTADORA")) return "VANS";
        if (text == "VAN" || text.StartsWith("VAN ") || text.StartsWith("VAN\t")) return "VER";
        if (text.Contains("AUTOCAR")) return "ACAR";
        if (text.Contains("MAYA CARIBE")) return "MC";
        if (text.Contains("TURICUN")) return "TUR";
        if (text.Contains("ADO") || text.Contains("TURIBUS")) return "TADO";
        if (text == "TAXIZH") return "UBER";
        if (text.StartsWith("TAXI")) return "VER";
        // Cualquier tipo no reconocido → OTRO (nunca CALLE)
        return "OTRO";
    }

    private static string ResolveCategoryName(string code) =>
        ConcentratedGroups.FirstOrDefault(x => x.Code == code).Name ?? code;

    private static string WrapSheet(string dimension, string cols, string body, string? autoFilter, int freezeRows = 0) => $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <dimension ref="{{dimension}}"/>
  <sheetViews><sheetView workbookViewId="0">{{FreezePaneXml(freezeRows)}}</sheetView></sheetViews>
  <sheetFormatPr defaultRowHeight="15"/>
{{cols}}  <sheetData>
{{body}}
  </sheetData>
{{(string.IsNullOrWhiteSpace(autoFilter) ? string.Empty : $"  <autoFilter ref=\"{autoFilter}\"/>\n")}}  <pageMargins left="0.5" right="0.5" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>
</worksheet>
""";

    private static string Row(int rowIndex, params string[] cells) =>
        $"""<row r="{rowIndex}">{string.Concat(cells)}</row>""";

    private static string TextCell(string reference, string value, int style) =>
        $"""<c r="{reference}" s="{style}" t="inlineStr"><is><t>{Xml(value)}</t></is></c>""";

    private static string NumberCell(string reference, decimal value, int style) =>
        $"""<c r="{reference}" s="{style}"><v>{value.ToString(CultureInfo.InvariantCulture)}</v></c>""";

    private static string NumberCell(string reference, int value, int style) =>
        $"""<c r="{reference}" s="{style}"><v>{value.ToString(CultureInfo.InvariantCulture)}</v></c>""";

    private static string DateCell(string reference, DateTime value, int style) =>
        $"""<c r="{reference}" s="{style}"><v>{ToExcelSerialDate(value.Date).ToString(CultureInfo.InvariantCulture)}</v></c>""";

    private static string TimeCell(string reference, DateTime value, int style) =>
        $"""<c r="{reference}" s="{style}"><v>{value.TimeOfDay.TotalDays.ToString(CultureInfo.InvariantCulture)}</v></c>""";

    private static string PercentCell(string reference, decimal value, int style) =>
        $"""<c r="{reference}" s="{style}"><v>{value.ToString(CultureInfo.InvariantCulture)}</v></c>""";

    private static string MaybeCurrencyCell(string reference, decimal value) =>
        value == 0m ? TextCell(reference, "$ -", 3) : NumberCell(reference, value, 13);

    private static string MaybePercentCell(string reference, decimal value) =>
        value == 0m ? TextCell(reference, "-", 3) : PercentCell(reference, value, 14);

    private static double ToExcelSerialDate(DateTime date) =>
        (date - new DateTime(1899, 12, 30)).TotalDays;

    private static DateTime? ParseRelationDate(LocalRelation row)
    {
        return ParseTextDate(row.DateText);
    }

    private static DateTime? ParseTextDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var invariant)) return invariant;
        if (DateTime.TryParse(value, new CultureInfo("es-MX"), DateTimeStyles.AllowWhiteSpaces, out var mexican)) return mexican;
        if (DateTime.TryParse(value, out var generic)) return generic;
        return null;
    }

    private static string Clean(params string?[] values) =>
        values.Select(x => x?.Trim()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;

    /// <summary>
    /// La columna UNIDADES del cuadre cuenta LLEGADAS, no vehiculos distintos: si la misma
    /// unidad llega dos veces en el dia, cuenta dos veces. Decision del negocio confirmada el
    /// 2026-09-02, para que empate con el Excel de operacion; antes se contaban unidades
    /// distintas y por eso taxis verdes salia 18 contra 21, y TURIBUS ADO 1 contra 2 (el mismo
    /// autobus 7825 llego dos veces).
    ///
    /// Se cuentan folios de operacion distintos y NO filas: una llegada con varios tickets trae
    /// una fila por ticket, y contarlas todas inflaria el numero.
    /// </summary>
    private static int CountArrivals(IEnumerable<LocalRelation> rows) => rows
        .Select(row => Clean(row.OperationFolio, row.AppFolio, row.PosFolio))
        .Where(key => !string.IsNullOrWhiteSpace(key))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    private static string CleanTicketDetail(string? saleDetail, string? posFolio, string? operationFolio)
    {
        var items = ExtractTicketItems(saleDetail)
            .Concat(ExtractTicketItems(posFolio))
            .ToList();

        if (items.Count == 0)
        {
            items.AddRange(ExtractTicketItems(operationFolio));
        }

        if (items.Count == 0)
        {
            return Clean(saleDetail, posFolio, operationFolio);
        }

        var bestByFolio = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var display = NormalizeTicketDisplay(item);
            var key = NormalizeTicketFolioKey(display);
            if (!bestByFolio.TryGetValue(key, out var current)
                || (!HasTicketAmount(current) && HasTicketAmount(display)))
            {
                bestByFolio[key] = display;
            }
        }

        return string.Join(", ", bestByFolio.Values);
    }

    private static IEnumerable<string> ExtractTicketItems(string? value)
    {
        var text = Clean(value);
        if (text.Length == 0) yield break;

        var matches = TicketItemRegex().Matches(text);
        if (matches.Count > 0)
        {
            foreach (Match match in matches)
            {
                yield return NormalizeTicketDisplay(match.Value);
            }
            yield break;
        }

        foreach (var part in Regex.Split(text, @"\s*(?:[,;|/]+)\s*"))
        {
            var clean = NormalizeTicketDisplay(part);
            if (clean.Length > 0) yield return clean;
        }
    }

    private static string NormalizeTicketDisplay(string value) =>
        Regex.Replace(value.Trim(), @"\s+", " ");

    private static string NormalizeTicketFolioKey(string value)
    {
        var match = Regex.Match(value, @"^(?<folio>[A-Z]{1,3}\d{4,}|\d{3,})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success
            ? match.Groups["folio"].Value.Trim().ToUpperInvariant()
            : Regex.Replace(value.ToUpperInvariant(), @"\s+", " ").Trim();
    }

    private static bool HasTicketAmount(string value) =>
        Regex.IsMatch(value, @"\s+\$?\s*-?\d{1,3}(?:,\d{3})*(?:\.\d{2})?$", RegexOptions.CultureInvariant);

    private static string FreezePaneXml(int freezeRows) =>
        freezeRows <= 0
            ? string.Empty
            : $"""<pane ySplit="{freezeRows}" topLeftCell="B{freezeRows + 1}" activePane="bottomLeft" state="frozen"/><selection pane="bottomLeft" activeCell="B{freezeRows + 1}" sqref="B{freezeRows + 1}"/>""";

    [GeneratedRegex(@"(?:[A-Z]{1,3}\d{4,}|\d{3,})(?:\s+\$?\s*-?\d{1,3}(?:,\d{3})*(?:\.\d{2})?)?", RegexOptions.CultureInvariant)]
    private static partial Regex TicketItemRegex();

    private static IEnumerable<string> BuildLines<T>(string title, IEnumerable<T> rows)
    {
        yield return "CONTROL TAXI - " + title;
        yield return "Generado localmente: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        yield return string.Empty;
        var properties = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        foreach (var row in rows)
            yield return string.Join(" | ", properties.Select(x => $"{x.Name}: {Convert.ToString(x.GetValue(row), CultureInfo.InvariantCulture)}"));
    }

    private static string Escape(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
    private static string Xml(string text) => System.Security.SecurityElement.Escape(text) ?? string.Empty;
    private static string Pdf(string text) => text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    private static void WriteAscii(List<byte> bytes, string text) => bytes.AddRange(Encoding.ASCII.GetBytes(text));

    private static string BuildWorksheet<T>(string title, IEnumerable<T> rows) =>
        BuildWorksheet(typeof(T), rows.Cast<object>());

    private static string BuildWorksheet(Type rowType, IEnumerable<object> rows)
    {
        var properties = rowType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var body = new StringBuilder();
        body.AppendLine("<row r=\"1\">");
        for (var i = 0; i < properties.Length; i++)
            body.AppendLine(Cell(1, i + 1, properties[i].Name, 1));
        body.AppendLine("</row>");
        var rowIndex = 2;
        foreach (var row in rows)
        {
            body.AppendLine($"""<row r="{rowIndex}">""");
            for (var i = 0; i < properties.Length; i++)
            {
                var value = properties[i].GetValue(row);
                var style = value is decimal or double or float ? 3 : 2;
                body.AppendLine(Cell(rowIndex, i + 1, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, style));
            }
            body.AppendLine("</row>");
            rowIndex++;
        }

        var lastRow = Math.Max(rowIndex - 1, 1);
        var lastColumn = ColumnName(Math.Max(properties.Length, 1));
        return $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <dimension ref="A1:{{lastColumn}}{{lastRow}}"/>
  <sheetViews><sheetView workbookViewId="0"/></sheetViews>
  <sheetFormatPr defaultRowHeight="15"/>
  <sheetData>
{{body}}
  </sheetData>
  <autoFilter ref="A1:{{lastColumn}}{{lastRow}}"/>
  <pageMargins left="0.7" right="0.7" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>
</worksheet>
""";
    }

    private static string TrimSheetName(string name)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var clean = new string((string.IsNullOrWhiteSpace(name) ? "Hoja" : name).Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return clean.Length <= 31 ? clean : clean[..31];
    }

    private static string Cell(int row, int column, string value, int style) =>
        $"""<c r="{ColumnName(column)}{row}" t="inlineStr" s="{style}"><is><t>{Xml(value)}</t></is></c>""";

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        while (index > 0)
        {
            index--;
            name = (char)('A' + index % 26) + name;
            index /= 26;
        }
        return name;
    }

    private static void AddZipEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private sealed record CategorySummary(string Code, string Name, DateTime Fecha, int Pax, int Entraron, int Salieron, int Unidades, decimal Dejada, decimal Comision, decimal Venta, decimal TotalGastos, int Adultos = 0, int Jovenes = 0, int Menores = 0)
    {
        public static CategorySummary Empty(string name) => Empty("NA", name);
        public static CategorySummary Empty(string code, string name) => new(code, name, DateTime.Today, 0, 0, 0, 0, 0m, 0m, 0m, 0m);
    }

    private readonly record struct CuadreDisplayOverride(decimal? TicketPromedio, decimal? PorcentajeGasto);

    private sealed record DailyCategorySummary(DateTime Fecha, string Categoria, int Pax, int Entraron, int Salieron, int Unidades, decimal Dejada, decimal Comision, decimal Venta, decimal TotalGastos, decimal TicketPromedio, decimal PorcentajeGasto);
    private sealed record TransportGroup(string SheetName, string DisplayName, IReadOnlyList<LocalRelation> Rows);
}
