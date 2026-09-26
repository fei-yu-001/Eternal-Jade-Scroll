using System;

namespace TwelveJade.Core
{
    [Serializable]
    public sealed class SaveData
    {
        public int schemaVersion = 1;
        public int slot;
        public string characterId;
        public string characterName;
        public string createdUtc;
        public string updatedUtc;
        public string location = "CharacterPreview";
        public int facing;
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
