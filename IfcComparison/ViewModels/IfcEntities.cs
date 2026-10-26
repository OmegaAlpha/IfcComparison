using IfcComparison.Enumerations;
using IfcComparison.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IfcComparison.Utils;

namespace IfcComparison.ViewModels
{
    public class IfcEntity
    {
        /// <summary>
        /// The output QA property set name(s). May be a single name or comma-separated list.
        /// </summary>
        public string PSetName { get; set; } = "QA_PSET";

        /// <summary>
        /// The IFC entity (interface) name(s). May be a single name, a comma-separated list,
        /// or "*" to match all IFC entities.
        /// </summary>
        public string Entity { get; set; }

        public List<string> IfcPropertySets { get; set; } = new List<string>();
        public string ComparisonOperator { get; set; } = string.Empty;
        public string ComparisonMethod { get; set; } = nameof(ComparisonEnumeration.Identifier);

        /// <summary>
        /// Whether to evaluate 3D shape, volume, centroid displacement, and bounding box.
        /// </summary>
        public bool CompareGeometry { get; set; } = false;

        /// <summary>
        /// Volume difference tolerance in cubic meters (default 0.001 = 1 liter).
        /// </summary>
        public double VolumeToleranceM3 { get; set; } = 0.001;

        /// <summary>
        /// Volume percentage change tolerance (default 1.0 = 1%). When greater than 0, relative volume changes exceeding this threshold flag VolumeChanged.
        /// </summary>
        public double VolumePercentTolerance { get; set; } = 1.0;

        /// <summary>
        /// Position / centroid displacement tolerance in millimeters (default 1.0 mm).
        /// </summary>
        public double PositionToleranceMm { get; set; } = 1.0;

        /// <summary>
        /// Whether to generate visual 3D ghost overlays (deleted in red, inserted in green, modified new in yellow, modified old in purple).
        /// </summary>
        public bool EnableGhostOverlays { get; set; } = true;

        // ----------------------------------------------------------------
        // Helpers -- not serialized, computed on demand
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns the resolved list of individual entity interface names.
        /// "*" expands to all known IFC entities. Comma-separated values are split.
        /// Each name is normalised to interface form (IIfcXxx).
        /// </summary>
        public List<string> ResolvedEntities
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Entity))
                    return new List<string>();

                if (Entity.Trim() == "*")
                    return IfcTools.IfcEntities.Select(t => t.Name).ToList();

                return Entity
                    .Split(',')
                    .Select(e => IfcTools.DisplayNameToInterfaceName(e.Trim()))
                    .Where(e => !string.IsNullOrEmpty(e))
                    .ToList();
            }
        }

        /// <summary>
        /// Returns the resolved list of individual PSet output names.
        /// Comma-separated values are split and trimmed.
        /// </summary>
        public List<string> ResolvedPSetNames
        {
            get
            {
                if (string.IsNullOrWhiteSpace(PSetName))
                    return new List<string> { "QA_PSET" };

                return PSetName
                    .Split(',')
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();
            }
        }

        /// <summary>
        /// Expands this row into concrete single-entity / single-psetname entries
        /// (one per combination of resolved entity x resolved PSetName).
        /// When both are single values the original object is returned as-is.
        /// </summary>
        public IEnumerable<IfcEntity> Expand()
        {
            var entities = ResolvedEntities;
            var psetNames = ResolvedPSetNames;

            if (entities.Count == 0)
                entities = new List<string> { Entity };

            foreach (var entityName in entities)
            {
                foreach (var psetName in psetNames)
                {
                    yield return new IfcEntity
                    {
                        PSetName = psetName,
                        Entity = entityName,
                        IfcPropertySets = IfcPropertySets,
                        ComparisonOperator = ComparisonOperator,
                        ComparisonMethod = ComparisonMethod,
                        CompareGeometry = CompareGeometry,
                        VolumeToleranceM3 = VolumeToleranceM3,
                        PositionToleranceMm = PositionToleranceMm,
                        EnableGhostOverlays = EnableGhostOverlays
                    };
                }
            }
        }
    }
}
