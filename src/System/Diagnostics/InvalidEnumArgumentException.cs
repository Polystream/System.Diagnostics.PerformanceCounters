using System.Globalization;
using PerformanceCounters;

namespace System.Diagnostics
{
    public class InvalidEnumArgumentException : ArgumentException
    {
        public InvalidEnumArgumentException()
            : this(null)
        { }

        public InvalidEnumArgumentException(string message)
            : base(message)
        { }

        public InvalidEnumArgumentException(string message, Exception innerException)
            : base(message, innerException)
        { }

        public InvalidEnumArgumentException(string argumentName, int invalidValue, Type enumClass)
            : base(SR.GetString("The value of argument '{0}' ({1}) is invalid for Enum type '{2}'.", argumentName, invalidValue.ToString(CultureInfo.CurrentCulture), enumClass.Name), argumentName)
        { }
    }
}