namespace VanadiumAPI.DTO
{
    public class UpdatePanelDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Color { get; set; }
        public float? Gain { get; set; }
        public float? Offset { get; set; }
        public int? Multiplier { get; set; }
        public int? DisplayedType { get; set; }
    }
}
