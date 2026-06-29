// TimeApi — wraps Director (the day/night/season clock).

using System;
using KingdomMod.Internal;

namespace KingdomMod
{
    /// <summary>Day/night cycle, seasons, and clock-speed manipulation.</summary>
    public sealed class TimeApi
    {
        internal static TimeApi Instance { get; } = new TimeApi();
        private TimeApi() { }

        // ---- Events forwarded from Director (instance events) ------------------

        /// <summary>Fired at midnight when the in-game day rolls over.</summary>
        public event Action OnDayChanged
        {
            add    { var d = GameRefs.Director; if (d != null) d.OnDayFlip += value; }
            remove { var d = GameRefs.Director; if (d != null) d.OnDayFlip -= value; }
        }

        /// <summary>Fired when the time of day moves to a new phase (Dawn/Day/Dusk/Night/etc).</summary>
        public event Action<Il2Cpp.DayPhase> OnDayPhaseChanged
        {
            add    { var d = GameRefs.Director; if (d != null) d.OnDayPhaseChange += value; }
            remove { var d = GameRefs.Director; if (d != null) d.OnDayPhaseChange -= value; }
        }

        /// <summary>Fired when the season changes (Spring/Summer/Autumn/Winter).</summary>
        public event Action<Il2Cpp.Season> OnSeasonChanged
        {
            add    { var d = GameRefs.Director; if (d != null) d.OnSeasonChange += value; }
            remove { var d = GameRefs.Director; if (d != null) d.OnSeasonChange -= value; }
        }

        /// <summary>Fired the moment winter ends.</summary>
        public event Action OnWinterEnd
        {
            add    { var d = GameRefs.Director; if (d != null) d.OnWinterEnd += value; }
            remove { var d = GameRefs.Director; if (d != null) d.OnWinterEnd -= value; }
        }

        // ---- Queries -----------------------------------------------------------

        /// <summary>Total days the current monarch has reigned across all islands.</summary>
        public int  DaysInReign         => GameRefs.Director?.TotalDaysInReign ?? 0;
        /// <summary>Days the monarch has been on the current island.</summary>
        public int  IslandDays          => GameRefs.Director?.CurrentIslandDays ?? 0;
        /// <summary>True when the in-game clock is past sunset and before sunrise.</summary>
        public bool IsNight             => GameRefs.Director?.IsNight ?? false;
        /// <summary>True when the in-game clock is in the daytime window.</summary>
        public bool IsDay               => GameRefs.Director?.IsDaytime ?? false;
        /// <summary>True while time is frozen (cinematics, menus, pause).</summary>
        public bool IsTimePaused        => GameRefs.Director?.IsTimePaused ?? false;
        /// <summary>Current season (Spring/Summer/Autumn/Winter).</summary>
        public Il2Cpp.Season CurrentSeason => GameRefs.Director?.CurrentSeason ?? default;

        // ---- Manipulation ------------------------------------------------------

        /// <summary>
        /// Multiplier on the in-game clock.  1.0 = normal.  Useful for fast-forward
        /// mods or for slowing nights down.  Set to 0 to freeze time.
        /// </summary>
        public float ClockSpeedMultiplier
        {
            get => GameRefs.Director?.ClockSpeedModifier ?? 1f;
            set { var d = GameRefs.Director; if (d != null) d.ClockSpeedModifier = value; }
        }

        /// <summary>Real-world seconds for one in-game hour. Lower = faster days.</summary>
        public float SecondsPerInGameHour
        {
            get => GameRefs.Director?.secondsPerInGameHour ?? 0f;
            set { var d = GameRefs.Director; if (d != null) d.secondsPerInGameHour = value; }
        }
    }
}
