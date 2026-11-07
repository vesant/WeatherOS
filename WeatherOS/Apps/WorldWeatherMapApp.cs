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
    /// Dedicated Full-Screen Meteorological Broadcast Mode (80x25, 720x400 VGA)
    /// with 5 Real-Time Multi-Spectral Meteorological Map Layers:
    ///   0: SYNOPTIC ICONS   (18x16 Weather Sprites & Station Temperatures)
    ///   1: THERMAL HEATMAP  (6-Band Continental Isotherms & Thermal Zone Classification)
    ///   2: PRECIP RADAR     (Synoptic Rain Bands, Frontal Showers & Cloud Cover)
    ///   3: WIND VECTORS     (Directional Wind Streamlines & Synoptic Wind Classification)
    ///   4: WEATHER RADAR    (Animated 16-Frame Loop NEXRAD Doppler Reflectivity Sweep)
    /// </summary>
    public class WorldWeatherMapApp : IApp
    {
        public string Id => "world_map";
        public string Name => "World Weather Map";
        public bool IsRealTime => false;

        private bool _initialized = false;
        private int _currentZone = 0;   // 0=Europe, 1=Asia, 2=Americas, 3=Oceania, 4=Africa
        private int _currentLayer = 0;  // 0=Synoptic, 1=Thermal, 2=Precip, 3=Wind, 4=Weather Radar (Animated Loop)
        private int _selectedStation = 0;
        private string _statusMessage = "";

        // Real-time zero-allocation animation state for Layer 4 (5/5 Weather Radar)
        private int _radarFrame = 0;
        private int _animTickCounter = 0;
        private bool _radarPaused = false;

        private GlobalStation[][] _zones;
        private string[][] _continentMaps;

        // Custom VGA Plane 2 Glyph Codes (0xC0..0xDF seamlessly bridge the 9th pixel column in hardware)
        public const byte GLYPH_COAST_NW        = 0xC0; // '/' NW sloped coast (top-left ocean, bottom-right land)
        public const byte GLYPH_COAST_NE        = 0xC1; // '\' NE sloped coast (top-right ocean, bottom-left land)
        public const byte GLYPH_COAST_SW        = 0xC2; // '\' SW sloped coast (bottom-left ocean, top-right land)
        public const byte GLYPH_COAST_SE        = 0xC3; // '/' SE sloped coast (bottom-right ocean, top-left land)
        public const byte GLYPH_COAST_NORTH     = 0xC4; // Smooth horizontal North coast (top ocean, bottom land)
        public const byte GLYPH_COAST_SOUTH     = 0xC5; // Smooth horizontal South coast (top land, bottom ocean)
        public const byte GLYPH_COAST_WEST      = 0xC6; // Smooth vertical West coast (left ocean, right land)
        public const byte GLYPH_COAST_EAST      = 0xC7; // Smooth vertical East coast (left land, right ocean)
        public const byte GLYPH_ISLAND          = 0xC8; // Rounded isolated island
        public const byte GLYPH_BORDER_HORIZ    = 0xC9; // Subtle 1px horizontal country border on land
        public const byte GLYPH_BORDER_VERT     = 0xCA; // Subtle 1px vertical country border on land
        public const byte GLYPH_HIGHLAND        = 0xCB; // Topographical mountain / highland relief on land
        public const byte GLYPH_PENINSULA_VERT  = 0xCC; // Narrow vertical peninsula / isthmus (ocean W & E)
        public const byte GLYPH_PENINSULA_HORIZ = 0xCD; // Narrow horizontal peninsula / island arc (ocean N & S)
        public const byte GLYPH_CAPE_NORTH      = 0xCE; // Rounded northern cape tip (land only S)
        public const byte GLYPH_CAPE_SOUTH      = 0xCF; // Rounded southern peninsula tip (land only N)

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
            "European & Mediterranean Sector",
            "Euro-Asian & Pacific Sector",
            "North & South American Sector",
            "Australian & Pacific Sector",
            "African Continental Sector"
        };

        private static readonly string[] LayerNames = new string[]
        {
            "1/5 SYNOPTIC ICONS",
            "2/5 THERMAL HEATMAP",
            "3/5 PRECIP RADAR",
            "4/5 WIND VECTORS",
            "5/5 WEATHER RADAR"
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
            WorldWeatherMapLayers.InvalidateGrid();
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

            // 0: EUROPE (Accurately mapped to the high-realism 80x22 European continental projection)
            _zones[0] = new GlobalStation[]
            {
                new GlobalStation("Lisbon",    "38.72,-9.14",   "Portugal",       "PT", "38.72",  "-9.14",  4, 16,  18, 74, 12, 1018, "Partly Cloudy"),
                new GlobalStation("Madrid",    "40.42,-3.70",   "Spain",          "ES", "40.42",  "-3.70", 11, 15,  16, 49, 11, 1019, "Clear"),
                new GlobalStation("London",    "51.51,-0.13",   "United Kingdom", "UK", "51.51",  "-0.13", 15,  8,  14, 76, 18, 1014, "Partly Cloudy"),
                new GlobalStation("Paris",     "48.86,2.35",    "France",         "FR", "48.86",   "2.35", 20, 11,  13, 67, 12, 1016, "Partly Cloudy"),
                new GlobalStation("Berlin",    "52.52,13.40",   "Germany",        "DE", "52.52",  "13.40", 35,  9,   9, 91, 15, 1011, "Rain"),
                new GlobalStation("Oslo",      "59.91,10.75",   "Norway",         "NO", "59.91",  "10.75", 33,  4,   6, 88, 14, 1013, "Cloudy"),
                new GlobalStation("Rome",      "41.90,12.50",   "Italy",          "IT", "41.90",  "12.50", 32, 15,  15, 76, 10, 1017, "Clear"),
                new GlobalStation("Vienna",    "48.21,16.37",   "Austria",        "AT", "48.21",  "16.37", 39, 13,  11, 79, 12, 1015, "Partly Cloudy"),
                new GlobalStation("Warsaw",    "52.23,21.01",   "Poland",         "PL", "52.23",  "21.01", 47,  9,  10, 92, 14, 1010, "Rain"),
                new GlobalStation("Athens",    "37.98,23.73",   "Greece",         "GR", "37.98",  "23.73", 44, 17,  16, 80, 12, 1018, "Partly Cloudy"),
                new GlobalStation("Ankara",    "39.93,32.86",   "Turkey",         "TR", "39.93",  "32.86", 60, 16,  11, 93, 14, 1012, "Rain"),
                new GlobalStation("Moscow",    "55.75,37.62",   "Russia",         "RU", "55.75",  "37.62", 62,  6,   5, 65, 16, 1009, "Cloudy"),
                new GlobalStation("Rabat",     "34.02,-6.83",   "Morocco",        "MA", "34.02",  "-6.83",  7, 20,  19, 81, 12, 1019, "Partly Cloudy"),
                new GlobalStation("Algiers",   "36.75,3.06",    "Algeria",        "DZ", "36.75",   "3.06", 20, 20,  20, 60, 11, 1018, "Clear"),
                new GlobalStation("Cairo",     "30.04,31.24",   "Egypt",          "EG", "30.04",  "31.24", 65, 20,  23, 50, 14, 1017, "Clear")
            };

            // 1: ASIA (80x22 full-screen map)
            _zones[1] = new GlobalStation[]
            {
                new GlobalStation("Ankara",    "39.93,32.86",   "Turkey",         "TR", "39.93",  "32.86",  5,  5,  11, 93, 11, 1016, "Partly Cloudy"),
                new GlobalStation("Riyadh",    "24.71,46.68",   "Saudi Arabia",   "SA", "24.71",  "46.68", 10, 10,  26, 22, 14, 1015, "Clear"),
                new GlobalStation("New Delhi", "28.61,77.21",   "India",          "IN", "28.61",  "77.21", 28, 10,  27, 48, 10, 1014, "Clear"),
                new GlobalStation("Beijing",   "39.90,116.41",  "China",          "CN", "39.90", "116.41", 50,  7,  12, 45, 15, 1018, "Partly Cloudy"),
                new GlobalStation("Bangkok",   "13.76,100.50",  "Thailand",       "TH", "13.76", "100.50", 47, 14,  29, 88, 16, 1009, "Thunderstorm"),
                new GlobalStation("Singapore", "1.35,103.82",   "Singapore",      "SG",  "1.35", "103.82", 48, 18,  28, 86, 14, 1010, "Rain"),
                new GlobalStation("Jakarta",   "-6.21,106.85",  "Indonesia",      "ID", "-6.21", "106.85", 49, 21,  29, 84, 12, 1010, "Rain"),
                new GlobalStation("Seoul",     "37.57,126.98",  "South Korea",    "KR", "37.57", "126.98", 59,  8,  11, 58, 12, 1019, "Clear"),
                new GlobalStation("Tokyo",     "35.68,139.65",  "Japan",          "JP", "35.68", "139.65", 70,  9,  16, 60, 14, 1020, "Clear")
            };

            // 2: AMERICAS (80x22 full-screen map)
            _zones[2] = new GlobalStation[]
            {
                new GlobalStation("Ottawa",       "45.42,-75.70",  "Canada",         "CA",  "45.42", "-75.70", 34,  4,   8, 68, 16, 1016, "Cloudy"),
                new GlobalStation("New York",     "40.71,-74.01",  "United States",  "US",  "40.71", "-74.01", 34,  6,  15, 59, 14, 1015, "Partly Cloudy"),
                new GlobalStation("Washington",   "38.91,-77.04",  "United States",  "US",  "38.91", "-77.04", 28,  7,  17, 61, 12, 1016, "Partly Cloudy"),
                new GlobalStation("Mexico City",  "19.43,-99.13",  "Mexico",         "MX",  "19.43", "-99.13", 20, 10,  21, 52, 10, 1018, "Clear"),
                new GlobalStation("Havana",       "23.11,-82.37",  "Cuba",           "CU",  "23.11", "-82.37", 42, 10,  27, 86, 22, 1009, "Thunderstorm"),
                new GlobalStation("Bogota",       "4.71,-74.07",   "Colombia",       "CO",   "4.71", "-74.07", 36, 13,  16, 84, 10, 1013, "Rain"),
                new GlobalStation("Lima",         "-12.05,-77.04", "Peru",           "PE", "-12.05", "-77.04", 32, 16,  19, 78, 12, 1014, "Cloudy"),
                new GlobalStation("Brasilia",     "-15.79,-47.88", "Brazil",         "BR", "-15.79", "-47.88", 51, 16,  25, 85, 14, 1011, "Thunderstorm"),
                new GlobalStation("Santiago",     "-33.45,-70.67", "Chile",          "CL", "-33.45", "-70.67", 35, 19,  14, 55, 11, 1016, "Clear"),
                new GlobalStation("Buenos Aires", "-34.60,-58.38", "Argentina",      "AR", "-34.60", "-58.38", 46, 19,  20, 67, 16, 1016, "Partly Cloudy")
            };

            // 3: OCEANIA (80x22 full-screen map)
            _zones[3] = new GlobalStation[]
            {
                new GlobalStation("Jakarta",      "-6.21,106.85",  "Indonesia",      "ID",  "-6.21", "106.85", 11,  3,  29, 84, 12, 1010, "Rain"),
                new GlobalStation("Port Moresby", "-9.44,147.18",  "Papua N. Guinea","PG",  "-9.44", "147.18", 40,  5,  29, 88, 18, 1009, "Thunderstorm"),
                new GlobalStation("Sydney",       "-33.87,151.21", "Australia",      "AU", "-33.87", "151.21", 45, 13,  19, 65, 16, 1020, "Partly Cloudy"),
                new GlobalStation("Canberra",     "-35.28,149.13", "Australia",      "AU", "-35.28", "149.13", 41, 15,  14, 82, 12, 1022, "Clear"),
                new GlobalStation("Melbourne",    "-37.81,144.96", "Australia",      "AU", "-37.81", "144.96", 36, 16,  11, 74, 15, 1021, "Cloudy"),
                new GlobalStation("Suva",         "-18.14,178.44", "Fiji",           "FJ", "-18.14", "178.44", 66,  8,  26, 85, 18, 1011, "Rain"),
                new GlobalStation("Wellington",   "-41.29,174.78", "New Zealand",    "NZ", "-41.29", "174.78", 66, 17,   9, 78, 22, 1014, "Cloudy")
            };

            // 4: AFRICA (80x22 full-screen map)
            _zones[4] = new GlobalStation[]
            {
                new GlobalStation("Rabat",        "34.02,-6.83",   "Morocco",        "MA",  "34.02",  "-6.83", 16,  2,  19, 81, 12, 1019, "Partly Cloudy"),
                new GlobalStation("Algiers",      "36.75,3.06",    "Algeria",        "DZ",  "36.75",   "3.06", 24,  1,  20, 60, 11, 1018, "Clear"),
                new GlobalStation("Cairo",        "30.04,31.24",   "Egypt",          "EG",  "30.04",  "31.24", 46,  3,  24, 50, 14, 1017, "Clear"),
                new GlobalStation("Dakar",        "14.69,-17.44",  "Senegal",        "SN",  "14.69", "-17.44",  9,  7,  29, 74, 18, 1012, "Partly Cloudy"),
                new GlobalStation("Abuja",        "9.08,7.40",     "Nigeria",        "NG",   "9.08",   "7.40", 27,  9,  28, 88, 14, 1010, "Thunderstorm"),
                new GlobalStation("Addis Ababa",  "9.03,38.74",    "Ethiopia",       "ET",   "9.03",  "38.74", 48,  9,  16, 68, 12, 1018, "Partly Cloudy"),
                new GlobalStation("Nairobi",      "-1.29,36.82",   "Kenya",          "KE",  "-1.29",  "36.82", 47, 12,  21, 76, 11, 1015, "Rain"),
                new GlobalStation("Luanda",       "-8.84,13.23",   "Angola",         "AO",  "-8.84",  "13.23", 29, 14,  25, 79, 14, 1014, "Partly Cloudy"),
                new GlobalStation("Cape Town",    "-33.92,18.42",  "South Africa",   "ZA", "-33.92",  "18.42", 34, 20,  13, 65, 16, 1017, "Clear")
            };
        }

        private void InitContinentMaps()
        {
            _continentMaps = new string[5][];

            // 0: EUROPE (High-realism projection with English Channel, Irish Sea, Danish Straits,
            //    Gulf of Bothnia, Strait of Gibraltar, Italian Boot + Sicily/Sardinia/Corsica, Adriatic,
            //    Peloponnese/Crete, Black Sea + Crimea, Anatolia, Cyprus, and North Africa)
            _continentMaps[0] = new string[]
            {
                "                                      #########             #####    ###########",
                "   #####                           ##############         ########  ############",
                "  #######                        ^^^^#####|#######       #######################",
                "   #####                        ^^^^####|###  #####     ########################",
                "                               ^^^^####|###    #####   ###################^^^###",
                "               ###            ^^^^####|###      ##### ####################^^^###",
                "              #####           ####|######        #####|###################^^^###",
                "         ###  #####                #####      ########|###################^^^###",
                "        ####  ######         ###    ###     ##########|########|###########^^###",
                "         ##    #####         ###  ######### ###########|########|###############",
                "                            #####|#########|###########|########|###############",
                "              #########---#######|#########|###########|########|###########    ",
                "             ############|#######|######|--^^^^^######|########|###  ###        ",
                "              ###########|#####^^^^^^^^^#######|######|######   ###             ",
                "     ######^^^#####|#####|####^^^^^^^^^^#######|#######|####          #####     ",
                "    ####|##########|########|#####^^^#    #####|########|##        ######^^^### ",
                "   #####|##########---######  ##  ###^^    ####|#######   #########^^^^^########",
                "    ####|#########     ##     ##   ####     ###|######   ##########^^^^^####### ",
                "     ###|######                     ###      ###|###       #############        ",
                "                                   ###        ###            ###      ####      ",
                "     ########################     ####                        ##################",
                "  ##############################            ####################################"
            };

            // 1: ASIA (High-realism projection with Anatolia, Caspian Sea, Red Sea, Arabian Peninsula,
            //    Persian Gulf, Triangular Indian Subcontinent + Sri Lanka, Himalayas, Bay of Bengal,
            //    Indochina & Malay Peninsula, Sumatra/Java/Borneo/Sulawesi/Philippines, Korea, Japan & Kamchatka)
            _continentMaps[1] = new string[]
            {
                "                ######################    ######################################",
                "             ###################################################################",
                "          ##########################################################      ######",
                "        #######    ################################################       ##### ",
                "   #########       #####|######################^^^#################       ####  ",
                "  ##########       #####|###############^^^^^######################        ##   ",
                "  #########         ####|##########^^^^^^^^^^################|#####      ####   ",
                "    ###|####        ####|#######^^^^^^^^^^^^^^#########|#####|####          ### ",
                "     ##|######     #####|######^^^^^^^^^^^^^^^^########|#####|###   ###    #### ",
                "     ##|#######   ######|#####^^^^^^^^^^^^^^^^^########|###   ##    ####  ##### ",
                "      #|########    ####|####|^^^^^^^###|######|###########               ###   ",
                "       ##########     #######|##########|######|############                    ",
                "        ########        #####|##########|######|###########     ###             ",
                "         #####            ####|#########|##   #|#########                       ",
                "                           ###|#######|##     ####|######          ###          ",
                "                            ##|######|#        ###|#####           ####         ",
                "                             #|#####           ##|###               ####        ",
                "                              |###              ###                  ##         ",
                "                                ##              ####        ######              ",
                "                                             ####  ##      ########   ###       ",
                "                                              #####        ########   ###   ####",
                "                                                #######               ##   #####"
            };

            // 2: AMERICAS (High-realism projection with Alaska & Aleutians, Hudson Bay, Baffin & Greenland,
            //    Newfoundland, Rocky Mountains, US East Coast, Florida Peninsula, Gulf of Mexico,
            //    Baja California, Yucatan, Cuba & Hispaniola, Panama Isthmus, Andes & Brazilian Bulge)
            _continentMaps[2] = new string[]
            {
                "        ########       #######     #########         #############              ",
                "    ###############   #########   ###########       ###############             ",
                "  ####################^^########     ##########     ##############              ",
                "   ####   ######^^^^############       ############  ############               ",
                "           ^^^^^^###############|######################   ######                ",
                "            ^^^^^^##############|########################                       ",
                "             ^^^^^^#############|#####################                          ",
                "              ^^^^^^############|##################                             ",
                "              ## ^^^^###########|###############                                ",
                "               ## ^^^^######         ###                                        ",
                "                  ^^^^######  ####    ##  ######                                ",
                "                    ^^^^#####                   ####  ###                       ",
                "                       ######        ###########                                ",
                "                         ####      #################                            ",
                "                                ^^^^######################                      ",
                "                               ^^^^^###|###########################             ",
                "                               ^^^^^###|############################            ",
                "                                ^^^^###|#########################               ",
                "                                 ^^^###|#####################                   ",
                "                                  ^^^##|################                        ",
                "                                   ^^^#|############                            ",
                "                                    ^^^|########                                "
            };

            // 3: OCEANIA (High-realism projection with Sumatra/Java/Borneo/Sulawesi, New Guinea & Solomons,
            //    Australia with Kimberley, Arnhem Land, Gulf of Carpentaria, Cape York, Great Australian Bight,
            //    Great Dividing Range, Tasmania across Bass Strait, New Caledonia, Fiji & New Zealand N/S Islands)
            _continentMaps[3] = new string[]
            {
                "   #####      #######                                                           ",
                "    ######   #########   #####                                                  ",
                "      #####  ########   ######      #####                                       ",
                "        ###########     #####     #############                                 ",
                "           ########               ###############   ####                        ",
                "                     #####          ###############   ####                      ",
                "                                       ##########      ####                     ",
                "                        ########        ###                                     ",
                "                     ############      #####                      #####         ",
                "                 #################    #######                    #######        ",
                "               ###############################         ####       #####         ",
                "              #################################                                 ",
                "             #######################|###########                                ",
                "             #######################|###########^^                              ",
                "              ######################|##########^^^                              ",
                "               #######          ####|#########^^^                   ####        ",
                "                #####            ###|##########                    ######       ",
                "                                    ########                      #######       ",
                "                                                          ####   ######         ",
                "                                     #####               #######                ",
                "                                      ###               ######                  ",
                "                                                                                "
            };

            // 4: AFRICA (High-realism projection with Mediterranean N shore, Maghreb & Atlas Mountains,
            //    Gulf of Sidra, Egypt/Nile, Red Sea & Arabia, Wide West African Bulge, Deep Gulf of Guinea,
            //    Ethiopian Highlands & Somali Horn, Great Rift, Southern Africa & Madagascar)
            _continentMaps[4] = new string[]
            {
                "      #########     ###          ####                   ###############         ",
                "                  ###############     ##                   ##############       ",
                "              #####^^^^^^#########                 ###      ##############      ",
                "            ########^^^^^^########   ################        ##############     ",
                "          ###########|############|##################   ############            ",
                "         ############|############|##################    #############          ",
                "        #############|############|###################    ##############        ",
                "       ##############|############|####################    #############        ",
                "        #############|############|#####################     #########          ",
                "         ############|############|#############^^^^############                ",
                "           ##########|############|#############^^^^##########                  ",
                "                         #########|############|#############                   ",
                "                          ########|############|############                    ",
                "                           #######|^^^^########|###########                     ",
                "                           #######|^^^^^#######|#########       ####            ",
                "                            ######|############|########       ######           ",
                "                            ######|############|#######       #######           ",
                "                             #####|###########|#######        ######            ",
                "                              ####|###########|######         #####             ",
                "                               ###|###############                              ",
                "                                ##|#############                                ",
                "                                  ##########                                    "
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

                // 3. Unlock VGA Plane 2 (Font RAM) mapped at 0xA0000
                seqIdx.Byte = 0x00; seqData.Byte = 0x01; // Sync reset
                seqIdx.Byte = 0x02; seqData.Byte = 0x04; // Write enable Plane 2 only
                seqIdx.Byte = 0x04; seqData.Byte = 0x07; // Sequential addressing, extended mem
                seqIdx.Byte = 0x00; seqData.Byte = 0x03; // End reset

                gcIdx.Byte  = 0x04; gcData.Byte  = 0x02; // Read Plane 2
                gcIdx.Byte  = 0x05; gcData.Byte  = 0x00; // Linear read/write mode 0
                gcIdx.Byte  = 0x06; gcData.Byte  = 0x04; // Map Plane 2 window at 0xA0000

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

                // 4. Write Custom 8x16 Coastline Slopes, Peninsulas, Borders & 18x16 Weather Sprites into 0xC0..0xDF
                ProgramCustomGlyphs(fontRam);

                // 5. Restore Sequencer & Graphics Controller back to normal Text Mode (Planes 0 & 1)
                seqIdx.Byte = 0x00; seqData.Byte = 0x01;
                seqIdx.Byte = 0x02; seqData.Byte = origSeq2;
                seqIdx.Byte = 0x04; seqData.Byte = origSeq4;
                seqIdx.Byte = 0x00; seqData.Byte = 0x03;

                gcIdx.Byte  = 0x04; gcData.Byte  = origGc4;
                gcIdx.Byte  = 0x05; gcData.Byte  = origGc5;
                gcIdx.Byte  = 0x06; gcData.Byte  = origGc6;

                // 6. Program Authentic Television Broadcast & NEXRAD Doppler RGB Palette (6-bit DAC values 0..63)
                SetDacColor(dacWrite, dacData,  1,  6, 18, 34); // DarkBlue: Deep Ocean & Radar Base Blue
                SetDacColor(dacWrite, dacData,  2, 14, 28, 10); // DarkGreen: Continental Landmass Forest Green
                SetDacColor(dacWrite, dacData,  3,  5, 23, 30); // DarkCyan: Broadcast HUD / Cold Isotherm Band
                SetDacColor(dacWrite, dacData,  4, 48, 10,  8); // DarkRed: Hot Thermal Core / Doppler Storm Crimson
                SetDacColor(dacWrite, dacData, 20, 52, 28,  4); // DarkYellow (EGA 6->20): Warm Amber / Doppler Heavy Orange
                SetDacColor(dacWrite, dacData, 57, 22, 34, 63); // Blue: NEXRAD Outer Virga Indigo-Blue
                SetDacColor(dacWrite, dacData, 58, 26, 56, 18); // Green: Vibrant NEXRAD Moderate Lime / Temperate Isotherm
                SetDacColor(dacWrite, dacData, 59, 20, 52, 63); // Cyan: Rain / Frost Cyan
                SetDacColor(dacWrite, dacData, 60, 63, 14, 10); // Red: Intense NEXRAD Storm Core Red
                SetDacColor(dacWrite, dacData, 61, 63, 24, 58); // Magenta: NEXRAD Hail Core Magenta
                SetDacColor(dacWrite, dacData, 62, 63, 56,  6); // Yellow: Radiant Broadcast Sun / Heavy Echo Gold
                SetDacColor(dacWrite, dacData, 63, 63, 63, 63); // White: Pure Cloud & Telemetry White

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
            // 1. Mathematically Aligned 8x16 Coastal Slopes, Peninsulas & Cape Tips
            //    (Horizontal coasts align at scanlines 2..13; vertical coasts align at columns 1..7)
            WriteGlyph(fontRam, GLYPH_COAST_NW,
                0x00, 0x00, 0x01, 0x03, 0x03, 0x07, 0x07, 0x0F,
                0x0F, 0x1F, 0x1F, 0x3F, 0x3F, 0x7F, 0x7F, 0x7F);

            WriteGlyph(fontRam, GLYPH_COAST_NE,
                0x00, 0x00, 0x80, 0xC0, 0xC0, 0xE0, 0xE0, 0xF0,
                0xF0, 0xF8, 0xF8, 0xFC, 0xFC, 0xFE, 0xFE, 0xFE);

            WriteGlyph(fontRam, GLYPH_COAST_SW,
                0x7F, 0x7F, 0x7F, 0x3F, 0x3F, 0x1F, 0x1F, 0x0F,
                0x0F, 0x07, 0x07, 0x03, 0x03, 0x01, 0x00, 0x00);

            WriteGlyph(fontRam, GLYPH_COAST_SE,
                0xFE, 0xFE, 0xFE, 0xFC, 0xFC, 0xF8, 0xF8, 0xF0,
                0xF0, 0xE0, 0xE0, 0xC0, 0xC0, 0x80, 0x00, 0x00);

            WriteGlyph(fontRam, GLYPH_COAST_NORTH,
                0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);

            WriteGlyph(fontRam, GLYPH_COAST_SOUTH,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00);

            WriteGlyph(fontRam, GLYPH_COAST_WEST,
                0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F,
                0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F);

            WriteGlyph(fontRam, GLYPH_COAST_EAST,
                0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE,
                0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE);

            WriteGlyph(fontRam, GLYPH_ISLAND,
                0x00, 0x00, 0x18, 0x3C, 0x7E, 0x7E, 0x7E, 0x7E,
                0x7E, 0x7E, 0x7E, 0x7E, 0x3C, 0x18, 0x00, 0x00);

            // 2. Internal Country Borders, Mountain Relief & Narrow Peninsula/Cape Glyphs
            WriteGlyph(fontRam, GLYPH_BORDER_HORIZ,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xAA,
                0x55, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);

            WriteGlyph(fontRam, GLYPH_BORDER_VERT,
                0x18, 0x00, 0x18, 0x00, 0x18, 0x00, 0x18, 0x00,
                0x18, 0x00, 0x18, 0x00, 0x18, 0x00, 0x18, 0x00);

            WriteGlyph(fontRam, GLYPH_HIGHLAND,
                0x22, 0x55, 0x88, 0x55, 0x22, 0x55, 0x88, 0x55,
                0x22, 0x55, 0x88, 0x55, 0x22, 0x55, 0x88, 0x55);

            WriteGlyph(fontRam, GLYPH_PENINSULA_VERT,
                0x7E, 0x7E, 0x7E, 0x7E, 0x7E, 0x7E, 0x7E, 0x7E,
                0x7E, 0x7E, 0x7E, 0x7E, 0x7E, 0x7E, 0x7E, 0x7E);

            WriteGlyph(fontRam, GLYPH_PENINSULA_HORIZ,
                0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00);

            WriteGlyph(fontRam, GLYPH_CAPE_NORTH,
                0x00, 0x00, 0x18, 0x3C, 0x3C, 0x7E, 0x7E, 0x7E,
                0x7E, 0x7E, 0x7E, 0x7E, 0x7E, 0x7E, 0x7E, 0x7E);

            WriteGlyph(fontRam, GLYPH_CAPE_SOUTH,
                0x7E, 0x7E, 0x7E, 0x7E, 0x7E, 0x7E, 0x7E, 0x7E,
                0x7E, 0x7E, 0x7E, 0x3C, 0x3C, 0x18, 0x00, 0x00);

            // 3. 18x16 Two-Cell Broadcast Weather Icon Sprites
            WriteGlyph(fontRam, GLYPH_SUN_L,
                0x01, 0x21, 0x10, 0x07, 0x0F, 0x1F, 0x1F, 0xDF,
                0xDF, 0x1F, 0x1F, 0x0F, 0x07, 0x10, 0x21, 0x01);
            WriteGlyph(fontRam, GLYPH_SUN_R,
                0x80, 0x84, 0x08, 0xE0, 0xF0, 0xF8, 0xF8, 0xFB,
                0xFB, 0xF8, 0xF8, 0xF0, 0xE0, 0x08, 0x84, 0x80);

            WriteGlyph(fontRam, GLYPH_PARTCLOUD_L,
                0x02, 0x22, 0x10, 0x0F, 0x1F, 0x3F, 0xFF, 0x3F,
                0x3F, 0x1F, 0x0F, 0x00, 0x00, 0x00, 0x00, 0x00);
            WriteGlyph(fontRam, GLYPH_PARTCLOUD_R,
                0x00, 0x00, 0x00, 0x1C, 0x3E, 0x7F, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFF, 0xFE, 0x7C, 0x00, 0x00, 0x00);

            WriteGlyph(fontRam, GLYPH_CLOUD_L,
                0x00, 0x00, 0x00, 0x03, 0x07, 0x1F, 0x3F, 0x7F,
                0xFF, 0xFF, 0xFF, 0x7F, 0x3F, 0x00, 0x00, 0x00);
            WriteGlyph(fontRam, GLYPH_CLOUD_R,
                0x00, 0x00, 0x38, 0x7C, 0xFE, 0xFF, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFF, 0xFE, 0xFC, 0x00, 0x00, 0x00);

            WriteGlyph(fontRam, GLYPH_RAIN_L,
                0x00, 0x03, 0x07, 0x1F, 0x3F, 0x7F, 0xFF, 0xFF,
                0x7F, 0x00, 0x11, 0x22, 0x44, 0x11, 0x22, 0x00);
            WriteGlyph(fontRam, GLYPH_RAIN_R,
                0x1C, 0x3E, 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE,
                0xFC, 0x00, 0x44, 0x88, 0x10, 0x44, 0x88, 0x00);

            WriteGlyph(fontRam, GLYPH_STORM_L,
                0x00, 0x03, 0x07, 0x1F, 0x3F, 0x7F, 0xFF, 0xFF,
                0x7F, 0x00, 0x08, 0x10, 0x20, 0x00, 0x00, 0x00);
            WriteGlyph(fontRam, GLYPH_STORM_R,
                0x1C, 0x3E, 0x7F, 0xFF, 0xFF, 0xFE, 0x1C, 0x38,
                0x70, 0x3E, 0x1C, 0x38, 0x70, 0x60, 0xC0, 0x00);

            WriteGlyph(fontRam, GLYPH_SNOW_L,
                0x00, 0x03, 0x07, 0x1F, 0x3F, 0x7F, 0xFF, 0xFF,
                0x7F, 0x00, 0x2A, 0x14, 0x2A, 0x00, 0x00, 0x00);
            WriteGlyph(fontRam, GLYPH_SNOW_R,
                0x1C, 0x3E, 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE,
                0xFC, 0x00, 0x54, 0x28, 0x54, 0x00, 0x00, 0x00);
        }

        /// <summary>
        /// Complete 16-case coastal topology resolver. Returns true and selects the exact 8x16
        /// coastal bevel, peninsula, cape, or island glyph whenever a land cell touches ocean on any side.
        /// </summary>
        public static bool TryGetCoastalGlyph(bool upLand, bool downLand, bool leftLand, bool rightLand, out byte coastGlyph)
        {
            if (upLand && downLand && leftLand && rightLand)
            {
                coastGlyph = (byte)' ';
                return false;
            }

            if (!upLand && !downLand && !leftLand && !rightLand)
            {
                coastGlyph = GLYPH_ISLAND;
                return true;
            }

            // 1-Neighbor Cape / Peninsula Tips
            if (!upLand && downLand && !leftLand && !rightLand)
            {
                coastGlyph = GLYPH_CAPE_NORTH;
                return true;
            }
            if (upLand && !downLand && !leftLand && !rightLand)
            {
                coastGlyph = GLYPH_CAPE_SOUTH;
                return true;
            }
            if (!upLand && !downLand && !leftLand && rightLand)
            {
                coastGlyph = GLYPH_COAST_WEST;
                return true;
            }
            if (!upLand && !downLand && leftLand && !rightLand)
            {
                coastGlyph = GLYPH_COAST_EAST;
                return true;
            }

            // 2-Neighbor Narrow Peninsulas / Isthmuses
            if (upLand && downLand && !leftLand && !rightLand)
            {
                coastGlyph = GLYPH_PENINSULA_VERT;
                return true;
            }
            if (!upLand && !downLand && leftLand && rightLand)
            {
                coastGlyph = GLYPH_PENINSULA_HORIZ;
                return true;
            }

            // 2-Neighbor Diagonal Coastal Slopes
            if (!upLand && !leftLand && downLand && rightLand)
            {
                coastGlyph = GLYPH_COAST_NW;
                return true;
            }
            if (!upLand && !rightLand && downLand && leftLand)
            {
                coastGlyph = GLYPH_COAST_NE;
                return true;
            }
            if (!downLand && !leftLand && upLand && rightLand)
            {
                coastGlyph = GLYPH_COAST_SW;
                return true;
            }
            if (!downLand && !rightLand && upLand && leftLand)
            {
                coastGlyph = GLYPH_COAST_SE;
                return true;
            }

            // 3-Neighbor Straight Cardinal Coastlines
            if (!upLand && downLand && leftLand && rightLand)
            {
                coastGlyph = GLYPH_COAST_NORTH;
                return true;
            }
            if (!downLand && upLand && leftLand && rightLand)
            {
                coastGlyph = GLYPH_COAST_SOUTH;
                return true;
            }
            if (!leftLand && rightLand && upLand && downLand)
            {
                coastGlyph = GLYPH_COAST_WEST;
                return true;
            }

            coastGlyph = GLYPH_COAST_EAST;
            return true;
        }

        // =====================================================================
        // REAL-TIME ZERO-ALLOCATION ANIMATION ENGINE (FOR LAYER 5: WEATHER RADAR)
        // =====================================================================

        /// <summary>
        /// Called every 15ms from TuiDesktop's non-blocking wait loop when WorldWeatherMapApp is active
        /// and the Main Commands modal is closed. Allocates 0 bytes on the managed heap.
        /// </summary>
        public void TickAnimation()
        {
            if (_currentLayer != 4 || _radarPaused) return;

            _animTickCounter++;
            if (_animTickCounter < 12) return; // 12 * 15ms = 180ms (~5.5 FPS smooth radar sweep)
            _animTickCounter = 0;

            _radarFrame = (_radarFrame + 1) & 15;
            RenderWeatherRadarFrameZeroAlloc();
        }

        /// <summary>
        /// Redraws only the animated Doppler Radar canvas, descriptive text station badges, and legend
        /// directly into 0xB8000 with ZERO string or object allocations.
        /// </summary>
        private void RenderWeatherRadarFrameZeroAlloc()
        {
            if (_zones == null || _currentZone < 0 || _currentZone >= _zones.Length) return;
            GlobalStation[] stations = _zones[_currentZone];
            string[] map = _continentMaps[_currentZone];
            if (stations == null || map == null) return;

            for (int my = 0; my < 22; my++)
            {
                int screenY = 1 + my;
                for (int mx = 0; mx < 80; mx++)
                {
                    WorldWeatherMapLayers.RenderDopplerRadarCell(
                        _currentZone, stations, map, mx, my, screenY, _radarFrame,
                        GLYPH_BORDER_HORIZ, GLYPH_BORDER_VERT, GLYPH_HIGHLAND);
                }
            }

            // Draw non-selected station text badges, then selected station badge on top (zero-alloc)
            for (int i = 0; i < stations.Length; i++)
            {
                if (i == _selectedStation) continue;
                WorldWeatherMapLayers.DrawRadarStationBadgeZeroAlloc(_currentZone, stations, i, false, _radarFrame);
            }
            if (_selectedStation >= 0 && _selectedStation < stations.Length)
            {
                WorldWeatherMapLayers.DrawRadarStationBadgeZeroAlloc(_currentZone, stations, _selectedStation, true, _radarFrame);
            }

            // Redraw the Row 22 Weather Radar Text Legend (zero-alloc)
            DrawRadarLegendZeroAlloc();
        }

        // =====================================================================
        // FULL-SCREEN 80x25 BROADCAST & METEOROLOGICAL LAYER RENDERER
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
            if (_currentLayer < 0 || _currentLayer >= LayerNames.Length) _currentLayer = 0;
            var stations = _zones[_currentZone];
            if (stations == null || stations.Length == 0) return;
            if (_selectedStation < 0 || _selectedStation >= stations.Length) _selectedStation = 0;

            WorldWeatherMapLayers.EnsureGridComputed(_currentZone, stations);

            // Row 0: Full-Screen World Weather Map Top Bar (80 columns)
            string topBar = " WORLD WEATHER MAP | SECTOR: [" + ZoneNames[_currentZone] + "] | LAYER: [" + LayerNames[_currentLayer] + "]";
            WriteFullRow(0, topBar, ConsoleColor.White, ConsoleColor.DarkCyan);

            // Rows 1..22: Full-Screen 80x22 Continental Map + Active Meteorological Layer Field
            DrawContinentMapFullScreen(stations);

            // Overlay Station Telemetry Markers / Sprites according to Active Layer
            DrawStationsFullScreen(stations);

            // Draw Layer Scale Legend Pill in Row 22 (Bottom-Right of Map Canvas)
            DrawActiveLayerLegend();

            // Rows 23..24: Bottom Broadcast Telemetry & Unified Main Commands Prompt
            DrawBottomBroadcastHud();

            // If Main Commands Popup is open, render Broadcast Modal Overlay in VGA memory
            if (desktop.IsCommandPopupOpen)
            {
                DrawBroadcastCommandsModal();
            }
        }

        private void SyncWithLiveReading(TuiDesktop desktop)
        {
            try
            {
                var cur = desktop.WeatherService.GetLatestReading();
                if (cur.Condition != null && !cur.Condition.Contains("Failure") && !cur.Condition.Contains("Offline") && !cur.Condition.Contains("Timeout") && !cur.Condition.Contains("Awaiting"))
                {
                    string locUpper = (cur.Location ?? "").ToUpper();
                    for (int z = 0; z < _zones.Length; z++)
                    {
                        for (int i = 0; i < _zones[z].Length; i++)
                        {
                            string stUpper = _zones[z][i].Name.ToUpper();
                            if (locUpper.StartsWith(stUpper) || (stUpper == "LISBON" && (locUpper.Contains("LISBOA") || locUpper.Contains("SETUBAL"))))
                            {
                                _zones[z][i].TempC = (int)cur.TemperatureCelsius;
                                _zones[z][i].Humidity = (int)cur.HumidityPercent;
                                _zones[z][i].WindKmh = (int)cur.WindSpeedKmh;
                                _zones[z][i].Pressure = (int)cur.PressureHpa;
                                _zones[z][i].Condition = cur.Condition;
                                _zones[z][i].IsLive = true;
                                WorldWeatherMapLayers.InvalidateGrid();
                                return;
                            }
                        }
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

        private void DrawContinentMapFullScreen(GlobalStation[] stations)
        {
            string[] map = _continentMaps[_currentZone];
            if (map == null) return;

            for (int my = 0; my < 22; my++)
            {
                int screenY = 1 + my;
                string line = (my < map.Length) ? map[my] : "";

                for (int mx = 0; mx < 80; mx++)
                {
                    // Layer 4 (5/5 Weather Radar) uses its own full-canvas NEXRAD Doppler sweep renderer
                    if (_currentLayer == 4)
                    {
                        WorldWeatherMapLayers.RenderDopplerRadarCell(
                            _currentZone, stations, map, mx, my, screenY, _radarFrame,
                            GLYPH_BORDER_HORIZ, GLYPH_BORDER_VERT, GLYPH_HIGHLAND);
                        continue;
                    }

                    char c = (mx < line.Length) ? line[mx] : ' ';

                    if (c == ' ')
                    {
                        if (_currentLayer == 1)
                        {
                            WorldWeatherMapLayers.RenderThermalCell(_currentZone, stations, mx, my, screenY, false);
                        }
                        else if (_currentLayer == 2)
                        {
                            WorldWeatherMapLayers.RenderPrecipCell(_currentZone, stations, mx, my, screenY, false);
                        }
                        else if (_currentLayer == 3)
                        {
                            WorldWeatherMapLayers.RenderWindCell(_currentZone, stations, mx, my, screenY, false);
                        }
                        else
                        {
                            PutVgaCell(mx, screenY, (byte)' ', ConsoleColor.DarkBlue, ConsoleColor.DarkBlue);
                        }
                        continue;
                    }

                    // Check orthogonal neighbors to bevel all outer coastlines, peninsulas, capes & islands
                    bool upLand    = IsLandCell(map, mx, my - 1);
                    bool downLand  = IsLandCell(map, mx, my + 1);
                    bool leftLand  = IsLandCell(map, mx - 1, my);
                    bool rightLand = IsLandCell(map, mx + 1, my);

                    byte coastGlyph;
                    if (TryGetCoastalGlyph(upLand, downLand, leftLand, rightLand, out coastGlyph))
                    {
                        ConsoleColor coastFg = (_currentLayer == 1)
                            ? WorldWeatherMapLayers.GetThermalCoastColor(_currentZone, stations, mx, my)
                            : ConsoleColor.DarkGreen;
                        PutVgaCell(mx, screenY, coastGlyph, coastFg, ConsoleColor.DarkBlue);
                        continue;
                    }

                    // Interior Continental Landmass: Render according to Active Meteorological Layer
                    if (_currentLayer == 0)
                    {
                        if (c == '^')
                            PutVgaCell(mx, screenY, GLYPH_HIGHLAND, ConsoleColor.Green, ConsoleColor.DarkGreen);
                        else if (c == '-')
                            PutVgaCell(mx, screenY, GLYPH_BORDER_HORIZ, ConsoleColor.Green, ConsoleColor.DarkGreen);
                        else if (c == '|')
                            PutVgaCell(mx, screenY, GLYPH_BORDER_VERT, ConsoleColor.Green, ConsoleColor.DarkGreen);
                        else
                            PutVgaCell(mx, screenY, (byte)' ', ConsoleColor.DarkGreen, ConsoleColor.DarkGreen);
                    }
                    else if (_currentLayer == 1)
                    {
                        WorldWeatherMapLayers.RenderThermalCell(_currentZone, stations, mx, my, screenY, true);
                    }
                    else if (_currentLayer == 2)
                    {
                        WorldWeatherMapLayers.RenderPrecipCell(_currentZone, stations, mx, my, screenY, true);
                    }
                    else if (_currentLayer == 3)
                    {
                        WorldWeatherMapLayers.RenderWindCell(_currentZone, stations, mx, my, screenY, true);
                    }
                }
            }
        }

        private void DrawStationsFullScreen(GlobalStation[] stations)
        {
            if (stations == null) return;

            for (int i = 0; i < stations.Length; i++)
            {
                if (i == _selectedStation) continue;
                DrawStationOverlay(stations, i, false);
            }

            if (_selectedStation >= 0 && _selectedStation < stations.Length)
            {
                DrawStationOverlay(stations, _selectedStation, true);
            }
        }

        private void DrawStationOverlay(GlobalStation[] stations, int stationIdx, bool isSelected)
        {
            GlobalStation st = stations[stationIdx];

            if (_currentLayer == 0)
            {
                DrawStationSpriteAndTemp(st, isSelected);
                return;
            }

            if (_currentLayer == 4)
            {
                WorldWeatherMapLayers.DrawRadarStationBadgeZeroAlloc(_currentZone, stations, stationIdx, isSelected, _radarFrame);
                return;
            }

            int x = st.MapX;
            int y = 1 + st.MapY;
            if (y < 1 || y > 22 || x < 0 || x >= 70) return;

            string badge;
            ConsoleColor fg;
            // High-contrast dark badge pill so station text is 100% readable over any dithered layer
            ConsoleColor bg = isSelected ? ConsoleColor.DarkCyan : ConsoleColor.Black;

            if (_currentLayer == 1)
            {
                // Thermal Layer: Show Descriptive Thermal Zone Text ("FREEZE", "COLD", "COOL", "MILD", "WARM", "HOT")
                if (st.TempC >= 21)      { badge = "HOT";    fg = isSelected ? ConsoleColor.Yellow : ConsoleColor.Red; }
                else if (st.TempC >= 17) { badge = "WARM";   fg = ConsoleColor.Yellow; }
                else if (st.TempC >= 14) { badge = "MILD";   fg = ConsoleColor.Yellow; }
                else if (st.TempC >= 11) { badge = "COOL";   fg = isSelected ? ConsoleColor.Yellow : ConsoleColor.Green; }
                else if (st.TempC >= 8)  { badge = "COLD";   fg = isSelected ? ConsoleColor.Yellow : ConsoleColor.Cyan; }
                else                     { badge = "FREEZE"; fg = isSelected ? ConsoleColor.Yellow : ConsoleColor.White; }
            }
            else if (_currentLayer == 2)
            {
                // Precip Radar Layer: Show Descriptive Precipitation Condition Text ("STORM", "RAIN", "OVERCAST", "CLOUDY", "CLEAR")
                int pScore = WorldWeatherMapLayers.GetStationPrecipScore(st);
                if (pScore == 4)      { badge = "STORM";    fg = ConsoleColor.Yellow; }
                else if (pScore == 3) { badge = "RAIN";     fg = isSelected ? ConsoleColor.Yellow : ConsoleColor.Cyan; }
                else if (pScore == 2) { badge = "OVERCAST"; fg = isSelected ? ConsoleColor.Yellow : ConsoleColor.White; }
                else if (pScore == 1) { badge = "CLOUDY";   fg = isSelected ? ConsoleColor.Yellow : ConsoleColor.Green; }
                else                  { badge = "CLEAR";    fg = isSelected ? ConsoleColor.Yellow : ConsoleColor.Green; }
            }
            else
            {
                // Wind Layer: Show Descriptive Wind Streamline Text (">>GALE", ">>STRONG", ">MODERATE", "~BREEZE", "-CALM")
                if (st.WindKmh >= 20)      { badge = ">>GALE";    fg = ConsoleColor.Yellow; }
                else if (st.WindKmh >= 15) { badge = ">>STRONG";  fg = ConsoleColor.Yellow; }
                else if (st.WindKmh >= 12) { badge = ">MODERATE"; fg = isSelected ? ConsoleColor.Yellow : ConsoleColor.Cyan; }
                else if (st.WindKmh >= 9)  { badge = "~BREEZE";   fg = isSelected ? ConsoleColor.Yellow : ConsoleColor.Green; }
                else                       { badge = "-CALM";     fg = isSelected ? ConsoleColor.Yellow : ConsoleColor.Green; }
            }

            if (isSelected)
            {
                badge = "[" + badge + "]";
            }

            DirectVgaWrite(x, y, badge, fg, bg);
        }

        private void DrawStationSpriteAndTemp(GlobalStation st, bool isSelected)
        {
            int x = st.MapX;
            int y = 1 + st.MapY;
            if (y < 1 || y > 22 || x < 0 || x >= 75) return;

            byte glyphL, glyphR;
            ConsoleColor colorL, colorR;
            GetWeatherSpritePair(st.Condition, out glyphL, out glyphR, out colorL, out colorR);

            ConsoleColor bg = isSelected ? ConsoleColor.DarkCyan : ConsoleColor.DarkGreen;
            ConsoleColor tempFg = isSelected ? ConsoleColor.Yellow : ConsoleColor.White;

            int curX = x;

            if (isSelected && curX > 0)
            {
                PutVgaCell(curX - 1, y, (byte)'[', ConsoleColor.Yellow, bg);
            }

            PutVgaCell(curX,     y, glyphL, colorL, bg);
            PutVgaCell(curX + 1, y, glyphR, colorR, bg);

            int nextX = WorldWeatherMapLayers.DirectVgaWriteInt(curX + 2, y, st.TempC, tempFg, bg);
            if (nextX < 79)
            {
                PutVgaCell(nextX, y, 0xF8, tempFg, bg);
            }

            if (isSelected && (nextX + 1) < 80)
            {
                PutVgaCell(nextX + 1, y, (byte)']', ConsoleColor.Yellow, bg);
            }
        }

        private void DrawActiveLayerLegend()
        {
            if (_currentLayer == 0)
            {
                const string leg0 = " SYNOPTIC OVERVIEW: [Sun]Clear  [Part]Partly  [Cloud]Cloudy  [Rain]Showers ";
                int sx = 80 - leg0.Length;
                DirectVgaWrite(sx, 22, leg0, ConsoleColor.White, ConsoleColor.DarkCyan);
            }
            else if (_currentLayer == 1)
            {
                const string leg1 = " THERMAL HEATMAP: [ ]Freeze  [ ]Cold  [ ]Cool  [ ]Mild  [ ]Warm  [ ]Hot Core ";
                int sx = 80 - leg1.Length;
                DirectVgaWrite(sx, 22, leg1, ConsoleColor.White, ConsoleColor.DarkCyan);
                // Place real colored thermal band swatches inside the brackets
                PutVgaCell(sx + 19, 22, 0xB2, ConsoleColor.Cyan,   ConsoleColor.DarkBlue);
                PutVgaCell(sx + 30, 22, 0xB1, ConsoleColor.Cyan,   ConsoleColor.DarkCyan);
                PutVgaCell(sx + 39, 22, 0xB1, ConsoleColor.Green,  ConsoleColor.DarkGreen);
                PutVgaCell(sx + 48, 22, 0xB1, ConsoleColor.Yellow, ConsoleColor.DarkGreen);
                PutVgaCell(sx + 57, 22, 0xB2, ConsoleColor.Yellow, ConsoleColor.DarkYellow);
                PutVgaCell(sx + 66, 22, 0xB2, ConsoleColor.Yellow, ConsoleColor.DarkRed);
            }
            else if (_currentLayer == 2)
            {
                const string leg2 = " PRECIP RADAR: [ ]Clear  [ ]Cloudy  [ ]Overcast  [/]Rain Band  [*]Storm Core ";
                int sx = 80 - leg2.Length;
                DirectVgaWrite(sx, 22, leg2, ConsoleColor.White, ConsoleColor.DarkCyan);
                PutVgaCell(sx + 16, 22, (byte)' ', ConsoleColor.DarkGreen, ConsoleColor.DarkGreen);
                PutVgaCell(sx + 26, 22, 0xB0,      ConsoleColor.Green,     ConsoleColor.DarkGreen);
                PutVgaCell(sx + 37, 22, 0xB1,      ConsoleColor.White,     ConsoleColor.DarkGreen);
                PutVgaCell(sx + 50, 22, (byte)'/', ConsoleColor.Cyan,      ConsoleColor.DarkCyan);
                PutVgaCell(sx + 64, 22, (byte)'*', ConsoleColor.Yellow,    ConsoleColor.DarkBlue);
            }
            else if (_currentLayer == 3)
            {
                const string leg3 = " WIND VECTORS: [-]Calm  [~]Light Breeze  [>]Moderate Wind  [>>]Strong Gale ";
                int sx = 80 - leg3.Length;
                DirectVgaWrite(sx, 22, leg3, ConsoleColor.White, ConsoleColor.DarkCyan);
            }
            else
            {
                DrawRadarLegendZeroAlloc();
            }
        }

        private void DrawRadarLegendZeroAlloc()
        {
            const string leg4Live   = " WEATHER RADAR [LIVE]: [ ]Light  [ ]Moderate  [ ]Heavy  [ ]Severe  [ ]Storm ";
            const string leg4Paused = " WEATHER RADAR [HOLD]: [ ]Light  [ ]Moderate  [ ]Heavy  [ ]Severe  [ ]Storm ";
            string leg4 = _radarPaused ? leg4Paused : leg4Live;
            int sx = 80 - leg4.Length;
            DirectVgaWrite(sx, 22, leg4, ConsoleColor.White, ConsoleColor.DarkCyan);

            // Place live NEXRAD radar intensity swatches inside the legend brackets (zero heap allocations)
            PutVgaCell(sx + 24, 22, 0xB0, ConsoleColor.Cyan,    ConsoleColor.DarkBlue);
            PutVgaCell(sx + 34, 22, 0xB1, ConsoleColor.Green,   ConsoleColor.DarkGreen);
            PutVgaCell(sx + 47, 22, 0xB2, ConsoleColor.Yellow,  ConsoleColor.DarkYellow);
            PutVgaCell(sx + 57, 22, 0xB2, ConsoleColor.Red,     ConsoleColor.DarkYellow);
            PutVgaCell(sx + 68, 22, 0xDB, ConsoleColor.Magenta, ConsoleColor.DarkRed);
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
            string liveBadge = sel.IsLive ? "[LIVE]" : "[SYNOPTIC]";

            // Row 23: Complete Selected Station Telemetry Bar
            string row23;
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                row23 = " > " + sel.Name.ToUpper() + " (" + sel.Code + "): " + sel.TempC + "ºC " + sel.Condition + "  |  " + _statusMessage;
            }
            else
            {
                row23 = " > " + sel.Name.ToUpper() + " (" + sel.Country + ") | " +
                        sel.TempC + "ºC " + sel.Condition + " | Hum:" + sel.Humidity + "% Wind:" + sel.WindKmh + "km/h " + sel.Pressure + "hPa " + liveBadge;
            }
            WriteFullRow(23, row23, ConsoleColor.Yellow, ConsoleColor.DarkCyan);

            // Row 24: Scrolling Station Selector Ribbon + Unified Main Commands Prompt ONLY (no extra shortcuts in footer)
            string ribbon = " STATIONS: ";
            int total = stations.Length;
            int windowSize = 5;
            int startIdx = _selectedStation - (windowSize / 2);
            if (startIdx < 0) startIdx = 0;
            if (startIdx + windowSize > total) startIdx = Math.Max(0, total - windowSize);

            for (int i = startIdx; i < total && i < startIdx + windowSize; i++)
            {
                if (i == _selectedStation) ribbon += "[" + stations[i].Name + "] ";
                else ribbon += stations[i].Name + " ";
            }

            string rightHint = "  [F1] Main Commands  [ESC] Exit ";
            int maxLeft = 80 - rightHint.Length;
            if (ribbon.Length > maxLeft) ribbon = ribbon.Substring(0, maxLeft);
            else ribbon = ribbon.PadRight(maxLeft, ' ');

            WriteFullRow(24, ribbon + rightHint, ConsoleColor.White, ConsoleColor.Black);
        }

        private void DrawBroadcastCommandsModal()
        {
            int left = 6;
            int top = 3;
            int width = 68;
            int height = 18;

            // Fill modal background and border in VGA text memory
            for (int y = top; y < top + height; y++)
            {
                for (int x = left; x < left + width; x++)
                {
                    byte ch = (byte)' ';
                    ConsoleColor fg = ConsoleColor.White;
                    if (y == top || y == top + height - 1)
                    {
                        ch = (x == left || x == left + width - 1) ? (byte)'+' : (byte)'-';
                        fg = ConsoleColor.Cyan;
                    }
                    else if (x == left || x == left + width - 1)
                    {
                        ch = (byte)'|';
                        fg = ConsoleColor.Cyan;
                    }
                    PutVgaCell(x, y, ch, fg, ConsoleColor.Black);
                }
            }

            DirectVgaWrite(left + 3, top, " MAIN COMMANDS MENU  |  WORLD WEATHER MAP & LAYERS ", ConsoleColor.Yellow, ConsoleColor.Black);
            DirectVgaWrite(left + 3, top + 2, "METEOROLOGICAL MAP LAYER & TELEMETRY CONTROLS:", ConsoleColor.Cyan, ConsoleColor.Black);

            string[] lines = new string[]
            {
                "[L]                  Cycle Meteorological Map Layer (1..5)",
                "[1] Synoptic Icons   [2] Thermal Heatmap   [3] Precip Radar",
                "[4] Wind Vectors     [5] Live Doppler Weather Radar (Loop)",
                "[Space / P]          Pause / Resume Weather Radar Animation",
                "[Tab / C / Z]        Switch Continental Sector (5 Continents)",
                "[Arrows Left/Right]  Select Regional Weather Station on Map",
                "[R]                  Receive & Sync Live API for Selected City",
                "[A]                  Receive & Sync Live API for All Continent",
                "[Enter]              Open Selected Station in Weather Dashboard",
                "[ESC]                Exit Full-Screen Map to Desktop Launcher"
            };

            for (int i = 0; i < lines.Length; i++)
            {
                DirectVgaWrite(left + 3, top + 4 + i, lines[i], ConsoleColor.White, ConsoleColor.Black);
            }

            DirectVgaWrite(left + 3, top + height - 2, "[F1 / H / M / Enter / ESC] Close Commands Menu", ConsoleColor.Yellow, ConsoleColor.Black);
        }

        public static unsafe void PutVgaCell(int x, int y, byte glyph, ConsoleColor fg, ConsoleColor bg)
        {
            if (x < 0 || x >= 80 || y < 0 || y >= 25) return;
            byte* vga = (byte*)0xB8000;
            int offset = (y * 80 + x) * 2;
            vga[offset] = glyph;
            vga[offset + 1] = (byte)((((byte)bg & 0x07) << 4) | ((byte)fg & 0x0F));
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
            _statusMessage = "Receiving live telemetry for " + sel.Name + "...";
            Draw(desktop);

            try
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
                    WorldWeatherMapLayers.InvalidateGrid();
                    _statusMessage = "Telemetry received: " + sel.Name + " (" + t + "ºC, " + cond + ") - Layer redrawn";
                }
                else
                {
                    desktop.WeatherService.FetchOpenWeather(sel.Name, desktop.ApiKey);
                    var live = desktop.WeatherService.GetLatestReading();
                    if (live.Condition != null && !live.Condition.Contains("Failure") && !live.Condition.Contains("Timeout") && !live.Condition.Contains("Offline"))
                    {
                        stations[_selectedStation].TempC = (int)live.TemperatureCelsius;
                        stations[_selectedStation].Humidity = (int)live.HumidityPercent;
                        stations[_selectedStation].WindKmh = (int)live.WindSpeedKmh;
                        stations[_selectedStation].Pressure = (int)live.PressureHpa;
                        stations[_selectedStation].Condition = live.Condition;
                        stations[_selectedStation].IsLive = true;
                        WorldWeatherMapLayers.InvalidateGrid();
                        _statusMessage = "Live API synced: " + sel.Name + " (" + stations[_selectedStation].TempC + "ºC)";
                    }
                    else
                    {
                        _statusMessage = "Synoptic baseline retained for " + sel.Name;
                    }
                }
            }
            catch (Exception ex)
            {
                _statusMessage = "Sync error: " + ex.Message;
            }
        }

        private void FetchAllContinentStations(TuiDesktop desktop)
        {
            var stations = _zones[_currentZone];
            if (stations == null || stations.Length == 0) return;

            int syncedCount = 0;
            for (int i = 0; i < stations.Length; i++)
            {
                _statusMessage = "Receiving telemetry [" + (i + 1) + "/" + stations.Length + "]: " + stations[i].Name + "...";
                Draw(desktop);

                int t, h, w;
                string cond;
                if (desktop.WeatherService.FetchQuickStationWeather(stations[i].Lat, stations[i].Lon, out t, out h, out w, out cond))
                {
                    stations[i].TempC = t;
                    stations[i].Humidity = h;
                    stations[i].WindKmh = w;
                    stations[i].Condition = cond;
                    stations[i].IsLive = true;
                    syncedCount++;
                }
            }

            WorldWeatherMapLayers.InvalidateGrid();
            _statusMessage = "Sector telemetry updated (" + syncedCount + "/" + stations.Length + " live) - Layer redrawn";
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

            if (keyEvent.KeyChar == 'm' || keyEvent.KeyChar == 'M')
            {
                desktop.IsCommandPopupOpen = true;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Spacebar || keyEvent.KeyChar == 'p' || keyEvent.KeyChar == 'P')
            {
                if (_currentLayer == 4)
                {
                    _radarPaused = !_radarPaused;
                    _statusMessage = _radarPaused ? "Weather Radar Loop: PAUSED" : "Weather Radar Loop: LIVE";
                }
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
            else if (keyEvent.KeyChar == 'l' || keyEvent.KeyChar == 'L')
            {
                _currentLayer = (_currentLayer + 1) % LayerNames.Length;
                _statusMessage = "Active Layer: " + LayerNames[_currentLayer];
            }
            else if (keyEvent.KeyChar >= '1' && keyEvent.KeyChar <= '5')
            {
                _currentLayer = keyEvent.KeyChar - '1';
                _statusMessage = "Active Layer: " + LayerNames[_currentLayer];
            }
            else if (keyEvent.Key == ConsoleKeyEx.Tab || keyEvent.KeyChar == 'c' || keyEvent.KeyChar == 'C' || keyEvent.KeyChar == 'z' || keyEvent.KeyChar == 'Z')
            {
                _currentZone = (_currentZone + 1) % _zones.Length;
                _selectedStation = 0;
                WorldWeatherMapLayers.InvalidateGrid();
                _statusMessage = "Region: " + ZoneDescriptions[_currentZone];
            }
            else if (keyEvent.KeyChar == 'r' || keyEvent.KeyChar == 'R')
            {
                FetchLiveStation(desktop);
            }
            else if (keyEvent.KeyChar == 'a' || keyEvent.KeyChar == 'A')
            {
                FetchAllContinentStations(desktop);
            }
            else if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                var sel = stations[_selectedStation];
                _statusMessage = "Opening " + sel.Name + " in Weather Dashboard...";
                Draw(desktop);

                string dispLoc = sel.Name.ToUpper() + ", " + sel.Code;
                desktop.DefaultCity = sel.Name;
                desktop.WeatherService.ApplySynopticFallback(dispLoc, sel.TempC, sel.Humidity, sel.Pressure, sel.WindKmh, "WSW", sel.Condition);

                int t, h, w;
                string cond;
                if (desktop.WeatherService.FetchQuickStationWeather(sel.Lat, sel.Lon, out t, out h, out w, out cond))
                {
                    sel.TempC = t;
                    sel.Humidity = h;
                    sel.WindKmh = w;
                    sel.Condition = cond;
                    sel.IsLive = true;
                    WorldWeatherMapLayers.InvalidateGrid();
                    desktop.WeatherService.ApplySynopticFallback(dispLoc, t, h, sel.Pressure, w, "WSW", cond);
                }

                ExitFullScreenBroadcastMode();
                desktop.SetCurrentApp("weather");
            }
        }
    }
}
