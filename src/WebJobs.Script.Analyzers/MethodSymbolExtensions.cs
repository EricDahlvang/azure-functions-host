using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WebJobs.Script.Analyzers
{
    internal static class MethodSymbolExtensions
    {
        public static bool IsFunction(this IMethodSymbol symbol, SymbolAnalysisContext analysisContext)
        {
            var attributes = symbol.GetAttributes();

            if (attributes.IsEmpty)
            {
                return false;
            }

            var attributeType = analysisContext.Compilation.GetTypeByMetadataName(Constants.Types.FunctionNameAttribute);

            return attributes.Any(a => attributeType.IsAssignableFrom(a.AttributeClass, true));
        }
    }
}
