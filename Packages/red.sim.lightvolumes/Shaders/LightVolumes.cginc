#ifndef VRC_LIGHT_VOLUMES_INCLUDED
#define VRC_LIGHT_VOLUMES_INCLUDED
#define VRCLV_VERSION 3
#define VRCLV_MIN_SUPPORTED_VERSION 2
#define VRCLV_MAX_VOLUMES_COUNT 32
#define VRCLV_MAX_LIGHTS_COUNT 128


#ifndef SHADER_TARGET_SURFACE_ANALYSIS
cbuffer LightVolumeUniforms {
#endif

// Are Light Volumes enabled on scene? can be 0 or 1
uniform float _UdonLightVolumeEnabled;

// Returns 1, 2 or other number if there are light volumes on the scene. Number represents the light volumes system internal version number.
uniform float _UdonLightVolumeVersion;

// All volumes count in scene
uniform float _UdonLightVolumeCount;

// Additive volumes max overdraw count
uniform float _UdonLightVolumeAdditiveMaxOverdraw;

// Additive volumes count
uniform float _UdonLightVolumeAdditiveCount;

// Should volumes be blended with lightprobes?
uniform float _UdonLightVolumeProbesBlend;

// Should volumes be with sharp edges when not blending with each other
uniform float _UdonLightVolumeSharpBounds;

// World to Local (-0.5, 0.5) UVW Matrix 4x4
uniform float4x4 _UdonLightVolumeInvWorldMatrix[VRCLV_MAX_VOLUMES_COUNT];

// L1 SH matrix rotation relative to baked rotation. Stores row 0 and row 1; row 2 is reconstructed in shader.
uniform float4 _UdonLightVolumeRotation[VRCLV_MAX_VOLUMES_COUNT * 2];

// Value that is needed to smoothly blend volumes ( BoundsScale / edgeSmooth )
uniform float3 _UdonLightVolumeInvLocalEdgeSmooth[VRCLV_MAX_VOLUMES_COUNT];

// AABB bounds of islands on the 3D Texture atlas. XYZ: UvwMin, W: Scale per axis
uniform float4 _UdonLightVolumeUvwScale[VRCLV_MAX_VOLUMES_COUNT * 3];

// Legacy shader compatibility upload. Current cginc does not use volume occlusion data.
uniform float _UdonLightVolumeOcclusionCount;

// Color multiplier (RGB) | If we actually need to rotate L1 components at all (A)
uniform float4 _UdonLightVolumeColor[VRCLV_MAX_VOLUMES_COUNT];

// Point Lights count
uniform float _UdonPointLightVolumeCount;

// Cubemaps count in the custom textures array
uniform float _UdonPointLightVolumeCubeCount;

// Shadow cubemaps count in the shadow texture array
uniform float _UdonPointLightVolumeShadowCubeCount;

// Total shadow maps count in the shadow texture array
uniform float _UdonPointLightVolumeShadowCount;

// VSM light bleed reduction amount. 0 disables reduction, 1 is strongest.
uniform float _UdonPointLightVolumeShadowBleedReduction;
// VSM minimum variance clamp.
uniform float _UdonPointLightVolumeShadowMinVariance;

// For point light: XYZ = Position, W = Inverse squared range
// For spot light: XYZ = Position, W = Inverse squared range, negated
// For area light: XYZ = Position, W = Width
uniform float4 _UdonPointLightVolumePosition[VRCLV_MAX_LIGHTS_COUNT];

// For point light: XYZ = Color, W = Cos of angle (for LUT)
// For spot light: XYZ = Color, W = Cos of outer angle if no custom texture, tan of outer angle otherwise
// For area light: XYZ = Color, W = 2 + Height
uniform float4 _UdonPointLightVolumeColor[VRCLV_MAX_LIGHTS_COUNT];
// Shared cookie data. RGB = area cookie Color * Intensity, W = custom spotlight cookie width / height aspect.
uniform float4 _UdonPointLightVolumeCookieData[VRCLV_MAX_LIGHTS_COUNT];

// For point light: XYZW = Rotation quaternion
// For spot light: XYZ = Direction, W = Cone falloff
// For area light: XYZW = Rotation quaternion
uniform float4 _UdonPointLightVolumeDirection[VRCLV_MAX_LIGHTS_COUNT];

// X = Custom ID:
//   If parametric: X stores 0
//   If uses custom lut: X stores LUT ID with positive sign
//   If uses custom texture: X stores texture ID with negative sign
// Y = shadow map ID when _UdonLightVolumeOcclusionCount is 0. Fraction stores inverted shading strength. Abs >= 10000 disables shading.
// Z = Squared Culling Range. Just a precalculated culling range to not recalculate it in shader.
// W = shadow far clip used to normalize shadow depth. 0 for lights without shadows.
uniform float4 _UdonPointLightVolumeCustomID[VRCLV_MAX_LIGHTS_COUNT];

// For World Space Shadows:
//   XYZ = shadow bake position in world space.
//   W = shadow projection tangent half-angle for single texture shadows.
uniform float4 _UdonPointLightVolumeShadowReprojectionData[VRCLV_MAX_LIGHTS_COUNT];

//   XYZW = Rotation from current world space to baked shadow space.
uniform float4 _UdonPointLightVolumeShadowRotationData[VRCLV_MAX_LIGHTS_COUNT];

// If we are far enough from a light that the irradiance
// is guaranteed lower than the threshold defined by this value,
// we cull the light.
uniform float _UdonLightBrightnessCutoff;

#ifndef SHADER_TARGET_SURFACE_ANALYSIS
}
#endif

// Texel count and max mip for Area Light with cookie
uniform float _UdonPointLightVolumeTextureTexelCount;
uniform float _UdonPointLightVolumeTextureMaxMip;

#ifndef SHADER_TARGET_SURFACE_ANALYSIS

