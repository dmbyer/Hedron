namespace Hedron.Core.Output
{
    public static class CategoryFlushPolicy
    {
        public static FlushPolicy GetPolicy(OutputCategory category) =>
            category == OutputCategory.Chat ? FlushPolicy.Immediate : FlushPolicy.Batched;
    }
}
