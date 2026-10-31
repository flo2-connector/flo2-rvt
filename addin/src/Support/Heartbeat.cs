// Writes a line to the job log every 20 seconds while a long Revit call runs.
// Autodesk's Design Automation guidance (aps-da-acad-c3d-skill, references/da-guardrails.md,
// "Heartbeat") reports that a job silent for about a minute is stopped as idle. It is
// measured on AutoCAD there; on Revit it is cheap insurance, since opening a large model
// or exporting its IFC can take minutes with no output of its own.

using System;
using System.Diagnostics;
using System.Threading;

namespace Flo2Rvt.Addin
{
    public sealed class Heartbeat : IDisposable
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private readonly Timer timer;
        private volatile string step = "starting";

        public Heartbeat()
        {
            timer = new Timer(_ => Beat(), null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
        }

        /// <summary>Names what the job is doing now, and logs it.</summary>
        public void Step(string what)
        {
            step = what;
            Console.WriteLine("[flo2-rvt] " + what + " (" + watch.Elapsed.TotalSeconds.ToString("F0") + " s)");
        }

        private void Beat()
        {
            Console.WriteLine("[flo2-rvt] still working: " + step + " (" + watch.Elapsed.TotalSeconds.ToString("F0") + " s)");
        }

        public void Dispose()
        {
            timer.Dispose();
        }
    }
}
