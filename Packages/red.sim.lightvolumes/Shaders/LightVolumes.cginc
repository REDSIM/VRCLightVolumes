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

// Color multiplier (RGB) | If we actually need to rotate L1 components at all (A)
uniform float4 _UdonLightVolumeColor[VRCLV_MAX_VOLUMES_COUNT];

// Point Lights count
uniform float _UdonPointLightVolumeCount;

// Cubemaps count in the custom textures array
uniform float _UdonPointLightVolumeCubeCount;

// Shadow cubemaps count in the shadow texture array
uniform float _UdonPointLightVolumeShadowCount;

// Shadow cubemap face size in pixels. X = width, Y = height.
uniform float2 _UdonPointLightVolumeShadowResolution;

// For point light: XYZ = Position, W = Inverse squared range
// For spot light: XYZ = Position, W = Inverse squared range, negated
// For area light: XYZ = Position, W = Width
uniform float4 _UdonPointLightVolumePosition[VRCLV_MAX_LIGHTS_COUNT];

// For point light: XYZ = Color, W = Cos of angle (for LUT)
// For spot light: XYZ = Color, W = Cos of outer angle if no custom texture, tan of outer angle otherwise
// For area light: XYZ = Color, W = 2 + Height
uniform float4 _UdonPointLightVolumeColor[VRCLV_MAX_LIGHTS_COUNT];

// For point light: XYZW = Rotation quaternion
// For spot light: XYZ = Direction, W = Cone falloff
// For area light: XYZW = Rotation quaternion
uniform float4 _UdonPointLightVolumeDirection[VRCLV_MAX_LIGHTS_COUNT];

// X = Custom ID:
//   If parametric: X stores 0
//   If uses custom lut: X stores LUT ID with positive sign
//   If uses custom texture: X stores texture ID with negative sign
// Y = projection source type. 0 = static, 1 = RenderTexture, 2 = Material. Animated spot cookies treat zero alpha as fully opaque so RGB-only render textures remain visible.
// Z = Squared Culling Range. Just a precalculated culling range to not recalculate it in shader.
uniform float3 _UdonPointLightVolumeCustomID[VRCLV_MAX_LIGHTS_COUNT];

// X = Shadow cubemap ID. 0 disables shadow, positive encodes World Space Shadows as id + 1, negative encodes Local Space Shadows as -id - 1.
// Y = Depth bias.
// Z = Bias smoothing radius.
// W = Shadow PCF sharpness.
uniform float4 _UdonPointLightVolumeShadowData[VRCLV_MAX_LIGHTS_COUNT];

// For World Space Shadows:
//   XYZ = shadow cubemap bake position in world space.
//   W = valid non-follow shadow data. Reprojection is always enabled.
// For Local Space Shadows:
//   XYZW = Rotation from current light space to baked cubemap space.
uniform float4 _UdonPointLightVolumeShadowReprojectionData[VRCLV_MAX_LIGHTS_COUNT];

// If we are far enough from a light that the irradiance
// is guaranteed lower than the threshold defined by this value,
// we cull the light.
uniform float _UdonLightBrightnessCutoff;

#ifndef SHADER_TARGET_SURFACE_ANALYSIS
}
#endif

#ifndef SHADER_TARGET_SURFACE_ANALYSIS

// Main 3D Texture atlas
uniform Texture3D _UdonLightVolume;
uniform SamplerState sampler_UdonLightVolume;
// First elements must be cubemap faces (6 face textures per cubemap). Then goes other textures
uniform Texture2DArray _UdonPointLightVolumeTexture;
// First elements are baked shadow cubemap faces, 6 face textures per cubemap.
uniform Texture2DArray _UdonPointLightVolumeShadowTexture;
uniform SamplerState sampler_UdonPointLightVolumeShadowTexture;
// Samples a texture using mip 0, and reusing a single sampler
#define LV_SAMPLE(tex, uvw) tex.SampleLevel(sampler_UdonLightVolume, uvw, 0)
#define LV_SAMPLE_SHADOW(uvw) _UdonPointLightVolumeShadowTexture.SampleLevel(sampler_UdonPointLightVolumeShadowTexture, uvw, 0)

#else

// Dummy macro definition to satisfy MojoShader (surface shaders).
#define LV_SAMPLE(tex, uvw) float4(0,0,0,0)
#define LV_SAMPLE_SHADOW(uvw) float4(0,0,0,0)

#endif

#define LV_PI 3.141592653589793f
#define LV_PI2 6.283185307179586f

// Smoothstep to 0, 1 but cheaper
float LV_Smoothstep01(float x) {
    return x * x * (3 - 2 * x);
}

// Rotates vector by Quaternion
float3 LV_MultiplyVectorByQuaternion(float3 v, float4 q) {
    float3 t = 2.0 * cross(q.xyz, v);
    return v + q.w * t + cross(q.xyz, t);
}

// Builds orthonormal axes from a normalized quaternion.
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

    xAxis = float3(1.0f - yy - zz, xy + wz, xz - wy);
    yAxis = float3(xy - wz, 1.0f - xx - zz, yz + wx);
    zAxis = float3(xz + wy, yz - wx, 1.0f - xx - yy);
}

// Rotates vector by Matrix 3x3 with precomputed third axis
float3 LV_MultiplyVectorByMatrix3x3(float3 v, float3 r0, float3 r1, float3 r2) {
    return float3(dot(v, r0), dot(v, r1), dot(v, r2));
}

