using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Windows.Media;

namespace Hexprite.Rendering
{
    /// <summary>
    /// Performs realistic display simulation rendering (OLED tinting, blur, bloom, and noise)
    /// to mimic the look of physical displays on a canvas.
    /// </summary>
    public static class DisplaySimulationRenderer
    {
        private static readonly ConcurrentDictionary<(int Radius, int SigmaMilli), float[]> GaussianKernelCache = new();
        private static float SrgbToLinear(float c)
            => c <= 0.04045f ? (c / 12.92f) : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

        private static float LinearToSrgb(float c)
            => c <= 0.0031308f ? (12.92f * c) : (1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f);

        /// <summary>
        /// Applies OLED color temperature tinting to foreground color.
        /// Blue OLED reduces red/green; Green OLED reduces red/blue.
        /// </summary>
        private static void ApplyOledColorTinting(ref (float r, float g, float b) fgLin, DisplaySimulationPreset preset, float strength)
        {
            if (strength <= 0f) return;

            if (preset == DisplaySimulationPreset.Ssd1306OledBlue)
            {
                fgLin.r *= 1f - 0.12f * strength;
                fgLin.g *= 1f - 0.06f * strength;
            }
            else if (preset == DisplaySimulationPreset.Ssd1306OledGreen)
            {
                fgLin.r *= 1f - 0.15f * strength;
                fgLin.b *= 1f - 0.18f * strength;
            }
            else if (preset is DisplaySimulationPreset.Ssd1306OledWhite or DisplaySimulationPreset.GenericLcd)
            {
                fgLin.r *= 1f - 0.02f * strength;
                fgLin.g *= 1f - 0.01f * strength;
            }
        }

        private static uint PackBgra(byte a, byte r, byte g, byte b)
            => (uint)((a << 24) | (r << 16) | (g << 8) | b);

        /// <summary>
        /// Standard smoothstep: returns 0 when x &lt;= edge0, 1 when x &gt;= edge1.
        /// Always call with edge0 &lt; edge1 to avoid a sign-flip in the denominator.
        /// </summary>
        private static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        private static float Hash01(int x, int y)
        {
            // Deterministic hash -> [0,1)
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= (h >> 16);
                return (h & 0x00FFFFFF) / 16777216f;
            }
        }

