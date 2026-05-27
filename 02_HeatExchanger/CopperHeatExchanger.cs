// SPDX-License-Identifier: CC0-1.0
//
// Additively manufactured copper Gyroid heat exchanger
// Specification:
//   - Cooling power   : 1000 W
//   - Fluid pair      : Water / Water, counter-flow
//   - Hot side        : 80 °C → 60 °C
//   - Cold side       : 20 °C → 40 °C
//   - Overall HTC k   : 3000 W/(m²·K)  [conservative W/W]
//   - Material        : Cu (λ = 400 W/(m·K)), LPBF
//   - Bounding box    : 50 × 50 × 30 mm
//   - Lattice type    : Schwartz–Gyroid TPMS (self-supporting ≥ 45°)

using PicoGK;
using System.Numerics;

namespace PicoGKExamples
{
    class CopperHeatExchanger
    {
        // ── Thermodynamic parameters ──────────────────────────────────────────────
        const float Q_W      = 1000f;   // required cooling power [W]
        const float K_OVER   = 3000f;   // overall heat transfer coeff. [W/(m²·K)]
        const float T_HI_IN  =   80f;   // hot side inlet  [°C]
        const float T_HI_OUT =   60f;   // hot side outlet [°C]
        const float T_CO_IN  =   20f;   // cold side inlet [°C]
        const float T_CO_OUT =   40f;   // cold side outlet [°C]

        // ── Bounding box [mm] ─────────────────────────────────────────────────────
        const float BOX_X = 50f;
        const float BOX_Y = 50f;
        const float BOX_Z = 30f;

        // ── Geometry / manufacturing parameters [mm] ──────────────────────────────
        // Cell size chosen so 5×5×3 = 75 full cells fit within the bounding box.
        // Gyroid surface area ≈ 3.091 · V_box / L_cell ≈ 231 cm² >> 83 cm² required.
        const float CELL_MM  = 10f;   // Gyroid period L
        const float WALL_MM  =  0.5f; // copper wall half-thickness (full wall = 1 mm)
        const float SHELL_MM =  2.0f; // outer housing wall thickness

        // ── Port / connection stub parameters [mm] ────────────────────────────────
        // Layout: hot fluid enters/exits on X faces, cold fluid on Y faces (counter-flow)
        const float PORT_R_IN  =  3.5f; // bore inner radius  → 7 mm inner ⌀
        const float PORT_R_OUT =  5.5f; // stub outer radius  → 11 mm outer ⌀, 4 mm wall
        const float PORT_STUB  = 12.0f; // protrusion beyond outer shell face [mm]

        // ─────────────────────────────────────────────────────────────────────────
        // Schwartz–Gyroid TPMS implicit surface
        //
        //   g(x,y,z) = cos(kx)·sin(ky) + cos(ky)·sin(kz) + cos(kz)·sin(kx)
        //   Solid copper: |g| / |∇g| < WALL_MM   (true Euclidean SDF)
        //
        // The Gyroid is a minimal surface (H = 0 everywhere), which means its
        // normals vary continuously through all angles — making it self-supporting
        // for LPBF without internal support structures.
        // ─────────────────────────────────────────────────────────────────────────
        class GyroidSolid : IImplicit
        {
            readonly float m_k;        // angular wavenumber = 2π / cellMM
            readonly float m_tOffset;  // level-set threshold (Gyroid-function units)

            public GyroidSolid(float cellMM, float wallHalfMM)
            {
                m_k      = 2f * MathF.PI / cellMM;
                // The Gyroid gradient magnitude averages to k·√(3/2).
                // Multiplying wallHalfMM by this factor converts from mm to
                // the level-set domain, giving an approximately mm-accurate SDF.
                m_tOffset = wallHalfMM * m_k * MathF.Sqrt(1.5f);
            }

            public float fSignedDistance(in Vector3 v)
            {
                float kx = m_k * v.X;
                float ky = m_k * v.Y;
                float kz = m_k * v.Z;

                // Gyroid function value
                float g = MathF.Cos(kx) * MathF.Sin(ky)
                        + MathF.Cos(ky) * MathF.Sin(kz)
                        + MathF.Cos(kz) * MathF.Sin(kx);

                // Analytical gradient for proper Euclidean SDF normalisation
                float gx = m_k * (-MathF.Sin(kx) * MathF.Sin(ky)
                                 +  MathF.Cos(kz) * MathF.Cos(kx));
                float gy = m_k * ( MathF.Cos(kx) * MathF.Cos(ky)
                                 - MathF.Sin(ky)  * MathF.Sin(kz));
                float gz = m_k * ( MathF.Cos(ky) * MathF.Cos(kz)
                                 - MathF.Sin(kz)  * MathF.Sin(kx));

                float gradMag = MathF.Sqrt(gx * gx + gy * gy + gz * gz);

                // SDF: negative inside copper, positive inside fluid channels
                return (MathF.Abs(g) - m_tOffset) / MathF.Max(gradMag, 1e-6f);
            }
        }

