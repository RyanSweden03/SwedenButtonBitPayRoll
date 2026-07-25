using iText.IO.Font;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using SwedenBttnBit.Domain;
using SwedenBttnBit.Services;
using System.Globalization;

namespace SwedenBttnBit.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PayRollController : Controller
    {
        private readonly SmtpSettings _smtpSettings;
        private readonly IPayRollHistoryStore _historyStore;
        private readonly IGuidePdfArchive _pdfArchive;

        public PayRollController(IConfiguration configuration, IPayRollHistoryStore historyStore, IGuidePdfArchive pdfArchive)
        {
            _smtpSettings = configuration.GetSection("SmtpSettings").Get<SmtpSettings>()!;
            _historyStore = historyStore;
            _pdfArchive = pdfArchive;
        }

        [HttpGet("history")]
        public IActionResult GetHistory()
        {
            return Ok(_historyStore.GetAll());
        }

        [HttpPost("get-payroll")]
        public IActionResult GeneratePDF([FromBody] PayRoll payroll)
        {
            if (payroll == null)
                return BadRequest("Payload inválido.");

            // === Tipografía ===
            PdfFont fontLato = PdfFontFactory.CreateFont("Fonts/Lato-Regular.ttf", PdfEncodings.IDENTITY_H);
            PdfFont fontLatoBold = PdfFontFactory.CreateFont("Fonts/Lato-Bold.ttf", PdfEncodings.IDENTITY_H);

            // === Estilo (como tu screenshot) ===
            Color brand = new DeviceRgb(17, 24, 39);      // dark bar
            Color soft = new DeviceRgb(243, 244, 246);    // light gray blocks
            Color border = new DeviceRgb(229, 231, 235);  // borders
            Color muted = new DeviceRgb(107, 114, 128);   // secondary text
            Color white = ColorConstants.WHITE;

            var borderThin = new SolidBorder(border, 0.8f);

            var cultureMoney = new CultureInfo("en-US"); // $ 9,930.10 como tu ejemplo

            // === Helpers ===
            static string Safe(object? v) => v?.ToString() ?? string.Empty;

            string Money(decimal v) => "$" + v.ToString("N2", cultureMoney);

            Cell CellText(
                string text,
                PdfFont font,
                float size,
                bool bold = false,
                TextAlignment align = TextAlignment.LEFT,
                Color? bg = null,
                Color? fg = null,
                Border? cellBorder = null,
                float paddingY = 8,
                float paddingX = 10
            )
            {
                var p = new Paragraph(text ?? string.Empty)
                    .SetMargin(0)
                    .SetMultipliedLeading(1.1f);

                var c = new Cell()
                    .Add(p)
                    .SetTextAlignment(align)
                    .SetVerticalAlignment(VerticalAlignment.MIDDLE)
                    .SetFont(bold ? font : font)
                    .SetFontSize(size)
                    .SetPaddingTop(paddingY)
                    .SetPaddingBottom(paddingY)
                    .SetPaddingLeft(paddingX)
                    .SetPaddingRight(paddingX);

                if (bold) c.SetFont(font);

                if (bg != null) c.SetBackgroundColor(bg);
                if (fg != null) c.SetFontColor(fg);

                c.SetBorder(cellBorder ?? borderThin);
                return c;
            }

            Cell CellNoBorder(string text, PdfFont font, float size, TextAlignment align = TextAlignment.LEFT, bool bold = false, Color? fg = null)
            {
                var p = new Paragraph(text ?? string.Empty).SetMargin(0).SetMultipliedLeading(1.1f);
                var c = new Cell().Add(p)
                    .SetBorder(Border.NO_BORDER)
                    .SetFont(bold ? font : font)
                    .SetFontSize(size)
                    .SetTextAlignment(align);

                if (bold) c.SetFont(font);
                if (fg != null) c.SetFontColor(fg);
                return c;
            }

            void AddSpacer(Table t, float height)
            {
                t.AddCell(new Cell().SetBorder(Border.NO_BORDER).SetHeight(height));
            }

            // === Archivo ===
            var d = payroll.Date;
            string dateLabel = $"{d:dd/MM/yyyy}";
            string fullyear = $"{d:dd_MM_yyyy}";
            string fileName = $"{Safe(payroll.GuideNumber).Trim()}_{fullyear}_{Safe(payroll.Destinatary).Trim()}.pdf"
                .Replace(" ", "_");

            // === PDF setup ===
            using var memoryStream = new MemoryStream();
            using var writer = new PdfWriter(memoryStream);
            using var pdf = new PdfDocument(writer);
            var document = new Document(pdf, PageSize.A4);
            document.SetMargins(30, 30, 30, 30);

            // === Contenedor general ===
            var main = new Table(UnitValue.CreatePercentArray(new float[] { 100f }))
                .SetWidth(UnitValue.CreatePercentValue(100))
                .SetBorder(Border.NO_BORDER);

            // =========================
            // HEADER (logo izquierda, meta derecha)
            // =========================
            var header = new Table(UnitValue.CreatePercentArray(new float[] { 65f, 35f }))
                .SetWidth(UnitValue.CreatePercentValue(100))
                .SetBorder(Border.NO_BORDER);

            // Logo (más pequeño, sin celda)
            Image logo;
            try
            {
                logo = new Image(iText.IO.Image.ImageDataFactory.Create("Images/LOGO.png"))
                    .SetWidth(150)   // antes 180
                    .SetHeight(60);  // antes 70
            }
            catch
            {
                logo = new Image(iText.IO.Image.ImageDataFactory.Create(new byte[] { }))
                    .SetWidth(140)
                    .SetHeight(55);
            }

            // Se agrega directamente, sin Cell
            header.AddCell(
                new Cell()
                    .Add(logo)
                    .SetBorder(Border.NO_BORDER)
                    .SetPadding(0)
            );


            // Meta derecha (Fecha, RUC, dirección)
            var meta = new Table(UnitValue.CreatePercentArray(new float[] { 100f }))
                .SetWidth(UnitValue.CreatePercentValue(100))
                .SetBorder(Border.NO_BORDER);

            meta.AddCell(CellNoBorder($"Fecha: {dateLabel}", fontLato, 10, TextAlignment.RIGHT, false));
            meta.AddCell(CellNoBorder($"RUC: 20606064552", fontLato, 10, TextAlignment.RIGHT, false));
            meta.AddCell(CellNoBorder($"Mayorazgo 4 etapa, Av. Asturias", fontLato, 9.5f, TextAlignment.RIGHT, false, muted));
            meta.AddCell(CellNoBorder($"Ate – Vitarte", fontLato, 9.5f, TextAlignment.RIGHT, false, muted));

            header.AddCell(new Cell().Add(meta).SetBorder(Border.NO_BORDER).SetPaddingLeft(10));

            main.AddCell(new Cell().Add(header).SetBorder(Border.NO_BORDER));
            AddSpacer(main, 12);

            // =========================
            // EMPRESA DESTINATARIA (COMPACTA)
            // =========================
            var destinataryWrapper = new Table(UnitValue.CreatePercentArray(new float[] { 100f }))
                .SetWidth(UnitValue.CreatePercentValue(100))
                .SetBorder(borderThin);

            // Barra dark (más delgada)
            destinataryWrapper.AddCell(
                new Cell()
                    .Add(new Paragraph("EMPRESA DESTINATARIA").SetMargin(0))
                    .SetBackgroundColor(brand)
                    .SetFont(fontLatoBold)
                    .SetFontColor(white)
                    .SetFontSize(9.5f)
                    .SetPaddingTop(6)
                    .SetPaddingBottom(6)
                    .SetPaddingLeft(10)
                    .SetBorder(Border.NO_BORDER)
            );

            // Contenido: etiqueta / valor
            var destinataryBody = new Table(UnitValue.CreatePercentArray(new float[] { 22f, 78f }))
                .SetWidth(UnitValue.CreatePercentValue(100))
                .SetBorder(Border.NO_BORDER);

            // helper compacto
            void AddKV(string k, string v)
            {
                destinataryBody.AddCell(
                    new Cell()
                        .Add(new Paragraph(k)
                            .SetMargin(0)
                            .SetMultipliedLeading(1.0f))
                        .SetFont(fontLatoBold)
                        .SetFontSize(9.2f)
                        .SetBorder(Border.NO_BORDER)
                        .SetPaddingTop(4)
                        .SetPaddingBottom(4)
                        .SetPaddingLeft(10)
                );

                destinataryBody.AddCell(
                    new Cell()
                        .Add(new Paragraph(v)
                            .SetMargin(0)
                            .SetMultipliedLeading(1.0f))
                        .SetFont(fontLato)
                        .SetFontSize(9.2f)
                        .SetBorder(Border.NO_BORDER)
                        .SetPaddingTop(4)
                        .SetPaddingBottom(4)
                        .SetPaddingRight(10)
                );
            }

            AddKV("Razón social:", Safe(payroll.Destinatary));
            AddKV("R.U.C. Nº:", Safe(payroll.DestinataryRUC));
            AddKV("Dirección:", Safe(payroll.DestinataryAddress));
            AddKV("Distrito:", Safe(payroll.DestinataryDistrict));
            AddKV("Guía N°:", Safe(payroll.GuideNumber));

            // wrapper body (menos padding)
            destinataryWrapper.AddCell(
                new Cell()
                    .Add(destinataryBody)
                    .SetBorder(Border.NO_BORDER)
                    .SetPaddingTop(6)
                    .SetPaddingBottom(6)
            );

            main.AddCell(new Cell().Add(destinataryWrapper).SetBorder(Border.NO_BORDER));
            AddSpacer(main, 8);


            // =========================
            // TABLA PRODUCTOS (header dark + zebra)
            // =========================
            var products = new Table(UnitValue.CreatePercentArray(new float[] { 15f, 15f, 15f, 55f }))
                .SetWidth(UnitValue.CreatePercentValue(100))
                .SetBorder(borderThin);

            // Header
            Cell HeaderCell(string t, TextAlignment align) =>
                new Cell()
                    .Add(new Paragraph(t).SetMargin(0))
                    .SetBackgroundColor(brand)
                    .SetFont(fontLatoBold)
                    .SetFontColor(white)
                    .SetFontSize(10.8f)
                    .SetTextAlignment(align)
                    .SetPadding(10)
                    .SetBorderRight(new SolidBorder(ColorConstants.WHITE, 0.2f))
                    .SetBorderTop(Border.NO_BORDER)
                    .SetBorderLeft(Border.NO_BORDER)
                    .SetBorderBottom(Border.NO_BORDER);

            products.AddHeaderCell(HeaderCell("PRECIO", TextAlignment.CENTER));
            products.AddHeaderCell(HeaderCell("CANTIDAD", TextAlignment.CENTER));
            products.AddHeaderCell(HeaderCell("TOTAL", TextAlignment.CENTER));
            products.AddHeaderCell(
                new Cell()
                    .Add(new Paragraph("DESCRIPCIÓN").SetMargin(0))
                    .SetBackgroundColor(brand)
                    .SetFont(fontLatoBold)
                    .SetFontColor(white)
                    .SetFontSize(10.8f)
                    .SetTextAlignment(TextAlignment.LEFT)
                    .SetPadding(10)
                    .SetBorder(Border.NO_BORDER)
            );

            decimal subtotal = 0m;

            if (payroll.Products != null && payroll.Products.Count > 0)
            {
                for (int i = 0; i < payroll.Products.Count; i++)
                {
                    var p = payroll.Products[i];
                    var rowBg = (i % 2 == 0) ? ColorConstants.WHITE : soft;

                    decimal price = p.Price;
                    int qty = p.Quantity;
                    decimal rowTotal = price * qty;
                    subtotal += rowTotal;

                    products.AddCell(
                        new Cell().Add(new Paragraph(Money(price)).SetMargin(0))
                            .SetBackgroundColor(rowBg)
                            .SetFont(fontLato)
                            .SetFontSize(10.2f)
                            .SetTextAlignment(TextAlignment.CENTER)
                            .SetPadding(9)
                            .SetBorder(borderThin)
                    );
                    products.AddCell(
                        new Cell().Add(new Paragraph(qty.ToString()).SetMargin(0))
                            .SetBackgroundColor(rowBg)
                            .SetFont(fontLato)
                            .SetFontSize(10.2f)
                            .SetTextAlignment(TextAlignment.CENTER)
                            .SetPadding(9)
                            .SetBorder(borderThin)
                    );
                    products.AddCell(
                        new Cell().Add(new Paragraph(Money(rowTotal)).SetMargin(0))
                            .SetBackgroundColor(rowBg)
                            .SetFont(fontLato)
                            .SetFontSize(10.2f)
                            .SetTextAlignment(TextAlignment.CENTER)
                            .SetPadding(9)
                            .SetBorder(borderThin)
                    );

                    var desc = !string.IsNullOrWhiteSpace(p.Description) ? p.Description : p.Name;

                    products.AddCell(
                        new Cell().Add(new Paragraph(desc ?? string.Empty).SetMargin(0))
                            .SetBackgroundColor(rowBg)
                            .SetFont(fontLato)
                            .SetFontSize(10.2f)
                            .SetTextAlignment(TextAlignment.LEFT)
                            .SetPadding(9)
                            .SetBorder(borderThin)
                    );
                }
            }
            else
            {
                // Fila vacía si no hay productos
                var rowBg = soft;
                products.AddCell(new Cell().Add(new Paragraph("-").SetMargin(0)).SetBackgroundColor(rowBg).SetBorder(borderThin).SetPadding(9).SetTextAlignment(TextAlignment.CENTER));
                products.AddCell(new Cell().Add(new Paragraph("-").SetMargin(0)).SetBackgroundColor(rowBg).SetBorder(borderThin).SetPadding(9).SetTextAlignment(TextAlignment.CENTER));
                products.AddCell(new Cell().Add(new Paragraph("-").SetMargin(0)).SetBackgroundColor(rowBg).SetBorder(borderThin).SetPadding(9).SetTextAlignment(TextAlignment.CENTER));
                products.AddCell(new Cell().Add(new Paragraph("Sin items").SetMargin(0)).SetBackgroundColor(rowBg).SetBorder(borderThin).SetPadding(9));
            }

            main.AddCell(new Cell().Add(products).SetBorder(Border.NO_BORDER));
            AddSpacer(main, 18);

            // =========================
            // TOTALES (caja a la derecha como tu screenshot)
            // =========================
            decimal total = subtotal;

            var totalsWrap = new Table(UnitValue.CreatePercentArray(new float[] { 65f, 35f }))
                .SetWidth(UnitValue.CreatePercentValue(100))
                .SetBorder(Border.NO_BORDER);

            totalsWrap.AddCell(new Cell().SetBorder(Border.NO_BORDER)); // espacio izquierdo

            var totalsBox = new Table(UnitValue.CreatePercentArray(new float[] { 55f, 45f }))
                .SetWidth(UnitValue.CreatePercentValue(100))
                .SetBorder(borderThin)
                .SetBackgroundColor(soft);

            void AddTotalRow(string label, string value, bool isFinal = false)
            {
                var left = new Cell()
                    .Add(new Paragraph(label).SetMargin(0))
                    .SetFont(isFinal ? fontLatoBold : fontLatoBold)
                    .SetFontSize(isFinal ? 9f : 8.3f)
                    .SetBorder(Border.NO_BORDER)
                    .SetPadding(10);

                var right = new Cell()
                    .Add(new Paragraph(value).SetMargin(0))
                    .SetFont(isFinal ? fontLatoBold : fontLato)
                    .SetFontSize(isFinal ? 9f : 8.3f)
                    .SetTextAlignment(TextAlignment.RIGHT)
                    .SetBorder(Border.NO_BORDER)
                    .SetPadding(10);

                if (isFinal)
                {
                    left.SetBackgroundColor(ColorConstants.WHITE);
                    right.SetBackgroundColor(ColorConstants.WHITE);
                }

                totalsBox.AddCell(left);
                totalsBox.AddCell(right);

                if (!isFinal)
                {
                    // línea divisoria sutil
                    totalsBox.AddCell(new Cell(1, 2)
                        .SetBorderTop(new SolidBorder(border, 0.8f))
                        .SetBorderLeft(Border.NO_BORDER)
                        .SetBorderRight(Border.NO_BORDER)
                        .SetBorderBottom(Border.NO_BORDER)
                        .SetPadding(0)
                        .SetHeight(0));
                }
            }

            AddTotalRow("Total:", Money(total), isFinal: true);

            totalsWrap.AddCell(new Cell().Add(totalsBox).SetBorder(Border.NO_BORDER));

            main.AddCell(new Cell().Add(totalsWrap).SetBorder(Border.NO_BORDER));
            AddSpacer(main, 28);

            // =========================
            // FIRMAS (línea + label)
            // =========================
            var signature = new Table(UnitValue.CreatePercentArray(new float[] { 50f, 50f }))
                .SetWidth(UnitValue.CreatePercentValue(100))
                .SetBorder(Border.NO_BORDER);

            Cell SigBlock(string label)
            {
                var wrapper = new Table(UnitValue.CreatePercentArray(new float[] { 100f }))
                    .SetWidth(UnitValue.CreatePercentValue(100))
                    .SetBorder(Border.NO_BORDER);

                wrapper.AddCell(
                    new Cell()
                        .SetHeight(70)
                        .SetBorderTop(Border.NO_BORDER)
                        .SetBorderLeft(Border.NO_BORDER)
                        .SetBorderRight(Border.NO_BORDER)
                        .SetBorderBottom(new SolidBorder(border, 1f))
                );

                wrapper.AddCell(
                    new Cell()
                        .Add(new Paragraph(label).SetMargin(0))
                        .SetFont(fontLatoBold)
                        .SetFontSize(11)
                        .SetTextAlignment(TextAlignment.CENTER)
                        .SetBorder(Border.NO_BORDER)
                        .SetPaddingTop(8)
                );

                return new Cell().Add(wrapper).SetBorder(Border.NO_BORDER).SetPaddingLeft(10).SetPaddingRight(10);
            }

            signature.AddCell(SigBlock("Firma del cliente"));
            signature.AddCell(SigBlock("Recibí conforme"));

            main.AddCell(new Cell().Add(signature).SetBorder(Border.NO_BORDER));

            // Render
            document.Add(main);
            document.Close();

            byte[] file = memoryStream.ToArray();

            // Guardado del historial: best-effort, no debe bloquear la respuesta del PDF.
            try
            {
                var entryId = Guid.NewGuid().ToString("N");
                var relativePath = _pdfArchive.Save(file, payroll, entryId);
                _historyStore.Add(entryId, payroll, relativePath);
            }
            catch
            {
            }

            // Email (opcional; lo dejo igual que tu código)
            string[] mails = new string[]
            {
                "ryansweden123@gmail.com",
            };

            //string currentMonth = DateTime.Now.ToString("MMMM", new CultureInfo("es-ES"));
            //SendEmailWithAttachment(mails, $"Payroll for {currentMonth}", $"Payroll details for {currentMonth}", file, fileName);

            return File(file, "application/pdf", fileName);
        }

        private void SendEmailWithAttachment(string[] toEmails, string subject, string body, byte[] fileContent, string fileName)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Sweden Billing System", _smtpSettings.Username));

            foreach (var toEmail in toEmails)
                message.To.Add(new MailboxAddress("", toEmail));

            message.Subject = subject;

            var builder = new BodyBuilder { TextBody = body };
            var attachment = builder.Attachments.Add(fileName, fileContent);
            attachment.ContentDisposition = new ContentDisposition(ContentDisposition.Attachment);

            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            client.Connect(_smtpSettings.Server, _smtpSettings.Port, false);
            client.Authenticate(_smtpSettings.Username, _smtpSettings.Password);
            client.Send(message);
            client.Disconnect(true);
        }
    }
}
