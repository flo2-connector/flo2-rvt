// A registry of the jobs the add-in can run, and the one place a model is opened
// and closed. A job never opens or closes the document itself, and the input model is
// never saved: it is closed without saving whatever the job did. The edit job writes its
// result as a NEW file, through its one save path (Edit/SaveResult.cs), before the box
// closes the input.
//
// Adapted from Autodesk's registered-tool pattern in aps-sample-revit-mcp-tools-bundle
// (github.com/autodesk-platform-services/aps-sample-revit-mcp-tools-bundle, commit
// 13d02b1464d0a22839fff9d3ce41b84180268c1a: Utils/IRevitMcpTool.cs, RevitMcpToolAttribute.cs,
// RevitMcpToolBox.cs, RevitMcpToolBoxBuilder.cs). Copyright (c) 2026 Autodesk, Inc.
// MIT License; see THIRD_PARTY_NOTICES.md. Changes: C# 7.3 for .NET Framework 4.8; the model is
// a local .rvt in the working folder, not a cloud model; workshared files are opened detached;
// the job is chosen by the files the Activity handed over (Edit/Logic/JobSelection.cs); and the
// input document is always closed without saving.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Autodesk.Revit.DB;
using DesignAutomationFramework;

namespace Flo2Rvt.Addin.Jobs
{
    /// <summary>Names a job class, as the Activity or a later job file refers to it.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class Flo2RvtJobAttribute : Attribute
    {
        public Flo2RvtJobAttribute(string name)
        {
            if (name == null) throw new ArgumentNullException("name");
            Name = name;
        }

        public string Name { get; private set; }
    }

    /// <summary>What a job is given: the opened model and where to write its outputs.</summary>
    public sealed class JobContext
    {
        public JobContext(DesignAutomationData data, Document document, BasicFileInfo fileInfo, bool openedDetached, string workingFolder, Heartbeat heartbeat)
        {
            Data = data;
            Document = document;
            FileInfo = fileInfo;
            OpenedDetached = openedDetached;
            WorkingFolder = workingFolder;
            Heartbeat = heartbeat;
        }

        public DesignAutomationData Data { get; private set; }
        public Document Document { get; private set; }
        public BasicFileInfo FileInfo { get; private set; }
        public bool OpenedDetached { get; private set; }
        /// <summary>The Automation Service allows writes only here and below.</summary>
        public string WorkingFolder { get; private set; }
        public Heartbeat Heartbeat { get; private set; }
    }

    public interface IFlo2RvtJob
    {
        /// <summary>Runs the job on the opened model. True when its required outputs were written.</summary>
        bool Execute(JobContext context);
    }

    public sealed class JobBoxBuilder
    {
        private readonly Dictionary<string, Func<IFlo2RvtJob>> registry = new Dictionary<string, Func<IFlo2RvtJob>>(StringComparer.OrdinalIgnoreCase);
        private readonly DesignAutomationData data;

        internal JobBoxBuilder(DesignAutomationData data)
        {
            if (data == null) throw new ArgumentNullException("data");
            this.data = data;
        }

        public JobBoxBuilder AddJob<TJob>() where TJob : IFlo2RvtJob, new()
        {
            var attribute = typeof(TJob).GetCustomAttribute<Flo2RvtJobAttribute>();
            if (attribute == null) throw new InvalidOperationException(typeof(TJob).Name + " has no [Flo2RvtJob] attribute.");
            registry[attribute.Name] = () => new TJob();
            Console.WriteLine("[flo2-rvt] registered job: " + attribute.Name);
            return this;
        }

        public JobBox Build()
        {
            return new JobBox(registry, data);
        }
    }

    public sealed class JobBox
    {
        /// <summary>The Activity's input parameter rvtFile arrives under this name (its localName).</summary>
        public const string InputFileName = "input.rvt";

        private readonly Dictionary<string, Func<IFlo2RvtJob>> registry;
        private readonly DesignAutomationData data;

        internal JobBox(Dictionary<string, Func<IFlo2RvtJob>> registry, DesignAutomationData data)
        {
            this.registry = registry;
            this.data = data;
        }

        public static JobBoxBuilder CreateBuilder(DesignAutomationData data)
        {
            return new JobBoxBuilder(data);
        }

        /// <summary>Run the job the working folder's files call for (Edit/Logic/JobSelection.cs).</summary>
        public bool ExecuteForWorkingFolder()
        {
            string folder = Directory.GetCurrentDirectory();
            bool hasChangeList = File.Exists(Path.Combine(folder, Flo2Rvt.Addin.Edit.Logic.JobSelection.ChangeListFileName));
            return Execute(Flo2Rvt.Addin.Edit.Logic.JobSelection.For(hasChangeList));
        }

        public bool Execute(string jobName)
        {
            Func<IFlo2RvtJob> factory;
            if (!registry.TryGetValue(jobName, out factory))
            {
                Console.WriteLine("[flo2-rvt] unknown job: " + jobName + "; known: " + string.Join(", ", registry.Keys));
                return false;
            }
            Console.WriteLine("[flo2-rvt] job: " + jobName);

            string folder = Directory.GetCurrentDirectory();
            string input = Path.Combine(folder, InputFileName);
            if (!File.Exists(input))
            {
                Console.WriteLine("[flo2-rvt] the input model is missing from the working folder");
                return false;
            }

            using (var heartbeat = new Heartbeat())
            {
                heartbeat.Step("reading the file header");
                BasicFileInfo info = BasicFileInfo.Extract(input);
                var options = new OpenOptions { Audit = false };
                bool detached = false;
                if (info.IsWorkshared)
                {
                    // A workshared file's central is out of reach here; detach, keep the worksets, open them all.
                    options.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
                    options.SetOpenWorksetsConfiguration(new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
                    detached = true;
                }

                heartbeat.Step("opening the model");
                var watch = Stopwatch.StartNew();
                Document doc = data.RevitApp.OpenDocumentFile(ModelPathUtils.ConvertUserVisiblePathToModelPath(input), options);
                if (doc == null)
                {
                    Console.WriteLine("[flo2-rvt] Revit could not open the model");
                    return false;
                }
                Console.WriteLine("[flo2-rvt] opened in " + watch.Elapsed.TotalSeconds.ToString("F1") + " s" + (detached ? " (detached from central)" : ""));

                try
                {
                    return factory().Execute(new JobContext(data, doc, info, detached, folder, heartbeat));
                }
                finally
                {
                    // The input is never saved: closed and discarded whatever the job did. (The
                    // edit job's result, if any, is already written as model.rvt.)
                    doc.Close(false);
                    Console.WriteLine("[flo2-rvt] input model closed without saving");
                }
            }
        }
    }
}
