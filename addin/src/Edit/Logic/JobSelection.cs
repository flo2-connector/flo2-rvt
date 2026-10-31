// Which job the add-in runs. One AppBundle carries every job; each Activity hands it
// different files, so the files say which job it is:
//
//   Flo2RvtProbe  inputs: input.rvt                 -> probe (reads, never saves)
//   Flo2RvtEdit   inputs: input.rvt, changes.json   -> edit  (saves a NEW model)
//
// The probe Activity has no parameter that could put a changes.json in the working folder,
// and the edit Activity's changes parameter is required (Design Automation fails the
// WorkItem before Revit starts when it cannot fetch it), so a probe never edits and an
// edit never quietly runs as a probe.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

namespace Flo2Rvt.Addin.Edit.Logic
{
    public static class JobSelection
    {
        public const string Probe = "probe";
        public const string Edit = "edit";
        /// <summary>The edit Activity's input parameter `changes` arrives under this name (its localName).</summary>
        public const string ChangeListFileName = "changes.json";

        public static string For(bool changeListPresent)
        {
            return changeListPresent ? Edit : Probe;
        }
    }
}
