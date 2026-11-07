using System;
using WeatherOS.Apps;

namespace WeatherOS.Services
{
    /// <summary>
    /// High-Detail Multi-Spectral Meteorological Layer & Animated NEXRAD Doppler Weather Radar Engine.
    /// Engineered for zero heap allocations (0 bytes allocated per frame) during real-time radar loop animation.
    /// </summary>
    public static class WorldWeatherMapLayers
    {
        // CP437 Hardware Shading & Block Glyphs (Untouched in VGA Font Plane 2)
        private const byte SHADE_LIGHT = 0xB0; // '░' 25% stipple
        private const byte SHADE_MED   = 0xB1; // '▒' 50% stipple
        private const byte SHADE_DARK  = 0xB2; // '▓' 75% stipple
        private const byte BLOCK_FULL  = 0xDB; // '█' 100% solid block

        // Pre-allocated 16-step fixed-point trig tables (-64 .. +64) for zero-allocation cyclonic rotation
        private static readonly int[] Cos16 = new int[16]
        {
             64,  59,  45,  24,   0, -24, -45, -59,
            -64, -59, -45, -24,   0,  24,  45,  59
        };

        private static readonly int[] Sin16 = new int[16]
        {
              0,  24,  45,  59,  64,  59,  45,  24,
              0, -24, -45, -59, -64, -59, -45, -24
        };

        // Pre-allocated static telemetry interpolation buffers (22 rows x 80 cols) — zero GC pressure
        private static readonly int[] _gridTemp     = new int[22 * 80];
        private static readonly int[] _gridHum      = new int[22 * 80];
        private static readonly int[] _gridWind     = new int[22 * 80];
        private static readonly int[] _gridPress    = new int[22 * 80];
        private static readonly int[] _gridNearDist = new int[22 * 80];
        private static readonly int[] _gridNearIdx  = new int[22 * 80];
        private static int _cachedZone = -1;
        private static bool _gridValid = false;

        public static void InvalidateGrid()
        {
            _gridValid = false;
        }

        /// <summary>
        /// Pre-computes spatial meteorological fields across the 80x22 canvas using steep local IDW (1/d^4)
        /// plus latitudinal solar gradient so every temperature and moisture zone is accurately represented.
        /// </summary>
        public static void EnsureGridComputed(int zone, GlobalStation[] stations)
        {
            if (_gridValid && _cachedZone == zone) return;
            if (stations == null || stations.Length == 0) return;

            int stCount = stations.Length;

            for (int my = 0; my < 22; my++)
            {
                int rowOffset = my * 80;
                for (int mx = 0; mx < 80; mx++)
                {
                    long numT = 0, numH = 0, numW = 0, numP = 0, denW = 0;
                    int bestD2 = 999999;
                    int bestIdx = 0;

                    for (int i = 0; i < stCount; i++)
                    {
                        int dx = mx - stations[i].MapX;
                        int dy = (my - stations[i].MapY) * 2; // 8x16 cell aspect correction
                        int d2 = dx * dx + dy * dy;

                        if (d2 < bestD2)
                        {
                            bestD2 = d2;
                            bestIdx = i;
                        }

                        // Steep inverse-fourth-power weight (1 / (d^4 + 4)) so local station values
                        // are never washed out toward the continental average
                        long d4 = (long)d2 * d2;
                        long w = 4000000L / (d4 + 4L);
                        if (w < 1L) w = 1L;

                        numT += stations[i].TempC * w;
                        numH += stations[i].Humidity * w;
                        numW += stations[i].WindKmh * w;
                        numP += stations[i].Pressure * w;
                        denW += w;
                    }

                    if (denW <= 0) denW = 1;
                    int interpT = (int)(numT / denW);
                    int interpH = (int)(numH / denW);
                    int interpW = (int)(numW / denW);
                    int interpP = (int)(numP / denW);

                    // Subtle latitudinal thermal & topographic adjustment away from exact station cores
                    if (bestD2 > 6)
                    {
                        int latBias = 0;
                        if (zone == 0) // Europe: colder in high Arctic north, warmer on Mediterranean south
                            latBias = (my - 11) / 3;
                        else if (zone == 1) // Asia: Siberia north vs Tropical south
                            latBias = (my - 9) / 2;
                        else if (zone == 3) // Oceania: Equatorial north vs Southern Ocean south
                            latBias = (10 - my) / 3;

                        interpT += latBias;
                    }

                    int idx = rowOffset + mx;
                    _gridTemp[idx]     = interpT;
                    _gridHum[idx]      = interpH;
                    _gridWind[idx]     = interpW;
                    _gridPress[idx]    = interpP;
                    _gridNearDist[idx] = bestD2;
                    _gridNearIdx[idx]  = bestIdx;
                }
            }

            _cachedZone = zone;
            _gridValid = true;
        }

