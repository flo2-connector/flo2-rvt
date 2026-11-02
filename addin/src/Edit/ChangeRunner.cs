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
//
// Since add-in 0.5.0 each failure is also recorded with whether it is a model group in the way
// (BuiltInFailures.GroupFailures.AtomViolationWhenMultiPlacedInstances), and every element it
// names, uncapped: a level move whose change says groups_in_the_way "ungroup" is tried again,
// in a new transaction of its own, with only the groups those elements are in ungrouped first
// (Logic/GroupsInTheWay.cs). Nothing is resolved here: an error still rolls the change back.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Flo2Rvt.Addin.Edit.Logic;

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
        /// <summary>Every error Revit raised, and the first MaxMessages warnings, as Logic/GroupsInTheWay.cs judges them.</summary>
        public readonly List<RevitFailure> Failures = new List<RevitFailure>();
        /// <summary>Every element a group-in-the-way failure named, failing or additional, uncapped.</summary>
        public readonly HashSet<long> GroupInTheWayIds = new HashSet<long>();
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
                bool groupInTheWay = m.GetFailureDefinitionId().Guid == BuiltInFailures.GroupFailures.AtomViolationWhenMultiPlacedInstances.Guid;
                if (!warning)
                {
                    error = true;
                    if (FirstError == null) FirstError = text;
                }
                List<long> named = m.GetFailingElementIds().Concat(m.GetAdditionalElementIds()).Select(id => id.Value).Distinct().ToList();
                if (groupInTheWay) foreach (long id in named) result.GroupInTheWayIds.Add(id);
                if (result.Messages.Count < MaxMessages)
                {
                    result.Messages.Add(new RevitMessage { Severity = severity.ToString(), Text = text, Handled = warning ? "dismissed" : "rolled_back", ElementIds = named.Take(MaxIdsPerMessage).ToList(), Kind = groupInTheWay ? RevitMessage.GroupInTheWay : null });
                }
                // Every error, uncapped (whether ungrouping could clear them all is judged on all of them); warnings within the cap.
                if (!warning || result.Failures.Count < MaxMessages) result.Failures.Add(new RevitFailure { Error = !warning, GroupInTheWay = groupInTheWay, Text = text });
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
