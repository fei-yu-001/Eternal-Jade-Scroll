using System;
using System.Globalization;

namespace TwelveJade.Core
{
    // 世界时间：一天 1440 分钟。开局固定在第 1 天 06:00——不读系统当前时间，
    // 于是同一份存档在任何机器、任何时刻读出来都是同一个时刻，测试也完全确定。
    // 存档里存的是"第几天 + 当天第几分钟"两个可校验的整数，不存时间戳。
    [Serializable]
    public sealed class WorldTime
    {
        public const int MinutesPerDay = 1440;
        public const int MaxDays = 3650;       // 十年封顶，脏档不许把日子推到 int 上限
        public const int StartDay = 1;
        public const int StartMinute = 360;    // 06:00 醒来

        public int day = StartDay;
        public int minuteOfDay = StartMinute;
    }

    // 世界时钟：只由调用方传入的增量推进，内部按小步走。
    // 为什么要小步：单帧掉一次 0.5 秒的帧，直接推进就会把货郎的整段夜间窗口一次跨过去——
    // 作息该开的时候不开、该关的时候不关。切成小步推进，窗口边界不会被一帧吞掉。
    public static class WorldClock
    {
        // 单步上限（秒）。每帧 0.016s 正常，掉到 0.5s 也只是多走几步，不会跳过窗口。
        public const double MaxStepSeconds = .1;
        // 加速上限：给测试与"快进一夜"用，别让一次调用把日程冲散。
        public const double MaxSpeed = 600.0;

        public static WorldTime NewGame() => new WorldTime();

        public static bool IsValid(WorldTime time) =>
            time != null && time.day >= WorldTime.StartDay && time.day <= WorldTime.MaxDays &&
            time.minuteOfDay >= 0 && time.minuteOfDay < WorldTime.MinutesPerDay;

        // 归一化：越界的日子与分钟拉回合法范围，迁移与脏档兜底共用。
        public static WorldTime Normalize(WorldTime time)
        {
            if (time == null) return NewGame();
            time.day = Math.Min(Math.Max(time.day, WorldTime.StartDay), WorldTime.MaxDays);
            time.minuteOfDay = ((time.minuteOfDay % WorldTime.MinutesPerDay) + WorldTime.MinutesPerDay)
                % WorldTime.MinutesPerDay;
            return time;
        }

        // 推进（秒）。speed <= 0 视为暂停：暂停、打开背包、读档都靠它。
        // 切成小步是为了"逐段经过"作息窗口（将来挂事件回调用），但落点必须一次算准——
        // 每步取整会把 0.0017 分钟一次次抹掉，走六十步时钟纹丝不动。步数另有上限：
        // 不设上限的话"快进一个月"会走出上千万步循环。
        public static void Advance(WorldTime time, double seconds, double speed = 1.0)
        {
            if (time == null || seconds <= 0 || speed <= 0) return;
            var totalMinutes = Math.Min(seconds * speed / 60.0, MaxAdvanceMinutes);
            var perStep = MaxStepSeconds * speed / 60.0; // 每步对应多少分钟
            var steps = perStep > 0 ? (int)Math.Ceiling(totalMinutes / perStep) : 1;
            steps = Math.Min(Math.Max(steps, 1), MaxSteps);
            var stepMinutes = totalMinutes / steps;
            for (var i = 0; i < steps; i++)
            {
                // 逐段经过：将来在这里按经过的分钟派发事件，现在只需要把总位移累加出来。
            }
            SetFromMinutes(time, time.minuteOfDay + stepMinutes * steps);
        }

        // 一次调用最多切多少步、最多推进多少分钟：防手滑把日程冲散。
        public const int MaxSteps = 4096;
        public const double MaxAdvanceMinutes = WorldTime.MinutesPerDay * 30.0;

        // 一次换算成"第几天 + 当天第几分钟"，只在末尾取整一次。
        static void SetFromMinutes(WorldTime time, double minutes)
        {
            var absolute = (time.day - 1) * WorldTime.MinutesPerDay + minutes;
            absolute = Math.Floor(absolute);
            if (absolute < 0) absolute = 0;
            var maxAbsolute = (double)WorldTime.MaxDays * WorldTime.MinutesPerDay;
            if (absolute > maxAbsolute) absolute = maxAbsolute;
            time.day = (int)(absolute / WorldTime.MinutesPerDay) + 1;
            time.minuteOfDay = (int)(absolute % WorldTime.MinutesPerDay);
        }

        // 直接跳到某个绝对分钟（快进、读档对表用）。
        public static void SkipTo(WorldTime time, int absoluteMinutes)
        {
            if (time == null) return;
            SetMinutes(time, absoluteMinutes);
        }

        public static int ToMinutes(WorldTime time) =>
            time == null ? 0 : (time.day - 1) * WorldTime.MinutesPerDay + time.minuteOfDay;

        public static void SetMinutes(WorldTime time, int absoluteMinutes)
        {
            if (time == null) return;
            var clamped = Math.Min(Math.Max(absoluteMinutes, 0), WorldTime.MaxDays * WorldTime.MinutesPerDay);
            time.day = clamped / WorldTime.MinutesPerDay + 1;
            time.minuteOfDay = clamped % WorldTime.MinutesPerDay;
        }

        public static bool IsNight(WorldTime time) => IsNight(time?.minuteOfDay ?? 0);

        // 夜里默认 19:00–05:00。作息窗口仍以日程表为准，这里只给"夜里"一个可读的判断。
        public const int NightStartMinute = 19 * 60;
        public const int NightEndMinute = 5 * 60;

        public static bool IsNight(int minuteOfDay)
        {
            var minute = ((minuteOfDay % WorldTime.MinutesPerDay) + WorldTime.MinutesPerDay) % WorldTime.MinutesPerDay;
            return minute >= NightStartMinute || minute < NightEndMinute;
        }

        public static string Format(WorldTime time)
        {
            if (time == null) return "——";
            return "第 " + time.day + " 天 " +
                (time.minuteOfDay / 60).ToString("00") + ":" + (time.minuteOfDay % 60).ToString("00");
        }

        // 存档与时钟之间的搬运：存档存两个扁平整数，时钟用结构体——两边都不将就。
        public static WorldTime ReadFrom(int day, int minuteOfDay) => Normalize(new WorldTime { day = day, minuteOfDay = minuteOfDay });

        public static void WriteTo(WorldTime time, Action<int, int> write)
        {
            var normalized = Normalize(time);
            write?.Invoke(normalized.day, normalized.minuteOfDay);
        }

        // 调试用：把分钟数写成 HH:MM。非法分钟按 0 处理，不抛。
        public static string Clock(int minuteOfDay)
        {
            var minute = ((minuteOfDay % WorldTime.MinutesPerDay) + WorldTime.MinutesPerDay) % WorldTime.MinutesPerDay;
            return (minute / 60).ToString("00", CultureInfo.InvariantCulture) + ":" +
                (minute % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