// Main 3D Texture atlas
uniform Texture3D _UdonLightVolume;
uniform SamplerState sampler_UdonLightVolume;
// First elements must be cubemap faces (6 face textures per cubemap). Then goes other textures
uniform Texture2DArray _UdonPointLightVolumeTexture;
uniform SamplerState sampler_UdonPointLightVolumeTexture;
// First elements are baked shadow cubemap faces, 6 face textures per cubemap.
uniform Texture2DArray _UdonPointLightVolumeShadowTexture;
uniform SamplerState sampler_UdonPointLightVolumeShadowTexture;
// Samples textures using mip 0. Shadow maps keep their own sampler state so they always use the shadow texture wrap mode.
#define LV_SAMPLE(tex, uvw) tex.SampleLevel(sampler_UdonLightVolume, uvw, 0)
#define LV_SAMPLE_POINT(uvw) _UdonPointLightVolumeTexture.SampleLevel(sampler_UdonPointLightVolumeTexture, uvw, 0)
#define LV_SAMPLE_POINT_LOD(uvw, lod) _UdonPointLightVolumeTexture.SampleLevel(sampler_UdonPointLightVolumeTexture, uvw, lod)
#define LV_SAMPLE_SHADOW(uvw) _UdonPointLightVolumeShadowTexture.SampleLevel(sampler_UdonPointLightVolumeShadowTexture, uvw, 0)

#else

// Dummy macro definition to satisfy MojoShader (surface shaders)
#define LV_SAMPLE(tex, uvw) float4(0,0,0,0)
#define LV_SAMPLE_POINT(uvw) float4(0,0,0,0)
#define LV_SAMPLE_POINT_LOD(uvw, lod) float4(0,0,0,0)
#define LV_SAMPLE_SHADOW(uvw) float4(0,0,0,0)

#endif

#define LV_PI 3.141592653589793f
#define LV_INV_PI 0.3183098861837907f
#define LV_PI2 6.283185307179586f

// Smoothstep to 0, 1 but cheaper
float LV_Smoothstep01(float x) {
    return x * x * (3 - 2 * x);
}

// Approximate log2 using frexp exponent extraction and a quadratic mantissa fit. Max error is below 0.008 log2 units.
float LV_FastLog2Positive(float x) {
    float exponent = 0;
    float mantissa = frexp(max(x, 1.0), exponent);
    float y = mantissa + mantissa - 1.0;
    return exponent - 1.0 + y * (1.3465554 - 0.3465554 * y);
}

// Rotates vector by Quaternion
float3 LV_MultiplyVectorByQuaternion(float3 v, float4 q) {
    float3 t = 2 * cross(q.xyz, v);
    return v + q.w * t + cross(q.xyz, t);
}

// Builds orthonormal axes from a normalized quaternion
void LV_QuaternionAxes(float4 q, out float3 xAxis, out float3 yAxis, out float3 zAxis) {
    float x2 = q.x + q.x;
    float y2 = q.y + q.y;
    float z2 = q.z + q.z;
    float xx = q.x * x2;
    float yy = q.y * y2;
    float zz = q.z * z2;
    float xy = q.x * y2;
    float xz = q.x * z2;
    float yz = q.y * z2;
    float wx = q.w * x2;
    float wy = q.w * y2;
    float wz = q.w * z2;
    xAxis = float3(1 - yy - zz, xy + wz, xz - wy);
    yAxis = float3(xy - wz, 1 - xx - zz, yz + wx);
    zAxis = float3(xz + wy, yz - wx, 1 - xx - yy);
}

// Rotates vector by Matrix 3x3 with precomputed third axis
float3 LV_MultiplyVectorByMatrix3x3(float3 v, float3 r0, float3 r1, float3 r2) {
    return float3(dot(v, r0), dot(v, r1), dot(v, r2));
}

// Fast approximate arctangent for positive values. Max error is small enough for area light attenuation
float LV_FastAtanPositive(float x) {
    if (x <= 1) { // atan small
        return x * rcp(1 + 0.280872 * x * x);
    } else { // atan large
        float invX = rcp(max(x, 1e-6));
        return LV_PI * 0.5 - invX * rcp(1 + 0.280872 * invX * invX);
    }
}

// Forms specular based on roughness
float LV_DistributionGGX(float NoH, float roughness) {
    float a2 = roughness * roughness;
    float f = (a2 - 1) * (NoH * NoH) + 1;
    return a2 * LV_INV_PI * rcp(f * f);
}

// Checks if local UVW point is in bounds from -0.5 to +0.5
bool LV_PointLocalAABB(float3 localUVW) {
    return all(abs(localUVW) <= 0.5);
}

// Calculates local UVW using volume ID
float3 LV_LocalFromVolume(uint volumeID, float3 worldPos) {
    return mul(_UdonLightVolumeInvWorldMatrix[volumeID], float4(worldPos, 1)).xyz;
}

// Linear single SH L1 channel evaluation
float LV_EvaluateSH(float L0, float3 L1, float3 n) {
    return L0 + dot(L1, n);
}

// Projects a cubemap direction into face index and face UV. (xy: uv, z: face)
float3 LV_CubemapUvFace(float3 dir) {
    float2 uv;
    float face;
    float3 absDir = abs(dir);
    [branch] if (absDir.x >= absDir.y && absDir.x >= absDir.z) {
        face = dir.x > 0 ? 0 : 1;
        uv = float2((dir.x > 0 ? -dir.z : dir.z), -dir.y) * rcp(absDir.x);
    } else [branch] if (absDir.y >= absDir.z) {
        face = dir.y > 0 ? 2 : 3;
        uv = float2(dir.x, (dir.y > 0 ? dir.z : -dir.z)) * rcp(absDir.y);
    } else {
        face = dir.z > 0 ? 4 : 5;
        uv = float2((dir.z > 0 ? dir.x : -dir.x), -dir.y) * rcp(absDir.z);
    }
    return float3(uv * 0.5 + 0.5, face);
}

// Samples a cubemap from _UdonPointLightVolumeTexture array
float4 LV_SampleCubemapArray(uint id, float3 dir) {
    return LV_SAMPLE_POINT(LV_CubemapUvFace(dir) + float3(0, 0, id * 6));
}

// Samples a shadow map array face using face UV
float4 LV_SampleShadowMapArrayFace(uint id, uint face, float2 uv) {
    return LV_SAMPLE_SHADOW(float3(uv, id * 6 + face));
}

// Remaps visibility to suppress VSM-style light bleeding.
float LV_ReduceLightBleeding(float visibility, float amount) {
    amount = saturate(amount);
    float edge = min(amount, 0.999f);
    float t = saturate((visibility - edge) * rcp(1.0f - edge));
    return LV_Smoothstep01(t);
}