// Fast approximate arctangent for positive values. Max error is small enough for area light attenuation.
float LV_FastAtanPositive(float x) {
    float x2 = x * x;
    float atanSmall = x * rcp(1.0f + 0.280872f * x2);
    float invX = rcp(max(x, 1e-6f));
    float atanLarge = LV_PI * 0.5f - invX * rcp(1.0f + 0.280872f * invX * invX);
    return x <= 1.0f ? atanSmall : atanLarge;
}

// Forms specular based on roughness
float LV_DistributionGGX(float NoH, float roughness) {
    float f = (roughness - 1) * ((roughness + 1) * (NoH * NoH)) + 1;
    return (roughness * roughness) / ((float) LV_PI * f * f);
}

// Checks if local UVW point is in bounds from -0.5 to +0.5
bool LV_PointLocalAABB(float3 localUVW) {
    return all(abs(localUVW) <= 0.5);
}

// Calculates local UVW using volume ID
float3 LV_LocalFromVolume(uint volumeID, float3 worldPos) {
    return mul(_UdonLightVolumeInvWorldMatrix[volumeID], float4(worldPos, 1.0)).xyz;
}

// Linear single SH L1 channel evaluation
float LV_EvaluateSH(float L0, float3 L1, float3 n) {
    return L0 + dot(L1, n);
}

// Samples a cubemap from _UdonPointLightVolumeTexture array
float4 LV_SampleCubemapArray(uint id, float3 dir) {
    float3 absDir = abs(dir);
    float2 uv;
    uint face;
    if (absDir.x >= absDir.y && absDir.x >= absDir.z) {
        face = dir.x > 0 ? 0 : 1;
        uv = float2((dir.x > 0 ? -dir.z : dir.z), -dir.y) * rcp(absDir.x);
    } else if (absDir.y >= absDir.z) {
        face = dir.y > 0 ? 2 : 3;
        uv = float2(dir.x, (dir.y > 0 ? dir.z : -dir.z)) * rcp(absDir.y);
    } else {
        face = dir.z > 0 ? 4 : 5;
        uv = float2((dir.z > 0 ? dir.x : -dir.x), -dir.y) * rcp(absDir.z);
    }
    float3 uvid = float3(uv * 0.5 + 0.5, id * 6 + face);
    return LV_SAMPLE(_UdonPointLightVolumeTexture, uvid);
}

// Projects a cubemap direction into face index and face UV.
void LV_CubemapFaceUv(float3 dir, out uint face, out float2 uv) {
    float3 absDir = abs(dir);
    if (absDir.x >= absDir.y && absDir.x >= absDir.z) {
        face = dir.x > 0 ? 0 : 1;
        uv = float2((dir.x > 0 ? -dir.z : dir.z), -dir.y) * rcp(absDir.x);
    } else if (absDir.y >= absDir.z) {
        face = dir.y > 0 ? 2 : 3;
        uv = float2(dir.x, (dir.y > 0 ? dir.z : -dir.z)) * rcp(absDir.y);
    } else {
        face = dir.z > 0 ? 4 : 5;
        uv = float2((dir.z > 0 ? dir.x : -dir.x), -dir.y) * rcp(absDir.z);
    }
    uv = uv * 0.5f + 0.5f;
}

// Reconstructs the direction represented by a cubemap face and face UV.
float3 LV_CubemapDirection(uint face, float2 uv) {
    float2 cubeUv = uv * 2.0f - 1.0f;
    float3 dir;
    if (face == 0) dir = float3( 1.0f, -cubeUv.y, -cubeUv.x);
    else if (face == 1) dir = float3(-1.0f, -cubeUv.y,  cubeUv.x);
    else if (face == 2) dir = float3( cubeUv.x,  1.0f,  cubeUv.y);
    else if (face == 3) dir = float3( cubeUv.x, -1.0f, -cubeUv.y);
    else if (face == 4) dir = float3( cubeUv.x, -cubeUv.y,  1.0f);
    else dir = float3(-cubeUv.x, -cubeUv.y, -1.0f);
    return dir * rsqrt(dot(dir, dir));
}

// Samples a shadow map array face using face UV.
float4 LV_SampleShadowMapArrayFace(uint id, uint face, float2 uv) {
    float3 uvid = float3(uv, id * 6 + face);
    return LV_SAMPLE_SHADOW(uvid);
}

// Resolves four depth samples and bilinear factors for the software PCF path.
void LV_PointLightShadowBilinearSamples(uint shadowId, uint face, float2 uv, float shadowSharpness, out float4 shadowDepths, out float2 texelFrac) {
    float2 resolution = max(_UdonPointLightVolumeShadowResolution * saturate(shadowSharpness), float2(1.0f, 1.0f));
    float2 invResolution = rcp(resolution);
    float2 texelPos = uv * resolution - 0.5f;
    float2 texelBase = floor(texelPos);
    texelFrac = texelPos - texelBase;
    float2 texelMax = resolution - 1.0f;

    float2 uv00 = (min(max(texelBase + float2(0.0f, 0.0f), 0.0f), texelMax) + 0.5f) * invResolution;
    float2 uv10 = (min(max(texelBase + float2(1.0f, 0.0f), 0.0f), texelMax) + 0.5f) * invResolution;
    float2 uv01 = (min(max(texelBase + float2(0.0f, 1.0f), 0.0f), texelMax) + 0.5f) * invResolution;
    float2 uv11 = (min(max(texelBase + float2(1.0f, 1.0f), 0.0f), texelMax) + 0.5f) * invResolution;

    shadowDepths = float4(LV_SampleShadowMapArrayFace(shadowId, face, uv00).r, LV_SampleShadowMapArrayFace(shadowId, face, uv10).r, LV_SampleShadowMapArrayFace(shadowId, face, uv01).r, LV_SampleShadowMapArrayFace(shadowId, face, uv11).r);
}

