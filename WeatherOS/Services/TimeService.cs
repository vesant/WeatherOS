using System;
using Cosmos.HAL;
using Cosmos.Core;

namespace WeatherOS.Services
{
    public static class TimeService
    {
        private static long _baseUtcEpochSeconds = 0;
        private static ulong _baseUptimeNs = 0;
        private static long _loopTickAccumulator = 0;
        private static int _loopMsAccumulator = 0;
        private static bool _isNetworkSynced = false;
        private static string _syncSource = "Uncalibrated Baseline";
        private static int _timeZoneOffset = 1; // Default UTC+1 (Western Europe)
        private static bool _is24HourFormat = true;
        private static bool _isInitialized = false;
        private static int _lastRtcSecond = -1;

        public static bool Is24HourFormat
        {
            get => _is24HourFormat;
            set => _is24HourFormat = value;
        }

        public static int TimezoneOffset
        {
            get => _timeZoneOffset;
            set
            {
                if (value >= -12 && value <= 14) _timeZoneOffset = value;
            }
        }

        public static bool IsNetworkSynced => _isNetworkSynced;
        public static string SyncSource => _syncSource;

        public static void Initialize()
        {
            if (_isInitialized) return;

            int hour = 20, minute = 0, second = 0, day = 17, month = 10, year = 2026;
            bool rtcPlausible = false;

            try
            {
                int rSec = Cosmos.HAL.RTC.Second;
                int rMin = Cosmos.HAL.RTC.Minute;
                int rHour = Cosmos.HAL.RTC.Hour;
                int rDay = Cosmos.HAL.RTC.DayOfTheMonth;
                int rMon = Cosmos.HAL.RTC.Month;
                int rYear = Cosmos.HAL.RTC.Year;

                if (rYear > 0 && rYear < 100) rYear += 2000;
                else if (rYear == 0) rYear = 2026;

                if (rMon >= 1 && rMon <= 12 && rDay >= 1 && rDay <= 31 &&
                    rHour >= 0 && rHour < 24 && rMin >= 0 && rMin < 60 && rSec >= 0 && rSec < 60)
                {
                    hour = rHour; minute = rMin; second = rSec;
                    day = rDay; month = rMon; year = rYear;
                    rtcPlausible = true;
                }
            }
            catch { }

            _baseUtcEpochSeconds = UtcToEpoch(year, month, day, hour, minute, second);
            try
            {
                _baseUptimeNs = Cosmos.Core.CPU.GetCPUUptime();
            }
            catch
            {
                _baseUptimeNs = 0;
            }

            _loopTickAccumulator = 0;
            _loopMsAccumulator = 0;
            _syncSource = rtcPlausible ? "CMOS RTC Hardware (Ports 0x70/0x71)" : "System Default Baseline";
            _isInitialized = true;
        }

        public static void Tick(int ms)
        {
            if (!_isInitialized) Initialize();

            if (ms > 0)
            {
                _loopMsAccumulator += ms;
                if (_loopMsAccumulator >= 1000)
                {
                    int secs = _loopMsAccumulator / 1000;
                    _loopMsAccumulator %= 1000;
                    _loopTickAccumulator += secs;
                }
            }

            // Check if hardware RTC is actively advancing (for systems with intact CMOS battery)
            if (!_isNetworkSynced)
            {
                try
                {
                    int curSec = Cosmos.HAL.RTC.Second;
                    if (_lastRtcSecond >= 0 && curSec != _lastRtcSecond)
                    {
                        // Hardware clock is ticking! Update baseline from RTC
                        int rMin = Cosmos.HAL.RTC.Minute;
                        int rHour = Cosmos.HAL.RTC.Hour;
                        int rDay = Cosmos.HAL.RTC.DayOfTheMonth;
                        int rMon = Cosmos.HAL.RTC.Month;
                        int rYear = Cosmos.HAL.RTC.Year;
                        if (rYear > 0 && rYear < 100) rYear += 2000;
                        else if (rYear == 0) rYear = 2026;

                        if (rMon >= 1 && rMon <= 12 && rDay >= 1 && rDay <= 31 &&
                            rHour >= 0 && rHour < 24 && rMin >= 0 && rMin < 60 && curSec >= 0 && curSec < 60)
                        {
                            _baseUtcEpochSeconds = UtcToEpoch(rYear, rMon, rDay, rHour, rMin, curSec);
                            try { _baseUptimeNs = Cosmos.Core.CPU.GetCPUUptime(); } catch { }
                            _loopTickAccumulator = 0;
                            _syncSource = "CMOS RTC Hardware (Active)";
                        }
                    }
                    _lastRtcSecond = curSec;
                }
                catch { }
            }
        }