// Evaluates the one-tailed Chebyshev upper bound used by VSM.
float LV_VSMChebyshevUpperBound(float2 moments, float mean, float minVariance, float bleedReduction) {
    float variance = max(moments.y - moments.x * moments.x, minVariance);
    float d = mean - moments.x;
    float pMax = variance * rcp(variance + d * d);
    pMax = LV_ReduceLightBleeding(pMax, bleedReduction);
    return mean <= moments.x ? 1.0f : pMax;
}

// Evaluates filtered VSM visibility.
float LV_ShadowMoments(float2 moments, float distanceToShadowCenter, float farClip) {
    float normalizedDepth = saturate(distanceToShadowCenter * rcp(max(farClip, 0.0001f)));
    return LV_VSMChebyshevUpperBound(moments, normalizedDepth, max(_UdonPointLightVolumeShadowMinVariance, 0.0f), saturate(_UdonPointLightVolumeShadowBleedReduction));
}

// Samples the per-light shadow map and returns attenuation
float LV_PointLightShadow(uint id, float3 worldPos, float3 dirN, float sqDistanceToLight, float invDistanceToLight, float shadowFarClip, float shadowIdData, uint shadowId) {
    uint shadowCubeCount = (uint)_UdonPointLightVolumeShadowCubeCount;
    float4 shadowRotationData = _UdonPointLightVolumeShadowRotationData[id];
    float attenuation = 1;
    [branch] if (shadowId >= shadowCubeCount) { // Single slice shadows

        float4 shadowReprojectionData = _UdonPointLightVolumeShadowReprojectionData[id];
        float3 localDir = 0;
        float distanceToShadowCenter = 0;

        [branch] if (shadowIdData < 0) { // Local-space shadow
            distanceToShadowCenter = sqDistanceToLight * invDistanceToLight;
            localDir = LV_MultiplyVectorByQuaternion(-dirN, shadowRotationData);
        } else { // World-space shadow
            float3 bakeOffset = worldPos - shadowReprojectionData.xyz;
            float bakeSqLen = dot(bakeOffset, bakeOffset);
            [branch] if (bakeSqLen > 0.0001) { // Ignore degenerate vectors before normalizing baked direction
                float invBakeLen = rsqrt(bakeSqLen);
                distanceToShadowCenter = bakeSqLen * invBakeLen;
                localDir = LV_MultiplyVectorByQuaternion(bakeOffset * invBakeLen, shadowRotationData);
            }
        }
        [branch] if (localDir.z > 0) { // Only sample receivers in front of the baked spot shadow camera
            float2 uv = localDir.xy * rcp(localDir.z * max(shadowReprojectionData.w, 0.0001f));
            [branch] if (max(abs(uv.x), abs(uv.y)) <= 1) { // Only sample inside the projected single shadow texture
                attenuation = LV_ShadowMoments(LV_SAMPLE_SHADOW(float3(uv * 0.5 + 0.5, shadowId + shadowCubeCount * 5)).xy, distanceToShadowCenter, shadowFarClip);
            }
        }

    } else { // Cubemap shadows
        float3 sampleDir = dirN;
        float distanceToShadowCenter = sqDistanceToLight * invDistanceToLight;

        [branch] if (shadowIdData < 0) { // Local-space shadows
            sampleDir = LV_MultiplyVectorByQuaternion(dirN, shadowRotationData);
        } else { // World-space shadows
            float4 shadowReprojectionData = _UdonPointLightVolumeShadowReprojectionData[id];
            float3 bakeDir = shadowReprojectionData.xyz - worldPos;
            float bakeSqLen = dot(bakeDir, bakeDir);
            [branch] if (bakeSqLen > 0.0001) { // Ignore degenerate vectors before normalizing baked cubemap direction
                float invBakeLen = rsqrt(bakeSqLen);
                sampleDir = LV_MultiplyVectorByQuaternion(bakeDir * invBakeLen, shadowRotationData);
                distanceToShadowCenter = bakeSqLen * invBakeLen;
            }
        }
        float3 uvFace = LV_CubemapUvFace(sampleDir);
        attenuation = LV_ShadowMoments(LV_SampleShadowMapArrayFace(shadowId, (uint)uvFace.z, uvFace.xy).xy, distanceToShadowCenter, shadowFarClip);

    }
    return attenuation;
}

// Projects a quad light into L1 SH using a cheap solid-angle approximation
float4 LV_ProjectFastQuadLightIrradianceSH(float3 lightToWorldPos, float3 localPos, float3 xAxis, float3 yAxis, float2 size, out float3 normalMaskDir) {
    [branch] if (localPos.z <= 0) {
        normalMaskDir = 0;
        return 0;
    } else {
        float2 halfSize = size * 0.5;
        float area = max(size.x * size.y, 1e-6);
        float extentSq = max(dot(halfSize, halfSize), 1e-6);

        float2 closestXY = clamp(localPos.xy, -halfSize, halfSize);
        float2 rectDelta = localPos.xy - closestXY;
        float rectDeltaSq = dot(rectDelta, rectDelta);
        float planeRectSq = rectDeltaSq + localPos.z * localPos.z;
        float closestSqDist = max(planeRectSq, 1e-6);
        float centerSqDist = max(dot(localPos, localPos), 1e-6);

        float distanceBlend = planeRectSq * rcp(planeRectSq + extentSq);
        float solidSqDist = lerp(closestSqDist, centerSqDist, distanceBlend);
        float invSolidDist = rsqrt(solidSqDist);
        float invExtendedDist = rsqrt(solidSqDist + extentSq);

        float solidAngle = LV_FastAtanPositive(area * localPos.z * invSolidDist * invSolidDist * invExtendedDist * 0.25);
        float l0 = solidAngle * LV_INV_PI;

        float2 representativeXY = lerp(closestXY, 0, distanceBlend);
        float3 dir = xAxis * representativeXY.x + yAxis * representativeXY.y - lightToWorldPos;
        dir *= rsqrt(max(dot(dir, dir), 1e-6));
        normalMaskDir = dir;
        return float4(dir * (l0 * saturate(1 - l0)), l0);
    }
}

// Calculates point light attenuation.
float3 LV_PointLightAttenuation(float sqdist, float sqlightSize, float3 color, float sqMaxDist) {
    float mask = saturate(1 - sqdist * rcp(sqMaxDist));
    return mask * mask * color * sqlightSize * rcp(sqdist + sqlightSize);
}

