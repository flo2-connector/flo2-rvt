// What a delete_elements_on_level left tied to its level, and why each was not taken.
//
// The edit job reads the model AFTER the change's transaction (Edit/Leftovers.cs) and hands
// every element still tied to the level here as a candidate: what in its Revit settings names
// the level, and whether Revit's own IFC exporter files it under the level's storey (and how:
// Logic/Storeys.cs). This file says why it was left, and builds the report's left_on_level.
//
// ⭐ NOTHING HERE IS DELETED: an element with no Revit level is reported, never deleted by a
// guess. The change reads applied_in_part when left_on_level holds any model element; the
// others (views of the level, its sketch planes, annotation) are listed and counted apart.
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
    }

    public static class LeftOnLevelRules
    {
        public const string TiedByIfcStorey = "ifc_storey";

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

        /// <summary>Its reason code and why, in a sentence.</summary>
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
            };
            if (c.StoreyBy != null) e.TiedBy.Add(TiedByIfcStorey);
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
            else if (c.GroupId != 0)
            {
                e.Reason = ReasonGroupMember;
                e.Why = "it is a member of group " + c.GroupId + ", which is not on level " + level + "; a member is deleted only with its group";
            }
            else if (c.RevitSettings.Count > 0)
            {
                e.Reason = ReasonSettingNotTaken;
                e.Why = "its " + settings + " names level " + level + ", a setting the change does not read as where an element is" + (c.StoreyBy != null ? " (Revit's IFC exporter files it under the level's storey)" : "");
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

        /// <summary>The report's left_on_level: every candidate judged, model elements first, the first ElementTally.Cap listed; null when there is none.</summary>
        public static LeftOnLevel Collect(IEnumerable<LeftCandidate> candidates, string levelName)
        {
            var seen = new HashSet<long>();
            var judged = new List<LeftElement>();
            foreach (LeftCandidate c in candidates)
            {
                if (seen.Add(c.Element.Id)) judged.Add(Judge(c, levelName));
            }
            if (judged.Count == 0) return null;
            var left = new LeftOnLevel
            {
                Total = judged.Count,
                ModelElements = judged.Count(e => e.ModelElement),
                OtherElements = judged.Count(e => !e.ModelElement),
                Truncated = judged.Count > ElementTally.Cap,
            };
            foreach (var g in judged.GroupBy(e => e.Reason).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, System.StringComparer.Ordinal))
            {
                left.ByReason.Add(new ReasonCount { Reason = g.Key, Count = g.Count() });
            }
            left.Elements = judged.Where(e => e.ModelElement).Concat(judged.Where(e => !e.ModelElement)).Take(ElementTally.Cap).ToList();
            return left;
        }
    }
}