        public static bool SyncFromNtp(byte[] ntpServerIp = null)
        {
            if (!_isInitialized) Initialize();

            uint ntpEpoch = 0;
            bool ok = RawTcpHttp.QueryNtp(ntpServerIp, out ntpEpoch);
            if (ok && ntpEpoch > 1700000000u)
            {
                _baseUtcEpochSeconds = (long)ntpEpoch;
                try { _baseUptimeNs = Cosmos.Core.CPU.GetCPUUptime(); } catch { _baseUptimeNs = 0; }
                _loopTickAccumulator = 0;
                _loopMsAccumulator = 0;
                _isNetworkSynced = true;
                string sIp = (ntpServerIp != null) ? $"{ntpServerIp[0]}.{ntpServerIp[1]}.{ntpServerIp[2]}.{ntpServerIp[3]}" : "pool.ntp.org";
                _syncSource = $"NTP Atomic Clock ({sIp})";
                return true;
            }

            return false;
        }

        public static bool SyncFromHttpDate(string httpDateHeader)
        {
            if (!_isInitialized) Initialize();

            long parsedEpoch = 0;
            if (TryParseHttpDate(httpDateHeader, out parsedEpoch))
            {
                _baseUtcEpochSeconds = parsedEpoch;
                try { _baseUptimeNs = Cosmos.Core.CPU.GetCPUUptime(); } catch { _baseUptimeNs = 0; }
                _loopTickAccumulator = 0;
                _loopMsAccumulator = 0;
                _isNetworkSynced = true;
                _syncSource = "HTTP Gateway Telemetry (Network Synced)";
                return true;
            }

            return false;
        }

        public static void SetManualTime(int year, int month, int day, int hour, int minute, int second)
        {
            if (!_isInitialized) Initialize();

            // Adjust inputs to UTC using the current timezone offset
            int utcHour = hour - _timeZoneOffset;
            int utcDay = day;
            int utcMonth = month;
            int utcYear = year;

            if (utcHour < 0)
            {
                utcHour += 24;
                utcDay--;
                if (utcDay < 1)
                {
                    utcMonth--;
                    if (utcMonth < 1) { utcMonth = 12; utcYear--; }
                    utcDay = GetDaysInMonth(utcYear, utcMonth);
                }
            }
            else if (utcHour >= 24)
            {
                utcHour -= 24;
                utcDay++;
                if (utcDay > GetDaysInMonth(utcYear, utcMonth))
                {
                    utcDay = 1;
                    utcMonth++;
                    if (utcMonth > 12) { utcMonth = 1; utcYear++; }
                }
            }

            _baseUtcEpochSeconds = UtcToEpoch(utcYear, utcMonth, utcDay, utcHour, minute, second);
            try { _baseUptimeNs = Cosmos.Core.CPU.GetCPUUptime(); } catch { _baseUptimeNs = 0; }
            _loopTickAccumulator = 0;
            _loopMsAccumulator = 0;
            _syncSource = "Manual User Calibration";
        }

        public static void AdjustHours(int deltaHours)
        {
            if (!_isInitialized) Initialize();
            _baseUtcEpochSeconds += (deltaHours * 3600L);
            _syncSource = "Manual Adjustment (Hours)";
        }

