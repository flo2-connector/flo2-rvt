// Which job the add-in runs. One AppBundle carries every job; each Activity hands it
// different files, so the files say which job it is:
//
//   Flo2RvtProbe  inputs: input.rvt, save-families.json  -> probe (reads, never saves the model;
//                                                            since add-in 0.12.0 saves the families
//                                                            named out of it, as new .rfa files)
//   Flo2RvtEdit   inputs: input.rvt, changes.json        -> edit  (saves a NEW model)
//   Flo2RvtNew    inputs: new-model.json, changes.json   -> new   (since add-in 0.13.0: makes a NEW
//                 (template.rte, family<n>.rfa)                     model, from Revit's own project
//                                                                   or a template, fills it with the
//                                                                   list and saves it; no input.rvt)
//
// The probe Activity has no parameter that could put a changes.json in the working folder,
// and the edit Activity's changes parameter is required (Design Automation fails the
// WorkItem before Revit starts when it cannot fetch it), so a probe never edits and an
// edit never quietly runs as a probe. Only the new-model Activity has a parameter that puts
// new-model.json there (required, as is its changes), and it has no model input; the edit
// Activity's model input stays required, so an edit never quietly runs as a new model.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

namespace Flo2Rvt.Addin.Edit.Logic
{
    public static class JobSelection
    {
        public const string Probe = "probe";
        public const string Edit = "edit";
        /// <summary>Since add-in 0.13.0: the new-model job, which makes the model it edits.</summary>
        public const string NewModel = "new";
        /// <summary>The edit Activity's input parameter `changes` arrives under this name (its localName).</summary>
        public const string ChangeListFileName = "changes.json";
        /// <summary>The new-model Activity's input parameter `newModel` arrives under this name (Logic/NewModel.cs).</summary>
        public const string NewModelFileName = Logic.NewModel.FileName;

        public static string For(bool changeListPresent)
        {
            return For(changeListPresent, false);
        }

        public static string For(bool changeListPresent, bool newModelPresent)
        {
            if (newModelPresent) return NewModel;
            return changeListPresent ? Edit : Probe;
        }
    }
}
