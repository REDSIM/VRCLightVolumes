#ifndef VRCLV_UNITY_PREVIEW_CLUSTERING_COMMON_INCLUDED
#define VRCLV_UNITY_PREVIEW_CLUSTERING_COMMON_INCLUDED

float _UdonClusteringEnabled;
float4 _UdonFroxelGrid;
float4 _UdonFroxelDepth;
float4 _UdonFroxelProjection;
float4 _UdonFroxelRight;
float4 _UdonFroxelUp;
float4 _UdonFroxelForward;

#if defined(VRCLV_PREVIEW_FINE_CLUSTERING)
Texture2D<int4> _UdonClusterMask;
#endif

#if defined(VRCLV_PREVIEW_COARSE_CLUSTERING)
Texture2D<int4> _UdonCoarseClusterMask;
float4 _UdonFroxelCoarseGrid;
float4 _UdonFroxelCoarse;
#endif

// Resolves the exact Fine cell used by the runtime lookup without pulling the complete
// Light Volumes lighting implementation into these editor-only shaders.
inline void VRCLVPreviewWorldToFineCell(float3 worldPosition, inout uint3 fineCell, inout bool valid)
{
    fineCell = 0u;
    valid = false;
    // Fine mirrors LV_LoadClusterMask's ordered rejection. Coarse retains its historical
    // positive validity tests, including their conservative behavior for unordered values.
    #if defined(VRCLV_PREVIEW_COARSE_CLUSTERING)
    [branch] if (!(_UdonClusteringEnabled >= 0.5)) return;
    #else
    [branch] if (_UdonClusteringEnabled < 0.5) return;
    #endif

    float3 cameraPosition = float3(_UdonFroxelRight.w, _UdonFroxelUp.w, _UdonFroxelForward.w);
    float3 cameraDelta = worldPosition - cameraPosition;
    float viewDepth = dot(cameraDelta, _UdonFroxelForward.xyz);
    #if defined(VRCLV_PREVIEW_COARSE_CLUSTERING)
    [branch] if (!(viewDepth >= _UdonFroxelDepth.x && viewDepth <= _UdonFroxelDepth.y)) return;
    #else
    [branch] if (viewDepth < _UdonFroxelDepth.x || viewDepth > _UdonFroxelDepth.y) return;
    #endif

    float2 viewPosition = float2(
        dot(cameraDelta, _UdonFroxelRight.xyz),
        dot(cameraDelta, _UdonFroxelUp.xyz));
    float2 halfExtent = viewDepth * _UdonFroxelProjection.xy + _UdonFroxelProjection.zw;
    #if defined(VRCLV_PREVIEW_COARSE_CLUSTERING)
    [branch] if (!all(abs(viewPosition) <= halfExtent)) return;
    #else
    [branch] if (any(abs(viewPosition) > halfExtent)) return;
    #endif

    float2 screenUv = saturate(viewPosition * (0.5 / halfExtent) + 0.5);
    float depthIndex = max(log2(viewDepth * _UdonFroxelDepth.z) * _UdonFroxelDepth.w, 0.0);
    uint3 grid = (uint3)_UdonFroxelGrid.xyz;
    uint2 screenCell = (uint2)(screenUv * _UdonFroxelGrid.xz);
    fineCell = uint3(
        min(screenCell.x, grid.x - 1u),
        min(screenCell.y, grid.z - 1u),
        min((uint)depthIndex, grid.y - 1u));
    valid = true;
}

#if defined(VRCLV_PREVIEW_FINE_CLUSTERING)
inline void VRCLVPreviewLoadFineClusterMask(float3 worldPosition, inout uint4 mask, inout bool loaded)
{
    mask = 0u;
    loaded = false;
    uint3 fineCell = 0u;
    bool valid = false;
    VRCLVPreviewWorldToFineCell(worldPosition, fineCell, valid);
    [branch] if (!valid) return;

    uint tileShift = (uint)_UdonFroxelGrid.w;
    uint tileX = fineCell.y & ((1u << tileShift) - 1u);
    uint tileY = fineCell.y >> tileShift;
    int2 atlasTexel = int2(
        tileX * (uint)_UdonFroxelGrid.x + fineCell.x,
        tileY * (uint)_UdonFroxelGrid.y + fineCell.z);
    mask = asuint(_UdonClusterMask.Load(int3(atlasTexel, 0)));
    loaded = true;
}
#endif

#if defined(VRCLV_PREVIEW_COARSE_CLUSTERING)
inline uint4 VRCLVPreviewLoadCoarseClusterMask(float3 worldPosition)
{
    uint3 fineCell = 0u;
    bool valid = false;
    VRCLVPreviewWorldToFineCell(worldPosition, fineCell, valid);
    [branch] if (!valid) return 0u;

    uint reductionShift = (uint)_UdonFroxelCoarse.y;
    uint3 coarseCell = fineCell >> reductionShift;
    uint tileShift = (uint)_UdonFroxelCoarseGrid.w;
    uint tileX = coarseCell.y & ((1u << tileShift) - 1u);
    uint tileY = coarseCell.y >> tileShift;
    int2 atlasTexel = int2(
        tileX * (uint)_UdonFroxelCoarseGrid.x + coarseCell.x,
        tileY * (uint)_UdonFroxelCoarseGrid.y + coarseCell.z);
    return asuint(_UdonCoarseClusterMask.Load(int3(atlasTexel, 0)));
}
#endif

// Places all 128 mask bits into one well-distributed deterministic color key.
inline uint VRCLVPreviewHashClusterMask(uint4 mask)
{
    uint hash = 2166136261u;
    hash = (hash ^ mask.x) * 16777619u;
    hash = (hash ^ mask.y) * 16777619u;
    hash = (hash ^ mask.z) * 16777619u;
    hash = (hash ^ mask.w) * 16777619u;
    hash ^= hash >> 16u;
    hash *= 2146121005u;
    hash ^= hash >> 15u;
    hash *= 2221713035u;
    hash ^= hash >> 16u;
    return hash;
}

// Converts a hash to a bright, highly saturated RGB color.
inline half3 VRCLVPreviewClusterMaskColor(uint hash)
{
    float hue = (float)(hash & 16777215u) * (1.0 / 16777216.0);
    float3 hueRgb = saturate(abs(frac(hue + float3(0.0, 0.6666667, 0.3333333)) * 6.0 - 3.0) - 1.0);
    half saturation = 0.84h + (half)((hash >> 24u) & 3u) * 0.04h;
    return lerp(half3(1.0h, 1.0h, 1.0h), (half3)hueRgb, saturation);
}

#endif