        public static void AdjustMinutes(int deltaMinutes)
        {
            if (!_isInitialized) Initialize();
            _baseUtcEpochSeconds += (deltaMinutes * 60L);
            _syncSource = "Manual Adjustment (Minutes)";
        }

        public static long GetCurrentUtcEpochSeconds()
        {
            if (!_isInitialized) Initialize();

            long elapsedSec = 0;
            ulong curUptime = 0;
            try { curUptime = Cosmos.Core.CPU.GetCPUUptime(); } catch { }

            if (_baseUptimeNs > 0 && curUptime > _baseUptimeNs)
            {
                elapsedSec = (long)((curUptime - _baseUptimeNs) / 1000000000UL);
            }
            else
            {
                elapsedSec = _loopTickAccumulator;
            }

            return _baseUtcEpochSeconds + elapsedSec;
        }

        public static void GetCurrentUtcTime(out int year, out int month, out int day, out int hour, out int minute, out int second)
        {
            long epoch = GetCurrentUtcEpochSeconds();
            EpochToUtc(epoch, out year, out month, out day, out hour, out minute, out second);
        }

        public static void GetCurrentLocalTime(out int year, out int month, out int day, out int hour, out int minute, out int second)
        {
            long epoch = GetCurrentUtcEpochSeconds() + (_timeZoneOffset * 3600L);
            EpochToUtc(epoch, out year, out month, out day, out hour, out minute, out second);
        }

        public static string GetFormattedTime(bool includeSeconds = true)
        {
            int y, m, d, h, min, s;
            GetCurrentLocalTime(out y, out m, out d, out h, out min, out s);

            string amPm = "";
            int displayHour = h;
            if (!_is24HourFormat)
            {
                amPm = (h >= 12) ? " PM" : " AM";
                displayHour = h % 12;
                if (displayHour == 0) displayHour = 12;
            }

            string hStr = displayHour.ToString().PadLeft(2, '0');
            string mStr = min.ToString().PadLeft(2, '0');

            if (includeSeconds)
            {
                string sStr = s.ToString().PadLeft(2, '0');
                return $"{hStr}:{mStr}:{sStr}{amPm}";
            }

            return $"{hStr}:{mStr}{amPm}";
        }

        public static string GetFormattedDate()
        {
            int y, m, d, h, min, s;
            GetCurrentLocalTime(out y, out m, out d, out h, out min, out s);
            string dayOfWeek = GetDayOfWeekName(y, m, d);
            string monthName = GetMonthName(m);
            return $"{dayOfWeek}, {d} {monthName} {y}";
        }

        public static string GetDayOfWeekName(int year, int month, int day)
        {
            long epoch = UtcToEpoch(year, month, day, 12, 0, 0);
            long daysSince1970 = epoch / 86400L;
            // 1970-01-01 was a Thursday (index 4)
            int dow = (int)((daysSince1970 + 4) % 7);
            if (dow < 0) dow += 7;

            switch (dow)
            {
                case 0: return "Sunday";
                case 1: return "Monday";
                case 2: return "Tuesday";
                case 3: return "Wednesday";
                case 4: return "Thursday";
                case 5: return "Friday";
                case 6: return "Saturday";
                default: return "Sunday";
            }
        }

        public static string GetMonthName(int month)
        {
            switch (month)
            {
                case 1: return "January";
                case 2: return "February";
                case 3: return "March";
                case 4: return "April";
                case 5: return "May";
                case 6: return "June";
                case 7: return "July";
                case 8: return "August";
                case 9: return "September";
                case 10: return "October";
                case 11: return "November";
                case 12: return "December";
                default: return "October";
            }
        }

