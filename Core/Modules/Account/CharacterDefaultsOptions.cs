namespace Hedron.Core.Modules.Account
{
    public sealed class CharacterDefaultsOptions
    {
        public int AttributeDefault { get; set; } = 10;
        public int MaxHp { get; set; } = 100;
        public int MaxMana { get; set; } = 50;
        public int MaxStamina { get; set; } = 50;
        public int MaxAstra { get; set; } = 10;
        public string[] StartingAbilities { get; set; } = ["kick", "empower"];
    }
}