// Blends four already-compared shadow samples using bilinear texel weights.
float LV_PointLightShadowBilinearBlend(float4 shadows, float2 texelFrac) {
    return lerp(lerp(shadows.x, shadows.y, texelFrac.x), lerp(shadows.z, shadows.w, texelFrac.x), texelFrac.y);
}

// Compares four depth values with a receiver distance for the bilinear PCF path.
float4 LV_PointLightShadowCompareDepths(float4 shadowDepths, float distanceToLight, float bias, float biasSmoothness) {
    float receiverDistance = distanceToLight - bias;
    float4 result;
    [branch] if (biasSmoothness <= 0.0001f) {
        result = step(float4(receiverDistance, receiverDistance, receiverDistance, receiverDistance), shadowDepths);
    } else {
        float smoothing = max(biasSmoothness, 0.0001f);
        float4 smoothShadow = saturate((shadowDepths - (receiverDistance - smoothing)) * rcp(smoothing * 2.0f));
        result = smoothShadow * smoothShadow * (3.0f - 2.0f * smoothShadow);
    }
    return result;
}

// Compares four squared reprojected depths with a receiver distance for the bilinear PCF path.
float4 LV_PointLightShadowCompareDepthsSq(float4 shadowDistanceSq, float distanceToLight, float bias, float biasSmoothness) {
    float receiverDistance = max(distanceToLight - bias, 0.0f);
    float receiverDistanceSq = receiverDistance * receiverDistance;
    float4 result;
    [branch] if (biasSmoothness <= 0.0001f) {
        result = step(float4(receiverDistanceSq, receiverDistanceSq, receiverDistanceSq, receiverDistanceSq), shadowDistanceSq);
    } else {
        float smoothing = max(biasSmoothness, 0.0001f);
        float nearDistance = max(receiverDistance - smoothing, 0.0f);
        float farDistance = receiverDistance + smoothing;
        float nearDistanceSq = nearDistance * nearDistance;
        float farDistanceSq = farDistance * farDistance;
        float4 smoothShadow = saturate((shadowDistanceSq - nearDistanceSq) * rcp(max(farDistanceSq - nearDistanceSq, 0.000001f)));
        result = smoothShadow * smoothShadow * (3.0f - 2.0f * smoothShadow);
    }
    return result;
}

// Bilinear PCF compare, close to how hardware shadow samplers soften shadow-map edges.
float LV_PointLightShadowCompareBilinear(uint shadowId, uint face, float2 uv, float distanceToLight, float bias, float biasSmoothness, float shadowSharpness) {
    float4 shadowDepths = 0.0f;
    float2 texelFrac = 0.0f;
    LV_PointLightShadowBilinearSamples(shadowId, face, uv, shadowSharpness, shadowDepths, texelFrac);
    return LV_PointLightShadowBilinearBlend(LV_PointLightShadowCompareDepths(shadowDepths, distanceToLight, bias, biasSmoothness), texelFrac);
}

// Bilinear PCF compare that reprojects baked depth samples to the current point light position.
float LV_PointLightShadowCompareBilinearReprojected(uint shadowId, uint face, float2 uv, float3 sampleDir, float3 lightPos, float3 bakePos, float distanceToLight, float bias, float biasSmoothness, float shadowSharpness) {
    float4 shadowDepths = 0.0f;
    float2 texelFrac = 0.0f;
    LV_PointLightShadowBilinearSamples(shadowId, face, uv, shadowSharpness, shadowDepths, texelFrac);

    float3 bakeToLight = lightPos - bakePos;
    float bakeToLightSq = dot(bakeToLight, bakeToLight);
    float bakeToLightDotDir2 = dot(bakeToLight, sampleDir) * 2.0f;
    float4 shadowDistanceSq = max(shadowDepths * (shadowDepths + bakeToLightDotDir2) + bakeToLightSq, 0.0f);
    return LV_PointLightShadowBilinearBlend(LV_PointLightShadowCompareDepthsSq(shadowDistanceSq, distanceToLight, bias, biasSmoothness), texelFrac);
}

