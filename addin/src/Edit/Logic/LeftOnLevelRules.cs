// What a delete_elements_on_level left tied to its level, and why each was not taken.
//
// The edit job reads the model AFTER the change's transaction (Edit/Leftovers.cs) and hands
// every element still tied to the level here as a candidate: what in its Revit settings names
// the level, whether Revit's own IFC exporter's storey rule puts it in the level's storey (and
// how: Logic/Storeys.cs), and, for a model element, what the exporter does with it and what it
// is part of (Logic/ExportFilter.cs). This file says why it was left, and builds the report's
// left_on_level.
//
// ⭐ NOTHING HERE IS DELETED: an element with no Revit level is reported, never deleted by a
// guess. The change reads applied_in_part when left_on_level's `elements` holds any model
// element; the others (views of the level, its sketch planes, annotation) are listed and
// counted apart.
//
// ⭐ A PART IS ITS OWNER'S (since 0.4.0). The storey rule ties an element to the level's storey
// only when the exporter exports it as a product of its own. One it exports only with another
// element (a stair's sketch lines, a rail's path extension lines, a top rail, a curtain grid
// line: the 0.3.0 re-run of the first live edit counted 14 of them as left on Level 2, beside
// the 2 real leftovers, the wardrobes) belongs to that owner:
//   · its owner is still in the model: listed under it in parts_of_kept_elements, never counted
//     as left (part_of_kept_element);
//   · its owner is gone: an ORPHAN, left behind, listed and counted in elements (orphaned_part);
//   · Revit names no owner, and no Revit setting ties it: listed in not_exported, with the
//     exporter's reason, never counted as left.
// An element the exporter does not export for another reason (its Export to IFC is No, a
// secondary design option, a demolished phase) and that no Revit setting ties is listed in
// not_exported too. An element the rule cannot judge is taken as exported, and listed as left.
//
// PURE LOGIC: no Revit type here, so addin/tests runs it on any OS.

using System.Collections.Generic;
using System.Linq;

namespace Flo2Rvt.Addin.Edit.Logic
{
    /// <summary>One element still tied to the level after the change, as Edit/Leftovers.cs read it.</summary>
    public sealed class LeftCandidate
    {
        public ElementFacts Element;
        /// <summary>Why it is not a model element (LevelRules.NotModel), when it is not one.</summary>
        public string NotModelWhy;
        /// <summary>The group it is a member of; 0 when none.</summary>
        public long GroupId;
        /// <summary>The change asked Revit to delete it.</summary>
        public bool Targeted;
        /// <summary>The Revit settings that name the level: Element.LevelId, a level parameter's BuiltInParameter name, FamilyInstance.Host.</summary>
        public List<string> RevitSettings = new List<string>();
        /// <summary>How Revit's IFC exporter files it under the level's storey (a Storeys* constant); null when it files it elsewhere.</summary>
        public string StoreyBy;
        /// <summary>For StoreyNonStoreyLevel, the level it is on; for StoreyContainerParameter, the parameter's value.</summary>
        public string StoreyVia;
        /// <summary>For StoreyByHeight: how many storeys share the span its lowest point is in (storeys at one elevation).</summary>
        public int StoreysSharingSpan = 1;
        /// <summary>For a model element, what Revit says of it for the exporter's choice and what it is part of (Logic/ExportFilter.cs); null when not read, and it is then taken as exported on its own.</summary>
        public ExportFacts Export;
    }

    public static class LeftOnLevelRules
    {
        public const string TiedByIfcStorey = "ifc_storey";
        /// <summary>The exporter's storey rule puts it in the level's storey, but the exporter does not export it on its own (Logic/ExportFilter.cs).</summary>
        public const string TiedByStoreyRule = "storey_rule";

        // How the exporter files it under the level's storey.
        /// <summary>Its own level (the exporter's base-level choice) is this level.</summary>
        public const string StoreyBaseLevel = "base_level";
        /// <summary>Its level is not a building storey, so it shares this level's storey.</summary>
        public const string StoreyNonStoreyLevel = "non_storey_level";
        /// <summary>No level: its lowest point is in this storey's span.</summary>
        public const string StoreyByHeight = "height";
        /// <summary>No level and no height in any span: the lowest storey, which this is.</summary>
        public const string StoreyLowest = "lowest_storey";
        /// <summary>Its IfcSpatialContainer (or OverrideElementContainer) parameter names this level.</summary>
        public const string StoreyContainerParameter = "container_parameter";