        /// <summary>
        /// Renders the sprite layers with display simulation effects into the output buffer.
        /// </summary>
        /// <param name="srcW">Source canvas width.</param>
        /// <param name="srcH">Source canvas height.</param>
        /// <param name="layers">List of layers to render.</param>
        /// <param name="colorMode">Reserved for future color-mode support.</param>
        /// <param name="selectionService">Optional selection service to handle floating layer rendering.</param>
        /// <param name="pasteMode">Determines how floating pixels are stamped.</param>
        /// <param name="outW">Output buffer width.</param>
        /// <param name="outH">Output buffer height.</param>
        /// <param name="bgSrgb">Background color.</param>
        /// <param name="fgSrgb">Foreground color.</param>
        /// <param name="preset">Target display preset.</param>
        /// <param name="quality">Rendering quality level.</param>
        /// <param name="strength01">Strength of simulation effects (0.0 to 1.0).</param>
        /// <param name="effectiveScale">Visual scale factor.</param>
        /// <param name="isScaleCapped">Whether scale is capped to prevent instability.</param>
        /// <param name="outBgra">The output buffer to write rendered BGRA pixels into.</param>
        public static void Render(
            int srcW,
            int srcH,
            System.Collections.Generic.IReadOnlyList<LayerState> layers,
            System.Collections.Generic.IReadOnlyList<Hexprite.Core.IPixelBuffer> layerPixels,
            ColorMode colorMode,
            ISelectionService? selectionService,
            FloatingPasteMode pasteMode,
            int outW,
            int outH,
            Color bgSrgb,
            Color fgSrgb,
            DisplaySimulationPreset preset,
            PreviewQuality quality,
            double strength01,
            double effectiveScale,
            bool isScaleCapped,
            uint[] outBgra)
        {
            ArgumentNullException.ThrowIfNull(layers);
            ArgumentNullException.ThrowIfNull(layerPixels);
            ArgumentNullException.ThrowIfNull(outBgra);
            if (outW <= 0 || outH <= 0) return;
            if (outBgra.Length < outW * outH) return;
            if (srcW <= 0 || srcH <= 0) return;

            float strength = (float)Math.Clamp(strength01, 0.0, 1.0);

            float baseEdgeSoftnessPx = preset switch
            {
                DisplaySimulationPreset.Ssd1306OledBlue or DisplaySimulationPreset.Ssd1306OledGreen => 0.22f,
                DisplaySimulationPreset.Ssd1306OledWhite or DisplaySimulationPreset.GenericLcd => 0.20f,
                DisplaySimulationPreset.EPaper => 0.15f,
                DisplaySimulationPreset.FlipperZeroLcd => 0.12f,
                _ => 0.20f,
            };

            float blurSigma = quality switch
            {
                PreviewQuality.Fast => 0f,
                PreviewQuality.High => 0.85f,
                _ => 0.55f,
            };

            float bloomSigma = quality switch
            {
                PreviewQuality.High => 1.6f,
                PreviewQuality.Balanced => 0.9f,
                _ => 0f,
            };

            float blurStrength = preset switch
            {
                DisplaySimulationPreset.Ssd1306OledBlue or DisplaySimulationPreset.Ssd1306OledGreen => 0.60f,
                DisplaySimulationPreset.Ssd1306OledWhite or DisplaySimulationPreset.GenericLcd => 0.55f,
                DisplaySimulationPreset.EPaper => 0.25f,
                DisplaySimulationPreset.FlipperZeroLcd => 0.04f,
                _ => 0.45f,
            } * strength;

            float bloomStrength = preset switch
            {
                DisplaySimulationPreset.Ssd1306OledGreen => 0.38f,
                DisplaySimulationPreset.Ssd1306OledBlue => 0.35f,
                DisplaySimulationPreset.Ssd1306OledWhite or DisplaySimulationPreset.GenericLcd => 0.28f,
                _ => 0.0f,
            } * strength;

            // Colors in linear
            var bgLin = (
                r: SrgbToLinear(bgSrgb.R / 255f),
                g: SrgbToLinear(bgSrgb.G / 255f),
                b: SrgbToLinear(bgSrgb.B / 255f));
            var fgLin = (
                r: SrgbToLinear(fgSrgb.R / 255f),
                g: SrgbToLinear(fgSrgb.G / 255f),
                b: SrgbToLinear(fgSrgb.B / 255f));

            ApplyOledColorTinting(ref fgLin, preset, strength);

            int zoomX = outW / srcW;
            int zoomY = outH / srcH;
            if (zoomX < 1) zoomX = 1;
            if (zoomY < 1) zoomY = 1;
            
            float cellW = zoomX;
            float cellH = zoomY;

            // Native-ish scale (around 1x) should be maximally stable and readable.
            // Avoid optical effects that can look broken at this footprint.
            if (effectiveScale <= 1.10 || (cellW <= 1.10f && cellH <= 1.10f))
            {
                RenderNativeScale(
                    srcW,
                    srcH,
                    layers,
                    layerPixels,
                    selectionService,
                    pasteMode,
                    outW,
                    outH,
                    bgSrgb,
                    fgSrgb,
                    preset,
                    strength,
                    outBgra);
                return;
            }

            float cellMin = MathF.Min(cellW, cellH);

            // ── Adaptive fill ──────────────────────────────────────────────
            float targetGapPx = preset switch
            {
                DisplaySimulationPreset.EPaper => 0.55f,
                DisplaySimulationPreset.FlipperZeroLcd => 0.35f,
                DisplaySimulationPreset.Ssd1306OledBlue or DisplaySimulationPreset.Ssd1306OledGreen => 0.75f,
                DisplaySimulationPreset.Ssd1306OledWhite or DisplaySimulationPreset.GenericLcd => 0.70f,
                _ => 0.70f,
            } * strength;
            
            // Constrain gap size to prevent pixels from disappearing at low zoom
            targetGapPx = MathF.Min(targetGapPx, cellMin * 0.4f);
            
            // Progressive detail regime: 0 -> 1 as pixel footprint grows
            float detailRegime = Math.Clamp((cellMin - 1.15f) / 2.35f, 0f, 1f);
            if (isScaleCapped)
                detailRegime *= 0.92f;
            _ = Math.Clamp((float)(effectiveScale / 2.0), 0.75f, 1.15f);

            // At high zoom (>6x), aggressively increase blur and bloom for dramatic glow effect
            float zoomBoost = Math.Clamp(((float)effectiveScale - 6.0f) / 6.0f, 0f, 3.0f);
            blurStrength *= (1f + zoomBoost * 3.0f);
            bloomStrength *= (1f + zoomBoost * 4.0f);
            blurSigma *= (1f + zoomBoost * 2.5f);
            bloomSigma *= (1f + zoomBoost * 2.0f);

            // Keep deterministic but gently reduce fragile details near cap/small cell sizes.
            blurStrength *= Math.Clamp(0.70f + (0.30f * detailRegime), 0.70f, 1f);
            bloomStrength *= Math.Clamp(0.65f + (0.35f * detailRegime), 0.65f, 1f);

            // E-paper ink has softer edges than emissive displays
            if (preset == DisplaySimulationPreset.EPaper)
            {
                blurSigma *= 1.4f;
                blurStrength = MathF.Max(blurStrength, 0.30f * strength);
            }

            // When output pixels per source pixel are below ~2.5, the aperture model
            // can't render meaningful sub-pixel effects (gap widths, edge transitions,
            // blur/bloom kernels all collapse to sub-pixel sizes, producing aliasing
            // and Moiré artifacts). This is common for higher-resolution canvases
            // (e.g. 128×64) where MaxPreviewDimensionPx caps the output to ~1.25px/cell.
            // Use the stable fallback which produces clean, artifact-free output.
            if (cellMin < 2.5f)
            {
                RenderLowScaleFallback(
                    srcW,
                    srcH,
                    layers,
                    layerPixels,
                    selectionService,
                    pasteMode,
                    outW,
                    outH,
                    bgSrgb,
                    fgSrgb,
                    preset,
                    strength,
                    detailRegime,
                    outBgra);
                return;
            }

            var pool = ArrayPool<float>.Shared;
            int pxCount = outW * outH;

            // Base intensity (aperture only)
            float[] intensity = pool.Rent(pxCount);
            try
            {
                Array.Clear(intensity, 0, pxCount);

                bool hasFloating = selectionService != null && selectionService.IsFloating && selectionService.FloatingPixels != null;

                for (int oy = 0; oy < outH; oy++)
                {
                    int y = Math.Clamp(oy / zoomY, 0, srcH - 1);

                    for (int ox = 0; ox < outW; ox++)
                    {
                        int x = Math.Clamp(ox / zoomX, 0, srcW - 1);

                        int si = (y * srcW) + x;
                        bool on = IsPixelOn(si, x, y, layers, layerPixels);

                        if (hasFloating)
                        {
                            int fx = x - selectionService!.FloatingX;
                            int fy = y - selectionService.FloatingY;
                            if (fx >= 0 && fx < selectionService.FloatingWidth &&
                                fy >= 0 && fy < selectionService.FloatingHeight)
                            {
                                bool floatingPixel = selectionService.FloatingPixels![fx, fy];
                                if (pasteMode == FloatingPasteMode.Transparent)
                                {
                                    if (floatingPixel) on = true;
                                }
                                else
                                {
                                    on = floatingPixel;
                                }
                            }
                        }

                        if (!on)
                        {
                            intensity[(oy * outW) + ox] = 0f;
                            continue;
                        }

                        float dx = (ox % zoomX) - (zoomX - 1) * 0.5f;
                        float dy = (oy % zoomY) - (zoomY - 1) * 0.5f;
                        
                        float currentEdgeSoftnessPx = baseEdgeSoftnessPx * strength * MathF.Sqrt(Math.Clamp(cellMin / 3.5f, 0.3f, 4f));
                        float cornerRadius = MathF.Max(0.01f, currentEdgeSoftnessPx * cellMin * 0.5f);
                        float aperture = 1f;

                        if (preset == DisplaySimulationPreset.EPaper)
                        {
                            float capsuleDetail = Math.Clamp((cellMin - 2f) / 6f, 0f, 1f);
                            float roughScale = 0.18f * strength * Math.Clamp(0.1f + 0.9f * capsuleDetail, 0.1f, 1f);
                            // Cluster microcapsules (2x2 pixel binning) to reflect physical ~35um titanium-dioxide capsules
                            int capX = ox / 2;
                            int capY = oy / 2;
                            float edgeRoughness = (Hash01(capX * 19 + 71, capY * 29 + 113) - 0.5f) * roughScale * cellMin;
                            dx += edgeRoughness;
                            
                            float edgeRoughnessV = (Hash01(capX * 23 + 43, capY * 31 + 89) - 0.5f) * roughScale * cellMin;
                            dy += edgeRoughnessV;
                            
                            currentEdgeSoftnessPx *= (1.5f + 1.7f * capsuleDetail);
                            
                            float innerHalfW = MathF.Max(0f, (zoomX - targetGapPx) * 0.5f - cornerRadius);
                            float innerHalfH = MathF.Max(0f, (zoomY - targetGapPx) * 0.5f - cornerRadius);
                            float qx = MathF.Abs(dx) - innerHalfW;
                            float qy = MathF.Abs(dy) - innerHalfH;
                            float dist = MathF.Sqrt(MathF.Max(qx, 0f) * MathF.Max(qx, 0f) + MathF.Max(qy, 0f) * MathF.Max(qy, 0f)) 
                                         + MathF.Min(MathF.Max(qx, qy), 0f) - cornerRadius;

                            float aaWidth = MathF.Max(0.7f, currentEdgeSoftnessPx);
                            aperture = Math.Clamp(0.5f - dist / aaWidth, 0f, 1f);

                            float capsuleGrain = Hash01(capX * 31 + 113, capY * 37 + 149);
                            float grain2 = Hash01(ox * 53 + 17, oy * 41 + 83);
                            float combinedGrain = capsuleGrain * 0.65f + grain2 * 0.35f;
                            float grainAmt = 0.22f * strength * capsuleDetail;
                            aperture *= (1f - grainAmt + grainAmt * combinedGrain);
                        }
                        else
                        {
                            float innerHalfW = MathF.Max(0f, (zoomX - targetGapPx) * 0.5f - cornerRadius);
                            float innerHalfH = MathF.Max(0f, (zoomY - targetGapPx) * 0.5f - cornerRadius);
                            float qx = MathF.Abs(dx) - innerHalfW;
                            float qy = MathF.Abs(dy) - innerHalfH;
                            float dist = MathF.Sqrt(MathF.Max(qx, 0f) * MathF.Max(qx, 0f) + MathF.Max(qy, 0f) * MathF.Max(qy, 0f)) 
                                         + MathF.Min(MathF.Max(qx, qy), 0f) - cornerRadius;

                            float aaWidth = MathF.Max(0.7f, currentEdgeSoftnessPx);
                            aperture = Math.Clamp(0.5f - dist / aaWidth, 0f, 1f);
                        }

                        intensity[(oy * outW) + ox] = aperture;
                    }
                }

                float[]? blurred = null;
                float[]? bloom = null;
                try
                {
                    if (blurSigma  > 0.0001f && blurStrength  > 0.0001f)
                        blurred = BlurSeparable(intensity, outW, outH, blurSigma, pool);
                    if (bloomSigma > 0.0001f && bloomStrength > 0.0001f)
                        bloom   = BlurSeparable(intensity, outW, outH, bloomSigma, pool);

                    bool isOled = preset is DisplaySimulationPreset.Ssd1306OledWhite
                        or DisplaySimulationPreset.GenericLcd
                        or DisplaySimulationPreset.Ssd1306OledBlue
                        or DisplaySimulationPreset.Ssd1306OledGreen;

                    // Vignetting (subtle radial darkening)
                    float vignetteAmt = preset switch
                    {
                        DisplaySimulationPreset.FlipperZeroLcd => 0.05f * strength,
                        DisplaySimulationPreset.EPaper => 0f,
                        _ => 0.02f * strength,
                    };

                    for (int oy = 0; oy < outH; oy++)
                    {
                        for (int ox = 0; ox < outW; ox++)
                        {
                            int i = (oy * outW) + ox;
                            float a = intensity[i];
                            float d = blurred != null ? blurred[i] : 0f;
                            float b = bloom != null ? bloom[i] : 0f;

                            // Combine: base aperture + diffusion + bloom
                            float lit = Math.Clamp(a + blurStrength * d, 0f, 1.35f);
                            float bloomLit = Math.Clamp(bloomStrength * b, 0f, 1.0f);

                            // Texture/noise (mostly for e-paper, subtle elsewhere).
                            // bg and fg use independent hash seeds so they model separate grain sources.
                            float paperNoise;
                            if (preset == DisplaySimulationPreset.EPaper)
                            {
                                // Capsule grain scales with zoom — subtle at low zoom, heavy at high zoom.
                                float capsuleDetail = Math.Clamp((cellMin - 2f) / 6f, 0f, 1f);
                                float baseGrain = (0.06f + 0.10f * capsuleDetail) * strength;
                                float hiFreq = (0.03f + 0.05f * capsuleDetail) * strength
                                    * (Hash01(ox * 3 + 7, oy * 5 + 11) - 0.5f);
                                paperNoise = baseGrain + MathF.Abs(hiFreq);
                            }
                            else
                            {
                                paperNoise = 0.012f * strength * Math.Clamp(0.45f + (0.55f * detailRegime), 0.45f, 1f);
                            }

                            float bgN = paperNoise * (Hash01(ox, oy) - 0.5f);
                            float fgN = paperNoise * (Hash01(ox + outW, oy) - 0.5f);

                            // E-paper: subtle blue/purple fringe at pixel boundaries,
                            // visible in microscope images as a tint at transitions.
                            if (preset == DisplaySimulationPreset.EPaper && strength > 0f)
                            {
                                float rawAp = intensity[i];
                                // Fringe is strongest at mid-aperture values (boundary zone).
                                float boundary = 4f * rawAp * (1f - rawAp); // peaks at 0.5
                                float fringeAmt = 0.035f * strength * boundary
                                    * Math.Clamp(detailRegime, 0.2f, 1f);
                                // Shift blue channel slightly darker, giving purple-ish tint.
                                bgN -= fringeAmt * 0.5f;
                            }

                            float r = bgLin.r + bgN;
                            float g = bgLin.g + bgN;
                            float bl = bgLin.b + bgN;

                            // Reduce contrast a bit for e-paper
                            float contrast = preset == DisplaySimulationPreset.EPaper ? (0.86f + 0.08f * (1f - strength)) : 1f;

                            float fr = fgLin.r + fgN;
                            float fg = fgLin.g + fgN;
                            float fb = fgLin.b + fgN;

                            // OLED diode core lift (center of lit diode is hotter/whiter)
                            if (isOled && a > 0.05f)
                            {
                                float cellDx = (ox % zoomX) - (zoomX - 1) * 0.5f;
                                float cellDy = (oy % zoomY) - (zoomY - 1) * 0.5f;
                                float distFromCenter = MathF.Sqrt(cellDx * cellDx + cellDy * cellDy) / (cellMin * 0.55f);
                                float core = Math.Clamp(1.0f - distFromCenter, 0f, 1f) * a * strength;

                                if (preset is DisplaySimulationPreset.Ssd1306OledWhite or DisplaySimulationPreset.GenericLcd)
                                {
                                    fr = Math.Clamp(fr + core * 0.25f, 0f, 1f);
                                    fg = Math.Clamp(fg + core * 0.25f, 0f, 1f);
                                    fb = Math.Clamp(fb + core * 0.25f, 0f, 1f);
                                }
                                else if (preset == DisplaySimulationPreset.Ssd1306OledBlue)
                                {
                                    fr = Math.Clamp(fr + core * 0.45f, 0f, 1f);
                                    fg = Math.Clamp(fg + core * 0.65f, 0f, 1f);
                                    fb = Math.Clamp(fb + core * 0.30f, 0f, 1f);
                                }
                                else if (preset == DisplaySimulationPreset.Ssd1306OledGreen)
                                {
                                    fr = Math.Clamp(fr + core * 0.55f, 0f, 1f);
                                    fg = Math.Clamp(fg + core * 0.30f, 0f, 1f);
                                    fb = Math.Clamp(fb + core * 0.55f, 0f, 1f);
                                }
                            }

                            // Combine substrate, diode emission, and atmospheric bloom
                            r = r + contrast * lit * (fr - r) + bloomLit * fgLin.r;
                            g = g + contrast * lit * (fg - g) + bloomLit * fgLin.g;
                            bl = bl + contrast * lit * (fb - bl) + bloomLit * fgLin.b;

                            r = Math.Clamp(r, 0f, 1f);
                            g = Math.Clamp(g, 0f, 1f);
                            bl = Math.Clamp(bl, 0f, 1f);

                            // Flipper Zero: subtle LCD drop shadow cast by dark pixels onto the orange backing
                            if (preset == DisplaySimulationPreset.FlipperZeroLcd && cellMin >= 3.0f && strength > 0f)
                            {
                                if (a < 0.2f && ox > 0 && oy > 0)
                                {
                                    float prevAp = intensity[i - 1] > 0.5f ? intensity[i - 1] : intensity[i - outW];
                                    if (prevAp > 0.5f)
                                    {
                                        float shadow = 0.06f * strength;
                                        r *= (1f - shadow);
                                        g *= (1f - shadow);
                                        bl *= (1f - shadow);
                                    }
                                }
                            }

                            // Flipper Zero: left-to-right edge-lit backlight falloff from 3 left-mounted LEDs
                            if (preset == DisplaySimulationPreset.FlipperZeroLcd && strength > 0f)
                            {
                                float xNorm = ox / (float)outW;
                                float edgeLight = 1.0f + 0.045f * (1.0f - xNorm) * strength;
                                r *= edgeLight;
                                g *= edgeLight;
                                bl *= edgeLight;

                                // Residual transmission through dark pixels
                                float leak = 0.018f * strength;
                                r = MathF.Max(r, bgLin.r * leak);
                                g = MathF.Max(g, bgLin.g * leak);
                                bl = MathF.Max(bl, bgLin.b * leak);
                            }

                            // Vignetting
                            if (vignetteAmt > 0f)
                            {
                                float nx = ox / (float)outW - 0.5f;
                                float ny = oy / (float)outH - 0.5f;
                                float vig = 1f - vignetteAmt * (nx * nx + ny * ny) * 4f;
                                r *= vig;
                                g *= vig;
                                bl *= vig;
                            }

                            r = Math.Clamp(r, 0f, 1f);
                            g = Math.Clamp(g, 0f, 1f);
                            bl = Math.Clamp(bl, 0f, 1f);

                            byte R = (byte)Math.Clamp((int)MathF.Round(LinearToSrgb(r) * 255f, MidpointRounding.AwayFromZero), 0, 255);
                            byte G = (byte)Math.Clamp((int)MathF.Round(LinearToSrgb(g) * 255f, MidpointRounding.AwayFromZero), 0, 255);
                            byte B = (byte)Math.Clamp((int)MathF.Round(LinearToSrgb(bl) * 255f, MidpointRounding.AwayFromZero), 0, 255);

                            outBgra[i] = PackBgra(255, R, G, B);
                        }
                    }
                }
                finally
                {
                    // Bug 2: Always return blurred/bloom to pool, even if an exception propagates.
                    if (blurred != null) pool.Return(blurred);
                    if (bloom   != null) pool.Return(bloom);
                }
            }
            finally
            {
                // Bug 2: Always return the primary intensity buffer to pool.
                pool.Return(intensity);
            }
        }

