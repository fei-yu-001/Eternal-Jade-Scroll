using System;
using System.Collections.Generic;
using System.Linq;

namespace TwelveJade.Core
{
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentSchemaVersion = 2;
        public const int MaxFaceStyles = 2;
        public static readonly string[] Genders = { "male", "female" };

        public int schemaVersion = CurrentSchemaVersion;
        public int slot;
        public string characterId;
        public string characterName;
        public string createdUtc;
        public string updatedUtc;
        public string location = "CharacterPreview";
        public int facing;
        // schemaVersion 2: a person is more than an outfit. Older files load with
        // these defaults applied by the deserializer and are upgraded on next write.
        public string gender = "male";
        public int faceStyle;
        public string[] traits = Array.Empty<string>();
        public string destiny = "";

        public static bool IsValidGender(string value) => Genders.Contains(value);
        public static bool IsValidFaceStyle(int value) => value >= 0 && value < MaxFaceStyles;
    }

    // 随机词条与命运只描述出身与性情，不给数值加成；命运为后续剧情埋的钩子。
    public static class CharacterGen
    {
        public const int TraitsPerCharacter = 2;

        public sealed class Entry
        {
            public Entry(string id, string name, string description)
            { Id = id; Name = name; Description = description; }
            public string Id { get; }
            public string Name { get; }
            public string Description { get; }
        }

        public static readonly IReadOnlyList<Entry> Traits =
            new[]
            {
                new Entry("clever", "早慧", "听老人讲过的古，记得比同龄人都牢。"),
                new Entry("accent", "乡音重", "一开口就是本乡土话，到了外地总要被笑。"),
                new Entry("darkward", "不怕黑", "田窖山路摸得惯，夜里也走得稳。"),
                new Entry("deft", "手巧", "竹篾草绳到了手里，总能编出个物件。"),
                new Entry("stubborn", "认死理", "认定的事，十头牛也拉不回。"),
                new Entry("curious", "爱打听", "渡口茶棚里的闲话，一件都落不下。"),
                new Entry("slow", "慢性子", "天塌下来，也要先把这碗饭吃完。"),
                new Entry("soft", "心软", "见不得乞儿挨饿，自己饿着也想分一口。"),
                new Entry("hardy", "好脾胃", "粗粮野菜都吃得香，底子结实。"),
                new Entry("cough", "夜咳", "阴雨天喉咙发痒，是幼年落下的病根。"),
                new Entry("fearwater", "怕水", "幼年落过水，从此见了深潭就绕道。"),
                new Entry("grudge", "记仇", "吃过的亏都记在心里，不报不快。"),
                new Entry("pride", "好面子", "宁可饿肚子，也不肯穿补丁衣裳出门。"),
                new Entry("lostway", "路痴", "出了村就认不得路，靠一张嘴问着走。"),
                new Entry("readface", "会看脸色", "市井里长大，察言观色是本能。"),
                new Entry("lightdrink", "酒量浅", "一口薄酒就上脸，躲不过席面。"),
                new Entry("literate", "识字", "跟村塾先生认过几百字，能看懂告示。"),
                new Entry("strong", "力气大", "一担水一口气挑到家，面不改色。")
            };

        public static readonly IReadOnlyList<Entry> Destinies =
            new[]
            {
                new Entry("jadeloft", "玉楼引", "梦里常有一座玉楼，醒来枕边似有微香。"),
                new Entry("fallingstar", "灾星过命", "生那夜恰逢流星坠野，村里老人念念不忘。"),
                new Entry("benefactor", "贵人候", "命里必有贵人相助，只是不知在何处。"),
                new Entry("hardfate", "命硬", "算命先生说你能扛兵灾，乱世里反倒活得长。"),
                new Entry("pledge", "守信约", "父辈留下半块玉佩与一句誓言，对面是谁已无人知晓。"),
                new Entry("lonely", "孤命", "六亲缘薄，注定要一个人走很远的路。"),
                new Entry("debtor", "旧债", "家里欠着一笔说不清的旧债，讨债的人迟早会来。"),
                new Entry("scholar", "文曲偏照", "见字不忘，先生说不去赶考可惜了这颗脑袋。"),
                new Entry("away", "离乡命", "故土留不住你，你的路在远方。"),
                new Entry("unreadable", "无名", "命格古怪，寻常卜算一概算不出你。")
            };

        public static Entry FindTrait(string id) => Traits.FirstOrDefault(x => x.Id == id);
        public static Entry FindDestiny(string id) => Destinies.FirstOrDefault(x => x.Id == id);

        public static string[] RollTraits(Random rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            return Traits.OrderBy(_ => rng.Next()).Take(TraitsPerCharacter)
                .Select(x => x.Id).ToArray();
        }

        public static string RollDestiny(Random rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            return Destinies[rng.Next(Destinies.Count)].Id;
        }
    }

    [Serializable]
    public sealed class UserSettings
    {
        public int schemaVersion = 1;
        public float masterVolume = 0.8f;
        public float musicVolume = 0.5f;
        public float effectsVolume = 0.7f;
        public int width = 1600;
        public int height = 900;
        public bool fullscreen;
        public bool reduceMotion;

        public void Normalize()
        {
            masterVolume = Clamp(masterVolume);
            musicVolume = Clamp(musicVolume);
            effectsVolume = Clamp(effectsVolume);
            if (width < 960 || width > 7680 || height < 540 || height > 4320)
            {
                width = 1600;
                height = 900;
            }
        }

        static float Clamp(float value) => float.IsNaN(value) || float.IsInfinity(value)
            ? 0.5f : Math.Max(0, Math.Min(1, value));
    }

    public enum SlotState { Empty, Ready, Recovered, Corrupt, FutureVersion }

    public enum SettingsLoadState { Default, Ready, Recovered, Reset, FutureVersion }

    public sealed class SlotInfo
    {
        public int Slot { get; }
        public SlotState State { get; }
        public SaveData Data { get; }
        public bool CanLoad => State == SlotState.Ready || State == SlotState.Recovered;

        public SlotInfo(int slot, SlotState state, SaveData data = null)
        { Slot = slot; State = state; Data = data; }
    }

    public interface IJsonCodec
    {
        string Serialize<T>(T value);
        T Deserialize<T>(string json) where T : class;
    }
}
