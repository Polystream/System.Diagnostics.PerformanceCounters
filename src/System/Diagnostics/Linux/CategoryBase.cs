namespace System.Diagnostics.Linux
{
    internal abstract class CategoryBase
    {
        public abstract string CategoryName { get; }
        public abstract PerformanceCounterCategoryType Type { get; }

        public abstract CategorySample GetSample();
        public abstract string[] GetCounters();
        public abstract bool CounterExists(string counter);

        public virtual string GetHelp()
        {
            return string.Empty;
        }
        public virtual string GetHelp(string counter)
        {
            return string.Empty;
        }
    }
}
