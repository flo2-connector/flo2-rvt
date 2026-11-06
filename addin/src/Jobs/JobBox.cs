// A registry of the jobs the add-in can run, and the one place a model is opened, made
// and closed. A job never opens, makes or closes the document itself, and the input model is
// never saved: it is closed without saving whatever the job did. The edit job writes its
// result as a NEW file, through its one save path (Edit/SaveResult.cs), before the box
// closes the input.
//
// ⭐ SINCE ADD-IN 0.13.0 THE BOX ALSO MAKES A NEW MODEL, for the new-model job (new-model.json in
// the working folder; Edit/Logic/NewModel.cs): Application.NewProjectDocument(UnitSystem) for
// Revit's own project with no template, or NewProjectDocument(path) from template.rte, a plain
// input of the Activity that the engine never opened (no /i; Autodesk's APS blog, 2019-03-20, found
// a template handed over as /i locked by Design Automation, so that NewProjectDocument threw). Then
// the job fills it and saves it through the one save path, as an edit's result is, and the box
// closes it without saving. A model Revit will not make is not a failure of the WorkItem: the job
// still writes report.json, saying why (scripts/addin-save-guard.ts holds NewProjectDocument to this
// file, and every Close here to Close(false)).
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
using System.Security.Cryptography;
using Autodesk.Revit.DB;
using DesignAutomationFramework;
using Flo2Rvt.Addin.Edit;
using Flo2Rvt.Addin.Edit.Logic;

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
        /// <summary>Since add-in 0.13.0, the new-model job's: what the box made the model from, and whether Revit made it (Document is null when it did not). Null for every other job.</summary>
        public NewModelInfo NewModel { get; internal set; }
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
            bool hasChangeList = File.Exists(Path.Combine(folder, JobSelection.ChangeListFileName));
            bool hasNewModel = File.Exists(Path.Combine(folder, JobSelection.NewModelFileName));
            return Execute(JobSelection.For(hasChangeList, hasNewModel));
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
            if (jobName == JobSelection.NewModel) return ExecuteNew(factory, folder);
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

        /// <summary>
        /// Since add-in 0.13.0: make the new model new-model.json asks for, from Revit's own project
        /// with no template in its units, or from template.rte; run the job on it; close it without
        /// saving (the job saved what it kept as model.rvt). When Revit makes no model the job still
        /// runs, with no document, and writes report.json saying why.
        /// </summary>
        private bool ExecuteNew(Func<IFlo2RvtJob> factory, string folder)
        {
            using (var heartbeat = new Heartbeat())
            {
                heartbeat.Step("reading what the new model starts from");
                var made = new NewModelInfo();
                string error;
                NewModelSpec spec = NewModel.Read(File.ReadAllText(Path.Combine(folder, NewModel.FileName)), out error);
                Document doc = null;
                var watch = Stopwatch.StartNew();
                if (spec != null)
                {
                    made.From = spec.From;
                    made.Units = spec.Units;
                    try
                    {
                        if (spec.From == NewModel.FromTemplate)
                        {
                            string template = Path.Combine(folder, NewModel.TemplateFileName);
                            made.Template = new TemplateInfo { File = spec.TemplateFile };
                            if (!File.Exists(template)) error = "the template " + spec.TemplateFile + " is not in the working folder (" + NewModel.TemplateFileName + ")";
                            else
                            {
                                byte[] bytes = File.ReadAllBytes(template);
                                made.Template.Bytes = bytes.LongLength;
                                using (var sha = SHA256.Create()) made.Template.Sha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                                BasicFileInfo info = null;
                                try
                                {
                                    info = BasicFileInfo.Extract(template);
                                    made.Template.Format = info.Format;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("[flo2-rvt] the template's header could not be read: " + ex.GetType().Name);
                                }
                                if (info != null && info.IsSavedInLaterVersion) error = "the template " + spec.TemplateFile + " was saved by a later Revit (" + info.Format + ") than this engine's, Revit 2024";
                                else
                                {
                                    heartbeat.Step("making the new model from the template");
                                    doc = data.RevitApp.NewProjectDocument(template);
                                }
                            }
                        }
                        else
                        {
                            heartbeat.Step("making the new model from Revit's own project, " + spec.Units);
                            doc = data.RevitApp.NewProjectDocument(spec.Units == NewModel.Metric ? UnitSystem.Metric : UnitSystem.Imperial);
                        }
                        if (doc == null && error == null) error = "Revit made no project document";
                    }
                    catch (Exception ex)
                    {
                        doc = null;
                        error = "Revit could not make the project" + (spec.From == NewModel.FromTemplate ? " from the template " + spec.TemplateFile : " with no template") + ": " + ex.GetType().Name + ": " + ex.Message;
                    }
                }
                made.Created = doc != null;
                made.Error = error;
                made.Seconds = Math.Round(watch.Elapsed.TotalSeconds, 1);
                Console.WriteLine("[flo2-rvt] " + (doc != null ? "new model made in " + made.Seconds + " s" : "no new model: " + error));
                try
                {
                    return factory().Execute(new JobContext(data, doc, null, false, folder, heartbeat) { NewModel = made });
                }
                finally
                {
                    if (doc != null)
                    {
                        // What the job kept is saved already, as model.rvt; nothing else of it is kept.
                        doc.Close(false);
                        Console.WriteLine("[flo2-rvt] new model closed without saving again");
                    }
                }
            }
        }
    }
}
