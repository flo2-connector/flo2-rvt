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
//
// Since add-in 0.10.0 a change may refuse itself inside its transaction (ChangeRefused: a room
// placed where nothing encloses it), which rolls it back like an error, in its own words.
//
// Since add-in 0.15.0 a change may be made of PARTS (Part): a drawing set's views, each in a
// sub-transaction of the change's own, committed into it or rolled back alone when the Revit API
// refuses that part, so one view Revit will not make leaves the others standing and the report says
// why. A part commits nothing to the model on its own: Revit commits sub-transactions only "when the
// active transaction is committed" (SubTransaction.Commit, Revit 2024's API reference), so the
// change still stands or falls whole at its own commit, and failure processing judges it there.

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
        /// <summary>Since add-in 0.6.0: every element a constraint-in-the-way failure named, failing or additional, uncapped (Logic/ConstraintsInTheWay.cs keeps the constraints among them).</summary>
        public readonly HashSet<long> ConstraintInTheWayIds = new HashSet<long>();
    }

    /// <summary>A change that finds, inside its own transaction, that what it made is not what was asked (a room nothing encloses): it is rolled back, and the report says this, never "the Revit API refused it".</summary>
    public sealed class ChangeRefused : Exception
    {
        public ChangeRefused(string why) : base(why)
        {
        }
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
            Document doc = failures.GetDocument();
            foreach (FailureMessageAccessor m in failures.GetFailureMessages())
            {
                FailureSeverity severity = m.GetSeverity();
                bool warning = severity == FailureSeverity.Warning;
                string text = m.GetDescriptionText();
                Guid definition = m.GetFailureDefinitionId().Guid;
                bool groupInTheWay = definition == BuiltInFailures.GroupFailures.AtomViolationWhenMultiPlacedInstances.Guid;
                // "Constraints are not satisfied" (Logic/ConstraintsInTheWay.cs). Revit's own
                // resolution is reported, never taken (RevitOffers).
                bool constraintInTheWay = definition == BuiltInFailures.DimensionFailures.RegenConstraintUnsatisfied.Guid;
                string offers = null;
                try
                {
                    if (m.HasResolutions()) offers = m.GetDefaultResolutionCaption();
                }
                catch (Exception)
                {
                    offers = null;
                }
                if (!warning)
                {
                    error = true;
                    if (FirstError == null) FirstError = text;
                }
                List<long> named = m.GetFailingElementIds().Concat(m.GetAdditionalElementIds()).Select(id => id.Value).Distinct().ToList();
                if (groupInTheWay) foreach (long id in named) result.GroupInTheWayIds.Add(id);
                if (constraintInTheWay) foreach (long id in named) result.ConstraintInTheWayIds.Add(id);
                if (result.Messages.Count < MaxMessages)
                {
                    List<long> shown = named.Take(MaxIdsPerMessage).ToList();
                    result.Messages.Add(new RevitMessage
                    {
                        Severity = severity.ToString(),
                        Text = text,
                        Handled = warning ? "dismissed" : "rolled_back",
                        ElementIds = shown,
                        Kind = groupInTheWay ? RevitMessage.GroupInTheWay : constraintInTheWay ? RevitMessage.ConstraintInTheWay : null,
                        // What each named element is, so a report says it without a second job.
                        ElementCategories = shown.Select(id => CategoryOf(doc, id)).ToList(),
                        Definition = definition.ToString(),
                        RevitOffers = offers,
                    });
                }
                // Every error, uncapped (whether ungrouping or removing constraints could clear them all is judged on all of them); warnings within the cap.
                if (!warning || result.Failures.Count < MaxMessages) result.Failures.Add(new RevitFailure { Error = !warning, GroupInTheWay = groupInTheWay, ConstraintInTheWay = constraintInTheWay, Text = text });
                if (warning) failures.DeleteWarning(m);
            }
            return error ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }

        /// <summary>An element's category as Revit names it; "" when it has none or cannot be read.</summary>
        private static string CategoryOf(Document doc, long id)
        {
            try
            {
                Element e = doc != null ? doc.GetElement(new ElementId(id)) : null;
                return e != null && e.Category != null ? e.Category.Name : "";
            }
            catch (Exception)
            {
                return "";
            }
        }
    }

    public static class ChangeRunner
    {
        /// <summary>
        /// Since add-in 0.15.0: one part of a change, inside the change's own open transaction, in a
        /// sub-transaction of its own: committed into the change when it runs through, rolled back alone
        /// when it throws. Null when it ran through; otherwise why not (a ChangeRefused's own words, or the
        /// exception's type and message).
        /// </summary>
        public static string Part(Document doc, Action part)
        {
            using (var sub = new SubTransaction(doc))
            {
                sub.Start();
                try
                {
                    part();
                }
                catch (Exception ex)
                {
                    if (sub.HasStarted() && !sub.HasEnded()) sub.RollBack();
                    return ex is ChangeRefused ? ex.Message : ex.GetType().Name + ": " + ex.Message;
                }
                TransactionStatus status = sub.Commit();
                return status == TransactionStatus.Committed ? null : "Revit did not keep it (" + status + ")";
            }
        }

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
                        result.RolledBackBecause = ex is ChangeRefused ? ex.Message : "the Revit API refused it: " + ex.GetType().Name + ": " + ex.Message;
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
