using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPDF.Drawing;
using QuestPDF.Elements;
using e_Pas_CMS.ViewModels;

public class ReportExcellentTemplate : IDocument
{
    private readonly DetailReportViewModel _model;

    public ReportExcellentTemplate(DetailReportViewModel model)
    {
        _model = model;
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(25);
            page.Size(PageSizes.A4);
            page.DefaultTextStyle(x => x.FontSize(9));
            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
        });
    }

    void ComposeHeader(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("SPBU EXCELLENT PERFORMANCE AUDIT REPORT").Bold().FontSize(14).FontColor(Colors.Blue.Medium);
                col.Item().Text("LAPORAN AUDIT PERFORMA SPBU EXCELLENT").FontSize(12);
                col.Item().Text("Report ini merupakan dokumen elektronik sehingga tidak membutuhkan tanda tangan dan cap perusahaan").Italic().FontSize(8);
            });

            //row.ConstantItem(100).Height(80).Image("Assets/pertamina-logo.png", ImageScaling.FitArea); // ganti path/logo sesuai kebutuhan
        });
    }

    void ComposeContent(IContainer container)
    {
        container.Column(col =>
        {
            // STATUS
            col.Item().Background(Colors.Red.Medium).Padding(5).AlignCenter().Text("NOT CERTIFIED").FontSize(14).Bold().FontColor(Colors.White);

            // TABEL INFORMASI UMUM
            col.Item().PaddingVertical(10).Element(ComposeInfoTable);

            // TOTAL SCORE
            col.Item().PaddingVertical(10).Row(row =>
            {
                row.RelativeItem().Background(Colors.Grey.Lighten2).Padding(10).Text($"TOTAL SCORE (TS): {_model.TotalScore:0.00}").Bold();
                row.ConstantItem(180).AlignMiddle().AlignRight().Text($"Nilai Minimum Pasti Pas: {_model.MinPassingScore:0.00}").FontSize(9);
            });

            // Elemen Penilaian
            col.Item().PaddingVertical(10).Element(ComposeElementTable);

            // Sub Elemen
            foreach (var element in _model.Elements.Where(x => !string.IsNullOrWhiteSpace(x.Description)))
            {
                col.Item().PaddingTop(10).Text(element.Description).Bold().FontSize(11);
                col.Item().Element(c => ComposeSubElementTable(c, element.Children));
            }
        });
    }

    void ComposeInfoTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
            });

            void InfoRow(string label, string value)
            {
                table.Cell().Text(label).SemiBold();
                table.Cell().Text(value ?? "-");
            }

            InfoRow("NOMOR SPBU", _model.SpbuNo);
            InfoRow("REGION", _model.Region);
            InfoRow("KOTA", _model.Kota);
            InfoRow("ALAMAT", _model.Alamat);

            InfoRow("NAMA PEMILIK", _model.OwnerName);
            InfoRow("NAMA MANAJER", _model.ManagerName);
            InfoRow("TIPE KEPEMILIKAN", _model.OwnershipType);
            InfoRow("QUARTER", _model.Quarter);

            InfoRow("TAHUN", _model.Year.ToString());
            InfoRow("MOR", _model.MOR);
            InfoRow("SALES AREA", _model.SalesArea);
            InfoRow("SBM", _model.SBM);

            InfoRow("TIPE AUDIT", _model.AuditType);
            InfoRow("KELAS SPBU", _model.ClassSPBU);
            InfoRow("TELEPON", _model.Phone);
        });
    }

    void ComposeElementTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(2);
                c.RelativeColumn();
                c.RelativeColumn();
                c.RelativeColumn();
            });

            table.Header(header =>
            {
                header.Cell().Text("Indikator Penilaian").Bold();
                header.Cell().AlignCenter().Text("Bobot Nilai").Bold();
                header.Cell().AlignCenter().Text("Nilai Minimum").Bold();
                header.Cell().AlignCenter().Text("Compliance Level").Bold();
            });

            foreach (var element in _model.Elements)
            {
                string level = "-";
                var af = element.ScoreAF ?? 0;
                var percent = af * 100;

                if (percent >= 100)
                    level = "Excellent";
                else if (percent >= 87.5m)
                    level = "Good";
                else
                    level = "Needs Improvement";

                table.Cell().Text($"{element.Title}\n{element.Description}").FontSize(9);
                table.Cell().AlignCenter().Text($"{element.Weight}");
                table.Cell().AlignCenter().Text("85.00%");
                table.Cell().AlignCenter().Text($"{percent:0.##}%\n{level}").FontSize(9);
            }
        });
    }

    void ComposeSubElementTable(IContainer container, List<AuditChecklistNode> children)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(2);
                c.RelativeColumn();
                c.RelativeColumn();
            });

            table.Header(header =>
            {
                header.Cell().Text("Sub Elemen").Bold();
                header.Cell().AlignCenter().Text("Marks").Bold();
                header.Cell().AlignCenter().Text("Compliance").Bold();
            });

            foreach (var item in children)
            {
                var af = item.ScoreAF ?? 0;
                var percent = af * 100;
                string level = percent >= 100m ? "Excellent" :
               percent >= 87.5m ? "Good" : "Needs Improvement";

                table.Cell().Text(item.Description ?? "-");
                table.Cell().AlignCenter().Text($"{item.Weight}");
                table.Cell().AlignCenter().Text($"{percent:0.##}%\n{level}").FontSize(9);
            }
        });
    }
}
