using ModernWpf.Controls;
using System;

namespace PalworldServerManager.Controls
{
    class NumberBoxFormatter : INumberBoxNumberFormatter
    {
        public string FormatDouble(double value)
        {
            return value.ToString("F");
        }

        public double? ParseDouble(string text)
        {
            if (double.TryParse(text, out double result))
            {
                return result;
            }
            return null;
        }

    }
}
