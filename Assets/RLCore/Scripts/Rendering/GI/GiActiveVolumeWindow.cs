using System.Collections.Generic;
using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Camera-follow tile window, deadzone, and Texture3D strip-shift helpers shared by GI managers.
    /// </summary>
    public static class GiActiveVolumeWindow
    {
        public struct RuntimeState
        {
            public bool HasActiveWindowBounds;
            public int ActiveMinX;
            public int ActiveMaxX;
            public int ActiveMinY;
            public int ActiveMaxY;
            public Vector2Int ActiveWindowAnchorTile;
            public Vector2Int GiDeadzoneOriginTile;
            public int PendingTexelShiftX;
            public int PendingTexelShiftZ;
        }

        public static Vector2Int WorldToBaseTile(Vector3 worldPos, float cellSizeXZ)
        {
            float cell = Mathf.Max(1e-4f, cellSizeXZ);
            return new Vector2Int(Mathf.FloorToInt(worldPos.x / cell), Mathf.FloorToInt(worldPos.z / cell));
        }

        public static Transform ResolveAnchorTransform(Transform windowCameraTransform)
        {
            if (windowCameraTransform != null)
                return windowCameraTransform;
            Camera cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        public static bool TryInitializeWindow(
            ref RuntimeState state,
            Transform anchor,
            float cellSizeXZ,
            int windowWidthTiles,
            int windowDepthTiles,
            bool useSquareFollowDeadzone,
            out bool deadzoneOriginSet)
        {
            deadzoneOriginSet = false;
            if (anchor == null)
                return false;
            Vector2Int tile = WorldToBaseTile(anchor.position, cellSizeXZ);
            if (SetActiveWindowFromAnchor(ref state, tile, windowWidthTiles, windowDepthTiles, force: true, out _, out _))
            {
                if (useSquareFollowDeadzone)
                {
                    state.GiDeadzoneOriginTile = tile;
                    deadzoneOriginSet = true;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Recenters the active window from the anchor tile. Returns true if the window changed.
        /// </summary>
        public static bool TryRecenterFromAnchor(
            ref RuntimeState state,
            Vector2Int anchorTile,
            int windowWidthTiles,
            int windowDepthTiles,
            bool useSquareFollowDeadzone,
            int squareDeadzoneRadiusTiles,
            bool force,
            out int deltaTilesX,
            out int deltaTilesZ)
        {
            deltaTilesX = 0;
            deltaTilesZ = 0;
            if (useSquareFollowDeadzone && state.HasActiveWindowBounds)
            {
                int cheb = Mathf.Max(
                    Mathf.Abs(anchorTile.x - state.GiDeadzoneOriginTile.x),
                    Mathf.Abs(anchorTile.y - state.GiDeadzoneOriginTile.y));
                if (cheb <= squareDeadzoneRadiusTiles)
                    return false;
            }

            if (!SetActiveWindowFromAnchor(ref state, anchorTile, windowWidthTiles, windowDepthTiles, force, out deltaTilesX, out deltaTilesZ))
                return false;

            if (useSquareFollowDeadzone)
                state.GiDeadzoneOriginTile = anchorTile;

            return true;
        }

        public static bool SetActiveWindowFromAnchor(
            ref RuntimeState state,
            Vector2Int anchorTile,
            int windowWidthTiles,
            int windowDepthTiles,
            bool force,
            out int dx,
            out int dz)
        {
            dx = 0;
            dz = 0;
            int width = Mathf.Max(1, windowWidthTiles);
            int depth = Mathf.Max(1, windowDepthTiles);
            int halfW = width / 2;
            int halfD = depth / 2;
            int newMinX = anchorTile.x - halfW;
            int newMinY = anchorTile.y - halfD;
            int newMaxX = newMinX + width - 1;
            int newMaxY = newMinY + depth - 1;

            if (!force && state.HasActiveWindowBounds &&
                newMinX == state.ActiveMinX && newMaxX == state.ActiveMaxX &&
                newMinY == state.ActiveMinY && newMaxY == state.ActiveMaxY)
            {
                return false;
            }

            if (state.HasActiveWindowBounds)
            {
                dx = anchorTile.x - state.ActiveWindowAnchorTile.x;
                dz = anchorTile.y - state.ActiveWindowAnchorTile.y;
            }

            state.ActiveMinX = newMinX;
            state.ActiveMaxX = newMaxX;
            state.ActiveMinY = newMinY;
            state.ActiveMaxY = newMaxY;
            state.ActiveWindowAnchorTile = anchorTile;
            state.HasActiveWindowBounds = true;
            return true;
        }

        public static void GetActiveBounds(
            in RuntimeState state,
            bool useCameraFollowWindow,
            int fullMinX,
            int fullMaxX,
            int fullMinY,
            int fullMaxY,
            out int minTileX,
            out int maxTileX,
            out int minTileY,
            out int maxTileY)
        {
            if (useCameraFollowWindow && state.HasActiveWindowBounds)
            {
                minTileX = state.ActiveMinX;
                maxTileX = state.ActiveMaxX;
                minTileY = state.ActiveMinY;
                maxTileY = state.ActiveMaxY;
                return;
            }

            minTileX = fullMinX;
            maxTileX = fullMaxX;
            minTileY = fullMinY;
            maxTileY = fullMaxY;
        }

        public static bool TryGetRebuildBounds(
            in RuntimeState state,
            bool useCameraFollowWindow,
            out int minTileX,
            out int maxTileX,
            out int minTileY,
            out int maxTileY)
        {
            minTileX = maxTileX = minTileY = maxTileY = 0;
            if (!useCameraFollowWindow || !state.HasActiveWindowBounds)
                return false;

            minTileX = state.ActiveMinX;
            maxTileX = state.ActiveMaxX;
            minTileY = state.ActiveMinY;
            maxTileY = state.ActiveMaxY;
            return true;
        }

        public static bool IsBaseTileInActiveBounds(
            in RuntimeState state,
            bool useCameraFollowWindow,
            Vector2Int tile,
            int fullMinX,
            int fullMaxX,
            int fullMinY,
            int fullMaxY)
        {
            GetActiveBounds(state, useCameraFollowWindow, fullMinX, fullMaxX, fullMinY, fullMaxY,
                out int minTileX, out int maxTileX, out int minTileY, out int maxTileY);
            return tile.x >= minTileX && tile.x <= maxTileX && tile.y >= minTileY && tile.y <= maxTileY;
        }

        public static void QueueEnteringStripTiles(
            in RuntimeState state,
            int dx,
            int dz,
            List<Vector2Int> enteringStripTilesBuffer)
        {
            enteringStripTilesBuffer.Clear();
            if (dx == 0 && dz == 0)
                return;

            int minTileX = state.ActiveMinX;
            int maxTileX = state.ActiveMaxX;
            int minTileY = state.ActiveMinY;
            int maxTileY = state.ActiveMaxY;

            if (dx > 0)
            {
                int startX = Mathf.Max(minTileX, maxTileX - dx + 1);
                for (int x = startX; x <= maxTileX; x++)
                    for (int y = minTileY; y <= maxTileY; y++)
                        enteringStripTilesBuffer.Add(new Vector2Int(x, y));
            }
            else if (dx < 0)
            {
                int endX = Mathf.Min(maxTileX, minTileX - dx - 1);
                for (int x = minTileX; x <= endX; x++)
                    for (int y = minTileY; y <= maxTileY; y++)
                        enteringStripTilesBuffer.Add(new Vector2Int(x, y));
            }

            if (dz > 0)
            {
                int startY = Mathf.Max(minTileY, maxTileY - dz + 1);
                for (int y = startY; y <= maxTileY; y++)
                    for (int x = minTileX; x <= maxTileX; x++)
                        enteringStripTilesBuffer.Add(new Vector2Int(x, y));
            }
            else if (dz < 0)
            {
                int endY = Mathf.Min(maxTileY, minTileY - dz - 1);
                for (int y = minTileY; y <= endY; y++)
                    for (int x = minTileX; x <= maxTileX; x++)
                        enteringStripTilesBuffer.Add(new Vector2Int(x, y));
            }
        }

        /// <summary>
        /// Applies pending XZ texel shift to pooled 3D texture buffers (same semantics as GiManager).
        /// </summary>
        public static bool TryApplyPendingTextureShift(
            Texture3D giTexture,
            ref Color[] pooledTextureData,
            ref Color[] pooledShiftBuffer,
            ref int pendingTexelShiftX,
            ref int pendingTexelShiftZ)
        {
            if (giTexture == null)
                return false;

            if (pendingTexelShiftX == 0 && pendingTexelShiftZ == 0)
                return false;

            int shiftX = pendingTexelShiftX;
            int shiftZ = pendingTexelShiftZ;
            int sizeX = giTexture.width;
            int sizeY = giTexture.height;
            int sizeZ = giTexture.depth;
            if (Mathf.Abs(shiftX) >= sizeX || Mathf.Abs(shiftZ) >= sizeZ)
                return false;

            int voxelCount = sizeX * sizeY * sizeZ;
            if (pooledTextureData == null || pooledShiftBuffer == null ||
                pooledTextureData.Length != voxelCount || pooledShiftBuffer.Length != voxelCount)
                return false;

            Color[] src = pooledTextureData;
            Color[] dst = pooledShiftBuffer;
            System.Array.Clear(dst, 0, voxelCount);
            for (int z = 0; z < sizeZ; z++)
            {
                int fromZ = z + shiftZ;
                if (fromZ < 0 || fromZ >= sizeZ)
                    continue;
                for (int y = 0; y < sizeY; y++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int fromX = x + shiftX;
                        if (fromX < 0 || fromX >= sizeX)
                            continue;

                        int dstIdx = x + sizeX * (y + sizeY * z);
                        int srcIdx = fromX + sizeX * (y + sizeY * fromZ);
                        dst[dstIdx] = src[srcIdx];
                    }
                }
            }

            for (int z = 0; z < sizeZ; z++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int fromX = x + shiftX;
                        int fromZ = z + shiftZ;
                        bool outOfBounds = fromX < 0 || fromX >= sizeX || fromZ < 0 || fromZ >= sizeZ;
                        if (!outOfBounds)
                            continue;

                        int clampedX = fromX < 0 ? 0 : (fromX >= sizeX ? sizeX - 1 : fromX);
                        int clampedFromZ = fromZ < 0 ? 0 : (fromZ >= sizeZ ? sizeZ - 1 : fromZ);
                        int dstIdx = x + sizeX * (y + sizeY * z);
                        int edgeSrcIdx = clampedX + sizeX * (y + sizeY * clampedFromZ);
                        dst[dstIdx] = src[edgeSrcIdx];
                    }
                }
            }

            giTexture.SetPixels(dst);
            giTexture.Apply(false, false);
            pendingTexelShiftX = 0;
            pendingTexelShiftZ = 0;
            Color[] tmp = pooledTextureData;
            pooledTextureData = pooledShiftBuffer;
            pooledShiftBuffer = tmp;
            return true;
        }
    }
}