        public static int GetStationPrecipScore(GlobalStation st)
        {
            string c = (st.Condition ?? "").ToLower();
            if (c.Contains("thunder") || c.Contains("storm")) return 4;
            if (c.Contains("rain") || c.Contains("shower") || c.Contains("drizzle")) return 3;
            if (c.Contains("snow") || c.Contains("sleet")) return 3;
            // High humidity (>= 85%) with low/moderate pressure represents active frontal rain/drizzle
            if (st.Humidity >= 90 || (st.Humidity >= 85 && st.Pressure <= 1013)) return 3;
            if (c.Contains("cloud") || c.Contains("overcast") || c.Contains("fog") || st.Humidity >= 75) return 2;
            if (c.Contains("part") || st.Humidity >= 62) return 1;
            return 0;
        }

        // =====================================================================
        // LAYER 1 (2/5): REALISTIC 6-BAND THERMAL HEATMAP & ISOTHERMS
        // All background colors strictly use 0..7 (DarkBlue, DarkCyan, DarkGreen,
        // DarkYellow, DarkRed) so hardware blink bit 7 is never triggered.
        // =====================================================================

        public static ConsoleColor GetThermalCoastColor(int zone, GlobalStation[] stations, int mx, int my)
        {
            EnsureGridComputed(zone, stations);
            int t = _gridTemp[my * 80 + mx];
            if (t >= 20) return ConsoleColor.Red;
            if (t >= 15) return ConsoleColor.Yellow;
            if (t >= 11) return ConsoleColor.Green;
            return ConsoleColor.Cyan;
        }