// Calculates point light solid angle coefficient
float LV_PointLightSolidAngle(float sqdist, float sqlightSize) {
    return saturate(sqrt(sqdist * rcp(sqlightSize + sqdist)));
}

// Calculates normal mask for a point or a spot light
float LV_PointLightNormalMask(float3 worldNormal, float3 lightDirNormal) {
    return LV_Smoothstep01(saturate(dot(worldNormal, lightDirNormal) * 2 + 0.5));
}

// Calculates normal mask for an area light
float LV_AreaLightNormalMask(float3 worldNormal, float3 representativeLightDirNormal) {
    return LV_Smoothstep01(saturate(dot(worldNormal, representativeLightDirNormal) * 2 + 0.5));
}

// Resolves spot cookie UV and culls fragments outside the projected cookie before expensive shadow work.
float2 LV_SphereSpotLightCookieUv(float3 dirN, float4 lightRot, float tanAngle) {
    float3 localDir = LV_MultiplyVectorByQuaternion(-dirN, lightRot);
    [branch] if (localDir.z <= 0) {
        return 2; // Just to cull later
    } else {
        return localDir.xy * rcp(localDir.z * tanAngle);
    }
}

// Samples a textured area emitter as a prefiltered mip-chain average: local high-frequency detail is blended with all available coarser mip levels to approximate the solid-angle tail from neighboring texels.
// Based on textured LTC prefiltering and filtered importance sampling:
// https://eheitzresearch.wordpress.com/415-2/
// https://developer.nvidia.com/gpugems/gpugems3/part-iii-rendering/chapter-20-gpu-based-importance-sampling
float4 LV_AreaLightCookie(float3 localPos, float2 size, uint textureId) {
    float2 safeSize = max(size, float2(0.0001, 0.0001));
    float2 halfSize = safeSize * 0.5;
    float2 closestXY = clamp(localPos.xy, -halfSize, halfSize);
    float2 rectDelta = localPos.xy - closestXY;
    float planeRectSq = dot(rectDelta, rectDelta) + localPos.z * localPos.z;
    float lightArea = max(safeSize.x * safeSize.y, 0.000001);
    float invLightArea = rcp(lightArea);
    float textureTexelCount = max(_UdonPointLightVolumeTextureTexelCount, 1.0);
    float filterArea = max(planeRectSq * LV_PI, lightArea * rcp(textureTexelCount));
    float texelsCovered = max(filterArea * textureTexelCount * invLightArea, 1.0);
    float shapeBlend = saturate(sqrt(filterArea * invLightArea));
    float2 representativeXY = closestXY * (1.0 - shapeBlend);

    float2 edgeUv = abs(representativeXY) * (2.0 * rcp(safeSize));
    float edgeBlend = LV_Smoothstep01(saturate((max(edgeUv.x, edgeUv.y) - 0.65) * 2.8571429));
    float grazingBlend = saturate(1 - abs(localPos.z) * rsqrt(max(dot(localPos, localPos), 0.000001)));
    float mip = 0.5 * LV_FastLog2Positive(texelsCovered) + edgeBlend * grazingBlend * (1.0 - shapeBlend) * 2.0;
    float maxMip = max(_UdonPointLightVolumeTextureMaxMip, 0.0);
    mip = min(mip, maxMip);

    float2 uv = representativeXY * rcp(safeSize) + 0.5;
    uv.x = 1.0 - uv.x;

    // Making 4 samples with different mip offsets and weights for the smoother result. All the values were tuned by hand.
    float mipRange = max(maxMip - mip, 0.0);
    float tap2Mip = mip + mipRange * 0.3;
    float tap3Mip = mip + mipRange * 0.5;
    float4 tap1Cookie = LV_SAMPLE_POINT_LOD(float3(uv, textureId), mip);
    float4 tap2Cookie = LV_SAMPLE_POINT_LOD(float3(uv, textureId), tap2Mip);
    float4 tap3Cookie = LV_SAMPLE_POINT_LOD(float3(uv, textureId), tap3Mip);
    float4 tap4Cookie = LV_SAMPLE_POINT_LOD(float3(uv, textureId), maxMip);
    float3 tap1Emission = tap1Cookie.rgb * tap1Cookie.a;
    float3 tap2Emission = tap2Cookie.rgb * tap2Cookie.a;
    float3 tap3Emission = tap3Cookie.rgb * tap3Cookie.a;
    float3 tap4Emission = tap4Cookie.rgb * tap4Cookie.a;
    float3 emission = tap1Emission + tap2Emission * 0.75 + tap3Emission * 0.55 + tap4Emission * 0.45;
    return float4(emission * 0.36363636, 1.0);
}

