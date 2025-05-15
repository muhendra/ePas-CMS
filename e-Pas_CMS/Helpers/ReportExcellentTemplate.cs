
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using e_Pas_CMS.ViewModels;
using System.Linq;
using SkiaSharp;
using QuestPDF.Drawing;   // Wajib agar ImageData dikenali
using QuestPDF.Helpers;   // Untuk ImageScaling

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

            // Gunakan header normal tanpa SERVE (semua halaman)
            page.Header().Element(ComposeHeaderWithoutServe);

            page.Content().Element(container =>
            {
                container.Column(col =>
                {
                    // SERVE hanya muncul di halaman pertama (karena berada di awal dokumen)
                    col.Item().PaddingTop(-8).Row(row =>
                    {
                        row.RelativeItem(3).Text("SERVE")
                            .FontColor("#ED7D7D")
                            .Bold()
                            .FontSize(28)
                            .LineHeight(1f);
                    });

                    // Konten utama audit
                    col.Item().Element(ComposeContent);
                });
            });
        });
    }

    void ComposeHeaderWithoutServe(IContainer container)
    {
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Images");
        var leftImagePath = Path.Combine(basePath, "pertaminaway.png");
        var rightImagePath = Path.Combine(basePath, "intertek.png");

        bool isNotCertified = _model.GoodStatus == "NOT CERTIFIED";
        var titleFontColor =  Colors.Blue.Medium;
        var subTitleFontColor = Colors.Blue.Medium;
        var descFontColor = Colors.Black;

        container.PaddingBottom(30).Row(row =>
        {
            // Logo kiri (Pertamina)
            row.RelativeItem(3).Column(left =>
            {
                left.Item().Height(50).Image(leftImagePath, ImageScaling.FitArea);
            });

            // Judul tengah
            row.RelativeItem(6).Column(center =>
            {
                center.Item().AlignCenter().Text("SPBU EXCELLENT PERFORMANCE")
                    .FontSize(13).Bold().FontColor(titleFontColor);
                center.Item().AlignCenter().Text("AUDIT REPORT")
                    .FontSize(13).Bold().FontColor(titleFontColor);
                center.Item().AlignCenter().Text("LAPORAN AUDIT PERFORMA")
                    .FontSize(11).FontColor(subTitleFontColor);
                center.Item().AlignCenter().Text(
                    _model.ExcellentStatus == "EXCELLENT" ? "SPBU EXCELLENT" :
                    _model.GoodStatus == "CERTIFIED" ? "SPBU GOOD" : "SPBU")
                    .FontSize(11).FontColor(subTitleFontColor);
                center.Item().AlignCenter().Text("Report ini merupakan dokumen elektronik sehingga tidak membutuhkan tanda tangan dan cap perusahaan")
                    .Italic().FontSize(6).FontColor(descFontColor).LineHeight(1);

            });

            // Logo kanan (Intertek)
            row.RelativeItem(3).Column(right =>
            {
                right.Item().AlignRight().Height(65).Image(rightImagePath, ImageScaling.FitArea);
            });

        });
    }

    void ComposeContent(IContainer container)
    {
        container.Column(col =>
        {
            // Warna dan teks box skor
            string boxColor = "#CCCCCC";
            string scoreFontColor = Colors.White;

            if (_model.ExcellentStatus == "EXCELLENT")
                boxColor = "#FFC107";
            else if (_model.GoodStatus == "CERTIFIED")
                boxColor = "#00A64F";
            else if (_model.GoodStatus == "NOT CERTIFIED")
                boxColor = "#F44336";

            col.Item()
   .PaddingTop(-18)
   .AlignRight()
   .Width(100)
   .Background(boxColor)
   .Padding(4)
   .Column(score =>
   {
       score.Item().AlignLeft().Text("TOTAL SCORE (TS):")
           .Bold().FontColor(scoreFontColor).FontSize(9);
       score.Item().AlignLeft().Text($"{_model.TotalScore:0.00}")
           .FontSize(16).Bold().FontColor(scoreFontColor);
       score.Item().AlignLeft().Text("Minimum Skor: 85")
           .FontSize(8).FontColor(scoreFontColor);
   });

            // Status Sertifikasi
            var statusText = "UNKNOWN";
            string statusBgColor = "#F8D7DA";

            if (_model.ExcellentStatus == "EXCELLENT")
            {
                statusText = "CERTIFIED";
                statusBgColor = "#FFC107";
            }
            else if (_model.GoodStatus == "CERTIFIED")
            {
                statusText = "CERTIFIED";
                statusBgColor = "#00A64F";
            }
            else if (_model.GoodStatus == "NOT CERTIFIED")
            {
                statusText = "NOT CERTIFIED";
                statusBgColor = "#F44336";
            }


            col.Item()
            .Background(statusBgColor)
            .Padding(8)
            .AlignCenter()
            .Text(statusText)
            .FontSize(16)
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

            var statusBoxText = _model.ExcellentStatus == "EXCELLENT"
                ? "PASTI PAS EXCELLENT!"
                : _model.GoodStatus == "CERTIFIED"
                    ? "PASTI PAS GOOD!"
                    : "NOT CERTIFIED";

            var statusColor = _model.ExcellentStatus == "EXCELLENT"
                ? "#FFC107"
                : _model.GoodStatus == "CERTIFIED"
                    ? "#00A64F"
                    : "#F44336";

            col.Item().Background(statusColor).Padding(10)
               .AlignCenter()
               .Text(statusBoxText)
               .FontSize(16).Bold()
               .FontColor(Colors.White);

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

            col.Item().PageBreak();

            col.Item().PaddingTop(20).Text("PENGECEKAN Q&Q").Bold().FontSize(12);
            col.Item().Element(ComposeQqTable);

            //col.Item().PaddingTop(20).Text("DOKUMENTASI").Bold().FontSize(12);
            //foreach (var doc in _model.FinalDocuments)
            //{
            //    col.Item().Text(doc.MediaPath).FontSize(8);
            //}

            col.Item().Element(container =>
            {
                container.Grid(grid =>
                {
                    grid.Columns(3);

                    foreach (var foto in _model.FotoTemuan)
                    {


                        if (foto == null || string.IsNullOrWhiteSpace(foto.Path))
                            continue;


                        string fullPath = null;
                        try
                        {
                            fullPath = Path.Combine("/var/www/epas-api", "wwwroot", foto.Path);

                        }
                        catch
                        {
                            continue; // Jika foto.Path berisi karakter tidak valid
                        }

                        if (!System.IO.File.Exists(fullPath))
                            continue;

                        grid.Item().Padding(5).Column(item =>
                        {
                            item.Item().Height(100).Element(e =>
                            {
                                e.Image(Image.FromFile(fullPath)).FitArea();
                            });

                            item.Item().PaddingTop(4)
                                .Text(foto.Caption ?? "Foto Temuan").FontSize(8).AlignCenter();
                        });
                    }
                });
            });




        });
    }

    void DrawImageFromUrl(SKCanvas canvas, Size size, string imageUrl)
    {
        using var httpClient = new HttpClient();
        var bytes = httpClient.GetByteArrayAsync(imageUrl).Result;
        using var skStream = new SKManagedStream(new MemoryStream(bytes));
        using var bitmap = SKBitmap.Decode(skStream);
        if (bitmap != null)
        {
            var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
            var destRect = new SKRect(0, 0, size.Width, size.Height);
            canvas.DrawBitmap(bitmap, destRect, paint);
        }
    }

    void RenderChecklistStructured(ColumnDescriptor col, AuditChecklistNode node, string prefix = "")
    {
        var indent = prefix + node.Title;
        string skorText;

        if (node.Children != null && node.Children.Any())
        {
            var af = node.ScoreAF;
            skorText = af.HasValue ? $"{af.Value * 100:0.##}%" : "-";
        }
        else
        {
            skorText = !string.IsNullOrWhiteSpace(node.ScoreInput) ? node.ScoreInput : "-";
        }

        col.Item().PaddingLeft(10).Text($"{indent}. {node.Description} | Skor: {skorText}").FontSize(9);

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
