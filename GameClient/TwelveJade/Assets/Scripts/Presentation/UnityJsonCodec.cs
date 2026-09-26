using System;
using TwelveJade.Core;
using UnityEngine;

namespace TwelveJade.Presentation
{
    public sealed class UnityJsonCodec : IJsonCodec
    {
        public string Serialize<T>(T value) => JsonUtility.ToJson(value, true);
        public T Deserialize<T>(string json) where T : class
        {
            try { return JsonUtility.FromJson<T>(json); }
            catch (Exception ex) { throw new ArgumentException("JSON data could not be read.", ex); }
        }
    }
}
