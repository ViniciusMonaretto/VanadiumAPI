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
        /// <summary>Omit = unchanged; JSON <c>null</c> = remove all high-limit alarms; number = add a new high alarm.</summary>
        public AlarmThresholdPatch MaxAlarm { get; set; }
        /// <summary>Omit = unchanged; JSON <c>null</c> = remove all low-limit alarms; number = add a new low alarm.</summary>
        public AlarmThresholdPatch MinAlarm { get; set; }
    }
}