        public static int GetDaysInMonth(int year, int month)
        {
            bool isLeap = (year % 4 == 0 && (year % 100 != 0 || year % 400 == 0));
            int[] mDays = { 31, isLeap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
            if (month >= 1 && month <= 12) return mDays[month - 1];
            return 31;
        }

        private static void EpochToUtc(long epoch, out int year, out int month, out int day, out int hour, out int minute, out int second)
        {
            if (epoch < 0) epoch = 0;

            second = (int)(epoch % 60); epoch /= 60;
            minute = (int)(epoch % 60); epoch /= 60;
            hour = (int)(epoch % 24); long days = epoch / 24;

            year = 1970;
            while (true)
            {
                bool leap = (year % 4 == 0 && (year % 100 != 0 || year % 400 == 0));
                long daysInYear = leap ? 366 : 365;
                if (days < daysInYear) break;
                days -= daysInYear;
                year++;
            }

            bool isLeap = (year % 4 == 0 && (year % 100 != 0 || year % 400 == 0));
            int[] mDays = { 31, isLeap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
            month = 1;
            for (int i = 0; i < 12; i++)
            {
                if (days < mDays[i]) { month = i + 1; break; }
                days -= mDays[i];
            }
            day = (int)days + 1;
        }

        public static long UtcToEpoch(int year, int month, int day, int hour, int minute, int second)
        {
            if (year < 1970) year = 1970;
            if (month < 1) month = 1; else if (month > 12) month = 12;
            if (day < 1) day = 1;
            if (hour < 0) hour = 0; else if (hour > 23) hour = 23;
            if (minute < 0) minute = 0; else if (minute > 59) minute = 59;
            if (second < 0) second = 0; else if (second > 59) second = 59;

            long days = 0;
            for (int y = 1970; y < year; y++)
            {
                bool leap = (y % 4 == 0 && (y % 100 != 0 || y % 400 == 0));
                days += leap ? 366 : 365;
            }

            bool isLeap = (year % 4 == 0 && (year % 100 != 0 || year % 400 == 0));
            int[] mDays = { 31, isLeap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
            for (int m = 1; m < month; m++)
            {
                days += mDays[m - 1];
            }
            days += (day - 1);

            return days * 86400L + hour * 3600L + minute * 60L + second;
        }

        public static bool TryParseHttpDate(string header, out long epoch)
        {
            epoch = 0;
            if (string.IsNullOrEmpty(header)) return false;

            string s = header.Trim();
            if (s.StartsWith("Date:", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(5).Trim();
            }

            int commaIdx = s.IndexOf(',');
            if (commaIdx != -1) s = s.Substring(commaIdx + 1).Trim();

            string[] parts = s.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return false;

            int day, year;
            if (!int.TryParse(parts[0], out day)) return false;

            int month = GetMonthFromShortName(parts[1]);
            if (month == 0) return false;

            if (!int.TryParse(parts[2], out year)) return false;

            string[] timeParts = parts[3].Split(':');
            if (timeParts.Length < 3) return false;

            int h, m, sec;
            if (!int.TryParse(timeParts[0], out h)) return false;
            if (!int.TryParse(timeParts[1], out m)) return false;
            if (!int.TryParse(timeParts[2], out sec)) return false;

            epoch = UtcToEpoch(year, month, day, h, m, sec);
            return true;
        }

        private static int GetMonthFromShortName(string m)
        {
            if (string.IsNullOrEmpty(m)) return 0;
            string lower = m.ToLower();
            if (lower.StartsWith("jan")) return 1;
            if (lower.StartsWith("feb")) return 2;
            if (lower.StartsWith("mar")) return 3;
            if (lower.StartsWith("apr")) return 4;
            if (lower.StartsWith("may")) return 5;
            if (lower.StartsWith("jun")) return 6;
            if (lower.StartsWith("jul")) return 7;
            if (lower.StartsWith("aug")) return 8;
            if (lower.StartsWith("sep")) return 9;
            if (lower.StartsWith("oct")) return 10;
            if (lower.StartsWith("nov")) return 11;
            if (lower.StartsWith("dec")) return 12;
            return 0;
        }
    }
}
