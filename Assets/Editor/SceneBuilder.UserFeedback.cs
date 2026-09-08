using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IterationRoom.EditorTools
{
    public static partial class SceneBuilder
    {
        // Shared by a full rebuild and the incremental refresh: generated scenes have one source.
        private static void AddDisplayApplyButton(SettingsPanel panel)
        {
            if (panel.applyDisplayButton != null || panel.resolutionValue == null) return;
            panel.applyDisplayButton = Localize(MakeSettingsButton(panel.resolutionValue.transform.parent,
                "ApplyDisplay", "APPLY", new Vector2(SettingsControlX + 170f, 25f),
                new Vector2(230f, 36f), out _, panel.resolutionValue.color), "set.apply");
        }

        private static void ApplyUserFeedbackToScene(Scene scene)
        {
            T[] All<T>() where T : Component => scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
            foreach (Camera camera in All<Camera>())
                if (camera.CompareTag("MainCamera") && camera.GetComponent<FixedAspectPresentation>() == null)
                    camera.gameObject.AddComponent<FixedAspectPresentation>();
            foreach (Canvas canvas in All<Canvas>())
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.isRootCanvas
                    && canvas.GetComponent<FixedAspectPresentation>() == null)
                    canvas.gameObject.AddComponent<FixedAspectPresentation>();
            foreach (SettingsPanel panel in All<SettingsPanel>()) AddDisplayApplyButton(panel);
            foreach (CarriedItemsDisplay display in All<CarriedItemsDisplay>()) display.dropHintRetireAfter = 1;
            foreach (TreeTrunk tree in All<TreeTrunk>()) AddStandingTreeCollider(tree);
            foreach (Transform t in All<Transform>())
            {
                if (t.name == "Room3_1_NorthCorridor") SealCorridorFloor(t);
                if (t.name == "Room3_1") SealRoomThreeGateFloors(t);
                if (t.name == "Cable" && t.Find("Cable_1") != null) ConnectCableToStation(t);
            }
            foreach (LadderMount mount in All<LadderMount>()) AlignWallLadder(mount);
            foreach (EndingDeparture departure in All<EndingDeparture>()) FixEndingShaftSeal(departure);
            foreach (FacilityFailure failure in All<FacilityFailure>())
                failure.movers = failure.movers
                    .Where(m => m.what != null && !HasAncestorNamed(m.what, "LadderShaft"))
                    .ToArray();
        }

        private static void AddStandingTreeCollider(TreeTrunk tree)
        {
            // Reuse the trunk's measured cut width, not the mesh bounds (which include branches).
            Transform treeRoot = tree.fallPivot != null ? tree.fallPivot.parent : tree.transform.parent;
            BoxCollider stump = treeRoot != null ? treeRoot.Find("StumpBlocker")?.GetComponent<BoxCollider>() : null;
            if (stump == null) throw new System.InvalidOperationException("Standing tree has no measured StumpBlocker: " + tree.name);
            var blocker = tree.standingBlocker as CapsuleCollider;
            if (blocker == null)
            {
                var go = new GameObject("StandingTrunkCollider");
                blocker = go.AddComponent<CapsuleCollider>();
            }
            blocker.transform.SetParent(treeRoot, false);
            blocker.transform.localPosition = Vector3.zero;
            blocker.height = TreeCutHeight + TreeSinkDepth + 2.5f;
            blocker.center = new Vector3(stump.transform.localPosition.x,
                blocker.height * 0.5f, stump.transform.localPosition.z);
            blocker.radius = Mathf.Min(stump.size.x, stump.size.z) * 0.5f;
            tree.standingBlocker = blocker;
        }

        private static void SealCorridorFloor(Transform corridor)
        {
            if (corridor.Find("FloorSeamBacking") != null) return;
            Transform floor = corridor.Find("Floor");
            if (floor == null) return;
            Vector3 size = floor.localScale;
            // 2 cm below the walking face, overlapping the joints underneath without z-fighting.
            Prim(PrimitiveType.Cube, "FloorSeamBacking", corridor,
                floor.localPosition + Vector3.down * 0.02f,
                size + new Vector3(0.2f, 0f, 0.4f), floor.GetComponent<Renderer>().sharedMaterial);
        }

        private static void SealRoomThreeGateFloors(Transform room)
        {
            Transform floor = room.Find("Floor");
            Renderer floorRenderer = floor != null ? floor.GetComponent<Renderer>() : null;
            if (floorRenderer == null) return;

            Rect northSouthGate = GateCutout(RoomWidth);
            Rect eastWestGate = GateCutout(RoomDepth);

            // Standard neighbouring rooms include a 10 cm pocket between their wall build-ups for
            // the gate leaves. Their slabs stop on either side of that pocket, so opening a gate used
            // to expose sky below the threshold. Fill the pocket at floor height, then put a wider
            // backing underneath so floating-point seams cannot reopen it from a grazing angle.
            AddGateThreshold(room, "South", new Vector3(0f, 0f, -RoomPitch / 2f),
                new Vector3(northSouthGate.width, WallThickness, DoorPocketDepth), floorRenderer.sharedMaterial);
            AddGateThreshold(room, "East", new Vector3(RoomPitchX / 2f, 0f, 0f),
                new Vector3(DoorPocketDepth, WallThickness, eastWestGate.width), floorRenderer.sharedMaterial);
            AddGateThreshold(room, "West", new Vector3(-RoomPitchX / 2f, 0f, 0f),
                new Vector3(DoorPocketDepth, WallThickness, eastWestGate.width), floorRenderer.sharedMaterial);
        }

        private static void AddGateThreshold(Transform room, string side, Vector3 centre,
                                             Vector3 size, Material material)
        {
            UpsertCube(room, "GateThreshold_" + side,
                centre + Vector3.down * (WallThickness / 2f), size, material);

            Vector3 backingSize = size;
            if (size.x < size.z) backingSize.x += 0.20f;
            else backingSize.z += 0.20f;
            UpsertCube(room, "GateThresholdBacking_" + side,
                centre + Vector3.down * (WallThickness + 0.04f),
                new Vector3(backingSize.x, 0.08f, backingSize.z), material);
        }

        private static void UpsertCube(Transform parent, string name, Vector3 position,
                                       Vector3 scale, Material material)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject
                : Prim(PrimitiveType.Cube, name, parent, position, scale, material);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = scale;
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
        }

        private static void FixEndingShaftSeal(EndingDeparture departure)
        {
            if (departure == null || departure.shaftLid == null) return;
            Transform lid = departure.shaftLid;
            lid.localPosition = new Vector3(lid.localPosition.x, -ShaftLidPark, lid.localPosition.z);
            lid.localScale = new Vector3(GridCellWidth + ShaftLidOverlap, ShaftLidThickness,
                                         GridCellWidth + ShaftLidOverlap);
            departure.shaftLidRise = ShaftLidRise;
        }

        private static bool HasAncestorNamed(Transform transform, string name)
        {
            for (Transform current = transform; current != null; current = current.parent)
                if (current.name == name) return true;
            return false;
        }

        private static void ConnectCableToStation(Transform cable)
        {
            Transform first = cable.Find("Cable_1");
            Vector3 end = first.position - first.up * first.lossyScale.y;
            // The first path point is the car centre, CarStandoffFromWall outside the visible wall.
            // The old extension also added the whole wall depth and 10 cm, leaving about 25 cm of
            // cable visible inside room3-2N. Even half the rope thickness hidden in the wall remained
            // visible from inside at the breach, so stop 1 cm outside the wall and overlap the
            // exterior cable by 1 cm. At this scale the join stays closed without entering the room.
            Vector3 wallFace = end - Vector3.right * CarStandoffFromWall;
            Vector3 inside = wallFace + Vector3.right * 0.01f;
            Vector3 outside = end + Vector3.right * 0.01f;
            Transform existing = cable.Find("StationConnection");
            GameObject line = existing != null ? existing.gameObject
                : Prim(PrimitiveType.Cylinder, "StationConnection", cable,
                    Vector3.zero, Vector3.one, first.GetComponent<Renderer>().sharedMaterial,
                    removeCollider: true);
            Vector3 span = outside - inside;
            line.transform.position = (inside + outside) * 0.5f;
            line.transform.rotation = Quaternion.FromToRotation(Vector3.up, span.normalized);
            line.transform.localScale = new Vector3(CableThickness, span.magnitude * 0.5f, CableThickness);
        }

        private static void AlignWallLadder(LadderMount mount)
        {
            mount.transform.localPosition = new Vector3(LadderFootX, DeckBSurfaceY, LadderShaftXZ.y);
            mount.climbDirection = mount.transform.parent.TransformDirection(Vector3.up);
            mount.climbOffset = mount.transform.parent.TransformDirection(Vector3.right * 0.5f);
            mount.radius = 0.65f;
            if (mount.seat != null)
            {
                // Incremental scenes may still carry the previously longer ladder model.
                // Seat its actual midpoint so its foot stays on the deck in both build paths.
                CarryableItem ladder = mount.gameObject.scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<CarryableItem>(true))
                    .FirstOrDefault(i => i.itemId == LadderItemId);
                float length = ladder != null ? ladder.transform.TransformVector(ladder.heldSpan).magnitude : LadderLength;
                mount.seat.localPosition = Vector3.up * (length * 0.5f);
                mount.seat.localRotation = Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(-90f, 0f, 0f);
            }
            if (mount.hintAnchor != null) mount.hintAnchor.localPosition = Vector3.up * 1.2f;
        }

        [MenuItem("Iteration Room/Apply User Feedback")]
        public static void ApplyUserFeedback()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play Mode first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (string path in EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path))
                {
                    Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    ApplyUserFeedbackToScene(scene);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                AssetDatabase.SaveAssets();
                Debug.Log("[UserFeedback] Updated presentation, display settings, tree, corridor, ladder and cable.");
            }
            finally
            {
                if (setup.Any(s => s.isLoaded && s.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }
    }
}
