using System;
using Cosmos.Core;
using Cosmos.System;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public class GlobalStation
    {
        public string Name;
        public string QueryCoords;
        public string Country;
        public string Code;
        public string Lat;
        public string Lon;
        public int MapX;
        public int MapY;
        public int TempC;
        public int Humidity;
        public int WindKmh;
        public int Pressure;
        public string Condition;
        public bool IsLive;

        public GlobalStation(string name, string coords, string country, string code, string lat, string lon, int x, int y, int temp, int hum, int wind, int press, string cond)
        {
            Name = name;
            QueryCoords = coords;
            Country = country;
            Code = code;
            Lat = lat;
            Lon = lon;
            MapX = x;
            MapY = y;
            TempC = temp;
            Humidity = hum;
            WindKmh = wind;
            Pressure = press;
            Condition = cond;
            IsLive = false;
        }
    }

    /// <summary>
    /// Dedicated Full-Screen Meteorological Broadcast Mode (80x25, 720x400 VGA).
    /// Takes over the entire screen without TUI window chrome and programs custom
    /// 8x16 pixel-slope coastline tiles, topographical relief textures, and 18x16
    /// two-cell graphical weather sprites directly into VGA Plane 2 Font RAM,
    /// paired with a custom broadcast RGB DAC palette.
    /// Restores the standard BIOS font and palette cleanly upon exit.
    /// </summary>
    public class WorldWeatherMapApp : IApp
    {
        public string Id => "world_map";
        public string Name => "World Weather Map";
        public bool IsRealTime => false;

        private bool _initialized = false;
        private int _currentZone = 0; // 0=Europe, 1=Asia, 2=Americas, 3=Oceania, 4=Africa
        private int _selectedStation = 0;
        private string _statusMessage = "";

        private GlobalStation[][] _zones;
        private string[][] _continentMaps;

        // Custom VGA Plane 2 Glyph Codes (0xC0..0xDF seamlessly bridge the 9th pixel column in hardware)
        private const byte GLYPH_COAST_NW      = 0xC0; // '/' NW sloped coast (top-left ocean, bottom-right land)
        private const byte GLYPH_COAST_NE      = 0xC1; // '\' NE sloped coast (top-right ocean, bottom-left land)
        private const byte GLYPH_COAST_SW      = 0xC2; // '\' SW sloped coast (bottom-left ocean, top-right land)
        private const byte GLYPH_COAST_SE      = 0xC3; // '/' SE sloped coast (bottom-right ocean, top-left land)
        private const byte GLYPH_COAST_NORTH   = 0xC4; // Smooth horizontal North coast (top ocean, bottom land)
        private const byte GLYPH_COAST_SOUTH   = 0xC5; // Smooth horizontal South coast (top land, bottom ocean)
        private const byte GLYPH_COAST_WEST    = 0xC6; // Smooth vertical West coast (left ocean, right land)
        private const byte GLYPH_COAST_EAST    = 0xC7; // Smooth vertical East coast (left land, right ocean)
        private const byte GLYPH_ISLAND        = 0xC8; // Rounded island / cape tip
        private const byte GLYPH_BORDER_HORIZ  = 0xC9; // Subtle 1px horizontal country border on land
        private const byte GLYPH_BORDER_VERT   = 0xCA; // Subtle 1px vertical country border on land
        private const byte GLYPH_HIGHLAND      = 0xCB; // Topographical mountain / highland stipple on land

        // 2-Cell (18x16 pixel) Broadcast Weather Icon Sprites in 0xD0..0xDB
        private const byte GLYPH_SUN_L         = 0xD0;
        private const byte GLYPH_SUN_R         = 0xD1;
        private const byte GLYPH_PARTCLOUD_L   = 0xD2; // Drawn in Bright Yellow (Sun rays)
        private const byte GLYPH_PARTCLOUD_R   = 0xD3; // Drawn in Bright White (Cloud puff)
        private const byte GLYPH_CLOUD_L       = 0xD4;
        private const byte GLYPH_CLOUD_R       = 0xD5;
        private const byte GLYPH_RAIN_L        = 0xD6;
        private const byte GLYPH_RAIN_R        = 0xD7;
        private const byte GLYPH_STORM_L       = 0xD8; // Drawn in White (Storm cloud)
        private const byte GLYPH_STORM_R       = 0xD9; // Drawn in Yellow (Lightning bolt)
        private const byte GLYPH_SNOW_L        = 0xDA;
        private const byte GLYPH_SNOW_R        = 0xDB;

        private static bool _vgaCustomActive = false;
        private static bool _backupsCaptured = false;
        private static readonly byte[] _savedFontGlyphs = new byte[32 * 16]; // Slots 0xC0..0xDF (32 glyphs * 16 rows)
        private static readonly byte[] _savedDacPalette = new byte[64 * 3];  // First 64 DAC RGB entries

        private static readonly string[] ZoneNames = new string[]
        {
            "EUROPE",
            "ASIA",
            "AMERICAS",
            "OCEANIA",
            "AFRICA"
        };

        private static readonly string[] ZoneDescriptions = new string[]
        {
            "European & Mediterranean Broadcast Sector",
            "Euro-Asian & Pacific Broadcast Sector",
            "North & South American Broadcast Sector",
            "Australian & Pacific Broadcast Sector",
            "African Continental Broadcast Sector"
        };

        public WorldWeatherMapApp()
        {
            InitStations();
            InitContinentMaps();
        }

        public void SyncRegionFromSettings(TuiDesktop desktop)
        {
            string defCity = !string.IsNullOrWhiteSpace(desktop.DefaultCity) ? desktop.DefaultCity : "Setubal";
            _currentZone = DetectContinentIndex(defCity);
            if (_currentZone < 0 || _currentZone >= 5) _currentZone = 0;
            _selectedStation = 0;
            SyncWithLiveReading(desktop);
        }

        private int DetectContinentIndex(string city)
        {
            string s = (city ?? "").ToUpper();

            if (s.Contains("AUSTRALIA") || s.Contains("SYDNEY") || s.Contains("CANBERRA") || s.Contains("MELBOURNE") ||
                s.Contains("ZEALAND") || s.Contains("AUCKLAND") || s.Contains("WELLINGTON") || s.Contains("FIJI") ||
                s.Contains(", AU") || s.Contains(", NZ"))
            {
                return 3; // Oceania
            }

            if (s.Contains("JAPAN") || s.Contains("TOKYO") || s.Contains("CHINA") ||
                s.Contains("BEIJING") || s.Contains("INDIA") || s.Contains("DELHI") ||
                s.Contains("KOREA") || s.Contains("SEOUL") || s.Contains("THAILAND") ||
                s.Contains("INDONESIA") || s.Contains("JAKARTA") || s.Contains("ANKARA") || s.Contains("RIYADH") ||
                s.Contains(", JP") || s.Contains(", CN") || s.Contains(", IN") || s.Contains(", KR"))
            {
                return 1; // Asia
            }

            if (s.Contains("USA") || s.Contains("UNITED STATES") || s.Contains("NEW YORK") || s.Contains("WASHINGTON") ||
                s.Contains("CANADA") || s.Contains("OTTAWA") || s.Contains("MEXICO") || s.Contains("BRAZIL") ||
                s.Contains("BRASIL") || s.Contains("SAO PAULO") || s.Contains("BRASILIA") || s.Contains("ARGENTINA") ||
                s.Contains("BUENOS AIRES") || s.Contains("CHILE") || s.Contains("SANTIAGO") || s.Contains("COLOMBIA") ||
                s.Contains("BOGOTA") || s.Contains("PERU") || s.Contains("LIMA") ||
                s.Contains(", US") || s.Contains(", BR") || s.Contains(", CA") || s.Contains(", AR") || s.Contains(", MX"))
            {
                return 2; // Americas
            }

            if (s.Contains("AFRICA") || s.Contains("CAIRO") || s.Contains("EGYPT") || s.Contains("ANGOLA") ||
                s.Contains("LUANDA") || s.Contains("MOZAMBIQUE") || s.Contains("MAPUTO") || s.Contains("NIGERIA") ||
                s.Contains("ABUJA") || s.Contains("KENYA") || s.Contains("NAIROBI") || s.Contains("MOROCCO") ||
                s.Contains("RABAT") || s.Contains("SOUTH AFRICA") || s.Contains("CAPE TOWN") ||
                s.Contains(", AO") || s.Contains(", MZ") || s.Contains(", ZA") || s.Contains(", EG") || s.Contains(", MA"))
            {
                return 4; // Africa
            }

            return 0; // Europe
        }

        private void InitStations()
        {
            _zones = new GlobalStation[5][];

            // 0: EUROPE (Positioned on the 80x22 full-screen map)
            _zones[0] = new GlobalStation[]
            {
                new GlobalStation("Lisbon",    "38.72,-9.14",   "Portugal",       "PT", "38.72",  "-9.14",  6, 17,  15, 67, 10, 1018, "Clear"),
                new GlobalStation("Madrid",    "40.42,-3.70",   "Spain",          "ES", "40.42",  "-3.70", 14, 16,  16, 49, 11, 1019, "Clear"),
                new GlobalStation("London",    "51.51,-0.13",   "United Kingdom", "UK", "51.51",  "-0.13", 18,  8,  14, 76, 18, 1014, "Partly Cloudy"),
                new GlobalStation("Paris",     "48.86,2.35",    "France",         "FR", "48.86",   "2.35", 23, 11,  13, 67, 12, 1016, "Partly Cloudy"),
                new GlobalStation("Berlin",    "52.52,13.40",   "Germany",        "DE", "52.52",  "13.40", 35,  9,   9, 91, 15, 1011, "Cloudy"),
                new GlobalStation("Oslo",      "59.91,10.75",   "Norway",         "NO", "59.91",  "10.75", 33,  4,   6, 88, 14, 1013, "Cloudy"),
                new GlobalStation("Rome",      "41.90,12.50",   "Italy",          "IT", "41.90",  "12.50", 36, 16,  14, 76, 10, 1017, "Clear"),
                new GlobalStation("Vienna",    "48.21,16.37",   "Austria",        "AT", "48.21",  "16.37", 41, 12,  11, 79, 12, 1015, "Partly Cloudy"),
                new GlobalStation("Warsaw",    "52.23,21.01",   "Poland",         "PL", "52.23",  "21.01", 46,  9,  10, 92, 14, 1012, "Cloudy"),
                new GlobalStation("Athens",    "37.98,23.73",   "Greece",         "GR", "37.98",  "23.73", 49, 17,  15, 80, 12, 1018, "Partly Cloudy"),
                new GlobalStation("Ankara",    "39.93,32.86",   "Turkey",         "TR", "39.93",  "32.86", 62, 17,  11, 93, 11, 1016, "Partly Cloudy"),
                new GlobalStation("Moscow",    "55.75,37.62",   "Russia",         "RU", "55.75",  "37.62", 62,  6,   5, 53, 16, 1009, "Partly Cloudy"),
                new GlobalStation("Rabat",     "34.02,-6.83",   "Morocco",        "MA", "34.02",  "-6.83",  9, 20,  19, 81, 12, 1019, "Partly Cloudy"),
                new GlobalStation("Algiers",   "36.75,3.06",    "Algeria",        "DZ", "36.75",   "3.06", 24, 20,  20, 60, 11, 1018, "Clear"),
                new GlobalStation("Cairo",     "30.04,31.24",   "Egypt",          "EG", "30.04",  "31.24", 58, 20,  23, 50, 14, 1017, "Clear")
            };

            // 1: ASIA (80x22 full-screen map)
            _zones[1] = new GlobalStation[]
            {
                new GlobalStation("Ankara",    "39.93,32.86",   "Turkey",         "TR", "39.93",  "32.86",  6,  7,  11, 93, 11, 1016, "Partly Cloudy"),
                new GlobalStation("Riyadh",    "24.71,46.68",   "Saudi Arabia",   "SA", "24.71",  "46.68", 11, 13,  26, 22, 14, 1015, "Clear"),
                new GlobalStation("New Delhi", "28.61,77.21",   "India",          "IN", "28.61",  "77.21", 29, 11,  27, 48, 10, 1014, "Clear"),
                new GlobalStation("Beijing",   "39.90,116.41",  "China",          "CN", "39.90", "116.41", 52,  7,  12, 45, 15, 1018, "Partly Cloudy"),
                new GlobalStation("Bangkok",   "13.76,100.50",  "Thailand",       "TH", "13.76", "100.50", 44, 14,  29, 78, 12, 1011, "Thunderstorm"),
                new GlobalStation("Singapore", "1.35,103.82",   "Singapore",      "SG",  "1.35", "103.82", 45, 18,  28, 82, 14, 1010, "Rain"),
                new GlobalStation("Jakarta",   "-6.21,106.85",  "Indonesia",      "ID", "-6.21", "106.85", 52, 20,  29, 79, 12, 1010, "Rain"),
                new GlobalStation("Seoul",     "37.57,126.98",  "South Korea",    "KR", "37.57", "126.98", 61,  8,  11, 58, 12, 1019, "Clear"),
                new GlobalStation("Tokyo",     "35.68,139.65",  "Japan",          "JP", "35.68", "139.65", 70,  9,  15, 60, 14, 1020, "Clear")
            };

            // 2: AMERICAS (80x22 full-screen map)
            _zones[2] = new GlobalStation[]
            {
                new GlobalStation("Ottawa",       "45.42,-75.70",  "Canada",         "CA",  "45.42", "-75.70", 31,  4,   9, 68, 16, 1016, "Cloudy"),
                new GlobalStation("New York",     "40.71,-74.01",  "United States",  "US",  "40.71", "-74.01", 34,  6,  16, 59, 14, 1015, "Partly Cloudy"),
                new GlobalStation("Washington",   "38.91,-77.04",  "United States",  "US",  "38.91", "-77.04", 26,  7,  17, 61, 12, 1016, "Partly Cloudy"),
                new GlobalStation("Mexico City",  "19.43,-99.13",  "Mexico",         "MX",  "19.43", "-99.13", 19, 10,  21, 52, 10, 1018, "Clear"),
                new GlobalStation("Havana",       "23.11,-82.37",  "Cuba",           "CU",  "23.11", "-82.37", 34, 10,  27, 76, 18, 1014, "Rain"),
                new GlobalStation("Bogota",       "4.71,-74.07",   "Colombia",       "CO",   "4.71", "-74.07", 36, 13,  16, 81, 10, 1015, "Rain"),
                new GlobalStation("Lima",         "-12.05,-77.04", "Peru",           "PE", "-12.05", "-77.04", 35, 16,  19, 78, 12, 1014, "Cloudy"),
                new GlobalStation("Brasilia",     "-15.79,-47.88", "Brazil",         "BR", "-15.79", "-47.88", 49, 16,  25, 64, 12, 1015, "Thunderstorm"),
                new GlobalStation("Santiago",     "-33.45,-70.67", "Chile",          "CL", "-33.45", "-70.67", 37, 19,  22, 55, 11, 1016, "Clear"),
                new GlobalStation("Buenos Aires", "-34.60,-58.38", "Argentina",      "AR", "-34.60", "-58.38", 45, 19,  20, 67, 16, 1016, "Partly Cloudy")
            };

            // 3: OCEANIA (80x22 full-screen map)
            _zones[3] = new GlobalStation[]
            {
                new GlobalStation("Jakarta",      "-6.21,106.85",  "Indonesia",      "ID",  "-6.21", "106.85", 12,  3,  29, 79, 12, 1010, "Rain"),
                new GlobalStation("Port Moresby", "-9.44,147.18",  "Papua N. Guinea","PG",  "-9.44", "147.18", 38,  5,  29, 82, 15, 1011, "Thunderstorm"),
                new GlobalStation("Sydney",       "-33.87,151.21", "Australia",      "AU", "-33.87", "151.21", 43, 13,  18, 65, 16, 1020, "Partly Cloudy"),
                new GlobalStation("Canberra",     "-35.28,149.13", "Australia",      "AU", "-35.28", "149.13", 39, 15,  12, 91, 12, 1022, "Clear"),
                new GlobalStation("Melbourne",    "-37.81,144.96", "Australia",      "AU", "-37.81", "144.96", 31, 16,  13, 72, 15, 1021, "Cloudy"),
                new GlobalStation("Suva",         "-18.14,178.44", "Fiji",           "FJ", "-18.14", "178.44", 64,  9,  26, 78, 18, 1013, "Rain"),
                new GlobalStation("Wellington",   "-41.29,174.78", "New Zealand",    "NZ", "-41.29", "174.78", 61, 18,  14, 76, 22, 1014, "Cloudy")
            };

            // 4: AFRICA (80x22 full-screen map)
            _zones[4] = new GlobalStation[]
            {
                new GlobalStation("Rabat",        "34.02,-6.83",   "Morocco",        "MA",  "34.02",  "-6.83", 17,  2,  19, 81, 12, 1019, "Partly Cloudy"),
                new GlobalStation("Algiers",      "36.75,3.06",    "Algeria",        "DZ",  "36.75",   "3.06", 28,  2,  20, 60, 11, 1018, "Clear"),
                new GlobalStation("Cairo",        "30.04,31.24",   "Egypt",          "EG",  "30.04",  "31.24", 46,  3,  23, 50, 14, 1017, "Clear"),
                new GlobalStation("Dakar",        "14.69,-17.44",  "Senegal",        "SN",  "14.69", "-17.44", 12,  7,  29, 74, 18, 1012, "Partly Cloudy"),
                new GlobalStation("Abuja",        "9.08,7.40",     "Nigeria",        "NG",   "9.08",   "7.40", 28,  9,  28, 78, 10, 1012, "Thunderstorm"),
                new GlobalStation("Addis Ababa",  "9.03,38.74",    "Ethiopia",       "ET",   "9.03",  "38.74", 50,  9,  19, 58, 12, 1018, "Partly Cloudy"),
                new GlobalStation("Nairobi",      "-1.29,36.82",   "Kenya",          "KE",  "-1.29",  "36.82", 48, 12,  22, 62, 11, 1016, "Cloudy"),
                new GlobalStation("Luanda",       "-8.84,13.23",   "Angola",         "AO",  "-8.84",  "13.23", 31, 14,  26, 77, 14, 1014, "Partly Cloudy"),
                new GlobalStation("Cape Town",    "-33.92,18.42",  "South Africa",   "ZA", "-33.92",  "18.42", 36, 19,  18, 65, 16, 1017, "Clear")
            };
        }

        private void InitContinentMaps()
        {
            _continentMaps = new string[5][];

            // Legend for the 80x22 continental templates:
            // ' ' = Deep Ocean
            // '#' = Solid Continental Landmass (auto-beveled with pixel slopes at all coastlines!)
            // '^' = Highland / Mountain Relief (pixel-stippled topographical olive texture on green land)
            // '-' / '|' = Internal Country Borders on green land

            // 0: EUROPE (80 columns x 22 rows - Full European & Mediterranean Broadcast Map)
            _continentMaps[0] = new string[]
            {
                "                                    ###########             ####################",
                "    ######                        ###############         ######################",
                "   ########                      ########|########      ########################",
                "    ######                      ^^^^###|###  ######    #########################",
                "                                ^^^^###|###   #####   ##########################",
                "                  ###           ######|###    #####  ###########################",
                "                 #####           ####|###    ################|##################",
                "            ###  #####            ###         ###############|##################",
                "           ####  ######                ###   ################|##################",
                "            ##   ######          ########################|###|##################",
                "                   ###        ######|##########|#########|###|##################",
                "                    #########--#####|##########|#########|###|##################",
                "                   ############|####|#####-----##########|###|##################",
                "                    ###########|####^^^^^^#####|#########|######         #######",
                "        ##############^^^######|###^^^^^^^^####|##########|###             #####",
                "      ####|###########^^^^#########|#####---########|########               ####",
                "     #####|#############---########|#####  #####  ##|#######    ################",
                "     #####|#############   ###   ##  #####   ###  ##########   ########^^^######",
                "      ####|###########          ##    #####       ######      #########^^^######",
                "       #############                   ####        ####        #################",
                "     ################################                     ######################",
                "   #############################################################################"
            };

            // 1: ASIA (80 columns x 22 rows)
            _continentMaps[1] = new string[]
            {
                "            ####################################################################",
                "         #######################################################################",
                "       #########################################################################",
                "     ##############################|############################################",
                "    ###############################|########################|###################",
                "   ##############      ############|################^^^^####|############       ",
                "  ###############       ###########|###########^^^^^^^^^####|##########    ###  ",
                "  #####^^^#######        ##########|#########^^^^^^^^^^^#####|#######     ##### ",
                "   ##############         #########|#######^^^^^#############|######     ###### ",
                "       ####|#####         #########|#####^^^^^^^^^###########|####      ######  ",
                "       ####|######      ###########|####^^^^^^^^^^###############       #####   ",
                "        ###|########   ################|##########|##############        ###    ",
                "        ###|#########    ##############|##########|#############                ",
                "         ############      ############|##########|###########                  ",
                "          ###########        ##########|###   ####|#########      ###           ",
                "           #########          #########       #####|#######       ####          ",
                "            #######            #######         #####|####          ###          ",
                "                                #####           #####                           ",
                "                                 ###             ####       ######              ",
                "                                   #          ######       ########   #####     ",
                "                                                ##########  ######   #######    ",
                "                                                      ############              "
            };

            // 2: AMERICAS (80 columns x 22 rows)
            _continentMaps[2] = new string[]
            {
                "      ############        ########################             ############     ",
                "    ################    ############################          ##############    ",
                "   ###################################################         ############     ",
                "     ######^^#########################################           ########       ",
                "           ^^^##################|#####################                          ",
                "            ^^^^################|###################                            ",
                "             ^^^^^##############|#################                              ",
                "              ^^^^^###############################                              ",
                "               ^^^^############################                                 ",
                "                ^^^############        ###                                      ",
                "                  ^^^#########           ######    #####                        ",
                "                    ^^########                      ###                         ",
                "                       ########        ############                             ",
                "                          ######     ################                           ",
                "                            #####  ^^^^##################                       ",
                "                                  ^^^^^#|####################                   ",
                "                                  ^^^^##|######################                 ",
                "                                   ^^^##|#####################                  ",
                "                                    ^^##|##################                     ",
                "                                     ^^#|##############                         ",
                "                                     ^^#|###########                            ",
                "                                      ^^|#######                                "
            };

            // 3: OCEANIA (80 columns x 22 rows)
            _continentMaps[3] = new string[]
            {
                "                                                                                ",
                "        #####                                                                   ",
                "       #########     ######                                                     ",
                "        ############  #######        ###########                                ",
                "           ##########                ###############                            ",
                "                        ####          ##############                            ",
                "                                         #########                              ",
                "                         ######    #####                                        ",
                "                      ##########  #######                      #####            ",
                "                   #######################                    #######           ",
                "                 ###########################                   #####            ",
                "                #############################                                   ",
                "               ###################|###########                                  ",
                "               ###################|###########                                  ",
                "                ##################|##########                                   ",
                "                 ########^^^^#####|#########                     ####           ",
                "                  ######      ####|########                     ######          ",
                "                                 #########                    ########          ",
                "                                   #####                     ########           ",
                "                                    ####                    #######             ",
                "                                                            #####               ",
                "                                                                                "
            };

            // 4: AFRICA (80 columns x 22 rows)
            _continentMaps[4] = new string[]
            {
                "                 ########################                                       ",
                "              ##############################      ###########                   ",
                "            #########|############|##########   ###############                 ",
                "           ##########|############|############################                 ",
                "          ###########|############|############################                 ",
                "         ############|############|############################  ####           ",
                "         #########################|#############################  #####         ",
                "         ############|############|#############################   #####        ",
                "          ###########|############|##############################   #####       ",
                "           ##########|############|#############|###################            ",
                "             ########|############|#############|#####################          ",
                "                ##################|#############|###################            ",
                "                           #######|#############|################               ",
                "                            ######|^^^^#########|#############                  ",
                "                             #####|^^^^^########|###########     #####          ",
                "                             #####|############|###########     #######         ",
                "                              ####|############|##########     ########         ",
                "                               ###|############|#########      #######          ",
                "                                ##|###########|########        ######           ",
                "                                 #|###################                          ",
                "                                  ###################                           ",
                "                                    ###############                             "
            };
        }

        // =====================================================================
        // VGA HARDWARE PLANE 2 CUSTOM TILE & BROADCAST PALETTE ENGINE
        // =====================================================================

        public static unsafe void EnterFullScreenBroadcastMode()
        {
            if (_vgaCustomActive) return;

            try
            {
                System.Console.CursorVisible = false;
            }
            catch { }

            try
            {
                var seqIdx  = new IOPort(0x3C4);
                var seqData = new IOPort(0x3C5);
                var gcIdx   = new IOPort(0x3CE);
                var gcData  = new IOPort(0x3CF);
                var dacRead = new IOPort(0x3C7);
                var dacWrite= new IOPort(0x3C8);
                var dacData = new IOPort(0x3C9);

                // 1. Capture original DAC palette once
                if (!_backupsCaptured)
                {
                    dacRead.Byte = 0;
                    for (int i = 0; i < 64 * 3; i++)
                    {
                        _savedDacPalette[i] = dacData.Byte;
                    }
                }

                // 2. Save original Sequencer & Graphics Controller state
                seqIdx.Byte = 0x02; byte origSeq2 = seqData.Byte;
                seqIdx.Byte = 0x04; byte origSeq4 = seqData.Byte;
                gcIdx.Byte  = 0x04; byte origGc4  = gcData.Byte;
                gcIdx.Byte  = 0x05; byte origGc5  = gcData.Byte;
                gcIdx.Byte  = 0x06; byte origGc6  = gcData.Byte;

                // 3. Unlock VGA Plane 2 (Font RAM) mapped at 0xB8000
                seqIdx.Byte = 0x00; seqData.Byte = 0x01; // Sync reset
                seqIdx.Byte = 0x02; seqData.Byte = 0x04; // Write enable Plane 2 only
                seqIdx.Byte = 0x04; seqData.Byte = 0x07; // Sequential addressing, extended mem
                seqIdx.Byte = 0x00; seqData.Byte = 0x03; // End reset

                gcIdx.Byte  = 0x04; gcData.Byte  = 0x02; // Read Plane 2
                gcIdx.Byte  = 0x05; gcData.Byte  = 0x00; // Linear read/write mode 0
                gcIdx.Byte  = 0x06; gcData.Byte  = 0x04; // Map Plane 2 window at 0xA0000 (IBM VGA BIOS standard)

                byte* fontRam = (byte*)0xA0000;

                // Save original glyphs 0xC0..0xDF (32 glyphs * 16 bytes)
                if (!_backupsCaptured)
                {
                    for (int g = 0; g < 32; g++)
                    {
                        int slotOffset = (0xC0 + g) * 32;
                        for (int row = 0; row < 16; row++)
                        {
                            _savedFontGlyphs[g * 16 + row] = fontRam[slotOffset + row];
                        }
                    }
                    _backupsCaptured = true;
                }

                // 4. Write Custom 8x16 Coastline Slopes, Borders & 18x16 Weather Sprites into 0xC0..0xDF
                ProgramCustomGlyphs(fontRam);

                // 5. Restore Sequencer & Graphics Controller back to normal Text Mode (Planes 0 & 1)
                seqIdx.Byte = 0x00; seqData.Byte = 0x01;
                seqIdx.Byte = 0x02; seqData.Byte = origSeq2;
                seqIdx.Byte = 0x04; seqData.Byte = origSeq4;
                seqIdx.Byte = 0x00; seqData.Byte = 0x03;

                gcIdx.Byte  = 0x04; gcData.Byte  = origGc4;
                gcIdx.Byte  = 0x05; gcData.Byte  = origGc5;
                gcIdx.Byte  = 0x06; gcData.Byte  = origGc6;

                // 6. Program Authentic Television Broadcast RGB Palette (6-bit DAC values 0..63)
                // DarkBlue (DAC 1)  -> Deep Ocean Blue (#1A4C8A -> 6, 19, 35)
                SetDacColor(dacWrite, dacData, 1,  6, 19, 35);
                // DarkGreen (DAC 2) -> Lush Continental Landmass Green (#447A2C -> 17, 31, 11)
                SetDacColor(dacWrite, dacData, 2, 17, 31, 11);
                // DarkCyan (DAC 3)  -> Broadcast HUD / Highlight Pill (#145A78 -> 5, 23, 30)
                SetDacColor(dacWrite, dacData, 3,  5, 23, 30);
                // Green (DAC 58)    -> Topographical Olive Highland / Country Border (#788A32 -> 30, 35, 13)
                SetDacColor(dacWrite, dacData, 58, 30, 35, 13);
                // Cyan (DAC 59)     -> Rain / Sky Cyan (#50C8FF -> 20, 50, 63)
                SetDacColor(dacWrite, dacData, 59, 20, 50, 63);
                // Yellow (DAC 62)   -> Radiant Broadcast Sun Gold (#FFE018 -> 63, 56, 6)
                SetDacColor(dacWrite, dacData, 62, 63, 56,  6);
                // White (DAC 63)    -> Pure Cloud & Temperature White (#FFFFFF -> 63, 63, 63)
                SetDacColor(dacWrite, dacData, 63, 63, 63, 63);

                _vgaCustomActive = true;
            }
            catch { }
        }

        public static unsafe void ExitFullScreenBroadcastMode()
        {
            if (!_vgaCustomActive) return;

            try
            {
                var seqIdx  = new IOPort(0x3C4);
                var seqData = new IOPort(0x3C5);
                var gcIdx   = new IOPort(0x3CE);
                var gcData  = new IOPort(0x3CF);
                var dacWrite= new IOPort(0x3C8);
                var dacData = new IOPort(0x3C9);

                if (_backupsCaptured)
                {
                    // 1. Restore original glyphs 0xC0..0xDF in Plane 2
                    seqIdx.Byte = 0x02; byte origSeq2 = seqData.Byte;
                    seqIdx.Byte = 0x04; byte origSeq4 = seqData.Byte;
                    gcIdx.Byte  = 0x04; byte origGc4  = gcData.Byte;
                    gcIdx.Byte  = 0x05; byte origGc5  = gcData.Byte;
                    gcIdx.Byte  = 0x06; byte origGc6  = gcData.Byte;

                    seqIdx.Byte = 0x00; seqData.Byte = 0x01;
                    seqIdx.Byte = 0x02; seqData.Byte = 0x04;
                    seqIdx.Byte = 0x04; seqData.Byte = 0x07;
                    seqIdx.Byte = 0x00; seqData.Byte = 0x03;

                    gcIdx.Byte  = 0x04; gcData.Byte  = 0x02;
                    gcIdx.Byte  = 0x05; gcData.Byte  = 0x00;
                    gcIdx.Byte  = 0x06; gcData.Byte  = 0x04;

                    byte* fontRam = (byte*)0xA0000;
                    for (int g = 0; g < 32; g++)
                    {
                        int slotOffset = (0xC0 + g) * 32;
                        for (int row = 0; row < 16; row++)
                        {
                            fontRam[slotOffset + row] = _savedFontGlyphs[g * 16 + row];
                        }
                    }

                    seqIdx.Byte = 0x00; seqData.Byte = 0x01;
                    seqIdx.Byte = 0x02; seqData.Byte = origSeq2;
                    seqIdx.Byte = 0x04; seqData.Byte = origSeq4;
                    seqIdx.Byte = 0x00; seqData.Byte = 0x03;

                    gcIdx.Byte  = 0x04; gcData.Byte  = origGc4;
                    gcIdx.Byte  = 0x05; gcData.Byte  = origGc5;
                    gcIdx.Byte  = 0x06; gcData.Byte  = origGc6;

                    // 2. Restore original 64 DAC palette entries
                    dacWrite.Byte = 0;
                    for (int i = 0; i < 64 * 3; i++)
                    {
                        dacData.Byte = _savedDacPalette[i];
                    }
                }
            }
            catch { }

            _vgaCustomActive = false;
        }

        private static void SetDacColor(IOPort dacWrite, IOPort dacData, byte index, byte r, byte g, byte b)
        {
            dacWrite.Byte = index;
            dacData.Byte = r;
            dacData.Byte = g;
            dacData.Byte = b;
        }

        private static unsafe void WriteGlyph(byte* fontRam, byte charCode,
            byte r0, byte r1, byte r2,  byte r3,  byte r4,  byte r5,  byte r6,  byte r7,
            byte r8, byte r9, byte r10, byte r11, byte r12, byte r13, byte r14, byte r15)
        {
            int off = charCode * 32;
            fontRam[off + 0]  = r0;  fontRam[off + 1]  = r1;  fontRam[off + 2]  = r2;  fontRam[off + 3]  = r3;
            fontRam[off + 4]  = r4;  fontRam[off + 5]  = r5;  fontRam[off + 6]  = r6;  fontRam[off + 7]  = r7;
            fontRam[off + 8]  = r8;  fontRam[off + 9]  = r9;  fontRam[off + 10] = r10; fontRam[off + 11] = r11;
            fontRam[off + 12] = r12; fontRam[off + 13] = r13; fontRam[off + 14] = r14; fontRam[off + 15] = r15;
        }

        private static unsafe void ProgramCustomGlyphs(byte* fontRam)
        {
            // 1. Smooth 45-degree & curved coastal edge slopes (Fg = Land Green, Bg = Ocean Blue)
            // NW Coastline '/' (Top-Left Ocean, Bottom-Right Land; Bit 0 = 1 joins seamlessly to Land on right)
            WriteGlyph(fontRam, GLYPH_COAST_NW,
                0x01, 0x03, 0x03, 0x07, 0x07, 0x0F, 0x0F, 0x1F,
                0x1F, 0x3F, 0x3F, 0x7F, 0x7F, 0xFF, 0xFF, 0xFF);

            // NE Coastline '\' (Top-Right Ocean, Bottom-Left Land; Bit 0 = 0 joins seamlessly to Ocean on right)
            WriteGlyph(fontRam, GLYPH_COAST_NE,
                0x80, 0xC0, 0xC0, 0xE0, 0xE0, 0xF0, 0xF0, 0xF8,
                0xF8, 0xFC, 0xFC, 0xFE, 0xFE, 0xFE, 0xFF, 0xFF);

            // SW Coastline '\' (Bottom-Left Ocean, Top-Right Land; Bit 0 = 1 joins seamlessly to Land on right)
            WriteGlyph(fontRam, GLYPH_COAST_SW,
                0xFF, 0xFF, 0xFF, 0x7F, 0x7F, 0x3F, 0x3F, 0x1F,
                0x1F, 0x0F, 0x0F, 0x07, 0x07, 0x03, 0x03, 0x01);

            // SE Coastline '/' (Bottom-Right Ocean, Top-Left Land; Bit 0 = 0 joins seamlessly to Ocean on right)
            WriteGlyph(fontRam, GLYPH_COAST_SE,
                0xFF, 0xFF, 0xFE, 0xFE, 0xFC, 0xFC, 0xF8, 0xF8,
                0xF0, 0xF0, 0xE0, 0xE0, 0xC0, 0xC0, 0x80, 0x80);

            // North Coastline (Upper 5px Ocean, Lower 11px Land)
            WriteGlyph(fontRam, GLYPH_COAST_NORTH,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x3F, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);

            // South Coastline (Upper 11px Land, Lower 5px Ocean)
            WriteGlyph(fontRam, GLYPH_COAST_SOUTH,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFC, 0x00, 0x00, 0x00, 0x00, 0x00);

            // West Coastline (Left 2px Ocean, Right 7px Rounded Land)
            WriteGlyph(fontRam, GLYPH_COAST_WEST,
                0x1F, 0x3F, 0x3F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F,
                0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x3F, 0x3F, 0x1F);

            // East Coastline (Left 7px Rounded Land, Right 2px Ocean)
            WriteGlyph(fontRam, GLYPH_COAST_EAST,
                0xF8, 0xFC, 0xFC, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE,
                0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFC, 0xFC, 0xF8);

            // Rounded Island / Peninsula Tip
            WriteGlyph(fontRam, GLYPH_ISLAND,
                0x00, 0x00, 0x18, 0x3C, 0x7E, 0x7E, 0xFF, 0xFF,
                0xFF, 0xFF, 0x7E, 0x7E, 0x3C, 0x18, 0x00, 0x00);

            // 2. Internal Country Borders & Mountain Highland Relief (Fg = Olive Green, Bg = Land Green)
            WriteGlyph(fontRam, GLYPH_BORDER_HORIZ,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xAA,
                0x55, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);

            WriteGlyph(fontRam, GLYPH_BORDER_VERT,
                0x18, 0x00, 0x18, 0x00, 0x18, 0x00, 0x18, 0x00,
                0x18, 0x00, 0x18, 0x00, 0x18, 0x00, 0x18, 0x00);

            WriteGlyph(fontRam, GLYPH_HIGHLAND,
                0x22, 0x55, 0x88, 0x55, 0x22, 0x55, 0x88, 0x55,
                0x22, 0x55, 0x88, 0x55, 0x22, 0x55, 0x88, 0x55);

            // 3. 18x16 Two-Cell Broadcast Weather Icon Sprites
            // RADIANT SUN (0xD0 Left + 0xD1 Right)
            WriteGlyph(fontRam, GLYPH_SUN_L,
                0x01, 0x21, 0x10, 0x07, 0x0F, 0x1F, 0x1F, 0xDF,
                0xDF, 0x1F, 0x1F, 0x0F, 0x07, 0x10, 0x21, 0x01);
            WriteGlyph(fontRam, GLYPH_SUN_R,
                0x80, 0x84, 0x08, 0xE0, 0xF0, 0xF8, 0xF8, 0xFB,
                0xFB, 0xF8, 0xF8, 0xF0, 0xE0, 0x08, 0x84, 0x80);

            // PARTLY CLOUDY: Left Sun in Yellow (0xD2) + Right Cloud in White (0xD3)
            WriteGlyph(fontRam, GLYPH_PARTCLOUD_L,
                0x02, 0x22, 0x10, 0x0F, 0x1F, 0x3F, 0xFF, 0x3F,
                0x3F, 0x1F, 0x0F, 0x00, 0x00, 0x00, 0x00, 0x00);
            WriteGlyph(fontRam, GLYPH_PARTCLOUD_R,
                0x00, 0x00, 0x00, 0x1C, 0x3E, 0x7F, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFF, 0xFE, 0x7C, 0x00, 0x00, 0x00);

            // CLOUDY / OVERCAST: Sculpted Two-Cell Cloud (0xD4 Left + 0xD5 Right)
            WriteGlyph(fontRam, GLYPH_CLOUD_L,
                0x00, 0x00, 0x00, 0x03, 0x07, 0x1F, 0x3F, 0x7F,
                0xFF, 0xFF, 0xFF, 0x7F, 0x3F, 0x00, 0x00, 0x00);
            WriteGlyph(fontRam, GLYPH_CLOUD_R,
                0x00, 0x00, 0x38, 0x7C, 0xFE, 0xFF, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFF, 0xFE, 0xFC, 0x00, 0x00, 0x00);

            // RAIN SHOWERS: Cloud + Diagonal Raindrops (0xD6 Left + 0xD7 Right)
            WriteGlyph(fontRam, GLYPH_RAIN_L,
                0x00, 0x03, 0x07, 0x1F, 0x3F, 0x7F, 0xFF, 0xFF,
                0x7F, 0x00, 0x11, 0x22, 0x44, 0x11, 0x22, 0x00);
            WriteGlyph(fontRam, GLYPH_RAIN_R,
                0x1C, 0x3E, 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE,
                0xFC, 0x00, 0x44, 0x88, 0x10, 0x44, 0x88, 0x00);

            // THUNDERSTORM: Cloud (0xD8 Left) + Lightning Bolt (0xD9 Right)
            WriteGlyph(fontRam, GLYPH_STORM_L,
                0x00, 0x03, 0x07, 0x1F, 0x3F, 0x7F, 0xFF, 0xFF,
                0x7F, 0x00, 0x08, 0x10, 0x20, 0x00, 0x00, 0x00);
            WriteGlyph(fontRam, GLYPH_STORM_R,
                0x1C, 0x3E, 0x7F, 0xFF, 0xFF, 0xFE, 0x1C, 0x38,
                0x70, 0x3E, 0x1C, 0x38, 0x70, 0x60, 0xC0, 0x00);

            // SNOW: Cloud + Snowflakes (0xDA Left + 0xDB Right)
            WriteGlyph(fontRam, GLYPH_SNOW_L,
                0x00, 0x03, 0x07, 0x1F, 0x3F, 0x7F, 0xFF, 0xFF,
                0x7F, 0x00, 0x2A, 0x14, 0x2A, 0x00, 0x00, 0x00);
            WriteGlyph(fontRam, GLYPH_SNOW_R,
                0x1C, 0x3E, 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE,
                0xFC, 0x00, 0x54, 0x28, 0x54, 0x00, 0x00, 0x00);
        }

        // =====================================================================
        // FULL-SCREEN 80x25 BROADCAST RENDERER
        // =====================================================================

        public void Draw(TuiDesktop desktop)
        {
            if (!_initialized)
            {
                SyncRegionFromSettings(desktop);
                _initialized = true;
            }

            EnterFullScreenBroadcastMode();

            if (_zones == null || _zones.Length == 0) return;
            if (_currentZone < 0 || _currentZone >= _zones.Length) _currentZone = 0;
            var stations = _zones[_currentZone];
            if (stations == null || stations.Length == 0) return;
            if (_selectedStation < 0 || _selectedStation >= stations.Length) _selectedStation = 0;

            // Row 0: Full-Screen Broadcast Top Bar (80 columns)
            string topBar = " WEATHEROS SYNOPTIC TV  |  SECTOR: [" + ZoneNames[_currentZone] + "]  |  [Tab/C] Continent  [<>] Station  [R] Sync";
            WriteFullRow(0, topBar, ConsoleColor.White, ConsoleColor.DarkCyan);

            // Rows 1..22: Full-Screen 80x22 Continental Map with Pixel-Sloped Coastlines
            DrawContinentMapFullScreen();

            // Overlay 18x16 Pixel Weather Icon Sprites + Temperatures directly on the Map
            DrawStationsFullScreen();

            // Rows 23..24: Bottom Broadcast Telemetry & Station Ribbon Bar
            DrawBottomBroadcastHud();
        }

        private void SyncWithLiveReading(TuiDesktop desktop)
        {
            try
            {
                var cur = desktop.WeatherService.GetLatestReading();
                if (cur.Condition != null && !cur.Condition.Contains("Failure") && !cur.Condition.Contains("Offline") && !cur.Condition.Contains("Awaiting"))
                {
                    if (_currentZone == 0 && _zones[0].Length > 0)
                    {
                        _zones[0][0].TempC = (int)cur.TemperatureCelsius;
                        _zones[0][0].Humidity = (int)cur.HumidityPercent;
                        _zones[0][0].WindKmh = (int)cur.WindSpeedKmh;
                        _zones[0][0].Pressure = (int)cur.PressureHpa;
                        _zones[0][0].Condition = cur.Condition;
                        _zones[0][0].IsLive = true;
                    }
                }
            }
            catch { }
        }

        private bool IsLandCell(string[] map, int x, int y)
        {
            if (y < 0 || y >= map.Length) return false;
            string line = map[y];
            if (x < 0 || x >= line.Length) return false;
            char c = line[x];
            return c != ' ';
        }

        private void DrawContinentMapFullScreen()
        {
            string[] map = _continentMaps[_currentZone];
            if (map == null) return;

            for (int my = 0; my < 22; my++)
            {
                int screenY = 1 + my;
                string line = (my < map.Length) ? map[my] : "";

                for (int mx = 0; mx < 80; mx++)
                {
                    char c = (mx < line.Length) ? line[mx] : ' ';

                    if (c == ' ')
                    {
                        // Deep Broadcast Ocean
                        PutVgaCell(mx, screenY, (byte)' ', ConsoleColor.DarkBlue, ConsoleColor.DarkBlue);
                        continue;
                    }

                    // Check orthogonal neighbors to auto-bevel outer coastlines with 8x16 pixel slopes
                    bool upLand    = IsLandCell(map, mx, my - 1);
                    bool downLand  = IsLandCell(map, mx, my + 1);
                    bool leftLand  = IsLandCell(map, mx - 1, my);
                    bool rightLand = IsLandCell(map, mx + 1, my);

                    if (!upLand && !leftLand && downLand && rightLand)
                    {
                        // NW 45-degree diagonal coastline '/'
                        PutVgaCell(mx, screenY, GLYPH_COAST_NW, ConsoleColor.DarkGreen, ConsoleColor.DarkBlue);
                    }
                    else if (!upLand && !rightLand && downLand && leftLand)
                    {
                        // NE 45-degree diagonal coastline '\'
                        PutVgaCell(mx, screenY, GLYPH_COAST_NE, ConsoleColor.DarkGreen, ConsoleColor.DarkBlue);
                    }
                    else if (!downLand && !leftLand && upLand && rightLand)
                    {
                        // SW 45-degree diagonal coastline '\'
                        PutVgaCell(mx, screenY, GLYPH_COAST_SW, ConsoleColor.DarkGreen, ConsoleColor.DarkBlue);
                    }
                    else if (!downLand && !rightLand && upLand && leftLand)
                    {
                        // SE 45-degree diagonal coastline '/'
                        PutVgaCell(mx, screenY, GLYPH_COAST_SE, ConsoleColor.DarkGreen, ConsoleColor.DarkBlue);
                    }
                    else if (!upLand && !downLand && !leftLand && !rightLand)
                    {
                        // Isolated small island
                        PutVgaCell(mx, screenY, GLYPH_ISLAND, ConsoleColor.DarkGreen, ConsoleColor.DarkBlue);
                    }
                    else if (!upLand && downLand && leftLand && rightLand)
                    {
                        // Gentle horizontal North coast
                        PutVgaCell(mx, screenY, GLYPH_COAST_NORTH, ConsoleColor.DarkGreen, ConsoleColor.DarkBlue);
                    }
                    else if (!downLand && upLand && leftLand && rightLand)
                    {
                        // Gentle horizontal South coast
                        PutVgaCell(mx, screenY, GLYPH_COAST_SOUTH, ConsoleColor.DarkGreen, ConsoleColor.DarkBlue);
                    }
                    else if (!leftLand && rightLand && upLand && downLand)
                    {
                        // Rounded vertical West coast
                        PutVgaCell(mx, screenY, GLYPH_COAST_WEST, ConsoleColor.DarkGreen, ConsoleColor.DarkBlue);
                    }
                    else if (!rightLand && leftLand && upLand && downLand)
                    {
                        // Rounded vertical East coast
                        PutVgaCell(mx, screenY, GLYPH_COAST_EAST, ConsoleColor.DarkGreen, ConsoleColor.DarkBlue);
                    }
                    else
                    {
                        // Solid Interior Landmass (with optional highland relief or subtle country borders)
                        if (c == '^')
                        {
                            PutVgaCell(mx, screenY, GLYPH_HIGHLAND, ConsoleColor.Green, ConsoleColor.DarkGreen);
                        }
                        else if (c == '-')
                        {
                            PutVgaCell(mx, screenY, GLYPH_BORDER_HORIZ, ConsoleColor.Green, ConsoleColor.DarkGreen);
                        }
                        else if (c == '|')
                        {
                            PutVgaCell(mx, screenY, GLYPH_BORDER_VERT, ConsoleColor.Green, ConsoleColor.DarkGreen);
                        }
                        else
                        {
                            PutVgaCell(mx, screenY, (byte)' ', ConsoleColor.DarkGreen, ConsoleColor.DarkGreen);
                        }
                    }
                }
            }
        }

        private void DrawStationsFullScreen()
        {
            var stations = _zones[_currentZone];
            if (stations == null) return;

            // Draw all unselected stations first, then draw the selected station on top
            for (int i = 0; i < stations.Length; i++)
            {
                if (i == _selectedStation) continue;
                DrawStationSpriteAndTemp(stations[i], false);
            }

            if (_selectedStation >= 0 && _selectedStation < stations.Length)
            {
                DrawStationSpriteAndTemp(stations[_selectedStation], true);
            }
        }

        private void DrawStationSpriteAndTemp(GlobalStation st, bool isSelected)
        {
            int x = st.MapX;
            int y = 1 + st.MapY; // Map starts at screen row 1
            if (y < 1 || y > 22 || x < 0 || x >= 75) return;

            byte glyphL, glyphR;
            ConsoleColor colorL, colorR;
            GetWeatherSpritePair(st.Condition, out glyphL, out glyphR, out colorL, out colorR);

            // Unselected stations sit directly on the lush green landmass (zero black boxes!)
            // Selected station uses a high-contrast broadcast highlight pill
            ConsoleColor bg = isSelected ? ConsoleColor.DarkCyan : ConsoleColor.DarkGreen;
            ConsoleColor tempFg = isSelected ? ConsoleColor.Yellow : ConsoleColor.White;

            int curX = x;

            if (isSelected && curX > 0)
            {
                PutVgaCell(curX - 1, y, (byte)'[', ConsoleColor.Yellow, bg);
            }

            // 18x16 Two-Cell Graphical Weather Icon Sprite
            PutVgaCell(curX,     y, glyphL, colorL, bg);
            PutVgaCell(curX + 1, y, glyphR, colorR, bg);

            // Temperature string (e.g. "15°") right next to the icon
            string tempStr = st.TempC.ToString();
            int tx = curX + 2;
            for (int i = 0; i < tempStr.Length && (tx + i) < 79; i++)
            {
                PutVgaCell(tx + i, y, (byte)tempStr[i], tempFg, bg);
            }
            int degX = tx + tempStr.Length;
            if (degX < 79)
            {
                PutVgaCell(degX, y, 0xF8, tempFg, bg); // CP437 degree symbol '°'
            }

            if (isSelected && (degX + 1) < 80)
            {
                PutVgaCell(degX + 1, y, (byte)']', ConsoleColor.Yellow, bg);
            }
        }

        private void GetWeatherSpritePair(string condition, out byte glyphL, out byte glyphR, out ConsoleColor colorL, out ConsoleColor colorR)
        {
            string c = (condition ?? "").ToLower();

            if (c.Contains("thunder") || c.Contains("storm"))
            {
                glyphL = GLYPH_STORM_L;
                glyphR = GLYPH_STORM_R;
                colorL = ConsoleColor.White;
                colorR = ConsoleColor.Yellow;
            }
            else if (c.Contains("rain") || c.Contains("drizzle") || c.Contains("shower"))
            {
                glyphL = GLYPH_RAIN_L;
                glyphR = GLYPH_RAIN_R;
                colorL = ConsoleColor.White;
                colorR = ConsoleColor.Cyan;
            }
            else if (c.Contains("snow") || c.Contains("ice") || c.Contains("flurr"))
            {
                glyphL = GLYPH_SNOW_L;
                glyphR = GLYPH_SNOW_R;
                colorL = ConsoleColor.White;
                colorR = ConsoleColor.White;
            }
            else if (c.Contains("part") || c.Contains("scattered"))
            {
                // Multi-color 2-tone icon: Yellow Sun on Left + White Cloud on Right!
                glyphL = GLYPH_PARTCLOUD_L;
                glyphR = GLYPH_PARTCLOUD_R;
                colorL = ConsoleColor.Yellow;
                colorR = ConsoleColor.White;
            }
            else if (c.Contains("cloud") || c.Contains("overcast") || c.Contains("mist") || c.Contains("fog"))
            {
                glyphL = GLYPH_CLOUD_L;
                glyphR = GLYPH_CLOUD_R;
                colorL = ConsoleColor.White;
                colorR = ConsoleColor.White;
            }
            else
            {
                // Radiant Broadcast Sun
                glyphL = GLYPH_SUN_L;
                glyphR = GLYPH_SUN_R;
                colorL = ConsoleColor.Yellow;
                colorR = ConsoleColor.Yellow;
            }
        }

        private void DrawBottomBroadcastHud()
        {
            var stations = _zones[_currentZone];
            if (stations == null || _selectedStation < 0 || _selectedStation >= stations.Length) return;

            var sel = stations[_selectedStation];
            string liveBadge = sel.IsLive ? "[LIVE API]" : "[SYNOPTIC]";

            // Row 23: Complete Selected Station Telemetry Bar
            string row23;
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                row23 = " > " + sel.Name.ToUpper() + " (" + sel.Code + "): " + sel.TempC + "ºC " + sel.Condition + "  |  " + _statusMessage;
            }
            else
            {
                row23 = " > " + sel.Name.ToUpper() + " (" + sel.Country + ") [" + sel.Lat + "," + sel.Lon + "] | " +
                        sel.TempC + "ºC " + sel.Condition + " | Hum:" + sel.Humidity + "% Wind:" + sel.WindKmh + "km/h " + sel.Pressure + "hPa " + liveBadge;
            }
            WriteFullRow(23, row23, ConsoleColor.Yellow, ConsoleColor.DarkCyan);

            // Row 24: Scrolling Station Selector Ribbon + Exit/Dashboard Shortcuts
            string ribbon = " STATIONS: ";
            int total = stations.Length;
            int windowSize = 6;
            int startIdx = _selectedStation - (windowSize / 2);
            if (startIdx < 0) startIdx = 0;
            if (startIdx + windowSize > total) startIdx = Math.Max(0, total - windowSize);

            for (int i = startIdx; i < total && i < startIdx + windowSize; i++)
            {
                if (i == _selectedStation) ribbon += "[" + stations[i].Name + "] ";
                else ribbon += stations[i].Name + " ";
            }

            string rightHint = "[Enter] Open  [ESC] Exit ";
            int maxLeft = 80 - rightHint.Length;
            if (ribbon.Length > maxLeft) ribbon = ribbon.Substring(0, maxLeft);
            else ribbon = ribbon.PadRight(maxLeft, ' ');

            WriteFullRow(24, ribbon + rightHint, ConsoleColor.White, ConsoleColor.Black);
        }

        public static unsafe void PutVgaCell(int x, int y, byte glyph, ConsoleColor fg, ConsoleColor bg)
        {
            if (x < 0 || x >= 80 || y < 0 || y >= 25) return;
            byte* vga = (byte*)0xB8000;
            int offset = (y * 80 + x) * 2;
            vga[offset] = glyph;
            vga[offset + 1] = (byte)((((byte)bg & 0x0F) << 4) | ((byte)fg & 0x0F));
        }

        public static unsafe void WriteFullRow(int y, string text, ConsoleColor fg, ConsoleColor bg)
        {
            if (y < 0 || y >= 25) return;
            string s = text ?? "";
            for (int x = 0; x < 80; x++)
            {
                byte b = (byte)' ';
                if (x < s.Length)
                {
                    char ch = s[x];
                    if (ch == 'º' || ch == '°' || ch == '\u00B0' || ch == '\u00BA' || (int)ch == 248)
                    {
                        b = 0xF8;
                    }
                    else if (ch <= 127)
                    {
                        b = (byte)ch;
                    }
                }
                PutVgaCell(x, y, b, fg, bg);
            }
        }

        public static unsafe void DirectVgaWrite(int x, int y, string text, ConsoleColor fg, ConsoleColor bg)
        {
            if (x < 0 || x >= 80 || y < 0 || y >= 25 || string.IsNullOrEmpty(text)) return;

            for (int i = 0; i < text.Length && (x + i) < 80; i++)
            {
                char ch = text[i];
                byte b = (byte)ch;

                if (ch == 'º' || ch == '°' || ch == '\u00B0' || ch == '\u00BA' || (int)ch == 248)
                {
                    b = 0xF8;
                }
                else if (ch == '☼' || (int)ch == 15)
                {
                    b = 0x0F;
                }
                else if (ch > 127)
                {
                    b = (byte)' ';
                }

                PutVgaCell(x + i, y, b, fg, bg);
            }
        }

        private void FetchLiveStation(TuiDesktop desktop)
        {
            var stations = _zones[_currentZone];
            if (stations == null || _selectedStation < 0 || _selectedStation >= stations.Length) return;

            var sel = stations[_selectedStation];
            _statusMessage = "Syncing live API for " + sel.Name + "...";
            Draw(desktop);

            try
            {
                desktop.WeatherService.FetchOpenWeather(sel.Lat + "," + sel.Lon, desktop.ApiKey);
                var live = desktop.WeatherService.GetLatestReading();

                if (live.Condition != null && !live.Condition.Contains("Failure") && !live.Condition.Contains("Timeout") && !live.Condition.Contains("Offline"))
                {
                    stations[_selectedStation].TempC = (int)live.TemperatureCelsius;
                    stations[_selectedStation].Humidity = (int)live.HumidityPercent;
                    stations[_selectedStation].WindKmh = (int)live.WindSpeedKmh;
                    stations[_selectedStation].Pressure = (int)live.PressureHpa;
                    stations[_selectedStation].Condition = live.Condition;
                    stations[_selectedStation].IsLive = true;
                    _statusMessage = "Live API synced: " + sel.Name + " (" + stations[_selectedStation].TempC + "ºC, " + live.Condition + ")";
                }
                else
                {
                    int t, h, w;
                    string cond;
                    if (desktop.WeatherService.FetchQuickStationWeather(sel.Lat, sel.Lon, out t, out h, out w, out cond))
                    {
                        stations[_selectedStation].TempC = t;
                        stations[_selectedStation].Humidity = h;
                        stations[_selectedStation].WindKmh = w;
                        stations[_selectedStation].Condition = cond;
                        stations[_selectedStation].IsLive = true;
                        _statusMessage = "Open-Meteo synced: " + sel.Name + " (" + t + "ºC, " + cond + ")";
                    }
                    else
                    {
                        _statusMessage = "Offline baseline retained for " + sel.Name;
                    }
                }
            }
            catch (Exception ex)
            {
                _statusMessage = "Sync error: " + ex.Message;
            }
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            var stations = _zones[_currentZone];
            if (stations == null || stations.Length == 0) return;

            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                ExitFullScreenBroadcastMode();
                desktop.SetCurrentApp("launcher");
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.RightArrow || keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                _selectedStation++;
                if (_selectedStation >= stations.Length) _selectedStation = 0;
                _statusMessage = "";
            }
            else if (keyEvent.Key == ConsoleKeyEx.LeftArrow || keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                _selectedStation--;
                if (_selectedStation < 0) _selectedStation = stations.Length - 1;
                _statusMessage = "";
            }
            else if (keyEvent.Key == ConsoleKeyEx.Tab || keyEvent.KeyChar == 'c' || keyEvent.KeyChar == 'C' || keyEvent.KeyChar == 'z' || keyEvent.KeyChar == 'Z')
            {
                _currentZone = (_currentZone + 1) % _zones.Length;
                _selectedStation = 0;
                _statusMessage = "Sector: " + ZoneDescriptions[_currentZone];
            }
            else if (keyEvent.KeyChar == 'r' || keyEvent.KeyChar == 'R')
            {
                FetchLiveStation(desktop);
            }
            else if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                var sel = stations[_selectedStation];
                _statusMessage = "Opening " + sel.Name + " in Weather Dashboard...";
                Draw(desktop);
                ExitFullScreenBroadcastMode();
                desktop.WeatherService.FetchOpenWeather(sel.Lat + "," + sel.Lon, desktop.ApiKey);
                desktop.SetCurrentApp("weather");
            }
        }
    }
}
