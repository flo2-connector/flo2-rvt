// The model groups a level move touches, read in Revit for Logic/GroupsInTheWay.cs. Since
// add-in 0.5.0. Read only: nothing here changes the model (the one ungrouping is the edit job's,
// inside the change's own transaction, Jobs/EditJob.cs).
//
// A group is TIED to a level when its Reference Level (GROUP_LEVEL) is that level, or any of
// its members, nested groups' members included, names it: Element.LevelId, a base, reference or
// Schedule Level (LevelRules.BaseLevelParameters), or a top level (LevelRules.TopLevelParameters).
// Only groups placed in the model are judged; a nested group is judged with the group that holds
// it. A detail group (drawn in one view) is not judged: a level move does not change its shape.
// What is read is what the move is EXPECTED to do; Revit's own failures say what it does.

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Flo2Rvt.Addin.Edit.Logic;

namespace Flo2Rvt.Addin.Edit
{
    public static class Groups
    {
        private const int MaxNesting = 16;

        /// <summary>Every model group placed in the model that is tied to one of these levels.</summary>
        public static List<GroupFacts> TiedTo(Document doc, ElementIndex index, ICollection<long> levelIds)
        {
            var moved = new HashSet<long>(levelIds);
            var levels = LevelIds(doc);
            var found = new List<GroupFacts>();
            foreach (Group g in new FilteredElementCollector(doc).OfClass(typeof(Group)).Cast<Group>())
            {
                if (g.GroupId != ElementId.InvalidElementId || g.ViewSpecific) continue;
                GroupFacts f = Facts(doc, index, g, levels);
                if (f.TiedLevels.Any(moved.Contains)) found.Add(f);
            }
            return found;
        }

        /// <summary>The group as the report names it, with every level it is tied to.</summary>
        public static GroupFacts Facts(Document doc, ElementIndex index, Group g)
        {
            return Facts(doc, index, g, LevelIds(doc));
        }

        /// <summary>The group a model element is a member of; 0 when none (Logic/GroupsInTheWay.Named walks up to the outermost).</summary>
        public static long GroupOf(Document doc, long id)
        {
            Element e = doc.GetElement(new ElementId(id));
            return e != null && e.GroupId != ElementId.InvalidElementId ? e.GroupId.Value : 0;
        }

        public static bool IsGroup(Document doc, long id)
        {
            return doc.GetElement(new ElementId(id)) is Group;
        }

        private static HashSet<long> LevelIds(Document doc)
        {
            return new HashSet<long>(new FilteredElementCollector(doc).OfClass(typeof(Level)).ToElementIds().Select(id => id.Value));
        }

        private static GroupFacts Facts(Document doc, ElementIndex index, Group g, HashSet<long> levels)
        {
            GroupType type = g.GroupType;
            var f = new GroupFacts
            {
                Id = g.Id.Value,
                GlobalId = index.Facts(doc, g.Id).GlobalId,
                Name = g.Name,
                TypeId = type != null ? type.Id.Value : 0,
                TypeName = type != null ? type.Name : null,
                InstancesOfType = type != null ? type.Groups.Size : 0,
                Members = g.GetMemberIds().Count,
            };
            Parameter reference = g.get_Parameter(BuiltInParameter.GROUP_LEVEL);
            if (reference != null && reference.StorageType == StorageType.ElementId)
            {
                Level level = doc.GetElement(reference.AsElementId()) as Level;
                if (level != null) f.ReferenceLevel = level.Name;
            }
            var tied = new HashSet<long>();
            Ties(doc, g, tied, levels, 0);
            f.TiedLevels = tied.ToList();
            return f;
        }

        private static void Ties(Document doc, Element e, HashSet<long> tied, HashSet<long> levels, int depth)
        {
            if (e.LevelId != ElementId.InvalidElementId && levels.Contains(e.LevelId.Value)) tied.Add(e.LevelId.Value);
            foreach (BuiltInParameter p in LevelRules.BaseLevelParameters.Concat(LevelRules.TopLevelParameters))
            {
                Parameter v = e.get_Parameter(p);
                if (v == null || v.StorageType != StorageType.ElementId) continue;
                ElementId id = v.AsElementId();
                if (id != null && levels.Contains(id.Value)) tied.Add(id.Value);
            }
            var group = e as Group;
            if (group == null || depth >= MaxNesting) return;
            foreach (ElementId m in group.GetMemberIds())
            {
                Element member = doc.GetElement(m);
                if (member != null) Ties(doc, member, tied, levels, depth + 1);
            }
        }
    }
}