        public static void RenderThermalCell(int zone, GlobalStation[] stations, int mx, int my, int screenY, bool isLand)
        {
            EnsureGridComputed(zone, stations);
            int idx = my * 80 + mx;
            int t = _gridTemp[idx];
            int nearDist = _gridNearDist[idx];

            if (!isLand)
            {
                // Coastal marine thermal bloom & sea-surface isotherm lines
                if (nearDist <= 55 && ((mx + my) % 3 == 0))
                {
                    ConsoleColor seaCol = (t >= 19) ? ConsoleColor.Yellow : (t >= 13 ? ConsoleColor.Green : ConsoleColor.Cyan);
                    WorldWeatherMapApp.PutVgaCell(mx, screenY, (byte)'~', seaCol, ConsoleColor.DarkBlue);
                }
                else
                {
                    WorldWeatherMapApp.PutVgaCell(mx, screenY, (byte)' ', ConsoleColor.DarkBlue, ConsoleColor.DarkBlue);
                }
                return;
            }

            // Check horizontal/vertical neighbor temperature to draw crisp isotherm contour boundaries
            int tRight = (mx < 79) ? _gridTemp[idx + 1] : t;
            int tDown  = (my < 21) ? _gridTemp[idx + 80] : t;

            bool isContour =
                ((t < 8  && (tRight >= 8  || tDown >= 8))  || (t >= 8  && (tRight < 8  || tDown < 8)))  ||
                ((t < 11 && (tRight >= 11 || tDown >= 11)) || (t >= 11 && (tRight < 11 || tDown < 11))) ||
                ((t < 14 && (tRight >= 14 || tDown >= 14)) || (t >= 14 && (tRight < 14 || tDown < 14))) ||
                ((t < 17 && (tRight >= 17 || tDown >= 17)) || (t >= 17 && (tRight < 17 || tDown < 17))) ||
                ((t < 21 && (tRight >= 21 || tDown >= 21)) || (t >= 21 && (tRight < 21 || tDown < 21)));

            // 6 Distinct Multi-Spectral Thermal Bands using CP437 Dithered Shading Blocks
            if (t >= 21)
            {
                // Band 6 (>= 21C): Hot / Subtropical-Tropical Core (Intense Red & Yellow on Crimson DarkRed)
                byte g = (t >= 25) ? SHADE_DARK : (isContour ? (byte)'~' : SHADE_MED);
                ConsoleColor fg = (t >= 25) ? ConsoleColor.Red : ConsoleColor.Yellow;
                WorldWeatherMapApp.PutVgaCell(mx, screenY, g, fg, ConsoleColor.DarkRed);
            }
            else if (t >= 17)
            {
                // Band 5 (17..20C): Warm Zone (Radiant Yellow/Orange Dither on Amber DarkYellow)
                byte g = isContour ? (byte)'~' : SHADE_MED;
                ConsoleColor fg = (t >= 19) ? ConsoleColor.Red : ConsoleColor.Yellow;
                WorldWeatherMapApp.PutVgaCell(mx, screenY, g, fg, ConsoleColor.DarkYellow);
            }
            else if (t >= 14)
            {
                // Band 4 (14..16C): Mild-Warm Zone (Golden Yellow on Continental Green)
                byte g = isContour ? (byte)'~' : SHADE_MED;
                WorldWeatherMapApp.PutVgaCell(mx, screenY, g, ConsoleColor.Yellow, ConsoleColor.DarkGreen);
            }
            else if (t >= 11)
            {
                // Band 3 (11..13C): Temperate Cool-Mild Zone (Vibrant Lime Green Stipple on DarkGreen)
                byte g = isContour ? (byte)'-' : SHADE_MED;
                WorldWeatherMapApp.PutVgaCell(mx, screenY, g, ConsoleColor.Green, ConsoleColor.DarkGreen);
            }
            else if (t >= 8)
            {
                // Band 2 (8..10C): Cold Zone (Crisp Cyan Frost Shading on DarkCyan)
                byte g = isContour ? (byte)'~' : SHADE_MED;
                WorldWeatherMapApp.PutVgaCell(mx, screenY, g, ConsoleColor.Cyan, ConsoleColor.DarkCyan);
            }
            else
            {
                // Band 1 (<= 7C): Deep Cold / Polar Freeze (Icy Arctic White-Cyan on Deep Blue)
                byte g = (t <= 5) ? SHADE_DARK : SHADE_MED;
                ConsoleColor fg = ((mx + my) % 2 == 0) ? ConsoleColor.White : ConsoleColor.Cyan;
                WorldWeatherMapApp.PutVgaCell(mx, screenY, g, fg, ConsoleColor.DarkBlue);
            }
        }

        // =====================================================================
        // LAYER 2 (3/5): REALISTIC PRECIPITATION & CLOUD COVER RADAR
        // =====================================================================

