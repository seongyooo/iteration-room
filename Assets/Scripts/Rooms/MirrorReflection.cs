using UnityEngine;
using UnityEngine.Rendering;

namespace IterationRoom
{
    // WHAT A MIRROR SHOWS. A second camera standing behind the glass looking back, rendered into a
    // texture the glass samples by screen position (`MirrorGlass.shader`).
    //
    // **THIS IS THE ONLY WAY TO SEE YOURSELF IN THIS GAME**, and that is not decoration in a game
    // about a person who keeps meeting copies of himself. It is also the only thing a reflection
    // probe cannot do: a probe is a cubemap baked at build time, so it holds no player, no ghosts and
    // no beam - the three things anybody would look in a mirror for.
    //
    // **IT COSTS A WHOLE EXTRA RENDER OF THE ROOM, which is exactly what the CCTV feeds were cut
    // for**, so it is capped by the same four rules and one more of its own:
    //
    //  1. **Only when the silvered side faces you.** A mirror is single-sided; from behind there is
    //     nothing to draw. This alone culls most of them for free - including the one in your own
    //     hands, which points away from you by definition.
    //  2. Only when it is on screen, and only within range.
    //  3. **At most ONE mirror renders per frame, ever**, whatever else is going on.
    //  4. Shadows, post-processing and the depth prepass off. A reflection of a white room needs none
    //     of them, and shadow maps were the expensive part when the feeds were profiled.
    //
    // **RECURSION IS FREE AND ENDS BY ITSELF.** A mirror's camera renders the other mirrors, and they
    // are already showing their own textures from the frame before - so a tunnel fills in over a few
    // frames without anything rendering twice, and each level comes back multiplied by the glass's
    // tint until there is nothing left. One frame of lag per level of depth, which is not something
    // anybody has ever caught in a mirror.
    public class MirrorReflection : MonoBehaviour
    {
        public Mirror mirror;
        public Renderer glass;
        public Camera reflectionCamera;

        public int textureSize = 512;
        public float viewerRange = 12f;
        // Pushes the clip plane a hair in FRONT of the glass. At exactly zero the mirror's own
        // surface is on the plane and z-fights its way into its own reflection.
        public float clipOffset = 0.03f;

        private RenderTexture target;
        private Material instanced;
        private static int lastRenderFrame = -1;

        private static readonly int ReflectionTex = Shader.PropertyToID("_ReflectionTex");

        private void Awake()
        {
            if (glass == null || reflectionCamera == null) return;

            // Built at runtime rather than saved as an asset, for the reason `CctvFeed`'s is: it is a
            // size and a depth buffer, and one more generated file for `SceneBuilder` to keep in step
            // is a file that will go out of step.
            target = new RenderTexture(textureSize, textureSize, 24, RenderTextureFormat.ARGB32)
            {
                name = "MirrorReflection",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
            };
            target.Create();

            // `material` rather than `sharedMaterial`: five mirrors built from one source material
            // would otherwise fight over whose reflection it holds, and four of them would lose.
            instanced = glass.material;
            instanced.SetTexture(ReflectionTex, target);

            reflectionCamera.targetTexture = target;
            reflectionCamera.enabled = false;
        }

        private void OnDestroy()
        {
            if (reflectionCamera != null) reflectionCamera.targetTexture = null;
            if (target == null) return;
            target.Release();
            Destroy(target);
        }

        private void LateUpdate()
        {
            if (target == null || mirror == null) return;
            // **AFTER `Mirror.Sync`, whatever order Unity picks.** The glass is re-aimed off its
            // holder every frame, and a reflection computed from last frame's plane is a reflection
            // that slides about as the holder turns.
            mirror.Sync();

            if (lastRenderFrame == Time.frameCount) return;

            Camera eye = PlayerLookup.Eye;
            if (eye == null || !WorthDrawing(eye)) return;

            lastRenderFrame = Time.frameCount;
            Draw(eye);
        }

