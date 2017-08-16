namespace PerformanceCounters
{
    public static class SR
    {
        public static string GetString(string format, params object[] args)
        {
            return string.Format(format, args);
        }
    }
}