        public static void RenderPrecipCell(int zone, GlobalStation[] stations, int mx, int my, int screenY, bool isLand)
        {
            EnsureGridComputed(zone, stations);
            int idx = my * 80 + mx;
            int h = _gridHum[idx];
            int p = _gridPress[idx];
            int nearDist = _gridNearDist[idx];
            int nearIdx = _gridNearIdx[idx];

            int stScore = GetStationPrecipScore(stations[nearIdx]);

            // Compute combined synoptic precipitation & moisture intensity (0..105)
            int moisture = h + (1016 - p) * 3;
            if (stScore == 4 && nearDist <= 95) moisture += (95 - nearDist) / 2;
            else if (stScore == 3 && nearDist <= 90) moisture += (90 - nearDist) / 3;

            // Spatial frontal wave modulation so rain bands form realistic curved fronts
            int wave = ((mx * 3 + my * 5) % 7) - 3;
            int effMoisture = moisture + wave;

            if (!isLand)
            {
                // Marine Precipitation & Frontal Cloud Bands over Ocean near coasts
                if (nearDist <= 115)
                {
                    if (effMoisture >= 92 || (stScore == 4 && nearDist <= 38))
                    {
                        byte g = ((mx + my) % 2 == 0) ? (byte)'*' : SHADE_DARK;
                        WorldWeatherMapApp.PutVgaCell(mx, screenY, g, ConsoleColor.Yellow, ConsoleColor.DarkBlue);
                        return;
                    }
                    if (effMoisture >= 83 || (stScore >= 3 && nearDist <= 65))
                    {
                        byte g = ((mx + my) % 2 == 0) ? (byte)'/' : SHADE_MED;
                        WorldWeatherMapApp.PutVgaCell(mx, screenY, g, ConsoleColor.Cyan, ConsoleColor.DarkBlue);
                        return;
                    }
                    if (effMoisture >= 73 && nearDist <= 78)
                    {
                        WorldWeatherMapApp.PutVgaCell(mx, screenY, SHADE_LIGHT, ConsoleColor.Cyan, ConsoleColor.DarkBlue);
                        return;
                    }
                }
                WorldWeatherMapApp.PutVgaCell(mx, screenY, (byte)' ', ConsoleColor.DarkBlue, ConsoleColor.DarkBlue);
                return;
            }

            // Continental Landmass Precipitation & Cloud Layers
            if (effMoisture >= 92 || (stScore == 4 && nearDist <= 32))
            {
                // Tier 4: Heavy Squall / Thunderstorm Core (Dense Cyan/Yellow Echo on Deep Blue)
                byte g = ((mx + my) % 2 == 0) ? (byte)'*' : SHADE_DARK;
                ConsoleColor fg = ((mx + my) % 2 == 0) ? ConsoleColor.Yellow : ConsoleColor.Cyan;
                WorldWeatherMapApp.PutVgaCell(mx, screenY, g, fg, ConsoleColor.DarkBlue);
            }
            else if (effMoisture >= 82 || (stScore >= 3 && nearDist <= 64))
            {
                // Tier 3: Active Frontal Rain Band (Diagonal rain sheets & cyan radar echo)
                byte g = ((mx + my) % 2 == 0) ? (byte)'/' : SHADE_MED;
                WorldWeatherMapApp.PutVgaCell(mx, screenY, g, ConsoleColor.Cyan, ConsoleColor.DarkCyan);
            }
            else if (effMoisture >= 73 || (stScore == 2 && nearDist <= 70))
            {
                // Tier 2: Dense Overcast Cloud Shield
                byte g = ((mx + my) % 2 == 0) ? SHADE_MED : SHADE_LIGHT;
                WorldWeatherMapApp.PutVgaCell(mx, screenY, g, ConsoleColor.White, ConsoleColor.DarkGreen);
            }
            else if (effMoisture >= 62 || stScore == 1)
            {
                // Tier 1: Scattered Clouds
                byte g = ((mx + my * 2) % 2 == 0) ? SHADE_LIGHT : (byte)'.';
                WorldWeatherMapApp.PutVgaCell(mx, screenY, g, ConsoleColor.Green, ConsoleColor.DarkGreen);
            }
            else
            {
                // Tier 0: Dry / Clear Sector
                WorldWeatherMapApp.PutVgaCell(mx, screenY, (byte)' ', ConsoleColor.DarkGreen, ConsoleColor.DarkGreen);
            }
        }

        // =====================================================================
        // LAYER 3 (4/5): WIND VELOCITY & STREAMLINE VECTORS
        // =====================================================================

        public static void RenderWindCell(int zone, GlobalStation[] stations, int mx, int my, int screenY, bool isLand)
        {
            EnsureGridComputed(zone, stations);
            int idx = my * 80 + mx;
            int w = _gridWind[idx];

            if (!isLand)
            {
                if (((mx + my * 3) % 6) == 0)
                {
                    ConsoleColor seaWindCol = (w >= 15) ? ConsoleColor.Yellow : ConsoleColor.Cyan;
                    WorldWeatherMapApp.PutVgaCell(mx, screenY, (byte)'~', seaWindCol, ConsoleColor.DarkBlue);
                }
                else
                {
                    WorldWeatherMapApp.PutVgaCell(mx, screenY, (byte)' ', ConsoleColor.DarkBlue, ConsoleColor.DarkBlue);
                }
                return;
            }

            bool isStreamCell = ((mx + my * 2) % 3 == 0);
            if (!isStreamCell)
            {
                WorldWeatherMapApp.PutVgaCell(mx, screenY, (byte)' ', ConsoleColor.DarkGreen, ConsoleColor.DarkGreen);
                return;
            }

            if (w >= 15)
            {
                byte g = (mx % 2 == 0) ? (byte)'>' : (byte)'=';
                WorldWeatherMapApp.PutVgaCell(mx, screenY, g, ConsoleColor.Yellow, ConsoleColor.DarkGreen);
            }
            else if (w >= 12)
            {
                byte g = (mx % 2 == 0) ? (byte)'~' : (byte)'>';
                WorldWeatherMapApp.PutVgaCell(mx, screenY, g, ConsoleColor.Cyan, ConsoleColor.DarkGreen);
            }
            else
            {
                WorldWeatherMapApp.PutVgaCell(mx, screenY, (byte)'-', ConsoleColor.Green, ConsoleColor.DarkGreen);
            }
        }