        private static void RenderNativeScale(
            int srcW,
            int srcH,
            System.Collections.Generic.IReadOnlyList<LayerState> layers,
            System.Collections.Generic.IReadOnlyList<Hexprite.Core.IPixelBuffer> layerPixels,
            ISelectionService? selectionService,
            FloatingPasteMode pasteMode,
            int outW,
            int outH,
            Color bgSrgb,
            Color fgSrgb,
            DisplaySimulationPreset preset,
            float strength,
            uint[] outBgra)
        {
            // Bug 3: Guard against NaN from invScaleX/invScaleY when dimensions are zero.
            if (outW <= 0 || outH <= 0) return;

            float invScaleX = srcW / (float)outW;
            float invScaleY = srcH / (float)outH;

            bool hasFloating = selectionService != null && selectionService.IsFloating && selectionService.FloatingPixels != null;
            // Keep only a tiny touch of texture at native scale.
            float noiseAmp = preset == DisplaySimulationPreset.EPaper ? 0.018f * strength : 0.0f;
            float contrast = preset == DisplaySimulationPreset.EPaper ? 0.92f : 1f;

            var bgLin = (
                r: SrgbToLinear(bgSrgb.R / 255f),
                g: SrgbToLinear(bgSrgb.G / 255f),
                b: SrgbToLinear(bgSrgb.B / 255f));
            var fgLin = (
                r: SrgbToLinear(fgSrgb.R / 255f),
                g: SrgbToLinear(fgSrgb.G / 255f),
                b: SrgbToLinear(fgSrgb.B / 255f));

            ApplyOledColorTinting(ref fgLin, preset, strength);

            float microBloomStrength = preset switch
            {
                DisplaySimulationPreset.Ssd1306OledBlue or DisplaySimulationPreset.Ssd1306OledGreen
                    or DisplaySimulationPreset.Ssd1306OledWhite or DisplaySimulationPreset.GenericLcd => 0.35f * strength,
                _ => 0.0f,
            };

            float vignetteAmt = preset switch
            {
                DisplaySimulationPreset.FlipperZeroLcd => 0.05f * strength,
                DisplaySimulationPreset.EPaper => 0f,
                _ => 0.02f * strength,
            };

            bool IsOnAt(int x, int y)
            {
                if (x < 0 || x >= srcW || y < 0 || y >= srcH) return false;
                int i = (y * srcW) + x;
                bool layerOn = IsPixelOn(i, x, y, layers, layerPixels);
                if (hasFloating)
                {
                    int fx = x - selectionService!.FloatingX;
                    int fy = y - selectionService.FloatingY;
                    if (fx >= 0 && fx < selectionService.FloatingWidth &&
                        fy >= 0 && fy < selectionService.FloatingHeight)
                    {
                        bool floatingPixel = selectionService.FloatingPixels![fx, fy];
                        if (pasteMode == FloatingPasteMode.Transparent)
                        {
                            if (floatingPixel) return true;
                        }
                        else
                        {
                            return floatingPixel;
                        }
                    }
                }
                return layerOn;
            }

            bool isDownscaling = invScaleX > 1.01f || invScaleY > 1.01f;

            for (int oy = 0; oy < outH; oy++)
            {
                for (int ox = 0; ox < outW; ox++)
                {
                    bool on;
                    int sx, sy;
                    if (isDownscaling)
                    {
                        int x0 = Math.Clamp((int)MathF.Floor(ox * invScaleX), 0, srcW - 1);
                        int x1 = Math.Clamp((int)MathF.Ceiling((ox + 1) * invScaleX) - 1, 0, srcW - 1);
                        int y0 = Math.Clamp((int)MathF.Floor(oy * invScaleY), 0, srcH - 1);
                        int y1 = Math.Clamp((int)MathF.Ceiling((oy + 1) * invScaleY) - 1, 0, srcH - 1);
                        int onCount = 0, total = 0;
                        for (int by = y0; by <= y1; by++)
                            for (int bx = x0; bx <= x1; bx++)
                            {
                                total++;
                                if (IsOnAt(bx, by)) onCount++;
                            }
                        on = total > 0 && onCount * 2 > total;
                        sx = (x0 + x1) / 2;
                        sy = (y0 + y1) / 2;
                    }
                    else
                    {
                        int zoomX = Math.Max(1, (int)MathF.Round(1f / invScaleX, MidpointRounding.AwayFromZero));
                        int zoomY = Math.Max(1, (int)MathF.Round(1f / invScaleY, MidpointRounding.AwayFromZero));
                        sy = Math.Clamp(oy / zoomY, 0, srcH - 1);
                        sx = Math.Clamp(ox / zoomX, 0, srcW - 1);
                        on = IsOnAt(sx, sy);
                    }

                    float n = (Hash01(ox, oy) - 0.5f) * noiseAmp;
                    float r = bgLin.r + n;
                    float g = bgLin.g + n;
                    float b = bgLin.b + n;
                    if (on)
                    {
                        r += contrast * (fgLin.r - r);
                        g += contrast * (fgLin.g - g);
                        b += contrast * (fgLin.b - b);
                        // Slightly lift lit pixels at native scale so the glow reads.
                        float coreLift = microBloomStrength * 0.35f;
                        r += coreLift * fgLin.r;
                        g += coreLift * fgLin.g;
                        b += coreLift * fgLin.b;
                    }

                    if (!on && microBloomStrength > 0f && !isDownscaling)
                    {
                        int ring1 = 0;
                        int ring2 = 0;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                if (IsOnAt(sx + dx, sy + dy)) ring1++;
                            }
                        }
                        for (int dy = -2; dy <= 2; dy++)
                        {
                            for (int dx = -2; dx <= 2; dx++)
                            {
                                // Skip the inner 3×3 ring (already counted in ring1).
                                // The (0,0) centre is covered by this guard, so no extra check needed.
                                if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1) continue;
                                if (IsOnAt(sx + dx, sy + dy)) ring2++;
                            }
                        }
                        if (ring1 > 0 || ring2 > 0)
                        {
                            float halo = microBloomStrength * ((ring1 / 8f) * 1.0f + (ring2 / 16f) * 0.50f);
                            r += halo * fgLin.r;
                            g += halo * fgLin.g;
                            b += halo * fgLin.b;
                        }
                    }

