using System;
using System.Diagnostics;

namespace TestApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var categories = PerformanceCounterCategory.GetCategories();

            foreach (var counterCategory in categories)
            {
                
                try
                {
                    if (counterCategory.CategoryType == PerformanceCounterCategoryType.SingleInstance || counterCategory.CategoryType == PerformanceCounterCategoryType.Unknown)
                    {

                        var counters = counterCategory.GetCounters();

                        foreach (var performanceCounter in counters)
                        {
                            Console.WriteLine($"{counterCategory.CategoryName}\\{performanceCounter.CounterName} = {performanceCounter.NextValue()}");
                        }
                    }
                    else
                    {
                        var instances = counterCategory.GetInstanceNames();
                        foreach (var instance in instances)
                        {
                            var counters = counterCategory.GetCounters(instance);

                            foreach (var performanceCounter in counters)
                            {
                                Console.WriteLine($"{counterCategory.CategoryName}\\{performanceCounter.CounterName}\\{instance} = {performanceCounter.NextValue()}");
                            }
                        }
                    }
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }
    }
}
