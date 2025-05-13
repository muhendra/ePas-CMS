
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPDF.Drawing;
using QuestPDF.Elements;
using e_Pas_CMS.ViewModels;
using System.Linq;

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
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Images");
        var leftImagePath = Path.Combine(basePath, "pertaminaway.png");
        var rightImagePath = Path.Combine(basePath, "intertek.png");

        container
            .PaddingBottom(15)
            .Row(row =>
            {
                row.RelativeItem(1).Height(60).AlignLeft().Image(leftImagePath, ImageScaling.FitArea);

                row.RelativeItem(2).PaddingHorizontal(10).Column(col =>
                {
                    col.Item().AlignCenter().Text("SPBU EXCELLENT PERFORMANCE AUDIT REPORT")
                        .Bold().FontSize(14).FontColor(Colors.Blue.Medium);
                    col.Item().AlignCenter().Text("LAPORAN AUDIT PERFORMA SPBU EXCELLENT")
                        .FontSize(12).FontColor(Colors.Blue.Medium);
                    col.Item().AlignCenter().Text("Report ini merupakan dokumen elektronik sehingga tidak membutuhkan tanda tangan dan cap perusahaan")
                        .Italic().FontSize(8);
                });

                row.RelativeItem(1).Height(60).AlignRight().Image(rightImagePath, ImageScaling.FitArea);
            });
    }

    void ComposeContent(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Background(Colors.Red.Medium).Padding(5).AlignCenter().Text(_model.Status?.ToUpper() == "VERIFIED" ? "CERTIFIED" : "NOT CERTIFIED").FontSize(14).Bold().FontColor(Colors.White);
            col.Item().PaddingVertical(10).Element(ComposeInfoTable);

            col.Item().PaddingBottom(10).Text($"Catatan Auditor: {_model.Notes}").Italic().FontSize(9);

            col.Item().PaddingVertical(10).Row(row =>
            {
                row.RelativeItem().Background(Colors.Grey.Lighten2).Padding(10).Text($"TOTAL SCORE (TS): {_model.TotalScore:0.00}").Bold();
                row.ConstantItem(180).AlignMiddle().AlignRight().Text($"Nilai Minimum Pasti Pas: {_model.MinPassingScore:0.00}").FontSize(9);
            });

            col.Item().PaddingVertical(10).Element(ComposeElementTable);

            foreach (var element in _model.Elements.Where(x => !string.IsNullOrWhiteSpace(x.Description)))
            {
                col.Item().PaddingTop(10).Text(element.Description).Bold().FontSize(11);
                col.Item().Element(c => ComposeSubElementTable(c, element.Children));
            }

            col.Item().PaddingTop(20).Text("KOMENTAR AUDITOR").Bold().FontSize(12);

            void KomentarItem(string label, string value)
            {
                col.Item().Text(label).Bold().FontSize(10);
                col.Item().PaddingBottom(10).Text(value ?? "-").FontSize(9);
            }

            KomentarItem("Staf Terlatih dan Termotivasi", _model.KomentarStaf);
            KomentarItem("Jaminan Kualitas dan Kuantitas", _model.KomentarQuality);
            KomentarItem("Peralatan Terpelihara dan HSSE", _model.KomentarHSSE);
            KomentarItem("Tampilan Fisik Seragam", _model.KomentarVisual);
            KomentarItem("Komentar Manajer SPBU", _model.KomentarManager);

            col.Item().PaddingTop(20).Text("DETAIL CHECKLIST").Bold().FontSize(12);
            foreach (var root in _model.Elements)
            {
                col.Item().Text($"{root.Title} - {root.Description}").Bold().FontSize(10);
                foreach (var child in root.Children)
                {
                    RenderChecklistStructured(col, child);
                }
            }

            col.Item().PaddingTop(20).Text("PENGECEKAN QQ").Bold().FontSize(12);
            col.Item().Element(ComposeQqTable);

            //col.Item().PaddingTop(20).Text("DOKUMENTASI").Bold().FontSize(12);
            //foreach (var doc in _model.FinalDocuments)
            //{
            //    col.Item().Text(doc.MediaPath).FontSize(8);
            //}
        });
    }

    void RenderChecklistStructured(ColumnDescriptor col, AuditChecklistNode node)
    {
        var af = node.ScoreAF ?? 0;
        var percent = af * 100;
        string levelLabel = percent >= 100m ? "Excellent" : percent >= 87.5m ? "Good" : "Needs Improvement";

        col.Item().PaddingLeft(10).Text($"• {node.Title}: {node.Description} | Skor: {percent:0.##}% ({levelLabel})").FontSize(9);

        if (node.Children != null && node.Children.Any())
        {
            foreach (var sub in node.Children)
            {
                RenderChecklistStructured(col, sub);
            }
        }
    }

    void ComposeQqTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(); // Nozzle Number
                columns.RelativeColumn(); // DU Make
                columns.RelativeColumn(); // DU Serial No
                columns.RelativeColumn(); // Product
                columns.RelativeColumn(); // Mode
                columns.RelativeColumn(); // Qty Var (m)
                columns.RelativeColumn(); // Qty Var (%)
                columns.RelativeColumn(); // Density
                columns.RelativeColumn(); // Temp
                columns.RelativeColumn(); // Density15
                columns.RelativeColumn(); // Ref Density15
                columns.RelativeColumn(); // Tank No
                columns.RelativeColumn(); // Density Var
            });

            table.Header(header =>
            {
                void HeaderCell(string text) =>
                    header.Cell().Background(Colors.Grey.Lighten2)
                                 .Border(1)
                                 .BorderColor(Colors.Grey.Medium)
                                 .Text(text).Bold();

                HeaderCell("Nozzle Number");
                HeaderCell("DU Make");
                HeaderCell("DU Serial No");
                HeaderCell("Product");
                HeaderCell("Mode");
                HeaderCell("Qty Var (m)");
                HeaderCell("Qty Var (%)");
                HeaderCell("Density");
                HeaderCell("Temp");
                HeaderCell("Density15°");
                HeaderCell("Ref Density15°");
                HeaderCell("Tank No");
                HeaderCell("Density Var");
            });

            foreach (var qq in _model.QqChecks)
            {
                void DataCell(string text) =>
                    table.Cell().Border(1)
                                .BorderColor(Colors.Grey.Medium)
                                .Text(text);

                DataCell(qq.NozzleNumber.ToString());
                DataCell(qq.DuMake);
                DataCell(qq.DuSerialNo);
                DataCell(qq.Product);
                DataCell(qq.Mode);
                DataCell(qq.QuantityVariationWithMeasure.ToString());
                DataCell($"{qq.QuantityVariationInPercentage:0.00}");
                DataCell(qq.ObservedDensity.ToString());
                DataCell(qq.ObservedTemp.ToString());
                DataCell(qq.ObservedDensity15Degree.ToString());
                DataCell(qq.ReferenceDensity15Degree.ToString());
                DataCell(qq.TankNumber.ToString());
                DataCell(qq.DensityVariation.ToString());
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
            InfoRow("QUATER", _model.Quarter);

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

                table.Cell().Text($"{element.Description}").FontSize(9);
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