                    r = Math.Clamp(r, 0f, 1f);
                    g = Math.Clamp(g, 0f, 1f);
                    b = Math.Clamp(b, 0f, 1f);

                    // Flipper Zero: left-to-right edge-lit backlight falloff & leakage
                    if (preset == DisplaySimulationPreset.FlipperZeroLcd && strength > 0f)
                    {
                        float xNorm = ox / (float)outW;
                        float edgeLight = 1.0f + 0.045f * (1.0f - xNorm) * strength;
                        r *= edgeLight;
                        g *= edgeLight;
                        b *= edgeLight;

                        float leak = 0.018f * strength;
                        r = MathF.Max(r, bgLin.r * leak);
                        g = MathF.Max(g, bgLin.g * leak);
                        b = MathF.Max(b, bgLin.b * leak);
                    }

                    // Vignetting
                    if (vignetteAmt > 0f)
                    {
                        float nx = ox / (float)outW - 0.5f;
                        float ny = oy / (float)outH - 0.5f;
                        float vig = 1f - vignetteAmt * (nx * nx + ny * ny) * 4f;
                        r *= vig;
                        g *= vig;
                        b *= vig;
                    }

                    r = Math.Clamp(r, 0f, 1f);
                    g = Math.Clamp(g, 0f, 1f);
                    b = Math.Clamp(b, 0f, 1f);

