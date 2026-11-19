using System.Collections.Generic;
using UnityEngine;

namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController
    {
        readonly List<BuildingOcclusionFader> occlusionBuildings = new List<BuildingOcclusionFader>(2);
        TravelerSilhouette travelerSilhouette;

        public TravelerSilhouette TravelerSilhouetteForCheck => travelerSilhouette;
        public int OcclusionBuildingCountForCheck => occlusionBuildings.Count;

        internal void RegisterBuildingOcclusion(BuildingOcclusionFader building)
        {
            if (building == null || worldRoot == null || travelerPuppet == null || page != "town") return;
            if (!occlusionBuildings.Contains(building)) occlusionBuildings.Add(building);
            if (travelerSilhouette == null)
                travelerSilhouette = TravelerSilhouette.Create(this, worldRoot, travelerPuppet);
            UpdateOcclusionLayerOrder();
        }

        internal void UnregisterBuildingOcclusion(BuildingOcclusionFader building)
        {
            occlusionBuildings.Remove(building);
            if (occlusionBuildings.Count == 0 && travelerSilhouette != null)
            {
                var old = travelerSilhouette;
                old.gameObject.SetActive(false);
                Destroy(old.gameObject);
                travelerSilhouette = null;
            }
        }

        // Called only after the unchanged foot sorting. This layer is never a Placeable.
        void UpdateOcclusionLayerOrder()
        {
            if (travelerSilhouette != null) travelerSilhouette.KeepAboveWorld();
        }

        internal bool ObserveBuildingOcclusion(TravelerSilhouette caller, float deltaTime, out bool reduceMotion)
        {
            reduceMotion = settings == null || settings.reduceMotion;
            if (caller != travelerSilhouette || page != "town" || worldRoot == null ||
                !worldRoot.gameObject.activeInHierarchy || traveler == null || town == null) return false;
            bool obscured = false;
            float depth = town.Elevation.ProjectY(worldPos.x, worldPos.y);
            for (int i = occlusionBuildings.Count - 1; i >= 0; i--)
            {
                var building = occlusionBuildings[i];
                if (building == null) { occlusionBuildings.RemoveAt(i); continue; }
                building.Observe(worldPos, depth, traveler, deltaTime);
                obscured |= building.IsOccluding;
            }
            return obscured;
        }

        internal void ReleaseBuildingOcclusion(TravelerSilhouette caller)
        {
            if (caller != travelerSilhouette) return;
            for (int i = 0; i < occlusionBuildings.Count; i++)
                if (occlusionBuildings[i] != null) occlusionBuildings[i].RestoreImmediately();
            // A component-only disable can be re-enabled on the same live page.
            // Page/world teardown drops all references immediately.
            if (page != "town" || worldRoot == null || !worldRoot.gameObject.activeInHierarchy)
            {
                occlusionBuildings.Clear();
                travelerSilhouette = null;
            }
        }

        void ResetTownOcclusion()
        {
            for (int i = 0; i < occlusionBuildings.Count; i++)
                if (occlusionBuildings[i] != null) occlusionBuildings[i].RestoreImmediately();
            occlusionBuildings.Clear();
            if (travelerSilhouette != null) travelerSilhouette.gameObject.SetActive(false);
            travelerSilhouette = null;
        }
    }
}