        // Why it was not taken.
        public const string ReasonNotDeleted = "not_deleted";
        public const string ReasonNotAModelElement = "not_a_model_element";
        public const string ReasonGroupMember = "group_member";
        public const string ReasonSettingNotTaken = "setting_not_taken";
        public const string ReasonNoRevitLevel = "no_revit_level";
        public const string ReasonNonStoreyLevel = "non_storey_level";
        public const string ReasonContainerParameter = "container_parameter";
        /// <summary>A part of an element still in the model: listed under its owner, not counted as left.</summary>
        public const string ReasonPartOfKeptElement = "part_of_kept_element";
        /// <summary>A part of an element no longer in the model: left behind, counted as left.</summary>
        public const string ReasonOrphanedPart = "orphaned_part";

        // Where Collect lists a judged element (LeftElement.ListedUnder).
        public const string ListedLeft = "elements";
        public const string ListedPart = "parts_of_kept_elements";
        public const string ListedNotExported = "not_exported";

        private static string Describe(OwnerFacts o)
        {
            return (o.Element.RevitClass ?? "element") + " " + o.Element.Id + (string.IsNullOrEmpty(o.Element.Category) ? "" : " (" + o.Element.Category + ")");
        }

        /// <summary>Its reason code and why, in a sentence, and where Collect lists it (LeftElement.ListedUnder).</summary>
        public static LeftElement Judge(LeftCandidate c, string levelName)
        {
            var e = new LeftElement
            {
                ElementId = c.Element.Id,
                GlobalId = c.Element.GlobalId,
                Category = c.Element.Category,
                RevitClass = c.Element.RevitClass,
                ModelElement = c.Element.Model,
                TiedBy = c.RevitSettings.ToList(),
                ListedUnder = ListedLeft,
            };
            ExportVerdict export = c.Element.Model ? ExportFilter.Of(c.Export) : null;
            bool exported = export == null || export.Standalone;
            OwnerFacts owner = c.Export != null ? c.Export.Owner : null;
            if (c.StoreyBy != null) e.TiedBy.Add(exported ? TiedByIfcStorey : TiedByStoreyRule);
            string level = "\"" + levelName + "\"";
            string settings = string.Join(", ", c.RevitSettings);
            if (c.Targeted)
            {
                e.Reason = ReasonNotDeleted;
                e.Why = "the change asked Revit to delete it, and it is still in the model";
            }
            else if (!c.Element.Model)
            {
                e.Reason = ReasonNotAModelElement;
                e.Why = "kept: " + (c.NotModelWhy ?? "it is not a model element") + "; the change deletes model elements only";
            }
            else if (!exported && export.PartOf != null && owner != null)
            {
                e.PartOf = export.PartOf;
                e.Owner = OwnerRef.Of(owner);
                if (owner.Exists)
                {
                    e.Reason = ReasonPartOfKeptElement;
                    e.ListedUnder = ListedPart;
                    e.Why = "part of " + Describe(owner) + ", which is still in the model: " + export.Why + "; it goes with its owner, and is not left on level " + level + " on its own";
                }
                else
                {
                    e.Reason = ReasonOrphanedPart;
                    e.Why = "part of " + Describe(owner) + ", which is no longer in the model: " + export.Why + "; with its owner gone it is left behind, "
                        + (c.RevitSettings.Count > 0 ? "its " + settings + " naming level " + level : "where the exporter's storey rule puts it in level " + level + "'s storey");
                }
            }
            else if (!exported && c.RevitSettings.Count == 0)
            {
                e.Reason = export.Rule;
                e.PartOf = export.PartOf;
                e.ListedUnder = ListedNotExported;
                e.Why = export.Why + (export.PartOf != null ? ", and Revit names nothing it is part of" : "")
                    + "; so it is not filed under level " + level + "'s storey, and no Revit setting ties it to the level";
            }
            else if (c.GroupId != 0)
            {
                e.Reason = ReasonGroupMember;
                e.Why = "it is a member of group " + c.GroupId + ", which is not on level " + level + "; a member is deleted only with its group";
            }
            else if (c.RevitSettings.Count > 0)
            {
                e.Reason = ReasonSettingNotTaken;
                e.Why = "its " + settings + " names level " + level + ", a setting the change does not read as where an element is" + (c.StoreyBy != null && exported ? " (Revit's IFC exporter files it under the level's storey)" : "");
            }
            else if (c.StoreyBy == StoreyByHeight || c.StoreyBy == StoreyLowest)
            {
                e.Reason = ReasonNoRevitLevel;
                e.Why = "it has no Revit level; Revit's IFC exporter files it under level " + level + "'s storey "
                    + (c.StoreyBy == StoreyByHeight
                        ? "by the height of its lowest point" + (c.StoreysSharingSpan > 1 ? " (or under another of the " + c.StoreysSharingSpan + " storeys at that elevation)" : "")
                        : "as the lowest storey, since no storey's span holds it")
                    + "; the change deletes by Revit's own settings and never by a guess";
            }
            else if (c.StoreyBy == StoreyNonStoreyLevel)
            {
                e.Reason = ReasonNonStoreyLevel;
                e.Why = "its level, \"" + c.StoreyVia + "\", is not a building storey, so Revit's IFC exporter files it under level " + level + "'s storey; the change deletes what is on level " + level + " itself";
            }
            else if (c.StoreyBy == StoreyContainerParameter)
            {
                e.Reason = ReasonContainerParameter;
                e.Why = "its IfcSpatialContainer parameter (\"" + c.StoreyVia + "\") files it under level " + level + "'s storey in Revit's IFC export; the change reads Revit's own level settings, not that parameter";
            }
            else
            {
                e.Reason = ReasonSettingNotTaken;
                e.Why = "Revit's IFC exporter files it under level " + level + "'s storey (" + (c.StoreyBy ?? "unknown") + "), by a rule the change does not read";
            }
            return e;
        }