// Samples the per-light shadow cubemap.
void LV_PointLightShadow(uint id, float4 shadowData, float3 lightPos, float3 worldPos, float3 dirN, float distanceToLight, out float shadow) {
    shadow = 1.0f;
    float shadowIdData = shadowData.x;
    [branch] if (shadowIdData == 0.0f || _UdonPointLightVolumeShadowCount <= 0.0f) return;

    bool localSpaceShadows = shadowIdData < 0.0f;
    float shadowIndex = localSpaceShadows ? -shadowIdData - 1.0f : shadowIdData - 1.0f;
    [branch] if (shadowIndex < 0.0f || shadowIndex >= _UdonPointLightVolumeShadowCount) return;

    uint shadowId = (uint)shadowIndex;
    float bias = max(shadowData.y, 0.0f);
    float biasSmoothness = max(shadowData.z, 0.0f);
    float shadowSharpness = saturate(shadowData.w);

    float4 reprojectionData = _UdonPointLightVolumeShadowReprojectionData[id];
    [branch] if (localSpaceShadows) {
        float3 shadowDir = LV_MultiplyVectorByQuaternion(dirN, reprojectionData);
        uint followFace = 0;
        float2 followUv = 0.0f;
        LV_CubemapFaceUv(shadowDir, followFace, followUv);
        shadow = LV_PointLightShadowCompareBilinear(shadowId, followFace, followUv, distanceToLight, bias, biasSmoothness, shadowSharpness);
        return;
    }

    float3 bakeOffset = lightPos - reprojectionData.xyz;
    [branch] if (reprojectionData.w > 0.0f && dot(bakeOffset, bakeOffset) > 0.000001f) {
        float3 bakeDir = reprojectionData.xyz - worldPos;
        float bakeSqLen = dot(bakeDir, bakeDir);
        [branch] if (bakeSqLen > 0.0001f) {
            uint reprojectionFace = 0;
            float2 reprojectionUv = 0.0f;
            float3 bakeDirN = bakeDir * rsqrt(bakeSqLen);
            LV_CubemapFaceUv(bakeDirN, reprojectionFace, reprojectionUv);
            shadow = LV_PointLightShadowCompareBilinearReprojected(shadowId, reprojectionFace, reprojectionUv, bakeDirN, lightPos, reprojectionData.xyz, distanceToLight, bias, biasSmoothness, shadowSharpness);
            return;
        }
    }

    uint face;
    float2 uv;
    LV_CubemapFaceUv(dirN, face, uv);
    shadow = LV_PointLightShadowCompareBilinear(shadowId, face, uv, distanceToLight, bias, biasSmoothness, shadowSharpness);
}

// Returns per-light shadow attenuation after the overdraw slot has already been reserved.
float LV_PointLightShadowAttenuation(uint id, float4 lightPositionData, float3 worldPos, float3 dirN, float sqDistanceToLight, float invDistanceToLight) {
    float shadow = 1.0f;
    [branch] if (_UdonPointLightVolumeShadowCount > 0.0f) {
        float4 shadowData = _UdonPointLightVolumeShadowData[id];
        [branch] if (shadowData.x != 0.0f) LV_PointLightShadow(id, shadowData, lightPositionData.xyz, worldPos, dirN, sqDistanceToLight * invDistanceToLight, shadow);
    }
    return shadow;
}

// Projects a quad light into L1 SH using a cheap solid-angle approximation.
// The axis-aligned case follows the same attenuation law as ComputeAreaLightSquaredBoundingSphere().
void LV_ProjectFastQuadLightIrradianceSH(float3 lightToWorldPos, float4 rotationQuat, float2 size, out float4 irradianceSH) {
    irradianceSH = 0.0f;
    float3 xAxis = float3(1.0f, 0.0f, 0.0f);
    float3 yAxis = float3(0.0f, 1.0f, 0.0f);
    float3 normal = float3(0.0f, 0.0f, 1.0f);
    LV_QuaternionAxes(rotationQuat, xAxis, yAxis, normal);

    float3 localPos = float3(dot(lightToWorldPos, xAxis), dot(lightToWorldPos, yAxis), dot(lightToWorldPos, normal));
    [branch] if (localPos.z <= 0.0f) return;

    float2 halfSize = size * 0.5f;
    float area = max(size.x * size.y, 1e-6f);
    float extentSq = max(dot(halfSize, halfSize), 1e-6f);

    float2 closestXY = clamp(localPos.xy, -halfSize, halfSize);
    float2 rectDelta = localPos.xy - closestXY;
    float rectDeltaSq = dot(rectDelta, rectDelta);
    float planeSq = localPos.z * localPos.z;
    float closestSqDist = max(rectDeltaSq + planeSq, 1e-6f);
    float centerSqDist = max(dot(localPos, localPos), 1e-6f);

    float distanceBlend = (rectDeltaSq + planeSq) * rcp(rectDeltaSq + planeSq + extentSq);
    float solidSqDist = lerp(closestSqDist, centerSqDist, distanceBlend);
    float invSolidDist = rsqrt(solidSqDist);
    float invExtendedDist = rsqrt(solidSqDist + extentSq);

    float atanArg = area * localPos.z * invSolidDist * invSolidDist * invExtendedDist * 0.25f;
    float solidAngle = 4.0f * LV_FastAtanPositive(atanArg);
    float l0 = solidAngle * (0.25f / LV_PI);

    float2 representativeXY = lerp(closestXY, float2(0.0f, 0.0f), distanceBlend);
    float3 worldDir = xAxis * representativeXY.x + yAxis * representativeXY.y - lightToWorldPos;
    float3 dir = worldDir * rsqrt(max(dot(worldDir, worldDir), 1e-6f));
    float directionality = saturate(1.0f - solidAngle * (0.25f / LV_PI));
    irradianceSH = float4(dir * (l0 * directionality), l0);
}

// Samples a quad light, including culling
void LV_QuadLight(float3 worldPos, float3 centroidPos, float4 rotationQuat, float2 size, float3 color, float sqMaxDist, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b, inout uint count) {

    float3 lightToWorldPos = worldPos - centroidPos;

    float4 areaLightSH = 0.0f;
    LV_ProjectFastQuadLightIrradianceSH(lightToWorldPos, rotationQuat, size, areaLightSH);
    [branch] if (areaLightSH.w <= 0.0f) return;

    // Attenuate the light based on distance to the bounding sphere, so we don't get hard seam at the edge.
    float sqCutoffDist = sqMaxDist - dot(lightToWorldPos, lightToWorldPos);
    color.rgb *= saturate(sqCutoffDist / sqMaxDist) * LV_PI;

    L0  += areaLightSH.w * color.rgb;
    L1r += areaLightSH.xyz * color.r;
    L1g += areaLightSH.xyz * color.g;
    L1b += areaLightSH.xyz * color.b;

    count++;
}