        private bool WorthDrawing(Camera eye)
        {
            Vector3 centre = mirror.Centre;
            Vector3 normal = mirror.Normal;
            Vector3 toEye = eye.transform.position - centre;

            // Behind the glass there is nothing to show. This is the cheapest test and it removes
            // the most, the one in the player's own hands first.
            if (Vector3.Dot(toEye, normal) <= 0f) return false;
            if (toEye.sqrMagnitude > viewerRange * viewerRange) return false;

            return PlayerLookup.OnScreen(centre);
        }

        private void Draw(Camera eye)
        {
            Vector3 centre = mirror.Centre;
            Vector3 normal = mirror.Normal;

            float d = -Vector3.Dot(normal, centre) - clipOffset;
            Matrix4x4 reflection = ReflectionMatrix(new Vector4(normal.x, normal.y, normal.z, d));

            // The transform is set as well as the matrix. The matrix is what draws; the transform is
            // what culling and sorting read, and a camera whose two disagree culls the wrong half of
            // the room.
            reflectionCamera.transform.SetPositionAndRotation(
                reflection.MultiplyPoint(eye.transform.position),
                Quaternion.LookRotation(Vector3.Reflect(eye.transform.forward, normal),
                                        Vector3.Reflect(eye.transform.up, normal)));

            reflectionCamera.fieldOfView = eye.fieldOfView;
            reflectionCamera.aspect = eye.aspect;
            reflectionCamera.nearClipPlane = eye.nearClipPlane;
            reflectionCamera.worldToCameraMatrix = eye.worldToCameraMatrix * reflection;

            // **OBLIQUE NEAR PLANE, and without it a mirror shows the wall it is hanging on.**
            // Everything on the far side of the glass is behind the reflection camera's subject and
            // must not be drawn; bending the near plane onto the mirror's own plane is what removes
            // it, and it is why this cannot be done with an ordinary camera pointed backwards.
            Vector4 clipPlane = CameraSpacePlane(reflectionCamera, centre, normal, clipOffset);
            reflectionCamera.projectionMatrix = eye.CalculateObliqueMatrix(clipPlane);

            // A reflection turns the world inside out, so every triangle's winding is reversed. Left
            // alone, the room renders with its back faces toward the camera and disappears.
            GL.invertCulling = true;
            reflectionCamera.Render();
            GL.invertCulling = false;
        }

        // The plane the near clip is bent onto, in the reflection camera's own space.
        private static Vector4 CameraSpacePlane(Camera cam, Vector3 point, Vector3 normal, float offset)
        {
            Vector3 offsetPoint = point + normal * offset;
            Matrix4x4 m = cam.worldToCameraMatrix;
            Vector3 cameraPoint = m.MultiplyPoint(offsetPoint);
            Vector3 cameraNormal = m.MultiplyVector(normal).normalized;
            return new Vector4(cameraNormal.x, cameraNormal.y, cameraNormal.z,
                               -Vector3.Dot(cameraPoint, cameraNormal));
        }

        // Householder reflection about the plane, written out because Unity has no built-in for it.
        private static Matrix4x4 ReflectionMatrix(Vector4 plane)
        {
            Matrix4x4 m = Matrix4x4.identity;

            m.m00 = 1f - 2f * plane.x * plane.x;
            m.m01 = -2f * plane.x * plane.y;
            m.m02 = -2f * plane.x * plane.z;
            m.m03 = -2f * plane.w * plane.x;

            m.m10 = -2f * plane.y * plane.x;
            m.m11 = 1f - 2f * plane.y * plane.y;
            m.m12 = -2f * plane.y * plane.z;
            m.m13 = -2f * plane.w * plane.y;

            m.m20 = -2f * plane.z * plane.x;
            m.m21 = -2f * plane.z * plane.y;
            m.m22 = 1f - 2f * plane.z * plane.z;
            m.m23 = -2f * plane.w * plane.z;

            return m;
        }
    }
}
