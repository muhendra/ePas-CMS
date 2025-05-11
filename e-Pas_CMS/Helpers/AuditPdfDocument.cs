using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using e_Pas_CMS.ViewModels;

public class AuditPdfDocument : IDocument
{
    private readonly DetailReportViewModel _model;

    public AuditPdfDocument(DetailReportViewModel model)
    {
        _model = model;
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(20);
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Header()
                .Text($"Laporan Audit SPBU - {_model.ReportNo}")
                .FontSize(16).Bold().AlignCenter();

            page.Content().Column(col =>
            {
                col.Spacing(10);

                // Info SPBU
                col.Item().Text($"No SPBU: {_model.SpbuNo} | Region: {_model.Region} | Kota: {_model.Kota}");
                col.Item().Text($"Alamat: {_model.Alamat} | Pemilik: {_model.OwnerName} | Manajer: {_model.ManagerName}");

                col.Item().Text($"Tahun: {_model.Year} | MOR: {_model.MOR} | SBM: {_model.SBM} | Tipe Audit: {_model.AuditType}");

                col.Item().Text("\nTotal Score:")
                    .FontSize(12).Bold();
                col.Item().Text($"{_model.TotalScore:0.00}% ({(_model.TotalScore >= _model.MinPassingScore ? "EXCELLENT" : "GOOD")})");

                // Komentar
                col.Item().Text("\nKomentar Auditor:").FontSize(12).Bold();
                col.Item().Text($"Staf Terlatih: {_model.KomentarStaf}");
                col.Item().Text($"Jaminan Q&Q: {_model.KomentarQuality}");
                col.Item().Text($"Peralatan & HSSE: {_model.KomentarHSSE}");
                col.Item().Text($"Tampilan Visual: {_model.KomentarVisual}");
                col.Item().Text($"Komentar Manager: {_model.KomentarManager}");

                // Table Elemen
                col.Item().PaddingTop(10).Element(ComposeTable);
            });
        });
    }

    void ComposeTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(4);
                columns.RelativeColumn(2);
                columns.RelativeColumn(2);
                columns.RelativeColumn(3);
            });

            table.Header(header =>
            {
                header.Cell().Element(CellStyle).Text("Indikator Penilaian").Bold();
                header.Cell().Element(CellStyle).AlignCenter().Text("Bobot").Bold();
                header.Cell().Element(CellStyle).AlignCenter().Text("Minimum").Bold();
                header.Cell().Element(CellStyle).AlignCenter().Text("Compliance Level").Bold();
            });

            foreach (var item in _model.Elements)
            {
                string level = "-";
                var score = (item.ScoreAF ?? 0) * 100;

                if (score >= 100) level = "Excellent";
                else if (score >= 87.5m) level = "Good";
                else level = "Needs Improvement";

                table.Cell().Element(CellStyle).Text(item.Title);
                table.Cell().Element(CellStyle).AlignCenter().Text((item.Weight ?? 0).ToString("0"));
                table.Cell().Element(CellStyle).AlignCenter().Text("85%");
                table.Cell().Element(CellStyle).AlignCenter().Text($"{score:0.##}% ({level})");
            }
        });
    }

    IContainer CellStyle(IContainer container)
    {
        return container
            .PaddingVertical(4)
            .PaddingHorizontal(6)
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Lighten2);
    }
}
