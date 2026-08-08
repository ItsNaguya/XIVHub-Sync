using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace XIVHubCompanion
{
    public static class WeatherPredictor
    {
        private const double EORZEA_MULTIPLIER = 144.0 / 7.0;
        private const int WEATHER_CHANGE_EORZEA_HOURS = 8;
        
        public static DateTime GetEorzeaTime(DateTime realTime)
        {
            long localEpoch = new DateTimeOffset(realTime).ToUnixTimeMilliseconds();
            long eorzeaEpoch = (long)(localEpoch * EORZEA_MULTIPLIER);
            return DateTimeOffset.FromUnixTimeMilliseconds(eorzeaEpoch).UtcDateTime;
        }

        public static DateTime GetRealTime(DateTime eorzeaTime)
        {
            long eorzeaEpoch = new DateTimeOffset(eorzeaTime).ToUnixTimeMilliseconds();
            long localEpoch = (long)(eorzeaEpoch / EORZEA_MULTIPLIER);
            return DateTimeOffset.FromUnixTimeMilliseconds(localEpoch).UtcDateTime;
        }

        /// <summary>
        /// Gets the real time of the start of the current weather window.
        /// Weather changes every 8 Eorzea hours (00:00, 08:00, 16:00).
        /// </summary>
        public static DateTime GetCurrentWeatherStartTime(DateTime realTime)
        {
            var eTime = GetEorzeaTime(realTime);
            int blockHour = (eTime.Hour / WEATHER_CHANGE_EORZEA_HOURS) * WEATHER_CHANGE_EORZEA_HOURS;
            var startETime = new DateTime(eTime.Year, eTime.Month, eTime.Day, blockHour, 0, 0, DateTimeKind.Utc);
            return GetRealTime(startETime);
        }

        /// <summary>
        /// Standard FFXIV weather target calculation.
        /// </summary>
        public static int CalculateTarget(DateTime realTime)
        {
            long unixSeconds = new DateTimeOffset(realTime).ToUnixTimeSeconds();
            long eorzeaHours = (long)(unixSeconds / 175.0); // 175 real seconds = 1 Eorzea hour
            
            // FFXIV shifts the hour by +8 to align the weather blocks (00:00, 08:00, 16:00)
            long shiftedHour = (long)(eorzeaHours + 8 - (eorzeaHours % 8)) % 24;
            
            long eorzeaDays = (long)(unixSeconds / 4200.0); // 4200 real seconds = 1 Eorzea day
            
            uint calcBase = (uint)(eorzeaDays * 100 + shiftedHour);
            uint step1 = (calcBase << 11) ^ calcBase;
            uint step2 = (step1 >> 8) ^ step1;
            
            return (int)(step2 % 100);
        }

        /// <summary>
        /// Retrieves the weather ID for a given territory and real time.
        /// </summary>
        public static uint GetWeather(TerritoryType territory, DateTime realTime)
        {
            if (territory.WeatherRate.RowId == 0) return 0;
            var weatherRate = territory.WeatherRate.Value;
            
            int target = CalculateTarget(realTime);
            int cumulative = 0;
            
            for (int i = 0; i < weatherRate.Rate.Count; i++)
            {
                if (weatherRate.Rate[i] > 0)
                {
                    cumulative += weatherRate.Rate[i];
                    if (target < cumulative)
                    {
                        return weatherRate.Weather[i].RowId;
                    }
                }
            }
            
            return 0; // Default or fallback
        }

        public static (DateTime start, DateTime end)? GetNextUptime(TerritoryType territory, List<int> time, List<int> weathers, List<int> prevWeathers, DateTime fromRealTime, int maxDaysToCheck = 14)
        {
            bool checkTime = time != null && time.Count == 2;
            bool checkWeather = weathers != null && weathers.Count > 0;
            bool checkPrevWeather = prevWeathers != null && prevWeathers.Count > 0;

            if (!checkTime && !checkWeather) return null; // Always up

            DateTime currentCheckRealTime = fromRealTime;
            DateTime endCheckTime = fromRealTime.AddDays(maxDaysToCheck);

            while (currentCheckRealTime < endCheckTime)
            {
                DateTime currentEorzeaTime = GetEorzeaTime(currentCheckRealTime);
                
                bool isWeatherValid = true;
                if (checkWeather || checkPrevWeather)
                {
                    uint currentWeather = GetWeather(territory, currentCheckRealTime);
                    uint previousWeather = GetWeather(territory, currentCheckRealTime.AddMinutes(-23.333)); // Step back 1 Eorzea block

                    if (checkWeather && !weathers.Contains((int)currentWeather))
                        isWeatherValid = false;

                    if (checkPrevWeather && !prevWeathers.Contains((int)previousWeather))
                        isWeatherValid = false;
                }

                if (isWeatherValid)
                {
                    if (checkTime)
                    {
                        int startMinute = time[0];
                        int endMinute = time[1];
                        int currentMinuteOfDay = currentEorzeaTime.Hour * 60 + currentEorzeaTime.Minute;

                        if (startMinute < endMinute)
                        {
                            if (currentMinuteOfDay >= startMinute && currentMinuteOfDay < endMinute)
                            {
                                // Active now
                                var endETime = new DateTime(currentEorzeaTime.Year, currentEorzeaTime.Month, currentEorzeaTime.Day, 0, 0, 0, DateTimeKind.Utc).AddMinutes(endMinute);
                                return (currentCheckRealTime, GetRealTime(endETime));
                            }
                        }
                        else
                        {
                            // Spans midnight
                            if (currentMinuteOfDay >= startMinute || currentMinuteOfDay < endMinute)
                            {
                                // Active now
                                var endETime = new DateTime(currentEorzeaTime.Year, currentEorzeaTime.Month, currentEorzeaTime.Day, 0, 0, 0, DateTimeKind.Utc).AddMinutes(endMinute);
                                if (currentMinuteOfDay >= startMinute) endETime = endETime.AddDays(1);
                                return (currentCheckRealTime, GetRealTime(endETime));
                            }
                        }
                    }
                    else
                    {
                        // Weather matches, no time requirement
                        // active until next weather change
                        DateTime weatherStartRealTime = GetCurrentWeatherStartTime(currentCheckRealTime);
                        DateTime weatherEndRealTime = weatherStartRealTime.AddSeconds(1400); // 8 Eorzea hours = 1400 real seconds
                        return (currentCheckRealTime, weatherEndRealTime);
                    }
                }

                // Advance time safely
                if (checkTime && (!checkWeather && !checkPrevWeather))
                {
                    // If only checking time, advance by 1 Eorzea Hour to speed up
                    currentCheckRealTime = currentCheckRealTime.AddSeconds(175);
                }
                else
                {
                    // Weather requires stepping by smaller blocks or stepping to next weather block
                    // Wait, we need to find exactly when the next block starts, or step by Eorzea Hour
                    // Eorzea Hour stepping is safest (175 real seconds)
                    currentCheckRealTime = currentCheckRealTime.AddSeconds(175); 
                }
            }

            return null;
        }
    }
}
