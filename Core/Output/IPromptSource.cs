namespace Hedron.Core.Output
{
    public interface IPromptSource
    {
        PromptMessage? GetPrompt(uint playerEntityId);
    }
}
