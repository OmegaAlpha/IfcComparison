using System.Collections.Generic;
using Xbim.Common.Step21;
using Xbim.Ifc4.Interfaces;

namespace IfcComparison.Models
{
    public class IfcComparerResult
    {
        public List<IfcObjectStorage> OldObjectsNotInNew { get; set; }
        public List<IfcObjectStorage> NewObjectsNotInOld { get; set; }
        public Dictionary<IIfcObject, Dictionary<string, string>> ComparedIfcObjects { get; set; }

        /// <summary>
        /// Objects in the new model that have geometric changes (to be styled in amber/yellow).
        /// </summary>
        public HashSet<IIfcProduct> ModifiedNewObjects { get; set; } = new HashSet<IIfcProduct>();

        /// <summary>
        /// Old revision products corresponding to modified objects, to be injected as purple ghost overlays.
        /// </summary>
        public List<IIfcProduct> ModifiedOldGhostObjects { get; set; } = new List<IIfcProduct>();

        /// <summary>
        /// Old revision products that were deleted, to be injected as translucent red ghosts.
        /// </summary>
        public List<IIfcProduct> DeletedGhostObjects { get; set; } = new List<IIfcProduct>();

        /// <summary>
        /// New revision products that were inserted (new in Rev B), to be styled in green.
        /// </summary>
        public HashSet<IIfcProduct> InsertedObjects { get; set; } = new HashSet<IIfcProduct>();

        /// <summary>
        /// New revision products that have property/metadata changes but no geometric difference.
        /// </summary>
        public HashSet<IIfcProduct> ModifiedPropertyOnlyObjects { get; set; } = new HashSet<IIfcProduct>();

        public IfcComparerResult() 
        { 
        }
    }
}
