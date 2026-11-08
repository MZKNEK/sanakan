#pragma warning disable 1591

using System.Text;

namespace Sanakan.Services
{
    public class MessageContent
    {
        public StringBuilder Text { get; } = new StringBuilder();
        public int Elements { get; private set; }
        public int Length => Text.Length;

        public override string ToString() => Text.ToString();

        public MessageContent Clear()
        {
            Text.Clear();
            Elements = 0;
            return this;
        }

        public MessageContent Append(string str)
        {
            Text.Append(str);
            Elements += DiscordLimits.CountElements(str);
            return this;
        }
    }
}