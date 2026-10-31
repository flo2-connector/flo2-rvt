// ⭐ THE ONE PLACE A TRANSACTION IS COMMITTED. Every change of an edit job runs here, alone,
// in its own transaction (scripts/addin-save-guard.ts fails the build if a .Commit( call
// appears in any other file of addin/src, or a save call appears here).
//
// Revit's failure processing is answered by RecordingPreprocessor, never by a person (the
// Automation Service has no one to ask): every message is recorded; a WARNING is dismissed
// and the change is kept; an ERROR (or worse) rolls the whole change back, with no default
// resolution chosen on anyone's behalf, since Revit's resolutions for errors are themselves
// edits ("delete the element", "unjoin") the change list did not ask for. An exception from
// the Revit API inside the change rolls it back the same way.
//
// What Revit did on its own is read from Application.DocumentChanged for this transaction:
// the elements it deleted, modified and added while regenerating.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;

namespace Flo2Rvt.Addin.Edit
{
    public sealed class RunResult
    {
        public bool Committed;
        /// <summary>Why it was rolled back: the first Revit error, or the exception.</summary>
        public string RolledBackBecause;
        public readonly List<RevitMessage> Messages = new List<RevitMessage>();
        public readonly HashSet<long> Deleted = new HashSet<long>();
        public readonly HashSet<long> Modified = new HashSet<long>();
        public readonly HashSet<long> Added = new HashSet<long>();
        public double Seconds;
    }

    internal sealed class RecordingPreprocessor : IFailuresPreprocessor
    {
        public const int MaxMessages = 200;
        public const int MaxIdsPerMessage = 50;
        private readonly RunResult result;
        public string FirstError;

        public RecordingPreprocessor(RunResult result)
        {
            this.result = result;
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failures)
        {
            bool error = false;
            foreach (FailureMessageAccessor m in failures.GetFailureMessages())
            {
                FailureSeverity severity = m.GetSeverity();
                bool warning = severity == FailureSeverity.Warning;
                string text = m.GetDescriptionText();
                if (!warning)
                {
                    error = true;
                    if (FirstError == null) FirstError = text;
                }
                if (result.Messages.Count < MaxMessages)
                {
                    var ids = m.GetFailingElementIds().Concat(m.GetAdditionalElementIds()).Select(id => id.Value).Distinct().Take(MaxIdsPerMessage).ToList();
                    result.Messages.Add(new RevitMessage { Severity = severity.ToString(), Text = text, Handled = warning ? "dismissed" : "rolled_back", ElementIds = ids });
                }
                if (warning) failures.DeleteWarning(m);
            }
            return error ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }

    public static class ChangeRunner
    {
        /// <summary>Run one change in its own transaction: committed whole, or rolled back whole.</summary>
        public static RunResult Run(Document doc, Application app, string name, Action<Document> change)
        {
            var result = new RunResult();
            var watch = Stopwatch.StartNew();
            var preprocessor = new RecordingPreprocessor(result);
            string txName = "flo2-rvt edit: " + name;
            EventHandler<DocumentChangedEventArgs> listen = (sender, args) =>
            {
                if (!args.GetDocument().Equals(doc) || !args.GetTransactionNames().Contains(txName)) return;
                foreach (ElementId id in args.GetDeletedElementIds()) result.Deleted.Add(id.Value);
                foreach (ElementId id in args.GetModifiedElementIds()) result.Modified.Add(id.Value);
                foreach (ElementId id in args.GetAddedElementIds()) result.Added.Add(id.Value);
            };
            app.DocumentChanged += listen;
            try
            {
                using (var tx = new Transaction(doc, txName))
                {
                    FailureHandlingOptions options = tx.GetFailureHandlingOptions();
                    options.SetFailuresPreprocessor(preprocessor);
                    options.SetClearAfterRollback(true);
                    options.SetForcedModalHandling(false);
                    tx.SetFailureHandlingOptions(options);
                    tx.Start();
                    try
                    {
                        change(doc);
                    }
                    catch (Exception ex)
                    {
                        if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                        result.RolledBackBecause = "the Revit API refused it: " + ex.GetType().Name + ": " + ex.Message;
                        return result;
                    }
                    TransactionStatus status = tx.Commit();
                    result.Committed = status == TransactionStatus.Committed;
                    if (!result.Committed)
                    {
                        result.RolledBackBecause = preprocessor.FirstError != null
                            ? "Revit raised an error: " + preprocessor.FirstError
                            : "Revit did not commit it (" + status + ")";
                    }
                }
            }
            finally
            {
                app.DocumentChanged -= listen;
                result.Seconds = Math.Round(watch.Elapsed.TotalSeconds, 1);
            }
            if (!result.Committed)
            {
                // A rolled-back change changed nothing, whatever the event said.
                result.Deleted.Clear();
                result.Modified.Clear();
                result.Added.Clear();
            }
            return result;
        }
    }
}