        // Euclidean signed distance to an axis-aligned box (negative = inside)
        class BoxSDF : IImplicit
        {
            readonly Vector3 m_min, m_max;

            public BoxSDF(Vector3 min, Vector3 max) { m_min = min; m_max = max; }

            public float fSignedDistance(in Vector3 v)
            {
                // Per-axis penetration (positive = outside on that axis)
                float qx = MathF.Max(m_min.X - v.X, v.X - m_max.X);
                float qy = MathF.Max(m_min.Y - v.Y, v.Y - m_max.Y);
                float qz = MathF.Max(m_min.Z - v.Z, v.Z - m_max.Z);

                float outsideDist = MathF.Sqrt(
                    MathF.Max(qx, 0f) * MathF.Max(qx, 0f) +
                    MathF.Max(qy, 0f) * MathF.Max(qy, 0f) +
                    MathF.Max(qz, 0f) * MathF.Max(qz, 0f));

                float insideDist = MathF.Min(MathF.Max(qx, MathF.Max(qy, qz)), 0f);

                return outsideDist + insideDist;
            }
        }

        // Finite cylinder SDF with flat end caps (negative = inside solid)
        class CylinderSDF : IImplicit
        {
            readonly Vector3 m_p0, m_axis;
            readonly float   m_len, m_r;

            public CylinderSDF(Vector3 p0, Vector3 p1, float r)
            {
                m_p0   = p0;
                m_axis = Vector3.Normalize(p1 - p0);
                m_len  = (p1 - p0).Length();
                m_r    = r;
            }

            public float fSignedDistance(in Vector3 v)
            {
                Vector3 dp     = v - m_p0;
                float   t      = Vector3.Dot(dp, m_axis);
                float   radial = (dp - m_axis * t).Length() - m_r;
                float   axial  = MathF.Max(-t, t - m_len);
                return MathF.Max(radial, axial);
            }
        }

        // Adds one hollow port stub to voxHX.
        // vecFace  = where the bore opens into the Gyroid interior (inner shell face)
        // vecTip   = outer end of the stub (where a fitting is connected)
        static void AddPort(Voxels voxHX, Vector3 vecFace, Vector3 vecTip)
        {
            BBox3 bbox = new BBox3();
            bbox.Include(vecFace);
            bbox.Include(vecTip);
            bbox.Grow(PORT_R_OUT + 1f);

            // Solid copper stub
            var implStub = new CylinderSDF(vecFace, vecTip, PORT_R_OUT);
            var voxStub  = new Voxels(implStub, bbox);
            voxHX.BoolAdd(voxStub);

            // Hollow bore (same extent — subtracted after all stubs are added)
            BBox3 bboxBore = new BBox3();
            bboxBore.Include(vecFace);
            bboxBore.Include(vecTip);
            bboxBore.Grow(PORT_R_IN + 1f);

            var implBore = new CylinderSDF(vecFace, vecTip, PORT_R_IN);
            var voxBore  = new Voxels(implBore, bboxBore);
            voxHX.BoolSubtract(voxBore);
        }