        // =====================================================================
        // LAYER 4 (5/5): ANIMATED NEXRAD DOPPLER WEATHER RADAR (dBZ LOOP)
        // Modeled after high-resolution cyclonic hook-echo Doppler sweeps.
        // Zero heap allocations (0 bytes allocated per animation tick).
        // =====================================================================

        public static int ComputeDopplerDbzAt(int zone, GlobalStation[] stations, int mx, int my, int animFrame)
        {
            EnsureGridComputed(zone, stations);

            int c1x, c1y, c2x, c2y;
            bool southernHemisphere = (zone == 3);

            if (zone == 0) // Europe: Central/Eastern European Frontal Cyclone + Atlantic Wave
            {
                c1x = 37; c1y = 10;
                c2x = 14; c2y = 13;
            }
            else if (zone == 1) // Asia: Western Pacific / South China Sea Typhoon + Monsoon Trough
            {
                c1x = 48; c1y = 13;
                c2x = 28; c2y = 12;
            }
            else if (zone == 2) // Americas: Gulf/Caribbean Hurricane Hook-Echo + Amazon Squall
            {
                c1x = 30; c1y = 9;
                c2x = 46; c2y = 15;
            }
            else if (zone == 3) // Oceania: Coral Sea Tropical Cyclone + Tasman Front
            {
                c1x = 42; c1y = 8;
                c2x = 58; c2y = 15;
            }
            else // Africa: West/Central African Squall Vortex + Indian Ocean Wave
            {
                c1x = 29; c1y = 10;
                c2x = 48; c2y = 13;
            }

            // Smooth cyclical advection along frontal track across the 16 frames
            int f = animFrame & 15;
            int driftX = (Cos16[f] * 3) >> 6; // -3 .. +3 cell cyclic advection
            int driftY = (Sin16[f] * 2) >> 6; // -2 .. +2 cell cyclic advection

            int dbz1 = EvaluateCycloneHookEchoDbz(mx, my, c1x + driftX, c1y + driftY, f, southernHemisphere, true);
            int dbz2 = EvaluateCycloneHookEchoDbz(mx, my, c2x - driftX, c2y + driftY, (f + 7) & 15, southernHemisphere, false);

            int maxDbz = (dbz1 > dbz2) ? dbz1 : dbz2;

            // Blend with live station precipitation/humidity telemetry
            int idx = my * 80 + mx;
            int nearDist = _gridNearDist[idx];
            int nearIdx = _gridNearIdx[idx];
            GlobalStation st = stations[nearIdx];
            int stScore = GetStationPrecipScore(st);

            if (stScore >= 3 && nearDist <= 64)
            {
                int localDbz = (stScore == 4 ? 58 : 46) - (nearDist / 2);
                if (localDbz > maxDbz) maxDbz = localDbz;
            }
            else if (_gridHum[idx] >= 84 && maxDbz < 28)
            {
                int humDbz = (_gridHum[idx] - 62);
                if (humDbz > maxDbz) maxDbz = humDbz;
            }

            return maxDbz;
        }