// Calculates point light attenuation. Returns false if it's culled
float3 LV_PointLightAttenuation(float sqdist, float sqlightSize, float3 color, float brightnessCutoff, float sqMaxDist) {
    float mask = saturate(1 - sqdist / sqMaxDist);
    return mask * mask * color * sqlightSize / (sqdist + sqlightSize);
}

// Calculates point light solid angle coefficient
float LV_PointLightSolidAngle(float sqdist, float sqlightSize) {
    return saturate(sqrt(sqdist / (sqlightSize + sqdist)));
}

// Calculates a spherical light source
void LV_SphereLight(float sqdist, float3 dirN, float sqlightSize, float3 color, float sqMaxDist, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {
    float3 att = LV_PointLightAttenuation(sqdist, sqlightSize, color, _UdonLightBrightnessCutoff, sqMaxDist);
    float3 l0 = att;
    float3 l1 = dirN * LV_PointLightSolidAngle(sqdist, sqlightSize);
    L0 += l0;
    L1r += l0.r * l1;
    L1g += l0.g * l1;
    L1b += l0.b * l1;
}

// Calculates a spherical spot light source
void LV_SphereSpotLight(float sqdist, float3 dirN, float sqlightSize, float3 att, float spotMask, float cosAngle, float coneFalloff, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {
    float smoothedCone = LV_Smoothstep01(saturate(spotMask * coneFalloff));
    float3 l0 = att * smoothedCone;
    float3 l1 = dirN * LV_PointLightSolidAngle(sqdist, sqlightSize * saturate(1 - cosAngle));
    L0 += l0;
    L1r += l0.r * l1;
    L1g += l0.g * l1;
    L1b += l0.b * l1;
}

// Resolves spot cookie UV and culls fragments outside the projected cookie before expensive shadow work.
void LV_SphereSpotLightCookieUv(float3 dirN, float4 lightRot, float tanAngle, out float2 uv, out bool isValidUv) {
    uv = 0.0f;
    isValidUv = false;
    float3 localDir = LV_MultiplyVectorByQuaternion(-dirN, lightRot);
    [branch] if (localDir.z <= 0.0f) return;

    uv = localDir.xy * rcp(localDir.z * tanAngle);
    [branch] if (abs(uv.x) > 1.0f || abs(uv.y) > 1.0f) return;
    isValidUv = true;
}

// Calculates a spherical spot light source with resolved cookie UV.
void LV_SphereSpotLightCookie(float sqdist, float3 dirN, float sqlightSize, float3 att, float4 cookie, float tanAngle, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {
    float angleSize = saturate(rsqrt(1 + tanAngle * tanAngle));
    float3 l0 = att * cookie.rgb * cookie.a;
    float3 l1 = dirN * LV_PointLightSolidAngle(sqdist, sqlightSize * (1 - angleSize));
    L0 += l0;
    L1r += l0.r * l1;
    L1g += l0.g * l1;
    L1b += l0.b * l1;
}

// Calculates a spherical spot light source
void LV_SphereSpotLightAttenuationLUT(float sqdist, float3 dirN, float sqlightSize, float3 color, float spotMask, float cosAngle, uint customId, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b, inout uint count) {
    float dirRadius = sqdist * abs(sqlightSize);
    float spot = 1 - saturate(spotMask * rcp(1 - cosAngle));
    count++;
    uint id = (uint) _UdonPointLightVolumeCubeCount * 5 + customId - 1;
    float3 uvid = float3(sqrt(float2(spot, dirRadius)), id);
    float3 att = color.rgb * LV_SAMPLE(_UdonPointLightVolumeTexture, uvid).xyz;
    L0 += att;
    L1r += dirN * att.r;
    L1g += dirN * att.g;
    L1b += dirN * att.b;
}

// Samples a spot light, point light or quad/area light
void LV_PointLight(uint id, float3 worldPos, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b, inout uint count) {
    
    // IDs and range data
    float3 customID_data = _UdonPointLightVolumeCustomID[id];
    int customId = (int) customID_data.x; // Custom Texture ID
    float sqrRange = customID_data.z; // Squared culling distance
    
    float4 pos = _UdonPointLightVolumePosition[id]; // Light position and inversed squared range 
    float3 dir = pos.xyz - worldPos;
    float sqlen = max(dot(dir, dir), 1e-6);
    [branch] if (sqlen > sqrRange) return; // Early distance based culling
    
    float4 color = _UdonPointLightVolumeColor[id]; // Color, angle
    float4 ldir = _UdonPointLightVolumeDirection[id]; // Dir + falloff or Rotation
    
    [branch] if (pos.w < 0) { // It is a spot light

        float invLen = rsqrt(sqlen);
        float3 dirN = dir * invLen;
        float angle = color.w;
        float spotMask = 0.0f;
        float2 cookieUv = 0.0f;
        [branch] if (customId >= 0) {
            spotMask = dot(ldir.xyz, -dirN) - angle;
            [branch] if (spotMask < 0.0f) return; // Spot cone based culling
        } else {
            bool isValidCookieUv = false;
            LV_SphereSpotLightCookieUv(dirN, ldir, angle, cookieUv, isValidCookieUv);
            [branch] if (!isValidCookieUv) return;
        }
        count++;
        [branch] if (customId > 0) {  // If it uses Attenuation LUT
            
            float dirRadius = sqlen * abs(pos.w);
            float spot = 1.0f - saturate(spotMask * rcp(1.0f - angle));
            uint textureId = (uint) _UdonPointLightVolumeCubeCount * 5 + customId - 1;
            float3 lutUv = float3(sqrt(float2(spot, dirRadius)), textureId);
            float3 att = color.rgb * LV_SAMPLE(_UdonPointLightVolumeTexture, lutUv).xyz;
            [branch] if (max(max(att.r, att.g), att.b) <= 0.0f) return;
            float shadowAttenuation = LV_PointLightShadowAttenuation(id, pos, worldPos, dirN, sqlen, invLen);
            [branch] if (shadowAttenuation <= 0.0f) return;
            att *= shadowAttenuation;

            L0 += att;
            L1r += dirN * att.r;
            L1g += dirN * att.g;
            L1b += dirN * att.b;
            
        } else { // If it uses default parametric attenuation

            float3 att = LV_PointLightAttenuation(sqlen, -pos.w, color.rgb, _UdonLightBrightnessCutoff, sqrRange);

            [branch] if (customId < 0) { // If uses cookie

                uint textureId = (uint) _UdonPointLightVolumeCubeCount * 5 - customId - 1;
                float4 cookie = LV_SAMPLE(_UdonPointLightVolumeTexture, float3(cookieUv * 0.5f + 0.5f, textureId));
                [branch] if (customID_data.y > 0.5f && cookie.a <= 0.0f) cookie.a = 1.0f;
                [branch] if (cookie.a <= 0.0f || max(max(cookie.r, cookie.g), cookie.b) <= 0.0f) return;
                float shadowAttenuation = LV_PointLightShadowAttenuation(id, pos, worldPos, dirN, sqlen, invLen);
                [branch] if (shadowAttenuation <= 0.0f) return;
                
                LV_SphereSpotLightCookie(sqlen, dirN, -pos.w, att * shadowAttenuation, cookie, angle, L0, L1r, L1g, L1b);
                
            } else { // If it uses default parametric attenuation

                float shadowAttenuation = LV_PointLightShadowAttenuation(id, pos, worldPos, dirN, sqlen, invLen);
                [branch] if (shadowAttenuation <= 0.0f) return;
                
                LV_SphereSpotLight(sqlen, dirN, -pos.w, att * shadowAttenuation, spotMask, angle, ldir.w, L0, L1r, L1g, L1b);
                
            }
            
        }
        
    } else if (color.w <= 1.5f) { // It is a point light
        
        float invLen = rsqrt(sqlen);
        float3 dirN = dir * invLen;
        count++;
        [branch] if (customId > 0) { // Using LUT
            
            float invSqRange = abs(pos.w); // Sign of range defines if it's point light (positive) or a spot light (negative)
            float dirRadius = sqlen * invSqRange;
            uint textureId = (uint) _UdonPointLightVolumeCubeCount * 5 + customId - 1;
            float3 uvid = float3(sqrt(float2(0, dirRadius)), textureId);
            float3 att = color.rgb * LV_SAMPLE(_UdonPointLightVolumeTexture, uvid).xyz;
            [branch] if (max(max(att.r, att.g), att.b) <= 0.0f) return;
            float shadowAttenuation = LV_PointLightShadowAttenuation(id, pos, worldPos, dirN, sqlen, invLen);
            [branch] if (shadowAttenuation <= 0.0f) return;
            att *= shadowAttenuation;
            
            L0 += att;
            L1r += dirN * att.r;
            L1g += dirN * att.g;
            L1b += dirN * att.b;
            
        } else { // If it uses default parametric attenuation

            float shadowAttenuation = LV_PointLightShadowAttenuation(id, pos, worldPos, dirN, sqlen, invLen);
            [branch] if (shadowAttenuation <= 0.0f) return;
            
            float3 l0 = 0, l1r = 0, l1g = 0, l1b = 0;
            LV_SphereLight(sqlen, dirN, pos.w, color.rgb * shadowAttenuation, sqrRange, l0, l1r, l1g, l1b);

            float3 cubeColor = 1;
            [branch] if (customId < 0) { // If it uses a cubemap
                uint id = -customId - 1; // Cubemap ID starts from zero and should not take in count texture array slices count.
                cubeColor = LV_SampleCubemapArray(id, LV_MultiplyVectorByQuaternion(dirN, ldir)).xyz;
            }

            L0 += l0 * cubeColor;
            L1r += l1r * cubeColor.r;
            L1g += l1g * cubeColor.g;
            L1b += l1b * cubeColor.b;
        }
        
    } else { // It is an area light
        
        float4 areaLightSH = 0.0f;
        LV_ProjectFastQuadLightIrradianceSH(worldPos - pos.xyz, ldir, float2(pos.w, color.w - 2.0f), areaLightSH);
        [branch] if (areaLightSH.w <= 0.0f) return;
        float attenuation = saturate((sqrRange - sqlen) * rcp(sqrRange));
        [branch] if (attenuation <= 0.0f) return;

        float invLen = rsqrt(sqlen);
        float3 dirN = dir * invLen;
        count++;
        float shadowAttenuation = LV_PointLightShadowAttenuation(id, pos, worldPos, dirN, sqlen, invLen);
        [branch] if (shadowAttenuation <= 0.0f) return;
        float3 areaColor = color.rgb * (attenuation * LV_PI * shadowAttenuation);
        L0 += areaLightSH.w * areaColor;
        L1r += areaLightSH.xyz * areaColor.r;
        L1g += areaLightSH.xyz * areaColor.g;
        L1b += areaLightSH.xyz * areaColor.b;
        
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
    float3 distToMin = (localUVW + 0.5) * invLocalEdgeSmooth;
    float3 distToMax = (0.5 - localUVW) * invLocalEdgeSmooth;
    float3 fade = saturate(min(distToMin, distToMax));
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
    L1r += unity_SHAr.xyz * 0.565f;
    L1g += unity_SHAg.xyz * 0.565f;
    L1b += unity_SHAb.xyz * 0.565f;
}

// Calculates atlas UVW coordinates for a volume sample using the compact bounds layout.
void LV_VolumeAtlasUVW(uint id, float3 localUVW, out float3 uvw0, out float3 uvw1, out float3 uvw2) {
    uint uvwID = id * 3;
    float4 uvwPos0 = _UdonLightVolumeUvwScale[uvwID];
    float4 uvwPos1 = _UdonLightVolumeUvwScale[uvwID + 1];
    float4 uvwPos2 = _UdonLightVolumeUvwScale[uvwID + 2];
    float3 uvwScale = float3(uvwPos0.w, uvwPos1.w, uvwPos2.w);

    float3 uvwScaled = saturate(localUVW + 0.5f) * uvwScale;
    uvw0 = uvwPos0.xyz + uvwScaled;
    uvw1 = uvwPos1.xyz + uvwScaled;
    uvw2 = uvwPos2.xyz + uvwScaled;
}

// Samples a Volume with ID and Local UVW
void LV_SampleVolume(uint id, float3 localUVW, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {
    
    // Additive UVW
    float3 uvw0 = 0.0f;
    float3 uvw1 = 0.0f;
    float3 uvw2 = 0.0f;
    LV_VolumeAtlasUVW(id, localUVW, uvw0, uvw1, uvw2);
    
    // Sample additive
    float3 l0 = 0.0f;
    float3 l1r = 0.0f;
    float3 l1g = 0.0f;
    float3 l1b = 0.0f;
    LV_SampleLightVolumeTex(uvw0, uvw1, uvw2, l0, l1r, l1g, l1b);

    // Color correction
    float4 color = _UdonLightVolumeColor[id];
    L0 += l0 * color.rgb;
    l1r *= color.r;
    l1g *= color.g;
    l1b *= color.b;
    
    // Rotate if needed
    if (color.a != 0) {
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
void LV_PointLightVolumeSH(float3 worldPos, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {
    
    uint pointCount = min((uint) _UdonPointLightVolumeCount, VRCLV_MAX_LIGHTS_COUNT);
    [branch] if (_UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION || pointCount == 0) return;
    
    uint maxOverdraw = min((uint) _UdonLightVolumeAdditiveMaxOverdraw, VRCLV_MAX_LIGHTS_COUNT);
    uint pcount = 0; // Point lights counter

    [loop] for (uint pid = 0; pid < pointCount && pcount < maxOverdraw; pid++) {
        LV_PointLight(pid, worldPos, L0, L1r, L1g, L1b, pcount);
    }
    
}

// Calculates L1 SH based on the world position.
void LV_LightVolumeSH(float3 worldPos, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {

    // Clamping gloabal iteration counts
    uint volumesCount = min((uint) _UdonLightVolumeCount, VRCLV_MAX_VOLUMES_COUNT);
    
    [branch] if (_UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION || volumesCount == 0) {
        LV_SampleLightProbe(L0, L1r, L1g, L1b);
        return;
    }
    
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
    }
        
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
    }
    
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

// Calculates L1 SH based on the world position from additive volumes only.
void LV_LightVolumeAdditiveSH(float3 worldPos, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {

    // Clamping gloabal iteration counts
    uint additiveCount = min((uint) _UdonLightVolumeAdditiveCount, VRCLV_MAX_VOLUMES_COUNT);
    [branch] if (_UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION || (additiveCount == 0 && (uint) _UdonPointLightVolumeCount == 0)) return;

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

// Calculates speculars for light volumes or any SH L1 data with privided f0
float3 LightVolumeSpecular(float3 f0, float smoothness, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b) {
    
    float3 specColor = max(float3(dot(reflect(-L1r, worldNormal), viewDir), dot(reflect(-L1g, worldNormal), viewDir), dot(reflect(-L1b, worldNormal), viewDir)), 0);
    
    float3 rDir = normalize(normalize(L1r) + viewDir);
    float3 gDir = normalize(normalize(L1g) + viewDir);
    float3 bDir = normalize(normalize(L1b) + viewDir);
    
    float rNh = saturate(dot(worldNormal, rDir));
    float gNh = saturate(dot(worldNormal, gDir));
    float bNh = saturate(dot(worldNormal, bDir));
    
    float roughness = 1 - smoothness * 0.9f;
    float roughExp = roughness * roughness;
    
    float rSpec = LV_DistributionGGX(rNh, roughExp);
    float gSpec = LV_DistributionGGX(gNh, roughExp);
    float bSpec = LV_DistributionGGX(bNh, roughExp);
    
    float3 specs = (rSpec + gSpec + bSpec) * f0;
    float3 coloredSpecs = specs * specColor;
    
    float3 a = coloredSpecs + specs * L0;
    float3 b = coloredSpecs * 3;
    
    return max(lerp(a, b, smoothness) * 0.5f, 0.0);
    
}

// Calculates speculars for light volumes or any SH L1 data
float3 LightVolumeSpecular(float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b) {
    float3 specularf0 = lerp(0.04f, albedo, metallic);
    return LightVolumeSpecular(specularf0, smoothness, worldNormal, viewDir, L0, L1r, L1g, L1b);
}

// Calculates speculars for light volumes or any SH L1 data, but simplified, with only one dominant direction with provided f0
float3 LightVolumeSpecularDominant(float3 f0, float smoothness, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b) {
    
    float3 dominantDir = L1r + L1g + L1b;
    float3 dir = normalize(normalize(dominantDir) + viewDir);
    float nh = saturate(dot(worldNormal, dir));
    
    float roughness = 1 - smoothness * 0.9f;
    float roughExp = roughness * roughness;
    
    float spec = LV_DistributionGGX(nh, roughExp);
    
    return max(spec * L0 * f0, 0.0) * 1.5f;
    
}

// Calculates speculars for light volumes or any SH L1 data, but simplified, with only one dominant direction
float3 LightVolumeSpecularDominant(float3 albedo, float smoothness, float metallic, float3 worldNormal, float3 viewDir, float3 L0, float3 L1r, float3 L1g, float3 L1b) {
    float3 specularf0 = lerp(0.04f, albedo, metallic);
    return LightVolumeSpecularDominant(specularf0, smoothness, worldNormal, viewDir, L0, L1r, L1g, L1b);
}

// Calculate Light Volume Color based on all SH components provided and the world normal
float3 LightVolumeEvaluate(float3 worldNormal, float3 L0, float3 L1r, float3 L1g, float3 L1b) {
    return float3(LV_EvaluateSH(L0.r, L1r, worldNormal), LV_EvaluateSH(L0.g, L1g, worldNormal), LV_EvaluateSH(L0.b, L1b, worldNormal));
}

// Calculates L1 SH based on the world position. Samples both light volumes and point lights.
void LightVolumeSH(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, float3 worldPosOffset = 0) {
    L0 = 0; L1r = 0; L1g = 0; L1b = 0;
    if (_UdonLightVolumeEnabled == 0 || _UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION) {
        LV_SampleLightProbeDering(L0, L1r, L1g, L1b);
    } else {
        LV_LightVolumeSH(worldPos + worldPosOffset, L0, L1r, L1g, L1b);
        LV_PointLightVolumeSH(worldPos, L0, L1r, L1g, L1b);
    }
}

// Calculates L1 SH based on the world position from additive volumes only. Samples both light volumes and point lights.
void LightVolumeAdditiveSH(float3 worldPos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b, float3 worldPosOffset = 0) {
    L0 = 0; L1r = 0; L1g = 0; L1b = 0;
    if (_UdonLightVolumeEnabled != 0 && _UdonLightVolumeVersion >= VRCLV_MIN_SUPPORTED_VERSION) {
        LV_LightVolumeAdditiveSH(worldPos + worldPosOffset, L0, L1r, L1g, L1b);
        LV_PointLightVolumeSH(worldPos, L0, L1r, L1g, L1b);
    }
}

// Calculates L0 SH based on the world position. Samples both light volumes and point lights.
float3 LightVolumeSH_L0(float3 worldPos, float3 worldPosOffset = 0) {
    if (_UdonLightVolumeEnabled == 0 || _UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION) {
        return float3(unity_SHAr.w, unity_SHAg.w, unity_SHAb.w);
    } else {
        float3 L0 = 0;
        float3 unused_L1 = 0.0f; // Let's just pray that compiler will strip everything x.x
        LV_LightVolumeSH(worldPos + worldPosOffset, L0, unused_L1, unused_L1, unused_L1);
        LV_PointLightVolumeSH(worldPos, L0, unused_L1, unused_L1, unused_L1);
        return L0;
    }
}

// Calculates L0 SH based on the world position from additive volumes only. Samples both light volumes and point lights.
float3 LightVolumeAdditiveSH_L0(float3 worldPos, float3 worldPosOffset = 0) {
    if (_UdonLightVolumeEnabled == 0 || _UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION) {
        return 0;
    } else {
        float3 L0 = 0;
        float3 unused_L1 = 0.0f; // Let's just pray that compiler will strip everything x.x
        LV_LightVolumeAdditiveSH(worldPos + worldPosOffset, L0, unused_L1, unused_L1, unused_L1);
        LV_PointLightVolumeSH(worldPos, L0, unused_L1, unused_L1, unused_L1);
        return L0;
    }
}

// Checks if Light Volumes are used in this scene. Returns 0 if not, returns 1 if enabled
float LightVolumesEnabled() {
    return (_UdonLightVolumeEnabled != 0 && _UdonLightVolumeVersion >= VRCLV_MIN_SUPPORTED_VERSION) ? 1.0f : 0.0f;
}

// Returns the light volumes version
float LightVolumesVersion() {
    return _UdonLightVolumeVersion == 0 ? _UdonLightVolumeEnabled : _UdonLightVolumeVersion;
}

#endif
