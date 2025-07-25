
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using e_Pas_CMS.ViewModels;
using System.Linq;
using SkiaSharp;
using QuestPDF.Drawing;   // Wajib agar ImageData dikenali
using QuestPDF.Helpers;   // Untuk ImageScaling

public class ReportBOATemplate : IDocument
{
    private readonly DetailReportViewModel _model;

    public ReportBOATemplate(DetailReportViewModel model)
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
                    //col.Item().PaddingTop(-8).Row(row =>
                    //{
                    //    row.RelativeItem(3).Text("SERVE")
                    //        .FontColor("#ED7D7D")
                    //        .Bold()
                    //        .FontSize(28)
                    //        .LineHeight(1f);
                    //});

                    // Konten utama audit
                    col.Item().Element(ComposeContent);
                });
            });
        });
    }

    public void ComposeHeaderWithoutServe(IContainer container)
    {
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Images");
        var leftImagePath = Path.Combine(basePath, "pertaminaway.png");
        var rightImagePath = Path.Combine(basePath, "intertek.png");

        var titleFontColor = Colors.Blue.Medium;
        var subTitleFontColor = Colors.Blue.Medium;
        var descFontColor = Colors.Black;

        container.PaddingBottom(30).Row(row =>
        {
            row.RelativeItem(3).Column(left =>
            {
                left.Item().Height(50).Image(leftImagePath, ImageScaling.FitArea);
            });

            row.RelativeItem(6).Column(center =>
            {
                center.Item().AlignCenter().Text("PERFORMANCE AUDIT REPORT")
                    .FontSize(13).Bold().FontColor(titleFontColor);
                center.Item().AlignCenter().Text("LAPORAN AUDIT PERFORMA")
                    .FontSize(11).FontColor(subTitleFontColor);
                center.Item().AlignCenter().Text("SPBU BASIC OPERATIONAL")
                    .FontSize(11).FontColor(subTitleFontColor);
                center.Item().AlignCenter().Text("Report ini merupakan dokumen elektronik sehingga tidak membutuhkan tanda tangan dan cap perusahaan")
                    .Italic().FontSize(6).FontColor(descFontColor).LineHeight(1);
            });

            row.RelativeItem(3).Column(right =>
            {
                right.Item().AlignRight().Height(65).Image(rightImagePath, ImageScaling.FitArea);
            });
        });
    }


    void ComposeContent(IContainer container)
    {
        container.Column(col =>
        {// Step 0: Gunakan TotalScore dari model, jangan hitung ulang
         // (kecuali jika model.TotalScore belum diset dari controller)
            if (_model.TotalScore == 0)
            {
                HitungSemuaTotalScore();
                _model.TotalScore = Math.Round(_model.Elements.Sum(x => x.TotalScore ?? 0), 2);
            }

            // Step 1: Set compliance per elemen
            decimal sss = _model.SSS ?? GetCompliance("Skilled Staff & Services", 30);
            decimal eqnq = _model.EQnQ ?? GetCompliance("Exact Quality & Quantity", 30);
            decimal rve = _model.RFS ?? GetCompliance("Keandalan, Visual, Keluasan", 40);

            _model.SSS = sss;
            _model.EQnQ = eqnq;
            _model.RFS = rve;

            // Step 2: Validasi GOOD
            bool failGood = sss < 80 || eqnq < 85 || rve < 85;
            bool isCertified = _model.TotalScore >= 75 &&
                               string.IsNullOrWhiteSpace(_model.PenaltyAlertsGood) &&
                               !failGood;

            var failedElements = new List<string>();
            if (sss < 80) failedElements.Add("SSS");
            if (eqnq < 85) failedElements.Add("EQnQ");
            if (rve < 85) failedElements.Add("RFS");

            string boxColor = isCertified ? "#1aa31f" : "#F44336";
            string scoreFontColor = Colors.White;

            // Box skor total
            //col.Item().PaddingTop(-18).AlignRight().Width(100).Background(boxColor).Padding(4).Column(score =>
            //{
            //    score.Item().AlignLeft().Text("TOTAL SCORE (TS):").Bold().FontColor(scoreFontColor).FontSize(9);
            //    score.Item().AlignLeft().Text($"{_model.TotalScore:0.00}").FontSize(16).Bold().FontColor(scoreFontColor);
            //    score.Item().AlignLeft().Text("Minimum Skor: 75").FontSize(8).FontColor(scoreFontColor);
            //});

            // Informasi SPBU & Audit
            col.Item().PaddingTop(15).PaddingBottom(5).Text("Informasi SPBU").Bold().FontSize(12);
            col.Item().PaddingBottom(15).Element(ComposeInfoTable);
            col.Item().PaddingBottom(5).Text("Informasi Kegiatan Audit").Bold().FontSize(12);
            col.Item().PaddingBottom(20).Element(ComposeAuditInfoTable);

            // Box Status Sertifikasi
            col.Item().PaddingTop(10).Element(container =>
            {
                var passed = _model.TotalScore >= _model.MinPassingScore && string.IsNullOrWhiteSpace(_model.PenaltyAlerts);
                var boxColor = passed ? "#4F96D8" : "#F44336";

                container
                    .Background(boxColor)
                    .ExtendHorizontal()
                    .PaddingVertical(15)
                    .PaddingHorizontal(20)
                    .Row(row =>
                    {
                        // 1/4 KIRI
                        row.RelativeItem(1).Column(left =>
                        {
                            left.Item().AlignLeft().Text("TOTAL SCORE (TS):")
                                .FontSize(10).FontColor(Colors.White).SemiBold();

                            left.Item().AlignLeft().Text($"{_model.TotalScore:0.00}")
                                .FontSize(16).FontColor(Colors.White).Bold();

                            left.Item().AlignLeft().Text($"Minimum Skor: {_model.MinPassingScore:0.##}")
                                .FontSize(9).FontColor(Colors.White);
                        });

                        // Garis separator vertikal
                        row.ConstantItem(2).Element(e =>
                        {
                            e.BorderLeft(2).BorderColor(Colors.White).Height(40);
                        });


                        // 3/4 KANAN
                        var status = _model.TotalScore >= _model.MinPassingScore && string.IsNullOrWhiteSpace(_model.PenaltyAlerts)
                            ? "PASSED BASIC OPERATIONAL"
                            : "NOT PASSED";

                        row.RelativeItem(3).AlignMiddle().AlignCenter().Text(status)
                            .FontSize(14).Bold().FontColor(Colors.White);
                    });
            });

            col.Item().Height(20);

            // Tabel Element dan SubElement
            col.Item().PaddingVertical(10).Element(ComposeElementTable);
            foreach (var element in _model.Elements.Where(x => !string.IsNullOrWhiteSpace(x.Description)))
            {
                col.Item().PaddingTop(10).Text(element.Description).Bold().FontSize(11);
                col.Item().Element(c => ComposeSubElementTable(c, element.Children));
            }

            // Komentar Auditor
            col.Item().PaddingTop(20).Text("KOMENTAR AUDITOR").Bold().FontSize(12);
            void KomentarItem(string label, string value)
            {
                col.Item().Text(label).Bold().FontSize(10);
                col.Item().PaddingBottom(10).Text(value ?? "-").FontSize(9);
            }
            KomentarItem("Staf Terlatih dan Termotivasi", _model.KomentarStaf);
            KomentarItem("Jaminan Kualitas dan Kuantitas", _model.KomentarQuality);
            KomentarItem("Fasilitas dan Peralatan Terpelihara dengan Baik", _model.KomentarHSSE);
            KomentarItem("Komentar Manajer SPBU", _model.KomentarManager);

            // Halaman baru: Checklist
            col.Item().PageBreak();
            col.Item().PaddingTop(20).Text("DETAIL CHECKLIST").Bold().FontSize(12);
            foreach (var root in _model.Elements)
                RenderChecklistStructured(col, root, root.Title, 0);

            // Halaman baru: Q&Q
            col.Item().PageBreak();
            col.Item().PaddingTop(20).Text("PENGECEKAN Q&Q").Bold().FontSize(12);
            col.Item().Element(ComposeQqTable);

            // Halaman baru: Foto Temuan
            col.Item().PageBreak();
            col.Item().Element(container =>
            {
                container.PaddingVertical(10).Grid(grid =>
                {
                    grid.Columns(2);
                    grid.Spacing(10);

                    foreach (var foto in _model.FotoTemuan)
                    {
                        if (foto == null || string.IsNullOrWhiteSpace(foto.Path))
                            continue;

                        string fullPath;
                        try
                        {
                            var relativePath = foto.Path.TrimStart('/');
                            fullPath = Path.Combine("/var/www/epas-asset/wwwroot", relativePath);
                        }
                        catch { continue; }

                        if (!System.IO.File.Exists(fullPath))
                            continue;

                        try
                        {
                            grid.Item().Padding(5).Column(item =>
                            {
                                item.Item().Height(120).AlignCenter().AlignMiddle().Element(e =>
                                {
                                    e.Image(Image.FromFile(fullPath)).FitArea();
                                });

                                item.Item().PaddingTop(5).Element(c =>
                                {
                                    c.Container().MaxWidth(250).AlignCenter().Text(foto.Caption ?? "IMAGE")
                                        .FontSize(8).WrapAnywhere();
                                });
                            });
                        }
                        catch { continue; }
                    }
                });
            });

            // Helper lokal
            decimal GetCompliance(string name, int expectedWeight)
            {
                var el = _model.Elements.FirstOrDefault(x =>
                    (x.Title?.Trim().ToUpperInvariant().Contains(name.Trim().ToUpperInvariant()) ?? false) ||
                    (x.Description?.Trim().ToUpperInvariant().Contains(name.Trim().ToUpperInvariant()) ?? false));

                if (el == null || !el.TotalScore.HasValue)
                    return 0;

                return Math.Round((el.TotalScore.Value / expectedWeight) * 100, 2);
            }
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

    void RenderChecklistStructured(ColumnDescriptor col, AuditChecklistNode node, string prefix = "", int level = 0)
    {
        string label = (!string.IsNullOrWhiteSpace(node.number) ? node.number.Trim() + " " : "") + (node.Title?.Trim() ?? "-");
        decimal skor = 0;
        string skorText = "-";

        var nilaiAF = new Dictionary<string, decimal>
        {
            ["A"] = 1.00m,
            ["B"] = 0.80m,
            ["C"] = 0.60m,
            ["D"] = 0.40m,
            ["E"] = 0.20m,
            ["F"] = 0.00m
        };

        bool isQuestion = (node.Type ?? "").ToLower() == "question";

        if ((node.Children?.Any() ?? false) && !isQuestion)
        {
            // Rekursif ke anak-anak dulu untuk memastikan nilai anak-anak sudah dihitung
            foreach (var child in node.Children)
                RenderChecklistStructured(new ColumnDescriptor(), child, child.Title, level + 1);

            bool semuaAnakAdaTotalScore = node.Children.All(c => c.TotalScore.HasValue);
            bool adaAnakX = node.Children.Any(c => string.Equals(c.ScoreInput?.Trim(), "X", StringComparison.OrdinalIgnoreCase));

            if (semuaAnakAdaTotalScore && !adaAnakX)
            {
                skor = node.Children.Sum(c => c.TotalScore ?? 0);
            }
            else
            {
                // Hitung ulang dari leaf: baik karena ada "X" atau sebagian anak belum punya TotalScore
                decimal sumAF = 0, sumWeight = 0, sumX = 0;

                void HitungLeafLangsung(AuditChecklistNode q)
                {
                    if (q.Children != null && q.Children.Any())
                    {
                        foreach (var c in q.Children)
                            HitungLeafLangsung(c);
                    }
                    else
                    {
                        string input = q.ScoreInput?.Trim().ToUpper() ?? "";
                        decimal w = q.Weight ?? 0;

                        if (input == "X")
                        {
                            sumX += w;
                            sumAF += q.ScoreX ?? 0;
                        }
                        else if (input == "F" && q.IsRelaksasi == true)
                        {
                            sumAF += 1.00m * w;
                        }
                        else if (nilaiAF.TryGetValue(input, out var af))
                        {
                            sumAF += af * w;
                        }

                        sumWeight += w;
                    }
                }

                HitungLeafLangsung(node);

                skor = (sumWeight > 0 && (sumWeight - sumX) > 0)
                    ? (sumAF / (sumWeight - sumX)) * sumWeight
                    : 0;
            }


            node.TotalScore = Math.Round(skor, 2);
            skorText = $"Skor: {node.TotalScore:0.##}";
        }
        else
        {
            // Hitung skor langsung jika leaf (question)
            decimal w = node.Weight ?? 0;
            string input = node.ScoreInput?.Trim().ToUpper() ?? "";

            if (input == "X")
                skor = node.ScoreX ?? w;
            else if (input == "F" && node.IsRelaksasi == true)
                skor = 1.00m * w;
            else if (nilaiAF.TryGetValue(input, out var af))
                skor = af * w;

            node.TotalScore = Math.Round(skor, 2);
            skorText = !string.IsNullOrWhiteSpace(node.ScoreInput) ? node.ScoreInput.ToUpper() : "-";
        }

        // Tampilan
        string bgColor = isQuestion ? "#DAE8FC" :
            level switch
            {
                0 => "#F4B7C5",
                1 => "#E2EFDA",
                2 => "#FFF2CC",
                _ => "#FFFFFF"
            };

        col.Item().Background(bgColor)
            .PaddingVertical(6)
            .PaddingLeft(10 * level)
            .Row(row =>
            {
                row.RelativeItem(8).Element(text =>
                {
                    var content = text.Text($"{label}. {node.Description ?? ""}")
                        .FontSize(9)
                        .LineHeight(1.2f);

                    if (level <= 1)
                        content.Bold();
                });

                row.RelativeItem(4).AlignRight().Text(skorText).FontSize(9).LineHeight(1.2f);
            });

        foreach (var child in node.Children ?? new())
            RenderChecklistStructured(col, child, child.Title, level + 1);
    }

    void ComposeQqTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
            });

            // HEADER
            table.Header(header =>
            {
                void HeaderCell(string text) =>
                    header.Cell()
                          .Background(Colors.Grey.Lighten3)
                          .Border(0.5f)
                          .BorderColor(Colors.Black)
                          .AlignCenter()
                          .AlignMiddle()
                          .Text(text).Bold().FontSize(7.5f);

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

            // ISI DATA
            foreach (var qq in _model.QqChecks)
            {
                void DataCell(string text) =>
                    table.Cell()
                         .Border(0.5f)
                         .BorderColor(Colors.Black)
                         .AlignCenter()
                         .AlignMiddle()
                         .Padding(2)
                         .Text(text).FontSize(8);

                DataCell(qq.NozzleNumber.ToString());
                DataCell(qq.DuMake);
                DataCell(qq.DuSerialNo);
                DataCell(qq.Product);
                DataCell(qq.Mode);
                DataCell($"{qq.QuantityVariationWithMeasure:0}");

                // Kolom Qty Var (%) dengan warna background penuh
                var qtyVarPercent = qq.QuantityVariationInPercentage;
                var qtyVarColor = qtyVarPercent < -0.003m ? "#ff0000" :  // merah
                                  qtyVarPercent > 0.003m ? "#ffff00" :  // kuning
                                  "#ffffff";                            // putih
                var qtyTextColor = qtyVarPercent < -0.003m ? Colors.White : Colors.Black;

                table.Cell()
                     .Background(qtyVarColor)
                     .Border(0.5f)
                     .BorderColor(Colors.Black)
                     .AlignCenter()
                     .AlignMiddle()
                     .Padding(2)
                     .Text($"{qtyVarPercent:0.00}")
                        .FontColor(qtyTextColor)
                        .FontSize(8)
                        .SemiBold();

                DataCell($"{qq.ObservedDensity:0.0000}");
                DataCell($"{qq.ObservedTemp}");
                DataCell($"{qq.ObservedDensity15Degree:0.0000}");
                DataCell($"{qq.ReferenceDensity15Degree:0.0000}");
                DataCell($"{qq.TankNumber}");
                DataCell($"{qq.DensityVariation:0.0000}");
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

            InfoRow("KELAS SPBU", _model.ClassSPBU);
            InfoRow("TELEPON", _model.Phone);
        });
    }

    void ComposeAuditInfoTable(IContainer container)
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

            InfoRow("No. Report", _model.ReportNo);
            InfoRow("Tanggal Audit", _model.TanggalAudit?.ToString("dd/MM/yyyy"));
            InfoRow("Verifikator", _model.ApproveBy);

            InfoRow("Auditor 1", _model.NamaAuditor);
            InfoRow("Auditor 2", "-"); // ganti jika ada field spesifik
            InfoRow("Tipe Audit", _model.AuditCurrent);
            InfoRow("Next Audit", _model.AuditNext); // ganti jika ada field spesifik

            InfoRow("Ko-ordinator", "Sabar Kembaren");
            InfoRow("Sent Date", _model.TanggalSubmit?.ToString("dd/MM/yyyy"));
        });
    }

    void ComposeElementTable(IContainer container)
    {
        var elements = new[]
{
    new { Name = "Skilled Staff & Services", Weight = 30 },
    new { Name = "Exact Quality & Quantity", Weight = 30 },
    new { Name = "Keandalan, Visual, Keluasan", Weight = 40 },
};

        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(2);
                c.RelativeColumn();
                c.RelativeColumn();
                c.ConstantColumn(70);
            });

            table.Header(header =>
            {
                header.Cell().Text("Indikator Penilaian").Bold();
                header.Cell().AlignCenter().Text("Bobot Nilai").Bold();
                header.Cell().AlignCenter().Text("Nilai Minimum").Bold();
                header.Cell().AlignCenter().Text("Compliance Level").Bold();
            });

            foreach (var e in elements)
            {
                var modelElement = _model.Elements.FirstOrDefault(x =>
                    (x.Title?.Trim().ToUpperInvariant().Contains(e.Name.Trim().ToUpperInvariant()) ?? false) ||
                    (x.Description?.Trim().ToUpperInvariant().Contains(e.Name.Trim().ToUpperInvariant()) ?? false)
                );

                decimal skor = modelElement?.TotalScore ?? 0;
                decimal percent = e.Name switch
                {
                    "Skilled Staff & Services" => _model.SSS ?? 0,
                    "Exact Quality & Quantity" => _model.EQnQ ?? 0,
                    "Reliability, Visual, Extensiveness" => _model.RFS ?? 0,
                    _ => 0
                };

                string level;
                string levelColor;

                if (percent <= 35)
                    (level, levelColor) = ("Warning", "#FF0000");
                else if (percent <= 80)
                    (level, levelColor) = ("Poor", "#FFFF99");
                //else if (percent <= 75)
                //    (level, levelColor) = ("Average", "#CCF2F4");
                else if (percent <= 95)
                    (level, levelColor) = ("Good", "#00FF00");
                else
                    (level, levelColor) = ("Excellent", "#FFA500");

                string minText = e.Name switch
                {
                    "Skilled Staff & Services" => "80.00%",
                    "Exact Quality & Quantity" => "85.00%",
                    "Reliability, Visual, Extensiveness" => "85.00%",
                    _ => "0.00%"
                };


                table.Cell().Text(e.Name).FontSize(9);
                table.Cell().AlignCenter().Text($"{e.Weight:0}");
                table.Cell().AlignCenter().Text(minText);
                table.Cell().Border(2).BorderColor(Colors.White).Padding(2).Element(cell =>
                    cell.Container().Background(levelColor)
                        .PaddingVertical(2).PaddingHorizontal(2)
                        .AlignCenter().AlignMiddle()
                        .Column(col =>
                        {
                            col.Item().Text($"{percent:0.##}%").FontSize(8).Bold().AlignCenter();
                            col.Item().Text(level).FontSize(7).AlignCenter();
                        })
                );
            }
        });
    }

    void ComposeSubElementTable(IContainer container, List<AuditChecklistNode> children)
    {
        var subElementWeights = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
    {
        { "Sub-Elemen 1.1", 10.00m },
        { "Sub-Elemen 1.2", 20.00m },
        { "Sub-Elemen 2.1", 7.00m },
        { "Sub-Elemen 2.2", 23.00m },
        { "Sub-Elemen 3.1", 14.50m },
        { "Sub-Elemen 3.2", 4.50m },
        { "Sub-Elemen 3.3", 1.00m },
        { "Sub-Element 3.3", 1.00m },
        { "Sub-Elemen 4.1", 4.00m },
        { "Sub-Elemen 4.2", 2.00m },
        { "Sub-Elemen 4.3", 4.00m },
        { "Sub-Elemen 5.1", 2.00m },
        { "Sub-Elemen 5.2", 8.00m }
    };

        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(2);
                c.RelativeColumn();
                c.ConstantColumn(70);
            });

            table.Header(header =>
            {
                header.Cell().Text("Sub Elemen").Bold();
                header.Cell().AlignCenter().Text("Bobot").Bold();
                header.Cell().AlignCenter().Text("Compliance").Bold();
            });

            foreach (var item in children)
            {
                HitungTotalScore(item);

                decimal weight = 0;
                if (!string.IsNullOrWhiteSpace(item.Title))
                    subElementWeights.TryGetValue(item.Title.Trim(), out weight);
                if (weight == 0 && !string.IsNullOrWhiteSpace(item.Description))
                    subElementWeights.TryGetValue(item.Description.Trim(), out weight);

                var skor = item.TotalScore ?? 0;
                var percent = (weight > 0) ? (skor / weight) * 100 : 0;

                string level;
                string levelColor;

                if (percent <= 35)
                    (level, levelColor) = ("Warning", "#FF0000");
                else if (percent <= 75)
                    (level, levelColor) = ("Poor", "#FFFF99");
                //else if (percent <= 75)
                //    (level, levelColor) = ("Average", "#CCF2F4");
                else if (percent <= 95)
                    (level, levelColor) = ("Good", "#00FF00");
                else
                    (level, levelColor) = ("Excellent", "#FFA500");

                table.Cell().Text(item.Description ?? "-").FontSize(9);
                table.Cell().AlignCenter().Text($"{weight:0.##}");
                table.Cell().Border(2).BorderColor(Colors.White).Padding(2).Element(cell =>
                    cell.Container().Background(levelColor)
                        .PaddingVertical(2).PaddingHorizontal(2)
                        .AlignCenter().AlignMiddle()
                        .Column(col =>
                        {
                            col.Item().Text($"{percent:0.##}%").FontSize(8).Bold().AlignCenter();
                            col.Item().Text(level).FontSize(7).AlignCenter();
                        })
                );
            }
        });
    }

    void HitungTotalScore(AuditChecklistNode node)
    {
        var nilaiAF = new Dictionary<string, decimal>
        {
            ["A"] = 1.00m,
            ["B"] = 0.80m,
            ["C"] = 0.60m,
            ["D"] = 0.40m,
            ["E"] = 0.20m,
            ["F"] = 0.00m
        };

        decimal sumAF = 0, sumWeight = 0, sumX = 0;

        void Traverse(AuditChecklistNode n)
        {
            if (n.Children != null && n.Children.Any())
            {
                foreach (var c in n.Children)
                    Traverse(c);
            }
            else
            {
                string input = n.ScoreInput?.Trim().ToUpper() ?? "";
                decimal w = n.Weight ?? 0;

                if (input == "X")
                {
                    sumX += w;
                    sumAF += n.ScoreX ?? 0;
                }
                else if (input == "F" && n.IsRelaksasi == true)
                {
                    sumAF += 1.00m * w;
                }
                else if (nilaiAF.TryGetValue(input, out var af))
                {
                    sumAF += af * w;
                }

                sumWeight += w;
            }
        }

        Traverse(node);
        var skor = (sumWeight - sumX) > 0 ? (sumAF / (sumWeight - sumX)) * sumWeight : 0;
        node.TotalScore = skor;

        System.Diagnostics.Debug.WriteLine($"[CALC-FINAL] {node.Title} → TotalScore: {skor:0.##}");
    }

    void HitungSemuaTotalScore()
    {
        foreach (var root in _model.Elements)
            HitungTotalScore(root);
    }

}
