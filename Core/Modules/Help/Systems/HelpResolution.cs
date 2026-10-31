using System.Collections.Generic;
using Hedron.Core.Commands;
using Hedron.Core.Modules.Abilities;

namespace Hedron.Core.Modules.Help.Systems
{
    /// <summary>
    /// Plain command metadata for help display — the lookup result never carries an
    /// <see cref="ICommand"/>, so nothing above the system layer leaks into it.
    /// </summary>
    public sealed record VerbHelp(
        string Name,
        string ShortDescription,
        string LongDescription,
        string Usage,
        CommandCategory Category,
        IReadOnlyList<string> Aliases)
    {
        public static VerbHelp From(ICommand command) => new(
            command.Name, command.ShortDescription, command.LongDescription, command.Usage, command.Category, command.Aliases);
    }

    /// <summary>The ability catalog appended after <c>help skills</c>/<c>spells</c>/<c>abilities</c>; null kind = all.</summary>
    public sealed record HelpAbilityCatalog(AbilityKind? Kind);

    /// <summary>Which family an ambiguous prefix matched (verbs and topics never share one prompt).</summary>
    public enum HelpMatchFamily
    {
        Verb,
        Topic,
    }

    /// <summary>The bare-<c>help</c> index: visible verbs (category order) and every topic (id order).</summary>
    public sealed record HelpIndex(IReadOnlyList<VerbHelp> Verbs, IReadOnlyList<HelpTopic> Topics);

    /// <summary>What a <c>help</c> query resolved to (R5/R6).</summary>
    public abstract record HelpResolution
    {
        private HelpResolution() { }

        /// <summary>Nothing to look up: show the index.</summary>
        public sealed record Index : HelpResolution;

        public sealed record Verb(VerbHelp Help, HelpAbilityCatalog? Catalog) : HelpResolution;

        public sealed record Topic(HelpTopic Help) : HelpResolution;

        public sealed record Ability(AbilityDefinition Definition) : HelpResolution;

        public sealed record Ambiguous(HelpMatchFamily Family, IReadOnlyList<string> Candidates) : HelpResolution;

        public sealed record NotFound : HelpResolution;
    }
}
