using UnityEngine;

namespace IterationRoom
{
    // The two shapes water takes in this room, generated rather than taken from a primitive.
    //
    // A PRIMITIVE CANNOT DO EITHER OF THEM. Unity's cylinder is one segment tall, so a stream built
    // from it has no vertices down its length to taper, narrow or wobble - and its cap is a fan of a
    // few triangles, so a puddle built from it is a perfect circle and can only ever be a perfect
    // circle. Both of those are exactly the things that made the water read as a 3D object.
    public static class WaterMeshes
    {
        // A FALLING STREAM. Round at the top and narrower at the bottom, because water accelerates as
        // it falls and a stream of constant width is the clearest possible statement that it is not
        // moving. The taper is real physics done cheaply: at constant flow, width goes as the inverse
        // square root of speed, and speed goes as the square root of the drop.
        //
        // The section is not a circle either - each ring is pushed in and out by a little noise that
        // TRAVELS DOWN the stream, so the silhouette ripples along its length instead of being a
        // solid of revolution. Height is 1 and radius 0.5 at the top; the caller scales it.
        public static Mesh Stream(int rings, int segments, float bottomScale, float irregularity, int seed)
        {
            var mesh = new Mesh { name = "WaterStream" };

            var rng = new System.Random(seed);
            float phase = (float)rng.NextDouble() * 10f;

            var verts = new Vector3[rings * segments];
            var uv = new Vector2[rings * segments];
            var norms = new Vector3[rings * segments];

            int v = 0;
            for (int r = 0; r < rings; r++)
            {
                float t = r / (float)(rings - 1);          // 0 at the top, 1 at the floor
                // Inverse-square-root taper, softened so the bottom does not pinch to nothing.
                float speed = Mathf.Sqrt(Mathf.Max(0.08f, t));
                float width = Mathf.Lerp(1f, bottomScale, Mathf.Clamp01(speed));

                for (int s = 0; s < segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    // Two incommensurate waves around and along, so no ring repeats the one above it
                    // and the pattern has no period the eye can find.
                    float wob = Mathf.Sin(a * 3f + t * 11f + phase) * 0.6f
                              + Mathf.Sin(a * 5f - t * 7f + phase * 2f) * 0.4f;
                    float rad = 0.5f * width * (1f + wob * irregularity);

                    verts[v] = new Vector3(Mathf.Cos(a) * rad, 0.5f - t, Mathf.Sin(a) * rad);
                    norms[v] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    // V runs DOWN the stream, so a texture scrolled in V falls with the water, and U
                    // wraps once around.
                    uv[v] = new Vector2(s / (float)segments, t);
                    v++;
                }
            }

            var tris = new System.Collections.Generic.List<int>(rings * segments * 6);
            for (int r = 0; r < rings - 1; r++)
            {
                int a0 = r * segments, b0 = (r + 1) * segments;
                for (int s = 0; s < segments; s++)
                {
                    int s2 = (s + 1) % segments;
                    tris.Add(a0 + s); tris.Add(a0 + s2); tris.Add(b0 + s);
                    tris.Add(a0 + s2); tris.Add(b0 + s2); tris.Add(b0 + s);
                }
            }

            mesh.vertices = verts;
            mesh.uv = uv;
            mesh.normals = norms;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        // AN OPEN TUBE: a wall with a thickness, a rim at the top, and NOTHING ACROSS EITHER END.
        //
        // A Unity cylinder primitive cannot be this. It is capped at both ends and its collider is a
        // capsule filling the whole volume, so as a tank it was a solid lump with a lid: the pour went
        // into a closed vessel, the waterline was read through a ceiling, and the inside was somewhere
        // that did not exist. This is the same shape with the middle actually empty.
        //
        // BOTH SURFACES ARE REAL. A single-walled tube is invisible from the inside under back-face
        // culling, and worse for glass, which is only believable when you can see it has a thickness -
        // the rim is where that reads, which is why the top annulus is here rather than left open.
        //
        // Unit outer radius 0.5 and unit height centred on the origin, so it drops straight into the
        // scale a primitive cylinder was using (with the caller's Y no longer halved - a primitive is
        // two units tall and this is one).
        public static Mesh Tube(int segments, float wallThickness)
        {
            var mesh = new Mesh { name = "GlassTube" };

            float outer = 0.5f;
            float inner = Mathf.Max(0.02f, outer - wallThickness);

            var verts = new Vector3[segments * 4];
            var norms = new Vector3[segments * 4];
            var uv = new Vector2[segments * 4];

            for (int s = 0; s < segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                float u = s / (float)segments;
                int o = s * 4;

                verts[o + 0] = new Vector3(cos * outer, -0.5f, sin * outer);
                verts[o + 1] = new Vector3(cos * outer, 0.5f, sin * outer);
                verts[o + 2] = new Vector3(cos * inner, -0.5f, sin * inner);
                verts[o + 3] = new Vector3(cos * inner, 0.5f, sin * inner);

                norms[o + 0] = norms[o + 1] = new Vector3(cos, 0f, sin);
                // INWARD, so the wall lights correctly when the player is looking at it from inside.
                norms[o + 2] = norms[o + 3] = new Vector3(-cos, 0f, -sin);

                uv[o + 0] = new Vector2(u, 0f);
                uv[o + 1] = new Vector2(u, 1f);
                uv[o + 2] = new Vector2(u, 0f);
                uv[o + 3] = new Vector2(u, 1f);
            }

            var tris = new System.Collections.Generic.List<int>(segments * 18);
            for (int s = 0; s < segments; s++)
            {
                int a = s * 4, b = ((s + 1) % segments) * 4;

                // Outer wall, facing out.
                tris.Add(a + 0); tris.Add(a + 1); tris.Add(b + 0);
                tris.Add(a + 1); tris.Add(b + 1); tris.Add(b + 0);

                // Inner wall, wound the other way so it faces into the tube.
                tris.Add(a + 2); tris.Add(b + 2); tris.Add(a + 3);
                tris.Add(a + 3); tris.Add(b + 2); tris.Add(b + 3);

                // The rim: the annulus that joins the two at the top, and the whole of why the glass
                // reads as having a thickness rather than as a sheet.
                tris.Add(a + 1); tris.Add(a + 3); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(a + 3); tris.Add(b + 3);
            }

            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uv;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A BAND: an open cylinder with NO CAPS, which is the difference between a line drawn round a
        // vessel and a plate bolted to it.
        //
        // The target mark used to be a primitive cylinder, and a primitive cylinder has a top and a
        // bottom face - so however thin it was scaled it kept reading as a disc seen edge-on, most
        // obviously from above where the whole lid of it was in view. This is the wall of that
        // cylinder and nothing else.
        //
        // TWO-SIDED, because the mark is on a transparent vessel: the far side of the band is seen
        // through the near side, and a back-face-culled ring is a semicircle from every angle.
        public static Mesh Band(int segments)
        {
            var mesh = new Mesh { name = "MarkBand" };

            var verts = new Vector3[segments * 2];
            var norms = new Vector3[segments * 2];
            var uv = new Vector2[segments * 2];

            for (int s = 0; s < segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                int o = s * 2;

                verts[o + 0] = new Vector3(cos * 0.5f, -0.5f, sin * 0.5f);
                verts[o + 1] = new Vector3(cos * 0.5f, 0.5f, sin * 0.5f);
                norms[o + 0] = norms[o + 1] = new Vector3(cos, 0f, sin);
                uv[o + 0] = new Vector2(s / (float)segments, 0f);
                uv[o + 1] = new Vector2(s / (float)segments, 1f);
            }

            var tris = new System.Collections.Generic.List<int>(segments * 12);
            for (int s = 0; s < segments; s++)
            {
                int a = s * 2, b = ((s + 1) % segments) * 2;

                tris.Add(a + 0); tris.Add(a + 1); tris.Add(b + 0);
                tris.Add(a + 1); tris.Add(b + 1); tris.Add(b + 0);
                // ...and the same quad wound backwards. The mark is emissive, so one set of normals
                // serves both faces - what matters is that neither side is culled away.
                tris.Add(b + 0); tris.Add(a + 1); tris.Add(a + 0);
                tris.Add(b + 0); tris.Add(b + 1); tris.Add(a + 1);
            }

            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uv;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A SPILL, AND NOT A CIRCLE. Water on a flat floor does not spread as a disc - it finds every
        // slope and every imperfection, so its outline is a lobed blob that is roughly round and
        // nowhere circular. The radius per angle is a couple of low-frequency waves plus noise, which
        // is enough: the eye reads "not drawn with a compass" long before it reads any particular
        // shape.
        //
        // FLAT. No ripple, no vertex animation - play called the wave surface less natural than the
        // still one, and it was right. Standing water in a sealed room is still; what moves on it is
        // the highlight, and that comes from the shader's noise rather than from the mesh.
        //
        // Unit radius, so the puddle scales it in X and Z.
        // `depth` is the water LEVEL, in metres, and it is what the mesh gains an edge for. A puddle
        // with no thickness has no waterline, and without a waterline there is nothing for the shader's
        // fresnel to catch at the rim - which is where almost all of the "that is water" information
        // lives. The surface sits at `depth` and a skirt drops from it to the floor, so the spill has a
        // visible side seen from anywhere but straight above.
        //
        // Kept out of the transform's Y scale on purpose: the puddle scales in X and Z as it spreads,
        // and a depth that scaled with it would be a foot deep by the time it reached the wall.
        public static Mesh Spill(int rings, int segments, float irregularity, float depth, int seed)
        {
            var mesh = new Mesh { name = "WaterSpill" };

            var rng = new System.Random(seed);
            float p1 = (float)rng.NextDouble() * 6.283f;
            float p2 = (float)rng.NextDouble() * 6.283f;
            float p3 = (float)rng.NextDouble() * 6.283f;

            var edge = new float[segments];
            for (int s = 0; s < segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                float shape = Mathf.Sin(a * 2f + p1) * 0.5f
                            + Mathf.Sin(a * 3f + p2) * 0.32f
                            + Mathf.Sin(a * 5f + p3) * 0.18f;
                edge[s] = 1f + shape * irregularity;
            }

            // One extra ring of vertices at the bottom of the skirt: the outer edge repeated at floor
            // level, so the rim is a wall rather than a crease.
            var verts = new Vector3[rings * segments + 1 + segments];
            var uv = new Vector2[verts.Length];
            verts[0] = new Vector3(0f, depth, 0f);
            uv[0] = new Vector2(0.5f, 0.5f);

            int v = 1;
            for (int r = 1; r <= rings; r++)
            {
                float t = r / (float)rings;
                for (int s = 0; s < segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    // The lobes grow with the radius, so the middle stays round and only the edge is
                    // ragged - which is where the shape of a spill actually lives.
                    float rad = t * Mathf.Lerp(1f, edge[s], t) * 0.5f;
                    // Very slightly domed, which is surface tension and is also what stops a wide
                    // puddle reading as a decal: a perfectly flat sheet has no shading across it.
                    float y = depth * (1f - t * t * 0.25f);
                    verts[v] = new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad);
                    uv[v] = new Vector2(0.5f + Mathf.Cos(a) * rad, 0.5f + Mathf.Sin(a) * rad);
                    v++;
                }
            }

            // The skirt's bottom ring, on the floor directly under the outer edge.
            int skirtStart = v;
            for (int s = 0; s < segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                float rad = Mathf.Lerp(1f, edge[s], 1f) * 0.5f;
                verts[v] = new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad);
                uv[v] = new Vector2(0.5f + Mathf.Cos(a) * rad, 0.5f + Mathf.Sin(a) * rad);
                v++;
            }

            var tris = new System.Collections.Generic.List<int>(rings * segments * 6);
            for (int s = 0; s < segments; s++)
            {
                int a = 1 + s, b = 1 + (s + 1) % segments;
                tris.Add(0); tris.Add(b); tris.Add(a);
            }
            for (int r = 0; r < rings - 1; r++)
            {
                int inner = 1 + r * segments, outer = 1 + (r + 1) * segments;
                for (int s = 0; s < segments; s++)
                {
                    int s2 = (s + 1) % segments;
                    tris.Add(inner + s); tris.Add(inner + s2); tris.Add(outer + s);
                    tris.Add(inner + s2); tris.Add(outer + s2); tris.Add(outer + s);
                }
            }

            // THE RIM, from the surface down to the floor. This is the waterline, and it is the whole
            // reason the spill has a depth at all - it is the only part of a puddle you can see edge-on.
            int lastRing = 1 + (rings - 1) * segments;
            for (int s = 0; s < segments; s++)
            {
                int s2 = (s + 1) % segments;
                tris.Add(lastRing + s); tris.Add(lastRing + s2); tris.Add(skirtStart + s);
                tris.Add(lastRing + s2); tris.Add(skirtStart + s2); tris.Add(skirtStart + s);
            }

            mesh.vertices = verts;
            mesh.uv = uv;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A WHIRLPOOL. Built as a funnel of revolution: the mouth is 1 across at the top, it narrows
        // to `throat` at the bottom, and every ring is TWISTED a little further round than the one
        // above it - which is what makes it read as spinning water rather than as a cone. The caller
        // scales it (see PoolDrain, which widens it with how much water is left).
        //
        // WHY IT IS NOT A SPINNING TEXTURE ON THE SURFACE. The one thing that says water is leaving a
        // room is the DIP - the surface stops being flat and goes down somewhere. A texture rotating
        // on a flat plane says the water is turning and nothing about where it is going.
        //
        // OPEN AT BOTH ENDS. There is no cap: the top opens onto the pool's own surface and the bottom
        // onto the drain, and a lid on either would be a visible disc where the water should be
        // continuous. The shader draws both faces (`Cull Off`), so the inside of the funnel is what
        // you see looking down it.
        public static Mesh Funnel(int rings, int segments, float throat, float swirl)
        {
            var mesh = new Mesh { name = "WaterFunnel" };

            var verts = new Vector3[rings * segments];
            var norms = new Vector3[verts.Length];
            var uv = new Vector2[verts.Length];

            int v = 0;
            for (int r = 0; r < rings; r++)
            {
                float t = r / (float)(rings - 1);          // 0 at the mouth, 1 at the throat
                // NARROWING FASTER THAN IT FALLS. A cone tapers linearly and reads as a funnel;
                // squaring it gives the flared bell a real vortex has, where most of the narrowing
                // happens in the last quarter.
                float radius = Mathf.Lerp(0.5f, throat, t * t);
                float y = -t;
                // Each ring turned further round than the last. This is the whole of the swirl.
                float twist = t * swirl;

                for (int seg = 0; seg < segments; seg++)
                {
                    float a = seg / (float)segments * Mathf.PI * 2f + twist;
                    verts[v] = new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius);
                    // Pointing out and slightly up, which is the wall of a funnel seen from inside.
                    norms[v] = new Vector3(Mathf.Cos(a), 0.35f, Mathf.Sin(a)).normalized;
                    uv[v] = new Vector2(seg / (float)segments, t);
                    v++;
                }
            }

            var tris = new System.Collections.Generic.List<int>(rings * segments * 6);
            for (int r = 0; r < rings - 1; r++)
            {
                int top = r * segments, bottom = (r + 1) * segments;
                for (int seg = 0; seg < segments; seg++)
                {
                    int s2 = (seg + 1) % segments;
                    tris.Add(top + seg); tris.Add(bottom + seg); tris.Add(top + s2);
                    tris.Add(top + s2); tris.Add(bottom + seg); tris.Add(bottom + s2);
                }
            }

            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uv;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A ROOM-SIZED SURFACE OF STANDING WATER - room2-5's landing room is flooded, and this is the
        // top of it. Centred on its own origin, facing up, `width` by `depth` metres.
        //
        // WHY IT IS SUBDIVIDED AT ALL, when the shape is a rectangle two triangles could describe: the
        // water shader displaces vertices along their normal (`_WobbleAmp`) and that is what stops the
        // surface being a mirror-flat pane. A quad has four vertices, all of them at the walls, so the
        // wobble moves the corners and nothing else - the middle of the pool, which is all anybody
        // looks at, stays perfectly flat. `cell` is how big a step of the grid is; at a quarter of a
        // metre a 9x10 room costs about 3,000 triangles, which is a twentieth of one chess piece.
        //
        // FLAT, NOT DOMED, unlike `Spill`. That dome is surface tension on a puddle a few centimetres
        // across; a pool the width of a room is held by its walls and is genuinely level.
        public static Mesh Sheet(float width, float depth, float cell)
        {
            var mesh = new Mesh { name = "WaterSheet" };

            int nx = Mathf.Max(1, Mathf.RoundToInt(width / Mathf.Max(0.02f, cell)));
            int nz = Mathf.Max(1, Mathf.RoundToInt(depth / Mathf.Max(0.02f, cell)));

            var verts = new Vector3[(nx + 1) * (nz + 1)];
            var norms = new Vector3[verts.Length];
            var uv = new Vector2[verts.Length];

            int v = 0;
            for (int z = 0; z <= nz; z++)
            {
                for (int x = 0; x <= nx; x++)
                {
                    float u = x / (float)nx, w = z / (float)nz;
                    verts[v] = new Vector3((u - 0.5f) * width, 0f, (w - 0.5f) * depth);
                    norms[v] = Vector3.up;
                    // UVs IN METRES, not normalised. The shader's noise is world-space, but anything
                    // later that samples a texture on this wants it at the same scale as the room
                    // rather than stretched across whatever size the sheet happens to be.
                    uv[v] = new Vector2(u * width, w * depth);
                    v++;
                }
            }

            var tris = new int[nx * nz * 6];
            int i = 0;
            for (int z = 0; z < nz; z++)
            {
                for (int x = 0; x < nx; x++)
                {
                    int a = z * (nx + 1) + x, b = a + 1, c = a + nx + 1, d = c + 1;
                    tris[i++] = a; tris[i++] = c; tris[i++] = b;
                    tris[i++] = b; tris[i++] = c; tris[i++] = d;
                }
            }

            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
