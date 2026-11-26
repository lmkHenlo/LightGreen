using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace LightGreen.Systems
{
    /// <summary>
    /// The scene's depth without the grass blades, drawn once a frame for ambient occlusion
    /// (always, while it's on). HTrace AO works its occlusion out from the scene's depth. With the
    /// game's grass in it, thousands of thin blades shade one another and open grass is a field of dark specks that
    /// no averaging evens out. Given this depth instead, the occlusion is the ground's own: even over open grass, a
    /// soft dark pool at the foot of carts, posts and buildings, and the blades standing in it are darkened with the
    /// ground under them when HTrace lays it over the image. HTrace reads URP's camera depth texture in its first pass
    /// (and binds it for the rest of its work, normals rebuilt from depth included); for the length of that pass it
    /// reads this one instead. Everything else still gets URP's real depth.
    ///
    /// <para><b>How grass is left out:</b> by its draw order. The game draws its grass (one instanced draw per
    /// terrain, through TerrainGrassRenderer) with its own copy of the grass material; while this is on, those copies
    /// are drawn at render queue <see cref="GrassQueue"/>, which nothing else in the game uses, and the depth draw
    /// takes every opaque queue but that one. Opaque objects are sorted by queue first, so the grass still draws after
    /// the ground it stands on, as it did at the grass shader's own queue (Geometry+100). The copies get their own
    /// queue back when it's off.</para>
    ///
    /// <para>The draw is each opaque object's own depth-and-normals pass (see <see cref="DepthPasses"/>; the game's
    /// shaders, nothing of LightGreen's), recorded just before HTrace's first pass. It costs about what URP's own
    /// depth-normals prepass would, without the grass.
    /// Each camera has its own texture, so cameras of different sizes don't make it reallocate every frame.</para>
    ///
    /// <para>Not for stereo (VR) cameras: there URP draws both eyes at once into a texture array, which this isn't
    /// set up for (ambient occlusion is off in VR anyway).</para>
    /// </summary>
    internal static class GrassFreeDepth
    {
        /// <summary>The grass copies' queue while this is on: Geometry+137, otherwise unused.</summary>
        internal const int GrassQueue = 2137;

        private static readonly Type GrassType = AccessTools.TypeByName("TerrainGrassRenderer");
        private static readonly FieldInfo GrassMaterialField =
            GrassType == null ? null : AccessTools.Field(GrassType, "materialInstance");
        private static readonly MethodInfo DepthSetter =
            AccessTools.PropertySetter(typeof(UniversalResourceData), nameof(UniversalResourceData.cameraDepthTexture));

        private static readonly HashSet<Material> Grass = new HashSet<Material>();
        private static readonly Dictionary<Material, int> OriginalQueue = new Dictionary<Material, int>();
        /// <summary>
        /// The passes drawn into the depth. The game's toon shader (roads, buildings, most props) has no DepthOnly
        /// pass, only DepthNormals; terrain, shader graphs and URP's Lit have both, URP's Unlit has DepthNormalsOnly,
        /// and a few simple shaders (Unlit/Color, the base plane) only their one unlit pass. Every opaque surface the
        /// camera draws has one of these, so nothing is missing from the depth (a missing road let the occlusion see
        /// what lies under it). The normals these passes write go to a scratch texture that is thrown away.
        /// </summary>
        private static readonly List<ShaderTagId> DepthPasses = new List<ShaderTagId>
        {
            new ShaderTagId("DepthNormals"),
            new ShaderTagId("DepthNormalsOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        private static readonly GraphicsFormat NormalsFormat =
            SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SNorm, GraphicsFormatUsage.Render)
                ? GraphicsFormat.R8G8B8A8_SNorm
                : GraphicsFormat.R16G16B16A16_SFloat;
        private static readonly ProfilingSampler Sampler = new ProfilingSampler("LightGreen depth without grass");

        private static readonly Dictionary<Camera, RTHandle> Depths = new Dictionary<Camera, RTHandle>();
        private static readonly List<Camera> Gone = new List<Camera>();
        private static bool _forOcclusion, _active;
        private static bool _swapped;
        private static TextureHandle _cameraDepth;
        private static bool _failureLogged;

        // The depth drawn for the camera being recorded, so it's drawn once per render of that camera. A texture
        // handle is only good inside the render graph that made it, so this is let go as each camera starts
        // rendering: a camera rendered twice in a frame gets a new one the second time.
        private static Camera _drawnCamera;
        private static TextureHandle _drawn;
        private static bool _listening;

        /// <summary>Set by Plugin when the hooks applied.</summary>
        internal static bool GrassHooked, HTraceHooked;

        /// <summary>Called every frame by ambient occlusion: on while it's on and the setting allows.</summary>
        internal static void ForOcclusion(bool on)
        {
            on &= HTraceHooked && DepthSetter != null;
            if (on == _forOcclusion) return;
            _forOcclusion = on;
            Refresh();
        }

        private static void Refresh()
        {
            bool on = _forOcclusion && GrassHooked && !_failureLogged;
            if (on) MarkAll();
            Listen(on);
            if (on == _active) return;
            _active = on;
            if (!on) UnmarkAll();
            // Logged when it first takes effect, not on every ToggleKey press.
            if (on) Plugin.LogChange("ao-grass", "Ambient occlusion: worked out without the grass blades.");
        }

        internal static void Destroy()
        {
            _forOcclusion = false;
            Refresh();
            foreach (RTHandle handle in Depths.Values) handle?.Release();
            Depths.Clear();
        }

        private static void Listen(bool on)
        {
            if (on == _listening) return;
            _listening = on;
            if (on) RenderPipelineManager.beginCameraRendering += ForgetDrawn;
            else RenderPipelineManager.beginCameraRendering -= ForgetDrawn;
            _drawnCamera = null;
        }

        private static void ForgetDrawn(ScriptableRenderContext context, Camera camera)
        {
            _drawnCamera = null;
            _drawn = TextureHandle.nullHandle;
        }

        // ---- Grass draw order ------------------------------------------------------------------------------

        private static void MarkAll()
        {
            Grass.RemoveWhere(m => m == null);
            foreach (Material m in Grass) Mark(m);
        }

        private static void Mark(Material m)
        {
            if (m.renderQueue == GrassQueue) return;
            if (!OriginalQueue.ContainsKey(m)) OriginalQueue[m] = m.renderQueue;
            m.renderQueue = GrassQueue;
        }

        private static void UnmarkAll()
        {
            foreach (KeyValuePair<Material, int> entry in OriginalQueue)
                if (entry.Key != null && entry.Key.renderQueue == GrassQueue) entry.Key.renderQueue = entry.Value;
            OriginalQueue.Clear();
        }

        /// <summary>Notes each terrain's grass material copy as the game makes it (when the terrain is enabled).</summary>
        [HarmonyPatch]
        internal static class GrassPatch
        {
            private static MethodBase TargetMethod() =>
                GrassType == null ? null : AccessTools.Method(GrassType, "UpdateRenderParams");

            private static void Postfix(object __instance)
            {
                if (GrassMaterialField == null) return;
                if (!(GrassMaterialField.GetValue(__instance) is Material m) || m == null) return;
                Grass.Add(m);
                if (_active) Mark(m);
            }
        }

        // ---- The depth draw --------------------------------------------------------------------------------

        /// <summary>A camera drawn to a headset (see the class summary).</summary>
        private static bool IsStereo(Camera camera) => camera.stereoEnabled || Vr.IsRunning;

        /// <summary>The camera's own depth texture, kept between frames; textures of destroyed cameras are freed.</summary>
        private static RTHandle DepthFor(Camera camera, RenderTextureDescriptor desc)
        {
            if (Depths.Count > 1)
            {
                Gone.Clear();
                foreach (Camera c in Depths.Keys)
                    if (c == null) Gone.Add(c);
                foreach (Camera c in Gone)
                {
                    Depths[c]?.Release();
                    Depths.Remove(c);
                }
            }
            Depths.TryGetValue(camera, out RTHandle handle);
            RenderingUtils.ReAllocateHandleIfNeeded(ref handle, desc, FilterMode.Point, TextureWrapMode.Clamp,
                name: "LightGreen depth without grass");
            Depths[camera] = handle;
            return handle;
        }

        /// <summary>
        /// HTrace's first pass each frame (it binds the depth and rebuilds normals from it). For as long as HTrace
        /// records it, URP's camera depth texture handle points at the depth without grass; it points back
        /// afterwards, so everything else still gets the real depth.
        /// </summary>
        [HarmonyPatch]
        internal static class HTracePatch
        {
            private static MethodBase TargetMethod()
            {
                Type pass = AccessTools.TypeByName("HTraceAO.Rendering.URP.Passes.GBufferPassURP");
                return pass == null ? null : AccessTools.Method(pass, "RecordRenderGraph");
            }

            private static void Prefix(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (!_active || !_forOcclusion) return;
                try
                {
                    var resources = frameData.Get<UniversalResourceData>();
                    TextureHandle cameraDepth = resources.cameraDepthTexture;
                    if (!cameraDepth.IsValid()) return;
                    TextureHandle depth = Draw(renderGraph, frameData);
                    if (!depth.IsValid()) return;
                    _cameraDepth = cameraDepth;
                    _swapped = true;
                    DepthSetter.Invoke(resources, new object[] { depth });
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }

            private static Exception Finalizer(Exception __exception, ContextContainer frameData)
            {
                if (_swapped)
                {
                    _swapped = false;
                    try
                    {
                        DepthSetter.Invoke(frameData.Get<UniversalResourceData>(), new object[] { _cameraDepth });
                    }
                    catch (Exception e)
                    {
                        Fail(e);
                    }
                }
                return __exception;
            }
        }

        private sealed class PassData
        {
            internal RendererListHandle Below, Above;
        }

        /// <summary>
        /// The depth without grass for the camera being recorded: drawn the first time it's asked for, the same
        /// handle after that. Invalid for cameras other than the game's.
        /// </summary>
        private static TextureHandle Draw(RenderGraph renderGraph, ContextContainer frameData)
        {
            var camera = frameData.Get<UniversalCameraData>();
            if (camera.cameraType != CameraType.Game || camera.camera == null || IsStereo(camera.camera))
                return TextureHandle.nullHandle;
            if (_drawnCamera != null && _drawnCamera == camera.camera) return _drawn;

            var rendering = frameData.Get<UniversalRenderingData>();
            var lights = frameData.Get<UniversalLightData>();

            // A texture of its own, kept between frames, so the render graph never hands its memory to anything
            // else while HTrace still reads it through its own bindings.
            RenderTextureDescriptor desc = camera.cameraTargetDescriptor;
            desc.graphicsFormat = GraphicsFormat.None;
            desc.depthStencilFormat = GraphicsFormat.D32_SFloat;
            desc.msaaSamples = 1;
            desc.bindMS = false;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;
            desc.enableRandomWrite = false;
            TextureHandle depth = renderGraph.ImportTexture(DepthFor(camera.camera, desc));

            // The normals the passes write, unused: transient, so the render graph reuses its memory.
            TextureHandle normals = renderGraph.CreateTexture(new TextureDesc(desc.width, desc.height)
            {
                format = NormalsFormat,
                name = "LightGreen normals (unused)",
                clearBuffer = false,
            });

            DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(DepthPasses, rendering, camera, lights,
                                                                           SortingCriteria.CommonOpaque);
            RendererListHandle below = renderGraph.CreateRendererList(new RendererListParams(rendering.cullResults,
                drawing, new FilteringSettings(new RenderQueueRange(RenderQueueRange.opaque.lowerBound, GrassQueue - 1))));
            RendererListHandle above = renderGraph.CreateRendererList(new RendererListParams(rendering.cullResults,
                drawing, new FilteringSettings(new RenderQueueRange(GrassQueue + 1, RenderQueueRange.opaque.upperBound))));

            using (IRasterRenderGraphBuilder builder =
                   renderGraph.AddRasterRenderPass("LightGreen depth without grass", out PassData data, Sampler))
            {
                data.Below = below;
                data.Above = above;
                builder.UseRendererList(below);
                builder.UseRendererList(above);
                builder.SetRenderAttachment(normals, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(RTClearFlags.Depth, Color.clear, 1f, 0);
                    context.cmd.DrawRendererList(d.Below);
                    context.cmd.DrawRendererList(d.Above);
                });
            }

            _drawnCamera = camera.camera;
            _drawn = depth;
            return depth;
        }

        /// <summary>Turns this off for the session (grass back in the depth everywhere) and says why, once.</summary>
        private static void Fail(Exception e)
        {
            if (_failureLogged) return;
            _failureLogged = true;
            Plugin.Log.LogError($"The depth without grass failed, so it's off until the game restarts: {e}");
            Refresh();
        }
    }
}
