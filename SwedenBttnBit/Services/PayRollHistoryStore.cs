using SwedenBttnBit.Domain;
using System.Text.Json;

namespace SwedenBttnBit.Services
{
    public interface IPayRollHistoryStore
    {
        IReadOnlyList<PayRollHistoryEntry> GetAll();
        PayRollHistoryEntry? GetById(string id);
        PayRollHistoryEntry Add(string id, PayRoll payload, string pdfRelativePath);
        bool MarkFinal(string id);
    }

    public class PayRollHistoryStore : IPayRollHistoryStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

        private readonly string _filePath;
        private readonly object _lock = new();

        public PayRollHistoryStore(IWebHostEnvironment env)
        {
            var dataDir = Path.Combine(env.ContentRootPath, "App_Data");
            Directory.CreateDirectory(dataDir);
            _filePath = Path.Combine(dataDir, "payroll-history.json");
        }

        public IReadOnlyList<PayRollHistoryEntry> GetAll()
        {
            lock (_lock)
            {
                return Load().OrderByDescending(e => e.CreatedAt).ToList();
            }
        }

        public PayRollHistoryEntry? GetById(string id)
        {
            lock (_lock)
            {
                return Load().FirstOrDefault(e => e.Id == id);
            }
        }

        public PayRollHistoryEntry Add(string id, PayRoll payload, string pdfRelativePath)
        {
            lock (_lock)
            {
                var entries = Load();
                var entry = new PayRollHistoryEntry
                {
                    Id = id,
                    GuideNumber = payload.GuideNumber ?? string.Empty,
                    CreatedAt = DateTime.Now,
                    IsFinal = false,
                    PdfRelativePath = pdfRelativePath,
                    Payload = payload
                };
                entries.Add(entry);
                Save(entries);
                return entry;
            }
        }

        public bool MarkFinal(string id)
        {
            lock (_lock)
            {
                var entries = Load();
                var target = entries.FirstOrDefault(e => e.Id == id);
                if (target == null) return false;

                foreach (var entry in entries.Where(e => e.GuideNumber == target.GuideNumber))
                    entry.IsFinal = entry.Id == id;

                Save(entries);
                return true;
            }
        }

        private List<PayRollHistoryEntry> Load()
        {
            if (!File.Exists(_filePath)) return new List<PayRollHistoryEntry>();

            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json)) return new List<PayRollHistoryEntry>();

            return JsonSerializer.Deserialize<List<PayRollHistoryEntry>>(json, SerializerOptions)
                   ?? new List<PayRollHistoryEntry>();
        }

        private void Save(List<PayRollHistoryEntry> entries)
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(entries, SerializerOptions));
        }
    }
}
