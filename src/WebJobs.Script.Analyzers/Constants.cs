using System;
using System.Collections.Generic;
using System.Text;

namespace WebJobs.Script.Analyzers
{
    internal static class Constants
    {
        internal static class Types
        {
            public const string FunctionNameAttribute = "Microsoft.Azure.WebJobs.FunctionNameAttribute";
        }

        internal static class DiagnosticsCategories
        {
            public const string Usage = "Usage";    // TODO: What diagnostic categories should we have?
        }
    }
}
