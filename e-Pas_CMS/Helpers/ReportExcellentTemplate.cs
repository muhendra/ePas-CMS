
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

            page.Footer().Row(row =>
            {
                // Kiri
                row.RelativeItem()
                    .AlignLeft()
                    .Text("VER 2015 REBORN.v04")
                    .FontSize(8)
                    .Italic();

                // Tengah
                row.RelativeItem()
                    .AlignCenter()
                    .Text(text =>
                    {
                        text.Span("Page ").FontSize(8);
                        text.CurrentPageNumber().FontSize(8);
                        text.Span(" of ").FontSize(8);
                        text.TotalPages().FontSize(8);
                    });

                // Kanan kosong
                row.RelativeItem()
    .AlignRight()
    .Text(_model.CreatedDateBanding.HasValue
        ? $"rev01-{_model.CreatedDateBanding.Value:ddMMyyyy}"
        : "rev01-");
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
                center.Item().AlignCenter().Text("PERFORMANCE AUDIT REPORT")
                    .FontSize(13).Bold().FontColor(titleFontColor);
                center.Item().AlignCenter().Text("LAPORAN AUDIT PERFORMA")
                    .FontSize(11).FontColor(subTitleFontColor);
                center.Item().AlignCenter().Text("SPBU EXCELLENT")
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
            // 0) Pastikan TotalScore ada
            if (_model.TotalScore == 0)
            {
                HitungSemuaTotalScore();
                _model.TotalScore = Math.Round(_model.Elements.Sum(x => x.TotalScore ?? 0), 2);
            }

            // 1) Special node override (C => Good only, non-A => Not Certified)
            var specialNodeIds = new[]
            {
            "555fe2e4-b95b-461b-9c92-ad8b5c837119",
            "bafc206f-ed29-4bbc-8053-38799e186fb0",
            "d26f4caa-e849-4ab4-9372-298693247272"
            };

            bool forceGoodOnly = false;
            bool forceNotCertified = false;

            foreach (var nodeId in specialNodeIds)
            {
                var node = FindNodeById(_model.Elements, nodeId);
                var score = node?.ScoreInput?.Trim().ToUpperInvariant();

                if (score == "C") forceGoodOnly = true;
                else if (score != "A") forceNotCertified = true;
            }

            // 2) Compliance per elemen (pakai yang ada, atau hitung jika null)
            _model.SSS ??= GetCompliance("Skilled Staff & Services", 30);
            _model.EQnQ ??= GetCompliance("Exact Quality & Quantity", 30);
            _model.RFS ??= GetCompliance("Reliable Facilities & Safety", 20);
            _model.VFC ??= GetCompliance("Visual Format Consistency", 10);
            _model.EPO ??= GetCompliance("Expansive Product Offer", 10);

            var sss = _model.SSS;
            var eqnq = _model.EQnQ;
            var rfs = _model.RFS;
            var vfc = _model.VFC;
            var epo = _model.EPO;

            // 3) Validasi EXCELLENT
            bool failExcellent = sss < 85 || eqnq < 85 || rfs < 85 || vfc < 20 || epo < 50;

            bool isCertified = _model.TotalScore >= 80 &&
                               string.IsNullOrWhiteSpace(_model.PenaltyAlerts) &&
                               !failExcellent &&
                               !forceNotCertified;

            var statusText = isCertified
                ? (forceGoodOnly ? "PASTI PAS GOOD!" : "PASTI PAS EXCELLENT!")
                : "NOT CERTIFIED";

            var barColor = isCertified ? "#FFB900" : "#F44336";

            // ==== INFO SPBU & AUDIT ====
            col.Item().PaddingTop(15).PaddingBottom(5).Text("Informasi SPBU").Bold().FontSize(12);
            col.Item().PaddingBottom(15).Element(ComposeInfoTable);
            col.Item().PaddingBottom(5).Text("Informasi Kegiatan Audit").Bold().FontSize(12);
            col.Item().PaddingBottom(20).Element(ComposeAuditInfoTable);

            // ==== BAR TOTAL SCORE ala BOA (full width) ====
            col.Item().PaddingTop(10).Element(containerBar =>
            {
                containerBar
                    .Background(barColor)
                    .ExtendHorizontal()
                    .PaddingVertical(15)
                    .PaddingHorizontal(20)
                    .Row(row =>
                    {
                        // Kiri: TOTAL SCORE + minimum
                        row.RelativeItem(1).Column(left =>
                        {
                            left.Item().AlignLeft().Text("TOTAL SCORE (TS):")
                                .FontSize(10).FontColor(Colors.White).SemiBold();

                            left.Item().AlignLeft().Text($"{_model.TotalScore:0.00}")
                                .FontSize(16).FontColor(Colors.White).Bold();

                            left.Item().AlignLeft().Text("Minimum Skor: 80")
                                .FontSize(9).FontColor(Colors.White);
                        });

                        // Separator
                        row.ConstantItem(2).Element(e =>
                        {
                            e.BorderLeft(2).BorderColor(Colors.White).Height(40);
                        });

                        // Kanan: STATUS + alasan gagal (kalau ada)
                        row.RelativeItem(3).AlignMiddle().Column(right =>
                        {
                            right.Item().AlignCenter().Text(statusText)
                                .FontSize(14).Bold().FontColor(Colors.White);

                            if (!isCertified)
                            {
                                if (!string.IsNullOrWhiteSpace(_model.PenaltyAlerts))
                                {
                                    right.Item().AlignCenter().PaddingTop(5)
                                        .Text($"Penalty: {_model.PenaltyAlerts}")
                                        .FontSize(9).Italic().FontColor(Colors.White);
                                }

                                var failed = new List<string>();
                                if (sss < 85) failed.Add("SSS");
                                if (eqnq < 85) failed.Add("EQnQ");
                                if (rfs < 85) failed.Add("RFS");
                                if (vfc < 20) failed.Add("VFC");
                                if (epo < 50) failed.Add("EPO");

                                if (failed.Any())
                                {
                                    right.Item().AlignCenter().PaddingTop(3)
                                        .Text($"Gagal di elemen: {string.Join(", ", failed)}")
                                        .FontSize(9).Italic().FontColor(Colors.White);
                                }
                            }
                        });
                    });
            });

            col.Item().Height(20);

            // ==== Tabel Element & SubElement ====
            col.Item().PaddingVertical(10).Element(ComposeElementTable);

            foreach (var element in _model.Elements.Where(x => !string.IsNullOrWhiteSpace(x.Description)))
            {
                col.Item().PaddingTop(10).Text(element.Description).Bold().FontSize(11);
                col.Item().Element(c => ComposeSubElementTable(c, element.Children));
            }

            // ==== Komentar Auditor ====
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
            KomentarItem("Penawaran Produk Komperhensif", _model.PenawaranKomperhensif);
            KomentarItem("Komentar Manajer SPBU", _model.KomentarManager);

            // ==== Halaman baru: Checklist ====
            col.Item().PageBreak();
            col.Item().PaddingTop(20).Text("DETAIL CHECKLIST").Bold().FontSize(12);
            foreach (var root in _model.Elements)
                RenderChecklistStructured(col, root, root.Title, 0);

            // ==== Halaman baru: Q&Q ====
            col.Item().PageBreak();
            col.Item().PaddingTop(20).Text("PENGECEKAN Q&Q").Bold().FontSize(12);
            col.Item().Element(ComposeQqTable);

            // ==== Halaman baru: Foto Temuan ====
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
        });
    }

    AuditChecklistNode FindNodeById(List<AuditChecklistNode> nodes, string id)
    {
        foreach (var node in nodes)
        {
            if (node.Id == id)
                return node;

            if (node.Children?.Any() == true)
            {
                var found = FindNodeById(node.Children, id);
                if (found != null)
                    return found;
            }
        }
        return null;
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
                // Qty Var (m)
                var qtyVarM = qq.QuantityVariationWithMeasure;
                var qtyVarMColor = qtyVarM < -60m ? "#ff0000" : "#ffffff"; // merah jika < -60
                var qtyVarMTextColor = qtyVarM < -60m ? Colors.White : Colors.Black;

                table.Cell()
                    .Background(qtyVarMColor)
                    .Border(0.5f)
                    .BorderColor(Colors.Black)
                    .AlignCenter()
                    .AlignMiddle()
                    .Padding(2)
                    .Text($"{qtyVarM:0}")
                        .FontColor(qtyVarMTextColor)
                        .FontSize(8)
                        .SemiBold();

                // Qty Var (%) dihitung dari Qty Var (m) / 20000 x 100%
                decimal qtyVarPercent = (decimal)((qtyVarM / 20000m) * 100m); // -120 -> -0.60%
                var qtyVarColor = qtyVarPercent < -0.3m ? "#ff0000" :  // merah jika kurang dari -0.3%
                                  qtyVarPercent > 0.3m ? "#ffff00" :  // kuning jika lebih dari 0.3%
                                                          "#ffffff";   // putih jika normal
                var qtyTextColor = qtyVarPercent < -0.3m ? Colors.White : Colors.Black;

                table.Cell()
                    .Background(qtyVarColor)
                    .Border(0.5f)
                    .BorderColor(Colors.Black)
                    .AlignCenter()
                    .AlignMiddle()
                    .Padding(2)
                    .Text($"{qtyVarPercent:0.00}%") // tampilkan dengan tanda %
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
                columns.RelativeColumn(); // label kiri
                columns.RelativeColumn(); // value kiri
                columns.RelativeColumn(); // label kanan
                columns.RelativeColumn(); // value kanan
            });

            void InfoRow(string labelLeft, string valueLeft, string labelRight, string valueRight)
            {
                table.Cell().Text(labelLeft).SemiBold();
                table.Cell().Text(valueLeft ?? "-");
                table.Cell().Text(labelRight).SemiBold();
                table.Cell().Text(valueRight ?? "-");
            }

            InfoRow("Nomor SPBU", _model.SpbuNo,
                    "Region", _model.Region);

            InfoRow("Kota", _model.Kota,
                    "Alamat", _model.Alamat);

            InfoRow("Nama Pemilik", _model.OwnerName,
                    "Nama Manager", _model.ManagerName);

            InfoRow("Tipe Kepemilikan", _model.OwnershipType,
                    "Quarter", _model.Quarter);

            InfoRow("Tahun", _model.Year.ToString(),
                    "Telepon", _model.Phone);

            InfoRow("Sales Area", _model.SalesArea,
                    "SBM", _model.SBM);
        });
    }

    void ComposeAuditInfoTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(); // label kiri
                columns.RelativeColumn(); // value kiri
                columns.RelativeColumn(); // label kanan
                columns.RelativeColumn(); // value kanan
            });

            void InfoRow(string labelLeft, string valueLeft, string labelRight, string valueRight)
            {
                table.Cell().Text(labelLeft).SemiBold();
                table.Cell().Text(valueLeft ?? "-");
                table.Cell().Text(labelRight).SemiBold();
                table.Cell().Text(valueRight ?? "-");
            }

            InfoRow("Tanggal Audit", _model.TanggalAudit?.ToString("dd/MM/yyyy"),
                    "No. Report", _model.ReportNo);

            InfoRow("Tipe Audit", _model.AuditCurrent,
                    "Next Audit", _model.AuditNext);

            InfoRow("Koordinator", "Rommy Irawan",
                    "Sent Date", _model.TanggalSubmit?.ToString("dd/MM/yyyy"));

            InfoRow("Verifikator", _model.ApproveBy,
                    "Kelas SPBU", _model.ClassSPBU);

            InfoRow("Auditor 1", _model.NamaAuditor,
                    "", "");

            InfoRow("Auditor 2", "-",
                    "", "");

            InfoRow("Acknowledge", "Sabar Kembaren",
                    "", "");
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

        var failedElements = new List<string>();

        container.Column(col =>
        {
            col.Item().Table(table =>
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

                    decimal percent = e.Name switch
                    {
                        "Skilled Staff & Services" => _model.SSS ?? 0,
                        "Exact Quality & Quantity" => _model.EQnQ ?? 0,
                        "Reliable Facilities & Safety" => _model.RFS ?? 0,
                        "Visual Format Consistency" => _model.VFC ?? 0,
                        "Expansive Product Offer" => _model.EPO ?? 0,
                        _ => 0
                    };

                    bool isFailed = e.Name switch
                    {
                        "Skilled Staff & Services" => percent < 85,
                        "Exact Quality & Quantity" => percent < 85,
                        "Reliable Facilities & Safety" => percent < 85,
                        "Visual Format Consistency" => percent < 20,
                        "Expansive Product Offer" => percent < 50,
                        _ => percent < 75
                    };

                    if (isFailed)
                        failedElements.Add(e.Name);

                    string level;
                    string levelColor;

                    if (percent <= 35)
                        (level, levelColor) = ("Warning", "#FF0000");
                    else if (percent <= 80)
                        (level, levelColor) = ("Poor", "#FFFF99");
                    else
                        (level, levelColor) = ("Excellent", "#FFA500");

                    string minText = e.Name switch
                    {
                        "Skilled Staff & Services" => "85.00%",
                        "Exact Quality & Quantity" => "85.00%",
                        "Reliable Facilities & Safety" => "85.00%",
                        "Visual Format Consistency" => "20.00%",
                        "Expansive Product Offer" => "50.00%",
                        _ => "75.00%"
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
                else if (percent <= 80)
                    (level, levelColor) = ("Poor", "#FFFF99");
                //else if (percent <= 80)
                //    (level, levelColor) = ("Average", "#CCF2F4");
                //else if (percent <= 95)
                //    (level, levelColor) = ("Good", "#00FF00");
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

    decimal GetCompliance(string name, int expectedWeight)
    {
        var el = _model.Elements.FirstOrDefault(x =>
            string.Equals(x.Title?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.Description?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (el == null || !el.TotalScore.HasValue)
            return 0;

        return Math.Round((el.TotalScore.Value / expectedWeight) * 100, 2);
    }

}