                    byte R = (byte)Math.Clamp((int)MathF.Round(LinearToSrgb(r) * 255f, MidpointRounding.AwayFromZero), 0, 255);
                    byte G = (byte)Math.Clamp((int)MathF.Round(LinearToSrgb(g) * 255f, MidpointRounding.AwayFromZero), 0, 255);
                    byte B = (byte)Math.Clamp((int)MathF.Round(LinearToSrgb(b) * 255f, MidpointRounding.AwayFromZero), 0, 255);
                    outBgra[(oy * outW) + ox] = PackBgra(255, R, G, B);
                }
            }
        }

        private static void RenderLowScaleFallback(
            int srcW,
            int srcH,
            System.Collections.Generic.IReadOnlyList<LayerState> layers,
            System.Collections.Generic.IReadOnlyList<Hexprite.Core.IPixelBuffer> layerPixels,
            ISelectionService? selectionService,
            FloatingPasteMode pasteMode,
            int outW,
            int outH,
            Color bgSrgb,
            Color fgSrgb,
            DisplaySimulationPreset preset,
            float strength,
            float detailRegime,
            uint[] outBgra)
        {
            // Bug 3: Guard against NaN from invScaleX/invScaleY when dimensions are zero.
            if (outW <= 0 || outH <= 0) return;

            float invScaleX = srcW / (float)outW;
            float invScaleY = srcH / (float)outH;

            bool hasFloating = selectionService != null && selectionService.IsFloating && selectionService.FloatingPixels != null;
            float noiseAmp = preset == DisplaySimulationPreset.EPaper
                ? 0.06f * strength * Math.Clamp(0.50f + (0.50f * detailRegime), 0.50f, 1f)
                : 0.01f * strength * Math.Clamp(0.35f + (0.65f * detailRegime), 0.35f, 1f);
            float contrast = preset == DisplaySimulationPreset.EPaper ? (0.88f + 0.08f * (1f - strength)) : 1f;

            var bgLin = (
                r: SrgbToLinear(bgSrgb.R / 255f),
                g: SrgbToLinear(bgSrgb.G / 255f),
                b: SrgbToLinear(bgSrgb.B / 255f));
            var fgLin = (
                r: SrgbToLinear(fgSrgb.R / 255f),
                g: SrgbToLinear(fgSrgb.G / 255f),
                b: SrgbToLinear(fgSrgb.B / 255f));

            ApplyOledColorTinting(ref fgLin, preset, strength);

            float vignetteAmt = preset switch
            {
                DisplaySimulationPreset.FlipperZeroLcd => 0.05f * strength,
                DisplaySimulationPreset.EPaper => 0f,
                _ => 0.02f * strength,
            };

            // Local helper for composite pixel state (matches RenderNativeScale's IsOnAt).
            bool IsOnAt(int x, int y)
            {
                if (x < 0 || x >= srcW || y < 0 || y >= srcH) return false;
                int idx = (y * srcW) + x;
                bool layerOn = IsPixelOn(idx, x, y, layers, layerPixels);
                if (hasFloating)
                {
                    int ffx = x - selectionService!.FloatingX;
                    int ffy = y - selectionService.FloatingY;
                    if (ffx >= 0 && ffx < selectionService.FloatingWidth &&
                        ffy >= 0 && ffy < selectionService.FloatingHeight)
                    {
                        bool floatingPixel = selectionService.FloatingPixels![ffx, ffy];
                        if (pasteMode == FloatingPasteMode.Transparent)
                        {
                            if (floatingPixel) return true;
                        }
                        else
                        {
                            return floatingPixel;
                        }
                    }
                }
                return layerOn;
            }

            bool isDownscaling = invScaleX > 1.01f || invScaleY > 1.01f;

            for (int oy = 0; oy < outH; oy++)
            {
                for (int ox = 0; ox < outW; ox++)
                {
                    bool on;
                    if (isDownscaling)
                    {
                        int x0 = Math.Clamp((int)MathF.Floor(ox * invScaleX), 0, srcW - 1);
                        int x1 = Math.Clamp((int)MathF.Ceiling((ox + 1) * invScaleX) - 1, 0, srcW - 1);
                        int y0 = Math.Clamp((int)MathF.Floor(oy * invScaleY), 0, srcH - 1);
                        int y1 = Math.Clamp((int)MathF.Ceiling((oy + 1) * invScaleY) - 1, 0, srcH - 1);
                        int onCount = 0, total = 0;
                        for (int by = y0; by <= y1; by++)
                            for (int bx = x0; bx <= x1; bx++)
                            {
                                total++;
                                if (IsOnAt(bx, by)) onCount++;
                            }
                        on = total > 0 && onCount * 2 > total;
                    }
                    else
                    {
                        int zoomX = Math.Max(1, (int)MathF.Round(1f / invScaleX, MidpointRounding.AwayFromZero));
                        int zoomY = Math.Max(1, (int)MathF.Round(1f / invScaleY, MidpointRounding.AwayFromZero));
                        int sx = Math.Clamp(ox / zoomX, 0, srcW - 1);
                        int sy = Math.Clamp(oy / zoomY, 0, srcH - 1);
                        on = IsOnAt(sx, sy);
                    }

                    float n = (Hash01(ox, oy) - 0.5f) * noiseAmp;
                    float r = bgLin.r + n;
                    float g = bgLin.g + n;
                    float b = bgLin.b + n;
                    if (on)
                    {
                        r += contrast * (fgLin.r - r);
                        g += contrast * (fgLin.g - g);
                        b += contrast * (fgLin.b - b);
                    }

                    r = Math.Clamp(r, 0f, 1f);
                    g = Math.Clamp(g, 0f, 1f);
                    b = Math.Clamp(b, 0f, 1f);

                    // Flipper Zero: left-to-right edge-lit backlight falloff & leakage
                    if (preset == DisplaySimulationPreset.FlipperZeroLcd && strength > 0f)
                    {
                        float xNorm = ox / (float)outW;
                        float edgeLight = 1.0f + 0.045f * (1.0f - xNorm) * strength;
                        r *= edgeLight;
                        g *= edgeLight;
                        b *= edgeLight;

                        float leak = 0.018f * strength;
                        r = MathF.Max(r, bgLin.r * leak);
                        g = MathF.Max(g, bgLin.g * leak);
                        b = MathF.Max(b, bgLin.b * leak);
                    }

                    // Vignetting
                    if (vignetteAmt > 0f)
                    {
                        float nx = ox / (float)outW - 0.5f;
                        float ny = oy / (float)outH - 0.5f;
                        float vig = 1f - vignetteAmt * (nx * nx + ny * ny) * 4f;
                        r *= vig;
                        g *= vig;
                        b *= vig;
                    }

                    r = Math.Clamp(r, 0f, 1f);
                    g = Math.Clamp(g, 0f, 1f);
                    b = Math.Clamp(b, 0f, 1f);

                    byte R = (byte)Math.Clamp((int)MathF.Round(LinearToSrgb(r) * 255f, MidpointRounding.AwayFromZero), 0, 255);
                    byte G = (byte)Math.Clamp((int)MathF.Round(LinearToSrgb(g) * 255f, MidpointRounding.AwayFromZero), 0, 255);
                    byte B = (byte)Math.Clamp((int)MathF.Round(LinearToSrgb(b) * 255f, MidpointRounding.AwayFromZero), 0, 255);
                    outBgra[(oy * outW) + ox] = PackBgra(255, R, G, B);
                }
            }
        }

        /// <summary>
        /// Applies a separable Gaussian blur to <paramref name="src"/> and returns the result buffer.
        /// The caller is responsible for returning the returned buffer to <paramref name="pool"/>.
        /// </summary>
        private static float[] BlurSeparable(float[] src, int w, int h, float sigma, ArrayPool<float> pool)
        {
            int radius = Math.Clamp((int)MathF.Ceiling(sigma * 2.5f), 1, 8);
            float[] kernel = BuildGaussianKernel(radius, sigma);

            int n = w * h;
            float[] tmp = pool.Rent(n);
            float[] dst = pool.Rent(n);
            // C-1: Guard both rented buffers so neither leaks on an unexpected exception.
            // tmp is always an intermediate; dst is returned to the caller who owns its lifetime.
            try
            {
                // Horizontal pass
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        float acc = 0f;
                        for (int k = -radius; k <= radius; k++)
                        {
                            int xx = Math.Clamp(x + k, 0, w - 1);
                            acc += src[row + xx] * kernel[k + radius];
                        }
                        tmp[row + x] = acc;
                    }
                }

                // Vertical pass
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        float acc = 0f;
                        for (int k = -radius; k <= radius; k++)
                        {
                            int yy = Math.Clamp(y + k, 0, h - 1);
                            acc += tmp[(yy * w) + x] * kernel[k + radius];
                        }
                        dst[row + x] = acc;
                    }
                }

                return dst;
            }
            catch
            {
                // On failure the caller never sees dst, so we must return it here.
                pool.Return(dst);
                throw;
            }
            finally
            {
                // tmp is always intermediate — return it unconditionally.
                pool.Return(tmp);
            }
        }

        private static float[] BuildGaussianKernel(int radius, float sigma)
        {
            int sigmaMilli = Math.Max(1, (int)MathF.Round(sigma * 1000f, MidpointRounding.AwayFromZero));
            if (GaussianKernelCache.TryGetValue((radius, sigmaMilli), out var cached))
                return cached;

            float[] k = new float[(radius * 2) + 1];
            float inv2s2 = 1f / (2f * sigma * sigma);
            float sum = 0f;
            for (int i = -radius; i <= radius; i++)
            {
                float v = MathF.Exp(-(i * i) * inv2s2);
                k[i + radius] = v;
                sum += v;
            }
            if (sum > 0f)
            {
                for (int i = 0; i < k.Length; i++)
                    k[i] /= sum;
            }

            // H-1: Cap cache size to prevent unbounded growth during continuous zoom.
            // We clear before inserting (not after a racy count check) so the newly
            // built kernel is always added even if two threads race here simultaneously.
            // The worst outcome of a concurrent clear is a brief cache miss — not a leak.
            if (GaussianKernelCache.Count >= 32)
                GaussianKernelCache.Clear();

            // TryAdd is benign if a concurrent thread inserted the same key first.
            GaussianKernelCache.TryAdd((radius, sigmaMilli), k);
            return k;
        }
        private static bool IsPixelOnWithOpacityHelper(bool pixelValue, int x, int y, LayerOpacityMode opacityMode)
        {
            if (!pixelValue) return false;
            return opacityMode switch
            {
                LayerOpacityMode.Checkerboard => ((x + y) % 2) == 0,
                LayerOpacityMode.Sparse => (x % 2 == 0) && (y % 2 == 0),
                LayerOpacityMode.Dense => !((x % 2 != 0) && (y % 2 != 0)),
                _ => true,
            };
        }

        private static bool IsPixelOn(
            int si, int x, int y,
            System.Collections.Generic.IReadOnlyList<LayerState> layers,
            System.Collections.Generic.IReadOnlyList<Hexprite.Core.IPixelBuffer> layerPixels)
        {
            bool composite = false;
            for (int l = layers.Count - 1; l >= 0; l--)
            {
                if (l >= layerPixels.Count) continue;
                var layer = layers[l];
                if (!layer.IsVisible) continue;
                
                var pixelBuf = layerPixels[l];
                if (pixelBuf == null) continue;
                var pixels = pixelBuf.GetMonochromeData();
                if (si >= pixels.Length) continue;
                bool layerOn = layer.OpacityMode == LayerOpacityMode.Solid ? pixels[si] : IsPixelOnWithOpacityHelper(pixels[si], x, y, layer.OpacityMode);
                
                var blendMode = layer.BlendMode;
                if (blendMode == LayerBlendMode.Normal)
                {
                    if (layerOn) composite = true;
                }
                else if (blendMode == LayerBlendMode.Xor)
                {
                    if (layerOn) composite = !composite;
                }
                else if (blendMode == LayerBlendMode.Mask)
                {
                    if (!layerOn) composite = false;
                }
                else if (blendMode == LayerBlendMode.Subtract)
                {
                    if (layerOn) composite = false;
                }
            }
            return composite;
        }
    }
}

