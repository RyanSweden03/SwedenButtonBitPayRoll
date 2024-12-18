﻿using iText.IO.Font;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using SwedenBttnBit.Domain;
using System.IO;
using System.Net;
using System.Net.Mail;
using MimeKit.Utils;
using static iText.Kernel.Pdf.Colorspace.PdfDeviceCs;
using System.Globalization;


namespace SwedenBttnBit.Controllers
{
    public class PayRollController : Controller
    {
        private readonly SmtpSettings _smtpSettings;
        public PayRollController(IConfiguration configuration)
        {
            _smtpSettings = configuration.GetSection("SmtpSettings").Get<SmtpSettings>();
        }

        [HttpPost("get-payroll")]
        public IActionResult GeneratePDF([FromBody] PayRoll payroll)
        {
            PdfFont fontLato = PdfFontFactory.CreateFont("Fonts/Lato-Regular.ttf", PdfEncodings.IDENTITY_H);
            float generalFontSize = 11f;
            Table generalTable = new Table(UnitValue.CreatePercentArray(new float[] { 100f })).SetWidth(UnitValue.CreatePercentValue(100));
            Color blue = new DeviceRgb(70, 130, 169);
            Color darkBlue = new DeviceRgb(39, 55, 66);
            Color darkerWhite = new DeviceRgb(221, 230, 237);
            decimal sumTotal = 0;
            #region lambdas
            Func<string, int, int, TextAlignment, PdfFont, float, bool, Cell> getCellBasic = (text, colspan, rowspan, textAlignment, font, fontSize, borderOff) =>
            {
                Cell cell = new Cell(colspan, rowspan).Add(new Paragraph(text));
                cell.SetPadding(0);
                cell.SetMargin(0);
                cell.SetTextAlignment(textAlignment);
                cell.SetVerticalAlignment(VerticalAlignment.MIDDLE);
                cell.SetFont(font);
                cell.SetFontSize(fontSize);
                if (borderOff)
                {
                    cell.SetBorder(Border.NO_BORDER);

                }

                return cell;
            };

            Func<string, int, int, TextAlignment, PdfFont, float, bool, bool, Color, Color, Cell> getCell = (text, colspan, rowspan, textAlignment, font, fontSize, isXborder, isYborder, backgroundColor, fontColor) =>
            {
                Cell cell = new Cell(colspan, rowspan).Add(new Paragraph(text));
                cell.SetTextAlignment(textAlignment);
                cell.SetPadding(0);
                cell.SetFont(font);
                cell.SetFontSize(fontSize);
                cell.SetBackgroundColor(backgroundColor);
                cell.SetFontColor(fontColor);
                if (!isXborder)
                {
                    cell.SetBorderTop(Border.NO_BORDER);
                    cell.SetBorderBottom(Border.NO_BORDER);
                }
                if (!isYborder)
                {
                    cell.SetBorderLeft(Border.NO_BORDER);
                    cell.SetBorderRight(Border.NO_BORDER);
                }
                return cell;
            };

            Func<string, int, int, int, int, TextAlignment, PdfFont, float, bool, bool, Color, Color, Cell> getCellAdvanced = (text, colspan, rowspan, paddingX, paddingY, textAlignment, font, fontSize, isXborder, isYborder, backgroundColor, fontColor) =>
            {
                Cell cell = new Cell(colspan, rowspan).Add(new Paragraph(text));
                cell.SetTextAlignment(textAlignment);
                cell.SetFont(font);
                cell.SetFontSize(fontSize);
                cell.SetBackgroundColor(backgroundColor);
                cell.SetFontColor(fontColor);
                cell.SetPaddingRight(paddingX);
                cell.SetPaddingLeft(paddingX);
                cell.SetPaddingTop(paddingY);
                cell.SetPaddingBottom(paddingY);
                if (!isXborder)
                {
                    cell.SetBorderTop(Border.NO_BORDER);
                    cell.SetBorderBottom(Border.NO_BORDER);
                }
                if (!isYborder)
                {
                    cell.SetBorderLeft(Border.NO_BORDER);
                    cell.SetBorderRight(Border.NO_BORDER);
                }
                return cell;
            };

            Func<int, int> AddSpacer = (height) =>
            {
                Table Spacer = new Table(UnitValue.CreatePercentArray(new float[] { 100f })).SetWidth(UnitValue.CreatePercentValue(100));
                Spacer.AddCell(getCellBasic("", 1, 1, TextAlignment.LEFT, fontLato, generalFontSize, true).SetHeight(height).SetBorder(Border.NO_BORDER));
                generalTable.AddCell(new Cell(1, 1).Add(Spacer).SetBorder(Border.NO_BORDER));
                return height; // opcional: devolver un valor si es necesario
            };

            #endregion
            var day = payroll.Date.Day;
            var month = payroll.Date.Month;
            var year = payroll.Date.Year;
            var fullyear = day + "_" + month + "_" + year;
            string namefile = $"{payroll.GuideNumber.ToString().Trim()}_{fullyear.Trim()}{payroll.Destinatary?.Trim()}.pdf";
            namefile = namefile.Replace(" ", "_");



            MemoryStream memoryStream = new();
            PdfWriter pdfWriter = new(memoryStream);
            PdfDocument pdf = new(pdfWriter);
            Document document = new(pdf, PageSize.A4);
            document.SetMargins(30, 30, 30, 30);

            //Header
            Image SwedenLogo = new Image(iText.IO.Image.ImageDataFactory.Create("Images/LOGO.png")).SetWidth(250).SetHeight(100);

            Table companyNameAndLogo = new Table(UnitValue.CreatePercentArray(new float[] { 80f, 20f })).SetWidth(UnitValue.CreatePercentValue(100));
            companyNameAndLogo.AddCell(new Cell().Add(SwedenLogo).SetBorder(Border.NO_BORDER));

            companyNameAndLogo.AddCell(getCellBasic("Fecha: " + day + "/" + month + "/" + year, 1, 1, TextAlignment.LEFT, fontLato, generalFontSize, true));
            companyNameAndLogo.AddCell(getCellBasic("RUC : 20606064552", 1, 2, TextAlignment.LEFT, fontLato, 10, true).SetMarginLeft(50));
            companyNameAndLogo.AddCell(getCellBasic("Mayorazgo 4 etapa, Av. Asturias\r\nAte – Vitarte", 1, 2, TextAlignment.LEFT, fontLato, 10, true).SetMarginLeft(50));


            generalTable.AddCell(new Cell(1, 1).Add(companyNameAndLogo).SetBorder(Border.NO_BORDER));
            AddSpacer(15);


            //Body
            Table bodyTable = new Table(UnitValue.CreatePercentArray(new float[] { 100f })).SetWidth(UnitValue.CreatePercentValue(100));
            bodyTable.AddCell(getCellBasic("Empresa destinataria", 1, 1, TextAlignment.LEFT, fontLato, generalFontSize, false).SetBold().SetPaddingLeft(10).SetPadding(4).SetBackgroundColor(blue));
            bodyTable.AddCell(getCellBasic("Razón social: " + payroll.Destinatary, 1, 1, TextAlignment.LEFT, fontLato, generalFontSize, false).SetPaddingLeft(10).SetPadding(7).SetBackgroundColor(darkerWhite));
            bodyTable.AddCell(getCellBasic("R.U.C. Nº: " + payroll.DestinataryRUC, 1, 1, TextAlignment.LEFT, fontLato, generalFontSize, false).SetPaddingLeft(10).SetPadding(7).SetBackgroundColor(darkerWhite));
            bodyTable.AddCell(getCellBasic("Dirección: " + payroll.DestinataryAddress, 1, 1, TextAlignment.LEFT, fontLato, generalFontSize, false).SetPaddingLeft(10).SetPadding(7).SetBackgroundColor(darkerWhite));
            bodyTable.AddCell(getCellBasic("Dirección: " + payroll.DestinataryDistrict, 1, 1, TextAlignment.LEFT, fontLato, generalFontSize, false).SetPaddingLeft(10).SetPadding(7).SetBackgroundColor(darkerWhite));
            bodyTable.AddCell(getCellBasic("Guía N°: " + payroll.GuideNumber, 1, 1, TextAlignment.LEFT, fontLato, generalFontSize, false).SetPaddingLeft(10).SetPadding(7).SetBackgroundColor(darkerWhite));

            generalTable.AddCell(new Cell(1, 1).Add(bodyTable).SetBorder(Border.NO_BORDER)).SetBorder(Border.NO_BORDER);
            if (payroll.Products?.Count >= 5)
            {
                AddSpacer(5);
            }
            else
            {
                AddSpacer(10);
            }


            Table products = new Table(UnitValue.CreatePercentArray(new float[] { 15f, 15f, 15f, 55f })).SetWidth(UnitValue.CreatePercentValue(100));
            products.AddCell(getCellBasic("Precio", 1, 1, TextAlignment.CENTER, fontLato, generalFontSize, false).SetBold().SetBackgroundColor(blue).SetFontColor(darkBlue));
            products.AddCell(getCellBasic("Cantidad", 1, 1, TextAlignment.CENTER, fontLato, generalFontSize, false).SetBold().SetBackgroundColor(blue).SetFontColor(darkBlue));
            products.AddCell(getCellBasic("Total", 1, 1, TextAlignment.CENTER, fontLato, generalFontSize, false).SetBold().SetBackgroundColor(blue).SetFontColor(darkBlue));
            products.AddCell(getCellBasic("Descripción", 1, 1, TextAlignment.LEFT, fontLato, generalFontSize, false).SetBold().SetPaddingLeft(10).SetBackgroundColor(blue).SetFontColor(darkBlue));

            if (payroll.Products?.Count > 0 && payroll != null)
            {


                for (int i = 0; i < payroll.Products.Count; i++)
                {
                    Color backgroundproduct = new DeviceRgb(246, 244, 235);
                    if (i % 2 == 0)
                    {
                        backgroundproduct = new DeviceRgb(221, 230, 237);
                    }

                    var total_price = payroll.Products[i].Price * payroll.Products[i].Quantity;
                    sumTotal += total_price;
                    products.AddCell(getCellBasic("$" + payroll.Products[i].Price.ToString(), 1, 1, TextAlignment.CENTER, fontLato, 10, false).SetBackgroundColor(backgroundproduct));
                    products.AddCell(getCellBasic(payroll.Products[i].Quantity.ToString(), 1, 1, TextAlignment.CENTER, fontLato, 10, false).SetBackgroundColor(backgroundproduct));
                    products.AddCell(getCellBasic("$" + total_price.ToString(), 1, 1, TextAlignment.CENTER, fontLato, 10, false).SetBackgroundColor(backgroundproduct));
                    if (payroll.Products[i].Description != null)
                    {
                        products.AddCell(getCellBasic(payroll.Products[i].Description!.ToString(), 1, 1, TextAlignment.LEFT, fontLato, 10, false).SetPaddingLeft(10).SetPaddingTop(4).SetPaddingBottom(4).SetBackgroundColor(backgroundproduct).SetPaddingRight(10));
                    }
                }
            }
            generalTable.AddCell(new Cell(1, 1).Add(products).SetBorder(Border.NO_BORDER)).SetBorder(Border.NO_BORDER);

            AddSpacer(5);
            Table totalTable = new Table(UnitValue.CreatePercentArray(new float[] { 80f, 20f })).SetWidth(UnitValue.CreatePercentValue(100));
            totalTable.AddCell(getCellBasic("Monto total: $" + sumTotal, 1, 1, TextAlignment.LEFT, fontLato, generalFontSize, false).SetPaddingLeft(10).SetBorder(Border.NO_BORDER));

            generalTable.AddCell(new Cell(1, 1).Add(totalTable).SetBorder(Border.NO_BORDER)).SetBorder(Border.NO_BORDER);
            if (payroll?.Products?.Count > 4)
            {
                AddSpacer(50);
            }
            else
            {
                AddSpacer(90);
            }


            Table signature = new Table(UnitValue.CreatePercentArray(new float[] { 50f, 50f })).SetWidth(UnitValue.CreatePercentValue(100));
            signature.AddCell(getCellBasic("Firma del cliente", 1, 1, TextAlignment.CENTER, fontLato, generalFontSize, false).SetPadding(10).SetBorder(Border.NO_BORDER));
            signature.AddCell(getCellBasic("Recibí conforme", 1, 1, TextAlignment.CENTER, fontLato, generalFontSize, false).SetPadding(10).SetBorder(Border.NO_BORDER));

            generalTable.AddCell(new Cell(1, 1).Add(signature).SetBorder(Border.NO_BORDER)).SetBorder(Border.NO_BORDER);


            document.Add(generalTable);
            document.Close();
            byte[] file = memoryStream.ToArray();


            string[] mails = new string[]
            {
                "ryansweden123@gmail.com",
                "jessy_fhon@hotmail.com"
            };


            string currentMonth = DateTime.Now.ToString("MMMM", new CultureInfo("es-ES"));
            SendEmailWithAttachment(mails, $"Payroll for {currentMonth}", $"Payroll details for {currentMonth}", file, namefile);


            return File(file, "application/pdf", namefile);

        }
        private void SendEmailWithAttachment(string[] toEmails, string subject, string body, byte[] fileContent, string fileName)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Sweden Billing System", _smtpSettings.Username));

            foreach (var toEmail in toEmails)
            {
                message.To.Add(new MailboxAddress("", toEmail));
            }

            message.Cc.Add(new MailboxAddress("", "jessy_fhon@hotmail.com"));

            message.Subject = subject;

            var builder = new BodyBuilder { TextBody = body };

            var attachment = builder.Attachments.Add(fileName, fileContent);
            attachment.ContentDisposition = new ContentDisposition(ContentDisposition.Attachment);

            message.Body = builder.ToMessageBody();

            using (var client = new MailKit.Net.Smtp.SmtpClient())
            {
                client.Connect(_smtpSettings.Server, _smtpSettings.Port, false);
                client.Authenticate(_smtpSettings.Username, _smtpSettings.Password);

                client.Send(message);
                client.Disconnect(true);
            }
        }

    }
}