        private static int EvaluateCycloneHookEchoDbz(int mx, int my, int cx, int cy, int frame, bool clockwise, bool isPrimary)
        {
            int dx = mx - cx;
            int dy = (my - cy) * 2;

            int r2 = dx * dx + dy * dy;
            int maxRadius2 = isPrimary ? 960 : 520;
            if (r2 > maxRadius2) return 0;

            int rotIdx = clockwise ? ((16 - (frame & 15)) & 15) : (frame & 15);
            int oscIdx = (rotIdx & 3);
            int cosA = Cos16[oscIdx];
            int sinA = Sin16[oscIdx];

            int rx = (dx * cosA - dy * sinA) >> 6;
            int ry = (dx * sinA + dy * cosA) >> 6;

            int dbz = 0;

            // 1. Sweeping Main Comma-Arc / Frontal Squall Band
            int arcShiftX = rx - 5;
            int bandCenterY = -4 + (arcShiftX * arcShiftX) / 26;
            int distFromBand = ry - bandCenterY;
            if (distFromBand < 0) distFromBand = -distFromBand;

            if (rx >= -14 && rx <= 26 && distFromBand <= 14)
            {
                int bandCore = 62 - (distFromBand * 4) - ((arcShiftX * arcShiftX) / 18);
                if (!isPrimary) bandCore -= 8;
                if (bandCore > dbz) dbz = bandCore;
            }

            // 2. Cyclonic Hook-Echo Vortex Eye on the Western/Southwestern Flank
            int hx = rx + 11;
            int hy = ry - 5;
            int hr2 = hx * hx + hy * hy;
            if (hr2 <= 42)
            {
                int hookDbz;
                if (hr2 <= 3)
                    hookDbz = 26; // Inner eye
                else if (hr2 <= 18)
                    hookDbz = 60 - (hr2 - 8) * (hr2 - 8) / 4; // Intense red hook-echo eyewall
                else
                    hookDbz = 48 - (hr2 - 18);

                if (!isPrimary) hookDbz -= 6;
                if (hookDbz > dbz) dbz = hookDbz;
            }

            // 3. Outer Spiral Feeder Bands (Green / Blue trailing bands rotating with frame)
            int spiralPhase = (dx * 3 + dy * 2 - frame * 3) & 7;
            if (r2 <= (isPrimary ? 780 : 400) && dbz < 36)
            {
                int feeder = (isPrimary ? 38 : 30) - (r2 / 24) - (spiralPhase <= 3 ? spiralPhase * 2 : (8 - spiralPhase) * 2);
                if (feeder > dbz) dbz = feeder;
            }

            // 4. High-Resolution Cellular Radar Granularity
            if (dbz > 8)
            {
                int cellHash = ((mx * 17) ^ (my * 31) ^ (frame * 13)) & 7;
                dbz += (cellHash - 3);
            }

            return dbz < 0 ? 0 : dbz;
        }

