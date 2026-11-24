using LmpCommon.QuickLoad;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LmpCommonTest
{
    [TestClass]
    public class QuickLoadTests
    {
        private static QuickLoadFamily Family() => new QuickLoadFamily(new uint[] { 1, 2, 3 }, new[] { "Jeb", "Bill" });

        private static QuickLoadState Ok() => new QuickLoadState
        {
            FeatureEnabled = true,
            HasSnapshot = true,
            SnapshotCrew = new[] { "Jeb" },
            CrewAboardFamily = new[] { "Jeb" },
            CrewAboardOtherVessels = new[] { "Val" },
            SnapshotTime = 1000,
            CurrentTime = 1100,
            MaxRewindMinutes = 60
        };

        #region Family

        [TestMethod]
        public void Family_AllPartsKnown_IsMember()
        {
            Assert.AreEqual(FamilyRelation.Member, Family().Classify(new uint[] { 1, 2, 3 }, null));
        }

        [TestMethod]
        public void Family_SubsetOfParts_IsMember()
        {
            Assert.AreEqual(FamilyRelation.Member, Family().Classify(new uint[] { 3 }, null));
        }

        [TestMethod]
        public void Family_NoKnownParts_IsOutside()
        {
            Assert.AreEqual(FamilyRelation.Outside, Family().Classify(new uint[] { 7, 8 }, null));
        }

        [TestMethod]
        public void Family_EmptyPartList_IsOutside()
        {
            Assert.AreEqual(FamilyRelation.Outside, Family().Classify(new uint[0], null));
            Assert.AreEqual(FamilyRelation.Outside, Family().Classify(null, null));
        }

        [TestMethod]
        public void Family_KnownAndUnknownParts_IsMemberWithOutsideParts()
        {
            Assert.AreEqual(FamilyRelation.MemberWithOutsideParts, Family().Classify(new uint[] { 1, 2, 3, 9 }, null));
        }

        [TestMethod]
        public void Family_EvaKerbalFromCrew_IsMember()
        {
            Assert.AreEqual(FamilyRelation.Member, Family().Classify(new uint[] { 50 }, "Bill"));
        }

        [TestMethod]
        public void Family_EvaKerbalNotInCrew_IsOutside()
        {
            Assert.AreEqual(FamilyRelation.Outside, Family().Classify(new uint[] { 50 }, "Val"));
        }

        #endregion

        #region Decide

        [TestMethod]
        public void Decide_Ok_ReturnsNone()
        {
            Assert.AreEqual(QuickLoadRefusal.None, QuickLoadCheck.Decide(Ok()));
        }

        [TestMethod]
        public void Refuses_WhenFeatureDisabled()
        {
            var s = Ok(); s.FeatureEnabled = false;
            Assert.AreEqual(QuickLoadRefusal.FeatureDisabled, QuickLoadCheck.Decide(s));
        }

        [TestMethod]
        public void Refuses_WithoutSnapshot()
        {
            var s = Ok(); s.HasSnapshot = false;
            Assert.AreEqual(QuickLoadRefusal.NoSnapshot, QuickLoadCheck.Decide(s));
        }

        [TestMethod]
        public void Refuses_WhileSpectating()
        {
            var s = Ok(); s.Spectating = true;
            Assert.AreEqual(QuickLoadRefusal.Spectating, QuickLoadCheck.Decide(s));
        }

        [TestMethod]
        public void Refuses_WhileWarping()
        {
            var s = Ok(); s.Warping = true;
            Assert.AreEqual(QuickLoadRefusal.WarpingOrWaitingSubspace, QuickLoadCheck.Decide(s));
        }

        [TestMethod]
        public void Refuses_WhileWaitingForSubspaceId()
        {
            var s = Ok(); s.WaitingSubspaceId = true;
            Assert.AreEqual(QuickLoadRefusal.WarpingOrWaitingSubspace, QuickLoadCheck.Decide(s));
        }

        [TestMethod]
        public void Refuses_WhenFamilyVesselLockedByOther()
        {
            var s = Ok(); s.FamilyVesselLockedByOther = true;
            Assert.AreEqual(QuickLoadRefusal.LockedByOtherPlayer, QuickLoadCheck.Decide(s));
        }

        [TestMethod]
        public void Refuses_WhenDockedWithOutsider()
        {
            var s = Ok(); s.DockedWithOutsider = true;
            Assert.AreEqual(QuickLoadRefusal.DockedWithOutsideVessel, QuickLoadCheck.Decide(s));
        }

        [TestMethod]
        public void CrewChanged_WhenSnapshotCrewIsOnOtherVessel()
        {
            var s = Ok(); s.CrewAboardFamily = new string[0]; s.CrewAboardOtherVessels = new[] { "Val", "Jeb" };
            Assert.AreEqual(QuickLoadRefusal.CrewChanged, QuickLoadCheck.Decide(s));
        }

        [TestMethod]
        public void CrewChanged_WhenStrangerIsAboardFamily()
        {
            var s = Ok(); s.CrewAboardFamily = new[] { "Jeb", "Bob" };
            Assert.AreEqual(QuickLoadRefusal.CrewChanged, QuickLoadCheck.Decide(s));
        }

        [TestMethod]
        public void CrewDeadOrMissing_IsAllowed()
        {
            var s = Ok(); s.CrewAboardFamily = new string[0];
            Assert.AreEqual(QuickLoadRefusal.None, QuickLoadCheck.Decide(s));
        }

        [TestMethod]
        public void NullCrewArrays_CountAsEmpty()
        {
            var s = Ok(); s.SnapshotCrew = null; s.CrewAboardFamily = null; s.CrewAboardOtherVessels = null;
            Assert.AreEqual(QuickLoadRefusal.None, QuickLoadCheck.Decide(s));
        }

        [TestMethod]
        public void ZeroOrNegativeLimit_MeansNoLimit()
        {
            var s = Ok(); s.SnapshotTime = 0; s.CurrentTime = 1e9;
            s.MaxRewindMinutes = 0;
            Assert.AreEqual(QuickLoadRefusal.None, QuickLoadCheck.Decide(s));
            s.MaxRewindMinutes = -5;
            Assert.AreEqual(QuickLoadRefusal.None, QuickLoadCheck.Decide(s));
        }

        [TestMethod]
        public void ExactlyAtLimit_IsAllowed()
        {
            var s = Ok(); s.SnapshotTime = 1000; s.CurrentTime = 4600;
            Assert.AreEqual(QuickLoadRefusal.None, QuickLoadCheck.Decide(s));
        }

        [TestMethod]
        public void OneSecondOver_IsRefused()
        {
            var s = Ok(); s.SnapshotTime = 1000; s.CurrentTime = 4601;
            Assert.AreEqual(QuickLoadRefusal.SnapshotTooOld, QuickLoadCheck.Decide(s));
        }

        [TestMethod]
        public void Order_FeatureDisabledWinsOverNoSnapshot()
        {
            var s = Ok(); s.FeatureEnabled = false; s.HasSnapshot = false;
            Assert.AreEqual(QuickLoadRefusal.FeatureDisabled, QuickLoadCheck.Decide(s));
        }

        #endregion
    }
}
