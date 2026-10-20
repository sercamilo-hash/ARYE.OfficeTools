using System.Collections.Generic;
using Autodesk.Revit.DB;
using ARYE.OfficeTools.Core.Results;

namespace ARYE.OfficeTools.Services
{
    /// <summary>
    /// Failure preprocessor for transactions committed while a BauTools modal
    /// window is open. Revit cannot show its own failure dialog behind a modal
    /// WPF window: the Commit then stays pending, the transaction never closes,
    /// and when the command ends Revit discards every change with
    /// "A transaction or sub-transaction was opened but not closed".
    ///
    /// Warnings are resolved by deletion and REPORTED into the result (never
    /// swallowed). Errors are recorded and the transaction is rolled back with
    /// the real message, so the user sees why nothing was drawn.
    /// </summary>
    internal sealed class RevitFailureCollector : IFailuresPreprocessor
    {
        private readonly OperationResult<string> _result;
        private readonly string _context;

        public List<string> Warnings { get; } = new List<string>();

        public List<string> Errors { get; } = new List<string>();

        public RevitFailureCollector(OperationResult<string> result, string context)
        {
            _result = result;
            _context = context;
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            bool hasError = false;
            foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
            {
                string text = failure.GetDescriptionText();
                int count = failure.GetFailingElementIds().Count;
                string label = count > 0 ? $"{text} ({count} element(s))" : text;

                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    Warnings.Add(label);
                    _result.Skipped.Add(new OperationSkip($"Revit warning during {_context}", label));
                    accessor.DeleteWarning(failure);
                }
                else
                {
                    hasError = true;
                    Errors.Add(label);
                    _result.Failed.Add(new OperationFailure($"Revit error during {_context}", label + " — transaction rolled back"));
                }
            }
            return hasError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }

        /// <summary>Wires this collector into a transaction. Call after tx.Start().</summary>
        public void Attach(Transaction tx)
        {
            FailureHandlingOptions options = tx.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(this);
            options.SetClearAfterRollback(true);
            options.SetForcedModalHandling(true);
            tx.SetFailureHandlingOptions(options);
        }
    }
}