        // ─────────────────────────────────────────────────────────────────────────
        public static void Task()
        {
            // ── 1. Thermodynamic design ───────────────────────────────────────────
            //
            // Counter-flow LMTD:
            //   ΔT₁ = T_hot_in  − T_cold_out = 80 − 40 = 40 K
            //   ΔT₂ = T_hot_out − T_cold_in  = 60 − 20 = 40 K
            //   ΔT_lm = (ΔT₁ − ΔT₂) / ln(ΔT₁/ΔT₂)  → L'Hôpital → 40 K
            //
            //   A_req = Q / (k · ΔT_lm)  [m²]
            //
            float dT1 = T_HI_IN  - T_CO_OUT;
            float dT2 = T_HI_OUT - T_CO_IN;
            float dTlm = (MathF.Abs(dT1 - dT2) < 0.001f)
                         ? dT1
                         : (dT1 - dT2) / MathF.Log(dT1 / dT2);

            float A_req_m2  = Q_W / (K_OVER * dTlm);
            float A_req_mm2 = A_req_m2 * 1e6f;

            // Theoretical Gyroid surface in bounding box (Schoen's constant: 3.091)
            //   A_gyroid = 3.091 · V_box / L_cell
            float vBox    = BOX_X * BOX_Y * BOX_Z;
            float A_gyroid_mm2 = 3.091f * vBox / CELL_MM;

            Library.Log("=== Copper Heat Exchanger — Design Report ===");
            Library.Log($"  Cooling power      : {Q_W:F0} W");
            Library.Log($"  Hot side           : {T_HI_IN} °C → {T_HI_OUT} °C");
            Library.Log($"  Cold side          : {T_CO_IN} °C → {T_CO_OUT} °C");
            Library.Log($"  ΔT_lm (counter-fl.): {dTlm:F1} K");
            Library.Log($"  k_overall          : {K_OVER:F0} W/(m²·K)");
            Library.Log($"  Required area      : {A_req_mm2 / 100f:F1} cm²");
            Library.Log($"  Gyroid area (theor): {A_gyroid_mm2 / 100f:F1} cm²  (L={CELL_MM} mm)");
            Library.Log($"  Area safety margin : {A_gyroid_mm2 / A_req_mm2:F2}×");
            Library.Log($"  Bounding box       : {BOX_X} × {BOX_Y} × {BOX_Z} mm");
            Library.Log($"  Wall thickness     : {WALL_MM * 2f:F1} mm (Cu, λ=400 W/(m·K))");
            Library.Log($"  Outer shell        : {SHELL_MM:F1} mm");

            // ── 2. Geometry ───────────────────────────────────────────────────────
            var innerMin = Vector3.Zero;
            var innerMax = new Vector3(BOX_X, BOX_Y, BOX_Z);

            var outerMin = innerMin - new Vector3(SHELL_MM, SHELL_MM, SHELL_MM);
            var outerMax = innerMax + new Vector3(SHELL_MM, SHELL_MM, SHELL_MM);

            var innerBbox = new BBox3(innerMin, innerMax);
            var outerBbox = new BBox3(outerMin, outerMax);

            // Gyroid TPMS lattice — copper walls separating the two fluid networks
            var gyroidImpl = new GyroidSolid(CELL_MM, WALL_MM);
            var voxGyroid  = new Voxels(gyroidImpl, innerBbox);

            // Outer housing: solid box minus inner cavity → 1 mm copper shell
            var outerBoxImpl = new BoxSDF(outerMin, outerMax);
            var innerBoxImpl = new BoxSDF(innerMin, innerMax);
            var voxOuter = new Voxels(outerBoxImpl, outerBbox);
            var voxInner = new Voxels(innerBoxImpl, innerBbox);
            var voxShell = voxOuter - voxInner;

            // Combine: Gyroid core + sealed housing
            var voxHX = voxGyroid + voxShell;

            // ── 3. Connection ports ───────────────────────────────────────────────
            // Counter-flow layout:
            //   Hot  fluid : enters X=0 face  (H_IN)  → exits X=BOX_X face (H_OUT)
            //   Cold fluid : enters Y=BOX_Y face (C_IN) → exits Y=0 face   (C_OUT)
            //
            // vecFace = inner shell face (opens into Gyroid interior)
            // vecTip  = outer stub end   (where a fitting/tube is attached)
            //
            float stubTotal = SHELL_MM + PORT_STUB;  // stub reaches this far beyond inner face
            float cY = BOX_Y / 2f;
            float cX = BOX_X / 2f;
            float cZ = BOX_Z / 2f;

            // Hot inlet  — left X face
            AddPort(voxHX,
                vecFace: new Vector3(0f,     cY, cZ),
                vecTip:  new Vector3(-stubTotal, cY, cZ));

            // Hot outlet — right X face
            AddPort(voxHX,
                vecFace: new Vector3(BOX_X,          cY, cZ),
                vecTip:  new Vector3(BOX_X + stubTotal, cY, cZ));

            // Cold inlet  — back Y face
            AddPort(voxHX,
                vecFace: new Vector3(cX, BOX_Y,           cZ),
                vecTip:  new Vector3(cX, BOX_Y + stubTotal, cZ));

            // Cold outlet — front Y face
            AddPort(voxHX,
                vecFace: new Vector3(cX, 0f,         cZ),
                vecTip:  new Vector3(cX, -stubTotal,  cZ));

            Library.Log($"  Port outer ⌀       : {PORT_R_OUT * 2f:F0} mm  (bore ⌀ {PORT_R_IN * 2f:F0} mm)");
            Library.Log($"  Port stub length   : {PORT_STUB:F0} mm beyond shell");

            // ── 5. Properties ─────────────────────────────────────────────────────
            voxHX.CalculateProperties(out float fVolMm3, out BBox3 oBBox);
            Library.Log($"  Solid Cu volume    : {fVolMm3 / 1000f:F2} cm³");
            Library.Log($"  Actual bounds      : {oBBox}");

            // ── 6. Viewer ─────────────────────────────────────────────────────────
            // Copper colour: #B87333, high metallic, low roughness
            Library.oViewer().SetGroupMaterial(0, "B87333", 0.9f, 0.15f);
            Library.oViewer().Add(voxHX);

            // ── 7. STL export ─────────────────────────────────────────────────────
            var msh = new Mesh(voxHX);
            string strPath = Path.Combine(Library.strLogFolder, "CopperHeatExchanger.stl");
            msh.SaveToStlFile(strPath);
            Library.Log($"  STL saved to       : {strPath}");
        }
    }
}