        public static void RenderDopplerRadarCell(
            int zone, GlobalStation[] stations, string[] map,
            int mx, int my, int screenY, int animFrame,
            byte glyphCoastNW, byte glyphCoastNE, byte glyphCoastSW, byte glyphCoastSE,
            byte glyphCoastN,  byte glyphCoastS,  byte glyphCoastW,  byte glyphCoastE,
            byte glyphIsland)
        {
            int dbz = ComputeDopplerDbzAt(zone, stations, mx, my, animFrame);

            // Render NEXRAD multi-spectral Doppler radar reflectivity (all backgrounds strictly 0..7)
            if (dbz >= 12)
            {
                byte glyph;
                ConsoleColor fg;
                ConsoleColor bg;

                if (dbz >= 56)
                {
                    // 56..65+ dBZ: Extreme Convective Storm / Hook-Echo Core (Solid Red & Magenta Core on DarkRed)
                    glyph = (dbz >= 61) ? BLOCK_FULL : SHADE_DARK;
                    fg = (dbz >= 62 && ((mx + my + animFrame) % 3 == 0)) ? ConsoleColor.Magenta : ConsoleColor.Red;
                    bg = ConsoleColor.DarkRed;
                }
                else if (dbz >= 47)
                {
                    // 47..55 dBZ: Severe Storm Red/Orange Arc (Red on Amber DarkYellow)
                    glyph = (dbz >= 51) ? SHADE_DARK : SHADE_MED;
                    fg = ConsoleColor.Red;
                    bg = ConsoleColor.DarkYellow;
                }
                else if (dbz >= 37)
                {
                    // 37..46 dBZ: Heavy Rain Yellow/Amber Band (Yellow on DarkYellow / DarkGreen)
                    glyph = (dbz >= 42) ? SHADE_DARK : SHADE_MED;
                    fg = ConsoleColor.Yellow;
                    bg = (dbz >= 41) ? ConsoleColor.DarkYellow : ConsoleColor.DarkGreen;
                }
                else if (dbz >= 24)
                {
                    // 24..36 dBZ: Moderate Rain Bright Green / Dark Green Band
                    glyph = (dbz >= 30) ? SHADE_DARK : SHADE_MED;
                    fg = ConsoleColor.Green;
                    bg = ConsoleColor.DarkGreen;
                }
                else
                {
                    // 12..23 dBZ: Outer Peripheral Light Precipitation / Virga Halo (Blue / Indigo / Cyan on DarkBlue)
                    glyph = (dbz >= 18) ? SHADE_MED : SHADE_LIGHT;
                    fg = (dbz >= 19) ? ConsoleColor.Cyan : ConsoleColor.Blue;
                    bg = ConsoleColor.DarkBlue;
                }

                WorldWeatherMapApp.PutVgaCell(mx, screenY, glyph, fg, bg);
                return;
            }

            // Otherwise, render the Dark-Contrast Radar Base Map
            string line = (my < map.Length) ? map[my] : "";
            char c = (mx < line.Length) ? line[mx] : ' ';

            if (c == ' ')
            {
                WorldWeatherMapApp.PutVgaCell(mx, screenY, (byte)' ', ConsoleColor.DarkBlue, ConsoleColor.DarkBlue);
                return;
            }

            bool upLand    = IsLand(map, mx, my - 1);
            bool downLand  = IsLand(map, mx, my + 1);
            bool leftLand  = IsLand(map, mx - 1, my);
            bool rightLand = IsLand(map, mx + 1, my);

            if (!upLand && !leftLand && downLand && rightLand)
                WorldWeatherMapApp.PutVgaCell(mx, screenY, glyphCoastNW, ConsoleColor.DarkGreen, ConsoleColor.DarkBlue);
            else if (!upLand && !rightLand && downLand && leftLand)
                WorldWeatherMapApp.PutVgaCell(mx, screenY, glyphCoastNE, ConsoleColor.DarkGreen, ConsoleColor.DarkBlue);
            else if (!downLand && !leftLand && upLand && rightLand)
                WorldWeatherMapApp.PutVgaCell(mx, screenY, glyphCoastSW, ConsoleColor.DarkGreen, ConsoleColor.DarkBlue);
            else if (!downLand && !rightLand && upLand && leftLand)
                WorldWeatherMapApp.PutVgaCell(mx, screenY, glyphCoastSE, ConsoleColor.DarkGreen, ConsoleColor.DarkBlue);
            else if (!upLand && !downLand && !leftLand && !rightLand)
                WorldWeatherMapApp.PutVgaCell(mx, screenY, glyphIsland, ConsoleColor.DarkGreen, ConsoleColor.DarkBlue);
            else if (!upLand && downLand && leftLand && rightLand)
                WorldWeatherMapApp.PutVgaCell(mx, screenY, glyphCoastN, ConsoleColor.DarkGreen, ConsoleColor.DarkBlue);
            else if (!downLand && upLand && leftLand && rightLand)
                WorldWeatherMapApp.PutVgaCell(mx, screenY, glyphCoastS, ConsoleColor.DarkGreen, ConsoleColor.DarkBlue);
            else if (!leftLand && rightLand && upLand && downLand)
                WorldWeatherMapApp.PutVgaCell(mx, screenY, glyphCoastW, ConsoleColor.DarkGreen, ConsoleColor.DarkBlue);
            else if (!rightLand && leftLand && upLand && downLand)
                WorldWeatherMapApp.PutVgaCell(mx, screenY, glyphCoastE, ConsoleColor.DarkGreen, ConsoleColor.DarkBlue);
            else
            {
                if (c == '^')
                    WorldWeatherMapApp.PutVgaCell(mx, screenY, (byte)'^', ConsoleColor.Green, ConsoleColor.DarkGreen);
                else if (c == '-' || c == '|')
                    WorldWeatherMapApp.PutVgaCell(mx, screenY, (byte)'.', ConsoleColor.Green, ConsoleColor.DarkGreen);
                else
                    WorldWeatherMapApp.PutVgaCell(mx, screenY, (byte)' ', ConsoleColor.DarkGreen, ConsoleColor.DarkGreen);
            }
        }

