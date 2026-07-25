using SwedenBttnBit.Domain;
using System.Globalization;

namespace SwedenBttnBit.Services
{
    public interface IGuidePdfArchive
    {
        string Save(byte[] pdfBytes, PayRoll payroll, string id);
        string GetFullPath(string relativePath);
    }

    public class GuidePdfArchive : IGuidePdfArchive
    {
        private readonly string _rootPath;

        public GuidePdfArchive(IWebHostEnvironment env)
        {
            _rootPath = Path.Combine(env.ContentRootPath, "App_Data", "Guides");
        }

        public string Save(byte[] pdfBytes, PayRoll payroll, string id)
        {
            var year = payroll.Date.ToString("yyyy", CultureInfo.InvariantCulture);
            var month = payroll.Date.ToString("MM", CultureInfo.InvariantCulture);
            var folder = Path.Combine(_rootPath, year, month);
            Directory.CreateDirectory(folder);

            var fileName = SanitizeFileName(
                $"{payroll.GuideNumber}_{payroll.Date:ddMMyyyy}_{payroll.Destinatary}_{id}.pdf"
                    .Replace(" ", "_"));

            File.WriteAllBytes(Path.Combine(folder, fileName), pdfBytes);

            return Path.Combine(year, month, fileName);
        }

        public string GetFullPath(string relativePath) => Path.Combine(_rootPath, relativePath);

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