// Samples a spot light, point light, area light and returns true when this light consumes one overdraw slot.
bool LV_PointLightContribution(uint id, float3 worldPos, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b, float3 worldNormal = 0) {

    // IDs and range data
    float4 customID_data = _UdonPointLightVolumeCustomID[id];
    int customId = (int) customID_data.x; // Custom Texture ID
    float sqrRange = customID_data.z; // Squared culling distance

    float4 pos = _UdonPointLightVolumePosition[id]; // Light position and inversed squared range
    float3 dir = pos.xyz - worldPos;
    float sqlen = max(dot(dir, dir), 1e-6);

    [branch] if (sqlen <= sqrRange) { // Is in range
        float4 color = _UdonPointLightVolumeColor[id]; // Color, angle
        float shadowIdData = customID_data.y;
        float shadowIdAbs = abs(shadowIdData);
        bool hasShading = shadowIdAbs < 10000;
        float shadingStrength = hasShading ? 1 - frac(shadowIdAbs) : 0;

        float angle = color.w;
        float spotMask = 0;
        float spotConeFalloff = 0;
        float3 att = 0;
        float4 cookie = float4(1, 1, 1, 1);
        float4 areaLightSH = 0;
        float areaAttenuation = 0;
        float normalAttenuation = 1;
        bool useNormalMask = hasShading && any(worldNormal != 0);
        bool isSpotLight = pos.w < 0;
        bool isPointLight = color.w <= 1.5;
        bool lightVisible = false;
        bool counted = false;

        float invLen = rsqrt(sqlen);
        float3 dirN = dir * invLen;

        [branch] if (isSpotLight) { // It is a spot light

            float2 cookieUv = 0;
            [branch] if (customId >= 0) {
                float4 directionData = _UdonPointLightVolumeDirection[id]; // Dir + falloff
                spotMask = dot(directionData.xyz, -dirN) - angle;
                spotConeFalloff = directionData.w;
                lightVisible = spotMask >= 0;
            } else {
                float4 directionData = _UdonPointLightVolumeDirection[id]; // Rotation
                cookieUv = LV_SphereSpotLightCookieUv(dirN, directionData, angle);
                cookieUv.y *= max(_UdonPointLightVolumeCookieData[id].w, 0.001);
                lightVisible = all(abs(cookieUv) <= 1);
            }

            // Normal Mask
            if (lightVisible && useNormalMask) {
                normalAttenuation = LV_PointLightNormalMask(worldNormal, dirN);
                lightVisible = normalAttenuation > 0 || shadingStrength < 1;
            }
            [branch] if (lightVisible) {
                counted = true;
                [branch] if (customId > 0) { // If it uses Attenuation LUT

                    float dirRadius = sqlen * abs(pos.w);
                    float spot = 1 - saturate(spotMask * rcp(1 - angle));
                    uint textureId = (uint) _UdonPointLightVolumeCubeCount * 5 + customId - 1;
                    float3 lutUv = float3(sqrt(float2(spot, dirRadius)), textureId);
                    att = color.rgb * LV_SAMPLE_POINT(lutUv).xyz;
                    lightVisible = max(max(att.r, att.g), att.b) > 0;

                } else { // If it uses default parametric attenuation

                    att = LV_PointLightAttenuation(sqlen, -pos.w, color.rgb, sqrRange);
                    [branch] if (customId < 0) { // If uses cookie
                        uint textureId = (uint) _UdonPointLightVolumeCubeCount * 5 - customId - 1;
                        cookie = LV_SAMPLE_POINT(float3(cookieUv * 0.5 + 0.5, textureId));
                        lightVisible = min(cookie.a, max(max(cookie.r, cookie.g), cookie.b)) > 0;
                    }
                }
            }

        } else [branch] if (isPointLight) { // It is a point light

            lightVisible = true;
            // Normal Mask
            if (useNormalMask) {
                normalAttenuation = LV_PointLightNormalMask(worldNormal, dirN);
                lightVisible = normalAttenuation > 0 || shadingStrength < 1;
            }

            [branch] if (lightVisible) {
                counted = true;
                [branch] if (customId > 0) { // Using LUT

                    float dirRadius = sqlen * abs(pos.w);
                    uint textureId = (uint) _UdonPointLightVolumeCubeCount * 5 + customId - 1;
                    float3 uvid = float3(sqrt(float2(0, dirRadius)), textureId);
                    att = color.rgb * LV_SAMPLE_POINT(uvid).xyz;
                    lightVisible = max(max(att.r, att.g), att.b) > 0;

                } else { // If it uses default parametric attenuation
                    att = LV_PointLightAttenuation(sqlen, pos.w, color.rgb, sqrRange);
                }
            }

        } else { // It is an area light

            float3 areaNormal;
            float4 areaRotation = _UdonPointLightVolumeDirection[id]; // Rotation
            float3 lightToWorldPos = worldPos - pos.xyz;
            float2 areaSize = float2(pos.w, color.w - 2);
            float3 areaXAxis;
            float3 areaYAxis;
            LV_QuaternionAxes(areaRotation, areaXAxis, areaYAxis, areaNormal);
            float3 areaLocalPos = float3(dot(lightToWorldPos, areaXAxis), dot(lightToWorldPos, areaYAxis), dot(lightToWorldPos, areaNormal));
            float3 areaNormalMaskDir;
            areaLightSH = LV_ProjectFastQuadLightIrradianceSH(lightToWorldPos, areaLocalPos, areaXAxis, areaYAxis, areaSize, areaNormalMaskDir);
            areaAttenuation = saturate((sqrRange - sqlen) * rcp(sqrRange));
            lightVisible = areaLightSH.w > 0 && areaAttenuation > 0;

            // Area cookies use filtered projection; no-cookie area lights keep the original fast quad path.
            [branch] if (customId < 0 && lightVisible) {
                uint textureId = (uint) _UdonPointLightVolumeCubeCount * 5 - customId - 1;
                cookie = LV_AreaLightCookie(areaLocalPos, areaSize, textureId);
                color.rgb = _UdonPointLightVolumeCookieData[id].rgb;
                lightVisible = min(cookie.a, max(max(cookie.r, cookie.g), cookie.b)) > 0;
            }

            // Normal Mask
            if (lightVisible && useNormalMask) {
                normalAttenuation = LV_AreaLightNormalMask(worldNormal, areaNormalMaskDir);
                lightVisible = normalAttenuation > 0 || shadingStrength < 1;
            }
            if (lightVisible) {
                counted = true;
            }
        }

        [branch] if (lightVisible) {
            // Add shadow-like visibility masks first, then apply Shading Strength once to the combined result
            float combinedAttenuation = normalAttenuation;
            float shadowIndex = floor(shadowIdAbs) - 1;
            bool hasShadow = hasShading && _UdonLightVolumeOcclusionCount == 0 && shadowIndex >= 0 && shadowIndex < _UdonPointLightVolumeShadowCount;
            [branch] if (hasShadow) {
                combinedAttenuation = max(0, combinedAttenuation + LV_PointLightShadow(id, worldPos, dirN, sqlen, invLen, customID_data.w, shadowIdData, (uint)shadowIndex) - 1);
            }
            float lightAttenuation = lerp(1, combinedAttenuation, shadingStrength);

            [branch] if (lightAttenuation > 0) {
                [branch] if (isSpotLight) { // Accumulate spot light contribution

                    att *= lightAttenuation;
                    [branch] if (customId > 0) { // LUT spot light already provides RGB attenuation

                        L0 += att;
                        L1r += dirN * att.r;
                        L1g += dirN * att.g;
                        L1b += dirN * att.b;

                    } else [branch] if (customId < 0) { // Textured spot light uses the cookie as color and alpha mask

                        float3 l0 = att * cookie.rgb * cookie.a;
                        float3 l1 = dirN * LV_PointLightSolidAngle(sqlen, -pos.w * (1 - saturate(rsqrt(1 + angle * angle))));
                        L0 += l0;
                        L1r += l0.r * l1;
                        L1g += l0.g * l1;
                        L1b += l0.b * l1;

                    } else { // Default spot light uses parametric cone smoothing

                        float3 l0 = att * LV_Smoothstep01(saturate(spotMask * spotConeFalloff));
                        float3 l1 = dirN * LV_PointLightSolidAngle(sqlen, -pos.w * saturate(1 - angle));
                        L0 += l0;
                        L1r += l0.r * l1;
                        L1g += l0.g * l1;
                        L1b += l0.b * l1;
                    }

                } else [branch] if (isPointLight) { // Accumulate point light contribution

                    att *= lightAttenuation;
                    [branch] if (customId > 0) { // LUT point light already provides RGB attenuation

                        L0 += att;
                        L1r += dirN * att.r;
                        L1g += dirN * att.g;
                        L1b += dirN * att.b;

                    } else { // Default point light can be optionally tinted by a cubemap

                        float3 l1 = dirN * LV_PointLightSolidAngle(sqlen, pos.w);
                        [branch] if (customId < 0) { // If it uses a cubemap
                            uint cubeId = -customId - 1; // Cubemap ID starts from zero and should not take in count texture array slices count
                            float4 cubeRotation = _UdonPointLightVolumeDirection[id]; // Rotation
                            float3 cubeColor = LV_SampleCubemapArray(cubeId, LV_MultiplyVectorByQuaternion(dirN, cubeRotation)).xyz;
                            L0 += att * cubeColor;
                            L1r += att.r * l1 * cubeColor.r;
                            L1g += att.g * l1 * cubeColor.g;
                            L1b += att.b * l1 * cubeColor.b;
                        } else {
                            L0 += att;
                            L1r += att.r * l1;
                            L1g += att.g * l1;
                            L1b += att.b * l1;
                        }
                    }

                } else { // Accumulate quad area light contribution

                    float3 areaColor = color.rgb * (areaAttenuation * LV_PI * lightAttenuation);
                    areaColor *= cookie.rgb * cookie.a;
                    L0 += areaLightSH.w * areaColor;
                    L1r += areaLightSH.xyz * areaColor.r;
                    L1g += areaLightSH.xyz * areaColor.g;
                    L1b += areaLightSH.xyz * areaColor.b;

                }
            }
        }
        return counted;
    } else {
        return false;
    }

}

