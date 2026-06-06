namespace Hedron.Core.Output
{
    public static class CategoryFlushPolicy
    {
        public static FlushPolicy GetPolicy(OutputCategory category) =>
            category is OutputCategory.Chat or OutputCategory.Notification
                ? FlushPolicy.Immediate
                : FlushPolicy.Batched;
    }
}
