using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace IterationRoom.EditorTools
{
    public static partial class SceneBuilder
    {
        private const string WallReferencePath = "Captures/wall_current.png";

        [MenuItem("Iteration Room/Capture Wall Reference")]
        public static void CaptureWallReference()
        {
            Scene scene = EditorSceneManager.OpenScene(CycleScenePath("Cycle1"), OpenSceneMode.Single);
            Transform room = FindInScene(scene, "Room");
            Transform roomOne = FindInScene(scene, "Room1");
            if (room == null || roomOne == null)
                throw new System.InvalidOperationException("Cycle1 scene does not contain Room/Room1.");

            bool roomWasActive = room.gameObject.activeSelf;
            Transform notice = FindChildByName(room, "IntakeNotice");
            bool noticeWasActive = notice != null && notice.gameObject.activeSelf;
            var hidden = new List<Renderer>();
            GameObject cameraObject = null;

            try
            {
                room.gameObject.SetActive(true);
                if (notice != null) notice.gameObject.SetActive(false);
                // Some Room1 props (including the intake notice) live beside the Room1 shell under
                // the cycle root. Filter the whole cycle so none can intrude at the bottom edge.
                hidden = KeepOnlyTheBuilding(room);

                cameraObject = new GameObject("WallReferenceCamera");
                Camera camera = cameraObject.AddComponent<Camera>();
                UniversalAdditionalCameraData cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
                cameraData.renderPostProcessing = true;

                // This reproduces the reference frame's first-person geometry: a level 60-degree
                // lens, 1.6 m eye height, and enough distance from Room1's solid south wall for all
                // five columns plus a narrow strip of each side wall to remain visible. The floor
                // enters only along the bottom fifth, matching menubg_retest.png.
                camera.fieldOfView = 60f;
                camera.nearClipPlane = 0.03f;
                camera.farClipPlane = 100f;
                camera.useOcclusionCulling = false;
                camera.allowHDR = true;
                camera.allowMSAA = true;
                camera.transform.SetPositionAndRotation(
                    new Vector3(0f, 1.6f, -0.7f),
                    Quaternion.LookRotation(Vector3.back, Vector3.up));

                WithFlatReflection(() =>
                    CaptureMenuFrame(camera, WallReferencePath, 960, 540,
                                     TextureImporterType.Default, "Wall reference shot"));
            }
            finally
            {
                foreach (Renderer renderer in hidden)
                    if (renderer != null) renderer.enabled = true;
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                if (notice != null) notice.gameObject.SetActive(noticeWasActive);
                room.gameObject.SetActive(roomWasActive);
            }
        }

        private static Transform FindInScene(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = FindChildByName(root.transform, objectName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