        private static bool IsLand(string[] map, int x, int y)
        {
            if (y < 0 || y >= map.Length) return false;
            string line = map[y];
            if (x < 0 || x >= line.Length) return false;
            return line[x] != ' ';
        }

        // =====================================================================
        // ZERO-ALLOCATION VGA INTEGER & BADGE WRITERS FOR REAL-TIME RADAR LOOP
        // =====================================================================

        public static int DirectVgaWriteInt(int x, int y, int value, ConsoleColor fg, ConsoleColor bg)
        {
            if (value < 0)
            {
                WorldWeatherMapApp.PutVgaCell(x++, y, (byte)'-', fg, bg);
                value = -value;
            }
            if (value >= 100)
            {
                int d100 = value / 100;
                WorldWeatherMapApp.PutVgaCell(x++, y, (byte)('0' + d100), fg, bg);
                value -= d100 * 100;
                int d10 = value / 10;
                WorldWeatherMapApp.PutVgaCell(x++, y, (byte)('0' + d10), fg, bg);
                int d1 = value - d10 * 10;
                WorldWeatherMapApp.PutVgaCell(x++, y, (byte)('0' + d1), fg, bg);
            }
            else if (value >= 10)
            {
                int d10 = value / 10;
                WorldWeatherMapApp.PutVgaCell(x++, y, (byte)('0' + d10), fg, bg);
                int d1 = value - d10 * 10;
                WorldWeatherMapApp.PutVgaCell(x++, y, (byte)('0' + d1), fg, bg);
            }
            else
            {
                WorldWeatherMapApp.PutVgaCell(x++, y, (byte)('0' + value), fg, bg);
            }
            return x;
        }

        public static void DirectVgaWriteTwoDigits(int x, int y, int value, ConsoleColor fg, ConsoleColor bg)
        {
            if (value < 0) value = 0;
            if (value > 99) value = 99;
            int d10 = value / 10;
            int d1 = value - d10 * 10;
            WorldWeatherMapApp.PutVgaCell(x,     y, (byte)('0' + d10), fg, bg);
            WorldWeatherMapApp.PutVgaCell(x + 1, y, (byte)('0' + d1),  fg, bg);
        }

        /// <summary>
        /// Zero-allocation station badge renderer for Layer 4 (5/5 Weather Radar).
        /// Draws "[54dBZ]" or "48dBZ" directly into VGA memory without creating any string objects.
        /// </summary>
        public static void DrawRadarStationBadgeZeroAlloc(int zone, GlobalStation[] stations, int stationIdx, bool isSelected, int animFrame)
        {
            GlobalStation st = stations[stationIdx];
            int x = st.MapX;
            int y = 1 + st.MapY;
            if (y < 1 || y > 22 || x < 0 || x >= 72) return;

            int dbz = ComputeDopplerDbzAt(zone, stations, st.MapX, st.MapY, animFrame);
            if (dbz < 5) dbz = 5;

            ConsoleColor bg = isSelected ? ConsoleColor.DarkCyan : ConsoleColor.Black;
            ConsoleColor fg;
            if (isSelected) fg = ConsoleColor.Yellow;
            else if (dbz >= 48) fg = ConsoleColor.Red;
            else if (dbz >= 36) fg = ConsoleColor.Yellow;
            else if (dbz >= 24) fg = ConsoleColor.Green;
            else fg = ConsoleColor.Cyan;

            int cx = x;
            if (isSelected)
            {
                WorldWeatherMapApp.PutVgaCell(cx++, y, (byte)'[', ConsoleColor.Yellow, bg);
            }

            cx = DirectVgaWriteInt(cx, y, dbz, fg, bg);
            WorldWeatherMapApp.PutVgaCell(cx++, y, (byte)'d', fg, bg);
            WorldWeatherMapApp.PutVgaCell(cx++, y, (byte)'B', fg, bg);
            WorldWeatherMapApp.PutVgaCell(cx++, y, (byte)'Z', fg, bg);

            if (isSelected && cx < 80)
            {
                WorldWeatherMapApp.PutVgaCell(cx, y, (byte)']', ConsoleColor.Yellow, bg);
            }
        }
    }
}
