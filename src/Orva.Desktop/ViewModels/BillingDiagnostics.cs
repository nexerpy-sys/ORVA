using System;
using System.Diagnostics;

namespace Orva.ViewModels
{
    public static class BillingDiagnostics
    {
        public static void Log(string operation, Exception exception)
        {
            if (exception == null) return;

            // Local diagnostic trace only. Exception details are never shown to shop staff.
            Trace.TraceError(
                "ORVA billing operation failed. Operation={0}; ErrorType={1}; Message={2}",
                operation ?? "Unknown",
                exception.GetType().FullName,
                exception.Message);
        }
    }
}