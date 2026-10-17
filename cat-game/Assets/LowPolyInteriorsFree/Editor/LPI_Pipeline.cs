// Low Poly Interiors - render pipeline setup.
//
// The pack's three materials ship on Unity's built-in Standard shader,
// because that is the only shader whose GUID is fixed for every install
// and can therefore be written into a .mat file by a build script that
// has never seen the buyer's project. In URP or HDRP that shader does not
// exist and every model imports magenta.
//
// So the materials are RE-SHADED here, in the project, where Shader.Find
// can actually see what is installed. It runs by itself the first time
// the pack is imported into a non-built-in project, and can be re-run by
// hand from the menu if the project changes pipeline later:
//
//   Tools > Low Poly Interiors > Set Up Materials For Current Pipeline
//
// Nothing in this file references a URP or HDRP type, so it compiles in a
// plain built-in project with neither package installed. The one HDRP
// call that has no built-in equivalent - resetting the material keywords
// after a shader swap - is made through reflection.
//
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LowPolyInteriors
{
    [InitializeOnLoad]
    public static class LPI_Pipeline
    {
        const string Root = "Assets/LowPolyInteriorsFree";
        const string MatDir = Root + "/Materials";
        const string DoneKey = "LPI_PipelineSetupDone_";

        public enum Pipe { BuiltIn, URP, HDRP, Unknown }

        static LPI_Pipeline()
        {
            // once per project, after the import has settled
            EditorApplication.delayCall += AutoRun;
        }

        static void AutoRun()
        {
            var pipe = Detect();
            string key = DoneKey + pipe;
            if (EditorPrefs.GetBool(key, false))
                return;
            if (!AssetDatabase.IsValidFolder(MatDir))
                return;                       // pack not imported (yet)
            EditorPrefs.SetBool(key, true);
            if (pipe == Pipe.BuiltIn)
                return;                       // already correct as shipped
            Debug.Log("[LPI] " + pipe + " detected - converting the pack's "
                      + "materials. Re-run from Tools > Low Poly Interiors.");
            Convert();
        }

        public static Pipe Detect()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null)
                return Pipe.BuiltIn;
            string n = rp.GetType().FullName ?? "";
            if (n.Contains("Universal"))
                return Pipe.URP;
            if (n.Contains("HighDefinition") || n.Contains("HDRenderPipeline"))
                return Pipe.HDRP;
            return Pipe.Unknown;
        }

        [MenuItem("Tools/Low Poly Interiors/Set Up Materials For Current "
                  + "Pipeline", false, 1)]
        public static void Convert()
        {
            var pipe = Detect();
            string shaderName =
                pipe == Pipe.URP ? "Universal Render Pipeline/Lit" :
                pipe == Pipe.HDRP ? "HDRP/Lit" : "Standard";
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError("[LPI] " + pipe + " is active but the shader \""
                               + shaderName + "\" is not installed.");
                return;
            }

            int done = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material",
                                                          new[] { MatDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                    continue;
                Reshade(m, shader, pipe);
                EditorUtility.SetDirty(m);
                done++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[LPI] " + done + " materials on " + shaderName
                      + " for " + pipe + ".");
        }

        // The three materials are named by what they do, which is the only
        // thing this needs to know: the glass one is transparent and the
        // emissive one glows.
        static void Reshade(Material m, Shader shader, Pipe pipe)
        {
            bool glass = m.name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0;
            bool emis = m.name.IndexOf("Emissive", StringComparison.OrdinalIgnoreCase) >= 0;

            // read the built-in properties BEFORE the shader swap wipes
            // whatever the new shader does not declare
            Texture tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex")
                        : m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap")
                        : null;
            Color col = m.HasProperty("_Color") ? m.GetColor("_Color")
                      : m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                      : Color.white;
            Color emc = m.HasProperty("_EmissionColor")
                      ? m.GetColor("_EmissionColor") : Color.black;

            m.shader = shader;

            SetTex(m, tex, "_BaseMap", "_BaseColorMap", "_MainTex");
            SetCol(m, col, "_BaseColor", "_Color");
            SetF(m, 0f, "_Metallic");
            SetF(m, glass ? 0.88f : 0.18f, "_Smoothness", "_Glossiness");

            if (glass)
            {
                // URP: _Surface 1 = transparent, _Blend 0 = alpha.
                // HDRP: _SurfaceType 1, _BlendMode 0. Setting both is
                // harmless - SetF only writes properties the shader has.
                SetF(m, 1f, "_Surface", "_SurfaceType");
                SetF(m, 0f, "_Blend", "_BlendMode");
                SetF(m, 0f, "_ZWrite");
                SetF(m, 0f, "_AlphaCutoffEnable");
                SetF(m, (float)BlendMode.SrcAlpha, "_SrcBlend");
                SetF(m, (float)BlendMode.OneMinusSrcAlpha, "_DstBlend");
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                SetF(m, 0f, "_Surface", "_SurfaceType");
                SetF(m, 1f, "_ZWrite");
                m.SetOverrideTag("RenderType", "Opaque");
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = -1;
            }

            if (emis)
            {
                m.EnableKeyword("_EMISSION");
                SetCol(m, emc, "_EmissionColor");
                // HDRP calls it something else and wants an intensity
                SetCol(m, emc, "_EmissiveColor");
                SetTex(m, tex, "_EmissiveColorMap");
                SetF(m, 1f, "_UseEmissiveIntensity");
                SetF(m, 1f, "_EmissiveIntensity");
                m.globalIlluminationFlags =
                    MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.globalIlluminationFlags =
                    MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            if (pipe == Pipe.HDRP)
                HdrpValidate(m);
        }

        static void SetTex(Material m, Texture t, params string[] names)
        {
            if (t == null) return;
            foreach (var n in names)
                if (m.HasProperty(n)) m.SetTexture(n, t);
        }

        static void SetCol(Material m, Color c, params string[] names)
        {
            foreach (var n in names)
                if (m.HasProperty(n)) m.SetColor(n, c);
        }

        static void SetF(Material m, float v, params string[] names)
        {
            foreach (var n in names)
                if (m.HasProperty(n)) m.SetFloat(n, v);
        }

        // HDRP keeps a pile of keywords and passes in sync with the
        // material's properties and will render it wrong until they are
        // reset. The call lives in the HDRP package, so it is made through
        // reflection - this file has to compile with no HDRP installed.
        static void HdrpValidate(Material m)
        {
            var t = Type.GetType(
                "UnityEditor.Rendering.HighDefinition.HDShaderUtils, "
                + "Unity.RenderPipelines.HighDefinition.Editor");
            if (t == null) return;
            var mi = t.GetMethod("ResetMaterialKeywords",
                                 BindingFlags.Public | BindingFlags.Static);
            if (mi != null) mi.Invoke(null, new object[] { m });
        }
    }
}
