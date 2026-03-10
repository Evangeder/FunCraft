namespace FunCraft.Data.Players
{
    public sealed class PlayerRecord
    {
        public Guid Uuid { get; set; }
        public string Username { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public float Yaw { get; set; }
        public float Pitch { get; set; }
        public DateTime LastSeen { get; set; }
    }
}