// Samples 3 SH textures and packing them into L1 channels
void LV_SampleLightVolumeTex(float3 uvw0, float3 uvw1, float3 uvw2, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b) {
    // Sampling 3D Atlas
    float4 tex0 = LV_SAMPLE(_UdonLightVolume, uvw0);
    float4 tex1 = LV_SAMPLE(_UdonLightVolume, uvw1);
    float4 tex2 = LV_SAMPLE(_UdonLightVolume, uvw2);
    // Packing final data
    L0 = tex0.rgb;
    L1r = float3(tex1.r, tex2.r, tex0.a);
    L1g = float3(tex1.g, tex2.g, tex1.a);
    L1b = float3(tex1.b, tex2.b, tex2.a);
}

// Bounds mask for a volume rotated in world space, using local UVW
float LV_BoundsMask(float3 localUVW, float3 invLocalEdgeSmooth) {
    float3 fade = saturate((0.5 - abs(localUVW)) * invLocalEdgeSmooth);
    return fade.x * fade.y * fade.z;
}

// Default light probes SH components
void LV_SampleLightProbe(inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {
    L0 += float3(unity_SHAr.w, unity_SHAg.w, unity_SHAb.w);
    L1r += unity_SHAr.xyz;
    L1g += unity_SHAg.xyz;
    L1b += unity_SHAb.xyz;
}

// Applies deringing to light probes. Useful if they baked with Bakery L1
void LV_SampleLightProbeDering(inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {
    L0 += float3(unity_SHAr.w, unity_SHAg.w, unity_SHAb.w);
    L1r += unity_SHAr.xyz * 0.565;
    L1g += unity_SHAg.xyz * 0.565;
    L1b += unity_SHAb.xyz * 0.565;
}

// Calculates atlas UVW coordinates for a volume sample using the compact bounds layout
void LV_VolumeAtlasUVW(uint id, float3 localUVW, out float3 uvw0, out float3 uvw1, out float3 uvw2) {
    uint uvwID = id * 3;
    float4 uvwPos0 = _UdonLightVolumeUvwScale[uvwID];
    float4 uvwPos1 = _UdonLightVolumeUvwScale[uvwID + 1];
    float4 uvwPos2 = _UdonLightVolumeUvwScale[uvwID + 2];
    float3 uvwScale = float3(uvwPos0.w, uvwPos1.w, uvwPos2.w);

    float3 uvwScaled = saturate(localUVW + 0.5) * uvwScale;
    uvw0 = uvwPos0.xyz + uvwScaled;
    uvw1 = uvwPos1.xyz + uvwScaled;
    uvw2 = uvwPos2.xyz + uvwScaled;
}

// Samples a Volume with ID and Local UVW
void LV_SampleVolume(uint id, float3 localUVW, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {

    // Additive UVW
    float3 uvw0 = 0;
    float3 uvw1 = 0;
    float3 uvw2 = 0;
    LV_VolumeAtlasUVW(id, localUVW, uvw0, uvw1, uvw2);

    // Sample additive
    float3 l0 = 0;
    float3 l1r = 0;
    float3 l1g = 0;
    float3 l1b = 0;
    LV_SampleLightVolumeTex(uvw0, uvw1, uvw2, l0, l1r, l1g, l1b);

    // Color correction
    float4 color = _UdonLightVolumeColor[id];
    L0 += l0 * color.rgb;
    l1r *= color.r;
    l1g *= color.g;
    l1b *= color.b;

    // Rotate if needed
    [branch] if (color.a != 0) {
        uint rotationID = id * 2;
        float3 r0 = _UdonLightVolumeRotation[rotationID].xyz;
        float3 r1 = _UdonLightVolumeRotation[rotationID + 1].xyz;
        float3 r2 = cross(r0, r1);
        L1r += LV_MultiplyVectorByMatrix3x3(l1r, r0, r1, r2);
        L1g += LV_MultiplyVectorByMatrix3x3(l1g, r0, r1, r2);
        L1b += LV_MultiplyVectorByMatrix3x3(l1b, r0, r1, r2);
    } else {
        L1r += l1r;
        L1g += l1g;
        L1b += l1b;
    }

}

// Calculates L1 SH based on the world position. Only samples point lights, not light volumes.
void LV_PointLightVolumeSH(float3 worldPos, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b, float3 worldNormal = 0) {

    uint pointCount = min((uint) _UdonPointLightVolumeCount, VRCLV_MAX_LIGHTS_COUNT);
    [branch] if (_UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION || pointCount == 0) {
        return;
    } else {
        float maxOverdraw = min(_UdonLightVolumeAdditiveMaxOverdraw, (float) VRCLV_MAX_LIGHTS_COUNT);
        float pcount = 0; // Point lights counter

        [loop] for (uint pid = 0; pid < pointCount; pid++) {
            [branch] if (pcount >= maxOverdraw) break;
            if (LV_PointLightContribution(pid, worldPos, L0, L1r, L1g, L1b, worldNormal)) pcount += 1;
        }
    }

}

// Calculates L1 SH based on the world position.
void LV_LightVolumeSH(float3 worldPos, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {

    // Clamping global iteration counts
    uint volumesCount = min((uint) _UdonLightVolumeCount, VRCLV_MAX_VOLUMES_COUNT);

    [branch] if (_UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION || volumesCount == 0) {
        LV_SampleLightProbe(L0, L1r, L1g, L1b);
        return;
    } else {
        uint maxOverdraw = min((uint) _UdonLightVolumeAdditiveMaxOverdraw, VRCLV_MAX_VOLUMES_COUNT);
        uint additiveCount = min((uint) _UdonLightVolumeAdditiveCount, VRCLV_MAX_VOLUMES_COUNT);
        bool lightProbesBlend = _UdonLightVolumeProbesBlend;

        uint volumeID_A = -1; // Main, dominant volume ID
        uint volumeID_B = -1; // Secondary volume ID to blend main with

        float3 localUVW   = 0; // Last local UVW to use in disabled Light Probes mode
        float3 localUVW_A = 0; // Main local UVW
        float3 localUVW_B = 0; // Secondary local UVW

        // Are A and B volumes NOT found?
        bool isNoA = true;
        bool isNoB = true;

        // Additive volumes variables
        uint addVolumesCount = 0;

        // Iterating through all light volumes with simplified algorithm requiring Light Volumes to be sorted by weight in descending order
        [loop] for (uint id = 0; id < volumesCount; id++) {
            localUVW = LV_LocalFromVolume(id, worldPos);
            [branch] if (LV_PointLocalAABB(localUVW)) { // Intersection test
                [branch] if (id < additiveCount) { // Sampling additive volumes
                    [branch] if (addVolumesCount < maxOverdraw) {
                        LV_SampleVolume(id, localUVW, L0, L1r, L1g, L1b);
                        addVolumesCount++;
                    }
                } else if (isNoA) { // First, searching for volume A
                    volumeID_A = id;
                    localUVW_A = localUVW;
                    isNoA = false;
                } else { // Next, searching for volume B if A found
                    volumeID_B = id;
                    localUVW_B = localUVW;
                    isNoB = false;
                    break;
                }
            }
        }

        // If no volumes found, using Light Probes as fallback
        [branch] if (isNoA && lightProbesBlend) {
            LV_SampleLightProbe(L0, L1r, L1g, L1b);
            return;
        } else {
            // Fallback to lowest weight light volume if outside of every volume
            localUVW_A = isNoA ? localUVW : localUVW_A;
            volumeID_A = isNoA ? volumesCount - 1 : volumeID_A;

            // Volume A SH components and mask to blend volume sides
            float3 L0_A  = 0;
            float3 L1r_A = 0;
            float3 L1g_A = 0;
            float3 L1b_A = 0;

            // Sampling Light Volume A
            LV_SampleVolume(volumeID_A, localUVW_A, L0_A, L1r_A, L1g_A, L1b_A);

            float mask = LV_BoundsMask(localUVW_A, _UdonLightVolumeInvLocalEdgeSmooth[volumeID_A]);
            [branch] if (mask == 1 || isNoA || (_UdonLightVolumeSharpBounds && isNoB)) { // Returning SH A result if it's the center of mask or out of bounds
                L0  += L0_A;
                L1r += L1r_A;
                L1g += L1g_A;
                L1b += L1b_A;
                return;
            } else {
                // Volume B SH components
                float3 L0_B  = 0;
                float3 L1r_B = 0;
                float3 L1g_B = 0;
                float3 L1b_B = 0;

                [branch] if (isNoB && lightProbesBlend) { // No Volume found and light volumes blending enabled

                    // Sample Light Probes B
                    LV_SampleLightProbe(L0_B, L1r_B, L1g_B, L1b_B);

                } else { // Blending Volume A and Volume B

                    // If no volume b found, use last one found to fallback
                    localUVW_B = isNoB ? localUVW : localUVW_B;
                    volumeID_B = isNoB ? volumesCount - 1 : volumeID_B;

                    // Sampling Light Volume B
                    LV_SampleVolume(volumeID_B, localUVW_B, L0_B, L1r_B, L1g_B, L1b_B);

                }

                // Lerping SH components
                L0  += lerp(L0_B,  L0_A,  mask);
                L1r += lerp(L1r_B, L1r_A, mask);
                L1g += lerp(L1g_B, L1g_A, mask);
                L1b += lerp(L1b_B, L1b_A, mask);

            }
        }
    }

}

// Calculates L1 SH based on the world position from additive volumes only.
void LV_LightVolumeAdditiveSH(float3 worldPos, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {

    // Clamping global iteration counts
    uint additiveCount = min((uint) _UdonLightVolumeAdditiveCount, VRCLV_MAX_VOLUMES_COUNT);
    [branch] if (_UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION || additiveCount == 0) {
        return;
    } else {
        uint maxOverdraw = min((uint) _UdonLightVolumeAdditiveMaxOverdraw, VRCLV_MAX_VOLUMES_COUNT);

        uint addVolumesCount = 0;
        [loop] for (uint id = 0; id < additiveCount && addVolumesCount < maxOverdraw; id++) {
            float3 localUVW = LV_LocalFromVolume(id, worldPos);
            [branch] if (LV_PointLocalAABB(localUVW)) {
                LV_SampleVolume(id, localUVW, L0, L1r, L1g, L1b);
                addVolumesCount++;
            }
        }
    }

}

// Calculates speculars for light volumes or any SH L1 data with privided f0
float3 LightVolumeSpecular(float3 f0, float smoothness, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b) {

    float3 specColor = max(float3(dot(reflect(-L1r, worldNormal), viewDir), dot(reflect(-L1g, worldNormal), viewDir), dot(reflect(-L1b, worldNormal), viewDir)), 0);

    float3 rDir = normalize(normalize(L1r) + viewDir);
    float3 gDir = normalize(normalize(L1g) + viewDir);
    float3 bDir = normalize(normalize(L1b) + viewDir);

    float rNh = saturate(dot(worldNormal, rDir));
    float gNh = saturate(dot(worldNormal, gDir));
    float bNh = saturate(dot(worldNormal, bDir));

    float roughness = 1 - smoothness * 0.9;
    float roughExp = roughness * roughness;

    float rSpec = LV_DistributionGGX(rNh, roughExp);
    float gSpec = LV_DistributionGGX(gNh, roughExp);
    float bSpec = LV_DistributionGGX(bNh, roughExp);

    float3 specs = (rSpec + gSpec + bSpec) * f0;
    float3 coloredSpecs = specs * specColor;

    return max(lerp(coloredSpecs + specs * L0, coloredSpecs * 3, smoothness) * 0.5, 0.0);

}

// Calculates speculars for light volumes or any SH L1 data
float3 LightVolumeSpecular(float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b) {
    float3 specularf0 = lerp(0.04, albedo, metallic);
    return LightVolumeSpecular(specularf0, smoothness, worldNormal, viewDir, L0, L1r, L1g, L1b);
}

// Calculates speculars for light volumes or any SH L1 data, but simplified, with only one dominant direction with provided f0
float3 LightVolumeSpecularDominant(float3 f0, float smoothness, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b) {

    float3 dominantDir = L1r + L1g + L1b;
    float3 dir = normalize(normalize(dominantDir) + viewDir);
    float nh = saturate(dot(worldNormal, dir));

    float roughness = 1 - smoothness * 0.9;
    float roughExp = roughness * roughness;

    float spec = LV_DistributionGGX(nh, roughExp);

    return max(spec * L0 * f0, 0.0) * 1.5;

}

// Calculates speculars for light volumes or any SH L1 data, but simplified, with only one dominant direction
float3 LightVolumeSpecularDominant(float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b) {
    float3 specularf0 = lerp(0.04, albedo, metallic);
    return LightVolumeSpecularDominant(specularf0, smoothness, worldNormal, viewDir, L0, L1r, L1g, L1b);
}

// Calculate Light Volume Color based on all SH components provided and the world normal
float3 LightVolumeEvaluate(float3 worldNormal, float3 L0, float3 L1r, float3 L1g, float3 L1b) {
    return L0 + float3(dot(L1r, worldNormal), dot(L1g, worldNormal), dot(L1b, worldNormal));
}

// Calculates L1 SH based on the world position. Samples both light volumes and point lights.
void LightVolumeSH(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, float3 worldPosOffset = 0, float3 worldNormal = 0) {
    L0 = 0; L1r = 0; L1g = 0; L1b = 0;
    [branch] if (_UdonLightVolumeEnabled == 0 || _UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION) {
        LV_SampleLightProbeDering(L0, L1r, L1g, L1b);
    } else {
        LV_LightVolumeSH(worldPos + worldPosOffset, L0, L1r, L1g, L1b);
        LV_PointLightVolumeSH(worldPos, L0, L1r, L1g, L1b, worldNormal);
    }
}

// Calculates L1 SH based on the world position from additive volumes only. Samples both light volumes and point lights.
void LightVolumeAdditiveSH(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, float3 worldPosOffset = 0, float3 worldNormal = 0) {
    L0 = 0; L1r = 0; L1g = 0; L1b = 0;
    [branch] if (_UdonLightVolumeEnabled != 0 && _UdonLightVolumeVersion >= VRCLV_MIN_SUPPORTED_VERSION) {
        LV_LightVolumeAdditiveSH(worldPos + worldPosOffset, L0, L1r, L1g, L1b);
        LV_PointLightVolumeSH(worldPos, L0, L1r, L1g, L1b, worldNormal);
    }
}

// Calculates L0 SH based on the world position. Samples both light volumes and point lights.
float3 LightVolumeSH_L0(float3 worldPos, float3 worldPosOffset = 0, float3 worldNormal = 0) {
    [branch] if (_UdonLightVolumeEnabled == 0 || _UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION) {
        return float3(unity_SHAr.w, unity_SHAg.w, unity_SHAb.w);
    } else {
        float3 L0 = 0;
        float3 unused_L1 = 0; // Let's just pray that compiler will strip everything x.x
        LV_LightVolumeSH(worldPos + worldPosOffset, L0, unused_L1, unused_L1, unused_L1);
        LV_PointLightVolumeSH(worldPos, L0, unused_L1, unused_L1, unused_L1, worldNormal);
        return L0;
    }
}

// Calculates L0 SH based on the world position from additive volumes only. Samples both light volumes and point lights.
float3 LightVolumeAdditiveSH_L0(float3 worldPos, float3 worldPosOffset = 0, float3 worldNormal = 0) {
    [branch] if (_UdonLightVolumeEnabled == 0 || _UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION) {
        return 0;
    } else {
        float3 L0 = 0;
        float3 unused_L1 = 0, unused_L1_ = 0; // Let's just pray that compiler will strip everything x.x
        LV_LightVolumeAdditiveSH(worldPos + worldPosOffset, L0, unused_L1, unused_L1, unused_L1);
        LV_PointLightVolumeSH(worldPos, L0, unused_L1_, unused_L1_, unused_L1_, worldNormal);
        return L0;
    }
}

// Checks if Light Volumes are used in this scene. Returns 0 if not, returns 1 if enabled
float LightVolumesEnabled() {
    return (_UdonLightVolumeEnabled != 0 && _UdonLightVolumeVersion >= VRCLV_MIN_SUPPORTED_VERSION) ? 1 : 0;
}

// Returns the light volumes version
float LightVolumesVersion() {
    return _UdonLightVolumeVersion == 0 ? _UdonLightVolumeEnabled : _UdonLightVolumeVersion;
}

#endif
