
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using e_Pas_CMS.ViewModels;
using System.Linq;
using SkiaSharp;
using QuestPDF.Drawing;   // Wajib agar ImageData dikenali
using QuestPDF.Helpers;   // Untuk ImageScaling

public class ReportGoodTemplate : IDocument
{
    private readonly DetailReportViewModel _model;

    public ReportGoodTemplate(DetailReportViewModel model)
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
        var titleFontColor = Colors.Blue.Medium;
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
                center.Item().AlignCenter().Text("PERFORMANCE AUDIT REPORT")
                    .FontSize(13).Bold().FontColor(titleFontColor);
                center.Item().AlignCenter().Text("LAPORAN AUDIT PERFORMA")
                    .FontSize(11).FontColor(subTitleFontColor);
                center.Item().AlignCenter().Text("SPBU GOOD")
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
            bool isCertified0 = _model.TotalScore >= 85 && string.IsNullOrWhiteSpace(_model.PenaltyAlertsGood); // Atau pakai _model.MinPassingScore
            string boxColor = isCertified0 ? "#1aa31f" : "#F44336";
            string scoreFontColor = Colors.White;

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
       score.Item().AlignLeft().Text("Minimum Skor: 75")
           .FontSize(8).FontColor(scoreFontColor);
   });

            // Status Sertifikasi
            var statusText = "UNKNOWN";
            string statusBgColor = "#F8D7DA";

            if (_model.GoodStatus == "Good")
            {
                statusText = "CERTIFIED";
                statusBgColor = "#1aa31f";
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


            //col.Item()
            //.Background(statusBgColor)
            //.Padding(8)
            //.AlignCenter()
            //.Text(statusText)
            //.FontSize(16)
            //.Bold()
            //.FontColor(Colors.White); 

            //if (!string.IsNullOrWhiteSpace(_model.PenaltyAlertsGood))
            //{
            //    col.Item()
            //        .PaddingTop(5)
            //        .Text($"Gagal di elemen: {_model.PenaltyAlertsGood}")
            //        .FontColor(Colors.Red.Darken2)
            //        .Italic()
            //        .FontSize(9);
            //}

            col.Item().PaddingVertical(10).Element(ComposeInfoTable);

            //col.Item().PaddingBottom(10).Text($"Catatan Auditor: {_model.Notes}").Italic().FontSize(9);

            //var statusBoxText = _model.GoodStatus == "Good"
            //    ? "PASTI PAS Good!"
            //    : _model.GoodStatus == "CERTIFIED"
            //        ? "PASTI PAS GOOD!"
            //        : "NOT CERTIFIED";

            //var statusColor = _model.GoodStatus == "Good"
            //    ? "#1aa31f"
            //    : _model.GoodStatus == "CERTIFIED"
            //        ? "#00A64F"
            //        : "#F44336";

            var isCertified = _model.TotalScore >= 75 && string.IsNullOrWhiteSpace(_model.PenaltyAlertsGood); // Atau ambil dari _model.MinPassingScore jika ada
            var statusBoxText = isCertified ? "PASTI PAS Good!" : "NOT CERTIFIED";
            var statusColor = isCertified ? "#1aa31f" : "#F44336";

            col.Item().Background(statusColor).Padding(10).Column(box =>
            {
                box.Item().AlignCenter()
                    .Text(statusBoxText)
                    .FontSize(16).Bold()
                    .FontColor(Colors.White);

                if (!isCertified && !string.IsNullOrWhiteSpace(_model.PenaltyAlertsGood))
                {
                    box.Item().PaddingTop(5)
                        .AlignCenter()
                        .Text($"Gagal di elemen: {_model.PenaltyAlertsGood}")
                        .FontSize(9)
                        .Italic()
                        .FontColor(Colors.White);
                }
            });

            foreach (var root in _model.Elements)
            {
                RenderChecklistStructured(new ColumnDescriptor(), root, root.Title, 0); // Render dummy (tidak ditampilkan), tapi akan mengisi node.TotalScore dengan logika benar
            }

            // Tampilkan tabel Element Compliance yang butuh node.TotalScore
            col.Item().PaddingVertical(10).Element(ComposeElementTable);

            // Tampilkan tabel Sub-Element
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
            //foreach (var root in _model.Elements)
            //{
            //    col.Item().Text($"{root.Title} - {root.Description}").Bold().FontSize(10);
            //    foreach (var child in root.Children)
            //    {
            //        //RenderChecklistStructured(col, child, root.Title + ".");
            //        RenderChecklistStructured(col, child, child.Title, 1);
            //    }
            //}

            foreach (var root in _model.Elements)
            {
                RenderChecklistStructured(col, root, root.Title, 0);
            }


            col.Item().PageBreak();

            col.Item().PaddingTop(20).Text("PENGECEKAN Q&Q").Bold().FontSize(12);
            col.Item().Element(ComposeQqTable);

            //col.Item().PaddingTop(20).Text("DOKUMENTASI").Bold().FontSize(12);
            //foreach (var doc in _model.FinalDocuments)
            //{
            //    col.Item().Text(doc.MediaPath).FontSize(8);
            //}

            col.Item().PageBreak();
            col.Item().Element(container =>
            {
                container.PaddingVertical(10).Grid(grid =>
                {
                    grid.Columns(3);
                    grid.Spacing(10); // Fixed: hanya satu nilai

                    foreach (var foto in _model.FotoTemuan)
                    {
                        if (foto == null || string.IsNullOrWhiteSpace(foto.Path))
                            continue;

                        string fullPath;
                        try
                        {
                            var relativePath = foto.Path.TrimStart('/');
                            fullPath = Path.Combine("/var/www/epas-api/wwwroot", relativePath);
                        }
                        catch
                        {
                            continue;
                        }

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

                                item.Item().PaddingTop(5).Text(foto.Caption ?? "IMAGE")
                                    .FontSize(8)
                                    .AlignCenter();
                            });
                        }
                        catch
                        {
                            continue;
                        }
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

    void RenderChecklistStructured(ColumnDescriptor col, AuditChecklistNode node, string prefix = "", int level = 0)
    {
        string label = node.Title?.Trim() ?? "-";
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

        bool isSpecialElement = node.Title?.ToUpperInvariant() == "ELEMEN 2" || node.Title?.ToUpperInvariant() == "ELEMEN 5";

        if (node.Children != null && node.Children.Any())
        {
            if (isSpecialElement && level == 0)
            {
                skor = 0;

                foreach (var child in node.Children ?? new())
                {
                    decimal sumAF = 0, sumWeight = 0, sumX = 0;

                    void HitungLeaf(AuditChecklistNode q)
                    {
                        if (q.Children != null && q.Children.Any())
                        {
                            foreach (var c in q.Children)
                                HitungLeaf(c);
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

                    HitungLeaf(child);
                    decimal partial = (sumWeight - sumX) > 0 ? (sumAF / (sumWeight - sumX)) * sumWeight : 0;
                    skor += partial;
                }

                skorText = $"Skor: {skor:0.##}";
                node.TotalScore = skor;
            }
            else
            {
                // Normal hitung dari anak
                decimal sumAF = 0, sumWeight = 0, sumX = 0;

                void HitungSkor(AuditChecklistNode n)
                {
                    if (n.Children != null && n.Children.Any())
                    {
                        foreach (var c in n.Children)
                            HitungSkor(c);
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

                HitungSkor(node);
                skor = (sumWeight - sumX) > 0 ? (sumAF / (sumWeight - sumX)) * sumWeight : 0;
                skorText = $"Skor: {skor:0.##}";
                node.TotalScore = skor;
            }
        }
        else
        {
            // Pertanyaan langsung
            decimal w = node.Weight ?? 0;
            string input = node.ScoreInput?.Trim().ToUpper() ?? "";

            if (input == "X")
                skor = node.ScoreX ?? 0;
            else if (input == "F" && node.IsRelaksasi == true)
                skor = 1.00m * w;
            else if (nilaiAF.TryGetValue(input, out var af))
                skor = af * w;

            node.TotalScore = skor;
            skorText = !string.IsNullOrWhiteSpace(node.ScoreInput) ? node.ScoreInput.ToUpper() : "-";

        }

        // Pewarnaan sesuai level
        string bgColor = (node.Type ?? "").ToLower() == "question"
            ? "#DAE8FC"
            : level switch
            {
                0 => "#F4B7C5",
                1 => "#E2EFDA",
                2 => "#FFF2CC",
                _ => "#FFFFFF"
            };

        var leftPad = 10 * level;

        // Tampilkan baris dengan warna dan skor
        col.Item().Background(bgColor)
            .PaddingVertical(6)
            .PaddingLeft(leftPad)
            .Row(row =>
            {
                row.RelativeItem(8).Element(text =>
                {
                    var content = text.Text($"{label}. {node.Description ?? "-"}")
                        .FontSize(9)
                        .LineHeight(1.2f);

                    if (level <= 1)
                        content = content.Bold();
                });

                row.RelativeItem(4).AlignRight().Text(skorText)
                    .FontSize(9).LineHeight(1.2f);
            });

        // Render child (jika ada)
        foreach (var child in node.Children ?? new())
        {
            RenderChecklistStructured(col, child, child.Title, level + 1);
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
                decimal percent = (e.Weight > 0) ? (skor / e.Weight) * 100 : 0;

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

                string minText = e.Name switch
                {
                    "Skilled Staff & Services" => "75.00%",
                    "Exact Quality & Quantity" => "80.00%",
                    "Reliable Facilities & Safety" => "85.00%",
                    "Visual Format Consistency" => "15.00%",
                    "Expansive Product Offer" => "25.00%",
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

}
