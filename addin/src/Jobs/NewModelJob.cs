// Since add-in 0.13.0, the new-model job: a NEW Revit model made and filled with its first change
// list in one job (flo2-rvt's dec:a-new-model-is-made-and-filled-in-one-job). The job box makes the
// model (Jobs/JobBox.cs ExecuteNew: Revit's own project with no template in the units new-model.json
// names, or the template beside it), and the edit job fills it exactly as it edits a model, through
// the same one commit path and one save path (Jobs/EditJob.cs, with the new model's context). The
// new-model Activity hands over new-model.json and no model, so the working folder says this job
// runs (Edit/Logic/JobSelection.cs).

namespace Flo2Rvt.Addin.Jobs
{
    [Flo2RvtJob(Name)]
    public sealed class NewModelJob : IFlo2RvtJob
    {
        public const string Name = Flo2Rvt.Addin.Edit.Logic.JobSelection.NewModel;

        public bool Execute(JobContext context)
        {
            return new EditJob().Execute(context);
        }
    }
}
