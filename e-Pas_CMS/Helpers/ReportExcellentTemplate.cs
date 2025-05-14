
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
            var statusText = _model.Status?.ToUpper() == "VERIFIED" ? "CERTIFIED" : "NOT CERTIFIED";
            string statusColor = "#4CAF50"; // Hijau untuk keduanya

            col.Item()
               .Background(statusColor)
               .Padding(5)
               .AlignCenter()
               .Text(statusText)
               .FontSize(14)
               .Bold()
               .FontColor(Colors.White);

            if (!string.IsNullOrWhiteSpace(_model.PenaltyAlerts))
            {
                col.Item()
                    .PaddingTop(5)
                    .Text($"Gagal di elemen: {_model.PenaltyAlerts}")
                    .FontColor(Colors.Red.Darken2)
                    .Italic()
                    .FontSize(9);
            }

            col.Item().PaddingVertical(10).Element(ComposeInfoTable);

            col.Item().PaddingBottom(10).Text($"Catatan Auditor: {_model.Notes}").Italic().FontSize(9);

            col.Item().PaddingVertical(10).Row(row =>
            {
                string scoreColor = _model.TotalScore >= 100m ? "#FFC800" : // Kuning
                                    _model.TotalScore >= 87.5m ? "#2196F3" : // Biru
                                    "#F44336"; // Merah

                row.RelativeItem()
                    .Background(scoreColor)
                    .Padding(10)
                    .Text($"TOTAL SCORE (TS): {_model.TotalScore:0.00}")
                    .Bold()
                    .FontColor(Colors.White);

                row.ConstantItem(180)
                    .AlignMiddle()
                    .AlignRight()
                    .Text($"Nilai Minimum Pasti Pas: {_model.MinPassingScore:0.00}")
                    .FontSize(9);
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
                    RenderChecklistStructured(col, child, root.Title + ".");
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

    void RenderChecklistStructured(ColumnDescriptor col, AuditChecklistNode node, string prefix = "")
    {
        // Gunakan Title sebagai nomor soal (misal "1.1", "A", dll)
        var skor = node.ScoreInput ?? "-";
        var indent = prefix + node.Title;

        col.Item().PaddingLeft(10).Text($"{indent}. {node.Description} | Skor: {skor}").FontSize(9);

        if (node.Children != null && node.Children.Any())
        {
            foreach (var child in node.Children)
            {
                RenderChecklistStructured(col, child, indent + ".");
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

            InfoRow("TIPE AUDIT", "Audit");
            InfoRow("KELAS SPBU", _model.ClassSPBU);
            InfoRow("TELEPON", _model.Phone);
        });
    }

    void ComposeElementTable(IContainer container)
    {
        var elements = new[]
        {
        new { Name = "Skilled Staff & Services", Weight = 30 },
        new { Name = "Exact Quality & Quantity", Weight = 30 },
        new { Name = "Reliable Facilities & Safety", Weight = 20 },
        new { Name = "Visual Format Consistency", Weight = 10 },
        new { Name = "Expansive Product Offer", Weight = 10 },
    };

        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(2); // Indikator
                c.RelativeColumn();  // Bobot
                c.RelativeColumn();  // Marks
                c.RelativeColumn();  // Nilai Minimum
                c.RelativeColumn();  // Compliance
            });

            table.Header(header =>
            {
                header.Cell().Text("Indikator Penilaian").Bold();
                header.Cell().AlignCenter().Text("Bobot Nilai").Bold();
                header.Cell().AlignCenter().Text("Marks").Bold();
                header.Cell().AlignCenter().Text("Nilai Minimum").Bold();
                header.Cell().AlignCenter().Text("Compliance Level").Bold();
            });

            foreach (var e in elements)
            {
                var modelElement = _model.Elements.FirstOrDefault(x => x.Description.Contains(e.Name));
                var af = modelElement?.ScoreAF ?? 0;
                var marks = e.Weight * af;
                var percent = af * 100;

                string level = percent >= 100 ? "Excellent" :
                               percent >= 87.5m ? "Good" : "Needs Improvement";

                table.Cell().Text(e.Name).FontSize(9);
                table.Cell().AlignCenter().Text($"{e.Weight:0}");
                table.Cell().AlignCenter().Text($"{marks:0.##}");
                table.Cell().AlignCenter().Text("85.00%");
                table.Cell().AlignCenter().Text($"{percent:0.##}%\n{level}").FontSize(9);
            }
        });
    }

    void ComposeSubElementTable(IContainer container, List<AuditChecklistNode> children)
    {
        // Hardcoded bobot berdasarkan urutan baris di Excel
        var weights = new decimal[]
        {
        10.00m, // 1. Standar Kebersihan
        20.00m, // 2. Prosedur Pelayanan

        7.00m,  // 3. Peralatan
        23.00m, // 4. Prosedur Monitoring

        14.50m, // 5. Kebersihan Harian
        4.50m,  // 6. Pemeliharaan berkala
        1.00m,  // 7. Uraian pemeliharaan kerusakan

        4.00m,  // 8. Identitas Visual Ritel
        2.00m,  // 9. Dispenser Unit
        4.00m,  // 10. Lain-lain

        2.00m,  // 11. Penawaran BBM
        8.00m   // 12. Penawaran Non-BBM
        };

        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(2); // Sub Elemen
                c.RelativeColumn();  // Bobot
                c.RelativeColumn();  // Marks
                c.RelativeColumn();  // Compliance
            });

            table.Header(header =>
            {
                header.Cell().Text("Sub Elemen").Bold();
                header.Cell().AlignCenter().Text("Bobot").Bold();
                header.Cell().AlignCenter().Text("Marks").Bold();
                header.Cell().AlignCenter().Text("Compliance").Bold();
            });

            for (int i = 0; i < children.Count; i++)
            {
                var item = children[i];
                var weight = i < weights.Length ? weights[i] : 0;
                var af = item.ScoreAF ?? 0;
                var marks = weight * af;
                var percent = af * 100;

                string level = percent >= 100m ? "Excellent" :
                               percent >= 87.5m ? "Good" : "Needs Improvement";

                table.Cell().Text(item.Description ?? "-").FontSize(9);
                table.Cell().AlignCenter().Text($"{weight:0.##}");
                table.Cell().AlignCenter().Text($"{marks:0.##}");
                table.Cell().AlignCenter().Text($"{percent:0.##}%\n{level}").FontSize(9);
            }
        });
    }


}
