using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace WebJobs.Script.Analyzers
{
    internal class DiagnosticDescriptors
    {
        private static DiagnosticDescriptor Create(string id, string title, string messageFormat, string category, DiagnosticSeverity severity)
        {
            // TODO: needs different site/code
            string helpLink = $"https://aka.ms/azfw-rules?ruleid={id}"; 
            return new DiagnosticDescriptor(id, title, messageFormat, category, severity, isEnabledByDefault: true, helpLinkUri: helpLink);
        }

        public static DiagnosticDescriptor AsyncVoidDiscouraged { get; }
            = Create(id: "AZFX0001", title: "Async void discouraged", 
                messageFormat: "Async void can lead to unexpected behavior. Return Task instead.",
                category: Constants.DiagnosticsCategories.Usage,
                severity: DiagnosticSeverity.Warning); // TODO: Should this be warning or erro?
    }
}
