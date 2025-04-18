#pragma warning disable 1591

using System.Text;

namespace Sanakan.Services
{
    public class MessageContent
    {
        public StringBuilder Text { get; private set; }
        public int Entries { get; private set; }
        public int Length { get; private set; }

        public MessageContent(int x = 0)
        {
            Text = new StringBuilder();
            Entries = x;
            Length = 0;
        }

        public override string ToString() => Text.ToString();

        public MessageContent Clear()
        {
            Text.Clear();
            Entries = 0;
            Length = 0;
            return this;
        }

        public MessageContent Append(string str)
        {
            Text.Append(str);
            Entries += 1;
            Length += str.Length;
            return this;
        }

        public MessageContent Append(MessageEntry str)
        {
            Text.Append(str);
            Entries += 1;
            Length += str.Length;
            return this;
        }
    }
}