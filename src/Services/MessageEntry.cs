#pragma warning disable 1591

namespace Sanakan.Services
{
    public class MessageEntry
    {
        public string Text { get; private set; }
        public int Length { get; private set; }

        public MessageEntry(string str, int inc = 0)
        {
            Text = str;
            Length = str.Length + inc;
        }

        public override string ToString() => Text;

        public static MessageEntry operator +(MessageEntry a, MessageEntry b)
        {
            return new MessageEntry($"{a.Text}{b.Text}", a.Length + b.Length - a.Text.Length - b.Text.Length);
        }

        public static MessageEntry operator +(string a, MessageEntry b)
        {
            return new MessageEntry($"{a}{b.Text}", b.Length - b.Text.Length);
        }

        public static MessageEntry operator +(MessageEntry a, string b)
        {
            return new MessageEntry($"{a.Text}{b}", a.Length - a.Text.Length);
        }
    }
}