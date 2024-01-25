namespace SwedenBttnBit.Domain
{
    public class PayRoll
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string? Destinatary { get; set; }
        public string? DestinataryAddress { get; set; }
        public string? DestinataryDistrict { get; set; }
        public long DestinataryRUC { get; set; }
        public int GuideNumber { get; set; }

        public List<Product>? Products { get; set; }

    }
}
