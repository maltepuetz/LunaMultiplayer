using System.Collections.Generic;

namespace LmpCommon.QuickLoad
{
    public enum FamilyRelation
    {
        Outside,
        Member,
        MemberWithOutsideParts
    }

    /// <summary>
    /// Tells if a vessel descends from the vessel that was quicksaved.
    /// A vessel belongs to the family when it holds at least one part that the quicksaved vessel had,
    /// or when it is a kerbal on EVA that was part of its crew.
    /// </summary>
    public class QuickLoadFamily
    {
        private readonly HashSet<uint> _partFlightIds;
        private readonly HashSet<string> _crewNames;

        public QuickLoadFamily(IEnumerable<uint> partFlightIds, IEnumerable<string> crewNames)
        {
            _partFlightIds = new HashSet<uint>(partFlightIds ?? new uint[0]);
            _crewNames = new HashSet<string>(crewNames ?? new string[0]);
        }

        /// <param name="vesselPartFlightIds">Flight ids of the parts of the vessel to classify</param>
        /// <param name="evaKerbalName">Name of the kerbal when the vessel is a kerbal on EVA, null otherwise</param>
        public FamilyRelation Classify(IEnumerable<uint> vesselPartFlightIds, string evaKerbalName)
        {
            if (evaKerbalName != null)
                return _crewNames.Contains(evaKerbalName) ? FamilyRelation.Member : FamilyRelation.Outside;

            if (vesselPartFlightIds == null)
                return FamilyRelation.Outside;

            var known = false;
            var unknown = false;
            foreach (var partFlightId in vesselPartFlightIds)
            {
                if (_partFlightIds.Contains(partFlightId))
                    known = true;
                else
                    unknown = true;
            }

            if (!known) return FamilyRelation.Outside;
            return unknown ? FamilyRelation.MemberWithOutsideParts : FamilyRelation.Member;
        }
    }
}
