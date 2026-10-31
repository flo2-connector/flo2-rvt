// The add-in's entry point on the Automation Service.
//
// Adapted from Autodesk's RevitMcpToolsHandler.cs in aps-sample-revit-mcp-tools-bundle
// (github.com/autodesk-platform-services/aps-sample-revit-mcp-tools-bundle, commit
// 13d02b1464d0a22839fff9d3ce41b84180268c1a) and ExportToPdfsApp in aps-export-revit-pdf
// (github.com/autodesk-platform-services/aps-export-revit-pdf, commit
// 4b62539284e404530c4fe2705fee45712c19d0d7, ExportToPdfsPlugin/ExportToPdfs.cs).
// Copyright (c) 2026 Autodesk, Inc. and Copyright (c) 2022 Autodesk Inc; both MIT License,
// see THIRD_PARTY_NOTICES.md. Changes: C# 7.3 for .NET Framework 4.8, flo2-rvt's job box
// in place of the MCP tool box, and the probe as the only registered job.

using System;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using DesignAutomationFramework;
using Flo2Rvt.Addin.Jobs;

namespace Flo2Rvt.Addin
{
    [Regeneration(RegenerationOption.Manual)]
    [Transaction(TransactionMode.Manual)]
    public class ProbeApp : IExternalDBApplication
    {
        public ExternalDBApplicationResult OnStartup(ControlledApplication application)
        {
            Console.WriteLine("[flo2-rvt] add-in loaded");
            DesignAutomationBridge.DesignAutomationReadyEvent += HandleDesignAutomationReadyEvent;
            return ExternalDBApplicationResult.Succeeded;
        }

        public ExternalDBApplicationResult OnShutdown(ControlledApplication application)
        {
            return ExternalDBApplicationResult.Succeeded;
        }

        private void HandleDesignAutomationReadyEvent(object sender, DesignAutomationReadyEventArgs e)
        {
            try
            {
                // v0 registers one job, the read-only probe. Edit jobs join here, one change kind at a time.
                e.Succeeded = JobBox.CreateBuilder(e.DesignAutomationData)
                    .AddJob<ProbeJob>()
                    .Build()
                    .Execute(ProbeJob.Name);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[flo2-rvt] the job failed: " + ex.GetType().Name + ": " + ex.Message);
                e.Succeeded = false;
            }
        }
    }
}
