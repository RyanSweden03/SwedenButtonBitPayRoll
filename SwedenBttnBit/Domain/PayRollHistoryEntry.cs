namespace SwedenBttnBit.Domain
{
    public class PayRollHistoryEntry
    {
        public string Id { get; set; } = string.Empty;
        public string GuideNumber { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsFinal { get; set; }
        public string PdfRelativePath { get; set; } = string.Empty;
        public PayRoll Payload { get; set; } = new PayRoll();
    }
}