        /// <summary>
        /// The report's left_on_level: every candidate judged, once. What is left (and counted) in
        /// elements, model elements first, the first ElementTally.Cap listed; parts of kept
        /// elements under their owner, the owner with most parts first; what the exporter does not
        /// export and that names no owner, apart. Null when there is no candidate.
        /// </summary>
        public static LeftOnLevel Collect(IEnumerable<LeftCandidate> candidates, string levelName)
        {
            var seen = new HashSet<long>();
            var judged = new List<LeftElement>();
            foreach (LeftCandidate c in candidates)
            {
                if (seen.Add(c.Element.Id)) judged.Add(Judge(c, levelName));
            }
            if (judged.Count == 0) return null;
            List<LeftElement> left = judged.Where(e => e.ListedUnder == ListedLeft).ToList();
            var result = new LeftOnLevel
            {
                Total = left.Count,
                ModelElements = left.Count(e => e.ModelElement),
                OtherElements = left.Count(e => !e.ModelElement),
                Truncated = left.Count > ElementTally.Cap,
                ByReason = CountByReason(left),
                Elements = left.Where(e => e.ModelElement).Concat(left.Where(e => !e.ModelElement)).Take(ElementTally.Cap).ToList(),
            };

            List<LeftElement> parts = judged.Where(e => e.ListedUnder == ListedPart).ToList();
            if (parts.Count > 0)
            {
                var p = new PartsOfKeptElements { Total = parts.Count, Truncated = parts.Count > ElementTally.Cap };
                int listed = 0;
                foreach (var g in parts.GroupBy(e => e.Owner.ElementId).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
                {
                    if (listed >= ElementTally.Cap) break;
                    var o = new OwnerParts { Owner = g.First().Owner };
                    foreach (LeftElement part in g.Take(ElementTally.Cap - listed))
                    {
                        part.Owner = null;
                        o.Parts.Add(part);
                        listed++;
                    }
                    p.Owners.Add(o);
                }
                result.PartsOfKeptElements = p;
            }

            List<LeftElement> notExported = judged.Where(e => e.ListedUnder == ListedNotExported).ToList();
            if (notExported.Count > 0)
            {
                result.NotExported = new NotExportedElements
                {
                    Total = notExported.Count,
                    Truncated = notExported.Count > ElementTally.Cap,
                    ByReason = CountByReason(notExported),
                    Elements = notExported.Take(ElementTally.Cap).ToList(),
                };
            }
            return result;
        }

        private static List<ReasonCount> CountByReason(IEnumerable<LeftElement> elements)
        {
            return elements.GroupBy(e => e.Reason).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, System.StringComparer.Ordinal)
                .Select(g => new ReasonCount { Reason = g.Key, Count = g.Count() }).ToList();
        }
    }
}
