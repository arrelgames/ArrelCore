using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// CPU-side helpers for packing GI into 3D textures (shared patterns, gap-aware fill, optional blur).
    /// <see cref="GiManager"/> applies the same Y-slice fill (bidirectional or gap-aware) to L0 and each SH volume,
    /// restoring pre-fill occupancy counts per pass. Burst propagation in <see cref="GiGrid"/> updates L0 only;
    /// direct SH coefficients stay on the main thread unless extended later.
    /// </summary>
    public static class GiVolumeTextureUtilities
    {
        /// <summary>
        /// Same behavior as <see cref="GiManager"/> fill: interpolate empty Y slices between occupied slices and optional upward carry.
        /// </summary>
        public static void FillEmptyYSlicesBidirectional(Color[] data, int[] counts, int sizeX, int sizeY, int sizeZ, int maxUpwardCarrySlices = 2)
        {
            for (int tz = 0; tz < sizeZ; tz++)
            {
                for (int tx = 0; tx < sizeX; tx++)
                {
                    int highestData = -1;
                    Color belowColor = Color.black;
                    int belowSlice = -1;

                    for (int ty = 0; ty < sizeY; ty++)
                    {
                        int idx = tx + sizeX * (ty + sizeY * tz);
                        if (counts[idx] > 0)
                        {
                            if (belowSlice >= 0 && ty - belowSlice > 1)
                            {
                                Color aboveColor = data[idx];
                                for (int fy = belowSlice + 1; fy < ty; fy++)
                                {
                                    int distBelow = fy - belowSlice;
                                    int distAbove = ty - fy;
                                    float total = distBelow + distAbove;
                                    float wBelow = distAbove / total;
                                    float wAbove = distBelow / total;
                                    int fIdx = tx + sizeX * (fy + sizeY * tz);
                                    data[fIdx] = belowColor * wBelow + aboveColor * wAbove;
                                    counts[fIdx] = -2;
                                }
                            }

                            belowColor = data[idx];
                            belowSlice = ty;
                            highestData = ty;
                        }
                    }

                    if (highestData >= 0 && maxUpwardCarrySlices > 0)
                    {
                        int hIdx = tx + sizeX * (highestData + sizeY * tz);
                        Color carry = data[hIdx];
                        int carryLimit = Mathf.Min(sizeY, highestData + 1 + maxUpwardCarrySlices);
                        for (int ty = highestData + 1; ty < carryLimit; ty++)
                        {
                            int idx = tx + sizeX * (ty + sizeY * tz);
                            if (counts[idx] == 0)
                            {
                                data[idx] = carry;
                                counts[idx] = -2;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Like <see cref="FillEmptyYSlicesBidirectional"/> but skips interpolating across vertical gaps larger than <paramref name="maxGapSlices"/>.
        /// Reduces light leaking between distant floors in the volume texture.
        /// </summary>
        public static void FillEmptyYSlicesGapAware(
            Color[] data,
            int[] counts,
            int sizeX,
            int sizeY,
            int sizeZ,
            int maxGapSlices,
            int maxUpwardCarrySlices = 2)
        {
            int maxGap = Mathf.Max(1, maxGapSlices);
            for (int tz = 0; tz < sizeZ; tz++)
            {
                for (int tx = 0; tx < sizeX; tx++)
                {
                    int highestData = -1;
                    Color belowColor = Color.black;
                    int belowSlice = -1;

                    for (int ty = 0; ty < sizeY; ty++)
                    {
                        int idx = tx + sizeX * (ty + sizeY * tz);
                        if (counts[idx] > 0)
                        {
                            if (belowSlice >= 0 && ty - belowSlice > 1)
                            {
                                int gap = ty - belowSlice;
                                if (gap <= maxGap)
                                {
                                    Color aboveColor = data[idx];
                                    for (int fy = belowSlice + 1; fy < ty; fy++)
                                    {
                                        int distBelow = fy - belowSlice;
                                        int distAbove = ty - fy;
                                        float total = distBelow + distAbove;
                                        float wBelow = distAbove / total;
                                        float wAbove = distBelow / total;
                                        int fIdx = tx + sizeX * (fy + sizeY * tz);
                                        data[fIdx] = belowColor * wBelow + aboveColor * wAbove;
                                        counts[fIdx] = -2;
                                    }
                                }
                            }

                            belowColor = data[idx];
                            belowSlice = ty;
                            highestData = ty;
                        }
                    }

                    if (highestData >= 0 && maxUpwardCarrySlices > 0)
                    {
                        int hIdx = tx + sizeX * (highestData + sizeY * tz);
                        Color carry = data[hIdx];
                        int carryLimit = Mathf.Min(sizeY, highestData + 1 + maxUpwardCarrySlices);
                        for (int ty = highestData + 1; ty < carryLimit; ty++)
                        {
                            int idx = tx + sizeX * (ty + sizeY * tz);
                            if (counts[idx] == 0)
                            {
                                data[idx] = carry;
                                counts[idx] = -2;
                            }
                        }
                    }
                }
            }
        }

        public static void FillEmptyTexelsFromNeighbors(Color[] data, int[] counts, int sizeX, int sizeY, int sizeZ)
        {
            for (int ty = 0; ty < sizeY; ty++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    for (int x = 1; x < sizeX; x++)
                    {
                        int idx = x + sizeX * (ty + sizeY * z);
                        int prev = (x - 1) + sizeX * (ty + sizeY * z);
                        if (counts[idx] == 0 && counts[prev] != 0)
                        {
                            data[idx] = data[prev];
                            counts[idx] = -1;
                        }
                    }
                }

                for (int z = 0; z < sizeZ; z++)
                {
                    for (int x = sizeX - 2; x >= 0; x--)
                    {
                        int idx = x + sizeX * (ty + sizeY * z);
                        int next = (x + 1) + sizeX * (ty + sizeY * z);
                        if (counts[idx] == 0 && counts[next] != 0)
                        {
                            data[idx] = data[next];
                            counts[idx] = -1;
                        }
                    }
                }

                for (int x = 0; x < sizeX; x++)
                {
                    for (int z = 1; z < sizeZ; z++)
                    {
                        int idx = x + sizeX * (ty + sizeY * z);
                        int prev = x + sizeX * (ty + sizeY * (z - 1));
                        if (counts[idx] == 0 && counts[prev] != 0)
                        {
                            data[idx] = data[prev];
                            counts[idx] = -1;
                        }
                    }
                }

                for (int x = 0; x < sizeX; x++)
                {
                    for (int z = sizeZ - 2; z >= 0; z--)
                    {
                        int idx = x + sizeX * (ty + sizeY * z);
                        int next = x + sizeX * (ty + sizeY * (z + 1));
                        if (counts[idx] == 0 && counts[next] != 0)
                        {
                            data[idx] = data[next];
                            counts[idx] = -1;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Simple separable box average on each Y slice (XZ only). Radius 0 is a no-op.
        /// </summary>
        public static void SeparableBoxBlurXZ(Color[] data, int sizeX, int sizeY, int sizeZ, int radius)
        {
            if (radius <= 0 || sizeX < 2 || sizeZ < 2)
                return;

            int n = sizeX * sizeY * sizeZ;
            Color[] tmp = new Color[n];
            int w = radius * 2 + 1;

            for (int z = 0; z < sizeZ; z++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        Color sum = Color.black;
                        int c = 0;
                        for (int ox = -radius; ox <= radius; ox++)
                        {
                            int nx = Mathf.Clamp(x + ox, 0, sizeX - 1);
                            int idx = nx + sizeX * (y + sizeY * z);
                            sum += data[idx];
                            c++;
                        }

                        int o = x + sizeX * (y + sizeY * z);
                        tmp[o] = sum / Mathf.Max(1, c);
                    }
                }
            }

            for (int z = 0; z < sizeZ; z++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        Color sum = Color.black;
                        int c = 0;
                        for (int oz = -radius; oz <= radius; oz++)
                        {
                            int nz = Mathf.Clamp(z + oz, 0, sizeZ - 1);
                            int idx = x + sizeX * (y + sizeY * nz);
                            sum += tmp[idx];
                            c++;
                        }

                        int o = x + sizeX * (y + sizeY * z);
                        data[o] = sum / Mathf.Max(1, c);
                    }
                }
            }
        }
    }
}
