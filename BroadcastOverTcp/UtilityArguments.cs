namespace BroadcastOverTcp
{
    public class UtilityArguments : InputArguments
    {
        public ushort? Port => this.GetUnsignedInt16("p");

        public string IpAddress => this.GetString("a");

        public string FilePath => this.GetString("f");

        public uint? Delay => this.GetUnsignedInt32("d");

        public bool Repeat => this.GetBoolean("r");

        public bool IncludeLineBreak => this.GetBoolean("i");

        public string SslCertificateName => this.GetString("s");

        public UtilityArguments(string[] args)
            : base(args)
        {
        }

        private ushort? GetUnsignedInt16(string key)
        {
            string adjustedKey;
            if (this.ContainsKey(key, out adjustedKey))
            {
                ushort res;
                ushort.TryParse(this.ParsedArguments[adjustedKey], out res);
                return res;
            }
            return null;
        }

        private uint? GetUnsignedInt32(string key)
        {
            string adjustedKey;
            if (this.ContainsKey(key, out adjustedKey))
            {
                uint res;
                uint.TryParse(this.ParsedArguments[adjustedKey], out res);
                return res;
            }
            return null;
        }

        private string GetString(string key)
        {
            string adjustedKey;
            return this.ContainsKey(key, out adjustedKey)
                ? this.ParsedArguments[adjustedKey]
                : null;
        }

        private bool GetBoolean(string key)
        {
            return this.Contains(key);
        }
    }
}