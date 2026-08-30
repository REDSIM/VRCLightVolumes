Shader "Hidden/VRCLV/FroxelShadowCullPyramid" {
    Properties {
        _UdonPointLightVolumeShadowTexture("EVSM Shadow Texture", 2DArray) = "" {}
        _UdonShadowCullPrevious("Previous Pyramid Level", 2D) = "white" {}
        _UdonShadowCullBuildParams("Build Params", Vector) = (1,0,0,0)
        _UdonShadowCullReceiverParams("Receiver Params", Vector) = (0,0,0,0)
        _UdonShadowCullPackParams("Pack Params", Vector) = (0,0,0,0)
    }

    SubShader {
        Tags { "RenderType" = "Opaque" }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off
        ColorMask R

        CGINCLUDE
            #include "UnityCG.cginc"

            // EVSM uses exp(5.54 * z) and -exp(-5 * z). Multiplication by log2(e) keeps both conversions on the native exp2/log2 path.
            #define VRCLV_EVSM_POSITIVE_EXPONENT_LOG2 7.9925305262f
            #define VRCLV_EVSM_NEGATIVE_EXPONENT_LOG2 7.2134752044f
            #define VRCLV_EVSM_COMBINED_EXPONENT_LOG2 15.2060057306f
            #define VRCLV_SHADOW_CULL_SENTINEL 2.0f
            #define VRCLV_SHADOW_CULL_DEPTH_SAFETY 0.002f
            #define VRCLV_SHADOW_CULL_MOMENT_INFLATION 1.001953125f
            #define VRCLV_SHADOW_CULL_RELATIVE_EPSILON 0.000001f
            #define VRCLV_EVSM_MAX_FINITE_HALF 65504.0f

            Texture2DArray<float4> _UdonPointLightVolumeShadowTexture;
            Texture2D<float> _UdonShadowCullPrevious;
            // Build/Reduce: x is current tile size, y is log2(current tile size), z is log2(tile column count), and w is the valid shadow slice count.
            // Pack: x is built level count, y is log2(first-level tile size), while zw retain the tile-column shift and valid slice count.
            float4 _UdonShadowCullBuildParams;
            // x: effective EVSM probability cutoff, w: first source level built into scratch. yz mirror CPU-side variance scales and are intentionally not read by this shader.
            float4 _UdonShadowCullReceiverParams;
            // Build First: x inverse probability, y Chebyshev scale, zw positive/negative variance-denominator reciprocals. Pack: x resolution, y first retained source level, z log2(atlas row pitch), and w exact valid linear node count.
            float4 _UdonShadowCullPackParams;

            struct MeshData {
                float4 vertex : POSITION;
            };

            struct Varyings {
                float4 position : SV_POSITION;
            };

            Varyings Vertex(MeshData input) {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                return output;
            }

            bool DecodeAtlasPixel(float2 pixelPosition, out uint slice, out uint2 localPixel) {
                uint tileSize = (uint)_UdonShadowCullBuildParams.x;
                uint tileShift = (uint)_UdonShadowCullBuildParams.y;
                uint columnShift = (uint)_UdonShadowCullBuildParams.z;
                uint sliceCount = (uint)_UdonShadowCullBuildParams.w;

                uint2 pixel = (uint2)pixelPosition;
                uint2 tile = uint2(pixel.x >> tileShift, pixel.y >> tileShift);
                slice = tile.x + (tile.y << columnShift);
                uint tileMask = tileSize - 1u;
                localPixel = pixel & uint2(tileMask, tileMask);
                return slice < sliceCount;
            }

            // Each lane represents one of the four bilinear cells covered by an L1 hierarchy node. Vectorizing the proof keeps the nine source loads but removes four cloned scalar graphs.
            void BuildWarpedCellBounds4(float4 means0, float4 means1, float4 means2, float4 means3,
                    float4 seconds0, float4 seconds1, float4 seconds2, float4 seconds3,
                    out float4 lowerMean, out float4 upperMean, out float4 upperVariance,
                    out float4 upperSecondMoment, out bool4 valid) {
                valid = (means0 == means0) & (means1 == means1) & (means2 == means2) & (means3 == means3)
                    & (seconds0 == seconds0) & (seconds1 == seconds1) & (seconds2 == seconds2) & (seconds3 == seconds3)
                    & (means0 > 0.0f) & (means1 > 0.0f) & (means2 > 0.0f) & (means3 > 0.0f)
                    & (seconds0 >= 0.0f) & (seconds1 >= 0.0f) & (seconds2 >= 0.0f) & (seconds3 >= 0.0f)
                    & (means0 < VRCLV_EVSM_MAX_FINITE_HALF) & (means1 < VRCLV_EVSM_MAX_FINITE_HALF)
                    & (means2 < VRCLV_EVSM_MAX_FINITE_HALF) & (means3 < VRCLV_EVSM_MAX_FINITE_HALF)
                    & (seconds0 < VRCLV_EVSM_MAX_FINITE_HALF) & (seconds1 < VRCLV_EVSM_MAX_FINITE_HALF)
                    & (seconds2 < VRCLV_EVSM_MAX_FINITE_HALF) & (seconds3 < VRCLV_EVSM_MAX_FINITE_HALF);

                lowerMean = min(min(means0, means1), min(means2, means3));
                upperMean = max(max(means0, means1), max(means2, means3));
                float4 variance0 = max(seconds0 - means0 * means0, 0.0f);
                float4 variance1 = max(seconds1 - means1 * means1, 0.0f);
                float4 variance2 = max(seconds2 - means2 * means2, 0.0f);
                float4 variance3 = max(seconds3 - means3 * means3, 0.0f);
                upperVariance = max(max(variance0, variance1), max(variance2, variance3));
                upperSecondMoment = max(max(seconds0, seconds1), max(seconds2, seconds3));
                upperSecondMoment = max(upperSecondMoment, upperMean * upperMean);

                float4 meanMargin = max(upperMean * VRCLV_SHADOW_CULL_RELATIVE_EPSILON, 1.0e-12f);
                lowerMean = max(lowerMean - meanMargin, 1.0e-20f);
                upperMean += meanMargin;
                float4 momentMargin = max(upperSecondMoment * VRCLV_SHADOW_CULL_RELATIVE_EPSILON, 1.0e-20f);
                upperVariance = upperVariance * VRCLV_SHADOW_CULL_MOMENT_INFLATION + momentMargin;
                upperSecondMoment = upperSecondMoment * VRCLV_SHADOW_CULL_MOMENT_INFLATION + momentMargin;
            }

            float4 PositiveCriticalDepth4(float4 means0, float4 means1, float4 means2, float4 means3,
                    float4 seconds0, float4 seconds1, float4 seconds2, float4 seconds3,
                    float probability, float inverseProbability, float k,
                    float positiveDenominatorReciprocal) {
                if (!(positiveDenominatorReciprocal > 0.0f)) return float4(VRCLV_SHADOW_CULL_SENTINEL, VRCLV_SHADOW_CULL_SENTINEL, VRCLV_SHADOW_CULL_SENTINEL, VRCLV_SHADOW_CULL_SENTINEL);
                float4 lowerMean, upperMean, upperVariance, upperSecondMoment;
                bool4 valid;
                BuildWarpedCellBounds4(means0, means1, means2, means3, seconds0, seconds1, seconds2, seconds3, lowerMean, upperMean, upperVariance, upperSecondMoment, valid);

                float4 center = (lowerMean + upperMean) * 0.5f;
                float4 radius = (upperMean - lowerMean) * 0.5f;
                float4 envelope = upperVariance + radius * radius;
                float4 offset = min(radius, sqrt(max(probability * envelope, 0.0f)));
                float4 envelopeAtOffset = sqrt(max(envelope - offset * offset, 0.0f));
                float4 rangeCritical = center + offset + k * envelopeAtOffset;
                float4 secondMomentCritical = sqrt(upperSecondMoment * inverseProbability);
                float4 regularCritical = min(rangeCritical, secondMomentCritical);

                float4 criticalWarpedDepth = max(regularCritical, upperMean * positiveDenominatorReciprocal);
                valid = valid & (criticalWarpedDepth > 0.0f) & (criticalWarpedDepth <= 1.0e19f);
                float4 criticalDepth = log2(criticalWarpedDepth) * rcp(VRCLV_EVSM_POSITIVE_EXPONENT_LOG2) + VRCLV_SHADOW_CULL_DEPTH_SAFETY;
                valid = valid & (criticalDepth == criticalDepth);
                return valid ? clamp(criticalDepth, -1.0f, VRCLV_SHADOW_CULL_SENTINEL) : float4(VRCLV_SHADOW_CULL_SENTINEL, VRCLV_SHADOW_CULL_SENTINEL, VRCLV_SHADOW_CULL_SENTINEL, VRCLV_SHADOW_CULL_SENTINEL);
            }

            float4 NegativeCriticalDepth4(float4 means0, float4 means1, float4 means2, float4 means3, float4 seconds0, float4 seconds1, float4 seconds2, float4 seconds3, float probability, float k, float negativeDenominatorReciprocal) {
                float4 lowerMean, upperMean, upperVariance, upperSecondMoment;
                bool4 valid;
                BuildWarpedCellBounds4(means0, means1, means2, means3, seconds0, seconds1, seconds2, seconds3, lowerMean, upperMean, upperVariance, upperSecondMoment, valid);

                float4 center = (lowerMean + upperMean) * 0.5f;
                float4 radius = (upperMean - lowerMean) * 0.5f;
                float4 envelope = upperVariance + radius * radius;
                float4 offset = min(radius, sqrt(max(probability * envelope, 0.0f)));
                float4 envelopeAtOffset = sqrt(max(envelope - offset * offset, 0.0f));
                float4 regularCritical = center - offset - k * envelopeAtOffset;
                float4 varianceCritical = lowerMean * negativeDenominatorReciprocal;
                float4 criticalWarpedMagnitude = min(regularCritical, varianceCritical);
                valid = valid & (criticalWarpedMagnitude > 0.0f) & (criticalWarpedMagnitude <= 1.0e19f);
                float4 criticalDepth = -log2(criticalWarpedMagnitude) * rcp(VRCLV_EVSM_NEGATIVE_EXPONENT_LOG2) + VRCLV_SHADOW_CULL_DEPTH_SAFETY;
                valid = valid & (criticalDepth == criticalDepth);
                return valid ? clamp(criticalDepth, -1.0f, VRCLV_SHADOW_CULL_SENTINEL) : float4(VRCLV_SHADOW_CULL_SENTINEL, VRCLV_SHADOW_CULL_SENTINEL, VRCLV_SHADOW_CULL_SENTINEL, VRCLV_SHADOW_CULL_SENTINEL);
            }

            float4 CriticalShadowDepth4(float4 m00, float4 m10, float4 m20,
                    float4 m01, float4 m11, float4 m21,
                    float4 m02, float4 m12, float4 m22,
                    float probability, float inverseProbability, float k,
                    float positiveDenominatorReciprocal, float negativeDenominatorReciprocal) {
                float4 positive0 = float4(m00.r, m10.r, m01.r, m11.r);
                float4 positive1 = float4(m10.r, m20.r, m11.r, m21.r);
                float4 positive2 = float4(m01.r, m11.r, m02.r, m12.r);
                float4 positive3 = float4(m11.r, m21.r, m12.r, m22.r);
                float4 positiveSecond0 = float4(m00.b, m10.b, m01.b, m11.b);
                float4 positiveSecond1 = float4(m10.b, m20.b, m11.b, m21.b);
                float4 positiveSecond2 = float4(m01.b, m11.b, m02.b, m12.b);
                float4 positiveSecond3 = float4(m11.b, m21.b, m12.b, m22.b);

                float4 negative0 = -float4(m00.g, m10.g, m01.g, m11.g);
                float4 negative1 = -float4(m10.g, m20.g, m11.g, m21.g);
                float4 negative2 = -float4(m01.g, m11.g, m02.g, m12.g);
                float4 negative3 = -float4(m11.g, m21.g, m12.g, m22.g);
                float4 negativeSecond0 = float4(m00.a, m10.a, m01.a, m11.a);
                float4 negativeSecond1 = float4(m10.a, m20.a, m11.a, m21.a);
                float4 negativeSecond2 = float4(m01.a, m11.a, m02.a, m12.a);
                float4 negativeSecond3 = float4(m11.a, m21.a, m12.a, m22.a);

                float4 positiveDepth = PositiveCriticalDepth4(positive0, positive1, positive2, positive3, positiveSecond0, positiveSecond1, positiveSecond2, positiveSecond3, probability, inverseProbability, k, positiveDenominatorReciprocal);
                float4 negativeDepth = NegativeCriticalDepth4(negative0, negative1, negative2, negative3, negativeSecond0, negativeSecond1, negativeSecond2, negativeSecond3, probability, k, negativeDenominatorReciprocal);
                return min(positiveDepth, negativeDepth);
            }

            // Couples the two EVSM warps before the spatial maximum. For one receiver, min(log(A) / cp, -log(B) / cn) is no greater than log(A / B) / (cp + cn).
            // The corner mean ratio and Kantorovich coefficient-of-variation bounds below hold for every convex combination, which includes every hardware-bilinear sample.
            // This prevents a positive-warp maximum at one UV and a negative-warp maximum at another UV from forming the unnecessarily loose min(max(+), max(-)) threshold.
            float4 JointCriticalDepth4(float4 m00, float4 m10, float4 m20, float4 m01, float4 m11, float4 m21, float4 m02, float4 m12, float4 m22, float k, float positiveDenominatorReciprocal, float negativeDenominatorReciprocal) {
                if (!(k >= 0.0f && k <= 1.0e19f) || !(positiveDenominatorReciprocal > 0.0f) || !(negativeDenominatorReciprocal > 0.0f))
                    return float4(VRCLV_SHADOW_CULL_SENTINEL, VRCLV_SHADOW_CULL_SENTINEL, VRCLV_SHADOW_CULL_SENTINEL, VRCLV_SHADOW_CULL_SENTINEL);

                float4 positive0 = float4(m00.r, m10.r, m01.r, m11.r);
                float4 positive1 = float4(m10.r, m20.r, m11.r, m21.r);
                float4 positive2 = float4(m01.r, m11.r, m02.r, m12.r);
                float4 positive3 = float4(m11.r, m21.r, m12.r, m22.r);
                float4 positiveSecond0 = float4(m00.b, m10.b, m01.b, m11.b);
                float4 positiveSecond1 = float4(m10.b, m20.b, m11.b, m21.b);
                float4 positiveSecond2 = float4(m01.b, m11.b, m02.b, m12.b);
                float4 positiveSecond3 = float4(m11.b, m21.b, m12.b, m22.b);

                float4 negative0 = -float4(m00.g, m10.g, m01.g, m11.g);
                float4 negative1 = -float4(m10.g, m20.g, m11.g, m21.g);
                float4 negative2 = -float4(m01.g, m11.g, m02.g, m12.g);
                float4 negative3 = -float4(m11.g, m21.g, m12.g, m22.g);
                float4 negativeSecond0 = float4(m00.a, m10.a, m01.a, m11.a);
                float4 negativeSecond1 = float4(m10.a, m20.a, m11.a, m21.a);
                float4 negativeSecond2 = float4(m01.a, m11.a, m02.a, m12.a);
                float4 negativeSecond3 = float4(m11.a, m21.a, m12.a, m22.a);

                // Legitimate EVSM means are bounded well away from zero by the fixed warps. Reject tiny positive values as malformed before reciprocal-square bounds can overflow; the sentinel keeps the hierarchy fail-open for that lane.
                bool4 valid = (positive0 == positive0) & (positive1 == positive1)
                    & (positive2 == positive2) & (positive3 == positive3)
                    & (negative0 == negative0) & (negative1 == negative1)
                    & (negative2 == negative2) & (negative3 == negative3)
                    & (positiveSecond0 == positiveSecond0) & (positiveSecond1 == positiveSecond1)
                    & (positiveSecond2 == positiveSecond2) & (positiveSecond3 == positiveSecond3)
                    & (negativeSecond0 == negativeSecond0) & (negativeSecond1 == negativeSecond1)
                    & (negativeSecond2 == negativeSecond2) & (negativeSecond3 == negativeSecond3)
                    & (positive0 > 1.0e-6f) & (positive1 > 1.0e-6f)
                    & (positive2 > 1.0e-6f) & (positive3 > 1.0e-6f)
                    & (negative0 > 1.0e-6f) & (negative1 > 1.0e-6f)
                    & (negative2 > 1.0e-6f) & (negative3 > 1.0e-6f)
                    & (positiveSecond0 >= 0.0f) & (positiveSecond1 >= 0.0f)
                    & (positiveSecond2 >= 0.0f) & (positiveSecond3 >= 0.0f)
                    & (negativeSecond0 >= 0.0f) & (negativeSecond1 >= 0.0f)
                    & (negativeSecond2 >= 0.0f) & (negativeSecond3 >= 0.0f)
                    & (positive0 < VRCLV_EVSM_MAX_FINITE_HALF)
                    & (positive1 < VRCLV_EVSM_MAX_FINITE_HALF)
                    & (positive2 < VRCLV_EVSM_MAX_FINITE_HALF)
                    & (positive3 < VRCLV_EVSM_MAX_FINITE_HALF)
                    & (negative0 < VRCLV_EVSM_MAX_FINITE_HALF)
                    & (negative1 < VRCLV_EVSM_MAX_FINITE_HALF)
                    & (negative2 < VRCLV_EVSM_MAX_FINITE_HALF)
                    & (negative3 < VRCLV_EVSM_MAX_FINITE_HALF)
                    & (positiveSecond0 < VRCLV_EVSM_MAX_FINITE_HALF)
                    & (positiveSecond1 < VRCLV_EVSM_MAX_FINITE_HALF)
                    & (positiveSecond2 < VRCLV_EVSM_MAX_FINITE_HALF)
                    & (positiveSecond3 < VRCLV_EVSM_MAX_FINITE_HALF)
                    & (negativeSecond0 < VRCLV_EVSM_MAX_FINITE_HALF)
                    & (negativeSecond1 < VRCLV_EVSM_MAX_FINITE_HALF)
                    & (negativeSecond2 < VRCLV_EVSM_MAX_FINITE_HALF)
                    & (negativeSecond3 < VRCLV_EVSM_MAX_FINITE_HALF);

                float4 positiveLower = min(min(positive0, positive1), min(positive2, positive3));
                float4 positiveUpper = max(max(positive0, positive1), max(positive2, positive3));
                float4 negativeLower = min(min(negative0, negative1), min(negative2, negative3));
                float4 negativeUpper = max(max(negative0, negative1), max(negative2, negative3));
                float4 positiveMeanMargin = max(positiveUpper * VRCLV_SHADOW_CULL_RELATIVE_EPSILON, 1.0e-12f);
                float4 negativeMeanMargin = max(negativeUpper * VRCLV_SHADOW_CULL_RELATIVE_EPSILON, 1.0e-12f);
                valid = valid & (positive0 > positiveMeanMargin + 1.0e-10f)
                    & (positive1 > positiveMeanMargin + 1.0e-10f)
                    & (positive2 > positiveMeanMargin + 1.0e-10f)
                    & (positive3 > positiveMeanMargin + 1.0e-10f)
                    & (negative0 > negativeMeanMargin + 1.0e-10f)
                    & (negative1 > negativeMeanMargin + 1.0e-10f)
                    & (negative2 > negativeMeanMargin + 1.0e-10f)
                    & (negative3 > negativeMeanMargin + 1.0e-10f);

                float4 positiveSafe0 = max(positive0 - positiveMeanMargin, 1.0e-10f);
                float4 positiveSafe1 = max(positive1 - positiveMeanMargin, 1.0e-10f);
                float4 positiveSafe2 = max(positive2 - positiveMeanMargin, 1.0e-10f);
                float4 positiveSafe3 = max(positive3 - positiveMeanMargin, 1.0e-10f);
                float4 negativeSafe0 = max(negative0 - negativeMeanMargin, 1.0e-10f);
                float4 negativeSafe1 = max(negative1 - negativeMeanMargin, 1.0e-10f);
                float4 negativeSafe2 = max(negative2 - negativeMeanMargin, 1.0e-10f);
                float4 negativeSafe3 = max(negative3 - negativeMeanMargin, 1.0e-10f);
                positiveLower = max(positiveLower - positiveMeanMargin, 1.0e-10f);
                positiveUpper += positiveMeanMargin;
                negativeLower = max(negativeLower - negativeMeanMargin, 1.0e-10f);
                negativeUpper += negativeMeanMargin;

                float4 positiveSecondUpper = max(max(positiveSecond0, positiveSecond1), max(positiveSecond2, positiveSecond3));
                float4 negativeSecondUpper = max(max(negativeSecond0, negativeSecond1), max(negativeSecond2, negativeSecond3));
                float4 positiveMomentMargin = max(positiveSecondUpper * VRCLV_SHADOW_CULL_RELATIVE_EPSILON, 1.0e-20f);
                float4 negativeMomentMargin = max(negativeSecondUpper * VRCLV_SHADOW_CULL_RELATIVE_EPSILON, 1.0e-20f);

                // R bounds each corner's second-moment / mean^2 ratio. Kantorovich then includes the additional between-corner variance of every convex blend.
                float4 positiveR = max(max(
                    (positiveSecond0 * VRCLV_SHADOW_CULL_MOMENT_INFLATION + positiveMomentMargin)
                        * rcp(positiveSafe0 * positiveSafe0),
                    (positiveSecond1 * VRCLV_SHADOW_CULL_MOMENT_INFLATION + positiveMomentMargin)
                        * rcp(positiveSafe1 * positiveSafe1)), max(
                    (positiveSecond2 * VRCLV_SHADOW_CULL_MOMENT_INFLATION + positiveMomentMargin)
                        * rcp(positiveSafe2 * positiveSafe2),
                    (positiveSecond3 * VRCLV_SHADOW_CULL_MOMENT_INFLATION + positiveMomentMargin)
                        * rcp(positiveSafe3 * positiveSafe3)));
                float4 negativeR = max(max(
                    (negativeSecond0 * VRCLV_SHADOW_CULL_MOMENT_INFLATION + negativeMomentMargin)
                        * rcp(negativeSafe0 * negativeSafe0),
                    (negativeSecond1 * VRCLV_SHADOW_CULL_MOMENT_INFLATION + negativeMomentMargin)
                        * rcp(negativeSafe1 * negativeSafe1)), max(
                    (negativeSecond2 * VRCLV_SHADOW_CULL_MOMENT_INFLATION + negativeMomentMargin)
                        * rcp(negativeSafe2 * negativeSafe2),
                    (negativeSecond3 * VRCLV_SHADOW_CULL_MOMENT_INFLATION + negativeMomentMargin)
                        * rcp(negativeSafe3 * negativeSafe3)));
                float4 positiveSum = positiveLower + positiveUpper;
                float4 negativeSum = negativeLower + negativeUpper;
                float4 positiveKantorovich = positiveSum * positiveSum * rcp(4.0f * positiveLower * positiveUpper);
                float4 negativeKantorovich = negativeSum * negativeSum * rcp(4.0f * negativeLower * negativeUpper);
                float4 positiveCv = sqrt(max(positiveR * positiveKantorovich - 1.0f, 0.0f));
                float4 negativeCv = sqrt(max(negativeR * negativeKantorovich - 1.0f, 0.0f));

                float4 positiveFactor = max(1.0f + k * positiveCv, positiveDenominatorReciprocal);
                float4 negativeFactor = min(1.0f - k * negativeCv, negativeDenominatorReciprocal);
                float4 maximumMeanRatio = max(max(
                    (positive0 + positiveMeanMargin) * rcp(negativeSafe0),
                    (positive1 + positiveMeanMargin) * rcp(negativeSafe1)), max(
                    (positive2 + positiveMeanMargin) * rcp(negativeSafe2),
                    (positive3 + positiveMeanMargin) * rcp(negativeSafe3)));
                float4 warpedRatio = maximumMeanRatio * positiveFactor * rcp(max(negativeFactor, 1.0e-20f));
                valid = valid & (positiveCv == positiveCv) & (negativeCv == negativeCv) & (negativeFactor > 0.0f) & (warpedRatio > 0.0f) & (warpedRatio <= 1.0e19f) & (warpedRatio == warpedRatio);
                float4 criticalDepth = log2(max(warpedRatio, 1.0e-20f)) * rcp(VRCLV_EVSM_COMBINED_EXPONENT_LOG2) + VRCLV_SHADOW_CULL_DEPTH_SAFETY;
                valid = valid & (criticalDepth == criticalDepth);
                return valid ? clamp(criticalDepth, -1.0f, VRCLV_SHADOW_CULL_SENTINEL) : float4(VRCLV_SHADOW_CULL_SENTINEL, VRCLV_SHADOW_CULL_SENTINEL, VRCLV_SHADOW_CULL_SENTINEL, VRCLV_SHADOW_CULL_SENTINEL);
            }

            // Bounds one original hardware-bilinear cell as four exact bilinear subcells. The synthetic midpoint moments are valid because hardware filtering is bilinear in all
            // four stored moments.  Taking positive/negative min inside each smaller domain,
            // followed by max across domains, preserves whichever EVSM warp is tighter locally;
            // bounding both warps over the complete cell first can lose that channel switch.
            float SubdividedCellCriticalDepth(float4 m00, float4 m10, float4 m01, float4 m11, float probability, float inverseProbability, float k, float positiveDenominatorReciprocal, float negativeDenominatorReciprocal) {
                float4 mMiddleTop = (m00 + m10) * 0.5f;
                float4 mMiddleBottom = (m01 + m11) * 0.5f;
                float4 mMiddleLeft = (m00 + m01) * 0.5f;
                float4 mMiddleRight = (m10 + m11) * 0.5f;
                float4 mCenter = (mMiddleTop + mMiddleBottom) * 0.5f;
                float4 thresholds = CriticalShadowDepth4(m00, mMiddleTop, m10, mMiddleLeft, mCenter, mMiddleRight, m01, mMiddleBottom, m11, probability, inverseProbability, k, positiveDenominatorReciprocal, negativeDenominatorReciprocal);
                return max(max(thresholds.x, thresholds.y), max(thresholds.z, thresholds.w));
            }

            // Evaluates the exact four mip-0 bilinear cells represented by one ordinary L1 node.
            float BuildFirstLevelBlock(uint slice, uint2 firstLevelPixel, uint sourceResolution, float probability, float inverseProbability, float k, float positiveDenominatorReciprocal, float negativeDenominatorReciprocal) {
                uint2 source = firstLevelPixel * 2u;
                uint maximumSourceIndex = sourceResolution - 1u;
                uint2 source2 = min(source + 2u, maximumSourceIndex);

                // Four bilinear cells start in this 2x2 core. Their right/top halo makes every possible hardware bilinear blend explicit without widening all nine samples into one unnecessarily loose statistical range.
                uint2 p00 = source;
                uint2 p10 = uint2(source.x + 1u, source.y);
                uint2 p20 = uint2(source2.x, source.y);
                uint2 p01 = uint2(source.x, source.y + 1u);
                uint2 p11 = source + 1u;
                uint2 p21 = uint2(source2.x, source.y + 1u);
                uint2 p02 = uint2(source.x, source2.y);
                uint2 p12 = uint2(source.x + 1u, source2.y);
                uint2 p22 = source2;
                float4 m00 = _UdonPointLightVolumeShadowTexture.Load(int4((int)p00.x, (int)p00.y, (int)slice, 0));
                float4 m10 = _UdonPointLightVolumeShadowTexture.Load(int4((int)p10.x, (int)p10.y, (int)slice, 0));
                float4 m20 = _UdonPointLightVolumeShadowTexture.Load(int4((int)p20.x, (int)p20.y, (int)slice, 0));
                float4 m01 = _UdonPointLightVolumeShadowTexture.Load(int4((int)p01.x, (int)p01.y, (int)slice, 0));
                float4 m11 = _UdonPointLightVolumeShadowTexture.Load(int4((int)p11.x, (int)p11.y, (int)slice, 0));
                float4 m21 = _UdonPointLightVolumeShadowTexture.Load(int4((int)p21.x, (int)p21.y, (int)slice, 0));
                float4 m02 = _UdonPointLightVolumeShadowTexture.Load(int4((int)p02.x, (int)p02.y, (int)slice, 0));
                float4 m12 = _UdonPointLightVolumeShadowTexture.Load(int4((int)p12.x, (int)p12.y, (int)slice, 0));
                float4 m22 = _UdonPointLightVolumeShadowTexture.Load(int4((int)p22.x, (int)p22.y, (int)slice, 0));

                // Keep the unsplit upper bounds beside the tighter subdivision and joint proofs. All three are independently conservative, so their per-cell minimum is too.
                float4 unsplitThresholds = CriticalShadowDepth4(m00, m10, m20, m01, m11, m21, m02, m12, m22, probability, inverseProbability, k, positiveDenominatorReciprocal, negativeDenominatorReciprocal);
                float4 jointThresholds = JointCriticalDepth4(m00, m10, m20, m01, m11, m21, m02, m12, m22, k, positiveDenominatorReciprocal, negativeDenominatorReciprocal);
                float4 originalCellThresholds = min(unsplitThresholds, jointThresholds);

                // Process the four original cells sequentially so the compiler can reuse one running scalar maximum instead of keeping all sixteen subcell proofs live.
                float maximumValue = min(originalCellThresholds.x, SubdividedCellCriticalDepth(m00, m10, m01, m11, probability, inverseProbability, k, positiveDenominatorReciprocal, negativeDenominatorReciprocal));
                maximumValue = max(maximumValue, min(originalCellThresholds.y, SubdividedCellCriticalDepth(m10, m20, m11, m21, probability, inverseProbability, k, positiveDenominatorReciprocal, negativeDenominatorReciprocal)));
                maximumValue = max(maximumValue, min(originalCellThresholds.z, SubdividedCellCriticalDepth(m01, m11, m02, m12, probability, inverseProbability, k, positiveDenominatorReciprocal, negativeDenominatorReciprocal)));
                maximumValue = max(maximumValue, min(originalCellThresholds.w, SubdividedCellCriticalDepth(m11, m21, m12, m22, probability, inverseProbability, k, positiveDenominatorReciprocal, negativeDenominatorReciprocal)));
                return maximumValue;
            }

            // Fuses up to five leading 2x2 max reductions into the expensive EVSM pass. Every ordinary L1 block is still evaluated exactly once; only intermediate writes vanish.
            float BuildFirstScratchLevel(uint slice, uint2 localPixel) {
                uint firstBuildLevel = (uint)_UdonShadowCullReceiverParams.w;
                if (firstBuildLevel < 1u || firstBuildLevel > 5u) return VRCLV_SHADOW_CULL_SENTINEL;

                float probability = _UdonShadowCullReceiverParams.x;
                float inverseProbability = _UdonShadowCullPackParams.x;
                float k = _UdonShadowCullPackParams.y;
                float positiveDenominatorReciprocal = _UdonShadowCullPackParams.z;
                float negativeDenominatorReciprocal = _UdonShadowCullPackParams.w;
                if (!(probability > 0.0f && probability < 1.0f) || !(inverseProbability > 1.0f && k >= 0.0f) || !(positiveDenominatorReciprocal >= 0.0f && negativeDenominatorReciprocal > 0.0f)) return VRCLV_SHADOW_CULL_SENTINEL;

                uint firstLevelBlockAxis = 1u << (firstBuildLevel - 1u);
                uint tileSize = (uint)_UdonShadowCullBuildParams.x;
                uint sourceResolution = tileSize << firstBuildLevel;
                uint2 firstLevelBase = localPixel * firstLevelBlockAxis;
                float maximumValue = -1.0f;
                [fastopt] for (uint offsetY = 0u; offsetY < firstLevelBlockAxis; offsetY++) {
                    [fastopt] for (uint offsetX = 0u; offsetX < firstLevelBlockAxis; offsetX++) {
                        maximumValue = max(maximumValue, BuildFirstLevelBlock(slice, firstLevelBase + uint2(offsetX, offsetY), sourceResolution, probability, inverseProbability, k, positiveDenominatorReciprocal, negativeDenominatorReciprocal));
                    }
                }
                return maximumValue;
            }

            float ReducePreviousLevel(uint slice, uint2 localPixel) {
                uint tileSize = (uint)_UdonShadowCullBuildParams.x;
                uint columnShift = (uint)_UdonShadowCullBuildParams.z;
                uint columnMask = (1u << columnShift) - 1u;
                uint2 tile = uint2(slice & columnMask, slice >> columnShift);
                uint2 source = tile * (tileSize * 2u) + localPixel * 2u;

                // Scratch textures are private to this pipeline; Build First already converts malformed EVSM input into the sentinel, so later max levels need only four loads.
                float value00 = _UdonShadowCullPrevious.Load(int3((int)source.x, (int)source.y, 0));
                float value10 = _UdonShadowCullPrevious.Load(int3((int)source.x + 1, (int)source.y, 0));
                float value01 = _UdonShadowCullPrevious.Load(int3((int)source.x, (int)source.y + 1, 0));
                float value11 = _UdonShadowCullPrevious.Load(int3((int)source.x + 1, (int)source.y + 1, 0));
                return max(max(value00, value10), max(value01, value11));
            }

            float FragmentBuildFirst(Varyings input) : SV_Target {
                uint slice;
                uint2 localPixel;
                if (!DecodeAtlasPixel(input.position.xy, slice, localPixel)) return VRCLV_SHADOW_CULL_SENTINEL;
                return BuildFirstScratchLevel(slice, localPixel);
            }

            float FragmentReduce(Varyings input) : SV_Target {
                uint slice;
                uint2 localPixel;
                if (!DecodeAtlasPixel(input.position.xy, slice, localPixel)) return VRCLV_SHADOW_CULL_SENTINEL;
                return ReducePreviousLevel(slice, localPixel);
            }

            Texture2D<float> _UdonShadowCullMip1;
            Texture2D<float> _UdonShadowCullMip2;
            Texture2D<float> _UdonShadowCullMip3;
            Texture2D<float> _UdonShadowCullMip4;
            Texture2D<float> _UdonShadowCullMip5;
            float LoadBuildLevel(uint level, int2 atlasPixel) {
                float value = VRCLV_SHADOW_CULL_SENTINEL;
                [branch] if (level == 1u) value = _UdonShadowCullMip1.Load(int3(atlasPixel, 0));
                else [branch] if (level == 2u) value = _UdonShadowCullMip2.Load(int3(atlasPixel, 0));
                else [branch] if (level == 3u) value = _UdonShadowCullMip3.Load(int3(atlasPixel, 0));
                else [branch] if (level == 4u) value = _UdonShadowCullMip4.Load(int3(atlasPixel, 0));
                else [branch] if (level == 5u) value = _UdonShadowCullMip5.Load(int3(atlasPixel, 0));
                return value;
            }

            // Flattens the useful hierarchy levels into one persistent level-major RFloat heap. It also finishes up to three coarse levels directly from a <=16x16 anchor, replacing three global reduction passes with at most 8x8 scalar loads.
            float PackHierarchy(float2 pixelPosition) {
                uint resolution = (uint)_UdonShadowCullPackParams.x;
                uint firstStoredLevel = min((uint)_UdonShadowCullPackParams.y, 12u);
                uint atlasWidthShift = (uint)_UdonShadowCullPackParams.z;
                uint totalNodeCount = (uint)_UdonShadowCullPackParams.w;
                uint buildLevelCount = (uint)_UdonShadowCullBuildParams.x;
                uint firstTileShift = (uint)_UdonShadowCullBuildParams.y;
                uint tileColumnShift = (uint)_UdonShadowCullBuildParams.z;
                uint sliceCount = (uint)_UdonShadowCullBuildParams.w;
                uint firstBuildLevel = (uint)_UdonShadowCullReceiverParams.w;
                uint anchorLevel = firstBuildLevel + buildLevelCount - 1u;
                uint resolutionShift = firstTileShift + firstBuildLevel;
                uint lastStoredLevel = resolution > 2u ? resolutionShift - 1u : 1u;
                uint2 pixel = (uint2)pixelPosition;
                uint linearIndex = (pixel.y << atlasWidthShift) + pixel.x;
                if (linearIndex >= totalNodeCount) return VRCLV_SHADOW_CULL_SENTINEL;

                uint remaining = linearIndex;
                uint levelSize = resolution >> firstStoredLevel;
                uint levelSizeShift = resolutionShift - firstStoredLevel;
                uint tileColumnMask = (1u << tileColumnShift) - 1u;

                [fastopt] for (uint level = firstStoredLevel; level <= 12u; level++) {
                    if (level > lastStoredLevel || levelSize == 0u) break;
                    uint nodesPerSlice = levelSize * levelSize;
                    uint levelNodeCount = sliceCount * nodesPerSlice;
                    if (remaining < levelNodeCount) {
                        uint nodeShift = levelSizeShift * 2u;
                        uint slice = remaining >> nodeShift;
                        uint node = remaining - (slice << nodeShift);
                        uint localY = node >> levelSizeShift;
                        uint localX = node - (localY << levelSizeShift);
                        uint2 tile = uint2(slice & tileColumnMask, slice >> tileColumnShift);
                        if (level <= anchorLevel) {
                            int2 sourcePixel = int2(tile * levelSize + uint2(localX, localY));
                            return LoadBuildLevel(level - firstBuildLevel + 1u, sourcePixel);
                        }

                        uint anchorSize = resolution >> anchorLevel;
                        uint reductionScale = 1u << (level - anchorLevel);
                        uint2 anchorBase = tile * anchorSize + uint2(localX, localY) * reductionScale;
                        float maximumValue = -1.0f;
                        [fastopt] for (uint offsetY = 0u; offsetY < reductionScale; offsetY++) {
                            [fastopt] for (uint offsetX = 0u; offsetX < reductionScale; offsetX++) {
                                float value = _UdonShadowCullPrevious.Load(int3(int2(anchorBase + uint2(offsetX, offsetY)), 0));
                                maximumValue = max(maximumValue, value);
                            }
                        }
                        return maximumValue;
                    }
                    remaining -= levelNodeCount;
                    levelSize >>= 1u;
                    if (levelSizeShift > 0u) levelSizeShift--;
                }
                return VRCLV_SHADOW_CULL_SENTINEL;
            }

            float FragmentPack(Varyings input) : SV_Target {
                return PackHierarchy(input.position.xy);
            }
        ENDCG

        Pass {
            Name "Build First"
            CGPROGRAM
            #pragma target 3.5
            #pragma only_renderers d3d11 glcore vulkan gles3 metal
            #pragma vertex Vertex
            #pragma fragment FragmentBuildFirst
            #pragma require integers
            ENDCG
        }

        Pass {
            Name "Reduce"
            CGPROGRAM
            #pragma target 3.5
            #pragma only_renderers d3d11 glcore vulkan gles3 metal
            #pragma vertex Vertex
            #pragma fragment FragmentReduce
            #pragma require integers
            ENDCG
        }

        Pass {
            Name "Pack"
            CGPROGRAM
            #pragma target 3.5
            #pragma only_renderers d3d11 glcore vulkan gles3 metal
            #pragma vertex Vertex
            #pragma fragment FragmentPack
            #pragma require integers
            ENDCG
        }
    }
    Fallback Off
}
