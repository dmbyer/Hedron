namespace Bot.Models
{
    public class BotConfig
    {
        public Tokens Tokens { get; set; }
        public string Prefix { get; set; }
    }

    public class Tokens
    {
        public string Discord { get; set; }
    }
}
